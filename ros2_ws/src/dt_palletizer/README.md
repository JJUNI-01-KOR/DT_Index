# dt_palletizer  ·  담당 D  ·  scope `palletizer`

레인 끝 1번 칸에 온 방석을 로봇팔로 집어 파레트에 1×5단으로 쌓는다. 관련: FR-09~11, 16, 18

같은 코드를 `lane` 파라미터만 바꿔 두 개 띄운다.

| 노드 | lane | 레인 끝 토픽 | 로봇팔 | 파레트 | 집는 자리 |
|---|---|---|---|---|---|
| `palletizer_1` | `ok` | `/conveyor_1_end_ok` | `/arm_2_pick_place` | 1 | `pick_ok` |
| `palletizer_2` | `ng` | `/conveyor_1_end_ng` | `/arm_3_pick_place` | 2 | `pick_ng` |

## 파일
| 파일 | 역할 |
|---|---|
| `dt_palletizer/stacker.py` | 판단 (rclpy 없음): 도착 순서 대기열, 호출 조건, 응답 처리, 20초 시간 초과, retry, 해제 초기화 |
| `dt_palletizer/palletizer_node.py` | ROS 연결: 토픽·서비스를 Stacker에 넘기고, 돌려준 일(팔 호출, placed·fault 발행)만 한다 |
| `test/test_stacker.py` | 단위 테스트 (스펙 시험 1~7을 판단 단계에서 확인) |

## 동작
- **호출 조건 (모두)**: 공정 RUNNING, 파레트 READY, 적재 수 < 5, 팔 호출 중 아님, 적재 수 확인 기다리는 중 아님. 시작 직후에는 `/factory/state`, `/pallet_N_status`, `/pallet_N_count`를 한 번씩 받을 때까지 기다린다.
- **적재 수는 직접 세지 않는다.** factory_manager의 `/pallet_N_count`를 쓴다.
  - 놓을 높이 z = 파레트 상면(`line_coords.yaml`의 `pallet_N.z`) + 적재 수 × `cushion_height_m` → 1단 0.1, 2단 0.2 … 5단 0.5
  - 성공하면 `/pallet_N_placed`(방석 id)를 보내고, count가 바뀐 것을 본 다음에 다음 방석을 부른다.
- **5개가 차면** 더 부르지 않고 방석 id만 기억한다. 새 파레트가 READY가 되고 count가 0이 되면 기억한 방석부터 1단에 쌓는다.
- **같은 id**가 다시 오면(대기 중, 호출 중, 들고 있음, 방금 놓음) 무시한다. Unity가 끊겼다 붙으면 같은 id를 다시 보낸다.

| 팔 응답 | 처리 |
|---|---|
| 성공 (message = 방석 id) | `/pallet_N_placed` 발행 → count 확인 후 다음 방석 |
| `E_STOP` | 고장 아님. 들고 있다가 해제(E_STOP → IDLE) 때 비움 |
| `busy` | 고장 아님. 1초 뒤 같은 요청 |
| `no_cushion` | 다시 해도 실패하므로 그 id는 버리고 다음 방석 |
| `timeout`, `ik_failed:*`, 20초 무응답 | `/factory/fault` `{"device":"arm_N","reason":"pick_failed","detail":…}` 발행, 요청을 들고 있음 |

- 들고 있는 동안에는 다음 방석도, placed도 보내지 않는다. `/factory/retry`가 내 팔이면 RUNNING일 때 같은 요청(같은 방석, 같은 단)을 **한 번** 다시 보낸다. 들고 있는 요청이 없으면 retry는 무시한다.
- 20초가 지나 포기한 요청이 나중에 성공하면 방석은 이미 파레트 위에 있으므로 놓은 것으로 친다.
- **E_STOP** 중에는 부르지 않는다. E_STOP → IDLE이면 대기열, 들고 있던 요청, retry, count 대기를 모두 비운다 (Unity가 해제할 때 레인 위 방석을 지운다).

## 실행
```bash
ros2 launch dt_bringup factory.launch.py    # palletizer_1·palletizer_2가 같이 뜬다
```
- launch를 먼저 켜고 Unity Play. 레인 끝 id는 도착할 때 한 번만 오므로 palletizer가 늦게 뜨면 이미 와 있는 방석을 모른다.
- `dt_bringup/scripts/m2_run.py`와 같이 돌리지 않는다 (둘 다 팔2·3을 부른다).

## 시험
```bash
cd ros2_ws/src/dt_palletizer && python3 -m pytest -q test
```
