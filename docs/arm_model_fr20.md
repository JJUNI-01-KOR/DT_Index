# 로봇팔 모델 변경: FR5 → FR20

> 결정: 2026.10.30 (D 김찬중 제안, WBS 4.2 회의에서 팀 확인 필요)
> 관련: FR-02, FR-08, FR-11, FR-13 · WBS 2.4, 3.13, 4.2 · `docs/FOLDER_STRUCTURE.md` 2.2

## 1. 왜 바꾸나

라인 설비(컨베이어, 박스, 팔레트) 크기에 비해 FR5가 작아서 DT_Main 씬에서 RobotArm_1~4를 **Scale 2**로 키웠다.
Scale로 키우면 아래 문제가 생긴다.

| 문제 | 내용 |
|---|---|
| ROS2 좌표가 안 맞음 | `frcobot_ros2`의 MoveIt 설정과 `dt_arm/kinematics.py`는 **실제 FR5 치수**로 관절각을 계산한다. Unity 팔이 2배면 같은 관절각에서 그리퍼가 약 2배 먼 곳에 간다. 에러 없이 엉뚱한 곳을 집는다 |
| 물리 불안정 | ArticulationBody는 Scale 1 기준. 키워도 질량·관성·드라이브 강성은 그대로라 떨림·처짐이 생기기 쉽다 |
| 디지털 트윈 전제 | 실제 로봇과 크기가 달라진다 |

→ Scale은 1로 두고, **실제 치수가 2배 FR5와 비슷한 모델**을 쓴다.

## 2. 후보 비교 (URDF 관절 위치 기준, 단위 m)

| 모델 | 베이스 높이 | 윗팔 | 아래팔 | 손목 두 마디 | 윗팔+아래팔 | FR5×2 대비 |
|---|---|---|---|---|---|---|
| FR5 × 2 (기존 씬) | 0.304 | 0.850 | 0.790 | 0.204 / 0.204 | 1.640 | 기준 |
| **FR20** | 0.215 | 1.000 | 0.716 | 0.166 / 0.138 | **1.716** | **+5%** |
| FR10 | 0.180 | 0.700 | 0.586 | 0.159 / 0.114 | 1.286 | −22% |
| FR30 | 0.215 | 0.700 | 0.536 | 0.166 / 0.138 | 1.236 | −25% |
| FR16 | 0.180 | 0.520 | 0.400 | 0.159 / 0.114 | 0.920 | −44% |
| FR5 (원본) | 0.152 | 0.425 | 0.395 | 0.102 / 0.102 | 0.820 | −50% |

출처: [FAIR-INNOVATION/frcobot_ros2](https://github.com/FAIR-INNOVATION/frcobot_ros2) `fairino_description/urdf/fairino*_v6.urdf`

**FR20을 고른 이유**
- 크기가 FR5×2와 가장 비슷하다 (팔 길이 +5%).
- 같은 저장소에 `fairino20_v6_moveit2_config`가 있다 → ROS2 관절 제어로 가도 그대로 쓴다.
- 관절 이름(j1~j6), 링크 이름(base_link ~ wrist3_link)이 FR5와 같다 → `ArmRig.cs` 등 스크립트 변경이 거의 없다.

**다른 점**
- 어깨 높이가 0.30 → 0.215 m로 낮다. 손목이 FR5×2보다 작다.
- 관절 effort: j1~j3 150, j4~j6 28 → ArticulationBody 드라이브 값 재조정 필요할 수 있음.

## 3. 지금 들어간 것 (D, 2026.10.30)

| 경로 | 내용 |
|---|---|
| `Assets/URDF/fairino20_v6.urdf` | 공식 저장소 원본 그대로 |
| `Assets/URDF/fairino_description/meshes/fairino20_v6/` | STL 7개(LFS) + URDF Importer가 만든 메시 프리팹·asset |
| `Assets/URDF/Materials/rgba-0.79216-0.81961-0.93333-1.mat` | URDF Importer가 만든 wrist3 색 |
| `Assets/_Project/Prefabs/Arm/Arm_Fairino.prefab` | 팔 공통 프리팹. 10/30 FR20으로 교체 (5절) |

공통 프리팹에 반영한 뒤 임시 도구 `Assets/Editor/Fr20GripperTools.cs`, 비교용 `fairino20_v6_robot.prefab`, 첫 가져오기 때 생긴 `Assets/URDF/meshes/`(쓰는 곳 없는 STL 사본), 빈 `Assets/URDF/resources/`는 지웠다.

가져오기 때 겪은 문제 (다시 가져올 사람 참고)
- 처음 가져오면 `NullReferenceException at VHACD.GenerateConvexMeshes` → STL을 처음 읽는 타이밍 문제. **한 번 더 가져오거나 Mesh Decomposer를 `Unity`로** 하면 된다.
- 여러 번 가져오면 렌더러의 메시 참조가 비어 씬에 안 보일 수 있다 → MeshFilter에 `meshes/fairino20_v6/<이름>_0.asset`을 다시 넣는다.
- forearm, wrist1~3 충돌체는 볼록 메시 1개로 만들어졌다 (집기는 KinematicGripper라 영향 작음).

## 4. 흡착 그리퍼 옮기기

기존 그리퍼는 Unity 기본 원기둥 2개 + 빈 오브젝트라 그대로 옮기고 **높이만** 바꾼다. 스크립트(`ArmRig`, `KinematicGripper`)는 수정 없음.

```
wrist3_link
├── Suction      ← 손목 끝면
│   ├── Body     원기둥 지름 5 cm × 높이 8 cm (Suction 기준 y 0.04)
│   └── Pad      원기둥 지름 7 cm × 두께 0.5 cm (y 0.0825), 흡착 중 초록
└── ToolPoint    패드 표면 = 집기 기준점 (Suction + 0.085)
```

| wrist3_link 기준 y (m) | FR5 | **FR20** | 근거 |
|---|---|---|---|
| Suction | 0.099 | **0.120** | wrist3 메시 위끝 (FR5 계산값 0.099가 기존 값과 일치) |
| ToolPoint | 0.184 | **0.205** | Suction + 0.085 |

- FR20 손목 끝면 지름이 약 11.6 cm(FR5 8 cm)라 Body가 작아 보이면 Body Scale X·Z만 키운다 (흡착 동작과 무관).
- FR20은 FR5보다 약 4배 무겁다 (윗팔 10 → 43 kg). ArmRig 기본값(stiffness 10000, forceLimit 1000)으로 계산상 버티지만, 처지거나 떨리면 올린다.
- URDF Importer가 붙인 `Controller`(키보드 제어)는 끈다.

## 5. 남은 일

| 할 일 | 담당 | 비고 |
|---|---|---|
| 4.2 회의에서 FR20 확정, 제어 방식(ROS2 관절값 / Unity IK)과 함께 결정 | 전원 | |
| ✅ 팔 공통 프리팹을 FR20으로 교체 (`Arm_Fairino.prefab`, Scale 1, 베이스 고정, Arm_Body/Arm_Joint 머티리얼) | D (A에서 이관) | 10/30 완료. 루트(UrdfRobot·ArmRig·KinematicGripper)는 그대로 두고 그 아래만 FR20으로 바꿔 프리팹 GUID와 참조 유지. Suction 0.120, ToolPoint 0.205, Controller 없음. Play 시험: 기본 드라이브값으로 홈 자세 처짐 0.3° 이내, 자세 이동 오차 0.24°, 흡착 후 방석 따라옴 |
| 씬의 RobotArm_1~4를 새 프리팹으로 교체, Scale 1 | 각 팔 담당 / Main은 D | DT_Main의 비교용 `fairino20_v6_robot`은 그때 삭제 |
| 팔 높이를 받침대로 맞춤 (Scale 금지) | 각 팔 담당 | 10/30 IK 시험(팔3): 지금 자리 Unity (-16.5, 0, 5.4)는 파레트 3단까지만 닿음. **(-16.75, 0.7, 5.3)** 즉 집는 자리와 파레트 사이 가운데 + 받침대 0.7 m면 집는 자리와 5단(접근 0.3 m 포함) 모두 닿고 1회 7~8.5초. (-17.0, 0.5, 5.3)도 가능. 팔2는 z만 반대쪽으로 같은 배치 권장 |
| `line_coords.yaml` 팔 베이스·집기 좌표 갱신 | 전원 합의 | |
| ROS2 관절 제어로 가면 `dt_arm`을 FR20 치수로 (`fairino20_v6_moveit2_config` 참고) | A | |
