// ROS 통신 창구 (NFR-08). 담당 D · scope unity-ros
// 씬에 하나만 둔다. ROS를 모르는 스크립트(UI 등)는 RosBridge.Instance로 발행·구독한다.
// ROS 통신은 Scripts/ROS/ 에서만 한다 (CONVENTIONS 3절). 토픽 이름은 TopicNames 상수만 쓴다.
//
//   발행: /factory/start  (std_msgs/Bool)    [동작] 버튼 → true 1번
//         /factory/emergency_stop (std_msgs/Bool)  [Emergency] → true, [Clear] → false (해제)
//         /factory/fault_clear (std_msgs/String)   [Fix] 3초 → 고장 장비 id
//   구독: /factory/state  (std_msgs/String)  IDLE, RUNNING, ERROR, E_STOP → FactoryStateChanged
//         /factory/stats  (std_msgs/String, JSON) → StatsReceived, CurrentFault (fault 값)
//   통신 끊김 (FR-17): HeartbeatLink를 자동으로 붙인다. 끊기면 SetRosLost(true) → FactoryState를 E_STOP으로 바꿔
//         FactoryStateChanged를 구독하는 설비(팔·AGV·파레트 자리 등)가 실제 E_STOP과 같은 길로 멈춘다.
//         복구되면 /factory/emergency_stop true를 1번 보내 factory_manager도 E_STOP으로 맞춘다 → [Clear]로 해제
//
// 확인 (VM):
//   ros2 launch dt_bringup connect.launch.py
//   ros2 topic echo /factory/start                                   ← [동작] 1번에 data: true 1번
//   ros2 topic pub --once /factory/state std_msgs/msg/String "{data: RUNNING}"  ← Console에 상태 로그

using System;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

[DisallowMultipleComponent]
public class RosBridge : MonoBehaviour
{
    public static RosBridge Instance { get; private set; }

    /// <summary>/factory/state 값이 바뀔 때 (처음 받은 값 포함)</summary>
    public event Action<string> FactoryStateChanged;

    /// <summary>마지막으로 받은 공정 상태. 아직 못 받았으면 빈 문자열</summary>
    public string FactoryState { get; private set; } = "";

    /// <summary>Endpoint와 연결돼 있는지 (HUD 화살표 파란색과 같은 뜻)</summary>
    public bool Connected => ros != null && ros.HasConnectionThread && !ros.HasConnectionError;

    /// <summary>/factory/stats JSON을 받을 때마다 (상태 패널은 이것만 구독한다, FR-20)</summary>
    public event Action<string> StatsReceived;

    /// <summary>마지막으로 받은 /factory/stats JSON. 아직 못 받았으면 빈 문자열</summary>
    public string LastStatsJson { get; private set; } = "";

    /// <summary>지금 고장 장비 (/factory/stats의 fault). 없으면 빈 문자열</summary>
    public string CurrentFault { get; private set; } = "";

    /// <summary>ROS2에서 마지막으로 무언가(/factory/state·stats·heartbeat_ros)를 받은 시각 (realtimeSinceStartup). 아직 없으면 -1</summary>
    public float LastHeardTime { get; private set; } = -1f;

    /// <summary>Heartbeat가 끊겨 ROS2를 잃었다고 판정했는지 (HeartbeatLink가 정한다, FR-17)</summary>
    public bool RosLost { get; private set; }

    /// <summary>끊김을 판정하거나 복구했을 때 (true = 끊김)</summary>
    public event Action<bool> RosLostChanged;

    /// <summary>버튼을 눌러도 되는 상태: Endpoint 연결 + Heartbeat 정상</summary>
    public bool Healthy => Connected && !RosLost;

    [Serializable]
    private class StatsHead { public string fault; }

    private ROSConnection ros;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[RosBridge] 씬에 RosBridge가 2개 있어 이것은 끔", this);
            enabled = false;
            return;
        }
        Instance = this;
    }

    void Start()
    {
        if (Instance != this) return;
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<BoolMsg>(TopicNames.FactoryStart);
        ros.RegisterPublisher<BoolMsg>(TopicNames.EmergencyStop);
        ros.RegisterPublisher<StringMsg>(TopicNames.FaultClear);
        ros.Subscribe<StringMsg>(TopicNames.FactoryState, OnFactoryState);
        ros.Subscribe<StringMsg>(TopicNames.FactoryStats, OnStats);
        if (GetComponent<HeartbeatLink>() == null) gameObject.AddComponent<HeartbeatLink>();   // FR-17
    }

    /// <summary>ROS2에서 무언가 받았음 (Heartbeat 판정용)</summary>
    public void NotifyHeard() => LastHeardTime = Time.realtimeSinceStartup;

    /// <summary>HeartbeatLink가 끊김·복구를 알린다 (FR-17)</summary>
    public void SetRosLost(bool lost)
    {
        if (lost == RosLost) return;
        RosLost = lost;
        if (lost)
        {
            Debug.LogWarning($"[Heartbeat] ROS2 끊김: {Time.realtimeSinceStartup - LastHeardTime:F1}초 동안 받은 것 없음 → Unity 설비 E_STOP", this);
            if (FactoryState != "E_STOP")
            {
                FactoryState = "E_STOP";
                FactoryStateChanged?.Invoke("E_STOP");
            }
        }
        else
        {
            Debug.Log("[Heartbeat] ROS2 복구 → factory_manager도 E_STOP으로 맞춤. [Clear]로 해제", this);
            PublishEmergencyStop(true);
        }
        RosLostChanged?.Invoke(lost);
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        if (ros != null)
        {
            ros.Unsubscribe(TopicNames.FactoryState);
            ros.Unsubscribe(TopicNames.FactoryStats);
        }
    }

    void OnStats(StringMsg msg)
    {
        NotifyHeard();
        LastStatsJson = msg.data;
        try
        {
            var head = JsonUtility.FromJson<StatsHead>(msg.data);
            CurrentFault = head != null && head.fault != null ? head.fault : "";
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RosBridge] {TopicNames.FactoryStats} JSON 해석 실패: {e.Message}", this);
        }
        StatsReceived?.Invoke(msg.data);
    }

    void OnFactoryState(StringMsg msg)
    {
        NotifyHeard();
        if (RosLost) return;                    // 끊김 판정 중에는 Unity 쪽 E_STOP 유지 (다음 Update에서 복구 판정)
        if (msg.data == FactoryState) return;   // factory_manager가 1초마다 보내므로 바뀔 때만 알림
        Debug.Log($"[ROS→Unity] {TopicNames.FactoryState} {(FactoryState == "" ? "(처음)" : FactoryState)} → {msg.data}");
        FactoryState = msg.data;
        FactoryStateChanged?.Invoke(msg.data);
    }

    /// <summary>[동작] 버튼: /factory/start true를 1번 발행</summary>
    public void PublishStart()
    {
        if (ros == null)
        {
            Debug.LogWarning("[RosBridge] ROS 연결 준비 전이라 /factory/start를 보내지 못함", this);
            return;
        }
        ros.Publish(TopicNames.FactoryStart, new BoolMsg(true));
        Debug.Log($"[Unity→ROS] {TopicNames.FactoryStart} = true");
    }

    /// <summary>[Emergency] true(누름) · [Clear] false(해제): /factory/emergency_stop 1번 (FR-16)</summary>
    public void PublishEmergencyStop(bool pressed)
    {
        if (ros == null)
        {
            Debug.LogWarning($"[RosBridge] ROS 연결 준비 전이라 {TopicNames.EmergencyStop}을 보내지 못함", this);
            return;
        }
        ros.Publish(TopicNames.EmergencyStop, new BoolMsg(pressed));
        Debug.Log($"[Unity→ROS] {TopicNames.EmergencyStop} = {(pressed ? "true (비상정지)" : "false (해제)")}");
    }

    /// <summary>[Fix] 3초 조치 완료: /factory/fault_clear (장비 id) 1번 (FR-18)</summary>
    public void PublishFaultClear(string device)
    {
        if (ros == null || string.IsNullOrEmpty(device))
        {
            Debug.LogWarning($"[RosBridge] {TopicNames.FaultClear}을 보내지 못함 (연결 준비 전이거나 장비 id 없음)", this);
            return;
        }
        ros.Publish(TopicNames.FaultClear, new StringMsg(device));
        Debug.Log($"[Unity→ROS] {TopicNames.FaultClear} = {device}");
    }
}
