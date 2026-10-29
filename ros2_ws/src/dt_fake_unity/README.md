# dt_fake_unity  ·  담당 D  ·  scope `fake`

Unity가 보내야 할 토픽·서비스를 흉내 낸다. ROS 담당(B, C, D)이 Unity 없이 자기 노드를 테스트할 때 쓴다.
**Endpoint·Unity Play와 동시에 띄우지 않는다** (같은 토픽·서비스를 두 곳에서 내게 됨).

## 노드
| 실행 파일 | 역할 |
|---|---|
| `fake_unity_node` | Heartbeat, 초기값(적재함 20, AGV IDLE), 박스 수, 감지 이벤트, AGV 상태 발행 + 팔 4대 서비스 서버 |
| `fake_panel` | 터미널 키로 [동작]·[비상정지]·[불량/출고 팔레트 정리]·[고장 주입]·[조치 완료] |

장면 계산은 `fake_scene.py`(rclpy 없음)에 있고 `test/test_fake_scene.py`로 시험한다.

## 실행
```bash
ros2 launch dt_bringup fake.launch.py          # fake_scenario.yaml 값 + 제어 노드
ros2 run dt_fake_unity fake_panel              # 다른 터미널. s=동작 e=비상정지 1·2=팔레트 정리 f=고장 c=조치 완료

# 노드만 따로, 값 바꿔서
ros2 run dt_fake_unity fake_unity_node --ros-args -p fail_arm_id:=4 -p box_sequence:="N,R"
```

## 흉내 내는 동작
| 상황 | 가짜 Unity가 하는 일 |
|---|---|
| 시작 1초 뒤 | `/bin/count` 20, `/conveyor/box_count` 0, `/agv_1_status` IDLE (이후는 바뀔 때만) |
| 항상 | `/factory/heartbeat_unity` true, 1초마다 |
| `/arm_1_pick_place` | 1초 뒤 `success=true`, `message=BOX_001…`. 적재함 −1, 컨베이어 +1 |
| 컨베이어 가동 중 2초 | 박스가 칸에 도착. 정상 → `/detect_2_event` `end_1`/`end_2`(다 차면 `end_wait`), 규격 초과 → `/detect_1_event` `reject`(차 있으면 `reject_wait`) |
| `/arm_2_pick_place` | 불량 칸 비움, 기다리던 박스가 있으면 `reject` 발행 |
| `/arm_3_pick_place` | 대기 칸에서 먼저 온 박스를 AGV에 실음 → `/agv_1_status` LOADED |
| `/agv_1_goal` | MOVING → 8초 뒤 하역 구역이면 ARRIVED, 상차 구역이면 IDLE |
| `/arm_4_pick_place` | AGV 위 박스를 내림 |
| `/factory/state`가 E_STOP | 팔 응답 `success=false`, `message=E_STOP` (고장 아님) |
| `fail_arm_id`=N | 팔 N은 `success=false`, `message=fake fail` |

순서가 이상한 호출(빈 칸에서 집기, AGV가 없는데 상차 등)은 1단계 약속대로 성공으로 응답하고 경고 로그만 남긴다.
DetectEvent의 `position`은 좌표표(1.6) 확정 전이라 (0, 0, 0)이다.

## 확인
```bash
ros2 run dt_fake_unity fake_unity_node                     # 터미널 1
ros2 topic hz /factory/heartbeat_unity                     # 터미널 2: average rate 1.000 근처
ros2 topic echo /bin/count                                  # 터미널 3
ros2 service call /arm_1_pick_place factory_interfaces/srv/PickPlace "{}"
#  → success=True, message='BOX_001', /bin/count 19. 호출 중에도 hz 1.0 유지
colcon test --packages-select dt_fake_unity && colcon test-result --verbose
```
