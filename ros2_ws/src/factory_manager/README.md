# factory_manager  ·  담당 B  ·  scope `manager`

라인 전체의 **유일한 명령권자**. 공정 상태 4개(IDLE, RUNNING, ERROR, E_STOP)를 판단한다. 기준: SRS v3.1.
**실행하면 E_STOP(원인 startup)으로 시작**하므로, 처음 한 번 [해제]를 눌러 IDLE로 만든 뒤 [시작]한다 (FR-17).
관련: FR-01, 02, 04, 07, 08, 12, 13, 16, 17, 18, 20 / NFR-01, 03, 05, 11

## 노드
| 실행 파일 | 노드 이름 |
|---|---|
| `factory_manager_node` | `factory_manager` |

## 모듈 (판단은 rclpy 없는 순수 Python → pytest)
| 파일 | 상태 | 내용 |
|---|---|---|
| `fsm.py` | 동작 | 4상태, E_STOP 시작과 원인, 해제, [시작]. ERROR·고장 목록은 TODO |
| `grading.py` | 뼈대 | 무게 판정 (FR-07) |
| `line_rules.py` | 뼈대 | 투입 조건: 레인 용량, 무게 보고 대기 (FR-02, 04) |
| `pallet_queue.py` | 동작 | 적재 수와 운반 대기열 (FR-12, 15) |
| `agv_dispatch.py` | 동작 | AGV1 명령 P1_DOCK·P2_DOCK·FINAL·HOME (FR-13) |
| `watchdog.py` | 뼈대 | 감시 시간 무게 보고 15초, AGV1 30초 (FR-02, 13) |
| `stats.py` | 뼈대 | `/factory/stats` JSON (FR-20) |

지금 동작하는 흐름: E_STOP → [해제] → IDLE → [시작] → RUNNING → `/conveyor/cmd(true)`, 비상정지 → 정지.
v3.1 토픽은 모두 구독·발행 선언만 되어 있고 콜백은 `TODO(FR-xx)`이다. 팔1 투입은 `FeedRule`이 채워지면 나간다.

## 실행
```bash
ros2 run factory_manager factory_manager_node --ros-args --params-file $(ros2 pkg prefix dt_bringup)/share/dt_bringup/config/factory.yaml
colcon test --packages-select factory_manager && colcon test-result --verbose
```
