"""Unity 연결만 확인하는 launch (M1, 팀원 세팅 검증용).

    ros2 launch dt_bringup connect.launch.py
    → Unity에서 ConnectionTest 씬 Play
"""
from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument
from launch.substitutions import LaunchConfiguration
from launch_ros.actions import Node


def generate_launch_description():
    port = LaunchConfiguration('port')
    return LaunchDescription([
        DeclareLaunchArgument('port', default_value='10000',
                              description='ROS-TCP-Endpoint 포트 (Unity ROS Settings의 Port와 같게)'),
        Node(
            package='dt_bringup',              # 스레드 여러 개 버전 (scripts/tcp_endpoint_mt.py)
            executable='tcp_endpoint_mt.py',      # name 없음: 이유는 factory.launch.py 참고
            parameters=[{'ROS_IP': '0.0.0.0', 'ROS_TCP_PORT': port}],
            output='screen',
            emulate_tty=True,
        ),
    ])
