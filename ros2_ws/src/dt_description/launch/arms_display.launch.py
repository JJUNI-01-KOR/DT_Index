"""Unity의 팔 1~3 관절 상태를 FR20 모델로 표시한다."""

import os

from ament_index_python.packages import get_package_share_directory
from launch import LaunchDescription
from launch_ros.actions import Node


def generate_launch_description():
    share = get_package_share_directory("dt_description")
    urdf_path = os.path.join(share, "urdf", "fairino20_v6.urdf")

    with open(urdf_path, encoding="utf-8") as f:
        robot_description = f.read()

    nodes = []

    for arm_id in (1, 2, 3):
        arm_name = f"arm_{arm_id}"

        nodes.append(
            Node(
                package="robot_state_publisher",
                executable="robot_state_publisher",
                name=f"{arm_name}_state_publisher",
                output="screen",
                parameters=[{
                    "robot_description": robot_description,
                    "frame_prefix": f"{arm_name}/",
                    "publish_frequency": 30.0,
                    "use_sim_time": False,
                }],
                remappings=[
                    ("joint_states", f"/{arm_name}_joint_states"),
                    ("robot_description", f"/{arm_name}_description"),
                ],
            )
        )

    return LaunchDescription(nodes)
