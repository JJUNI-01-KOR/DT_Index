# factory_interfaces  ·  담당 B  ·  scope `interfaces`

커스텀 메시지 2개만 둔다. 나머지는 표준 타입(std_msgs, geometry_msgs)을 쓴다.

| 파일 | 토픽/서비스 | 필드 |
|---|---|---|
| `msg/DetectEvent.msg` | `/detect_1_event`, `/detect_2_event` | `box_id`, `zone`, `position` |
| `srv/PickPlace.srv` | `/arm_1~4_pick_place` | 요청 `box_id`, `pick_pose`, `place_pose` / 응답 `success`, `message` |

## 확인
```bash
ros2 interface show factory_interfaces/msg/DetectEvent
ros2 interface show factory_interfaces/srv/PickPlace
```

## ⚠ 수정할 때
1. 이 패키지를 고치면 `colcon build` 후 **Endpoint를 재시작**한다.
2. Unity에서 **Robotics → Generate ROS Messages**로 C#을 다시 생성해 `Assets/RosMessages/`를 같은 PR에 커밋한다.
3. 필드 변경은 4명 합의 사항 (SRS 4.4 계약).
