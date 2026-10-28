# factory_manager  ·  담당 B  ·  scope `manager`

라인 전체의 **유일한 명령권자**. 공정 상태 5개(IDLE, RUNNING, PALLET_FULL, ERROR, E_STOP)를 판단한다.
관련: FR-01, 04, 06, 07, 08, 10, 14, 16, 17, 18 / NFR-01, 03, 05, 11

## 노드
| 실행 파일 | 노드 이름 |
|---|---|
| `factory_manager_node` | `factory_manager` |

## 예정 모듈 (docs/FOLDER_STRUCTURE.md 1.2)
`fsm.py` · `line_rules.py` · `arm_dispatcher.py` · `stats.py` · `heartbeat_monitor.py` · `test/`

## 실행
```bash
ros2 run factory_manager factory_manager_node --ros-args --params-file $(ros2 pkg prefix dt_bringup)/share/dt_bringup/config/factory.yaml
```
