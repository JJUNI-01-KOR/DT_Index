// 컨베이어 ↔ ROS2 연결 (FR-03, FR-04). 담당 A · scope unity-ros
// ROS 통신은 Scripts/ROS/ 에서만 한다 (CONVENTIONS 3절). Conveyor.cs는 ROS를 모른다.
//
//   구독: /conveyor/cmd        (std_msgs/Bool)  true=가동, false=정지
//   발행: /conveyor/box_count  (std_msgs/Int32) 벨트 위 박스 수, 바뀔 때
//
// 확인 (VM):
//   ros2 topic echo /conveyor/box_count
//   ros2 topic pub --once /conveyor/cmd std_msgs/msg/Bool "{data: true}"
//   ros2 topic pub --once /conveyor/cmd std_msgs/msg/Bool "{data: false}"

using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class ConveyorRosLink : MonoBehaviour
{
    [SerializeField] private Conveyor conveyor;

    private ROSConnection ros;

    void Reset() => conveyor = GetComponent<Conveyor>();

    void Start()
    {
        if (conveyor == null) conveyor = GetComponent<Conveyor>();
        if (conveyor == null) { Debug.LogError("[ConveyorRosLink] Conveyor가 연결되지 않음", this); enabled = false; return; }

        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<Int32Msg>(TopicNames.ConveyorBoxCount);
        ros.Subscribe<BoolMsg>(TopicNames.ConveyorCmd, OnConveyorCmd);

        conveyor.BoxCountChanged += PublishBoxCount;
        PublishBoxCount(conveyor.BoxCount); // 시작 값 1회
    }

    void OnDestroy()
    {
        if (conveyor != null) conveyor.BoxCountChanged -= PublishBoxCount;
        if (ros != null) ros.Unsubscribe(TopicNames.ConveyorCmd);
    }

    void OnConveyorCmd(BoolMsg msg) => conveyor.SetRunning(msg.data);

    void PublishBoxCount(int count)
    {
        if (ros == null) return;
        ros.Publish(TopicNames.ConveyorBoxCount, new Int32Msg(count));
    }
}
