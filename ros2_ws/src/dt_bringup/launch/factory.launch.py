"""실제 시연용: Unity + 전체 제어 노드 (NFR-10: 명령 1줄로 시작).

    ros2 launch dt_bringup factory.launch.py
    ros2 launch dt_bringup factory.launch.py use_ros_arm:=true   # ROS2 관절 제어일 때
"""
import os
import sys

from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument
from launch_ros.actions import Node

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import control_nodes  # noqa: E402


def generate_launch_description():
    return LaunchDescription([
        DeclareLaunchArgument('use_ros_arm', default_value='false'),
        Node(package='ros_tcp_endpoint', executable='default_server_endpoint',
             name='ros_tcp_endpoint',
             parameters=[{'ROS_IP': '0.0.0.0', 'ROS_TCP_PORT': 10000}],
             output='screen', emulate_tty=True),
        *control_nodes(),
    ])
