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

## FR20 로봇팔 3대 표시

Unity의 ArmRig 동기화 변경과 함께 사용한다. 실물 로봇 드라이버나 MoveIt을 실행하지 않으며,
Unity가 보내는 실제 관절값과 베이스 TF로 표시한다. 그리퍼·받침대 외형은 이 URDF에 포함되지 않는다.

### 최초 설치 / 업데이트

```bash
cd ~/dev/DT_Index
git lfs pull
source /opt/ros/humble/setup.bash
cd ros2_ws
colcon build
source install/setup.bash
```

### Cyclone DDS + localhost 환경

프로젝트의 `ROS_DOMAIN_ID=30`, `ROS_LOCALHOST_ONLY=1`을 유지한다.
Cyclone DDS를 사용하는 팀원은 필요하면 `ros-humble-rmw-cyclonedds-cpp`를 설치하고,
아래 환경을 모든 ROS 터미널에 동일하게 적용한다. `.bashrc`에 한 번 설정하면 새 터미널에서 자동 적용된다.
기존 동일 변수 설정은 정리하며, 다른 용도로 쓰는 DDS 설정이 있으면 Discovery 항목을 병합한다.

```bash
export RMW_IMPLEMENTATION=rmw_cyclonedds_cpp
export ROS_DOMAIN_ID=30
export ROS_LOCALHOST_ONLY=1
export CYCLONEDDS_URI='<CycloneDDS xmlns="https://cdds.io/config"><Domain Id="any"><Discovery><ParticipantIndex>auto</ParticipantIndex><MaxAutoParticipantIndex>99</MaxAutoParticipantIndex><Peers><Peer Address="127.0.0.1"/></Peers></Discovery></Domain></CycloneDDS>'
```

정확한 변수명은 `CYCLONEDDS_URI`다 (`CYCLONE_DDS_URI` 아님).
`/tmp` 설정 파일은 재부팅 뒤 없어질 수 있다. `source install/setup.bash`는 위 수동 설정을 대신하지 않는다.
환경을 변경한 후에는 기존 launch를 종료하고 다시 실행한다. DDS 설정은 각자의 환경이며 저장소가 `.bashrc`를 수정하지 않는다.

### 매번 실행 (각각 별도 터미널)

각 터미널에서 위 DDS 환경을 적용하고 `source ~/dev/DT_Index/ros2_ws/install/setup.bash`를 실행한다.

```bash
ros2 launch dt_bringup factory.launch.py
```

```bash
ros2 launch dt_description agv_1_display.launch.py
```

```bash
ros2 launch dt_description arms_display.launch.py
```

Unity에서 자기 VM의 IP와 TCP 포트 10000을 확인하고 Main을 Play한다.
팔 ID는 1·2·3, 공통 프리팹 기본 ID는 1이다. 초기 E_STOP은 연결 안정 후 해제하고 시작한다.
factory는 기존 `tcp_endpoint_mt.py`를 사용해야 한다. 시작 로그의 스레드 16개를 확인한다.

| 팔 | 관절 상태 입력 | 모델 출력 | TF 접두사 |
|---|---|---|---|
| 1 | `/arm_1_joint_states` | `/arm_1_description` | `arm_1/` |
| 2 | `/arm_2_joint_states` | `/arm_2_description` | `arm_2/` |
| 3 | `/arm_3_joint_states` | `/arm_3_description` | `arm_3/` |

관절 타입은 `sensor_msgs/JointState`, 이름은 `j1`~`j6`, 회전값은 라디안이다.
Unity가 `map → arm_N/base_link`를 발행하며 ROS가 팔 내부 TF를 계산한다.
동일한 베이스 TF나 관절 토픽을 다른 테스트 노드에서 중복 발행하지 않는다.

```bash
ros2 topic echo /arm_1_joint_states --once
ros2 run tf2_ros tf2_echo map arm_1/wrist3_link
```

저장된 `rviz/agv_1.rviz`에는 팔 RobotModel 3개가 포함된다. 소스의 설정을 저장한 후 다시 빌드해야
일반 설치 방식의 launch가 최신 파일을 읽는다. Save As 대화상자가 응답하지 않으면
`ros2 run rviz2 rviz2 -d "$HOME/dev/DT_Index/ros2_ws/src/dt_description/rviz/agv_1.rviz"`로 소스를 열고 Ctrl+S로 저장한다.

원본 모델: FAIR-INNOVATION/frcobot_ros2의 fairino_description/urdf/fairino20_v6.urdf 및 대응 STL.
ROS용 사본의 메시 URI만 `package://dt_description/meshes/fairino20_v6/`로 변경했다.
STL은 Git LFS로 관리하며 Unity의 .asset/.prefab/.meta와 FR5 모델은 이 ROS 패키지에 포함하지 않는다.
