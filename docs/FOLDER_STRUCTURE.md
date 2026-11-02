# 폴더 구조와 파일 역할

> 기준: SRS v3.1 (2026.10.30) · WBS v3.1 역할 분담 (A 권오민, B 김예은, C 박준영, D 김찬중)
> 폴더·파일을 추가하거나 역할을 바꾸면 **같은 PR에서 이 문서도 수정**한다.
> ⚠ 표시는 아직 결정되지 않은 부분이다 (결정 후 갱신).
> 파일 표의 **상태** 열: `있음` = develop에 이미 있음, `수정` = 있지만 v3.1에 맞게 고쳐야 함, `예정` = 담당자가 새로 추가, `삭제` = v2.2 전용이라 정리 대상(6절).

---

## 0. 전체 트리

```
DT_Index/
├── README.md                     # 프로젝트 소개, 실행 3단계, 문서 링크
├── COMMIT_RULES.md               # 브랜치·커밋·PR 규칙 (type(scope): 내용)
├── CONVENTIONS.md                # 명명 규칙, Unity 작업 규칙, 파일 소유자
├── .gitignore / .gitattributes   # ROS build·Unity Library 제외, LFS 대상 지정
├── .githooks/                    # commit-msg, pre-commit + Git LFS 훅
├── .github/pull_request_template.md
├── scripts/setup-git-hooks.sh    # 클론 후 1회 실행: 훅 경로 등록, ROS 설정 skip-worktree
├── docs/                         # SRS, WBS, 설계 문서, 좌표표, 이 문서
│
├── ros2_ws/src/                  # ── ROS2 (Ubuntu 22.04 + Humble) ──
│   ├── factory_interfaces/       # [B] 커스텀 srv (PickPlace.srv 하나)
│   ├── factory_manager/          # [B] 공정 FSM, 무게 판정, 투입 조절, 운반 대기열, AGV1 명령
│   ├── dt_palletizer/            # [D] palletizer_1·_2 (같은 코드, 레인 파라미터)
│   ├── dt_agv/                   # [C] agv_1_controller (목적지 → 웨이포인트, 연결·분리)
│   ├── dt_fake_unity/            # [C] Unity 없이 시험하는 가짜 노드 3종 (Unity, palletizer, agv_1_controller)
│   ├── dt_arm/                   # 사용 안 함 (10.30 Unity IK 결정, 1.3절) → 정리 대상
│   ├── dt_bringup/               # [C·D] launch + 설정 yaml
│   ├── dt_description/           # [D] AGV1 URDF(단순화)와 공장 배치 RViz 표시
│   └── ROS-TCP-Endpoint/         # 외부 패키지 (ROS2v0.7.0), vcs import, 직접 수정 금지
│
└── unity/INDEX_Twin/             # ── Unity 6 (6000.6.0f1) + URP (Windows) ──
    └── Assets/
        ├── _Project/             # 팀이 만든 모든 것
        │   ├── Scripts/  ROS/ Arms/ Line/ AGV/ UI/ Common/
        │   ├── Prefabs/  Arm/ Line/ AGV/ UI/
        │   ├── Materials/  Settings/
        │   └── Scenes/   Main.unity, Sandbox/
        ├── RosMessages/          # 자동 생성 C# 메시지, 직접 수정 금지
        ├── URDF/                 # Fairino URDF + 메시 (LFS)
        ├── Resources/            # ROSConnectionPrefab (각자 IP, skip-worktree)
        ├── Conveyor System/      # 구매 에셋 — 예외로 Git에 포함 (Private 레포, 2.7절)
        └── ThirdParty/           # 앞으로 사는 구매 에셋 (git에 올리지 않음, SRS 2.4)
```

**레인·파레트·팔 번호 대응** (헷갈리기 쉬우니 코드와 문서 모두 이 표를 따른다)

| 레인 | 판정 | 로봇팔 | palletizer | 파레트 | 레인 끝 토픽 |
|---|---|---|---|---|---|
| `ok` (진행 방향 왼쪽, 위) | 정상 | arm_2 | palletizer_1 | pallet_1 | `/conveyor_1_end_ok` |
| `ng` (진행 방향 오른쪽, 아래) | 불량 | arm_3 | palletizer_2 | pallet_2 | `/conveyor_1_end_ng` |

> v2.2와 **뜻이 바뀌었다**: v2.2에서 PALLET_1은 불량, PALLET_2는 출고였다. v3.1은 pallet_1 = 정상, pallet_2 = 불량.

---

## 1. ROS2 패키지 (`ros2_ws/src/`)

### 1.1 `factory_interfaces/` — [B] · scope `interfaces`
커스텀 타입은 `PickPlace.srv` 하나만 둔다. 필드가 여러 개인 공장 토픽은 JSON 문자열(`std_msgs/String`)로 보낸다 (SRS 4.4).

| 파일 | 상태 | 역할 |
|---|---|---|
| `srv/PickPlace.srv` | 있음 | 요청 `box_id`(string, 값은 방석 id `C001`), `pick_pose`, `place_pose` / 응답 `success`, `message`. `/arm_N_pick_place`에 사용. 팔1은 `box_id`를 비워 보내고 Unity가 집은 방석 id를 `message`로 돌려준다 |
| `msg/DetectEvent.msg` | 삭제 | v3.1에서 사용하지 않음 |
| `CMakeLists.txt`, `package.xml` | 수정 | DetectEvent 제거 시 함께 수정 |

> 이 패키지를 바꾸면 Unity 쪽 `RosMessages/`도 다시 생성해야 한다 → D에게 알림. 필드 이름 `box_id`는 이미 생성한 C# 클래스를 쓰기 위해 유지한다.

### 1.2 `factory_manager/` — [B] · scope `manager`
라인 전체의 **유일한 명령권자**. Unity 버튼도 여기로 요청만 보낸다. 판단 로직은 rclpy 없는 순수 Python 모듈로 나눠 단위 테스트한다.

| 파일 | 상태 | 역할 | 관련 |
|---|---|---|---|
| `factory_manager/factory_manager_node.py` | 수정 | 노드 본체. 토픽 구독·발행, `/arm_1_pick_place` 클라이언트, 1초 타이머(state·stats·heartbeat)를 연결하고 판단은 아래 모듈에 맡긴다 | 전체 |
| `factory_manager/fsm.py` | 수정 | 공정 상태 4개(IDLE, RUNNING, ERROR, E_STOP)와 전이표 (SRS 부록 A.2). `PALLET_FULL` 삭제. **실행하면 E_STOP(원인 `startup`)으로 시작**. E_STOP 원인(`button`, `heartbeat`, `timeout_weight`, `timeout_agv`, `startup`)과 고장 목록(먼저 난 순서) 관리 | FR-01, 16~19 |
| `factory_manager/line_rules.py` | 수정 | 투입 조건: 적재함에 방석 있음 + 직전 방석 무게 보고 받음 + `main+ok`와 `main+ng`가 모두 레인 용량 미만 (`/conveyor_1_load`) | FR-02, 04 |
| `factory_manager/grading.py` | 예정 | 무게 판정. 정상 범위(하한·상한 포함, yaml) 안이면 `ok`, 밖이면 `ng`. JSON 오류는 `ng` + 경고 | FR-07 |
| `factory_manager/pallet_queue.py` | 예정 | 파레트별 적재 수(`/pallet_N_placed`로 +1), 적재 기준에 **닿는 순간 한 번만** 운반 대기열에 넣기, `/pallet_N_status`가 READY로 **바뀔 때만** 0으로 | FR-12, 15 |
| `factory_manager/agv_dispatch.py` | 예정 | 운반 중 파레트 기록, `/agv_1_loaded`의 바뀜을 보고 `PN_DOCK` → `FINAL` → 다음 `PN_DOCK` 또는 `HOME` 결정 (SRS 부록 A.6). 출동 조건은 RUNNING이 될 때, 대기열·자리 상태·적재 여부가 바뀔 때, 1초마다 검사 | FR-13 |
| `factory_manager/watchdog.py` | 예정 | 감시 시간 2개. 팔1 투입 성공 뒤 15초 안에 그 방석의 무게 보고가 없으면 `timeout_weight`, PN_DOCK·FINAL 뒤 30초 안에 `/agv_1_loaded`가 바뀌지 않으면 `timeout_agv` → E_STOP. RUNNING 동안만 세고 RUNNING으로 돌아오면 처음부터 | FR-02, 13 |
| `factory_manager/stats.py` | 예정 | `/factory/stats` JSON (SRS 부록 A.4). `fault`(ERROR일 때), `estop`(E_STOP 원인) 포함. 받은 값은 그대로 담고 따로 세지 않는다 | FR-20 |
| `factory_manager/heartbeat_monitor.py` | 예정 | `/factory/heartbeat_ros` 1초 발행, `/factory/heartbeat_unity`가 3초 끊기면 E_STOP | FR-17, NFR-04 |
| `test/test_fsm.py`, `test/test_line_rules.py` | 수정 | v3.1 상태·투입 조건으로 갱신 | NFR-03 |
| `test/test_grading.py`, `test/test_pallet_queue.py`, `test/test_agv_dispatch.py`, `test/test_watchdog.py` | 예정 | 경계값(TC-09), 대기열 중복 방지(TC-15·16), 명령 순서(TC-17·41), 감시 시간(TC-40·42) | |

**토픽 요약**
- 구독: `/factory/start`, `/factory/emergency_stop`, `/factory/fault`, `/factory/fault_clear`, `/factory/inject_fault`, `/factory/heartbeat_unity`, `/bin/count`, `/scale_1_weight`, `/conveyor_1_load`, `/pallet_1_placed`, `/pallet_2_placed`, `/pallet_1_status`, `/pallet_2_status`, `/agv_1_loaded`
- 발행: `/factory/state`, `/factory/stats`, `/factory/heartbeat_ros`, `/factory/retry`, `/conveyor/cmd`, `/diverter_1_cmd`, `/pallet_1_count`, `/pallet_2_count`, `/agv_1_cmd`
- 서비스 호출: `/arm_1_pick_place` (팔2·3은 palletizer가 호출)

### 1.3 `dt_arm/` — 사용 안 함 · scope `arm`
**10.30 결정(WBS 1.4): 로봇팔은 Unity IK로 움직인다.** `/arm_N_pick_place` 서비스 서버는 Unity `ArmPickPlaceServer.cs`(2.2절)가 맡고, 이 패키지는 쓰지 않는다. 호출하는 쪽(factory_manager는 팔1, palletizer는 팔2·3)과 서비스 계약은 그대로다.

| 파일 | 상태 | 역할 |
|---|---|---|
| `dt_arm/` 전체 | 삭제 | launch에서 빼고 6절 정리 PR에서 지운다 |

### 1.4 `dt_palletizer/` — [D] · scope `palletizer` (11/03 C → D)
같은 코드를 레인 파라미터(`ok`/`ng`)만 바꿔 `palletizer_1`, `palletizer_2` 두 개로 띄운다. **적재 수는 직접 세지 않고** factory_manager가 보낸 `/pallet_N_count`를 쓴다.

| 파일 | 상태 | 역할 | 관련 |
|---|---|---|---|
| `dt_palletizer/palletizer_node.py` | 있음 | `/conveyor_1_end_<lane>`으로 받은 방석 id를 도착 순서대로 기억. RUNNING이고 `/pallet_N_status`가 READY이며 `/pallet_N_count`가 적재 기준 미만이면 `/arm_(N+1)_pick_place` 호출. 성공하면 `/pallet_N_placed`(방석 id) 발행 후 count가 c+1로 바뀐 것을 보고 다음 방석 처리. 실패(Unity 팔 15초 자체 중단 포함)·20초 무응답이면 `/factory/fault` 발행 후 요청을 들고 기다리고 `/factory/retry`를 받으면 RUNNING에서 한 번 다시 보냄. `/factory/state`가 E_STOP → IDLE로 바뀌면 들고 있던 요청·retry 기억·방석 순서를 비움 | FR-09~11, 16, 18 |
| `dt_palletizer/stacker.py` | 있음 | 판단 모듈 (rclpy 없음): 대기열·호출 조건·응답 처리(성공·E_STOP·busy·no_cushion·실패)·20초 시간 초과·retry·해제 초기화. k단 놓기 높이 = 파레트 자리 상면 + (k−1) × 방석 높이 (1×5 수직) | FR-09~11, 16, 18, NFR-06 |
| `test/test_stacker.py` | 있음 | 판단 단위 테스트 (스펙 시험 1~7) | |

### 1.5 `dt_agv/` — [C] · scope `agv`

| 파일 | 상태 | 역할 | 관련 |
|---|---|---|---|
| `dt_agv/agv_1_controller_node.py` | 동작 | `/agv_1_cmd`(P1_DOCK, P2_DOCK, FINAL, HOME)를 받아 일방통행 경로의 웨이포인트를 `/agv_1_goal`(JSON)로 순서대로 보내고, `/agv_1_status`(MOVING, ARRIVED, ATTACHED, DETACHED)를 보고 도킹 위치에서 연결, 최종 구역에서 분리. 연결 여부를 `/agv_1_loaded`(Bool, 바뀔 때 + 1초)로 보고. 적재 여부와 맞지 않는 명령은 무시 + 경고. `/factory/state`가 E_STOP → IDLE로 바뀌면 경로를 버리고 현재 위치를 HOME으로 둔 뒤 `/agv_1_loaded(false)`. 판단은 `route.py`(rclpy 없음) | FR-13, 16, A.6 |
| `dt_agv/cycle_timer.py` | 안 만듦 | AGV 사이클 시간 기록은 SRS대로 factory_manager(`agv_dispatch.py`)가 PN_DOCK 시각부터 잼 (3.7 ⑥에서 완료) | SRS 6장 |
| 웨이포인트 | 수정 | `dt_bringup/config/agv_waypoints.yaml` (HOME, P1_DOCK, P2_DOCK, FINAL과 경로점) | |

### 1.6 `dt_fake_unity/` — [C] · scope `fake`
Unity 없이 ROS2 쪽만 시험하기 위한 노드 (WBS 2.4, 3.13). v3.1에서 **가짜 노드 3종**: 가짜 Unity, 가짜 palletizer(`/pallet_N_placed`), 가짜 agv_1_controller(`/agv_1_loaded`). factory_manager 단독 시험(TC-15~18, TC-40~42)에 쓴다.

| 파일 | 상태 | 역할 |
|---|---|---|
| `dt_fake_unity/fake_unity_node.py` | 수정 | Unity가 보내야 할 것을 흉내: `/bin/count`, `/scale_1_weight`, `/conveyor_1_load`, `/conveyor_1_end_ok·_ng`, `/pallet_N_status`, `/factory/heartbeat_unity`. `/arm_1~3_pick_place` 서버(E_STOP이면 `message=E_STOP`). AGV는 agv_1_controller를 쓰므로 `/agv_1_goal`을 받아 `/agv_1_status`를 흉내 |
| `dt_fake_unity/fake_scene.py` | 수정 | 가짜 장면 상태 (적재함, 메인 라인, 레인 2개, 파레트 자리 2개, 예비 파레트, AGV). 순수 Python |
| `dt_fake_unity/fake_palletizer.py`, `dt_fake_unity/fake_agv_controller.py` | 예정 | 진짜 palletizer·agv_1_controller 대신 띄워 factory_manager만 시험. 일부러 응답을 안 보내 감시 시간 초과를 만들 수 있게 |
| `dt_fake_unity/fake_panel.py` | 수정 | 키 입력으로 버튼 흉내: s=시작, e=비상정지·해제, f=고장 주입, c=조치 완료(`fault_clear`) |
| `test/test_fake_scene.py` | 수정 | v3.1 장면으로 갱신 |
| `config/fake_scenario.yaml` | 수정 | 방석 순서·무게, 동작 시간, 실패를 일부러 낼 팔 번호 (`dt_bringup/config/`에 있음) |

### 1.7 `dt_bringup/` — [C·D] · scope `bringup`
실행과 **코드 수정 없이 바꾸는 설정값**(NFR-09)을 모은다. launch는 C, 좌표표는 D가 정리하고 4명이 합의한다.

| 파일 | 상태 | 역할 |
|---|---|---|
| `launch/connect.launch.py` | 있음 | 연결 확인용: Endpoint만 실행 |
| `launch/_common.py` | 수정 | factory·fake launch가 함께 쓰는 제어 노드 목록 (palletizer 2개 추가) |
| `launch/factory.launch.py` | 수정 | 시연용: Endpoint(10000) + factory_manager + palletizer_1·_2 + agv_1_controller. 팔 노드는 없다(Unity IK). **명령 1줄로 시작** (NFR-10) |
| `launch/fake.launch.py` | 수정 | Endpoint 대신 fake_unity_node |
| `config/factory.yaml` | 수정 | factory_manager와 palletizer가 **같은 파일**을 읽음: 정상 무게 범위, 레인 용량(15), 적재 기준(5), 집기 응답 제한(20초), Heartbeat(1초·3초) |
| `config/line_coords.yaml` | 수정 | 라인 좌표표 (ROS 좌표, m·rad): 적재함 칸, 투입 위치, 무게 센서, 분기 게이트, 집는 자리 2곳, 팔 베이스 3개, 파레트 자리 2곳, 예비 파레트 자리 2곳, 최종 구역. **바꾸려면 4명 합의** |
| `config/agv_waypoints.yaml` | 수정 | AGV1 일방통행 루프 (대기 → 도킹 → 최종 구역 → 바깥 레인 → 대기) |
| `config/fake_scenario.yaml` | 수정 | 1.6 참고 |
| `scripts/tcp_endpoint_mt.py` | 완료 (11/02 D) | ROS-TCP-Endpoint를 **스레드 16개**로 실행 (factory·connect launch가 씀). 기본 `default_server_endpoint`는 스레드 1개라 팔 서비스 응답(6~15초)을 기다리는 동안 다른 토픽 전달이 모두 멈춤 → 비상정지 지연, Heartbeat 끊김 오판 |

> 외부 패키지 ROS-TCP-Endpoint는 `ros2_ws/dependencies.repos`에 버전(ROS2v0.7.0)을 고정하고 `vcs import src < dependencies.repos`로 받는다. 저장소에는 올리지 않는다. 실행은 위 `tcp_endpoint_mt.py`로 한다.

### 1.8 `dt_description/` — [D] · scope `bringup`
Unity AGV1을 RViz에서 모델로 보이게 하고 공장 배치를 같은 좌표로 그린다. 주행은 Unity가 하고 여기는 표시만 한다 (FR-13 확인·시연용).

| 파일 | 상태 | 역할 |
|---|---|---|
| `urdf/agv_1.urdf` | 완료 (11/03 D) | robot_boxy를 상자·원기둥으로 단순화 (구매 에셋 메시는 쓰지 않음). 치수는 robot_boxy.fbx 뼈대에서 잼: 바퀴 간격 좌우 0.368·앞뒤 0.274 m, 높이 0.109. 바퀴 관절 이름 = Unity `bone_wheel_*` |
| `launch/agv_1_display.launch.py` | 완료 (11/03 D) | robot_state_publisher(`/agv_1_joint_states` → 바퀴 TF, `/agv_1_description`) + RViz. `test:=true`면 Unity 없이 HOME에 고정해 모델만 확인 |
| `rviz/agv_1.rviz` | 완료 (11/03 D) | Fixed Frame `map`, Grid, TF, Factory Layout, RobotModel |
| `scripts/factory_layout_markers.py` | 완료 (11/03 D) | 컨베이어·파레트 자리·예비 자리·최종 구역·집는 자리·팔·적재함·무게 센서·분기·AGV 정지 지점과 경로 → `/factory_layout` (MarkerArray) |
| `config/factory_layout.yaml` | 완료 (11/03 D) | 컨베이어 칸 위치 (Unity Main에서 뽑음, 직접 고치지 않음) |
| `tools/export_unity_layout.py` | 완료 (11/03 D) | Main.unity → `factory_layout.yaml`. Main에서 컨베이어를 옮기면 다시 실행 |

---

## 2. Unity (`unity/INDEX_Twin/Assets/`)

### 2.1 `_Project/Scripts/ROS/` — [D] · scope `unity-ros`
ROS 통신은 **이 폴더에서만** 한다. 다른 스크립트는 여기 이벤트를 구독하거나 여기 메서드를 부른다.

| 파일 | 상태 | 역할 | 관련 |
|---|---|---|---|
| `RosBridge.cs` | 수정 | `ROSConnection` 초기화, 연결 상태. 제어 패널 발행(`/factory/start`, `/factory/emergency_stop`, `/factory/fault_clear`), `/factory/state` → `FactoryStateChanged`, `/factory/stats` → `StatsReceived`·`CurrentFault` (상태 패널은 이 이벤트를 구독). 통신 끊김: `RosLost`·`Healthy`·`RosLostChanged` (HeartbeatLink가 정함) | NFR-08, FR-17 |
| `TopicNames.cs` | 수정 | 토픽 이름 상수. SRS 4.4 v3.1 표로 교체 (`detect_*`, `box_count`, `pallet_N_reset`, `arm_N_joint_states` 삭제, 새 토픽 추가) | NFR-08 |
| `CoordConverter.cs` | 있음 | 좌표 변환 **유일한 곳**. `To<FLU>()`, `From<FLU>()` | SRS 4.4 |
| `HeartbeatLink.cs` | 완료 (11/02) | `/factory/heartbeat_unity` 1초 발행. ROS2에서 3초 동안 아무것도(`/factory/heartbeat_ros`, `/factory/state`, `/factory/stats`) 못 받으면 끊김 → `RosBridge.SetRosLost`로 Unity 설비 E_STOP + 패널 "ROS2 LOST". 복구되면 `/factory/emergency_stop` true 1번 → [Clear]로 해제. 판정은 LateUpdate(에디터 멈춤 오판 방지). RosBridge가 Play 때 자동으로 붙임 (씬 수정 없음) | FR-17, NFR-04 |
| `BinLink.cs`, `ConveyorLink.cs` | 수정 | 적재함·컨베이어 토픽 연결 (`/bin/count`, `/conveyor/cmd`, `/conveyor_1_load`) | FR-03~05 |
| `ConnectionTest.cs` | 수정 | 세팅 검증용. `/detect_1_event` 발행 부분 삭제 | |
| `LaneEndLink.cs` | 있음 | 레인 끝(Line/LaneEnd.cs) 1번 칸에 방석이 들어올 때마다 `/conveyor_1_end_ok`·`_ng`로 id 1회. 11/02: ROS와 끊겨 있으면 모아 두었다가 다시 붙으면 보냄(그때도 집는 자리에 있는 방석만), 발행 등록이 사라졌으면 다시 등록. E_STOP → IDLE에서 레인 끝 방석 비움 | FR-09, 16 |
| `ArmPickPlaceServer.cs` | 있음 | `/arm_N_pick_place` 서비스 서버 (팔마다 1개, `armId`로 서비스 이름 결정). 요청 좌표를 Unity로 바꿔 `Arms/ArmPickPlace.Run()` 호출, 결과를 응답. E_STOP이면 진행 중 요청에 `success=false, message=E_STOP`, E_STOP → IDLE이면 팔 초기화 | FR-02, 10, 11, 16, 18 |
| `AgvLink.cs` | 있음 | `/agv_1_goal`(JSON: `{"wp"}` 또는 `{"wp","x","y","yaw"}`, `{"hitch"}`) → AgvWaypointDriver·AgvHitch, `/agv_1_status`(JSON `{"state","wp"}`) 바뀔 때. E_STOP이면 정지, 해제 시 끌던 파레트·최종 구역 파레트 삭제 + HOME | FR-13, 14, 16 |
| `PalletSlotLink.cs` | 있음 | `/pallet_N_status` 바뀔 때 + 1초. E_STOP이면 보충 정지, 해제 시 빈 자리에 빈 파레트 바로 놓기 | FR-15, 16 |

### 2.2 `_Project/Scripts/Arms/` — [D] 공통 · scope `unity-arm`
팔 3대가 같은 컴포넌트를 쓰고 **Unity IK로 움직인다** (10.30 확정, `/arm_N_joint_states`는 없음). 팔별 배치·좌표는 각 팔 담당: **팔1 A, 팔2 B, 팔3 D**.

| 파일 | 상태 | 역할 |
|---|---|---|
| `ArmRig.cs` | 있음 | 팔 1대의 공통 컴포넌트. `armId`(1~3), ArticulationBody 관절 6개 참조 |
| `KinematicGripper.cs` | 있음 | 집기 단순화: 방석을 Kinematic으로 바꾸고 그리퍼에 부착, 놓을 때 해제 (SRS 2.5) |
| `ArmPickPlace.cs` | 있음 | 집기·놓기 동작 (ROS 모름). 방석 찾기(box_id, 팔1은 pick 위치 근처) → 관절값 4개 미리 계산 → 집기 위 → 집기 → 붙이기 → 집기 위 → 놓기 위 → 놓기 → 떼기 → 놓기 위 → 응답 → 홈. 성공이면 `message`=방석 id. 닿지 않으면 움직이지 않고 `ik_failed:<단계>`. **15초(Inspector) 안에 못 마치면 스스로 멈추고 방석을 집기 좌표에 되돌린 뒤 `success=false`** (`timeout`). `Picked`·`Placed` 이벤트 (적재함 수 갱신 등) |
| `ArmIk.cs` | 있음 | 6축 IK. 관절 0 자세의 앵커 축으로 정기구학(PoE), 감쇠 최소제곱으로 목표 ToolPoint 위치·방향(공구 아래)을 푼다. 관절 한계 안, 시작값 근처 해 |
| `ArmPickPlaceServer.cs` | 이동 | ROS 통신이라 `Scripts/ROS/`로 옮김 (2.1) |
| `ArmIkSolver.cs` | 이름 변경 | `ArmIk.cs` |

### 2.3 `_Project/Scripts/Line/` — A·B · scope `unity-line`
방석이 적재함에서 레인 끝 집는 자리까지 가는 설비. **무게 센서와 분기 게이트(B)도 이 폴더**에 둔다.

| 파일 | 담당 | 상태 | 역할 | 관련 |
|---|---|---|---|---|
| `Box.cs` | A | 수정 | 방석 데이터: id(`C001`), 무게(kg), 구분(정상·불량), 현재 위치. ⚠ 클래스 이름을 `Cushion`으로 바꿀지 결정 필요 (바꾸면 프리팹 참조 확인) | SRS 6장 |
| `BoxBin.cs` | A | 수정 | 적재함 20개(정상 14·불량 6, 순서 랜덤), 무게는 부록 A.5 범위에서 랜덤(경계 0.05 kg 이내 금지), id 순서 부여, 비면 다시 채움, `/bin/count` | FR-02, 05 |
| `Conveyor.cs` | A | 수정 | 메인 라인 + 레인 2개, `/conveyor/cmd`로 0.2 m/s 가동·정지(0.5초 이내). 메인·레인별 방석 수를 `/conveyor_1_load`(JSON, 바뀔 때 + 1초) | FR-03, 04 |
| `LaneEnd.cs` | A | 예정 | 레인 끝 스토퍼와 집는 자리. 방석이 집는 자리에 들어올 때마다 `/conveyor_1_end_ok` 또는 `_ng`로 id 1회, 뒤 방석은 줄 서기 | FR-09 |
| `WeightScale.cs` | B | 예정 | 무게 센서 구간(Trigger). 방석당 1회 `/scale_1_weight`(`{"id","kg"}`, 소수 둘째 자리) | FR-06 |
| `Diverter.cs` | B | 예정 | 분기 게이트. `/diverter_1_cmd`를 방석 id별로 기억했다가 갈림길에서 `ok`는 왼쪽(위), `ng`는 오른쪽(아래)으로. 명령 없는 방석은 `ng` + 경고. 레인 이동은 Kinematic | FR-08 |

### 2.4 `_Project/Scripts/AGV/` — [D] · scope `unity-agv`
AGV1과 파레트 순환 (SRS 부록 A.6). 판단은 하지 않고 명령대로 움직이며 결과만 보고한다.

| 파일 | 상태 | 역할 | 관련 |
|---|---|---|---|
| `AgvWaypointDriver.cs` | 있음 | 웨이포인트 주행 (ROS 모름): 제자리 회전 → 직진 → 마지막 방향. goal에 좌표가 없으면 Inspector 정지 지점 표(HOME, P1_DOCK, P2_DOCK, FINAL)에서 찾음. 바퀴 회전은 차동 구동으로 표시(선택). ROS 연결은 `ROS/AgvLink.cs` | FR-13 |
| `AgvHitch.cs` | 있음 | 가장 가까운 파레트 자리의 파레트를 연결·분리 (강체 견인). 파레트 위 방석은 파레트 자식 + Kinematic으로 고정. 최종 구역 안에서 분리하면 FinalZone에 넘김 | FR-13 |
| `PalletSlot.cs` | 있음 | 파레트 자리 1개(1·2). 파레트가 **완전히 벗어나면** OUT, 예비 파레트 이동 중 REFILLING, 도착하면 READY. 발행은 `ROS/PalletSlotLink.cs` | FR-15 |
| `SpareRail.cs` | 있음 | 예비 레일. 자리가 비면 빈 파레트를 자리로 옮기고(3초) 예비 자리에 새로 생성 | FR-15 |
| `FinalZone.cs` | 있음 | 분리된 파레트와 방석을 3초(Inspector) 뒤 삭제. AGV 복귀를 기다리지 않음 | FR-14 |

### 2.5 `_Project/Scripts/UI/` — A·D · scope `unity-ui`

| 파일 | 담당 | 상태 | 역할 | 관련 |
|---|---|---|---|---|
| `ControlPanel.cs` | D | 수정 | [Start](IDLE일 때만), [Emergency](E_STOP 아닐 때, `/factory/emergency_stop` true), [Clear](E_STOP일 때만, false = 해제). 10/31: 비상정지·해제를 버튼 2개로 나눔. factory_manager가 E_STOP으로 시작하므로 처음 화면은 [Clear]만 켜짐. 버튼 상태는 `/factory/state`로 정함, ROS2 끊기거나 Heartbeat가 끊기면 모두 꺼짐. 11/02: [Start] 위에 연결 상태 글자 자동 생성 ("ROS2 connected · 상태" / 빨강 "ROS2 LOST"). 11/02: [Emergency]는 ROS 연결과 상관없이 누를 수 있고, factory_manager 답을 기다리지 않고 Unity 설비(컨베이어 포함)를 먼저 멈춘 뒤 ROS에 알림. manager E_STOP을 받을 때까지 1초마다 다시 보냄 (`RosBridge.EmergencyStopNow`). [Fix](ERROR이고 고장 장비가 있을 때만): 3초 누르면 `/factory/fault_clear`(장비 id, `/factory/stats`의 fault). 누르는 동안 버튼에 장비와 남은 시간 표시 | UI-02, FR-01, 16, 18 |
| `HoldButton.cs` | D | 있음 | 길게 누르기 (기본 3초). 중간에 떼거나 버튼이 꺼지면 처음부터. [Fix]에 붙음 | FR-18 |
| `FaultSelector.cs` | D | ⚠ 예정 | [고장 주입] 대상(arm_1~3, conveyor). 유지 여부는 팀 결정(SRS FR-19 Could) | FR-19 |
| `StatusPanel.cs` | A | 예정 | `/factory/state`, `/factory/stats`만 구독해 표시. 직접 세지 않음. 3초 못 받으면 회색. E_STOP이면 `estop` 원인 표시(비상정지, 통신 끊김, 무게 보고 시간 초과, AGV1 시간 초과, factory_manager 시작) | UI-03, FR-20 |
| `StatusBanner.cs` | A | 예정 | 공정 상태 색, ERROR(고장 장비)·E_STOP·통신 끊김 경고 배너 | FR-16~18 |

### 2.6 `_Project/Scripts/Common/`

| 파일 | 담당 | 상태 | 역할 |
|---|---|---|---|
| `LineSettings.cs` | A | 예정 | ScriptableObject: 적재함 수량·비율, 방석 무게 분포와 높이, 컨베이어 속도, 최종 구역 삭제 시간, 로봇팔 동작 제한 시간 15초 (NFR-09, Inspector) |
| `FactoryState.cs` | D | 예정 | `/factory/state` 문자열 ↔ enum (IDLE, RUNNING, ERROR, E_STOP) |
| `BufferZoneSensor.cs` | — | 있음 | v2.2 대기 칸용이지만 `Main.unity`가 `BufferZoneSensor.prefab`을 쓰고 있어 남김. `LaneEnd.cs`가 생기면 씬에서 빼고 지운다 |

### 2.7 그 밖의 Unity 폴더

| 폴더 | 내용 | 규칙 |
|---|---|---|
| `_Project/Prefabs/Arm/` | `Arm_Fairino` (팔 3대 공통) | 기능은 프리팹 안에서 완성 → 씬 충돌 방지 |
| `_Project/Prefabs/Line/` | `BoxBin`, `Box_Normal`, `Box_Oversize`(⚠ 방석 정상·불량으로 교체), `Conveyor`, `WeightScale`, `Diverter`, `LaneEnd` | |
| `_Project/Prefabs/AGV/` | `AGV_1`, `Pallet`, `PalletSlot`, `SpareRail`, `FinalZone` | |
| `_Project/Prefabs/UI/` | `ControlPanel`, `StatusPanel` | |
| `_Project/Settings/` | `LineSettings.asset`, 물리 재질, URP 설정 | |
| `_Project/Models/Cushion/` | 방석 겉모양 메시(몸통·파이핑)와 천 짜임 텍스처 (11/02 D) | 직접 고치지 않는다. `Editor/CushionVisualBuilder.cs` (메뉴 INDEX > Build Cushion Visual)로 다시 만든다 → `Prefabs/Line/CushionVisual.prefab`. Cushion·Cushion 2 프리팹은 회색 큐브를 끄고 이것을 `Visual` 자식으로 씀 (충돌 상자·무게는 그대로) |
| `_Project/Scenes/Main.unity` | 최종 씬 | **D만 수정** |
| `_Project/Scenes/Sandbox/<이름>_*.unity` | 개인 시험 씬 | 각자 자유, Main에 영향 없음 |
| `RosMessages/` | `Robotics > Generate ROS Messages`로 만든 C# (FactoryInterfaces) | 직접 수정 금지, 재생성만 |
| `URDF/` | Fairino URDF + 메시 | LFS |
| `Resources/ROSConnectionPrefab.prefab` | ROS IP 설정 | 각자 VM IP, skip-worktree로 커밋 금지 |
| `ThirdParty/` | 앞으로 사는 구매 에셋 | `.gitignore` — 팀원 간 별도 공유 (SRS 2.4). 빈 폴더는 Git에 안 올라가므로 필요할 때 각자 만든다 |
| `Conveyor System/` | 구매 에셋. 팀 씬 `DT_Main.unity`와 팀원이 고친 프리팹·재질이 안에 있음 | **예외로 Git에 포함한다** (10.30 결정, Private 레포). 옮기지 않는다. 레포를 Public으로 바꾸면 다시 검토 |

---

## 3. `docs/`

| 파일 | 상태 | 내용 |
|---|---|---|
| `SETUP.md` | 있음 | 개발 환경 초기 세팅 |
| `FOLDER_STRUCTURE.md` | 있음 | 이 문서 |
| `INDEX_SRS_v3.1.docx` | 예정 | 요구사항 명세 (LFS). 팀 검토(WBS 1.3) 후 추가 |
| `WBS_INDEX_DT_v3.1.xlsx` | 예정 | 작업 분해와 일정 |
| `design_v3.md` | 예정 | 역할·노드·토픽 설계 (SRS 8장: 4.4 표와 함께 고친다) |
| `line_coords.md` | 예정 | 라인 좌표표 (Unity 좌표와 ROS 좌표 둘 다) — `dt_bringup/config/line_coords.yaml`의 원본 |
| `tests/<TC-ID>.md` | 예정 | 시험 결과 (SRS 7.2) |

---

## 4. 방석 1개가 지나가는 파일 경로 (정상 방석)

```
[시작] ControlPanel.cs ──/factory/start──▶ factory_manager_node.py → fsm.py: IDLE→RUNNING
                                          └─/conveyor/cmd(true)──▶ Conveyor.cs
line_rules.py (투입 조건) ──/arm_1_pick_place──▶ 팔1 + BoxBin.cs (방석 id를 message로 응답)
WeightScale.cs ──/scale_1_weight──▶ grading.py: ok ──/diverter_1_cmd──▶ Diverter.cs → 정상 레인
LaneEnd.cs ──/conveyor_1_end_ok──▶ palletizer_1 (+ stacker.py: 호출 조건, k단 높이)
palletizer_1 ──/arm_2_pick_place──▶ 팔2 → 파레트1 k단 ──/pallet_1_placed──▶ pallet_queue.py ──/pallet_1_count──▶ palletizer_1
pallet_queue.py: 5개 → 운반 대기열 → agv_dispatch.py ──/agv_1_cmd(P1_DOCK)──▶ agv_1_controller_node.py
agv_1_controller ──/agv_1_goal──▶ AgvWaypointDriver.cs + AgvHitch.cs ──/agv_1_status──▶ agv_1_controller ──/agv_1_loaded(true)──▶ agv_dispatch.py
agv_dispatch.py ──/agv_1_cmd(FINAL)──▶ … 분리 → FinalZone.cs: 3초 뒤 삭제 / PalletSlot.cs: OUT → REFILLING → READY (SpareRail.cs)
pallet_queue.py: READY로 바뀜 → 적재 수 0 ──/pallet_1_count(0)──▶ palletizer_1: 다음 방석은 1단
stats.py ──/factory/stats──▶ StatusPanel.cs
```

---

## 5. 열려 있는 결정 (⚠)

| 항목 | 결정되면 바뀌는 곳 |
|---|---|
| `Box` → `Cushion` 이름 변경 | `Scripts/Line/`, 프리팹, `PickPlace.srv`는 유지 |
| AGV 사이클 시간 기록 위치 | `factory_manager/agv_dispatch.py` 또는 `dt_agv/cycle_timer.py` |
| [고장 주입] 유지 여부 | `FaultSelector.cs`, `fake_panel.py` |

---

## 6. v2.2 전용이라 정리할 것

별도 PR(`chore/remove-v22-equipment`)에서 코드 담당자와 합의 후 지운다. 이 문서 PR에서는 지우지 않는다.

| 대상 | 이유 |
|---|---|
| `TopicNames.cs`의 `ArmJointStates` | SRS v3.1에서 `/arm_N_joint_states` 삭제 |
| `factory_interfaces/msg/DetectEvent.msg`, `RosMessages/`의 DetectEvent | `/detect_1_event`, `/detect_2_event` 삭제 |
| `ros2_ws/src/dt_arm/` | Unity IK 결정으로 ROS 팔 노드 불필요 |
| `TopicNames.cs`의 `Pallet1Reset`, `Pallet2Reset`, `ConveyorBoxCount`, `Detect1Event`, `Detect2Event` | v3.1 토픽 표에 없음 |
| `fsm.py`의 `PALLET_FULL`, `line_rules.py`의 `reject_wait`·`end_wait` 정지 규칙 | 파레트가 차도 라인은 계속 돈다 |
| `Common/DropParcel.cs` | 대기 칸 2개 삭제 (삭제함). `BufferZoneSensor`는 `Main.unity`가 쓰고 있어 LaneEnd 이후로 미룸 |
| `Scenes/Sandbox/BoxTest.unity` | 지워진 BoxBin 프리팹을 참조하던 연습 씬 (삭제함) |
| 팔4, 출고 팔레트(12칸), [팔레트 정리] 버튼 관련 코드 | 설비 삭제 |
| 미머지 브랜치 `feature/bringup-launch-params`, `feature/bringup-line-coords`, `feature/palletizer-layout` | 머지하거나 v3.1 기준으로 다시 작업. `feature/palletizer-layout`(12칸 2×2×3단)은 11/03 `stacker.py`로 대체되어 삭제 대상 |
