// ConveyorLink.cs : 컨베이어 ↔ ROS2 연결 (FR-03, FR-04). 담당 A · scope unity-ros
// ROS 통신은 Scripts/ROS/ 에서만 한다 (CONVENTIONS 3절).
//
//   구독: /conveyor/cmd       (std_msgs/Bool)   true=가동, false=정지. 메인 라인과 두 레인 함께
//   발행: /conveyor_1_load    (std_msgs/String) JSON {"main":1,"ok":2,"ng":0,"ok_end":1,"ng_end":0}. 바뀔 때 + 1초마다 (FR-04)
//         ok_end·ng_end: 레인 끝 칸 2개 중 찬 칸 수 (11/02 A, 둘 중 하나라도 2면 factory_manager가 컨베이어와 팔1 투입을 멈춤)
//         값은 Line/ConveyorLoad.cs가 센다. 같은 오브젝트에 없으면 자동으로 붙인다.
//
// 11/01 A: v2.2의 /conveyor/box_count(Int32) 발행을 /conveyor_1_load로 바꿈.
//          TopicNames.ConveyorBoxCount는 이제 아무도 안 쓴다 (D가 정리).
//
// 같은 오브젝트에 ConveyorGroup이 있으면 그룹(구간 전체)을, 없으면 Conveyor 하나를 가동·정지한다.
//
// 11/05 (BUG-003): RosBridge.FactoryStateChanged 구독. E_STOP → IDLE이면 ConveyorLoad.ClearLine()으로
//   적재함 밖 방석을 모두 없앤다 (FR-16 해제 초기화). factory_manager가 통계·파레트 수를 0으로 돌릴 때
//   라인 위 방석이 남아 있으면 Clear 뒤 파레트에 쌓여 숫자가 어긋났다.
//
// 확인 (VM):
//   ros2 topic echo /conveyor_1_load
//   ros2 topic pub --once /conveyor/cmd std_msgs/msg/Bool "{data: true}"

using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class ConveyorLink : MonoBehaviour
{
    [SerializeField] private Conveyor conveyor;
    [SerializeField] private ConveyorGroup group;
    [SerializeField] private ConveyorLoad load;

    [Tooltip("값이 안 바뀌어도 이 간격(초)마다 다시 보낸다 (FR-04: factory_manager는 3초 못 받으면 투입 안 함)")]
    [SerializeField] private float republishSec = 1.0f;

    private ROSConnection ros;
    private RosBridge bridge;
    private string lastState = "";
    private float lastSent = -999f;

    void Reset()
    {
        group = GetComponent<ConveyorGroup>();
        conveyor = GetComponent<Conveyor>();
        load = GetComponent<ConveyorLoad>();
    }

    void Awake()
    {
        if (load == null) load = GetComponent<ConveyorLoad>();
        if (load == null) load = gameObject.AddComponent<ConveyorLoad>();
    }

    void Start()
    {
        if (group == null) group = GetComponent<ConveyorGroup>();
        if (group == null && conveyor == null) conveyor = GetComponent<Conveyor>();
        if (group == null && conveyor == null)
        {
            Debug.LogError("[컨베이어 연결] Conveyor나 ConveyorGroup이 연결되지 않음", this);
            enabled = false;
            return;
        }

        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<StringMsg>(TopicNames.Conveyor1Load);
        ros.Subscribe<BoolMsg>(TopicNames.ConveyorCmd, OnConveyorCmd);

        load.Changed += OnLoadChanged;

        bridge = RosBridge.Instance;
        if (bridge != null)
        {
            lastState = bridge.FactoryState;
            bridge.FactoryStateChanged += OnFactoryState;
        }
        else
        {
            Debug.LogWarning("[컨베이어 연결] 씬에 RosBridge가 없어 E_STOP 해제 때 라인 방석을 지우지 못함", this);
        }
    }

    void OnDestroy()
    {
        if (load != null) load.Changed -= OnLoadChanged;
        if (ros != null) ros.Unsubscribe(TopicNames.ConveyorCmd);
        if (bridge != null) bridge.FactoryStateChanged -= OnFactoryState;
    }

    void OnFactoryState(string state)
    {
        if (lastState == "E_STOP" && state == "IDLE") load.ClearLine();
        lastState = state;
    }

    void Update()
    {
        if (ros != null && Time.unscaledTime - lastSent >= republishSec) Publish();
    }

    void OnConveyorCmd(BoolMsg msg)
    {
        if (group != null) group.SetRunning(msg.data);
        else conveyor.SetRunning(msg.data);
    }

    void OnLoadChanged(int main, int ok, int ng)
    {
        Publish();
        Debug.Log($"[Unity→ROS] {TopicNames.Conveyor1Load} 방석 수: 메인 {main}개, 정상 레인 {ok}개 (끝 칸 {load.OkEnd}/2), 불량 레인 {ng}개 (끝 칸 {load.NgEnd}/2)", this);
    }

    void Publish()
    {
        if (ros == null) return;
        lastSent = Time.unscaledTime;
        // ROS PC와 연결이 안 됐으면 보내지 않는다 (1초마다 다시 시도하니까 연결되면 바로 나감)
        if (!ros.HasConnectionThread || ros.HasConnectionError) return;

        var msg = new StringMsg($"{{\"main\":{load.Main},\"ok\":{load.Ok},\"ng\":{load.Ng},\"ok_end\":{load.OkEnd},\"ng_end\":{load.NgEnd}}}");
        try
        {
            ros.Publish(TopicNames.Conveyor1Load, msg);
        }
        catch (System.Exception)
        {
            // 연결이 끊겼다 다시 붙으면서 발행 등록이 사라진 경우: 다시 등록하고 한 번 더
            ros.RegisterPublisher<StringMsg>(TopicNames.Conveyor1Load);
            ros.Publish(TopicNames.Conveyor1Load, msg);
        }
    }
}