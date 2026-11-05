# INDEX · 로봇팔·AGV 협업 공정의 Unity–ROS2 디지털 트윈

방석 분류 라인 하나를 **Unity 6**로 시뮬레이션하고 **ROS2 Humble** 노드로 제어한다.
적재함의 방석을 무게로 정상·불량으로 나눠 파레트 2개에 5단으로 쌓고, 가득 찬 파레트는 AGV1이 최종 구역으로 끌고 간다.

- 설비: 로봇팔 3대(Fairino FR20), ㄷ자 컨베이어와 레인 2개, 무게 센서, 분기 게이트, 파레트 2개(예비 2개), AGV 1대, 적재함 방석 20개
- 기간: 2026.10.28 ~ 11.05 · 팀 INDEX 4명
- 기준 문서: SRS v3.4, WBS v3.7, QA 시트 v2.0

![RViz에서 본 공장 배치](ros2_ws/src/dt_description/tools/layout_preview.png)

## 공정 흐름

| 순서 | 단계 | 하는 일 |
|---|---|---|
| 1 | 투입 | 로봇팔1이 적재함 선반에서 방석을 꺼내 컨베이어에 올린다 |
| 2 | 무게 측정 | 무게 센서가 방석마다 한 번 id와 무게를 보고한다 |
| 3 | 판정·분기 | `factory_manager`가 0.80 ~ 1.20 kg이면 정상, 밖이면 불량으로 판정하고 분기 게이트가 레인을 나눈다 |
| 4 | 레인 끝 대기 | 레인 끝 칸 2개가 차면 컨베이어를, 메인+레인 방석이 15개면 투입을 멈춘다 |
| 5 | 적재 | 로봇팔2는 정상을 파레트1에, 로봇팔3은 불량을 파레트2에 1단부터 5단까지 쌓는다 |
| 6 | 운반·보충 | AGV1이 가득 찬 파레트를 최종 구역으로 끌고 가고(3초 뒤 삭제), 빈 자리는 예비 파레트가 채운다 |

## 구조

```
 Windows PC                                              Ubuntu 22.04 VM (브리지)
 Unity 6 + URP ── ROS-TCP-Connector ── TCP 10000 ── ROS-TCP-Endpoint (스레드 16) ── ROS2 노드
 장비 물리 동작·감지·화면                내 VM IP                                     공정 판단·장비 명령
                                                                                     RViz (표시 전용)
```

- **Unity**: 장비의 물리 동작과 감지 결과 보고, 제어 버튼·상태 패널. 스스로 판단하지 않는다.
- **ROS2**: 공정 상태(FSM) 판단과 장비 명령. 라인 명령은 `factory_manager`만 보낸다.
- 로봇팔 관절은 Unity IK로 움직이고, ROS2는 `/arm_N_pick_place` 서비스로 집을 곳·놓을 곳만 보낸다.
- Unity ROS Settings의 IP는 **각자 자기 VM IP**다. 그 설정 파일은 skip-worktree로 Git에 올리지 않는다 ([docs/SETUP.md](docs/SETUP.md)).

## 팀

| 역할 | 담당 | 맡은 장비 | 주요 작업 |
|---|---|---|---|
| A | 권오민 | 로봇팔1 · 투입, 컨베이어 | 적재함·방석, ㄷ자 컨베이어와 레인 끝, 무게 센서·분기 게이트(Unity), 상태 패널·카메라, 공장 건물·바닥 표시 |
| B | 김예은 | 로봇팔2 · 정상 적재, 공정 제어 | `factory_manager`, `factory_interfaces`, 고장 주입·경고 아이콘, 제어 버튼 배치 |
| C | 박준영 | AGV1 제어 | `dt_agv`(agv_1_controller), AGV 경로 yaml, 로봇팔 RViz 동기화 |
| D | 김찬중 | 로봇팔3 · 불량 적재, 연동·통합 | 팔 공통 pick·place(Unity IK), `dt_palletizer`, `dt_bringup`, `dt_description`, `dt_fake_unity`, AGV1·파레트·최종 구역 Unity 동작, ROS 연결, `Main` 씬 |

팔레타이징(`dt_palletizer`)은 11/03에 C에서 D로 옮겼다. 폴더·파일별 담당은 [docs/FOLDER_STRUCTURE.md](docs/FOLDER_STRUCTURE.md).

## 빠른 시작

처음이면 **[docs/SETUP.md](docs/SETUP.md)** 를 끝까지 따라 한다.

**1. VM: 빌드**
```bash
cd ~/dev/DT_Index/ros2_ws
vcs import src < dependencies.repos        # ROS-TCP-Endpoint (ROS2v0.7.0)
rosdep install --from-paths src --ignore-src -y
colcon build --symlink-install && source install/setup.bash
```
yaml만 고쳐도 `colcon build`를 다시 한다 (install 폴더로 복사되므로).

**2. VM: 실행**
```bash
ros2 launch dt_bringup factory.launch.py           # Endpoint + factory_manager + palletizer_1·2 + agv_1_controller
ros2 launch dt_description factory_display.launch.py   # (선택) 다른 터미널, RViz에 공장 배치·AGV1·로봇팔 3대
```
launch를 끌 때는 창을 닫지 말고 `Ctrl+C`로 끈다 (Endpoint가 포트 10000을 놓아 준다).

**3. Windows: Unity**
1. Unity Hub에서 `unity/INDEX_Twin`을 **6000.6.0f1**로 연다.
2. `Robotics > ROS Settings`의 ROS IP를 내 VM IP로, 포트 10000으로 맞춘다.
3. `Assets/_Project/Scenes/Main.unity`를 열고 Play.
4. 위쪽 배너에 `E-STOP : factory_manager started - press Clear`가 보이면 **[Clear] → [Start]**.

`factory_manager`는 E_STOP으로 시작하므로 처음 한 번 [Clear]를 눌러야 IDLE이 된다.

### 실행 방법별 정리

| 목적 | VM | Windows |
|---|---|---|
| 연결 확인 | `ros2 launch dt_bringup connect.launch.py` | `Scenes/Sandbox/ConnectionTest` 씬 Play |
| 전체 시연 | `ros2 launch dt_bringup factory.launch.py` | `Main` 씬 Play → [Clear] → [Start] |
| RViz 표시 | `ros2 launch dt_description factory_display.launch.py` (`arms:=false`, `test:=true` 선택) | 전체 시연과 같이 |
| Unity 없이 | `ros2 launch dt_bringup fake.launch.py` + `ros2 run dt_fake_unity fake_panel` | — |

`arms_display.launch.py`는 `factory_display`가 같이 띄우므로 따로 켜지 않는다.

## 조작

| 버튼 | 켜지는 때 | 하는 일 |
|---|---|---|
| **Start** | IDLE, ROS2 연결 정상 | 공정 시작 → RUNNING |
| **Emergency** | E_STOP이 아닐 때 | 모든 장비를 그 자리에서 정지 → E_STOP |
| **Clear** | E_STOP | 해제와 초기화 → IDLE |
| **Fix** (3초 누름) | ERROR | 고장 장비 조치 완료. 고장 장비 머리 위 경고 아이콘을 3초 눌러도 같다 |
| **Arm1 · Arm2 · Arm3 · Line** | RUNNING, ROS2 연결 정상 | 고장 주입 → ERROR. 고장 주입된 팔은 방석을 든 채 멈췄다가 조치 뒤 이어서 움직인다 |

- 상태 패널: `Tab`으로 열고 닫기, `1 ~ 4`로 MAIN · LINE · PALLETS · AGV1 탭, `Esc`로 닫기. 장비를 클릭하면 카메라가 그 장비로 간다.
- 카메라: `WASD` 이동, `Q / E` 회전, 마우스 휠 확대·축소.
- 위쪽 배너에 현재 상태와 원인이 영문으로 표시되고, 북쪽 벽 전광판에도 같은 값이 나온다.

## 공정 상태

| 상태 | 뜻 | 들어오는 조건 | 나가는 방법 |
|---|---|---|---|
| E_STOP | 정지 | 시작할 때, [Emergency], 통신 끊김 3초, 감시 시간 초과(무게 보고 15초, AGV1 30초) | [Clear] → IDLE |
| IDLE | 대기 | [Clear] | [Start] → RUNNING |
| RUNNING | 공정 진행 | [Start], 조치 완료 | 집기 실패·고장 주입 → ERROR |
| ERROR | 고장 조치 대기 | 집기 실패, [고장 주입] | [Fix] 3초 → RUNNING (실패한 집기는 한 번 다시 보냄) |

[Clear]로 해제하면 적재함 밖의 방석(벨트, 레인 끝, 그리퍼, 파레트, 최종 구역)을 모두 지우고, 두 파레트 자리에 빈 파레트를 놓고, AGV1을 대기 위치로 돌린다. 적재 수·운반 대기열·생산 통계는 0부터 다시 센다.

## ROS2 패키지

| 패키지 | 담당 | 노드 | 역할 |
|---|---|---|---|
| `factory_interfaces` | B | — | `PickPlace.srv` (집기·놓기 서비스) |
| `factory_manager` | B | `factory_manager` | 공정 상태(FSM), 무게 판정, 투입 조절, 파레트 적재 수와 운반 대기열, AGV1 명령, Heartbeat, 감시 시간 |
| `dt_palletizer` | D | `palletizer_1`, `palletizer_2` | 레인 끝 방석을 로봇팔2·3으로 파레트에 1 ~ 5단 적재 (같은 코드, 레인 파라미터) |
| `dt_agv` | C | `agv_1_controller` | `/agv_1_cmd`(P1_DOCK, P2_DOCK, FINAL, HOME) → 웨이포인트 `/agv_1_goal`, 연결 여부 `/agv_1_loaded` |
| `dt_bringup` | D | `ros_tcp_endpoint` | launch, 설정 yaml(`factory.yaml`, `line_coords.yaml`, `agv_waypoints.yaml`), 스레드 16개 Endpoint |
| `dt_description` | D (팔 RViz C) | `robot_state_publisher`, `factory_layout_markers` | AGV1·FR20 URDF, RViz 공장 배치 `/factory_layout` |
| `dt_fake_unity` | D | `fake_unity_node`, `fake_panel` 등 | Unity 없이 ROS 쪽을 시험하는 가짜 노드 |
| `ROS-TCP-Endpoint` | 외부 | — | `vcs import`로 받음, 직접 수정하지 않는다 |

## 주요 토픽·서비스

| 방향 | 이름 |
|---|---|
| Unity → ROS2 (버튼) | `/factory/start`, `/factory/emergency_stop`, `/factory/fault_clear`, `/factory/inject_fault` |
| Unity → ROS2 (장비 보고) | `/bin/count`, `/scale_1_weight`, `/conveyor_1_load`, `/conveyor_1_end_ok`·`_ng`, `/pallet_N_status`, `/agv_1_status` |
| ROS2 → Unity (명령·상태) | `/factory/state`, `/factory/stats`, `/conveyor/cmd`, `/diverter_1_cmd`, `/agv_1_goal` |
| ROS2 노드끼리 | `/pallet_N_placed`, `/pallet_N_count`, `/agv_1_cmd`, `/agv_1_loaded`, `/factory/fault`, `/factory/retry` |
| Heartbeat (1초, 3초 끊기면 정지) | `/factory/heartbeat_unity`, `/factory/heartbeat_ros` |
| RViz 표시 전용 (10 Hz) | `/arm_N_joint_states`, `/agv_1_joint_states`, `/tf`, `/factory_layout` |
| 서비스 (Unity가 서버) | `/arm_1_pick_place`, `/arm_2_pick_place`, `/arm_3_pick_place` |

이름 상수는 Unity `Scripts/ROS/TopicNames.cs` 한 곳에 둔다. 메시지 형식은 SRS 4.4절을 따른다.

## 설정값

코드를 고치지 않고 바꾼다. `factory.yaml`은 `factory_manager`와 `palletizer_1·2`가 같이 읽으므로 같은 값을 여러 섹션에 맞춰 적는다.

| 위치 | 값 |
|---|---|
| `dt_bringup/config/factory.yaml` | 정상 무게 0.80 ~ 1.20 kg, 레인 용량 15, 레인 끝 칸 2, 적재 기준 5, 집기 응답 제한 20초, 무게 보고 감시 15초, AGV1 감시 30초, Heartbeat 1초 / 3초 |
| `dt_bringup/config/agv_waypoints.yaml` | AGV1 정지 지점(HOME, P1_DOCK, P2_DOCK, FINAL)과 경로점, 도킹별 2 m 직진 이탈 경로 |
| `dt_bringup/config/line_coords.yaml` | 집는 자리, 파레트 자리, 로봇팔 베이스 좌표 (4명 합의 후 변경) |
| Unity Inspector | 적재함 20개(정상 14 : 불량 6), 컨베이어 0.8 m/s, 로봇팔 동작 제한 15초, 최종 구역 삭제 3초 |

## 테스트

판단 로직은 rclpy 없는 순수 Python 모듈로 나눠 pytest로 시험한다.

```bash
cd ~/dev/DT_Index/ros2_ws
colcon test --packages-select factory_manager dt_palletizer dt_agv dt_fake_unity
colcon test-result --verbose
```

통합 시험 항목은 QA 시트 v2.0(TC_DT_001 ~ 051, SRS TC-01 ~ 50)을 따른다.

## 폴더 구조

```
DT_Index/
├── README.md · COMMIT_RULES.md · CONVENTIONS.md
├── docs/                     SETUP.md, FOLDER_STRUCTURE.md, arm_model_fr20.md
├── scripts/                  setup-git-hooks.sh (클론 후 1회)
├── ros2_ws/
│   ├── dependencies.repos    외부 패키지 버전 고정
│   └── src/                  factory_interfaces, factory_manager, dt_palletizer, dt_agv,
│                             dt_bringup, dt_description, dt_fake_unity
└── unity/INDEX_Twin/         Unity 6000.6.0f1 + URP 17.6
    └── Assets/
        ├── _Project/         Scripts(ROS, Arms, Line, AGV, UI, Common), Prefabs, Scenes(Main, Sandbox)
        ├── RosMessages/      자동 생성 C# 메시지 (직접 수정 금지)
        ├── URDF/             Fairino FR20 URDF + 메시 (LFS)
        └── Conveyor System/  구매 에셋 (Private 레포라 포함)
```

## 문서

| 문서 | 내용 |
|---|---|
| [docs/SETUP.md](docs/SETUP.md) | 개발 환경 초기 세팅 (Windows + Unity / VM + ROS2) |
| [docs/FOLDER_STRUCTURE.md](docs/FOLDER_STRUCTURE.md) | 폴더·파일별 역할, 담당, 토픽 |
| [docs/arm_model_fr20.md](docs/arm_model_fr20.md) | 로봇팔 모델을 FR5에서 FR20으로 바꾼 이유와 프리팹 설정 |
| [COMMIT_RULES.md](COMMIT_RULES.md) | 브랜치·커밋·PR 규칙 (`type(scope): 내용`) |
| [CONVENTIONS.md](CONVENTIONS.md) | 명명 규칙, Unity 작업 규칙, 파일 소유자 |
| 각 패키지 `README.md` | 노드, 토픽, 실행·확인 방법 |

SRS v3.4, WBS v3.7, QA 시트 v2.0은 저장소 밖에서 관리한다.

## 일정

| 마일스톤 | 날짜 | 기준 |
|---|---|---|
| M1 연결 | 2026.10.29 | Unity–ROS2 연결, [Start]로 컨베이어 가동·정지 |
| 시나리오 변경 | 2026.10.30 | 방석 무게 분류 라인으로 변경, 로봇팔 Unity IK 확정 |
| M2 MVP | 2026.11.03 | 정상·불량 방석이 각 파레트에 쌓이고 AGV1이 최종 구역으로 운반 |
| M3 리허설 | 2026.11.04 | 방석 40개 연속 운전, 비상정지·통신 끊김·고장 주입 복구, RViz |
| 최종 시연 | 2026.11.05 | |

## 알려진 한계

- 분기 게이트는 컨베이어가 멈춰도 갈림길 위 방석을 옆으로 계속 민다.
- 로봇팔이 방석을 놓은 뒤 물러나는 동작에는 시간 제한이 없다.
- 가짜 Unity(`fake_unity_node`)는 Heartbeat, `/bin/count`, 팔 서비스만 흉내 내고 라인·파레트·AGV는 뼈대다.
