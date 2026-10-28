// 팀원 세팅 검증용 (chore #1). 담당 D · scope unity-ros
// 씬: Assets/_Project/Scenes/Sandbox/ConnectionTest.unity 의 빈 GameObject에 붙인다.
//
// 확인 항목
//   1) Unity → ROS 표준 메시지   : ros2 topic echo /unity_ping
//   2) Unity → ROS 커스텀 메시지 : ros2 topic echo /detect_1_event   (3초마다)
//   3) ROS → Unity              : ros2 topic pub --once /conveyor/cmd std_msgs/msg/Bool "{data: true}"
//   4) 좌표 변환                 : /detect_1_event 의 position 이 (3, -1, 2) 이면 정상
//
// ⚠ 컴파일 오류 'FactoryInterfaces' 가 나면 Robotics → Generate ROS Messages 로
//   ros2_ws/src/factory_interfaces 를 먼저 생성한다.

using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using RosMessageTypes.FactoryInterfaces;

public class ConnectionTest : MonoBehaviour
{
    [Tooltip("이 위치를 ROS 좌표로 바꿔 /detect_1_event 에 실어 보낸다")]
    public Vector3 testUnityPosition = new Vector3(1f, 2f, 3f);

    ROSConnection ros;
    float timer;
    int count;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();

        ros.RegisterPublisher<StringMsg>(TopicNames.UnityPing);
        ros.RegisterPublisher<DetectEventMsg>(TopicNames.Detect1Event);

        ros.Subscribe<BoolMsg>(TopicNames.ConveyorCmd,
            msg => Debug.Log($"[ROS→Unity] {TopicNames.ConveyorCmd} = {msg.data}"));

        Debug.Log("[ConnectionTest] 시작. VM에서 ros2 topic echo /unity_ping 으로 확인하세요.");
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < 1f) return;
        timer = 0f;
        count++;

        ros.Publish(TopicNames.UnityPing, new StringMsg($"ping {count}"));

        if (count % 3 == 0)
        {
            var ev = new DetectEventMsg(
                $"BOX_{count:000}",
                "reject",
                CoordConverter.ToRosPoint(testUnityPosition));
            ros.Publish(TopicNames.Detect1Event, ev);
        }
    }
}
