# dt_fake_unity  ·  담당 D  ·  scope `fake`

Unity가 보내야 할 토픽·서비스를 흉내 낸다. ROS 담당(B, C, D)이 Unity 없이 자기 노드를 테스트할 때 쓴다.
**Endpoint와 동시에 띄우지 않는다** (같은 토픽을 둘이 발행하게 됨).

## 노드
| 실행 파일 | 역할 |
|---|---|
| `fake_unity_node` | 감지 이벤트, 박스 수, AGV 상태, Heartbeat 발행 + 팔 4대 서비스 서버 |
| `fake_panel` | 터미널 키로 [동작]·[비상정지]·[팔레트 정리]·[고장 주입]·[조치 완료] |

## 실행
```bash
ros2 launch dt_bringup fake.launch.py
ros2 run dt_fake_unity fake_panel        # 다른 터미널
```
