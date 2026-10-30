# dt_fake_unity  ·  담당 D  ·  scope `fake`

Unity와 다른 노드 대신 띄워 ROS 쪽을 따로 시험하는 가짜 노드. 기준: SRS v3.1 (WBS 2.4 가짜 노드 3종).
**Endpoint·Unity Play, 그리고 같은 이름의 진짜 노드와 동시에 띄우지 않는다** (같은 토픽·서비스를 두 곳에서 내게 됨).

## 노드
| 실행 파일 | 상태 | 역할 |
|---|---|---|
| `fake_unity_node` | 일부 동작 | Heartbeat, `/bin/count`, 팔 1~3 서비스 서버(팔1은 방석 id `C001…` 응답). 라인·레인·파레트 자리·AGV는 뼈대 |
| `fake_panel` | 동작 | 터미널 키로 [시작]·[비상정지]/[해제]·[고장 주입]·[조치 완료] |
| `fake_palletizer` | 뼈대 | 진짜 palletizer 대신 `/pallet_N_placed` 발행 (TC-15 ~ 16) |
| `fake_agv_controller` | 뼈대 | 진짜 agv_1_controller 대신 `/agv_1_loaded` 발행 (TC-17, 41, 42) |

장면 계산은 `fake_scene.py`(rclpy 없음)에 있고 `test/test_fake_scene.py`로 시험한다.

## 실행
```bash
ros2 launch dt_bringup fake.launch.py          # fake_scenario.yaml 값 + 제어 노드
ros2 run dt_fake_unity fake_panel              # 다른 터미널. e=해제(처음 한 번) → s=시작

# 노드만 따로, 값 바꿔서
ros2 run dt_fake_unity fake_unity_node --ros-args -p fail_arm_id:=2 -p cushion_sequence:="N,D"
```
factory_manager는 E_STOP으로 시작하므로 **처음에 e(해제)를 눌러야** [시작]이 받아들여진다.

## 확인
```bash
ros2 run dt_fake_unity fake_unity_node                     # 터미널 1
ros2 topic hz /factory/heartbeat_unity                     # 터미널 2: average rate 1.000 근처
ros2 service call /arm_1_pick_place factory_interfaces/srv/PickPlace "{}"
#  → success=True, message='C001', /bin/count 19
colcon test --packages-select dt_fake_unity && colcon test-result --verbose
```
