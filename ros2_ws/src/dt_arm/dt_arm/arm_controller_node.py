"""arm_controller — 뼈대 (chore #1). 담당 A · scope `arm`

실행: ros2 run dt_arm arm_controller_node
"""
import rclpy
from rclpy.node import Node


class ArmController(Node):

    def __init__(self):
        super().__init__('arm_controller')
        self.get_logger().info('arm_controller 시작 (뼈대)')
        # TODO 파라미터 arm_id (1~4) → 서비스 /arm_{id}_pick_place, 토픽 /arm_{id}_joint_states
        # TODO ⚠ 10/30 결정: ROS2 관절 제어로 가면 이 노드가 서비스 서버, Unity IK로 가면 사용 안 함


def main(args=None):
    rclpy.init(args=args)
    node = ArmController()
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
