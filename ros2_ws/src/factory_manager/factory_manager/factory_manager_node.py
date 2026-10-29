"""factory_manager — 공정 FSM과 라인 명령권. 담당 B · scope `manager`

상태 규칙은 fsm.py에 두고, 이 노드는 토픽을 연결하고 장비 명령을 step() 한 곳에서 정한다 (부록 A.3).

실행: ros2 run factory_manager factory_manager_node
"""
import rclpy
from rclpy.node import Node
from std_msgs.msg import Bool, Int32, String

from .fsm import Fsm, RUNNING


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
        self.create_timer(period, self.publish_state)

        # 라인 위 박스 (FR-04)
        self.declare_parameter('feed_stop_count', 4)
        self.declare_parameter('feed_resume_count', 3)
        self.feed_stop_count = self.get_parameter('feed_stop_count').value
        self.feed_resume_count = self.get_parameter('feed_resume_count').value
        self.box_count = 0  # 라인 위 박스 수
        self.feed_allowed = True  # 로봇팔1 투입 허용 여부
        self.create_subscription(Int32, '/conveyor/box_count', self.on_box_count, 10)

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
        self.apply('비상정지' if msg.data else '비상정지 해제', self.fsm.on_estop, msg.data)

    def on_box_count(self, msg):
        """라인 위 박스 수 (FR-04). 4개 이상이면 투입 중지, 3개 이하면 재개."""
        self.box_count = msg.data
        if self.feed_allowed and self.box_count >= self.feed_stop_count:
            self.feed_allowed = False
            self.get_logger().info(f'라인 위 박스 {self.box_count}개, 투입 중지')
        elif not self.feed_allowed and self.box_count <= self.feed_resume_count:
            self.feed_allowed = True
            self.get_logger().info(f'라인 위 박스 {self.box_count}개, 투입 재개')

    def publish_state(self):
        self.state_pub.publish(String(data=self.fsm.state))

    def step(self):
        """장비 명령은 여기서만 정한다."""
        self.set_conveyor(self.fsm.state == RUNNING)

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
