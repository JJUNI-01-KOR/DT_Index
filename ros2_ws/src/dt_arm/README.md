# dt_arm  ·  담당 A  ·  scope `arm`

로봇팔 4대가 같은 코드를 쓰고 `arm_id`만 바꿔 4번 실행한다.
⚠ **10/30 제어 방식 결정에 따라 사용 여부가 정해진다.** Unity IK로 가면 서비스 서버는 Unity가 맡고 이 패키지는 쓰지 않는다. 호출 쪽(factory_manager, palletizer)과 `PickPlace.srv` 계약은 바뀌지 않는다.

## 노드
| 실행 파일 | 노드 이름 (launch에서 지정) |
|---|---|
| `arm_controller_node` | `arm_1_controller` ~ `arm_4_controller` |

## 예정 모듈 (docs/FOLDER_STRUCTURE.md 1.3)
`pick_sequence.py` · `kinematics.py` · `config/arm_params.yaml`

## 실행 (단독)
```bash
ros2 run dt_arm arm_controller_node --ros-args -r __node:=arm_1_controller -p arm_id:=1
```
