// ScaleLink.cs : 무게 센서 → ROS2 연결 (FR-06). scope unity-ros
// ROS 통신은 Scripts/ROS/ 에서만 한다 (CONVENTIONS 3절).
//
//   발행: /scale_1_weight (std_msgs/String) JSON {"id":"C001","kg":1.02}. 방석당 1회
//
// 씬의 WeightScanner에 Play 때 자동으로 붙는다. Main.unity를 고치지 않아도 된다.
//
// 확인 (VM):
//   ros2 topic info /scale_1_weight
//   ros2 topic echo /scale_1_weight

using System.Globalization;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class ScaleLink : MonoBehaviour
{
    private WeightScanner scanner;
    private ROSConnection ros;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AttachToScanner()
    {
        var found = Object.FindAnyObjectByType<WeightScanner>();
        if (found != null && found.GetComponent<ScaleLink>() == null)
            found.gameObject.AddComponent<ScaleLink>();
    }

    void Start()
    {
        scanner = GetComponent<WeightScanner>();
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<StringMsg>(TopicNames.Scale1Weight);
        scanner.Scanned += OnScanned;
    }

    void OnDestroy()
    {
        if (scanner != null) scanner.Scanned -= OnScanned;
    }

    void OnScanned(ItemSort sort, float kg)
    {
        // 11/05 BUG-008 (TC_DT_043): Inspector에서 ScaleLink를 끄면(무게 센서 고장 흉내) 무게를 보내지 않는다.
        //   이벤트 구독은 enabled와 상관없이 살아 있어서 따로 확인해야 함
        if (!enabled)
        {
            Debug.Log($"[무게 연결] ScaleLink가 꺼져 있어 {sort.name} 무게를 보내지 않음", this);
            return;
        }
        var cushion = sort.GetComponent<Cushion>();
        if (cushion == null || string.IsNullOrEmpty(cushion.CushionId))
        {
            Debug.LogWarning($"[무게 연결] 방석 id가 없어 무게를 보내지 않음: {sort.name}", this);
            return;
        }
        if (!ros.HasConnectionThread || ros.HasConnectionError)
        {
            Debug.LogWarning($"[무게 연결] ROS2 연결이 없어 {cushion.CushionId} 무게를 보내지 못함", this);
            return;
        }

        string kgText = kg.ToString("F2", CultureInfo.InvariantCulture);
        var msg = new StringMsg($"{{\"id\":\"{cushion.CushionId}\",\"kg\":{kgText}}}");
        ros.Publish(TopicNames.Scale1Weight, msg);
        Debug.Log($"[Unity→ROS] {TopicNames.Scale1Weight} {msg.data}", this);
    }
}