# factory_interfaces  ·  담당 B  ·  scope `interfaces`

커스텀 타입은 `PickPlace.srv` 하나만 둔다 (SRS v3.1 4.4). 나머지는 표준 타입(std_msgs, geometry_msgs)을 쓰고,
필드가 여러 개인 공장 토픽은 `std_msgs/String`에 JSON으로 보낸다.

| 파일 | 서비스 | 필드 |
|---|---|---|
| `srv/PickPlace.srv` | `/arm_1~3_pick_place` | 요청 `box_id`(값은 방석 id `C001`), `pick_pose`, `place_pose` / 응답 `success`, `message` |

`DetectEvent.msg`는 SRS v3.0에서 `/detect_1_event`, `/detect_2_event`와 함께 삭제했다.

## 확인
```bash
ros2 interface show factory_interfaces/srv/PickPlace
```

## ⚠ 수정할 때
1. 이 패키지를 고치면 `colcon build` 후 **Endpoint를 재시작**한다.
2. Unity에서 **Robotics → Generate ROS Messages**로 C#을 다시 생성해 `Assets/RosMessages/`를 같은 PR에 커밋한다.
3. 필드 변경은 4명 합의 사항 (SRS 4.4 계약).
