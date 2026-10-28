# 명명 · 작업 규칙

> Git 규칙은 [COMMIT_RULES.md](COMMIT_RULES.md), 폴더별 역할은 [docs/FOLDER_STRUCTURE.md](docs/FOLDER_STRUCTURE.md).
> `[확정]` = 팀 결정 / `[제안]` = 구현 착수 전 합의 필요

## 1. 표기 [확정]

| 대상 | 표기 | 예시 |
|---|---|---|
| ROS 패키지·노드·토픽·파라미터 | `snake_case` | `factory_manager`, `feed_stop_count` |
| 번호 붙는 장비 | `<장비>_<번호>_<기능>` (SRS 4.4, NFR-08) | `arm_1_controller`, `/arm_1_pick_place`, `/agv_1_status` |
| 공정 상태·zone 값 | 대문자 / 소문자 그대로 | `IDLE, RUNNING, PALLET_FULL, ERROR, E_STOP` / `reject, end_1, end_wait` |
| C# 클래스·파일 | `PascalCase` (파일명 = 클래스명) | `Conveyor.cs`, `KinematicGripper.cs` |
| C# 필드·변수 | `camelCase`, Inspector 노출은 `[SerializeField] private` | `[SerializeField] private float speedMps = 0.2f;` |
| 프리팹·씬 | `PascalCase` | `Arm_Fairino.prefab`, `Main.unity` |
| 박스 ID | `BOX_001` (적재함을 채울 때 순서대로, 중복 불가) | |

**한글 금지**: 패키지, 노드, 토픽, 파일명, 폴더명, 브랜치명, 씬·프리팹 이름. 한글은 주석·문서·커밋 subject에만.

## 2. ROS2 [확정]

| 항목 | 규칙 |
|---|---|
| 패키지 | 커스텀 메시지는 `factory_interfaces` 하나에만. 나머지는 표준 타입 |
| 토픽 이름 | SRS 4.4 표가 계약. 바꾸면 Unity `TopicNames.cs`와 같은 PR에서 |
| 숫자 설정값 | 코드에 쓰지 않고 `dt_bringup/config/*.yaml` 파라미터로 (NFR-09). 이름에 단위 (`_sec`, `_m`, `_count`) |
| 좌표 | ROS 좌표계(FLU), 단위 m·rad. 원본은 `line_coords.yaml` |
| 판단 로직 | rclpy 없는 순수 Python 모듈로 분리해 단위 테스트 (`fsm.py`, `line_rules.py`) |
| 로그 | `self.get_logger().info/warn/error`. 상태 전이는 반드시 info로 남긴다 (SRS 6장 이벤트 로그) |

## 3. Unity [확정]

| 항목 | 규칙 |
|---|---|
| 에디터 버전 | 팀 고정 `6000.6.0f1`. "Upgrade" 창이 뜨면 취소 |
| 직렬화 | Force Text + Visible Meta Files. **`.meta`는 항상 함께 커밋** |
| 내 파일 위치 | 팀이 만든 것은 전부 `Assets/_Project/` 아래 |
| ROS 통신 | `Scripts/ROS/`에서만. 토픽 이름은 `TopicNames` 상수로 |
| ROS IP 설정 | 각자 자기 VM IP. `Assets/Resources/ROSConnectionPrefab.prefab`은 **skip-worktree로 커밋 금지** (`setup-git-hooks.sh`가 자동 적용) |
| 좌표 변환 | `CoordConverter`에서만 (`To<FLU>()`, `From<FLU>()`) |
| 메인 씬 | `Main.unity`는 **D만 수정**. 각자 `Scenes/Sandbox/<이름>_*.unity`에서 작업 |
| 기능 단위 | 프리팹 안에서 완성 → D가 메인 씬에 배치. 프리팹을 고칠 때는 담당자에게 먼저 말한다 |
| 설정값 | 적재함 수량, 규격 높이, 컨베이어 속도는 `LineSettings` ScriptableObject (Inspector) |
| 집기 | 물리 그립 대신 Kinematic 부착 (SRS 2.5) |
| 구매 에셋 | `Assets/ThirdParty/`에 두고 Git에 올리지 않는다 (SRS 2.4) |
| 자동 생성 | `Assets/RosMessages/`는 직접 수정 금지, 재생성만 |

## 4. 같은 파일을 두 사람이 고치지 않기 [확정]

| 파일 | 소유자 | 다른 사람이 바꿔야 하면 |
|---|---|---|
| `Main.unity` | D | 프리팹·스크립트로 만들어 D에게 요청 |
| `factory_interfaces/*` | B | 단톡 합의 후 B가 수정, D가 Unity 메시지 재생성 |
| `TopicNames.cs`, `CoordConverter.cs` | D | D에게 요청 |
| `line_coords.yaml` | 공동 | 4명 합의 |
| `Arm_Fairino.prefab` | A | A에게 요청 (팔 4대 공통) |

## 5. 기록 [제안]

- 막혔던 문제와 해결은 `docs/troubleshooting.md`에 `날짜 · 증상 · 원인 · 해결` 한 줄씩.
- 측정(NFR-01·02·04·06·11)은 `docs/tests/<NFR-ID>.md`에 조건과 결과 수치를 남기고 `test(<scope>):` 커밋.
