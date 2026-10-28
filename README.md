# INDEX · 로봇팔·AGV 협업 공정의 Unity–ROS2 디지털 트윈

물류센터 출고 라인 하나(로봇팔 4대, 컨베이어 1대, AGV 1대)를 **Unity**로 시뮬레이션하고 **ROS2 Humble** 노드로 제어한다.
적재함의 박스가 크기 검사를 거쳐, 규격 초과 박스는 불량 팔레트로, 정상 박스는 AGV를 거쳐 출고 팔레트로 간다.

```
 Windows PC                                         Ubuntu 22.04 VM (브리지)
 Unity 6 + URP ── ROS-TCP-Connector ── TCP 10000 ── ROS-TCP-Endpoint ── ROS2 노드
 (장비 물리·감지·화면)                  내 VM IP                         (공정 판단·장비 명령)
```
> Unity ROS Settings의 IP는 **각자 자기 VM IP**. 그 설정 파일은 skip-worktree로 Git에 올리지 않는다 ([docs/SETUP.md](docs/SETUP.md)).

- **Unity**: 장비의 물리 동작과 감지 결과 보고. 스스로 판단하지 않는다.
- **ROS2**: 공정 상태(FSM) 판단과 장비 명령. 라인 명령권은 `factory_manager` 하나만 가진다.

## 팀

| 역할 | 담당 | ROS2 | Unity |
|---|---|---|---|
| A 로봇팔1·투입·대시보드 | | `dt_arm` | 팔 공통 프리팹, 팔1, 적재함·박스, 컨베이어, 대시보드 |
| B 로봇팔2·공정 제어 | | `factory_manager`, `factory_interfaces` | 게이트, 푸셔, 불량 칸·팔레트, 팔2 |
| C 로봇팔3·AGV1 | | `dt_agv` | 대기 칸 2개, 팔3, AGV1 주행 |
| D 로봇팔4·연동·통합 | | `dt_palletizer`, `dt_fake_unity`, `dt_bringup` | 팔4, 출고 팔레트, ROS 연결, 메인 씬, 버튼 |

팀원: 권오민, 김예은, 김찬중, 박준영

## 빠른 시작

처음이면 **[docs/SETUP.md](docs/SETUP.md)** 를 끝까지 따라 한다.

```bash
# VM — 빌드
cd ~/dev/DT_Index/ros2_ws
vcs import src < dependencies.repos
rosdep install --from-paths src --ignore-src -y
colcon build --symlink-install && source install/setup.bash
```

| 목적 | VM | Windows |
|---|---|---|
| 연결 확인 | `ros2 launch dt_bringup connect.launch.py` | `Sandbox/ConnectionTest` 씬 Play |
| 전체 시연 | `ros2 launch dt_bringup factory.launch.py` | `Main` 씬 Play → [동작] |
| Unity 없이 | `ros2 launch dt_bringup fake.launch.py` | — |

## 문서

| 문서 | 내용 |
|---|---|
| [docs/SETUP.md](docs/SETUP.md) | 개발 환경 초기 세팅 (Windows + Unity / VM + ROS2) |
| [docs/FOLDER_STRUCTURE.md](docs/FOLDER_STRUCTURE.md) | 폴더·파일별 역할, 담당, 토픽 |
| [COMMIT_RULES.md](COMMIT_RULES.md) | 브랜치·커밋·PR 규칙 |
| [CONVENTIONS.md](CONVENTIONS.md) | 명명·Unity 작업 규칙 |
| 각 패키지 `README.md` | 노드, 토픽, 실행 방법 |

## 일정

| 마일스톤 | 날짜 | 기준 |
|---|---|---|
| M1 연결 | 2026.10.29 | ROS2–Unity 연결, [동작]으로 컨베이어 가동·정지 |
| M2 MVP | 2026.10.30 | 정상 1개 + 불량 1개 한 사이클, 로봇팔 제어 방식 결정 |
| M3 리허설 | 2026.11.04 | 50개 연속 운전, 비상정지·고장 주입 |
| 최종 시연 | 2026.11.05 | |
