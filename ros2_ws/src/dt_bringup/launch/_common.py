"""factory.launch.py / fake.launch.py 가 함께 쓰는 노드 목록."""
import os

from ament_index_python.packages import get_package_share_directory
from launch_ros.actions import Node


def config_path(name):
    return os.path.join(get_package_share_directory('dt_bringup'), 'config', name)


def control_nodes():
    """공정 제어 노드 (Unity든 fake든 공통).

    로봇팔은 Unity IK로 움직이므로 ROS 팔 노드는 없다 (10.30 확정, SRS v3.1).
    /arm_N_pick_place 서버는 Unity ArmPickPlaceServer.cs가 맡는다.
    """
    factory_yaml = config_path('factory.yaml')

    return [
        Node(package='factory_manager', executable='factory_manager_node',
             name='factory_manager', parameters=[factory_yaml],
             output='screen', emulate_tty=True),
        Node(package='dt_palletizer', executable='palletizer_node',
             name='palletizer_node', parameters=[factory_yaml],
             output='screen', emulate_tty=True),
        Node(package='dt_agv', executable='agv_1_controller_node',
             name='agv_1_controller', parameters=[factory_yaml],
             output='screen', emulate_tty=True),
    ]
