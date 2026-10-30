// LaneEndLink.cs : 레인 끝 → ROS2 (FR-09, FR-16). scope unity-ros
// ROS 통신은 Scripts/ROS/ 에서만 한다 (CONVENTIONS 3절). LaneEnd.cs는 ROS를 모른다.
//
//   발행: /conveyor_1_end_ok, /conveyor_1_end_ng (std_msgs/String)
//         방석이 집는 자리(1번 칸)에 들어올 때마다 그 방석 id 1회
//   구독: RosBridge.FactoryStateChanged — E_STOP → IDLE이면 LaneEnd.ResetAll() (FR-16 해제 초기화)
//
// 붙이는 곳: LaneEnd와 같은 Stopper. Lane은 정상 레인(Unity -Z, BufferZoneSensor 2) Ok, 불량 레인(+Z, BufferZoneSensor) Ng.
//
// 확인 (VM):
//   ros2 topic echo /conveyor_1_end_ok
//   ros2 topic echo /conveyor_1_end_ng

using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

[DisallowMultipleComponent]
[RequireComponent(typeof(LaneEnd))]
public class LaneEndLink : MonoBehaviour
{
    public enum Lane { Ok, Ng }

    [Tooltip("Ok: 정상 레인 → /conveyor_1_end_ok, Ng: 불량 레인 → /conveyor_1_end_ng")]
    [SerializeField] private Lane lane = Lane.Ok;
    [SerializeField] private LaneEnd laneEnd;

    private ROSConnection ros;
    private RosBridge bridge;
    private string topic;
    private string lastState = "";

    void Reset() => laneEnd = GetComponent<LaneEnd>();

    void Start()
    {
        if (laneEnd == null) laneEnd = GetComponent<LaneEnd>();
        topic = lane == Lane.Ok ? TopicNames.Conveyor1EndOk : TopicNames.Conveyor1EndNg;

        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<StringMsg>(topic);
        laneEnd.ArrivedAtPickSpot += Publish;

        bridge = RosBridge.Instance;
        if (bridge != null)
        {
            lastState = bridge.FactoryState;
            bridge.FactoryStateChanged += OnFactoryState;
        }
        else
        {
            Debug.LogWarning("[LaneEndLink] 씬에 RosBridge가 없어 E_STOP 해제 초기화를 하지 못함", this);
        }
    }

    void OnDestroy()
    {
        if (laneEnd != null) laneEnd.ArrivedAtPickSpot -= Publish;
        if (bridge != null) bridge.FactoryStateChanged -= OnFactoryState;
    }

    void Publish(GameObject cushion)
    {
        if (ros == null || cushion == null) return;
        ros.Publish(topic, new StringMsg(cushion.name));
        Debug.Log($"[Unity→ROS] {topic} {cushion.name}");
    }

    void OnFactoryState(string state)
    {
        if (lastState == "E_STOP" && state == "IDLE") laneEnd.ResetAll();
        lastState = state;
    }
}
