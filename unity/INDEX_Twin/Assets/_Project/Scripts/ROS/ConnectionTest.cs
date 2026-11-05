// 팀원 세팅 검증용 (chore #1). 담당 D · scope unity-ros
// 씬: Assets/_Project/Scenes/Sandbox/ConnectionTest.unity 의 빈 GameObject에 붙인다.
//
// 확인 항목
//   1) Unity → ROS : ros2 topic echo /unity_ping   (1초마다 ping N)
//   2) ROS → Unity : ros2 topic pub --once /conveyor/cmd std_msgs/msg/Bool "{data: true}"  → Console 로그
//
// v3.1: /detect_1_event(DetectEvent)는 삭제되어 커스텀 메시지·좌표 변환 확인은 뺐다.

using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class ConnectionTest : MonoBehaviour
{
    ROSConnection ros;
    float timer;
    int count;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();

        ros.RegisterPublisher<StringMsg>(TopicNames.UnityPing);

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
    }
}
