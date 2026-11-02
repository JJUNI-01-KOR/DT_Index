# dt_description — AGV1 URDF와 RViz 표시 (D)

Unity AGV1(robot_boxy)을 RViz에서 모델로 보이게 한다. 실제 주행은 Unity가 하고, 여기는 **보이기만** 한다.

| 파일 | 역할 |
|---|---|
| `urdf/agv_1.urdf` | 몸통·무한궤도·바퀴 4개를 상자·원기둥으로 단순화 (구매 에셋 메시는 쓰지 않음). 바퀴 관절 이름 = Unity `bone_wheel_*` |
| `launch/agv_1_display.launch.py` | robot_state_publisher(`/agv_1_joint_states` → 바퀴 TF) + RViz |
| `rviz/agv_1.rviz` | Fixed Frame `map`, Grid, TF, RobotModel(`/agv_1_description`) |

## 쓰는 법
```bash
colcon build --symlink-install --packages-select dt_description && source install/setup.bash

# 1) Unity 없이 모델만 확인 (HOME 위치에 고정)
ros2 launch dt_description agv_1_display.launch.py test:=true

# 2) 실제: factory.launch 켜고 Unity Play 중에 다른 터미널에서
ros2 launch dt_description agv_1_display.launch.py
```

## 토픽·프레임
- Unity → ROS: `/tf` (map → agv_1_link, 10 Hz), `/agv_1_joint_states` (바퀴 4개 각도, 10 Hz) — `AgvWaypointDriver.cs`
- 이 패키지: `/agv_1_description` (URDF), `/tf` (agv_1_link → 바퀴), `/tf_static` (몸통·무한궤도·표시등)
- 앞(x+)에 하늘색 눈, 뒤에 빨간 후미등
