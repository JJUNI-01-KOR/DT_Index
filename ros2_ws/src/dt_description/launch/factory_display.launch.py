"""AGV1·팔 1~3 모델과 공장 배치를 RViz에 표시한다 (FR-13 확인·시연용). 담당 D · scope bringup

    ros2 launch dt_description factory_display.launch.py              # Unity Play 중: Unity가 보내는 /tf, 관절 상태로 움직임
    ros2 launch dt_description factory_display.launch.py test:=true    # Unity 없이 모델만: map → agv_1_link를 HOME에 고정, 바퀴 0
    ros2 launch dt_description factory_display.launch.py rviz:=false   # RViz 없이 (RViz는 따로 띄울 때)
    ros2 launch dt_description factory_display.launch.py layout:=false # 공장 배치 표시물 없이
    ros2 launch dt_description factory_display.launch.py arms:=false   # 팔 1~3 없이 AGV만

팔 1~3은 arms_display.launch.py를 그대로 불러 띄운다 (arms:=true 기본). 이 launch를 켰으면 arms_display.launch.py를 따로 켜지 않는다.
팔은 Unity가 보내는 map → arm_N/base_link와 /arm_N_joint_states로 보이므로 test:=true(Unity 없음)에서는 RViz에 팔 TF 경고가 뜬다.

robot_state_publisher가 urdf/agv_1.urdf를 읽어 agv_1_link → 몸통·바퀴 TF를 낸다.
  구독: /agv_1_joint_states (Unity AgvWaypointDriver, 바퀴 4개 각도, 10 Hz)
  발행: /agv_1_description (URDF 문자열, RViz RobotModel이 읽음), /tf, /tf_static
map → agv_1_link는 Unity가 /tf로 보낸다 (이 launch가 내지 않음, test:=true일 때만 고정값).
factory_layout_markers가 컨베이어(Unity Main에서 뽑은 config/factory_layout.yaml)와 line_coords·agv_waypoints 좌표를
/factory_layout (MarkerArray)으로 낸다.
"""
import os

from ament_index_python.packages import get_package_share_directory
from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument, ExecuteProcess, IncludeLaunchDescription
from launch.conditions import IfCondition
from launch.launch_description_sources import PythonLaunchDescriptionSource
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
    layout = LaunchConfiguration('layout')
    arms = LaunchConfiguration('arms')
    zeros = '{name: [%s], position: [0.0, 0.0, 0.0, 0.0]}' % ', '.join(WHEELS)

    return LaunchDescription([
        DeclareLaunchArgument('rviz', default_value='true', description='RViz도 같이 띄움'),
        DeclareLaunchArgument('test', default_value='false', description='Unity 없이 모델만 확인 (HOME 고정, 바퀴 0)'),
        DeclareLaunchArgument('layout', default_value='true', description='공장 배치 표시물 (/factory_layout)'),
        DeclareLaunchArgument('arms', default_value='true', description='팔 1~3 FR20 모델 (arms_display.launch.py)'),

        # 팔 1~3: robot_state_publisher 3개 (/arm_N_joint_states → arm_N/ TF, /arm_N_description)
        IncludeLaunchDescription(
            PythonLaunchDescriptionSource(os.path.join(share, 'launch', 'arms_display.launch.py')),
            condition=IfCondition(arms)),

        Node(package='robot_state_publisher', executable='robot_state_publisher',
             name='agv_1_state_publisher', output='screen',
             parameters=[{'robot_description': urdf}],
             remappings=[('joint_states', '/agv_1_joint_states'),
                         ('robot_description', '/agv_1_description')]),

        Node(package='dt_description', executable='factory_layout_markers.py',
             name='factory_layout_markers', output='screen', condition=IfCondition(layout)),

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
