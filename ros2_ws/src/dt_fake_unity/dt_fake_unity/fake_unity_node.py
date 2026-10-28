"""fake_unity — 뼈대 (chore #1). 담당 D · scope `fake`

실행: ros2 run dt_fake_unity fake_unity_node
"""
import rclpy
from rclpy.node import Node


class FakeUnity(Node):

    def __init__(self):
        super().__init__('fake_unity')
        self.get_logger().info('fake_unity 시작 (뼈대)')
        # TODO 발행: /bin/count, /conveyor/box_count, /detect_1_event, /detect_2_event, /agv_1_status, /factory/heartbeat
        # TODO 구독: /conveyor/cmd, /agv_1_goal → goal 받으면 MOVING, 8초 뒤 ARRIVED
        # TODO 서비스 서버: /arm_1_pick_place ~ /arm_4_pick_place (1초 뒤 success)


def main(args=None):
    rclpy.init(args=args)
    node = FakeUnity()
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
