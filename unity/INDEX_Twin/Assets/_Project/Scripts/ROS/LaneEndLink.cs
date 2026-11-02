// LaneEndLink.cs : 레인 끝 → ROS2 (FR-09, FR-16). scope unity-ros
// ROS 통신은 Scripts/ROS/ 에서만 한다 (CONVENTIONS 3절). LaneEnd.cs는 ROS를 모른다.
//
//   발행: /conveyor_1_end_ok, /conveyor_1_end_ng (std_msgs/String)
//         방석이 집는 자리(1번 칸)에 들어올 때마다 그 방석 id 1회
//         11/02: ROS와 끊겨 있으면 보내지 않고 모아 두었다가 다시 붙으면 보낸다 (그때도 집는 자리에 있는 방석만).
//         끊겼다 붙으며 발행 등록이 사라졌으면 다시 등록한다 (ConveyorLink와 같은 방식)
//   구독: RosBridge.FactoryStateChanged — E_STOP → IDLE이면 LaneEnd.ResetAll() (FR-16 해제 초기화)
//
// 붙이는 곳: LaneEnd와 같은 Stopper. Lane은 정상 레인(Unity -Z, BufferZoneSensor 2) Ok, 불량 레인(+Z, BufferZoneSensor) Ng.
//
// 확인 (VM):
//   ros2 topic echo /conveyor_1_end_ok
//   ros2 topic echo /conveyor_1_end_ng

using System.Collections.Generic;
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
    private readonly List<string> pending = new List<string>();   // 아직 못 보낸 방석 id

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
        pending.Add(cushion.name);
        Flush();
    }

    void Update()
    {
        if (pending.Count > 0) Flush();   // 끊겨서 못 보낸 id가 있으면 연결될 때 보낸다
    }

    void Flush()
    {
        if (!ros.HasConnectionThread || ros.HasConnectionError) return;   // 연결되면 Update에서 다시
        for (int i = 0; i < pending.Count; i++)
        {
            string id = pending[i];
            if (!LaneEnd.TryGetAtPick(id, out _)) { Debug.Log($"[LaneEndLink] {id}: 이미 집는 자리에 없어 보내지 않음", this); continue; }
            var msg = new StringMsg(id);
            try
            {
                ros.Publish(topic, msg);
            }
            catch (System.Exception)
            {
                // 연결이 끊겼다 다시 붙으면서 발행 등록이 사라진 경우: 다시 등록하고 한 번 더
                ros.RegisterPublisher<StringMsg>(topic);
                ros.Publish(topic, msg);
            }
            Debug.Log($"[Unity→ROS] {topic} {id}");
        }
        pending.Clear();
    }

    void OnFactoryState(string state)
    {
        if (lastState == "E_STOP" && state == "IDLE") { laneEnd.ResetAll(); pending.Clear(); }
        lastState = state;
    }
}
