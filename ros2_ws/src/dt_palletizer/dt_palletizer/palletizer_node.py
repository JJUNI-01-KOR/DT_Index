"""palletizer_node — 레인 끝 방석을 파레트에 쌓는다 (SRS v3.1 FR-10, 11, 18). 담당 D · scope `palletizer`

같은 코드를 lane 파라미터만 바꿔 두 개 띄운다 (dt_bringup/launch/_common.py, config/factory.yaml).
  palletizer_1: lane=ok → /conveyor_1_end_ok, 팔2(/arm_2_pick_place), 파레트1, pick_ok
  palletizer_2: lane=ng → /conveyor_1_end_ng, 팔3(/arm_3_pick_place), 파레트2, pick_ng

구독: 레인 끝 방석 id, /pallet_N_count, /pallet_N_status, /factory/state, /factory/retry
발행: /pallet_N_placed (놓은 방석 id), /factory/fault ({"device":"arm_N","reason":"pick_failed"})
호출: /arm_N_pick_place (Unity ArmPickPlaceServer)
판단은 stacker.py (rclpy 없음, pytest). 이 노드는 토픽을 연결하고 Stacker가 돌려준 일만 한다.

⚠ dt_bringup/scripts/m2_run.py와 같이 돌리지 않는다 (둘 다 팔2·3을 부른다).

실행: ros2 launch dt_bringup factory.launch.py   (palletizer_1·palletizer_2가 같이 뜬다)
      ros2 run dt_palletizer palletizer_node --ros-args -r __node:=palletizer_1 -p lane:=ok
"""
import json
import math
import os

import rclpy
import yaml
from ament_index_python.packages import PackageNotFoundError, get_package_share_directory
from factory_interfaces.srv import PickPlace
from geometry_msgs.msg import Pose
from rclpy.node import Node
from std_msgs.msg import Int32, String

from .stacker import BUSY, DROP, FAULT, HOLD, PLACED, Stacker, lane_config, place_z


def default_line_coords():
    """dt_bringup에 설치된 라인 좌표표 경로. 못 찾으면 빈 문자열."""
    try:
        share = get_package_share_directory('dt_bringup')
    except PackageNotFoundError:
        return ''
    return os.path.join(share, 'config', 'line_coords.yaml')


def make_pose(x, y, z, yaw=0.0):
    p = Pose()
    p.position.x, p.position.y, p.position.z = float(x), float(y), float(z)
    p.orientation.z = math.sin(yaw / 2)
    p.orientation.w = math.cos(yaw / 2)
    return p


class Palletizer(Node):

    def __init__(self):
        super().__init__('palletizer_node')

        # ── 파라미터 (dt_bringup/config/factory.yaml의 palletizer_1·palletizer_2) ──
        def param(name, default):
            self.declare_parameter(name, default)
            return self.get_parameter(name).value

        lane = param('lane', 'ok')
        capacity = int(param('pallet_capacity', 5))
        self.height = float(param('cushion_height_m', 0.1))
        self.timeout = float(param('pick_timeout_sec', 20.0))
        coords_file = param('line_coords_file', default_line_coords())
        try:
            with open(coords_file, encoding='utf-8') as f:
                coords = yaml.safe_load(f) or {}
            self.cfg = lane_config(lane, coords)
        except (OSError, ValueError, KeyError, TypeError) as e:
            self.get_logger().fatal(f'설정을 읽지 못해 종료: {e} (line_coords_file={coords_file!r}, lane={lane!r})')
            raise

        arm, n = self.cfg['arm'], self.cfg['pallet']
        self.device = f'arm_{arm}'
        self.pallet = n
        self.st = Stacker(capacity)
        px, py, pz, pyaw = self.cfg['pick']
        self.pick_pose = make_pose(px, py, pz, pyaw)

        self.placed_pub = self.create_publisher(String, f'/pallet_{n}_placed', 10)
        self.fault_pub = self.create_publisher(String, '/factory/fault', 10)
        self.arm = self.create_client(PickPlace, f'/arm_{arm}_pick_place')
        self.create_subscription(String, self.cfg['topic'], self.on_arrived, 10)
        self.create_subscription(Int32, f'/pallet_{n}_count', self.on_count, 10)
        self.create_subscription(String, f'/pallet_{n}_status', self.on_status, 10)
        self.create_subscription(String, '/factory/state', self.on_state, 10)
        self.create_subscription(String, '/factory/retry', self.on_retry, 10)
        self.create_timer(0.2, self.tick)

        self.last_block = None      # 마지막으로 남긴 "못 부르는 이유" (바뀔 때만 로그)
        self.wait_logged = False    # 팔 서비스 대기 로그를 남겼는지
        self.get_logger().info(
            f'{self.get_name()} 시작: lane={lane}, {self.cfg["topic"]} → 팔{arm} → 파레트{n}, '
            f'적재 기준 {capacity}개, 방석 높이 {self.height} m, 응답 제한 {self.timeout:.0f}초')

    # ── 받은 값 ──────────────────────────────────────────────
    def on_arrived(self, msg):
        """레인 끝 1번 칸에 방석 도착 (FR-09)."""
        if self.st.on_arrived(msg.data):
            self.get_logger().info(f'도착 {msg.data} (대기 {len(self.st.queue)}개)')
        else:
            self.get_logger().info(f'이미 아는 방석 {msg.data}, 무시')
        self.tick()

    def on_count(self, msg):
        """/pallet_N_count. factory_manager가 센 적재 수 (FR-12)."""
        before = self.st.count
        if self.st.on_count(msg.data):
            self.get_logger().info(f'파레트{self.pallet} 적재 수 {msg.data}개 확인')
        elif before is not None and before != msg.data:
            self.get_logger().info(f'파레트{self.pallet} 적재 수 {before} → {msg.data}')
        self.tick()

    def on_status(self, msg):
        """/pallet_N_status READY · OUT · REFILLING (FR-15)."""
        if msg.data != self.st.status:
            self.get_logger().info(f'파레트{self.pallet} {self.st.status or "-"} → {msg.data}')
        self.st.on_status(msg.data)
        self.tick()

    def on_state(self, msg):
        """/factory/state. E_STOP → IDLE이면 해제 초기화 (FR-16)."""
        if self.st.on_state(msg.data):
            self.last_block = None
            self.get_logger().info('해제 초기화: 기억한 방석, 들고 있던 요청, retry, 적재 수 대기를 비움')
        self.tick()

    def on_retry(self, msg):
        """/factory/retry. 조치 완료 뒤 factory_manager가 다시 시도할 팔을 보낸다 (FR-18)."""
        if msg.data != self.device:
            return
        if self.st.on_retry():
            self.get_logger().info(f'retry 받음: {self.st.held.cid}을(를) RUNNING일 때 한 번 다시 보냄')
        else:
            self.get_logger().info('retry 받았지만 들고 있는 요청이 없어 무시')
        self.tick()

    # ── 주기 검사와 호출 ─────────────────────────────────────
    def now(self):
        return self.get_clock().now().nanoseconds / 1e9

    def tick(self):
        now = self.now()
        lost = self.st.check_timeout(now, self.timeout)
        if lost is not None:
            self.get_logger().warn(f'{self.device} 응답 없음 {self.timeout:.0f}초: {lost.cid}을(를) 들고 /factory/fault 보냄')
            self.send_fault('no_response')
        waited = self.st.wait_warning(now)
        if waited is not None:
            self.get_logger().warn(
                f'/pallet_{self.pallet}_count가 {self.st.wait_level}에서 {waited:.0f}초째 안 바뀜 '
                '(factory_manager가 placed를 받았는지 확인)')

        if not self.arm.service_is_ready():
            if (self.st.queue or self.st.held) and not self.wait_logged:
                self.get_logger().warn(f'/arm_{self.cfg["arm"]}_pick_place 서비스 대기 중 (Unity Play 확인)')
                self.wait_logged = True
            return
        self.wait_logged = False

        call = self.st.next_call(now)
        if call is None:
            if not (self.st.queue or self.st.held):     # 부를 방석이 없으면 이유를 남기지 않는다
                self.last_block = None
                return
            reason = self.st.blocked(now)
            if reason != self.last_block:
                self.get_logger().info(f'대기: {reason}')
                self.last_block = reason
            return
        self.last_block = None
        self.send(call)

    def send(self, call):
        x, y, top, _ = self.cfg['pallet_pose']
        z = place_z(top, call.level, self.height)
        req = PickPlace.Request()
        req.box_id = call.cid
        req.pick_pose = self.pick_pose
        req.place_pose = make_pose(x, y, z)     # 방향 w=1.0
        self.get_logger().info(
            f'{self.device} 호출: {call.cid} → 파레트{self.pallet} {call.level + 1}단 (z {z:.2f})')
        future = self.arm.call_async(req)
        future.add_done_callback(lambda f, seq=call.seq: self.on_done(seq, f))

    def on_done(self, seq, future):
        try:
            res = future.result()
            success, message = bool(res.success), res.message
        except Exception as e:  # noqa: BLE001  서비스 호출 자체가 실패한 경우
            success, message = False, f'exception:{e}'
        kind, cid = self.st.on_response(seq, success, message, self.now())
        if kind == PLACED:
            self.placed_pub.publish(String(data=cid))
            self.get_logger().info(
                f'{self.device} 성공 {cid} → /pallet_{self.pallet}_placed, 적재 수가 바뀌면 다음 방석')
        elif kind == HOLD:
            self.get_logger().info(f'{self.device} E_STOP으로 중단 {cid} (고장 아님, 해제 때 비움)')
        elif kind == BUSY:
            self.get_logger().info(f'{self.device} busy, {self.st.busy_retry_sec:.0f}초 뒤 {cid} 다시 보냄')
        elif kind == DROP:
            self.get_logger().warn(f'{self.device} no_cushion: 집는 자리에 {cid}이(가) 없어 버림')
        elif kind == FAULT:
            self.get_logger().warn(f'{self.device} 실패 {message}: {cid}을(를) 들고 /factory/fault 보냄')
            self.send_fault(message)
        else:
            self.get_logger().info(f'{self.device} 이미 끝낸 요청의 늦은 응답 무시 ({message})')
        self.tick()

    def send_fault(self, detail):
        """집기 실패 보고 (FR-18). factory_manager는 device만 읽는다."""
        data = {'device': self.device, 'reason': 'pick_failed', 'detail': detail}
        self.fault_pub.publish(String(data=json.dumps(data, ensure_ascii=False)))


def main(args=None):
    rclpy.init(args=args)
    node = Palletizer()
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
