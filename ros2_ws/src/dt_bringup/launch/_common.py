"""factory.launch.py / fake.launch.py 가 함께 쓰는 노드 목록."""
import os

from ament_index_python.packages import get_package_share_directory
from launch.conditions import IfCondition
from launch.substitutions import LaunchConfiguration
from launch_ros.actions import Node


def config_path(name):
    return os.path.join(get_package_share_directory('dt_bringup'), 'config', name)


def control_nodes():
    """공정 제어 노드 (Unity든 fake든 공통)."""
    factory_yaml = config_path('factory.yaml')
    use_ros_arm = LaunchConfiguration('use_ros_arm')

    nodes = [
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
    # ⚠ 10/30 결정: ROS2가 관절을 제어하는 경우에만 켠다 (use_ros_arm:=true)
    for i in range(1, 5):
        nodes.append(
            Node(package='dt_arm', executable='arm_controller_node',
                 name=f'arm_{i}_controller', parameters=[{'arm_id': i}],
                 condition=IfCondition(use_ros_arm),
                 output='screen', emulate_tty=True))
    return nodes
