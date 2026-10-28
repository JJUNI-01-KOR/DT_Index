"""fake_panel — 뼈대 (chore #1). 담당 D · scope `fake`

실행: ros2 run dt_fake_unity fake_panel
"""
import rclpy
from rclpy.node import Node


class FakePanel(Node):

    def __init__(self):
        super().__init__('fake_panel')
        self.get_logger().info('fake_panel 시작 (뼈대)')
        # TODO 키 입력으로 버튼 흉내: s=동작, e=비상정지, r=팔레트 정리, f=고장 주입, c=조치 완료


def main(args=None):
    rclpy.init(args=args)
    node = FakePanel()
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
