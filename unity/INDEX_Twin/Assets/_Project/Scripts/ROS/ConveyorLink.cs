// ConveyorLink.cs : 컨베이어 ↔ ROS2 연결 (FR-03, FR-04). 담당 A · scope unity-ros
// ROS 통신은 Scripts/ROS/ 에서만 한다 (CONVENTIONS 3절).
//
//   구독: /conveyor/cmd        (std_msgs/Bool)  true=가동, false=정지
//   발행: /conveyor/box_count  (std_msgs/Int32) 벨트 위 박스 수, 바뀔 때
//
// 같은 오브젝트에 ConveyorGroup이 있으면 그룹(구간 전체)을, 없으면 Conveyor 하나를 쓴다.
//
// 확인 (VM):
//   ros2 topic echo /conveyor/box_count
//   ros2 topic pub --once /conveyor/cmd std_msgs/msg/Bool "{data: true}"

using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class ConveyorLink : MonoBehaviour
{
    [SerializeField] private Conveyor conveyor;
    [SerializeField] private ConveyorGroup group;

    private ROSConnection ros;

    void Reset()
    {
        group = GetComponent<ConveyorGroup>();
        conveyor = GetComponent<Conveyor>();
    }

    void Start()
    {
        if (group == null) group = GetComponent<ConveyorGroup>();
        if (group == null && conveyor == null) conveyor = GetComponent<Conveyor>();
        if (group == null && conveyor == null)
        {
            Debug.LogError("[ConveyorLink] Conveyor나 ConveyorGroup이 연결되지 않음", this);
            enabled = false;
            return;
        }

        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<Int32Msg>(TopicNames.ConveyorBoxCount);
        ros.Subscribe<BoolMsg>(TopicNames.ConveyorCmd, OnConveyorCmd);

        if (group != null) group.BoxCountChanged += PublishBoxCount;
        else conveyor.BoxCountChanged += PublishBoxCount;

        PublishBoxCount(group != null ? Mathf.Max(0, group.BoxCount) : Mathf.Max(0, conveyor.BoxCount));
    }

    void OnDestroy()
    {
        if (group != null) group.BoxCountChanged -= PublishBoxCount;
        if (conveyor != null) conveyor.BoxCountChanged -= PublishBoxCount;
        if (ros != null) ros.Unsubscribe(TopicNames.ConveyorCmd);
    }

    void OnConveyorCmd(BoolMsg msg)
    {
        if (group != null) group.SetRunning(msg.data);
        else conveyor.SetRunning(msg.data);
    }

    void PublishBoxCount(int count)
    {
        if (ros == null) return;
        ros.Publish(TopicNames.ConveyorBoxCount, new Int32Msg(count));
    }
}