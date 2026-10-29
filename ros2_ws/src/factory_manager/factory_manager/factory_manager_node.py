"""factory_manager — 뼈대 (chore #1). 담당 B · scope `manager`

실행: ros2 run factory_manager factory_manager_node
"""
import rclpy
from rclpy.node import Node
from std_msgs.msg import String, Bool

# 공정 상태
IDLE = 'IDLE'
RUNNING = 'RUNNING'
PALLET_FULL = 'PALLET_FULL'
ERROR = 'ERROR'
E_STOP = 'E_STOP'


class FactoryManager(Node):

    def __init__(self):
        super().__init__('factory_manager')
        self.state = IDLE
        self.state_pub = self.create_publisher(String, '/factory/state', 10)
        self.conveyor_pub = self.create_publisher(Bool, '/conveyor/cmd', 10)
        self.conveyor_on = None  # 마지막으로 보낸 컨베이어 명령 변수로 담음 . None = 아직 안 보냄
        self.create_subscription(Bool, '/factory/start', self.on_start, 10)
        self.create_subscription(Bool, '/factory/emergency_stop', self.on_emergency_stop, 10)
        self.declare_parameter('state_publish_period_sec', 1.0)
        period = self.get_parameter('state_publish_period_sec').value
        self.create_timer(period, self.publish_state)

        self.get_logger().info('factory_manager 시작 (뼈대)')

        # TODO 구독: /factory/start, /factory/emergency_stop, /pallet_1_reset, /pallet_2_reset, /factory/inject_fault, /factory/fault_clear
        # TODO 구독: /bin/count, /conveyor/box_count, /detect_1_event, /detect_2_event, /agv_1_status, /pallet_2_count, /factory/heartbeat_unity
        # TODO 발행: /conveyor/cmd, /factory/state(1초+변경 시), /factory/stats(1초, JSON), /factory/heartbeat_ros(1초), /agv_1_cmd(depart | return)
        # TODO AGV1 (10.28 결정): /agv_1_status LOADED + 상태 RUNNING → /agv_1_cmd depart, /pallet_2_count 증가(출고 팔레트 적재 완료) → /agv_1_cmd return
        # TODO 불량 팔레트 (SRS v2.2): 칸 n은 출고 팔레트와 같은 2x2x3 규칙으로 계산해 /arm_2_pick_place의 place_pose로 보내고, 성공 응답마다 적재 수 +1 (/factory/stats pallet_1)
        # TODO PALLET_FULL (SRS v2.2): 어느 팔레트든 12개면 전환. /pallet_N_reset으로 그 팔레트를 0으로, 다른 팔레트도 12개면 PALLET_FULL 유지
        # TODO 서비스 호출: /arm_1_pick_place ~ /arm_3_pick_place
        # TODO 파라미터: feed_stop_count=4, feed_resume_count=3, heartbeat_timeout_sec=3.0 (dt_bringup/config/factory.yaml)
        # TODO ⚠ 미정 (SRS v2.1 피드백 4): PALLET_FULL 중 E_STOP 해제 후 어느 상태로 돌아갈지 (FR-15, FR-16)
        # TODO ⚠ 미정 (SRS v2.1 피드백 5): [조치 완료] /factory/fault_clear 처리 방법 (FR-18)

    def set_state(self, new_state):
        """공정 상태를 바꾸고 바뀌었으면 /factory/state로 바로 알린다."""
        if new_state == self.state:
            return
        self.get_logger().info(f'상태 {self.state} -> {new_state}')
        self.state = new_state
        self.publish_state()

    def publish_state(self):
        """지금 상태를 /factory/state 로 발행한다."""
        msg = String()
        msg.data = self.state
        self.state_pub.publish(msg)

    def set_conveyor(self, on):
        """컨베이어 가동 (true) 정지 (false) 같은 명령은 다시 보내지 않는다."""
        if on == self.conveyor_on:
            return
        self.conveyor_on = on
        msg = Bool()
        msg.data = on
        self.conveyor_pub.publish(msg)
        self.get_logger().info(f'컨베이어 {"가동" if on else "정지"}')

    def on_start(self, msg):
        """[동작] 버튼 눌렀을 때 IDLE에서만 RUNNING으로 바꾸고 컨베이어를 가동한다."""
        if not msg.data:
            return
        if self.state != IDLE:
            self.get_logger().warn(f'{self.state} 상태라 /factory/start 무시')
            return
        self.set_state(RUNNING)
        self.set_conveyor(True)

    def on_emergency_stop(self, msg):
        """비상정지 (FR-16). 누르면 E_STOP, 해제하면 IDLE"""
        if msg.data:
            self.set_conveyor(False)
            self.set_state(E_STOP)
        elif self.state == E_STOP:
            self.set_state(IDLE)


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
