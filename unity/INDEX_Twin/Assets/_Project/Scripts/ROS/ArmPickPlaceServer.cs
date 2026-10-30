// ArmPickPlaceServer.cs : /arm_N_pick_place 서비스 서버 (FR-02, FR-10, FR-11, FR-16, FR-18). 담당 D · scope unity-ros
// ROS 통신은 Scripts/ROS/ 에서만 한다 (CONVENTIONS 3절). 팔 동작은 Arms/ArmPickPlace.cs가 한다.
// Arm_Fairino 프리팹 루트에 붙인다. 서비스 이름은 ArmRig의 armId로 정해진다 (팔마다 1개).
//
//   서비스: /arm_N_pick_place (factory_interfaces/PickPlace)
//     요청: box_id (팔1은 빈 문자열), pick_pose · place_pose (ROS 좌표, 방석 바닥 중심. place_pose 방향의 yaw = 놓은 방석 방향)
//     응답: success, message (성공: 방석 id. 실패: busy · E_STOP · no_cushion · ik_failed:<단계> · timeout)
//     Unity 팔은 15초(Inspector) 안에 스스로 끝내므로 호출 쪽 제한 20초 안에 항상 응답한다.
//   구독: RosBridge.FactoryStateChanged
//     E_STOP         → 그 자리에서 멈추고 진행 중 요청에 success=false, message=E_STOP (FR-16 3번)
//     E_STOP → IDLE  → 든 방석을 없애고 홈 자세로 (FR-16 5번)
//
// 확인 (VM):
//   ros2 service list | grep pick_place
//   ros2 service call /arm_3_pick_place factory_interfaces/srv/PickPlace \
//     "{box_id: 'C001', pick_pose: {position: {x: 5.124, y: 15.5, z: 0.111}}, place_pose: {position: {x: 5.4, y: 18.0, z: 0.1}, orientation: {w: 1.0}}}"

using System.Threading.Tasks;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;
using RosMessageTypes.FactoryInterfaces;

[DisallowMultipleComponent]
[RequireComponent(typeof(ArmRig), typeof(ArmPickPlace))]
public class ArmPickPlaceServer : MonoBehaviour
{
    private ArmRig rig;
    private ArmPickPlace arm;
    private ROSConnection ros;
    private RosBridge bridge;
    private string service;
    private string lastState = "";

    void Start()
    {
        rig = GetComponent<ArmRig>();
        arm = GetComponent<ArmPickPlace>();
        service = TopicNames.ArmPickPlace(rig.ArmId);

        ros = ROSConnection.GetOrCreateInstance();
        ros.ImplementService<PickPlaceRequest, PickPlaceResponse>(service, Handle);
        Debug.Log($"[{rig.ArmName}] 서비스 서버 준비: {service}", this);

        bridge = RosBridge.Instance;
        if (bridge != null)
        {
            lastState = bridge.FactoryState;
            bridge.FactoryStateChanged += OnFactoryState;
            if (lastState == "E_STOP") arm.EmergencyStop();
        }
        else
        {
            Debug.LogWarning($"[{rig.ArmName}] 씬에 RosBridge가 없어 E_STOP을 받지 못함", this);
        }
    }

    void OnDestroy()
    {
        if (bridge != null) bridge.FactoryStateChanged -= OnFactoryState;
    }

    Task<PickPlaceResponse> Handle(PickPlaceRequest req)
    {
        var tcs = new TaskCompletionSource<PickPlaceResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        Vector3 pick = CoordConverter.ToUnityPosition(req.pick_pose.position);
        Vector3 place = CoordConverter.ToUnityPosition(req.place_pose.position);
        float yaw = YawDeg(req.place_pose.orientation);
        Debug.Log($"[ROS→Unity] {service} box_id='{req.box_id}' pick {pick} place {place} yaw {yaw:F0}", this);

        arm.Run(req.box_id, pick, place, yaw, (ok, msg) =>
        {
            Debug.Log($"[Unity→ROS] {service} success={ok} message={msg}", this);
            tcs.TrySetResult(new PickPlaceResponse(ok, msg));
        });
        return tcs.Task;
    }

    void OnFactoryState(string state)
    {
        if (state == "E_STOP") arm.EmergencyStop();
        else if (lastState == "E_STOP" && state == "IDLE") arm.ResetAfterEStop();
        lastState = state;
    }

    /// <summary>ROS 방향 → Unity yaw(도). 방향을 안 채운 요청(0,0,0,0)이면 0</summary>
    static float YawDeg(QuaternionMsg o)
    {
        if (o == null || (o.x == 0 && o.y == 0 && o.z == 0 && o.w == 0)) return 0f;
        return CoordConverter.ToUnityRotation(o).eulerAngles.y;
    }
}
