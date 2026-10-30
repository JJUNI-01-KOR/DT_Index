"""fake_unity — Unity 없이 ROS2 노드를 시험하는 가짜 Unity. 담당 D · scope `fake`

SRS v3.1 기준 뼈대. Unity가 내는 토픽·서비스를 흉내 낸다. 장면 상태 계산은 fake_scene.py(순수 Python).
⚠ Unity를 Play하는 동안에는 띄우지 않는다 (같은 토픽·서비스를 두 곳에서 내게 됨).

실행
    ros2 run dt_fake_unity fake_unity_node
    ros2 run dt_fake_unity fake_unity_node --ros-args -p fail_arm_id:=2 -p cushion_sequence:="N,D"
    ros2 launch dt_bringup fake.launch.py          # fake_scenario.yaml 값으로

지금 동작하는 것
    /factory/heartbeat_unity  Bool    heartbeat_period_sec마다 true (FR-17)
    /bin/count                Int32   시작 1초 뒤 1번 + 바뀔 때
    /arm_1_pick_place ~ /arm_3_pick_place  PickPlace 서버
        pick_place_sec 뒤 success=true (fail_arm_id 팔만 false), /factory/state가 E_STOP이면 message=E_STOP
        팔1: 적재함 → 투입 위치, message = 집은 방석 id (C001 …)
뼈대만 있는 것 (TODO)
    발행: /scale_1_weight, /conveyor_1_load, /conveyor_1_end_ok, /conveyor_1_end_ng,
          /pallet_1_status, /pallet_2_status, /agv_1_status
    구독: /conveyor/cmd, /diverter_1_cmd, /agv_1_goal (JSON), /factory/state
"""
import threading
import time

import rclpy
from rclpy.callback_groups import ReentrantCallbackGroup
from rclpy.executors import MultiThreadedExecutor
from rclpy.node import Node
from std_msgs.msg import Bool, Int32, String

from factory_interfaces.srv import PickPlace

from dt_fake_unity.fake_scene import FakeScene

TICK_SEC = 0.1            # 컨베이어·AGV 진행 계산 주기
INITIAL_DELAY_SEC = 1.0   # 다른 노드가 구독을 붙일 시간을 주고 초기값 발행
ARM_COUNT = 3             # 로봇팔 3대 (SRS v3.1)


class FakeUnity(Node):

    def __init__(self):
        super().__init__('fake_unity')
        self.cb_service = ReentrantCallbackGroup()   # 서비스가 sleep 해도 다른 콜백이 돌게
        self.lock = threading.Lock()

        # 파라미터 (dt_bringup/config/fake_scenario.yaml). 실수 값은 yaml에도 2.0처럼 소수점으로 쓴다
        self.declare_parameter('cushion_sequence', 'N,D')  # N=정상, D=불량. 팔1이 꺼내는 순서대로 반복
        self.declare_parameter('pick_place_sec', 1.0)      # 로봇팔 한 번 동작 시간
        self.declare_parameter('fail_arm_id', 0)           # 0이면 실패 없음, 1~3이면 그 팔이 실패 응답
        self.declare_parameter('line_travel_sec', 2.0)     # 투입 → 갈림길 (컨베이어 가동 중일 때만 흐름)
        self.declare_parameter('agv_travel_sec', 8.0)      # AGV1 웨이포인트 한 구간
        self.declare_parameter('bin_capacity', 20)         # 적재함 방석 수
        self.declare_parameter('heartbeat_period_sec', 1.0)

        def param(name):
            return self.get_parameter(name).value

        self.pick_sec = float(param('pick_place_sec'))
        self.fail_arm = int(param('fail_arm_id'))
        self.line_sec = float(param('line_travel_sec'))
        self.agv_sec = float(param('agv_travel_sec'))
        self.scene = FakeScene(param('cushion_sequence'), param('bin_capacity'))

        self.conveyor_on = False
        self.factory_state = ''
        self.sent_bin = None
        self.started = time.monotonic()
        self.initial_sent = False

        # ── 발행 (Unity → ROS2) ──
        self.pub_hb = self.create_publisher(Bool, '/factory/heartbeat_unity', 10)
        self.pub_bin = self.create_publisher(Int32, '/bin/count', 10)
        self.pub_weight = self.create_publisher(String, '/scale_1_weight', 10)
        self.pub_load = self.create_publisher(String, '/conveyor_1_load', 10)
        self.pub_end = {lane: self.create_publisher(String, f'/conveyor_1_end_{lane}', 10)
                        for lane in ('ok', 'ng')}
        self.pub_pallet = {n: self.create_publisher(String, f'/pallet_{n}_status', 10) for n in (1, 2)}
        self.pub_agv = self.create_publisher(String, '/agv_1_status', 10)

        # ── 구독 (ROS2 → Unity) ──
        self.create_subscription(Bool, '/conveyor/cmd', self.on_conveyor, 10)
        self.create_subscription(String, '/diverter_1_cmd', self.on_diverter, 10)
        self.create_subscription(String, '/agv_1_goal', self.on_agv_goal, 10)
        self.create_subscription(String, '/factory/state', self.on_state, 10)

        # ── 서비스: 로봇팔 1~3 ──
        for arm in range(1, ARM_COUNT + 1):   # arm=arm 으로 지금 값을 묶어 둔다
            self.create_service(
                PickPlace, f'/arm_{arm}_pick_place',
                lambda req, res, arm=arm: self.on_pick(arm, req, res),
                callback_group=self.cb_service)

        self.create_timer(float(param('heartbeat_period_sec')),
                          lambda: self.pub_hb.publish(Bool(data=True)))
        self.create_timer(TICK_SEC, self.on_tick)

        self.get_logger().info(
            f'fake_unity 시작: cushion_sequence={",".join(self.scene.seq)} '
            f'적재함={self.scene.bin_capacity} 팔 동작={self.pick_sec}s fail_arm_id={self.fail_arm}')

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
            # TODO(FR-16): E_STOP → IDLE이면 scene.reset() (해제 초기화)
        self.factory_state = msg.data

    def on_diverter(self, msg):
        """/diverter_1_cmd {"id", "lane"} (FR-08)."""
        # TODO(FR-08): scene.on_diverter

    def on_agv_goal(self, msg):
        """/agv_1_goal {"wp": ...} 또는 {"hitch": "attach" | "detach"} (FR-13)."""
        # TODO(FR-13): scene.on_agv_goal → agv_travel_sec 뒤 /agv_1_status

    # ── 서비스: 로봇팔 1~3 ──────────────────────────────────
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

        cushion = req.box_id
        with self.lock:
            if arm == 1:
                cushion, _, warns = self.scene.pick_from_bin()
            else:
                warns = []
                # TODO(FR-10, 11): scene.pick_from_lane('ok' if arm == 2 else 'ng', req.box_id)
        for w in warns:
            self.get_logger().warn(f'arm_{arm}: {w}')
        self.publish_bin()

        res.success = True
        res.message = cushion
        self.get_logger().info(f'arm_{arm} 완료 success=True message={cushion or "(없음)"}')
        return res

    # ── 0.1초마다: 초기값, 라인 흐름, AGV 이동 ───────────────
    def on_tick(self):
        now = time.monotonic()
        if not self.initial_sent and now - self.started >= INITIAL_DELAY_SEC:
            self.initial_sent = True
            self.publish_bin(force=True)
            self.get_logger().info(f'초기값 발행: /bin/count={self.scene.bin}')
            # TODO(FR-15): /pallet_N_status READY 초기값, 이후 바뀔 때 + 1초마다
            # TODO(FR-04): /conveyor_1_load 초기값, 이후 바뀔 때 + 1초마다
        # TODO(FR-06, 08, 09): 컨베이어 가동 중이면 방석을 line_travel_sec에 걸쳐 센서 → 갈림길 → 레인 끝으로
        # TODO(FR-13): AGV 이동 진행

    # ── 발행 도우미 ─────────────────────────────────────────
    def publish_bin(self, force=False):
        """/bin/count를 바뀐 경우에만 발행 (force면 항상)."""
        with self.lock:
            count = self.scene.bin
            sent, self.sent_bin = self.sent_bin, count
        if force or count != sent:
            self.pub_bin.publish(Int32(data=count))


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
