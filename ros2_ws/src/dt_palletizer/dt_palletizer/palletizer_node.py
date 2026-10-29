"""palletizer_node — 뼈대 (chore #1). 담당 D · scope `palletizer`

실행: ros2 run dt_palletizer palletizer_node
"""
import rclpy
from rclpy.node import Node


class Palletizer(Node):

    def __init__(self):
        super().__init__('palletizer_node')
        # launch(_common.py)가 dt_bringup/config/line_coords.yaml 전체 경로를 넘긴다 (2.8)
        self.declare_parameter('line_coords_file', '')
        self.line_coords_file = self.get_parameter('line_coords_file').value
        self.get_logger().info(
            f'palletizer_node 시작 (뼈대) line_coords_file={self.line_coords_file or "(없음)"}')
        # TODO 구독: /agv_1_status(ARRIVED), /factory/state, /pallet_2_reset (출고 팔레트 정리)
        # TODO 서비스 호출: /arm_4_pick_place → 출고 팔레트 다음 칸 (2x2, 3단 = 12칸)
        # TODO 발행: /pallet_2_count (std_msgs/Int32, 출고 팔레트 0~12, 바뀔 때) → factory_manager (10.28 결정)
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
