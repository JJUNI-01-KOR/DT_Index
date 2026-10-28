# 폴더 구조와 파일 역할

> 기준: SRS v2.0 (2026.10.28) · 역할 A~D 분담표
> 폴더·파일을 추가하거나 역할을 바꾸면 **같은 PR에서 이 문서도 수정**한다.
> ⚠ 표시는 아직 결정되지 않은 부분이다 (결정 후 갱신).
> chore #1 시점에는 각 ROS 패키지가 **노드 1개짜리 뼈대**로만 들어 있다. 아래 "예정" 모듈은 담당자가 추가한다.

---

## 0. 전체 트리

```
DT_Index/
├── README.md                     # 프로젝트 소개, 실행 3단계, 문서 링크
├── COMMIT_RULES.md               # 커밋 메시지 규칙 (type(scope): 내용)
├── CONVENTIONS.md                # 브랜치·PR·명명 규칙, Unity 씬 규칙
├── .gitignore / .gitattributes   # ROS build·Unity Library 제외, LFS 대상 지정
├── .githooks/                    # commit-msg, pre-commit 훅
├── .github/pull_request_template.md
├── scripts/setup-git-hooks.sh    # 클론 후 1회 실행: 훅 경로 등록
├── docs/                         # SRS, 시나리오, 좌표표, 이 문서
│
├── ros2_ws/src/                  # ── ROS2 (Ubuntu 22.04 + Humble) ──
│   ├── factory_interfaces/       # [B] 커스텀 msg·srv
│   ├── factory_manager/          # [B] 공정 FSM, 라인 명령권
│   ├── dt_arm/                   # [A] 로봇팔 공통 집기 서비스 (arm_N_controller)
│   ├── dt_agv/                   # [C] agv_1_controller
│   ├── dt_palletizer/            # [D] palletizer_node
│   ├── dt_fake_unity/            # [D] Unity 없이 테스트하는 가짜 노드
│   ├── dt_bringup/               # [D] launch + 설정 yaml
│   └── ROS-TCP-Endpoint/         # 외부 패키지 (ROS2v0.7.0), 직접 수정 금지
│
└── unity/INDEX_Twin/             # ── Unity 6 + URP (Windows) ──
    └── Assets/
        ├── _Project/             # 팀이 만든 모든 것
        │   ├── Scripts/  ROS/ Arms/ Line/ AGV/ UI/ Common/
        │   ├── Prefabs/
        │   ├── Settings/
        │   └── Scenes/
        ├── RosMessages/          # 자동 생성 C# 메시지, 직접 수정 금지
        ├── URDF/                 # Fairino URDF + 메시
        └── ThirdParty/           # 구매 에셋 (git에 올리지 않음)
```

패키지 이름 접두어(`factory_*` vs `dt_*`)는 ⚠ 통일 여부를 결정해야 한다. 이 문서는 SRS에 이미 나온 `factory_interfaces`, `factory_manager`는 그대로 두고 나머지를 `dt_*`로 적었다.

---

## 1. ROS2 패키지 (`ros2_ws/src/`)

### 1.1 `factory_interfaces/` — [B] · scope `interfaces`
커스텀 메시지 2개만 둔다. 나머지는 표준 타입을 쓴다 (SRS 4.4).

| 파일 | 역할 |
|---|---|
| `msg/DetectEvent.msg` | 칸 감지 이벤트. `box_id`(string, BOX_001), `zone`(string), `position`(geometry_msgs/Point, ROS 좌표). `/detect_1_event`, `/detect_2_event`에 사용 |
| `srv/PickPlace.srv` | 집기·놓기 명령. 요청 `box_id`, `pick_pose`, `place_pose` / 응답 `success`, `message`. `/arm_N_pick_place`에 사용 |
| `CMakeLists.txt` | `rosidl_generate_interfaces`로 msg·srv 빌드 (ament_cmake 패키지) |
| `package.xml` | 의존성: `geometry_msgs`, `rosidl_default_generators` |

> 이 패키지를 바꾸면 Unity 쪽 `RosMessages/`도 다시 생성해야 한다 → D에게 알림.

### 1.2 `factory_manager/` — [B] · scope `manager`
라인 전체의 **유일한 명령권자**. Unity 버튼도 여기로 요청만 보낸다.

| 파일 | 역할 | 관련 |
|---|---|---|
| `factory_manager/factory_manager_node.py` | 노드 본체. 토픽 구독·발행, 서비스 클라이언트, 타이머(1초 상태·통계 발행)를 연결하고 판단은 아래 모듈에 맡긴다 | 전체 |
| `factory_manager/fsm.py` | 공정 상태 5개(IDLE, RUNNING, PALLET_FULL, ERROR, E_STOP)와 전이표. ERROR 진입 전 상태 기억 → [조치 완료] 시 복귀. **rclpy 없이 순수 Python** → 단위 테스트 가능 | FR-01, 14~18 |
| `factory_manager/line_rules.py` | 라인 조건 판단. ① `/conveyor/box_count` ≥ 4면 투입 중지, ≤ 3이면 재개 ② `reject_wait`·`end_wait`면 컨베이어 정지, 칸이 비면 재가동 | FR-02, 04, 10 |
| `factory_manager/arm_dispatcher.py` | 로봇팔1~3에 `/arm_N_pick_place` 요청. 1: 적재함→컨베이어, 2: 불량 칸→PALLET_1, 3: 대기 칸(먼저 온 것)→AGV1. 실패 횟수 처리 | FR-02, 08, 11 |
| `factory_manager/stats.py` | 생산 통계 계산 후 `/factory/stats` JSON 생성. `pallet_2`는 palletizer에게 받은 값 그대로. 투입 = 정상 + 불량 + 라인 위 | FR-19, SRS 6장 |
| `factory_manager/heartbeat_monitor.py` | `/factory/heartbeat_ros` 1초 발행, `/factory/heartbeat_unity` 수신이 3초 끊기면 E_STOP 요청 | FR-17, NFR-04 |
| `test/test_fsm.py`, `test/test_line_rules.py` | 상태 전이·투입 조건 단위 테스트 (`colcon test`) | NFR-03 |
| `setup.py`, `package.xml` | 실행 파일 등록 (`factory_manager_node`) | |

**토픽 요약**
- 구독: `/factory/start`, `/factory/emergency_stop`, `/pallet_1_reset`, `/pallet_2_reset`, `/factory/inject_fault`, `/factory/fault_clear`, `/bin/count`, `/conveyor/box_count`, `/detect_1_event`, `/detect_2_event`, `/agv_1_status`, `/pallet_2_count`, `/factory/heartbeat_unity`
- 발행: `/conveyor/cmd`, `/factory/state`, `/factory/stats`, `/factory/heartbeat_ros`, `/agv_1_cmd`
- 서비스 호출: `/arm_1~3_pick_place`
- AGV1 (10.28 결정): `/agv_1_status`가 LOADED면 `/agv_1_cmd` depart, `/pallet_2_count`가 늘면(출고 팔레트 적재 완료) `/agv_1_cmd` return. RUNNING이 아니면 보내지 않음

### 1.3 `dt_arm/` — [A] · scope `arm`
로봇팔 4대가 **같은 코드**를 쓰고 `arm_id` 파라미터만 다르게 4번 실행한다.
⚠ 10.30 결정: ROS2가 관절을 직접 제어하면 이 패키지가 서비스 서버가 되고, Unity IK로 가면 서버는 Unity `ArmPickPlaceServer.cs`가 맡고 이 패키지는 쓰지 않는다. **호출하는 쪽과 서비스 계약은 바뀌지 않는다.**

| 파일 | 역할 |
|---|---|
| `dt_arm/arm_controller_node.py` | `arm_N_controller` 노드. `/arm_N_pick_place` 서비스 서버, `/arm_N_joint_states` 30 Hz 발행 |
| `dt_arm/pick_sequence.py` | 집기 순서: 접근 → 하강 → 부착 → 상승 → 이동 → 하강 → 분리 → 복귀. 단계별 목표 자세 생성 |
| `dt_arm/kinematics.py` | Fairino 6축 역기구학 (목표 Pose → 관절각, 라디안). URDF 관절 이름·순서 그대로 |
| `config/arm_params.yaml` | 관절 속도, 접근 높이, 타임아웃 |

### 1.4 `dt_agv/` — [C] · scope `agv`

| 파일 | 역할 | 관련 |
|---|---|---|
| `dt_agv/agv_1_controller_node.py` | `/agv_1_cmd`(depart·return)를 받으면 `/agv_1_goal`(PoseStamped) 발행, `/agv_1_status`(IDLE·MOVING·ARRIVED·LOADED) 구독. 상차 구역 ↔ 출고 팔레트 구역 왕복 관리 | FR-12 |
| `dt_agv/cycle_timer.py` | 출발·도착·복귀 시각 기록 → 사이클 시간(초) 계산, 대시보드용으로 전달 | FR-19, SRS 6장 |
| 웨이포인트 | `dt_bringup/config/agv_waypoints.yaml`에서 읽음 (정지 위치 2곳) | |

AGV1 출발·복귀 (10.28 결정): factory_manager가 `/agv_1_cmd`(std_msgs/String: depart | return)로 명령한다. 명령권은 factory_manager 하나.

### 1.5 `dt_palletizer/` — [D] · scope `palletizer`

| 파일 | 역할 | 관련 |
|---|---|---|
| `dt_palletizer/palletizer_node.py` | `/agv_1_status`가 ARRIVED면 `/arm_4_pick_place`로 AGV 위 박스를 다음 칸에 적재. 적재 수(0~12)를 `/pallet_2_count`(Int32, 바뀔 때)로 factory_manager에 전달. `/pallet_2_reset` 받으면 0으로. `/factory/state`가 RUNNING이 아니면 대기 | FR-13~15 |
| `dt_palletizer/pallet_layout.py` | 출고 팔레트 12칸(2×2, 3단) 좌표 계산. 팔레트 원점 + 박스 크기 → n번째 칸 Pose. 적재 순서 고정 | FR-13, NFR-06 |

### 1.6 `dt_fake_unity/` — [D] · scope `fake`
Unity 없이 ROS2 쪽만 테스트하기 위한 노드. 10.29 ROS 테스트와 B·C의 단독 개발에 쓴다.

| 파일 | 역할 |
|---|---|
| `dt_fake_unity/fake_unity_node.py` | Unity가 보내야 할 것을 흉내: `/bin/count`, `/conveyor/box_count`, `/detect_1·2_event`, `/agv_1_status`(goal 받으면 MOVING → 8초 뒤 ARRIVED), `/factory/heartbeat_unity`. `/arm_1~4_pick_place` 서버(1초 뒤 success) |
| `dt_fake_unity/fake_panel.py` | 터미널 키 입력으로 버튼 흉내: s=동작, e=비상정지, r=팔레트 정리, f=고장 주입, c=조치 완료 |
| `config/fake_scenario.yaml` | 박스 순서(정상·불량), 이동 시간, 실패를 일부러 낼 팔 번호 |

### 1.7 `dt_bringup/` — [D] · scope `bringup`
실행과 **코드 수정 없이 바꾸는 설정값**(NFR-09)을 모은다.

| 파일 | 역할 |
|---|---|
| `launch/connect.launch.py` | 연결 확인용: Endpoint만 실행 (M1, 팀원 세팅 검증) |
| `launch/_common.py` | factory·fake launch가 함께 쓰는 제어 노드 목록 |
| `launch/factory.launch.py` | 실제 시연용: ROS-TCP-Endpoint(포트 10000) + factory_manager + palletizer + agv_1_controller (+ ⚠ arm_controller ×4). **명령 1줄로 시작** (NFR-10) |
| `launch/fake.launch.py` | Endpoint 대신 fake_unity_node를 띄움 |
| `config/factory.yaml` | 투입 중지 4개, 재개 3개, 팔레트 12개, Heartbeat 1초·3초 |
| `config/line_coords.yaml` | 라인 좌표표 (ROS 좌표): 적재함 칸, 투입 위치, 불량 칸, 대기 칸 2개, 팔 베이스 4개, AGV 정지 2곳, 불량 팔레트, 출고 팔레트 원점. **바꾸려면 4명 합의** |
| `config/agv_waypoints.yaml` | AGV1 왕복 경로 |
| `config/fake_scenario.yaml` | 가짜 Unity 박스 순서·이동 시간·실패 팔 |

> 외부 패키지 ROS-TCP-Endpoint는 `ros2_ws/dependencies.repos`에 버전(ROS2v0.7.0)을 고정하고 `vcs import src < dependencies.repos`로 받는다. 저장소에는 올리지 않는다.

---

## 2. Unity (`unity/INDEX_Twin/Assets/`)

### 2.1 `_Project/Scripts/ROS/` — [D] · scope `unity-ros`
ROS 통신은 **이 폴더에서만** 한다. 다른 스크립트는 여기 이벤트를 구독한다.

| 파일 | 역할 | 관련 |
|---|---|---|
| `RosBridge.cs` | `ROSConnection` 초기화, 모든 토픽 Publisher/Subscriber 등록, 받은 메시지를 C# 이벤트로 뿌림. 연결 상태를 UI에 알림 | NFR-08 |
| `CoordConverter.cs` | 좌표 변환 **유일한 곳**. Unity→ROS `To<FLU>()`, ROS→Unity `From<FLU>()` | SRS 4.4 |
| `ConnectionTest.cs` | 세팅 검증용. `/unity_ping`, `/detect_1_event`(커스텀 + 좌표 변환) 발행, `/conveyor/cmd` 구독 | chore #1 |
| `HeartbeatLink.cs` | `/factory/heartbeat_unity` 1초 발행, `/factory/heartbeat_ros` 수신이 3초 끊기면 장비 정지 + 경고 | FR-17 |
| `TopicNames.cs` | 토픽 이름 상수 모음 (오타 방지, `<장비>_<번호>_<기능>` 규칙) | NFR-08 |

### 2.2 `_Project/Scripts/Arms/` — [A] 공통 · scope `unity-arm`

| 파일 | 역할 |
|---|---|
| `ArmRig.cs` | 팔 1대의 공통 컴포넌트. `armId`(1~4), ArticulationBody 관절 6개 참조 |
| `KinematicGripper.cs` | 집기 단순화: 박스 Rigidbody를 Kinematic으로 바꾸고 그리퍼에 부착, 놓을 때 해제 (SRS 2.5) |
| `ArmPickPlaceServer.cs` | ⚠ Unity IK 방식일 때: `/arm_N_pick_place` 서비스 서버. IK로 이동 → 집기 → 놓기 → success 응답. 팔1은 집은 박스 ID를 message로 반환 |
| `ArmJointFollower.cs` | ⚠ ROS2 관절 제어 방식일 때: `/arm_N_joint_states`를 받아 관절 목표값 적용 |
| `ArmStatusReporter.cs` | 팔 상태(IDLE·MOVING·ERROR) 보고 |

팔별 배치·좌표는 각 담당이 프리팹 인스턴스로 설정: 팔1 A, 팔2 B, 팔3 C, 팔4 D.

### 2.3 `_Project/Scripts/Line/` — A·B·C · scope `unity-line`

| 파일 | 담당 | 역할 | 관련 |
|---|---|---|---|
| `Box.cs` | A | 박스 데이터: `box_id`, 크기(W×H×D), 구분(정상·규격 초과), 현재 위치 | SRS 6장 |
| `BoxBin.cs` | A | 적재함. 20개(정상 14·초과 6, 크기 랜덤) 생성, ID 순서 부여, 비면 다시 채움, `/bin/count` 발행 | FR-02, 05 |
| `Conveyor.cs` | A | `/conveyor/cmd` 받아 0.2 m/s 이동·정지(0.5초 이내), 라인 위 박스 수 `/conveyor/box_count` 발행 | FR-03, 04 |
| `SizeGate.cs` | B | 규격 높이 게이트 **Trigger**. 닿은 박스를 규격 초과로 판정 | FR-06 |
| `Pusher.cs` | B | 판정된 박스를 불량 칸으로 밀어냄 (컨베이어는 계속 동작) | FR-07 |
| `RejectSlot.cs` | B | 불량 칸 1칸. 들어오면 `reject`, 차서 대기면 `reject_wait` → `/detect_1_event` | FR-07, 10 |
| `EndSlots.cs` | C | 라인 끝 대기 칸 2칸. `end_1`·`end_2`, 다 차면 `end_wait` → `/detect_2_event`. 먼저 온 순서 유지 | FR-09, 10 |
| `PalletSlots.cs` | B·D | 팔레트 칸 표시용. 불량 팔레트 = pallet_1 12칸 (B, 칸 계산과 적재 수는 factory_manager), 출고 팔레트 = pallet_2 12칸 (D) | FR-08, 13 |

### 2.4 `_Project/Scripts/AGV/` — [C] · scope `unity-agv`

| 파일 | 역할 |
|---|---|
| `AgvWaypointDriver.cs` | `/agv_1_goal` 받아 웨이포인트 따라 이동(편도 8초 초기값), 상태 바뀔 때 `/agv_1_status` 발행 |
| `AgvCargo.cs` | AGV 위 박스 1개 슬롯. 실린 박스를 함께 이동 |

### 2.5 `_Project/Scripts/UI/` — A·D · scope `unity-ui`

| 파일 | 담당 | 역할 | 관련 |
|---|---|---|---|
| `ControlPanel.cs` | D | 버튼 5개 → `/factory/*` 발행. 상태별 활성화 규칙, E_STOP이면 [비상정지]가 "해제"로 바뀜 | UI-02, FR-01, 14~18 |
| `FaultSelector.cs` | D | [고장 주입] 대상 목록(arm_1~4, conveyor, agv_1) | FR-18 |
| `Dashboard.cs` | A | `/factory/stats` JSON 파싱해 표시만 함(직접 세지 않음), 1초 갱신 | UI-03, FR-19 |
| `StatusBanner.cs` | A | 공정 상태 색 표시, ERROR·E_STOP·통신 끊김 경고 배너 | FR-16~18 |

### 2.6 `_Project/Scripts/Common/`

| 파일 | 역할 |
|---|---|
| `LineSettings.cs` | ScriptableObject. 적재함 수량, 규격 높이, 컨베이어 속도 → Inspector에서 수정 (NFR-09) |
| `FactoryState.cs` | `/factory/state` 문자열 ↔ enum |

### 2.7 그 밖의 Unity 폴더

| 폴더 | 내용 | 규칙 |
|---|---|---|
| `_Project/Prefabs/` | `Arm_Fairino`(A, 4대 공통), `Box_Normal`, `Box_Oversize`, `Conveyor`, `SizeGate`, `AGV_1`, `Pallet_1`, `Pallet_2`, `ControlPanel`, `Dashboard` | 기능은 프리팹 안에서 완성 → 씬 충돌 방지 |
| `_Project/Settings/` | `LineSettings.asset`, URP 설정 | |
| `_Project/Scenes/Main.unity` | 최종 씬 | **D만 수정** |
| `_Project/Scenes/Sandbox/<이름>.unity` | 개인 테스트 씬 | 각자 자유, Main에 영향 없음 |
| `RosMessages/` | `Robotics > Generate ROS Messages`로 만든 C# (FactoryInterfaces) | 직접 수정 금지, 재생성만 |
| `URDF/` | Fairino URDF + 메시 | LFS |
| `ThirdParty/` | 구매 에셋 | `.gitignore` — 팀원 간 별도 공유 (SRS 2.4) |

---

## 3. `docs/`

| 파일 | 내용 |
|---|---|
| `SRS_INDEX_v2.0.docx` | 요구사항 명세 |
| `scenario.md` | 시연 시나리오 |
| `line_coords.md` | 라인 좌표표 (Unity 좌표와 ROS 좌표 둘 다) — `dt_bringup/config/line_coords.yaml`의 원본 |
| `interfaces.md` | 토픽·서비스 표 (SRS 4.4 복사본, 결정 사항 추가) |
| `FOLDER_STRUCTURE.md` | 이 문서 |

---

## 4. 박스 1개가 지나가는 파일 경로 (정상 박스)

```
[동작] ControlPanel.cs ──/factory/start──▶ factory_manager_node.py → fsm.py: IDLE→RUNNING
                                          └─/conveyor/cmd(true)──▶ Conveyor.cs
line_rules.py (박스 ≤3) → arm_dispatcher.py ──/arm_1_pick_place──▶ ArmPickPlaceServer.cs(팔1) + BoxBin.cs
Conveyor.cs → SizeGate.cs(안 닿음) → EndSlots.cs ──/detect_2_event(end_1)──▶ line_rules.py, stats.py
arm_dispatcher.py ──/arm_3_pick_place──▶ 팔3 → AgvCargo.cs
factory_manager ──/agv_1_cmd──▶ agv_1_controller_node.py ──/agv_1_goal──▶ AgvWaypointDriver.cs ──/agv_1_status(ARRIVED)──▶ palletizer_node.py
palletizer_node.py + pallet_layout.py ──/arm_4_pick_place──▶ 팔4 → PalletSlots.cs
palletizer_node ──/pallet_2_count──▶ factory_manager → stats.py ──/factory/stats──▶ Dashboard.cs
```
