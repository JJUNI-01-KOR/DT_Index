"""factory_manager — 공정 FSM과 라인 명령권. 담당 B · scope `manager`

상태 규칙은 fsm.py에 두고, 이 노드는 토픽을 연결하고 장비 명령을 step() 한 곳에서 정한다 (부록 A.3).

실행: ros2 run factory_manager factory_manager_node
"""

import math
import os

import rclpy
import yaml
from ament_index_python.packages import PackageNotFoundError, get_package_share_directory
from factory_interfaces.msg import DetectEvent
from factory_interfaces.srv import PickPlace
from geometry_msgs.msg import Pose
from rclpy.node import Node
from std_msgs.msg import Bool, Int32, String

from .fsm import E_STOP, Fsm, RUNNING
from .line_rules import FeedGate, SlotWait


def default_line_coords():
    """dt_bringup에 설치된 라인 좌표표 경로. 못 찾으면 빈 문자열."""
    try:
        share = get_package_share_directory('dt_bringup')
    except PackageNotFoundError:
        return ''
    return os.path.join(share, 'config', 'line_coords.yaml')


def load_poses(path):
    """라인 좌표표 yaml → {이름: Pose}. yaw는 z축 회전 쿼터니언으로 바꾼다."""
    with open(path, encoding='utf-8') as f:
        data = yaml.safe_load(f) or {}
    poses = {}
    for name, v in data.items():
        if not isinstance(v, dict):  # bin_slots 같은 목록은 건너뜀
            continue
        p = Pose()
        p.position.x, p.position.y, p.position.z = float(v['x']), float(v['y']), float(v['z'])
        p.orientation.z = math.sin(v['yaw'] / 2)
        p.orientation.w = math.cos(v['yaw'] / 2)
        poses[name] = p
    return poses


class FactoryManager(Node):

    def __init__(self):
        super().__init__('factory_manager')
        self.declare_parameter('state_publish_period_sec', 1.0)
        self.declare_parameter('pallet_capacity', 12)  # ⚠ factory.yaml factory_manager 칸에 아직 없음 → D에게 요청
        self.fsm = Fsm(self.get_parameter('pallet_capacity').value)
        self.conveyor_on = None  # 마지막으로 보낸 컨베이어 명령. None = 아직 안 보냄

        self.state_pub = self.create_publisher(String, '/factory/state', 10)
        self.conveyor_pub = self.create_publisher(Bool, '/conveyor/cmd', 10)
        self.create_subscription(Bool, '/factory/start', self.on_start, 10)
        # true = 비상정지, false = 해제 (SRS 4.4)
        self.create_subscription(Bool, '/factory/emergency_stop', self.on_estop, 10)
        period = self.get_parameter('state_publish_period_sec').value
        self.create_timer(period, self.on_tick)

        # 라인 위 박스 (FR-04)
        self.declare_parameter('feed_stop_count', 4)
        self.declare_parameter('feed_resume_count', 3)
        self.feed_stop_count = self.get_parameter('feed_stop_count').value
        self.feed_resume_count = self.get_parameter('feed_resume_count').value
        self.gate = FeedGate(self.feed_stop_count, self.feed_resume_count)
        self.box_count = 0  # 라인 위 박스 수
        self.feed_open = True  # 로봇팔1 투입 허용 여부
        self.create_subscription(Int32, '/conveyor/box_count', self.on_box_count, 10)
        self.bin_count = 0  # 적재함 남은 박스 수
        self.create_subscription(Int32, '/bin/count', self.on_bin_count, 10)
        # 칸 가득 대기 (FR-07, FR-10)
        self.slots = SlotWait()
        self.reject_count = 0  # 불량 수 (FR-07, /factory/stats에서 씀)
        self.create_subscription(DetectEvent, '/detect_1_event', self.on_detect, 10)
        self.create_subscription(DetectEvent, '/detect_2_event', self.on_detect, 10)
        # 로봇팔1 투입
        self.arm1 = self.create_client(PickPlace, '/arm_1_pick_place')
        self.arm1_busy = False  # 요청을 보내고 응답을 기다리는 중
        self.arm1_wait_logged = False  # '서비스 없음' 로그를 한 번만 찍기 위해
        # 라인 좌표표 (D 관리, ROS 좌표)
        self.declare_parameter('line_coords_file', default_line_coords())
        coords = self.get_parameter('line_coords_file').value
        self.poses = load_poses(coords) if coords else {}
        if 'feed_point' not in self.poses:
            self.get_logger().warn('라인 좌표표에 feed_point가 없어 원점을 투입 위치로 씀')

        self.get_logger().info('factory_manager 시작')
        self.step()  # 켜지면 컨베이어 정지부터 보낸다

        # TODO 구독: /pallet_1_reset, /pallet_2_reset, /factory/inject_fault, /factory/fault_clear
        # TODO 구독: /bin/count, /detect_1_event, /detect_2_event, /agv_1_status, /pallet_2_count, /factory/heartbeat_unity
        # TODO 발행: /factory/stats(1초, JSON), /factory/heartbeat_ros(1초), /agv_1_cmd(depart | return)
        # TODO AGV1 (10.28 결정): /agv_1_status LOADED + 상태 RUNNING → /agv_1_cmd depart, /pallet_2_count 증가(출고 팔레트 적재 완료) → /agv_1_cmd return
        # TODO 불량 팔레트 (SRS v2.2): 칸 n은 출고 팔레트와 같은 2x2x3 규칙으로 계산해 /arm_2_pick_place의 place_pose로 보내고, 성공 응답마다 적재 수 +1 (/factory/stats pallet_1)
        # TODO PALLET_FULL (SRS v2.2): 어느 팔레트든 12개면 전환. /pallet_N_reset으로 그 팔레트를 0으로, 다른 팔레트도 12개면 PALLET_FULL 유지
        # TODO 서비스 호출: /arm_1_pick_place ~ /arm_3_pick_place
        # TODO 파라미터: heartbeat_timeout_sec=3.0 (dt_bringup/config/factory.yaml)
        # TODO ⚠ 미정 (SRS v2.1 피드백 5): [조치 완료] /factory/fault_clear 처리 방법 (FR-18)

    def apply(self, event, fn, *args):
        """이벤트로 상태를 바꾸고, 바뀌었으면 로그를 남기고 바로 발행한다 (SRS 6장 이벤트 로그)."""
        before = self.fsm.state
        if fn(*args):
            self.get_logger().info(f'{before} → {self.fsm.state} ({event})')
            self.publish_state()
        self.step()

    def on_start(self, msg):
        if msg.data:
            self.apply('[동작]', self.fsm.on_start)

    def on_estop(self, msg):
        released = not msg.data and self.fsm.state == E_STOP
        self.apply('비상정지' if msg.data else '비상정지 해제', self.fsm.on_estop, msg.data)
        if released:  # FR-16 6단계: Unity가 라인과 칸의 박스를 치우므로 칸 상태와 불량 수를 0으로
            self.slots.reset()
            self.reject_count = 0

    def on_box_count(self, msg):
        """라인 위 박스 수 (FR-04). 4개 이상이면 투입 중지, 3개 이하면 재개."""
        self.box_count = msg.data
        feed_open = self.gate.update(msg.data)
        if feed_open != self.feed_open:
            self.feed_open = feed_open
            self.get_logger().info(f'라인 위 박스 {self.box_count}개, 투입 {"재개" if feed_open else "중지"}')
        self.step()

    def on_bin_count(self, msg):
        """적재함 남은 박스 수 (FR-02, FR-05). 0이면 로봇팔1을 부르지 않는다."""
        self.bin_count = msg.data
        self.step()

    def on_detect(self, msg):
        """칸 감지 이벤트 (FR-07, FR-09, FR-10). 칸이 차서 기다리면 정지, 칸에 들어가면 재가동."""
        if not self.slots.update(msg.zone):
            self.get_logger().warn(f'모르는 zone {msg.zone} ({msg.box_id})')
            return
        if msg.zone == 'reject':
            self.reject_count += 1
        waiting = ', '.join(sorted(self.slots.waiting)) or '없음'
        self.get_logger().info(f'{msg.zone} {msg.box_id}, 대기: {waiting}')
        self.step()

    def publish_state(self):
        self.state_pub.publish(String(data=self.fsm.state))

    def step(self):
        """장비 명령은 여기서만 정한다."""
        run = self.fsm.state == RUNNING
        self.set_conveyor(run and not self.slots.blocked())
        if run and self.bin_count > 0 and self.feed_open and self.conveyor_on and not self.arm1_busy:
            self.feed()

    def on_tick(self):
        """1초마다 상태를 다시 보내고 조건을 다시 본다."""
        self.publish_state()
        self.step()

    def feed(self):
        """로봇팔1에 투입 요청 (FR-02). 서비스가 아직 없으면 기다린다."""
        if not self.arm1.service_is_ready():
            if not self.arm1_wait_logged:
                self.get_logger().warn('/arm_1_pick_place 서비스 대기 중 (로봇팔1 또는 fake 노드 없음)')
                self.arm1_wait_logged = True
            return
        self.arm1_wait_logged = False
        req = PickPlace.Request()
        req.place_pose = self.poses.get('feed_point', Pose())
        self.arm1_busy = True
        self.get_logger().info('로봇팔1 투입 요청')
        self.arm1.call_async(req).add_done_callback(self.on_arm1_done)

    def on_arm1_done(self, future):
        """로봇팔1 응답. 다음 투입은 박스 수가 바뀌거나 1초 타이머에서 다시 판단한다."""
        self.arm1_busy = False
        res = future.result()
        if res.success:
            self.get_logger().info(f'로봇팔1 투입 완료 {res.message}')
        else:
            self.get_logger().warn(f'로봇팔1 투입 실패 {res.message}')  # 집기 실패 → ERROR는 5.7

    def set_conveyor(self, on):
        """컨베이어 가동(True)·정지(False). 바뀔 때만 보낸다."""
        if on == self.conveyor_on:
            return
        self.conveyor_on = on
        self.conveyor_pub.publish(Bool(data=on))
        self.get_logger().info(f'컨베이어 {"가동" if on else "정지"}')


def main(args=None):
    rclpy.init(args=args)
    node = FactoryManager()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == '__main__':
    main()
