// HeartbeatLink.cs : 통신 끊김 감지 Unity 쪽 (FR-17, NFR-04). 담당 D · scope unity-ros
// RosBridge가 Play 때 같은 오브젝트에 자동으로 붙인다 (씬 수정 없음). 값을 바꾸려면 씬에서 직접 붙여 Inspector로 고친다.
//
//   발행: /factory/heartbeat_unity (std_msgs/Bool, true)  periodSec(1초)마다
//   구독: /factory/heartbeat_ros   (std_msgs/Bool)        → RosBridge.NotifyHeard
//   판정: ROS2에서 마지막으로 받은 것(heartbeat_ros, /factory/state, /factory/stats)이 timeoutSec(3초)보다 오래되면 끊김
//         → RosBridge.SetRosLost(true): Unity 설비를 E_STOP처럼 그 자리에서 멈추고, 제어 패널 버튼을 끄고 "ROS2 LOST" 표시
//           (factory_manager도 같은 규칙으로 E_STOP. 상태 전환은 B 담당)
//         다시 받으면 복구 → /factory/emergency_stop true를 1번 보내 factory_manager도 E_STOP으로 맞춘다 → [Clear]로 해제
//   처음 한 번이라도 받기 전에는 판정하지 않는다 (ROS를 Play보다 늦게 켜도 끊김으로 보지 않음)
//   /factory/state도 살아 있다는 신호로 본다: factory_manager가 1초마다 보내므로 heartbeat_ros가 아직 없어도 오판하지 않는다
//
// 확인 (VM):
//   ros2 topic echo /factory/heartbeat_unity        ← 1초마다 data: true
//   factory_manager를 Ctrl+C로 끄면 3초 뒤 Console에 "[Heartbeat] ROS2 끊김", 다시 켜면 "복구"

using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

[DisallowMultipleComponent]
public class HeartbeatLink : MonoBehaviour
{
    [Tooltip("/factory/heartbeat_unity 보내는 주기 (초)")]
    [SerializeField] private float periodSec = 1f;
    [Tooltip("ROS2에서 이 시간(초) 동안 아무것도 못 받으면 끊김")]
    [SerializeField] private float timeoutSec = 3f;

    private ROSConnection ros;
    private RosBridge bridge;
    private float nextBeat;

    void Start()
    {
        bridge = RosBridge.Instance;
        if (bridge == null)
        {
            Debug.LogWarning("[Heartbeat] 씬에 RosBridge가 없어 끔", this);
            enabled = false;
            return;
        }
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<BoolMsg>(TopicNames.HeartbeatUnity);
        ros.Subscribe<BoolMsg>(TopicNames.HeartbeatRos, _ => bridge.NotifyHeard());
    }

    void OnDestroy()
    {
        if (ros != null) ros.Unsubscribe(TopicNames.HeartbeatRos);
    }

    void Update()
    {
        float now = Time.realtimeSinceStartup;
        if (now < nextBeat) return;
        nextBeat = now + periodSec;
        if (bridge.Connected) ros.Publish(TopicNames.HeartbeatUnity, new BoolMsg(true));
    }

    // 판정은 LateUpdate에서: 에디터가 몇 초 멈췄다 풀린 프레임에도 ROSConnection이 Update에서 밀린 메시지를 먼저 처리한 뒤라
    // "Unity가 멈춰서 못 받은 것"을 ROS 끊김으로 잘못 보지 않는다
    void LateUpdate()
    {
        if (bridge.LastHeardTime < 0f) return;   // 아직 한 번도 못 받음
        bool lost = Time.realtimeSinceStartup - bridge.LastHeardTime > timeoutSec;
        if (lost != bridge.RosLost) bridge.SetRosLost(lost);
    }
}
