"""Unity 없이 테스트: 가짜 Unity + 전체 제어 노드. Endpoint는 띄우지 않는다.

    ros2 launch dt_bringup fake.launch.py
    ros2 run dt_fake_unity fake_panel        # 다른 터미널에서 버튼 흉내
"""
import os
import sys

from launch import LaunchDescription
from launch_ros.actions import Node

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _common import config_path, control_nodes  # noqa: E402


def generate_launch_description():
    return LaunchDescription([
        Node(package='dt_fake_unity', executable='fake_unity_node',
             name='fake_unity', parameters=[config_path('fake_scenario.yaml')],
             output='screen', emulate_tty=True),
        *control_nodes(),
    ])
