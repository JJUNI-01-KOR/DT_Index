"""factory.launch.py / fake.launch.py 가 함께 쓰는 노드 목록."""
import os

from ament_index_python.packages import get_package_share_directory
from launch.conditions import IfCondition
from launch.substitutions import LaunchConfiguration
from launch_ros.actions import Node


def config_path(name):
    """dt_bringup의 share/config 안 파일 전체 경로."""
    return os.path.join(get_package_share_directory('dt_bringup'), 'config', name)


def control_nodes():
    """공정 제어 노드 (Unity든 fake든 공통)."""
    factory_yaml = config_path('factory.yaml')
    # line_coords.yaml은 ros__parameters 형식이 아니라 노드가 파일을 직접 읽는다 → 전체 경로를 넘긴다
    coords = {'line_coords_file': config_path('line_coords.yaml')}
    # factory.yaml의 waypoints_file(파일 이름만)을 전체 경로로 덮어쓴다 (뒤에 오는 값이 이김)
    waypoints = {'waypoints_file': config_path('agv_waypoints.yaml')}
    use_ros_arm = LaunchConfiguration('use_ros_arm')

    nodes = [
        Node(package='factory_manager', executable='factory_manager_node',
             name='factory_manager', parameters=[factory_yaml, coords],
             output='screen', emulate_tty=True),
        Node(package='dt_palletizer', executable='palletizer_node',
             name='palletizer_node', parameters=[factory_yaml, coords],
             output='screen', emulate_tty=True),
        Node(package='dt_agv', executable='agv_1_controller_node',
             name='agv_1_controller', parameters=[factory_yaml, coords, waypoints],
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
