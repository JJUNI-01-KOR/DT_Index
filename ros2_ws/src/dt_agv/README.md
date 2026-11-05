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

### 상차 후 레일 이탈

`ATTACHED`가 확인된 도킹 지점을 기억하고, `FINAL` 명령 시
`route.to_final_from_p1_dock` 또는 `route.to_final_from_p2_dock`의 경유점을
순서대로 보낸다. 해당 키가 없으면 기존 `route.to_final`을 사용한다.
현재 첫 경유점은 각 도킹 지점에서 전방 2m인 Unity x=-21 (ROS y=21)이며,
도킹 방향과 같은 yaw를 유지한 뒤 FINAL로 이동한다. 좌표·거리는 YAML에서 조정한다.

Unity `AgvLink`는 자체 경유점을 만들지 않고 ROS 경유점마다 `ARRIVED`를 보고한다.
하차 후에는 분리한 팔레트가 `FinalZone`에서 삭제될 때까지 다음 이동을 보류한다.
이 ROS 변경과 Unity의 자체 직진 제거를 함께 적용해야 이탈 이동이 중복되지 않는다.
P1/P2 각각 상차→이탈→FINAL→삭제 후 복귀, 그리고 이탈/대기 중 E_STOP을 시험한다.

## 실행
```bash
ros2 run dt_agv agv_1_controller_node --ros-args --params-file $(ros2 pkg prefix dt_bringup)/share/dt_bringup/config/factory.yaml
colcon test --packages-select dt_agv && colcon test-result --verbose
```
