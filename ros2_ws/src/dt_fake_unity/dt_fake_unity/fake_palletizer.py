"""fake_palletizer — 진짜 palletizer 대신 띄워 factory_manager만 시험한다. 담당 D · scope `fake`

SRS v3.1 기준 뼈대 (WBS 2.4 가짜 노드 3종). factory_manager의 적재 수·대기열(FR-12)과
운반 흐름(TC-15 ~ TC-18)을 Unity와 진짜 palletizer 없이 확인할 때 쓴다.
⚠ 진짜 palletizer_node와 동시에 띄우지 않는다.

실행
    ros2 run dt_fake_unity fake_palletizer

할 일 (TODO)
    발행: /pallet_1_placed, /pallet_2_placed (방석 id)  — 키 입력 또는 place_period_sec마다
    구독: /pallet_1_count, /pallet_2_count             — 적재 기준이면 더 놓지 않음
          /factory/state                             — RUNNING일 때만 놓음
"""
import rclpy
from rclpy.node import Node
from std_msgs.msg import Int32, String


class FakePalletizer(Node):

    def __init__(self):
        super().__init__('fake_palletizer')
        self.declare_parameter('place_period_sec', 0.0)  # 0이면 자동으로 놓지 않음
        self.declare_parameter('pallet_capacity', 5)
        self.counts = {1: 0, 2: 0}
        self.factory_state = ''

        self.pub_placed = {n: self.create_publisher(String, f'/pallet_{n}_placed', 10) for n in (1, 2)}
        for n in (1, 2):  # n=n 으로 지금 값을 묶어 둔다
            self.create_subscription(Int32, f'/pallet_{n}_count',
                                     lambda msg, n=n: self.on_count(n, msg), 10)
        self.create_subscription(String, '/factory/state', self.on_state, 10)
        self.get_logger().info('fake_palletizer 시작 (뼈대)')

    def on_count(self, pallet, msg):
        self.counts[pallet] = msg.data

    def on_state(self, msg):
        self.factory_state = msg.data

    def place(self, pallet, cushion):
        """파레트 N에 방석 하나를 놓았다고 알린다 (FR-10, 11 4번)."""
        # TODO(FR-12): RUNNING이고 counts[pallet] < pallet_capacity면 /pallet_N_placed 발행
        raise NotImplementedError


def main(args=None):
    rclpy.init(args=args)
    node = FakePalletizer()
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
