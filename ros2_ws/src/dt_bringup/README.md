# dt_bringup  ·  담당 D  ·  scope `bringup`

실행 launch와 **코드 수정 없이 바꾸는 설정값**(NFR-09)을 모은다.

## launch
| 파일 | 용도 | 띄우는 것 |
|---|---|---|
| `connect.launch.py` | 연결 확인 (M1, 세팅 검증) | Endpoint만 |
| `factory.launch.py` | 실제 시연 (NFR-10) | Endpoint + factory_manager + palletizer + agv_1_controller (+ `use_ros_arm:=true`면 팔 4대) |
| `fake.launch.py` | Unity 없이 테스트 | fake_unity + 위 제어 노드 (Endpoint 없음) |
| `_common.py` | 두 launch가 공유하는 노드 목록 | |

## config
| 파일 | 내용 |
|---|---|
| `factory.yaml` | 투입 기준 4/3, 팔레트 12, Heartbeat 1초/3초 |
| `line_coords.yaml` | 라인 좌표표 (4명 합의 후 변경) |
| `agv_waypoints.yaml` | AGV1 왕복 경로 |
| `fake_scenario.yaml` | 가짜 Unity 박스 순서·이동 시간·실패 팔 |

yaml만 고쳤을 때도 `colcon build`가 필요하다 (install 폴더로 복사되므로).
