"""agv_1_controller — AGV1 왕복 제어 (SRS v3.1 FR-13, FR-16, 부록 A.6). 담당 C · scope `agv`

판단 규칙(경로 순서, attach/detach 시점, /agv_1_loaded 결정)은 rclpy 없는 route.py에 두고,
이 노드는 토픽을 연결하고 받은 goal을 그대로 Unity(AgvLink.cs)로 전달한다.

실행: ros2 run dt_agv agv_1_controller_node
"""
import json
import os

import rclpy
import yaml
from ament_index_python.packages import PackageNotFoundError, get_package_share_directory
from rclpy.node import Node
from std_msgs.msg import Bool, String

from .route import AgvRoute


def default_waypoints_file():
    """dt_bringup에 설치된 agv_waypoints.yaml 경로. 못 찾으면 빈 문자열."""
    try:
        share = get_package_share_directory('dt_bringup')
    except PackageNotFoundError:
        return ''
    return os.path.join(share, 'config', 'agv_waypoints.yaml')


def load_waypoints(path):
    """agv_waypoints.yaml → (stops, route)."""
    with open(path, encoding='utf-8') as f:
        data = yaml.safe_load(f) or {}
    return data.get('stops', {}), data.get('route', {})


class Agv1Controller(Node):

    def __init__(self):
        super().__init__('agv_1_controller')

        self.declare_parameter('waypoints_file', default_waypoints_file())
        path = self.get_parameter('waypoints_file').value
        if not path:
            self.get_logger().error('agv_waypoints.yaml 경로를 못 찾음 (dt_bringup 설치 확인)')
            stops, route = {}, {}
        else:
            stops, route = load_waypoints(path)
        self.route = AgvRoute(stops, route)

        self.goal_pub = self.create_publisher(String, '/agv_1_goal', 10)
        self.loaded_pub = self.create_publisher(Bool, '/agv_1_loaded', 10)

        self.create_subscription(String, '/agv_1_cmd', self.on_cmd, 10)
        self.create_subscription(String, '/agv_1_status', self.on_status, 10)
        self.create_subscription(String, '/factory/state', self.on_factory_state, 10)

        self.last_factory_state = ''
        self.create_timer(1.0, self.publish_loaded)  # /agv_1_loaded: 바뀔 때 + 1초마다 (부록 A.6)

        self.get_logger().info('agv_1_controller 시작')

    # ── /agv_1_cmd ───────────────────────────────────────────
    def on_cmd(self, msg):
        goal, warn = self.route.start(msg.data)
        if warn:
            self.get_logger().warn(f'/agv_1_cmd {msg.data}: {warn}')
            return
        self.send_goal(goal)

    # ── /agv_1_status ────────────────────────────────────────
    def on_status(self, msg):
        try:
            data = json.loads(msg.data)
        except (TypeError, ValueError):
            self.get_logger().error(f'/agv_1_status JSON 해석 실패: {msg.data}')
            return
        state = data.get('state')

        if state == 'ARRIVED':
            nxt = self.route.on_arrived()
            if nxt is not None:
                self.send_goal(nxt)
        elif state == 'ATTACHED':
            if self.route.on_attached() is not None:
                self.publish_loaded()
        elif state == 'DETACHED':
            if self.route.on_detached() is not None:
                self.publish_loaded()
        # MOVING은 로그만 필요하면 보되, 판단에는 안 씀

    # ── /factory/state (E_STOP 해제 초기화, FR-16) ──────────────
    def on_factory_state(self, msg):
        if self.last_factory_state == 'E_STOP' and msg.data == 'IDLE':
            self.route.on_estop_cleared()
            self.publish_loaded()
            self.get_logger().info('해제 초기화: 경로 버리고 HOME, 적재 없음')
        self.last_factory_state = msg.data

    # ── 발행 ────────────────────────────────────────────────
    def send_goal(self, goal):
        self.goal_pub.publish(String(data=json.dumps(goal)))

    def publish_loaded(self):
        self.loaded_pub.publish(Bool(data=self.route.loaded))


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
