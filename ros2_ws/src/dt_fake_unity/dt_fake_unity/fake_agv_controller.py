"""fake_agv_controller — 진짜 agv_1_controller 대신 띄워 factory_manager만 시험한다. 담당 D · scope `fake`

SRS v3.1 기준 뼈대 (WBS 2.4 가짜 노드 3종). factory_manager의 AGV1 명령 순서(TC-17, TC-41)와
AGV1 감시 시간(TC-42)을 Unity와 진짜 agv_1_controller 없이 확인할 때 쓴다.
⚠ 진짜 agv_1_controller와 동시에 띄우지 않는다.

실행
    ros2 run dt_fake_unity fake_agv_controller
    ros2 run dt_fake_unity fake_agv_controller --ros-args -p no_reply:=true   # 감시 시간 초과 시험

할 일 (TODO)
    구독: /agv_1_cmd (P1_DOCK, P2_DOCK, FINAL, HOME)
    발행: /agv_1_loaded (Bool, 바뀔 때 + 1초마다)
          PN_DOCK → reply_sec 뒤 true, FINAL → reply_sec 뒤 false, HOME → 그대로
          적재 여부와 맞지 않는 명령(적재 중 PN_DOCK, 빈 상태 FINAL)은 무시 + 경고
"""
import rclpy
from rclpy.node import Node
from std_msgs.msg import Bool, String


class FakeAgvController(Node):

    def __init__(self):
        super().__init__('fake_agv_controller')
        self.declare_parameter('reply_sec', 3.0)    # 명령을 받고 적재 여부를 바꿀 때까지
        self.declare_parameter('no_reply', False)   # true면 적재 여부를 바꾸지 않음 (TC-42)
        self.loaded = False

        self.pub_loaded = self.create_publisher(Bool, '/agv_1_loaded', 10)
        self.create_subscription(String, '/agv_1_cmd', self.on_cmd, 10)
        self.create_timer(1.0, lambda: self.pub_loaded.publish(Bool(data=self.loaded)))
        self.get_logger().info('fake_agv_controller 시작 (뼈대)')

    def on_cmd(self, msg):
        """/agv_1_cmd 수신 (FR-13)."""
        self.get_logger().info(f'/agv_1_cmd {msg.data}')
        # TODO(FR-13): reply_sec 뒤 loaded를 바꾸고 바로 발행. no_reply면 바꾸지 않음


def main(args=None):
    rclpy.init(args=args)
    node = FakeAgvController()
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
