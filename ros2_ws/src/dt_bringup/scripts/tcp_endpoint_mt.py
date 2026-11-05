#!/usr/bin/env python3
"""ROS-TCP-Endpoint를 스레드 여러 개로 실행한다. 담당 D · scope bringup

왜: 기본 실행 파일(ros_tcp_endpoint/default_server_endpoint)은 시작할 때 등록된 토픽 수로
    MultiThreadedExecutor 스레드 수를 정한다. 시작할 때는 0개라 스레드가 1개뿐이다.
    그래서 ROS가 Unity 서비스(/arm_N_pick_place)를 부르고 응답(팔 동작 6~15초)을 기다리는 동안
    다른 토픽 전달(/factory/state, /conveyor/cmd, /agv_1_goal …)이 모두 멈춘다.
    → 팔이 움직이는 동안 비상정지가 Unity에 늦게 가고, Unity Heartbeat가 3초 끊김으로 판정한다 (11/02 실측).
고침: 같은 TcpServer를 쓰되 스레드를 THREADS개로 고정해, 서비스 응답을 기다리는 동안에도 다른 노드가 돈다.
      노드마다 콜백 그룹이 따로라 /arm_1·/arm_3 서비스와 토픽 전달이 동시에 처리된다.

11/05 BUG-002 (TC_DT_041): Unity가 끊겼다 다시 붙기를 반복하면 엔드포인트가 exit code 1로 죽는 문제
    원인 1) Unity는 붙을 때마다 모든 토픽·서비스를 다시 등록한다 (발행은 한 연결에 2번씩).
            원본 SysCommands는 그때마다 기존 노드를 지우고 새로 만든다. 지울 때도 destroy_node를 먼저 하고
            executor.remove_node를 나중에 해서, 다른 스레드가 그 노드를 돌리는 중이면 executor가 예외를 낸다.
    원인 2) 콜백 예외(예: Unity가 끊긴 사이 /arm_N_pick_place 서비스 응답 실패)를 executor.spin()이
            그대로 다시 던져 main이 끝난다.
    고침 1) 같은 토픽·같은 타입으로 다시 등록하면 기존 노드를 그대로 쓴다 (지우고 만들기 없음, 노드 이름 중복 경고도 없음).
            타입이 다르면 원본대로 바꾸되, executor에서 먼저 빼고 나서 지운다.
    고침 2) spin() 대신 spin_once를 돌리며 예외는 로그만 남기고 계속 돈다.
    원인 3) pkill(SIGTERM)이나 Ctrl+C로 rclpy가 내려가도 Unity 연결 스레드(ClientThread, daemon 아님)가 남아
            프로세스가 안 끝난다. 노드는 다 지워졌는데 10000번 포트는 계속 잡고 있어 Unity가 1초마다 붙었다 끊기고
            (InvalidHandle 예외), launch를 다시 켜도 새 엔드포인트가 포트를 못 잡아 재연결이 안 된다.
    고침 3) 정리가 끝나면 os._exit로 남은 스레드까지 끝내 포트를 바로 놓는다.

실행: ros2 launch dt_bringup factory.launch.py (connect.launch.py도 이것을 쓴다)
"""
import os
import sys
import time

import rclpy
from rclpy.executors import MultiThreadedExecutor
from ros_tcp_endpoint import TcpServer
from ros_tcp_endpoint.server import SysCommands

THREADS = 16

_orig_publish = SysCommands.publish
_orig_subscribe = SysCommands.subscribe
_orig_unity_service = SysCommands.unity_service


def _same_class(self, message_name, extension, current):
    """다시 등록하려는 타입이 지금 노드의 타입과 같은지"""
    cls = self.resolve_message_name(message_name, extension)
    return cls is not None and cls is current


def _publish(self, topic, message_name, queue_size=10, latch=False):
    old = self.tcp_server.publishers_table.get(topic)
    if old is not None and _same_class(self, message_name, 'msg', type(old.msg)):
        return   # 같은 토픽·타입 발행 등록이 이미 있다 → 그대로 씀
    _orig_publish(self, topic, message_name, queue_size, latch)


def _subscribe(self, topic, message_name):
    old = self.tcp_server.subscribers_table.get(topic)
    if old is not None and _same_class(self, message_name, 'msg', old.msg):
        return
    _orig_subscribe(self, topic, message_name)


def _unity_service(self, topic, message_name):
    old = self.tcp_server.unity_services_table.get(topic)
    if old is not None and _same_class(self, message_name, 'srv', old.service_class):
        return
    _orig_unity_service(self, topic, message_name)


def _unregister_node(self, old_node):
    """원본은 destroy_node → remove_node 순서라 다른 스레드가 돌리는 중이면 예외. 빼고 나서 지운다"""
    if old_node is None:
        return
    if self.executor is not None:
        try:
            self.executor.remove_node(old_node)
        except Exception as e:  # noqa: BLE001
            self.logwarn(f'remove_node({old_node.get_name()}) 실패: {e!r}')
    try:
        old_node.unregister()
    except Exception as e:  # noqa: BLE001
        self.logwarn(f'unregister({old_node.get_name()}) 실패: {e!r}')


SysCommands.publish = _publish
SysCommands.subscribe = _subscribe
SysCommands.unity_service = _unity_service
TcpServer.unregister_node = _unregister_node


def main(args=None):
    rclpy.init(args=args)
    server = TcpServer('ros_tcp_endpoint')   # launch에서 name을 주면 하위 노드까지 같은 이름이 되므로 여기서 정한다
    executor = MultiThreadedExecutor(num_threads=THREADS)
    executor.add_node(server)
    server.executor = executor        # Unity가 붙어 토픽을 등록하면 TcpServer가 이 executor에 노드를 더한다
    server.start()
    server.get_logger().info(f'ROS-TCP-Endpoint 시작 (스레드 {THREADS}개, 재등록 시 노드 재사용)')
    try:
        while rclpy.ok():
            try:
                executor.spin_once(timeout_sec=0.5)
            except KeyboardInterrupt:
                raise
            except Exception as e:  # noqa: BLE001  콜백 하나의 예외로 엔드포인트 전체가 죽지 않게
                server.get_logger().error(f'executor 예외, 계속 실행: {type(e).__name__}: {e}')
                time.sleep(0.05)
    except KeyboardInterrupt:
        pass
    finally:
        try:
            server.get_logger().info('ROS-TCP-Endpoint 종료')
            server.destroy_nodes()
            if rclpy.ok():
                rclpy.shutdown()
        finally:
            # Unity 연결 스레드(daemon 아님)가 남아 프로세스가 포트를 쥔 채 안 끝나는 것을 막는다
            sys.stdout.flush()
            sys.stderr.flush()
            os._exit(0)


if __name__ == '__main__':
    main()
