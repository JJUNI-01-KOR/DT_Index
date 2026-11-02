# dt_description — AGV1 URDF와 공장 배치 RViz 표시 (D)

Unity AGV1(robot_boxy)을 RViz에서 모델로 보이게 하고, 공장 배치(컨베이어·파레트 자리·팔·AGV 경로)를 같은 좌표로 그린다.
실제 주행은 Unity가 하고, 여기는 **보이기만** 한다.

![배치 미리보기 (위에서 본 모습, ROS 좌표)](tools/layout_preview.png)

| 파일 | 역할 |
|---|---|
| `urdf/agv_1.urdf` | 몸통·무한궤도·바퀴 4개를 상자·원기둥으로 단순화 (구매 에셋 메시는 쓰지 않음). 바퀴 관절 이름 = Unity `bone_wheel_*` |
| `launch/agv_1_display.launch.py` | robot_state_publisher(`/agv_1_joint_states` → 바퀴 TF) + RViz |
| `rviz/agv_1.rviz` | Fixed Frame `map`, Grid, TF, Factory Layout(`/factory_layout`), RobotModel(`/agv_1_description`) |
| `scripts/factory_layout_markers.py` | 공장 배치 표시물 → `/factory_layout` (MarkerArray, transient_local, 5초마다) |
| `config/factory_layout.yaml` | 컨베이어 칸 38개 위치 (Unity Main에서 뽑음, 직접 고치지 않음) |
| `tools/export_unity_layout.py` | Main.unity → `config/factory_layout.yaml`. 컨베이어를 옮기면 다시 실행 |

## 쓰는 법
```bash
colcon build --symlink-install --packages-select dt_description && source install/setup.bash

# 1) Unity 없이 모델만 확인 (HOME 위치에 고정)
ros2 launch dt_description agv_1_display.launch.py test:=true

# 2) 실제: factory.launch 켜고 Unity Play 중에 다른 터미널에서
ros2 launch dt_description agv_1_display.launch.py
```

## 공장 배치 다시 뽑기 (Main 씬에서 컨베이어를 옮겼을 때)
```bash
cd ros2_ws/src/dt_description
python3 tools/export_unity_layout.py ../../../unity/INDEX_Twin/Assets/_Project/Scenes/Main.unity config/factory_layout.yaml
```
파레트 자리·팔·집는 자리·AGV 정지 지점은 `dt_bringup/config/line_coords.yaml`, `agv_waypoints.yaml`(공식 좌표표)을 그대로 읽는다.

## 토픽·프레임
- Unity → ROS: `/tf` (map → agv_1_link, 10 Hz), `/agv_1_joint_states` (바퀴 4개 각도, 10 Hz) — `AgvWaypointDriver.cs`
- 이 패키지: `/agv_1_description` (URDF), `/tf` (agv_1_link → 바퀴), `/tf_static` (몸통·무한궤도·표시등), `/factory_layout` (공장 배치)
- 앞(x+)에 하늘색 눈, 뒤에 빨간 후미등
