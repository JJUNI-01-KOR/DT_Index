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

    private AgvWaypointDriver driver;
    private AgvHitch hitch;
    private ROSConnection ros;
    private RosBridge bridge;
    private string lastState = "";
    private bool eStopped;

    void Start()
    {
        driver = GetComponent<AgvWaypointDriver>();
        hitch = GetComponent<AgvHitch>();

        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<StringMsg>(TopicNames.Agv1Status);
        ros.Subscribe<StringMsg>(TopicNames.Agv1Goal, m => HandleGoalJson(m.data));
        driver.Arrived += wp => Publish("ARRIVED", wp);

        bridge = RosBridge.Instance;
        if (bridge != null)
        {
            lastState = bridge.FactoryState;
            bridge.FactoryStateChanged += OnFactoryState;
            if (lastState == "E_STOP") { eStopped = true; driver.Halt(); }
        }
        else
        {
            Debug.LogWarning("[AgvLink] 씬에 RosBridge가 없어 E_STOP을 받지 못함", this);
        }
    }

    void OnDestroy()
    {
        if (ros != null) ros.Unsubscribe(TopicNames.Agv1Goal);
        if (bridge != null) bridge.FactoryStateChanged -= OnFactoryState;
    }

    /// <summary>/agv_1_goal 1개 처리 (시험할 때 직접 불러도 된다)</summary>
    public void HandleGoalJson(string json)
    {
        Debug.Log($"[ROS→Unity] {TopicNames.Agv1Goal} {json}", this);
        if (eStopped) { Debug.LogWarning("[AgvLink] E_STOP 중이라 goal 무시", this); return; }

        Goal g;
        try { g = JsonUtility.FromJson<Goal>(json); }
        catch (Exception e) { Debug.LogError($"[AgvLink] JSON 해석 실패: {json} ({e.Message})", this); return; }
        if (g == null) return;

        if (!string.IsNullOrEmpty(g.hitch))
        {
            string wp = driver.CurrentWp;
            string err;
            if (g.hitch == "attach")
            {
                if (hitch.Attach(out err)) Publish("ATTACHED", wp);
                else Debug.LogError($"[AgvLink] 연결 실패 ({wp}): {err}", this);
            }
            else if (g.hitch == "detach")
            {
                if (hitch.Detach(out err)) Publish("DETACHED", wp);
                else Debug.LogError($"[AgvLink] 분리 실패 ({wp}): {err}", this);
            }
            else Debug.LogError($"[AgvLink] 모르는 hitch 값: {g.hitch}", this);
            return;
        }

        if (string.IsNullOrEmpty(g.wp)) { Debug.LogError($"[AgvLink] wp도 hitch도 없음: {json}", this); return; }

        Vector3 target;
        float? yawDeg = null;
        if (json.Contains("\"x\"") && json.Contains("\"y\""))
        {
            target = CoordConverter.ToUnityPosition(new PointMsg(g.x, g.y, 0));
            if (json.Contains("\"yaw\"")) yawDeg = RosYawToUnity(g.yaw);
        }
        else if (driver.TryGetStop(g.wp, out target, out float stopYaw))
        {
            yawDeg = stopYaw;
        }
        else
        {
            Debug.LogError($"[AgvLink] 좌표 없이 모르는 wp: {g.wp} (정지 지점 표에 없음)", this);
            return;
        }

        driver.MoveTo(g.wp, target, yawDeg);
        Publish("MOVING", g.wp);
    }

    void Publish(string state, string wp)
    {
        string json = $"{{\"state\":\"{state}\",\"wp\":\"{wp}\"}}";
        if (ros != null) ros.Publish(TopicNames.Agv1Status, new StringMsg(json));
        Debug.Log($"[Unity→ROS] {TopicNames.Agv1Status} {json}", this);
    }

    void OnFactoryState(string state)
    {
        if (state == "E_STOP")
        {
            eStopped = true;
            driver.Halt();
        }
        else if (lastState == "E_STOP" && state == "IDLE")
        {
            eStopped = false;
            hitch.ResetAll();
            FinalZone.ResetAllZones();
            driver.ResetToHome();
            Debug.Log("[AgvLink] 해제 초기화: AGV1 HOME", this);
        }
        lastState = state;
    }

    /// <summary>ROS yaw(rad, 반시계 +) → Unity yaw(도). 변환은 CoordConverter로</summary>
    static float RosYawToUnity(double yaw)
    {
        var q = new QuaternionMsg(0, 0, Math.Sin(yaw / 2), Math.Cos(yaw / 2));
        return CoordConverter.ToUnityRotation(q).eulerAngles.y;
    }
}
