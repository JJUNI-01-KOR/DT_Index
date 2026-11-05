"""실제 시연용: Unity + 전체 제어 노드 (NFR-10: 명령 1줄로 시작).

    ros2 launch dt_bringup factory.launch.py
"""
import os
import sys

from launch import LaunchDescription
from launch_ros.actions import Node

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import control_nodes  # noqa: E402


def generate_launch_description():
    return LaunchDescription([
        # 기본 default_server_endpoint는 스레드 1개라 팔 서비스 응답을 기다리는 동안 토픽 전달이 멈춘다 → 스레드 여러 개 버전
        # name을 주지 않는다: launch의 __node 리매핑은 이 프로세스의 모든 노드(엔드포인트가 토픽마다 만드는 노드 포함)에
        # 걸려 이름이 전부 같아진다 (NFR-08 · TC_DT_050). 서버 노드 이름은 tcp_endpoint_mt.py에서 정한다.
        Node(package='dt_bringup', executable='tcp_endpoint_mt.py',
             parameters=[{'ROS_IP': '0.0.0.0', 'ROS_TCP_PORT': 10000}],
             output='screen', emulate_tty=True),
        *control_nodes(),
    ])
