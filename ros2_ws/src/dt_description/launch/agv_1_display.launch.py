"""AGV1을 RViz에 모델로 표시한다 (FR-13 확인용). 담당 D · scope bringup

    ros2 launch dt_description agv_1_display.launch.py              # Unity Play 중: Unity가 보내는 /tf, /agv_1_joint_states로 움직임
    ros2 launch dt_description agv_1_display.launch.py test:=true    # Unity 없이 모델만: map → agv_1_link를 HOME에 고정, 바퀴 0
    ros2 launch dt_description agv_1_display.launch.py rviz:=false   # robot_state_publisher만 (RViz는 따로 띄울 때)

robot_state_publisher가 urdf/agv_1.urdf를 읽어 agv_1_link → 몸통·바퀴 TF를 낸다.
  구독: /agv_1_joint_states (Unity AgvWaypointDriver, 바퀴 4개 각도, 10 Hz)
  발행: /agv_1_description (URDF 문자열, RViz RobotModel이 읽음), /tf, /tf_static
map → agv_1_link는 Unity가 /tf로 보낸다 (이 launch가 내지 않음, test:=true일 때만 고정값).
"""
import os

from ament_index_python.packages import get_package_share_directory
from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument, ExecuteProcess
from launch.conditions import IfCondition
from launch.substitutions import LaunchConfiguration
from launch_ros.actions import Node

WHEELS = ['bone_wheel_l_back', 'bone_wheel_l_front', 'bone_wheel_r_back', 'bone_wheel_r_front']
HOME = ('-0.75', '16.5', '0', '-1.5708', '0', '0')   # agv_waypoints.yaml HOME (x, y, z, yaw, pitch, roll)


def generate_launch_description():
    share = get_package_share_directory('dt_description')
    with open(os.path.join(share, 'urdf', 'agv_1.urdf'), encoding='utf-8') as f:
        urdf = f.read()

    rviz = LaunchConfiguration('rviz')
    test = LaunchConfiguration('test')
    zeros = '{name: [%s], position: [0.0, 0.0, 0.0, 0.0]}' % ', '.join(WHEELS)

    return LaunchDescription([
        DeclareLaunchArgument('rviz', default_value='true', description='RViz도 같이 띄움'),
        DeclareLaunchArgument('test', default_value='false', description='Unity 없이 모델만 확인 (HOME 고정, 바퀴 0)'),

        Node(package='robot_state_publisher', executable='robot_state_publisher',
             name='agv_1_state_publisher', output='screen',
             parameters=[{'robot_description': urdf}],
             remappings=[('joint_states', '/agv_1_joint_states'),
                         ('robot_description', '/agv_1_description')]),

        Node(package='rviz2', executable='rviz2', name='rviz2', output='screen',
             arguments=['-d', os.path.join(share, 'rviz', 'agv_1.rviz')],
             condition=IfCondition(rviz)),

        # test:=true — Unity 대신 map → agv_1_link와 바퀴 각도 0을 낸다
        Node(package='tf2_ros', executable='static_transform_publisher', name='agv_1_test_tf',
             arguments=[*HOME, 'map', 'agv_1_link'], condition=IfCondition(test)),
        ExecuteProcess(cmd=['ros2', 'topic', 'pub', '-r', '10', '/agv_1_joint_states',
                            'sensor_msgs/msg/JointState', zeros],
                       condition=IfCondition(test), output='log'),
    ])
