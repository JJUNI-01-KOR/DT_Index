// BinLink.cs : 적재함 → ROS2 (FR-02, FR-05). 담당 A · scope unity-ros
// ROS 통신은 Scripts/ROS/ 에서만 한다 (CONVENTIONS 3절). CushionBin.cs는 ROS를 모른다.
//
//   발행: /bin/count (std_msgs/Int32) 적재함 남은 방석 수. 바뀔 때 + 1초마다
//
// 11/03: 1초마다 다시 보내기 추가 (ConveyorLink와 같은 방식).
//   ROS 토픽은 늦게 구독한 쪽에 지난 값을 다시 주지 않는다. 바뀔 때만 보내면
//   Play 직후 ROS 연결 전이거나 factory_manager가 늦게 켜지면(재시작 포함) 첫 값(20)을 못 받고,
//   factory_manager는 적재함 0개로 보고 투입을 안 한다 → 투입이 없으니 값도 안 바뀌어 계속 0.
//   - ROS와 연결이 안 됐으면 보내지 않는다 (1초마다 다시 시도하니까 연결되면 바로 나감)
//   - 끊겼다 붙으면서 발행 등록이 사라졌으면 다시 등록한다
//
// 확인 (VM):
//   ros2 topic hz /bin/count     ← 값이 안 바뀌어도 약 1 Hz
//   factory_manager를 Ctrl+C 했다가 다시 켜기 → 1초 안에 적재함 수를 받고 투입 시작

using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class BinLink : MonoBehaviour
{
    [SerializeField] private CushionBin bin;

    [Tooltip("값이 안 바뀌어도 이 간격(초)마다 다시 보낸다 (factory_manager가 늦게 켜지거나 재시작해도 현재 값을 받게)")]
    [SerializeField] private float republishSec = 1.0f;

    private ROSConnection ros;
    private float lastSent = -999f;

    void Reset() => bin = GetComponent<CushionBin>();

    void Awake()
    {
        if (bin == null) bin = GetComponent<CushionBin>();
        if (bin == null) { Debug.LogError("[BinLink] CushionBin이 연결되지 않음", this); enabled = false; return; }

        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher(TopicNames.BinCount, Int32Msg.k_RosMessageName); // 빌드에선 Awake 때 메시지 이름 등록 전이라 <Int32Msg>로는 이름이 비어 등록 실패
        bin.CountChanged += OnCountChanged; // CushionBin.Start에서 채우기 전에 구독해야 첫 값(20)도 바로 나감
    }

    void OnDestroy()
    {
        if (bin != null) bin.CountChanged -= OnCountChanged;
    }

    void Update()
    {
        if (ros != null && Time.unscaledTime - lastSent >= republishSec) Publish();
    }

    void OnCountChanged(int count) => Publish();

    void Publish()
    {
        if (ros == null || bin == null) return;
        lastSent = Time.unscaledTime;
        // ROS PC와 연결이 안 됐으면 보내지 않는다 (1초마다 다시 시도하니까 연결되면 바로 나감)
        if (!ros.HasConnectionThread || ros.HasConnectionError) return;

        var msg = new Int32Msg(bin.Count);
        try
        {
            ros.Publish(TopicNames.BinCount, msg);
        }
        catch (System.Exception)
        {
            // 연결이 끊겼다 다시 붙으면서 발행 등록이 사라진 경우: 다시 등록하고 한 번 더
            ros.RegisterPublisher(TopicNames.BinCount, Int32Msg.k_RosMessageName); // 빌드에선 Awake 때 메시지 이름 등록 전이라 <Int32Msg>로는 이름이 비어 등록 실패
            ros.Publish(TopicNames.BinCount, msg);
        }
    }
}
