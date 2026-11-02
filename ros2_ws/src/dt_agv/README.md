# dt_agv  ·  담당 C  ·  scope `agv`

AGV1이 상차 구역 ↔ 출고 팔레트 구역을 웨이포인트로 왕복하도록 목표를 보낸다. 관련: FR-11, 12, 13, 16

## 노드
| 실행 파일 | 노드 이름 |
|---|---|
| `agv_1_controller_node` | `agv_1_controller` |

## 모듈 (판단은 rclpy 없는 순수 Python → pytest)
| 파일 | 상태 | 내용 |
|---|---|---|
| `route.py` | 동작 | `/agv_1_cmd` → 경로점 순서대로 `/agv_1_goal`, 도킹·FINAL 자동 attach/detach, `/agv_1_loaded` 결정 |

`cycle_timer.py`는 만들지 않음 — SRS대로 AGV 사이클 시간은 factory_manager(`agv_dispatch.py`)가 PN_DOCK 시각부터 잰다 (docs/FOLDER_STRUCTURE.md 1.5절 참고).

웨이포인트는 `dt_bringup/config/agv_waypoints.yaml` (`stops`, `route`).

## 실행
```bash
ros2 run dt_agv agv_1_controller_node --ros-args --params-file $(ros2 pkg prefix dt_bringup)/share/dt_bringup/config/factory.yaml
colcon test --packages-select dt_agv && colcon test-result --verbose
```
