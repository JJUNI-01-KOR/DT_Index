# dt_bringup  ·  담당 D  ·  scope `bringup`

실행 launch와 **코드 수정 없이 바꾸는 설정값**(NFR-09)을 모은다.

## launch
| 파일 | 용도 | 띄우는 것 |
|---|---|---|
| `connect.launch.py` | 연결 확인 (M1, 세팅 검증) | Endpoint만 |
| `factory.launch.py` | 실제 시연 (NFR-10) | Endpoint + factory_manager + palletizer + agv_1_controller (팔은 Unity IK라 ROS 노드 없음) |
| `fake.launch.py` | Unity 없이 테스트 | fake_unity + 위 제어 노드 (Endpoint 없음) |
| `_common.py` | 두 launch가 공유하는 노드 목록 | |

## config
| 파일 | 내용 |
|---|---|
| `factory.yaml` | 공정 설정값 (SRS v3.1 A.5): 정상 무게 범위, 레인 용량 3, 적재 기준 5, 집기 제한 20초, 감시 시간(무게 보고 15초, AGV1 30초), Heartbeat 1초/3초. 키는 factory_manager가 선언하는 파라미터와 같다 |
| `line_coords.yaml` | 라인 좌표표 v3.1 이름 (값 0.0 = 아직 안 채움, 4명 합의 후 변경) |
| `agv_waypoints.yaml` | AGV1 정지 지점 4곳(HOME, P1_DOCK, P2_DOCK, FINAL)과 일방통행 경로점 |
| `fake_scenario.yaml` | 가짜 Unity 박스 순서·이동 시간·실패 팔 |

yaml만 고쳤을 때도 `colcon build`가 필요하다 (install 폴더로 복사되므로).
