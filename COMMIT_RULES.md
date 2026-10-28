# 브랜치 · 커밋 · PR 규칙

> 팀 INDEX가 합의한 Git 작업 규칙. 이 문서가 원본이며, 바꾸려면 6절 절차를 따른다.
> 이 규칙은 `.githooks/`가 로컬에서 강제한다 — 클론 후 `bash scripts/setup-git-hooks.sh` 1회 실행.

## 1. 브랜치

| 브랜치 | 머지 방향 | 용도 |
|---|---|---|
| `main` | ← `develop` | 마일스톤을 통과한 상태만. 머지 후 태그 |
| `develop` | ← 작업 브랜치 | 통합 브랜치. **기본 브랜치** |
| `feature/<scope>-<기능>` | → `develop` | 기능 개발 (`feature/manager-fsm`) |
| `fix/<scope>-<내용>` | → `develop` | 버그 수정 (`fix/unity-line-end-wait`) |
| `docs/<내용>` | → `develop` | 문서 (`docs/setup-vm`) |
| `chore/<내용>` | → `develop` | 설정·빌드 (`chore/gitignore-unity`) |
| `test/<내용>` | → `develop` | 시험·측정 (`test/nfr-02-fps`) |

- **`main`, `develop`에 직접 커밋하지 않는다** (pre-commit 훅이 막는다).
- 브랜치명은 영문 소문자 + 하이픈. `<scope>`는 아래 2절 목록과 같다 (노션 작업 트래커 scope와 동일).
- 작업 시작 전: `git switch develop && git pull && git switch -c feature/<scope>-<기능>`

## 2. 커밋 메시지

```
<type>(<scope>): <subject>
```

| type | 용도 | 예시 |
|---|---|---|
| `feat` | 새 기능 | `feat(manager): 투입 중지·재개 조건 추가` |
| `fix` | 버그 수정 | `fix(unity-line): 대기 칸 end_wait 중복 발행 수정` |
| `docs` | 문서 | `docs: VM 브리지 설정 설명 추가` |
| `test` | 시험·측정 | `test(unity-arm): 팔레트 적재 오차 측정 결과 기록` |
| `refactor` | 동작 변화 없는 구조 변경 | `refactor(manager): FSM을 별도 모듈로 분리` |
| `chore` | 설정·빌드·의존성 | `chore(bringup): Endpoint 버전 고정` |

- `scope`는 선택이며, 쓰면 아래 목록 중 하나만.
- `subject`는 **한글**, 끝에 마침표 없이, 72자 이내.
- 자세한 설명은 한 줄 띄우고 본문에.

### scope 목록

| scope | 대상 | 담당 |
|---|---|---|
| `interfaces` | `ros2_ws/src/factory_interfaces` | B |
| `manager` | `ros2_ws/src/factory_manager` | B |
| `arm` | `ros2_ws/src/dt_arm` | A |
| `agv` | `ros2_ws/src/dt_agv` | C |
| `palletizer` | `ros2_ws/src/dt_palletizer` | D |
| `fake` | `ros2_ws/src/dt_fake_unity` | D |
| `bringup` | `ros2_ws/src/dt_bringup` (launch, config) | D |
| `unity-arm` | `Assets/_Project/Scripts/Arms`, 팔 프리팹 | A(공통)·각 팔 담당 |
| `unity-line` | `Assets/_Project/Scripts/Line` | A·B·C |
| `unity-agv` | `Assets/_Project/Scripts/AGV` | C |
| `unity-ui` | `Assets/_Project/Scripts/UI` | A·D |
| `unity-ros` | `Assets/_Project/Scripts/ROS`, `Assets/RosMessages` | D |
| `scene` | `Assets/_Project/Scenes/Main.unity` | D |
| `test` | 측정·검증 기록 | |
| `chore` | 저장소 설정 | |
| `design` | 설계 결정 기록 | |
| `milestone` | 마일스톤 통합 | |
| `docs` | `docs/` | |

## 3. PR

| 구간 | 셀프 머지 | 조건 |
|---|---|---|
| 작업 브랜치 → `develop` | 허용 | PR 필수, 템플릿 체크리스트 완료, 빌드·실행 확인 |
| `develop` → `main` | D가 머지 | 마일스톤 기준 통과, 머지 직후 태그 |

- **PR 하나 = 작업 하나** (노션 트래커 작업 1개와 대응).
- PR 올리기 전 `develop`을 받아 충돌을 먼저 해결한다: `git pull origin develop`
- 머지 후 동작이 깨지면 고치기보다 **revert 먼저**. develop은 항상 실행 가능해야 한다.
- `factory_interfaces`·토픽 이름·`line_coords.yaml`을 바꾸는 PR은 **4명 모두에게 알린다**.

## 4. 태그

| 태그 | 시점 | 기준 |
|---|---|---|
| `v0.0` | 10.28 | 저장소 세팅 완료, 4명 연결 확인 |
| `v0.1` | 10.29 | M1 연결 |
| `v0.2` | 10.30 | M2 MVP — 토픽·메시지 동결 |
| `v0.3` | 11.04 | M3 리허설 |
| `v1.0` | 11.05 | 최종 시연 |

## 5. Git 훅

| 훅 | 하는 일 |
|---|---|
| `commit-msg` | 2절 형식·scope·한글·마침표 검사. 어기면 커밋 거부 |
| `pre-commit` | main/develop 직접 커밋 거부 · 빌드 산출물·Unity Library·ThirdParty 차단 · Unity 에셋 `.meta` 누락 차단 · LFS 아닌 10 MB 초과 파일 차단 · 브랜치명 경고 |
| `pre-push` 외 3개 | Git LFS 훅 (지우지 말 것) |

## 6. 규칙 변경

1. 무엇을, 왜 지금 바꾸는지 한 줄로 단톡에 제안한다.
2. 그 이름을 쓰는 코드·launch·Unity 스크립트·문서·훅을 모두 찾는다.
3. 코드와 이 문서, 훅을 **같은 PR**에서 고친다.
