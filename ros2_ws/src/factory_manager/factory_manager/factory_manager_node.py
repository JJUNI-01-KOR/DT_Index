"""factory_manager — 뼈대 (chore #1). 담당 B · scope `manager`

실행: ros2 run factory_manager factory_manager_node
"""
import rclpy
from rclpy.node import Node


class FactoryManager(Node):

    def __init__(self):
        super().__init__('factory_manager')
        self.get_logger().info('factory_manager 시작 (뼈대)')
        # TODO 구독: /factory/start, /factory/emergency_stop, /factory/pallet_reset, /factory/inject_fault, /factory/fault_clear
        # TODO 구독: /bin/count, /conveyor/box_count, /detect_1_event, /detect_2_event, /agv_1_status, /factory/heartbeat
        # TODO 발행: /conveyor/cmd, /factory/state(1초+변경 시), /factory/stats(1초, JSON), /factory/heartbeat(1초)
        # TODO 서비스 호출: /arm_1_pick_place ~ /arm_3_pick_place
        # TODO 파라미터: feed_stop_count=4, feed_resume_count=3, heartbeat_timeout_sec=3.0 (dt_bringup/config/factory.yaml)


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
