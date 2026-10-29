"""fake_unity — Unity 없이 ROS2 노드를 시험하는 가짜 Unity. 담당 D · scope `fake`

Unity가 내는 토픽·서비스를 그대로 흉내 낸다. 장면 상태 계산은 fake_scene.py(순수 Python).
⚠ Unity를 Play하는 동안에는 띄우지 않는다 (같은 토픽·서비스를 두 곳에서 내게 됨).

실행
    ros2 run dt_fake_unity fake_unity_node
    ros2 run dt_fake_unity fake_unity_node --ros-args -p fail_arm_id:=4 -p box_sequence:="N,R"
    ros2 launch dt_bringup fake.launch.py          # fake_scenario.yaml 값으로

발행 (Unity → ROS2)
    /factory/heartbeat_unity  Bool    heartbeat_period_sec마다 true
    /bin/count                Int32   시작 1초 뒤 1번 + 바뀔 때
    /conveyor/box_count       Int32   시작 1초 뒤 1번 + 바뀔 때
    /agv_1_status             String  시작 1초 뒤 IDLE, 이후 MOVING / ARRIVED / IDLE / LOADED
    /detect_1_event           DetectEvent  reject | reject_wait
    /detect_2_event           DetectEvent  end_1 | end_2 | end_wait
구독 (ROS2 → Unity)
    /conveyor/cmd   Bool         true=가동. 멈춰 있으면 박스도 안 움직임
    /agv_1_goal     PoseStamped  받으면 MOVING → agv_travel_sec 뒤 ARRIVED(하역) 또는 IDLE(상차)
    /factory/state  String       E_STOP이면 로봇팔 응답이 success=false, message=E_STOP
서비스 (Unity가 서버)
    /arm_1_pick_place ~ /arm_4_pick_place  PickPlace
        pick_place_sec 뒤 응답. 항상 success=true (fail_arm_id 팔만 false)
        팔1: 적재함 → 컨베이어, message = 집은 박스 ID (BOX_001 …)
        팔2: 불량 칸 → 불량 팔레트 / 팔3: 대기 칸 → AGV1 (+ /agv_1_status LOADED) / 팔4: AGV1 → 출고 팔레트
"""
import threading
import time

import rclpy
from rclpy.callback_groups import ReentrantCallbackGroup
from rclpy.executors import MultiThreadedExecutor
from rclpy.node import Node
from std_msgs.msg import Bool, Int32, String
from geometry_msgs.msg import PoseStamped

from factory_interfaces.msg import DetectEvent
from factory_interfaces.srv import PickPlace

from dt_fake_unity.fake_scene import FakeScene

TICK_SEC = 0.1            # 컨베이어·AGV 진행 계산 주기
INITIAL_DELAY_SEC = 1.0   # 다른 노드가 구독을 붙일 시간을 주고 초기값 발행


class FakeUnity(Node):

    def __init__(self):
        super().__init__('fake_unity')
        self.cb_service = ReentrantCallbackGroup()   # 서비스가 sleep 해도 다른 콜백이 돌게
        self.lock = threading.Lock()

        # 파라미터 (dt_bringup/config/fake_scenario.yaml). 실수 값은 yaml에도 2.0처럼 소수점으로 쓴다
        self.declare_parameter('box_sequence', 'N,R')     # N=정상, R=규격 초과. 순서대로 반복
        self.declare_parameter('agv_travel_sec', 8.0)     # AGV 편도 시간
        self.declare_parameter('pick_place_sec', 1.0)     # 로봇팔 한 번 동작 시간
        self.declare_parameter('fail_arm_id', 0)          # 0이면 실패 없음, 1~4면 그 팔이 실패 응답
        self.declare_parameter('line_travel_sec', 2.0)    # 컨베이어 투입 → 칸 도착 (가동 중일 때만 흐름)
        self.declare_parameter('bin_capacity', 20)        # 적재함 박스 수
        self.declare_parameter('heartbeat_period_sec', 1.0)

        def param(name):
            return self.get_parameter(name).value

        self.agv_sec = float(param('agv_travel_sec'))
        self.pick_sec = float(param('pick_place_sec'))
        self.fail_arm = int(param('fail_arm_id'))
        self.line_sec = float(param('line_travel_sec'))
        self.scene = FakeScene(param('box_sequence'), param('bin_capacity'))

        self.belt = []              # 컨베이어 위로 흐르는 박스 [box_id, 남은 초]
        self.agv_trip = None        # 이동 중이면 [하역 구역으로 가는지, 남은 초]
        self.conveyor_on = False
        self.factory_state = ''
        self.sent_counts = (None, None)
        self.started = time.monotonic()
        self.last_tick = self.started
        self.initial_sent = False

        self.pub_bin = self.create_publisher(Int32, '/bin/count', 10)
        self.pub_line = self.create_publisher(Int32, '/conveyor/box_count', 10)
        self.pub_det1 = self.create_publisher(DetectEvent, '/detect_1_event', 10)
        self.pub_det2 = self.create_publisher(DetectEvent, '/detect_2_event', 10)
        self.pub_agv = self.create_publisher(String, '/agv_1_status', 10)
        self.pub_hb = self.create_publisher(Bool, '/factory/heartbeat_unity', 10)

        self.create_subscription(Bool, '/conveyor/cmd', self.on_conveyor, 10)
        self.create_subscription(PoseStamped, '/agv_1_goal', self.on_goal, 10)
        self.create_subscription(String, '/factory/state', self.on_state, 10)

        for arm in range(1, 5):   # arm=arm 으로 지금 값을 묶어 둔다
            self.create_service(
                PickPlace, f'/arm_{arm}_pick_place',
                lambda req, res, arm=arm: self.on_pick(arm, req, res),
                callback_group=self.cb_service)

        self.create_timer(float(param('heartbeat_period_sec')),
                          lambda: self.pub_hb.publish(Bool(data=True)))
        self.create_timer(TICK_SEC, self.on_tick)

        self.get_logger().info(
            f'fake_unity 시작: box_sequence={",".join(self.scene.seq)} '
            f'적재함={self.scene.bin_capacity} 라인={self.line_sec}s AGV={self.agv_sec}s '
            f'팔 동작={self.pick_sec}s fail_arm_id={self.fail_arm}')

    # ── 구독 ────────────────────────────────────────────────
    def on_conveyor(self, msg):
        with self.lock:
            changed = msg.data != self.conveyor_on
            self.conveyor_on = msg.data
        if changed:
            self.get_logger().info(f'컨베이어 {"가동" if msg.data else "정지"}')

    def on_state(self, msg):
        if msg.data != self.factory_state:
            self.get_logger().info(f'/factory/state = {msg.data}')
        self.factory_state = msg.data

    def on_goal(self, msg):
        with self.lock:
            if self.agv_trip is not None:
                busy = True
            else:
                busy = False
                to_unload = self.scene.agv_depart()
                self.agv_trip = [to_unload, self.agv_sec]
        if busy:
            self.get_logger().warn('AGV1 이동 중에 /agv_1_goal을 받아 무시')
            return
        where = '출고 팔레트 구역' if to_unload else '상차 구역'
        self.get_logger().info(f'AGV1 출발 → {where} ({self.agv_sec:.1f}s)')
        self.pub_agv.publish(String(data='MOVING'))

    # ── 서비스: 로봇팔 1~4 ──────────────────────────────────
    def on_pick(self, arm, req, res):
        self.get_logger().info(f'arm_{arm} 집기 요청 box_id={req.box_id or "(없음)"}')
        time.sleep(self.pick_sec)          # 동작 시간 흉내 (콜백 그룹 덕분에 heartbeat는 계속 나감)

        if self.factory_state == 'E_STOP':
            res.success = False
            res.message = 'E_STOP'
            self.get_logger().warn(f'arm_{arm}: E_STOP 중이라 동작 중단 (고장 아님)')
            return res
        if arm == self.fail_arm:
            res.success = False
            res.message = 'fake fail'
            self.get_logger().warn(f'arm_{arm}: fail_arm_id라 실패 응답')
            return res

        loaded = False
        with self.lock:
            if arm == 1:
                box, events, warns = self.scene.pick_from_bin()
                self.belt.append([box, self.line_sec])
            elif arm == 2:
                box, events, warns = self.scene.pick_reject(req.box_id)
            elif arm == 3:
                box, events, warns = self.scene.pick_end(req.box_id)
                loaded = bool(box)
            else:
                box, events, warns = self.scene.pick_from_agv(req.box_id)

        for w in warns:
            self.get_logger().warn(f'arm_{arm}: {w}')
        self.emit(events)
        self.publish_counts()
        if loaded:
            self.pub_agv.publish(String(data='LOADED'))
            self.get_logger().info(f'AGV1 상차 완료 {box} → /agv_1_status LOADED')

        res.success = True
        res.message = box
        self.get_logger().info(f'arm_{arm} 완료 success=True message={box or "(빈 칸)"}')
        return res

    # ── 0.1초마다: 초기값, 컨베이어 흐름, AGV 이동 ──────────
    def on_tick(self):
        now = time.monotonic()
        dt = now - self.last_tick
        self.last_tick = now
        events, warns, agv_status, initial = [], [], None, False

        with self.lock:
            if not self.initial_sent and now - self.started >= INITIAL_DELAY_SEC:
                self.initial_sent = initial = True
            if self.conveyor_on and self.belt:
                for item in self.belt:
                    item[1] -= dt
                arrived = [box for box, left in self.belt if left <= 0]
                self.belt = [item for item in self.belt if item[1] > 0]
                for box in arrived:
                    _, ev, w = self.scene.box_arrives(box)
                    events += ev
                    warns += w
            if self.agv_trip is not None:
                self.agv_trip[1] -= dt
                if self.agv_trip[1] <= 0:
                    agv_status = self.scene.agv_arrive(self.agv_trip[0])
                    self.agv_trip = None

        if initial:
            self.publish_counts(force=True)
            self.pub_agv.publish(String(data='IDLE'))
            self.get_logger().info(
                f'초기값 발행: /bin/count={self.scene.bin} /conveyor/box_count=0 /agv_1_status=IDLE')
        for w in warns:
            self.get_logger().warn(w)
        self.emit(events)
        if events:
            self.publish_counts()
        if agv_status:
            self.pub_agv.publish(String(data=agv_status))
            self.get_logger().info(f'AGV1 도착 → /agv_1_status {agv_status}')

    # ── 발행 도우미 ─────────────────────────────────────────
    def emit(self, events):
        for topic, zone, box_id in events:
            msg = DetectEvent()
            msg.box_id = box_id
            msg.zone = zone
            # position: 좌표표(1.6) 확정 전이라 (0, 0, 0)
            (self.pub_det1 if topic == 'detect_1' else self.pub_det2).publish(msg)
            self.get_logger().info(f'/{topic}_event {zone} {box_id}')

    def publish_counts(self, force=False):
        """/bin/count, /conveyor/box_count를 바뀐 것만 발행 (force면 둘 다)."""
        with self.lock:
            counts = (self.scene.bin, self.scene.on_line)
            sent, self.sent_counts = self.sent_counts, counts
        if force or counts[0] != sent[0]:
            self.pub_bin.publish(Int32(data=counts[0]))
        if force or counts[1] != sent[1]:
            self.pub_line.publish(Int32(data=counts[1]))


def main(args=None):
    rclpy.init(args=args)
    node = FakeUnity()
    executor = MultiThreadedExecutor()
    executor.add_node(node)
    try:
        executor.spin()
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == '__main__':
    main()
