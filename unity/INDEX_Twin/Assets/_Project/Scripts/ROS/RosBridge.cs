// ROS 통신 창구 (NFR-08). 담당 D · scope unity-ros
// 씬에 하나만 둔다. ROS를 모르는 스크립트(UI 등)는 RosBridge.Instance로 발행·구독한다.
// ROS 통신은 Scripts/ROS/ 에서만 한다 (CONVENTIONS 3절). 토픽 이름은 TopicNames 상수만 쓴다.
//
//   발행: /factory/start  (std_msgs/Bool)    [동작] 버튼 → true 1번
//   구독: /factory/state  (std_msgs/String)  IDLE, RUNNING, PALLET_FULL, ERROR, E_STOP → FactoryStateChanged
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
        ros.Subscribe<StringMsg>(TopicNames.FactoryState, OnFactoryState);
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        if (ros != null) ros.Unsubscribe(TopicNames.FactoryState);
    }

    void OnFactoryState(StringMsg msg)
    {
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
}
