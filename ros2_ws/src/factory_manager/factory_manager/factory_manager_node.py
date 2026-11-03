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

from .agv_dispatch import AgvDispatcher, FINAL, HOME
from .fsm import CAUSE_HEARTBEAT, E_STOP, Fsm, IDLE, RUNNING, parse_fault
from .grading import Grader, diverter_cmd, parse_weight
from .line_rules import FeedRule, parse_load
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
        self.grader = Grader(param('normal_weight_min_kg', 0.80), param('normal_weight_max_kg', 1.20))
        self.feed_rule = FeedRule(param('lane_capacity_count', 15), param('load_timeout_sec', 3.0),
                                  param('lane_end_slots', 2))
        self.lane_end_full_logged = False  # '레인 끝 가득' 로그를 바뀔 때만 찍기 위해
        self.pallets = PalletBook(param('pallet_capacity', 5))
        self.agv = AgvDispatcher()
        self.watchdog = Watchdog(param('weight_report_timeout_sec', 15.0), param('agv_timeout_sec', 30.0))
        self.stats = Stats()

        self.conveyor_on = None  # 마지막으로 보낸 컨베이어 명령. None = 아직 안 보냄
        self.bin_count = 0       # 적재함 남은 방석 수 (/bin/count)
        self.graded = set()# 이미 판정한 방석
        self.unity_heard_at = None # Unity Heartbeat를 마지막으로 받은 시각. None = 아직 못 받음

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
        self.arm1_future = None #응답을 기다리는 요청 , 지난 요청의 늦은 응답은 무시 
        self.arm1_sent_at = None # 요청 시각 (pick_timeout_sec 검사)
        self.pick_failed = set() # 조치 후 다시 요청할 팔 
        # 라인 좌표표 (D 관리, ROS 좌표)
        self.declare_parameter('line_coords_file', default_line_coords())
        coords = self.get_parameter('line_coords_file').value
        self.poses = load_poses(coords) if coords else {}
        if 'feed_point' not in self.poses:
            self.get_logger().warn('라인 좌표표에 feed_point가 없어 원점을 투입 위치로 씀')

        self.create_timer(period, self.on_tick)
        self.create_timer(self.heartbeat_period_sec, self.on_heartbeat_tick)
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
            self.publish_stats()
            if before != RUNNING and self.fsm.state == RUNNING:
                self.watchdog.restart(self.now_sec())            
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
        self.arm1_future = None
        self.arm1_sent_at = None
        self.pick_failed.clear()
        for n in (1,2):
            self.publish_count(n)

    def on_start(self, msg):
        if msg.data:
            self.apply('[시작]', self.fsm.on_start)

    def on_estop(self, msg):
        self.apply('비상정지' if msg.data else '비상정지 해제', self.fsm.on_estop, msg.data)

    def on_fault_clear(self, msg):
        """3초 조치 완료 (FR-18). 장비 id: arm_1 ~ arm_3, conveyor."""
        before = self.fsm.state
        self.apply(f'조치 완료 {msg.data}', self.fsm.on_fault_clear, msg.data)
        if before != 'ERROR' or self.fsm.state != RUNNING:
            return
        for device in sorted(self.pick_failed):
            if device != 'arm_1':  # arm_1은 위 apply 안의 step()이 다시 요청한다
                self.retry_pub.publish(String(data=device))
        self.pick_failed.clear()
        

    def on_inject_fault(self, msg):
        """[고장 주입] (FR-19, Could)."""
        # TODO(FR-19): RUNNING에서만 fsm.on_fault(msg.data). 재전송 없음

    def on_fault(self, msg):
        """palletizer의 집기 실패 보고 {"device": "arm_2", "reason": "pick_failed"} (FR-18)."""
        device = parse_fault(msg.data)
        if device is None:
            self.get_logger().warn(f'/factory/fault를 읽을 수 없어 무시: {msg.data}')
            return
        self.report_pick_failure(device)
    
    def report_pick_failure(self, device):
        """집기 실패를 고장 목록에 넣음 ,RUNNING, ERROR 에서 받음 """
        if self.fsm.state not in (RUNNING, 'ERROR'):
            return
        self.pick_failed.add(device)
        self.apply(f'집기 실패 {device}', self.fsm.on_fault, device)

    def on_heartbeat_unity(self, msg):
        """Unity Heartbeat 수신 (FR-17)."""
        # 받은 시각 기록. on_tick에서 heartbeat_timeout_sec 넘으면 fsm.to_estop(CAUSE_HEARTBEAT)
        self.unity_heard_at = self.now_sec()

    # ── 장비 보고 ────────────────────────────────────────────
    def on_bin_count(self, msg):
        """적재함 남은 방석 수 (FR-02, FR-05). 0이면 로봇팔1을 부르지 않는다."""
        self.bin_count = msg.data
        self.step()

    def on_weight(self, msg):
        """/scale_1_weight {"id", "kg"} (FR-06, 07, 08)."""
        # (FR-07): grading.parse_weight → grader.grade → stats 정상·불량 수 +1, 이벤트 로그
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
        # (FR-08): 바로 /diverter_1_cmd {"id", "lane"} 방석당 1회 (NFR-01: 0.5초 이내)
        if self.feed_rule.on_weight(cushion):
            self.watchdog.stop_weight()
        self.step()

    def on_load(self, msg):
        """/conveyor_1_load {"main", "ok", "ng"} (FR-04)."""
        load = parse_load(msg.data)
        if load is None:  # 이전 값은 load_timeout_sec이 지나면 쓰지 않는다
            self.get_logger().warn(f'/conveyor_1_load를 읽을 수 없어 무시: {msg.data}')
            return
        self.feed_rule.on_load(load, self.now_sec())
        full = self.feed_rule.lane_end_full()
        if full != self.lane_end_full_logged:  # 11/02 A: 레인 끝 칸이 다 차면 컨베이어와 팔1 투입을 멈춤
            self.lane_end_full_logged = full
            if full:
                self.get_logger().info(f'레인 끝 칸 가득 (ok {load["ok_end"]}/2, ng {load["ng_end"]}/2) → 컨베이어 정지, 로봇팔1 투입 대기')
            else:
                self.get_logger().info('레인 끝 칸 자리 남 → 컨베이어 가동, 로봇팔1 투입 재개')
        self.step()

    def on_placed(self, pallet, msg):
        """/pallet_N_placed 방석 id (FR-12)."""
        #(FR-12): pallets.on_placed(pallet), /pallet_N_count 발행, 대기열에 들어갔으면 출동 검사
        if self.fsm.state ==E_STOP:
            return
        if self.pallets.is_full(pallet):
            self.get_logger().warn(f'파레트{pallet}이 이미 가득 찼는데 적재 보고를 받아 무시: {msg.data}')
            return
        self.pallets.on_placed(pallet)
        self.publish_count(pallet)
        self.get_logger().info(f'파레트{pallet} 적재 {self.pallets.counts[pallet]}개 ({msg.data})')
        self.step()

    def on_pallet_status(self, pallet, msg):
        """/pallet_N_status READY·OUT·REFILLING (FR-12, 15)."""
        #(FR-12, 15): pallets.on_status, READY로 바뀌어 0이 되면 /pallet_N_count(0), 출동 검사
        if self.pallets.on_status(pallet, msg.data):
            self.publish_count(pallet)
            self.get_logger().info(f'파레트{pallet} 새 파레트 도착, 적재 수 0')
        self.step()

    def on_agv_loaded(self, msg):
        """/agv_1_loaded 적재 여부 (FR-13)."""
        # (FR-13): agv.on_loaded → FINAL·다음 PN_DOCK·HOME 발행, watchdog.start_agv / stop_agv
        if self.fsm.state == E_STOP:
            self.agv.loaded = msg.data
            return
        before = self.agv.loaded
        run = self.fsm.state == RUNNING
        cmd = self.agv.on_loaded(msg.data, run, self.pallets.front_ready(), self.now_sec())
        if msg.data != before:
            self.watchdog.stop_agv()
        if cmd is not None:
            self.send_agv(cmd)
        self.step()


    # ── 발행·명령 ────────────────────────────────────────────
    def publish_state(self):
        self.state_pub.publish(String(data=self.fsm.state))
    
    def publish_stats(self):
        self.stats_pub.publish(String(data=self.stats.to_json(
            self.fsm, self.bin_count, self.feed_rule.load, self.pallets, 
            self.agv, self.agv.last_cycle_sec)))

    def publish_count(self, pallet):
        """/pallet_N_count. 바뀔 때와 1초마다 (FR-12)."""
        self.count_pubs[pallet].publish(Int32(data=self.pallets.counts[pallet]))

    def now_sec(self):
        return self.get_clock().now().nanoseconds / 1e9

    def send_agv(self, cmd):
        """/agv_1_cmd 발행 (FR-13). 도킹 명령이면 대기열에서 빼고, HOME이 아니면 감시 시간을 시작한다."""
        if cmd not in (HOME, FINAL):
            self.pallets.pop_front()
        if cmd != HOME:
            self.watchdog.start_agv(self.now_sec())
        self.agv_cmd_pub.publish(String(data=cmd))
        self.get_logger().info(f'AGV1 명령 {cmd}')

    def step(self):
        """장비 명령은 여기서만 정한다."""
        run = self.fsm.state == RUNNING
        # 11/02 A: 레인 끝 칸(집는 자리+대기)이 다 찬 레인이 있으면 RUNNING이어도 컨베이어를 멈춘다 (상태는 RUNNING 유지).
        #          팔2·3이 집어서 칸이 비면 다시 돈다. 팔1 투입도 FeedRule이 같은 조건으로 막는다
        self.set_conveyor(run and not self.feed_rule.lane_end_full())
        if run and self.feed_rule.can_feed(self.bin_count, self.arm1_busy, self.now_sec()):
            self.feed()
        cmd = self.agv.try_dispatch(run, self.pallets.front_ready(), self.now_sec())
        if cmd is not None:
            self.send_agv(cmd)
    
    def on_heartbeat_tick(self):
        """heartbeat를 보내고 unity 끊김을 검사한다 """
        self.heartbeat_pub.publish(Bool(data=True))
        if self.unity_heard_at is None: # 아직 못 받음
            return
        if self.now_sec() - self.unity_heard_at > self.heartbeat_timeout_sec:
            self.apply('Unity Heartbeat 끊김', self.fsm.to_estop, CAUSE_HEARTBEAT)

    def on_tick(self):
        """1초마다 상태를 다시 보내고 조건을 다시 본다."""
        self.publish_state()
        for n in (1,2):
            self.publish_count(n)
        self.publish_stats()
        self.check_arm1_timeout()
        if self.fsm.state == RUNNING:
            cause = self.watchdog.expired(self.now_sec(), self.conveyor_on is True)
            if cause is not None:
                self.apply('감시 시간 초과', self.fsm.to_estop, cause)
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
        self.arm1_sent_at = self.now_sec()
        self.get_logger().info('로봇팔1 투입 요청')
        self.arm1_future = self.arm1.call_async(req)
        self.arm1_future.add_done_callback(self.on_arm1_done)

    def on_arm1_done(self, future):
        """로봇팔1 응답. 다음 투입은 무게 보고와 방석 수를 보고 다시 판단한다."""
        if future is not self.arm1_future:  # 시간 초과나 해제로 이미 끝낸 요청
            return
        self.arm1_future = None
        self.arm1_sent_at = None
        self.arm1_busy = False
        res = future.result()
        if res.success:
            self.get_logger().info(f'로봇팔1 투입 완료 {res.message}')
            self.stats.fed += 1
            if res.message not in self.graded:
                self.feed_rule.on_fed(res.message)
                self.watchdog.start_weight(self.now_sec())
        elif res.message == 'E_STOP':
            self.get_logger().info('로봇팔1 E_STOP으로 중단 (고장 아님)')
        else:
            self.get_logger().warn(f'로봇팔1 투입 실패 {res.message}')
            self.report_pick_failure('arm_1')
        self.step()
    
    def check_arm1_timeout(self):
        """pick_timeout_sec 안에 응답이 없으면 실패로 처리한다 (FR-18)."""
        if self.arm1_sent_at is None or self.fsm.state == E_STOP:
            return
        if self.now_sec() - self.arm1_sent_at <= self.pick_timeout_sec:
            return
        self.get_logger().warn(f'로봇팔1 응답 없음 {self.pick_timeout_sec}초')
        self.arm1_future = None
        self.arm1_sent_at = None
        self.arm1_busy = False
        self.report_pick_failure('arm_1')

    def set_conveyor(self, on):
        """컨베이어 가동(True)·정지(False). 바뀔 때만 보낸다."""
        if on == self.conveyor_on:
            return
        self.conveyor_on = on
        if on:
            self.watchdog.restart_weight(self.now_sec())
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
