# INDEX · 로봇팔·AGV 협업 공정의 Unity–ROS2 디지털 트윈

방석 분류 라인 하나(로봇팔 3대, ㄷ자 컨베이어 1대, 무게 센서 1개, AGV 1대, 파레트 2개)를 **Unity**로 시뮬레이션하고 **ROS2 Humble** 노드로 제어한다 (SRS v3.1).
적재함의 방석이 무게 검사를 거쳐 정상은 파레트1, 불량은 파레트2에 5단으로 쌓이고, 가득 찬 파레트는 AGV1이 최종 구역으로 끌고 간다.

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
| A 로봇팔1·투입 + 컨베이어 · 상태 패널 | 권오민 | — (팔1 호출은 factory_manager) | 팔1 좌표, 적재함·방석, 컨베이어 레인 2개, 레인 끝, 상태 패널 |
| B 로봇팔2·정상 적재 + 공정 제어 | 김예은 | `factory_manager`, `factory_interfaces` | 무게 센서, 분기 게이트, 팔2 좌표 |
| C AGV1 제어 + 팔레타이징 (ROS2 전용) | 박준영 | `dt_agv`, `dt_palletizer`, `dt_fake_unity`, `dt_bringup`(launch) | — |
| D 로봇팔3·불량 적재 + 연동·통합 | 김찬중 | `dt_bringup`(좌표표) | 팔 공통 pick·place(Unity IK), 팔3, AGV1·파레트·최종 구역, 제어 패널, ROS 연결, 메인 씬 |

폴더·파일별 담당은 [docs/FOLDER_STRUCTURE.md](docs/FOLDER_STRUCTURE.md).

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
| 전체 시연 | `ros2 launch dt_bringup factory.launch.py` | `Main` 씬 Play → [해제] → [시작] (factory_manager는 E_STOP으로 시작, SRS v3.1) |
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
