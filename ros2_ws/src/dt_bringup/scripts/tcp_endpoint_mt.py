#!/usr/bin/env python3
"""ROS-TCP-Endpoint를 스레드 여러 개로 실행한다. 담당 D · scope bringup

왜: 기본 실행 파일(ros_tcp_endpoint/default_server_endpoint)은 시작할 때 등록된 토픽 수로
    MultiThreadedExecutor 스레드 수를 정한다. 시작할 때는 0개라 스레드가 1개뿐이다.
    그래서 ROS가 Unity 서비스(/arm_N_pick_place)를 부르고 응답(팔 동작 6~15초)을 기다리는 동안
    다른 토픽 전달(/factory/state, /conveyor/cmd, /agv_1_goal …)이 모두 멈춘다.
    → 팔이 움직이는 동안 비상정지가 Unity에 늦게 가고, Unity Heartbeat가 3초 끊김으로 판정한다 (11/02 실측).
고침: 같은 TcpServer를 쓰되 스레드를 THREADS개로 고정해, 서비스 응답을 기다리는 동안에도 다른 노드가 돈다.
      노드마다 콜백 그룹이 따로라 /arm_1·/arm_3 서비스와 토픽 전달이 동시에 처리된다.

실행: ros2 launch dt_bringup factory.launch.py (connect.launch.py도 이것을 쓴다)
"""
import rclpy
from rclpy.executors import MultiThreadedExecutor
from ros_tcp_endpoint import TcpServer

THREADS = 16


def main(args=None):
    rclpy.init(args=args)
    server = TcpServer('UnityEndpoint')
    executor = MultiThreadedExecutor(num_threads=THREADS)
    executor.add_node(server)
    server.executor = executor        # Unity가 붙어 토픽을 등록하면 TcpServer가 이 executor에 노드를 더한다
    server.start()
    server.get_logger().info(f'ROS-TCP-Endpoint 시작 (스레드 {THREADS}개)')
    try:
        executor.spin()
    except KeyboardInterrupt:
        pass
    finally:
        server.destroy_nodes()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == '__main__':
    main()
