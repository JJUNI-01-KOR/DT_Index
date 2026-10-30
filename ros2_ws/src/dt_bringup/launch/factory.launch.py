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
        Node(package='ros_tcp_endpoint', executable='default_server_endpoint',
             name='ros_tcp_endpoint',
             parameters=[{'ROS_IP': '0.0.0.0', 'ROS_TCP_PORT': 10000}],
             output='screen', emulate_tty=True),
        *control_nodes(),
    ])
