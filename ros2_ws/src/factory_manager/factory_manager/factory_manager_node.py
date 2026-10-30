"""factory_manager — 공정 FSM과 라인 명령권. 담당 B · scope `manager`

SRS v3.1 기준 뼈대. 판단 규칙은 순수 Python 모듈(fsm, grading, line_rules, pallet_queue,
agv_dispatch, watchdog, stats)에 두고, 이 노드는 토픽을 연결하고 장비 명령을 step() 한 곳에서 정한다 (부록 A.3).
지금 동작하는 것: E_STOP으로 시작 → [해제] → IDLE → [시작] → RUNNING → 컨베이어 가동, 비상정지 → 정지.
나머지 콜백은 받은 값만 저장하고 TODO(FR-xx)로 남겨 둔다.

실행: ros2 run factory_manager factory_manager_node
"""

import math
import os

import rclpy
import yaml
from ament_index_python.packages import PackageNotFoundError, get_package_share_directory
from factory_interfaces.srv import PickPlace
from geometry_msgs.msg import Pose
from rclpy.node import Node
from std_msgs.msg import Bool, Int32, String

from .agv_dispatch import AgvDispatcher
from .fsm import E_STOP, Fsm, IDLE, RUNNING
from .grading import Grader, diverter_cmd, parse_weight
from .line_rules import FeedRule
from .pallet_queue import PalletBook
from .stats import Stats
from .watchdog import Watchdog


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

        # ── 파라미터 (dt_bringup/config/factory.yaml, SRS v3.1 부록 A.5) ──
        def param(name, default):
            self.declare_parameter(name, default)
            return self.get_parameter(name).value

        period = param('state_publish_period_sec', 1.0)
        self.heartbeat_period_sec = param('heartbeat_period_sec', 1.0)
        self.heartbeat_timeout_sec = param('heartbeat_timeout_sec', 3.0)
        self.pick_timeout_sec = param('pick_timeout_sec', 20.0)

        # ── 판단 규칙 (순수 Python) ──
        self.fsm = Fsm()
        self.grader = Grader(param('normal_weight_min_kg', 0.90), param('normal_weight_max_kg', 1.10))
        self.feed_rule = FeedRule(param('lane_capacity_count', 3), param('load_timeout_sec', 3.0))
        self.pallets = PalletBook(param('pallet_capacity', 5))
        self.agv = AgvDispatcher()
        self.watchdog = Watchdog(param('weight_report_timeout_sec', 15.0), param('agv_timeout_sec', 30.0))
        self.stats = Stats()

        self.conveyor_on = None  # 마지막으로 보낸 컨베이어 명령. None = 아직 안 보냄
        self.bin_count = 0       # 적재함 남은 방석 수 (/bin/count)
        self.graded = set()# 이미 판정한 방석

        # ── 발행 (SRS v3.1 4.4) ──
        self.state_pub = self.create_publisher(String, '/factory/state', 10)
        self.stats_pub = self.create_publisher(String, '/factory/stats', 10)
        self.heartbeat_pub = self.create_publisher(Bool, '/factory/heartbeat_ros', 10)
        self.retry_pub = self.create_publisher(String, '/factory/retry', 10)
        self.conveyor_pub = self.create_publisher(Bool, '/conveyor/cmd', 10)
        self.diverter_pub = self.create_publisher(String, '/diverter_1_cmd', 10)
        self.count_pubs = {n: self.create_publisher(Int32, f'/pallet_{n}_count', 10) for n in (1, 2)}
        self.agv_cmd_pub = self.create_publisher(String, '/agv_1_cmd', 10)

        # ── 구독: 제어 패널 ──
        self.create_subscription(Bool, '/factory/start', self.on_start, 10)
        # true = 비상정지, false = 해제 (SRS 4.4)
        self.create_subscription(Bool, '/factory/emergency_stop', self.on_estop, 10)
        self.create_subscription(String, '/factory/fault_clear', self.on_fault_clear, 10)
        self.create_subscription(String, '/factory/inject_fault', self.on_inject_fault, 10)
        self.create_subscription(Bool, '/factory/heartbeat_unity', self.on_heartbeat_unity, 10)
        # ── 구독: 장비 보고 ──
        self.create_subscription(Int32, '/bin/count', self.on_bin_count, 10)
        self.create_subscription(String, '/scale_1_weight', self.on_weight, 10)
        self.create_subscription(String, '/conveyor_1_load', self.on_load, 10)
        self.create_subscription(String, '/factory/fault', self.on_fault, 10)
        for n in (1, 2):  # n=n 으로 지금 값을 묶어 둔다
            self.create_subscription(String, f'/pallet_{n}_placed',
                                     lambda msg, n=n: self.on_placed(n, msg), 10)
            self.create_subscription(String, f'/pallet_{n}_status',
                                     lambda msg, n=n: self.on_pallet_status(n, msg), 10)
        self.create_subscription(Bool, '/agv_1_loaded', self.on_agv_loaded, 10)

        # ── 로봇팔1 투입 (팔2·3은 palletizer가 호출) ──
        self.arm1 = self.create_client(PickPlace, '/arm_1_pick_place')
        self.arm1_busy = False  # 요청을 보내고 응답을 기다리는 중
        self.arm1_wait_logged = False  # '서비스 없음' 로그를 한 번만 찍기 위해
        # 라인 좌표표 (D 관리, ROS 좌표)
        self.declare_parameter('line_coords_file', default_line_coords())
        coords = self.get_parameter('line_coords_file').value
        self.poses = load_poses(coords) if coords else {}
        if 'feed_point' not in self.poses:
            self.get_logger().warn('라인 좌표표에 feed_point가 없어 원점을 투입 위치로 씀')

        self.create_timer(period, self.on_tick)
        self.get_logger().info(f'factory_manager 시작: {self.fsm.state} (원인 {self.fsm.estop}), [해제]를 기다림')
        self.publish_state()
        self.step()  # 켜지면 컨베이어 정지부터 보낸다

    # ── 상태 전이 ────────────────────────────────────────────
    def apply(self, event, fn, *args):
        """이벤트로 상태를 바꾸고, 바뀌었으면 로그를 남기고 바로 발행한다 (SRS 6장 이벤트 로그)."""
        before = self.fsm.state
        if fn(*args):
            self.get_logger().info(f'{before} → {self.fsm.state} ({event})')
            self.publish_state()
            if before == E_STOP and self.fsm.state == IDLE:
                self.reset_line()
        self.step()

    def reset_line(self):
        """E_STOP 해제 초기화 (FR-16 6번). 적재함 남은 수만 유지한다."""
        self.feed_rule.reset()
        self.pallets.reset()
        self.agv.reset()
        self.watchdog.reset()
        self.stats.reset()
        self.graded.clear()
        self.arm1_busy = False
        # TODO(FR-16): /pallet_N_count(0) 발행

    def on_start(self, msg):
        if msg.data:
            self.apply('[시작]', self.fsm.on_start)

    def on_estop(self, msg):
        self.apply('비상정지' if msg.data else '비상정지 해제', self.fsm.on_estop, msg.data)

    def on_fault_clear(self, msg):
        """3초 조치 완료 (FR-18). 장비 id: arm_1 ~ arm_3, conveyor."""
        # TODO(FR-18): fsm.on_fault_clear, RUNNING이 되면 집기 실패한 팔만 재전송 (arm_1은 직접, arm_2·3은 /factory/retry)

    def on_inject_fault(self, msg):
        """[고장 주입] (FR-19, Could)."""
        # TODO(FR-19): RUNNING에서만 fsm.on_fault(msg.data). 재전송 없음

    def on_fault(self, msg):
        """palletizer의 집기 실패 보고 {"device": "arm_2", "reason": "pick_failed"} (FR-18)."""
        # TODO(FR-18): JSON 읽고 fsm.on_fault(device)

    def on_heartbeat_unity(self, msg):
        """Unity Heartbeat 수신 (FR-17)."""
        # TODO(FR-17): 받은 시각 기록. on_tick에서 heartbeat_timeout_sec 넘으면 fsm.to_estop(CAUSE_HEARTBEAT)

    # ── 장비 보고 ────────────────────────────────────────────
    def on_bin_count(self, msg):
        """적재함 남은 방석 수 (FR-02, FR-05). 0이면 로봇팔1을 부르지 않는다."""
        self.bin_count = msg.data
        self.step()

    def on_weight(self, msg):
        """/scale_1_weight {"id", "kg"} (FR-06, 07, 08)."""
        # TODO(FR-07): grading.parse_weight → grader.grade → stats 정상·불량 수 +1, 이벤트 로그
        if self.fsm.state == E_STOP:
            return
        cushion, kg = parse_weight(msg.data)
        if cushion is None:
            self.get_logger().warn(f'방석 id가 없는 무게 보고라 분기 명령을 보낼 수 없음: {msg.data}')
            return
        if cushion in self.graded:
            self.get_logger().warn(f'{cushion} 무게 보고를 다시 받아 무시 (방석당 1회)')
            return
        self.graded.add(cushion)

        lane = self.grader.grade(kg)
        if kg is None:
            self.get_logger().warn(f'{cushion} 무게를 읽을 수 없어 불량 처리: {msg.data}')
        self.stats.on_graded(cushion, kg, lane)
        self.diverter_pub.publish(String(data=diverter_cmd(cushion, lane)))   # 판정 직후 바로 (NFR-01)
        self.get_logger().info(f'판정 {cushion} {kg} kg → {lane}')
        # TODO(FR-08): 바로 /diverter_1_cmd {"id", "lane"} 방석당 1회 (NFR-01: 0.5초 이내)
        # TODO(FR-02): feed_rule.on_weight, watchdog.stop_weight

    def on_load(self, msg):
        """/conveyor_1_load {"main", "ok", "ng"} (FR-04)."""
        # TODO(FR-04): JSON 읽고 feed_rule.on_load(load, now) 후 step()

    def on_placed(self, pallet, msg):
        """/pallet_N_placed 방석 id (FR-12)."""
        # TODO(FR-12): pallets.on_placed(pallet), /pallet_N_count 발행, 대기열에 들어갔으면 출동 검사

    def on_pallet_status(self, pallet, msg):
        """/pallet_N_status READY·OUT·REFILLING (FR-12, 15)."""
        # TODO(FR-12, 15): pallets.on_status, READY로 바뀌어 0이 되면 /pallet_N_count(0), 출동 검사

    def on_agv_loaded(self, msg):
        """/agv_1_loaded 적재 여부 (FR-13)."""
        # TODO(FR-13): agv.on_loaded → FINAL·다음 PN_DOCK·HOME 발행, watchdog.start_agv / stop_agv

    # ── 발행·명령 ────────────────────────────────────────────
    def publish_state(self):
        self.state_pub.publish(String(data=self.fsm.state))

    def step(self):
        """장비 명령은 여기서만 정한다."""
        run = self.fsm.state == RUNNING
        self.set_conveyor(run)
        # TODO(FR-02, 04): run and feed_rule.can_feed(bin_count, arm1_busy, now)이면 self.feed()
        # TODO(FR-13): run이면 agv.try_dispatch(run, pallets.front_ready())

    def on_tick(self):
        """1초마다 상태를 다시 보내고 조건을 다시 본다."""
        self.publish_state()
        # TODO(FR-17): /factory/heartbeat_ros 발행, Unity Heartbeat 끊김 검사
        # TODO(FR-02, 13): RUNNING이면 watchdog.expired(now) → fsm.to_estop(원인)
        # TODO(FR-20): /factory/stats 발행 (stats.to_json)
        self.step()

    def feed(self):
        """로봇팔1에 투입 요청 (FR-02). 서비스가 아직 없으면 기다린다."""
        if not self.arm1.service_is_ready():
            if not self.arm1_wait_logged:
                self.get_logger().warn('/arm_1_pick_place 서비스 대기 중 (Unity 로봇팔1 또는 fake 노드 없음)')
                self.arm1_wait_logged = True
            return
        self.arm1_wait_logged = False
        req = PickPlace.Request()  # box_id는 비워 보낸다. Unity가 집은 방석 id를 message로 돌려준다
        req.place_pose = self.poses.get('feed_point', Pose())
        self.arm1_busy = True
        self.get_logger().info('로봇팔1 투입 요청')
        self.arm1.call_async(req).add_done_callback(self.on_arm1_done)

    def on_arm1_done(self, future):
        """로봇팔1 응답. 다음 투입은 무게 보고와 방석 수를 보고 다시 판단한다."""
        self.arm1_busy = False
        res = future.result()
        if res.success:
            self.get_logger().info(f'로봇팔1 투입 완료 {res.message}')
            # TODO(FR-02): stats.fed +1, feed_rule.on_fed(res.message), watchdog.start_weight(now)
        elif res.message == 'E_STOP':
            self.get_logger().info('로봇팔1 E_STOP으로 중단 (고장 아님)')
        else:
            self.get_logger().warn(f'로봇팔1 투입 실패 {res.message}')
            # TODO(FR-18): fsm.on_fault('arm_1'), 요청을 기억했다가 조치 후 한 번 재전송
        # TODO(FR-18): pick_timeout_sec 안에 응답이 없으면 실패로 처리

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
