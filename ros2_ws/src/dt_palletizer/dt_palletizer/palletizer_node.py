"""palletizer_node — 뼈대 (chore #1). 담당 D · scope `palletizer`

실행: ros2 run dt_palletizer palletizer_node
"""
import rclpy
from rclpy.node import Node


class Palletizer(Node):

    def __init__(self):
        super().__init__('palletizer_node')
        self.get_logger().info('palletizer_node 시작 (뼈대)')
        # TODO 구독: /agv_1_status(ARRIVED), /factory/state, /factory/pallet_reset
        # TODO 서비스 호출: /arm_4_pick_place → 출고 팔레트 다음 칸 (2x2, 3단 = 12칸)
        # TODO 발행: /pallet/count (std_msgs/Int32, 0~12, 바뀔 때) → factory_manager (10.28 결정)
        # TODO 파라미터: pallet_capacity=12 (dt_bringup/config/factory.yaml)


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
