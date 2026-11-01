// BinLink.cs : 적재함 → ROS2 (FR-02, FR-05). 담당 A · scope unity-ros
// ROS 통신은 Scripts/ROS/ 에서만 한다 (CONVENTIONS 3절). CushionBin.cs는 ROS를 모른다.
//
//   발행: /bin/count (std_msgs/Int32) 적재함 남은 박스 수, 바뀔 때
//
// 확인 (VM): ros2 topic echo /bin/count

using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class BinLink : MonoBehaviour
{
    [SerializeField] private CushionBin bin;

    private ROSConnection ros;

    void Reset() => bin = GetComponent<CushionBin>();

    void Awake()
    {
        if (bin == null) bin = GetComponent<CushionBin>();
        if (bin == null) { Debug.LogError("[BinLink] CushionBin이 연결되지 않음", this); enabled = false; return; }

        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<Int32Msg>(TopicNames.BinCount);
        bin.CountChanged += Publish; // CushionBin.Start에서 채우기 전에 구독해야 첫 값(20)도 나감
    }

    void OnDestroy()
    {
        if (bin != null) bin.CountChanged -= Publish;
    }

    void Publish(int count) => ros.Publish(TopicNames.BinCount, new Int32Msg(count));
}