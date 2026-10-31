// PalletSlotLink.cs : 파레트 자리 → ROS2 (FR-15, FR-16). 담당 D · scope unity-ros
// ROS 통신은 Scripts/ROS/ 에서만 한다. 상태 판단은 AGV/PalletSlot.cs가 한다.
//
//   발행: /pallet_N_status (std_msgs/String) READY · OUT · REFILLING. 바뀔 때와 1초마다 (N = PalletSlot.slotId)
//   구독: RosBridge.FactoryStateChanged
//           E_STOP        → 보충 중이면 그 자리에서 멈춤
//           E_STOP → IDLE → 비어 있는 자리에 빈 파레트를 바로 놓음 (FR-16 5번)
//
// 확인 (VM): ros2 topic echo /pallet_2_status

using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

[DisallowMultipleComponent]
[RequireComponent(typeof(PalletSlot))]
public class PalletSlotLink : MonoBehaviour
{
    [SerializeField] private float periodSec = 1f;

    private PalletSlot slot;
    private ROSConnection ros;
    private RosBridge bridge;
    private string topic;
    private string lastState = "";
    private float timer;

    void Start()
    {
        slot = GetComponent<PalletSlot>();
        topic = TopicNames.PalletStatus(slot.SlotId);
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<StringMsg>(topic);
        slot.StateChanged += _ => Publish(true);

        bridge = RosBridge.Instance;
        if (bridge != null)
        {
            lastState = bridge.FactoryState;
            bridge.FactoryStateChanged += OnFactoryState;
        }
        Publish(false);
    }

    void OnDestroy()
    {
        if (bridge != null) bridge.FactoryStateChanged -= OnFactoryState;
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= periodSec) Publish(false);
    }

    void Publish(bool changed)
    {
        timer = 0f;
        if (ros == null) return;
        string s = slot.State.ToString();
        ros.Publish(topic, new StringMsg(s));
        if (changed) Debug.Log($"[Unity→ROS] {topic} {s}", this);
    }

    void OnFactoryState(string state)
    {
        if (state == "E_STOP") slot.Halt();
        else if (lastState == "E_STOP" && state == "IDLE") slot.ResetAfterEStop();
        lastState = state;
    }
}
