// AgvLink.cs : AGV1 ↔ ROS2 (FR-13, FR-14, FR-16). 담당 D · scope unity-ros
// ROS 통신은 Scripts/ROS/ 에서만 한다 (CONVENTIONS 3절). 주행은 AGV/AgvWaypointDriver, 연결·분리는 AGV/AgvHitch가 한다.
//
//   구독: /agv_1_goal   (std_msgs/String, JSON) agv_1_controller → Unity
//           {"wp":"P1_DOCK"}                         이름만 오면 AgvWaypointDriver의 정지 지점 표에서 좌표를 찾는다
//           {"wp":"to_p1_dock_0","x":-0.75,"y":19.0,"yaw":0.0}   좌표(ROS, m·rad)가 오면 그대로 간다. yaw는 없어도 된다
//           {"hitch":"attach"} · {"hitch":"detach"}  파레트 연결·분리
//   발행: /agv_1_status (std_msgs/String, JSON) 바뀔 때
//           {"state":"MOVING","wp":"P1_DOCK"} · ARRIVED · ATTACHED · DETACHED (wp = 지금 가는 곳 또는 마지막으로 도착한 곳)
//   구독: RosBridge.FactoryStateChanged
//           E_STOP        → 그 자리에서 멈춤, 이후 goal 무시
//           E_STOP → IDLE → 끌던 파레트·최종 구역 파레트 삭제, AGV1을 HOME으로 (FR-16 5번). 상태는 보고하지 않는다
//
// 확인 (VM):
//   ros2 topic echo /agv_1_status
//   ros2 topic pub --once /agv_1_goal std_msgs/msg/String "{data: '{\"wp\": \"P2_DOCK\"}'}"
//   ros2 topic pub --once /agv_1_goal std_msgs/msg/String "{data: '{\"hitch\": \"attach\"}'}"

using System;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using RosMessageTypes.Geometry;

[DisallowMultipleComponent]
[RequireComponent(typeof(AgvWaypointDriver), typeof(AgvHitch))]
public class AgvLink : MonoBehaviour
{
    [Serializable]
    private class Goal
    {
        public string wp;
        public string hitch;
        public double x, y, yaw;
    }

    [Header("상차 후 레일에서 직진 이탈")]
    [Tooltip("상차 뒤 첫 이동 명령에서 현재 전방으로 먼저 이동할 거리(m)")]
    [Min(0f)]
    [SerializeField]
    private float pullOutDistanceM = 2.0f;

    [Header("하차 후 복귀 대기")]
    [Tooltip("분리한 팔레트의 삭제를 기다리는 최대 시간. 초과 시 E_STOP 요청")]
    [Min(0.1f)]
    [SerializeField]
    private float clearanceTimeoutSec = 10f;

    private const string PullOutWaypoint = "__AGV_PULL_OUT__";

    private AgvWaypointDriver driver;
    private AgvHitch hitch;
    private ROSConnection ros;
    private RosBridge bridge;

    private string lastState = "";
    private bool eStopped;

    private bool pullOutPending;
    private bool doingPullOut;
    private Vector3 pullOutForward;
    private float pullOutYaw;

    private bool hasQueuedGoal;
    private string queuedWp;
    private Vector3 queuedTarget;
    private float? queuedYaw;

    private GameObject releasedPallet;
    private float clearanceElapsed;

    void Start()
    {
        driver = GetComponent<AgvWaypointDriver>();
        hitch = GetComponent<AgvHitch>();

        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<StringMsg>(TopicNames.Agv1Status);
        ros.Subscribe<StringMsg>(
            TopicNames.Agv1Goal,
            m => HandleGoalJson(m.data)
        );

        driver.Arrived += OnArrived;

        bridge = RosBridge.Instance;

        if (bridge != null)
        {
            lastState = bridge.FactoryState;
            bridge.FactoryStateChanged += OnFactoryState;

            if (lastState == "E_STOP")
            {
                eStopped = true;
                driver.Halt();
            }
        }
        else
        {
            Debug.LogWarning(
                "[AgvLink] RosBridge가 없어 공장 E_STOP 상태를 구독하지 못함",
                this
            );
        }
    }

    void OnDestroy()
    {
        if (driver != null)
            driver.Arrived -= OnArrived;

        if (ros != null)
            ros.Unsubscribe(TopicNames.Agv1Goal);

        if (bridge != null)
            bridge.FactoryStateChanged -= OnFactoryState;
    }

    public void HandleGoalJson(string json)
    {
        Debug.Log(
            $"[ROS→Unity] {TopicNames.Agv1Goal} {json}",
            this
        );

        if (eStopped)
        {
            Debug.LogWarning(
                "[AgvLink] E_STOP 중이라 goal 무시",
                this
            );
            return;
        }

        Goal g;

        try
        {
            g = JsonUtility.FromJson<Goal>(json);
        }
        catch (Exception e)
        {
            Debug.LogError(
                $"[AgvLink] JSON 해석 실패: {e.Message}",
                this
            );
            return;
        }

        if (g == null)
            return;

        // ROS는 ARRIVED를 받은 뒤 다음 goal을 보내는 순차 방식이다.
        // 내부 이탈/대기 중 추가 명령으로 현재 목표를 덮어쓰지 않는다.
        if (hasQueuedGoal || doingPullOut)
        {
            Debug.LogWarning(
                "[AgvLink] 이탈/하차 대기 처리 중: 중복 또는 추가 goal 무시",
                this
            );
            return;
        }

        if (!string.IsNullOrEmpty(g.hitch))
        {
            HandleHitch(g.hitch);
            return;
        }

        if (string.IsNullOrEmpty(g.wp))
        {
            Debug.LogError(
                "[AgvLink] wp도 hitch도 없는 goal",
                this
            );
            return;
        }

        Vector3 target;
        float? yaw = null;

        if (json.Contains("\"x\"") && json.Contains("\"y\""))
        {
            target = CoordConverter.ToUnityPosition(
                new PointMsg(g.x, g.y, 0)
            );

            if (json.Contains("\"yaw\""))
                yaw = RosYawToUnity(g.yaw);
        }
        else if (driver.TryGetStop(
            g.wp,
            out target,
            out float stopYaw))
        {
            yaw = stopYaw;
        }
        else
        {
            Debug.LogError(
                $"[AgvLink] 좌표 없이 모르는 wp: {g.wp}",
                this
            );
            return;
        }

        // ROS에서 받은 원래 목적지를 보관한다.
        queuedWp = g.wp;
        queuedTarget = target;
        queuedYaw = yaw;
        hasQueuedGoal = true;
        clearanceElapsed = 0f;

        // 분리한 팔레트가 남아 있으면 삭제 완료까지 기다린다.
        if (releasedPallet != null)
        {
            driver.Halt();

            Debug.Log(
                $"[AGV1] {g.wp} 이동 대기: 하차한 팔레트 삭제 확인 중",
                this
            );
            return;
        }

        BeginQueuedTravel();
    }

    private void HandleHitch(string command)
    {
        string wp = driver.CurrentWp;
        string error;

        if (command == "attach")
        {
            if (releasedPallet != null)
            {
                Debug.LogWarning(
                    "[AgvLink] 이전 팔레트 삭제 대기 중이라 연결 요청 무시",
                    this
                );
                return;
            }

            bool alreadyAttached = hitch.Towed != null;

            if (!hitch.Attach(out error))
            {
                Debug.LogError(
                    $"[AgvLink] 연결 실패 ({wp}): {error}",
                    this
                );
                return;
            }

            if (!alreadyAttached)
            {
                // 상차 당시 AGV의 전방 방향을 저장한다.
                pullOutForward = transform.forward;
                pullOutForward.y = 0f;
                pullOutForward.Normalize();

                pullOutYaw = transform.eulerAngles.y;
                pullOutPending = pullOutDistanceM > 0f;
            }

            // 실제 연결은 즉시 보고한다.
            // 다음 이동 명령을 받으면 먼저 직진한다.
            Publish("ATTACHED", wp);
        }
        else if (command == "detach")
        {
            var pallet = hitch.Towed;

            if (pallet == null)
            {
                Debug.LogError(
                    "[AgvLink] 분리할 팔레트가 없음",
                    this
                );
                return;
            }

            if (FinalZone.Containing(pallet.transform.position) == null)
            {
                FailAndStop(
                    "최종 구역 밖에서 하차 요청: FinalZone 위치/Accept Radius 확인"
                );
                return;
            }

            if (!hitch.Detach(out error))
            {
                Debug.LogError(
                    $"[AgvLink] 분리 실패 ({wp}): {error}",
                    this
                );
                return;
            }

            // 분리했던 오브젝트를 기억해 실제 삭제 여부를 확인한다.
            releasedPallet = pallet;
            pullOutPending = false;

            // 실제 분리는 즉시 보고한다.
            // 다음 HOME/DOCK 이동은 삭제 완료까지 보류한다.
            Publish("DETACHED", wp);
        }
        else
        {
            Debug.LogError(
                $"[AgvLink] 모르는 hitch 값: {command}",
                this
            );
        }
    }

    void Update()
    {
        if (eStopped || !hasQueuedGoal || doingPullOut)
            return;

        if (releasedPallet == null)
        {
            // Unity의 Destroy가 완료된 뒤 출발한다.
            BeginQueuedTravel();
            return;
        }

        clearanceElapsed += Time.deltaTime;

        if (clearanceElapsed >= Mathf.Max(0.1f, clearanceTimeoutSec))
        {
            FailAndStop(
                "하차 팔레트 삭제 시간 초과: FinalZone 동작 확인"
            );
        }
    }

    private void BeginQueuedTravel()
    {
        if (!hasQueuedGoal || eStopped)
            return;

        if (pullOutPending && hitch.Towed != null)
        {
            pullOutPending = false;
            doingPullOut = true;

            Vector3 exit =
                transform.position
                + pullOutForward * Mathf.Max(0f, pullOutDistanceM);

            Publish("MOVING", queuedWp);

            // 방향을 꺾지 않고 상차 당시 전방으로 먼저 이동한다.
            driver.MoveTo(
                PullOutWaypoint,
                exit,
                pullOutYaw
            );

            Debug.Log(
                $"[AGV1] 상차 후 전방 {pullOutDistanceM:F2}m 직진 이탈",
                this
            );
            return;
        }

        pullOutPending = false;
        StartDestination();
    }

    private void StartDestination()
    {
        string wp = queuedWp;
        Vector3 target = queuedTarget;
        float? yaw = queuedYaw;

        hasQueuedGoal = false;
        doingPullOut = false;

        driver.MoveTo(wp, target, yaw);
        Publish("MOVING", wp);
    }

    private void OnArrived(string wp)
    {
        if (eStopped)
            return;

        if (doingPullOut && wp == PullOutWaypoint)
        {
            Debug.Log(
                "[AGV1] 직진 이탈 완료 → 원래 목표 경로로 이동",
                this
            );

            StartDestination();

            // 내부 경유점 도착은 ROS에 보고하지 않는다.
            // 보고하면 ROS가 원래 목적지에 도착했다고 판단할 수 있다.
            return;
        }

        Publish("ARRIVED", wp);
    }

    private void Publish(string state, string wp)
    {
        string json =
            $"{{\"state\":\"{state}\",\"wp\":\"{wp}\"}}";

        if (ros != null)
        {
            ros.Publish(
                TopicNames.Agv1Status,
                new StringMsg(json)
            );
        }

        Debug.Log(
            $"[Unity→ROS] {TopicNames.Agv1Status} {json}",
            this
        );
    }

    private void CancelPendingTravel()
    {
        hasQueuedGoal = false;
        doingPullOut = false;
        pullOutPending = false;

        clearanceElapsed = 0f;
        releasedPallet = null;

        driver.Halt();
    }

    private void FailAndStop(string reason)
    {
        Debug.LogError(
            $"[AgvLink] {reason}",
            this
        );

        eStopped = true;
        CancelPendingTravel();

        if (bridge != null)
        {
            bridge.EmergencyStopNow();
        }
        else if (ros != null)
        {
            ros.Publish(
                TopicNames.EmergencyStop,
                new BoolMsg(true)
            );
        }
    }

    private void OnFactoryState(string state)
    {
        if (state == "E_STOP")
        {
            eStopped = true;
            CancelPendingTravel();
        }
        else if (lastState == "E_STOP" && state == "IDLE")
        {
            CancelPendingTravel();

            hitch.ResetAll();
            FinalZone.ResetAllZones();
            driver.ResetToHome();

            eStopped = false;

            Debug.Log(
                "[AgvLink] 해제 초기화: AGV1 HOME",
                this
            );
        }

        lastState = state;
    }

    private static float RosYawToUnity(double yaw)
    {
        var q = new QuaternionMsg(
            0,
            0,
            Math.Sin(yaw / 2),
            Math.Cos(yaw / 2)
        );

        return CoordConverter.ToUnityRotation(q).eulerAngles.y;
    }
}
