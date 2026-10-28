"""agv_1_controller — 뼈대 (chore #1). 담당 C · scope `agv`

실행: ros2 run dt_agv agv_1_controller_node
"""
import rclpy
from rclpy.node import Node


class Agv1Controller(Node):

    def __init__(self):
        super().__init__('agv_1_controller')
        self.get_logger().info('agv_1_controller 시작 (뼈대)')
        # TODO 발행: /agv_1_goal (geometry_msgs/PoseStamped)
        # TODO 구독: /agv_1_status (IDLE | MOVING | ARRIVED | LOADED)
        # TODO 사이클 시간 기록 (출발·도착·복귀)
        # TODO ⚠ 출발·복귀 요청 받는 방법 미정 (SRS 2.5)


def main(args=None):
    rclpy.init(args=args)
    node = Agv1Controller()
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
