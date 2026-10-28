# dt_agv  ·  담당 C  ·  scope `agv`

AGV1이 상차 구역 ↔ 출고 팔레트 구역을 웨이포인트로 왕복하도록 목표를 보낸다. 관련: FR-11, 12

## 노드
| 실행 파일 | 노드 이름 |
|---|---|
| `agv_1_controller_node` | `agv_1_controller` |

## 예정 모듈 (docs/FOLDER_STRUCTURE.md 1.4)
`cycle_timer.py` · 웨이포인트는 `dt_bringup/config/agv_waypoints.yaml`

## 실행
```bash
ros2 run dt_agv agv_1_controller_node
```
