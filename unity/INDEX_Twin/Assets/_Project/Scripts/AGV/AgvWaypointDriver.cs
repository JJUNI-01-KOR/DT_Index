// AgvWaypointDriver.cs : AGV1 웨이포인트 주행 (FR-13, 부록 A.6). 담당 D · scope unity-agv
// ROS를 모른다. /agv_1_goal은 Scripts/ROS/AgvLink.cs가 받아 MoveTo()를 부른다. 경로 판단은 agv_1_controller(C)가 한다.
//
// 주행: 제자리 회전 → 직진 → (방향이 주어지면) 마지막 방향 맞춤. 물리 없이 Transform으로 움직인다.
// 바퀴(선택): 차동 구동으로 바퀴 회전만 보여 준다. vL = v + ω·W/2, vR = v − ω·W/2 (ω: Unity 시계 방향 +, rad/s)
// 정지 지점 표: goal에 좌표가 없고 이름(wp)만 오면 이 표에서 찾는다. 기본값은 agv_waypoints.yaml (10/30)과 같다.
//
// 붙이는 곳: AGV1 루트 (yaw만 도는 빈 오브젝트). 모델은 자식으로 둔다.

using System;
using UnityEngine;

[DisallowMultipleComponent]
public class AgvWaypointDriver : MonoBehaviour
{
    [Serializable]
    public class StopPoint
    {
        public string name;
        public Vector3 position;   // Unity 월드 (y는 안 씀)
        public float yawDeg;       // Unity yaw (도). 0 = +Z, 90 = +X
    }

    [Header("정지 지점 (Unity 좌표)")]
    [SerializeField] private StopPoint[] stops =
    {
        new StopPoint { name = "HOME",    position = new Vector3(-16.5f, 0f, -0.75f), yawDeg = 90f },
        new StopPoint { name = "P1_DOCK", position = new Vector3(-19f,   0f, -6.5f),  yawDeg = -90f },
        new StopPoint { name = "P2_DOCK", position = new Vector3(-19f,   0f,  5.4f),  yawDeg = -90f },
        new StopPoint { name = "FINAL",   position = new Vector3(-23f,   0f, -0.75f), yawDeg = -90f },
    };

    [Header("속도")]
    [SerializeField] private float speed = 1.0f;          // m/s
    [SerializeField] private float turnSpeedDeg = 90f;    // 도/s

    [Header("바퀴 (선택, 모델을 바꾸면 연결)")]
    [SerializeField] private Transform[] leftWheels;
    [SerializeField] private Transform[] rightWheels;
    [SerializeField] private float wheelRadius = 0.1f;    // m
    [SerializeField] private float trackWidth = 0.5f;     // 좌우 바퀴 사이 (m)
    [SerializeField] private Vector3 wheelAxis = Vector3.right;   // 바퀴 로컬 회전축

    /// <summary>목표에 도착했을 때 (wp 이름)</summary>
    public event Action<string> Arrived;

    /// <summary>마지막으로 도착한 지점 이름</summary>
    public string CurrentWp { get; private set; } = "HOME";

    /// <summary>지금 가는 지점 이름 (멈춰 있으면 빈 문자열)</summary>
    public string TargetWp => phase == Phase.Idle ? "" : goalWp;

    public bool Moving => phase != Phase.Idle;

    private enum Phase { Idle, Turning, Driving, FinalTurn }
    private Phase phase = Phase.Idle;
    private string goalWp;
    private Vector3 goalPos;
    private bool hasGoalYaw;
    private float goalYaw;

    private float Yaw
    {
        get => transform.eulerAngles.y;
        set => transform.rotation = Quaternion.Euler(0f, value, 0f);
    }

    public bool TryGetStop(string wp, out Vector3 position, out float yawDeg)
    {
        foreach (var s in stops)
        {
            if (s.name == wp)
            {
                position = s.position;
                yawDeg = s.yawDeg;
                return true;
            }
        }
        position = Vector3.zero;
        yawDeg = 0f;
        return false;
    }

    /// <summary>목표로 출발. yawDeg가 null이면 도착 방향은 맞추지 않는다. 가는 중에 새 목표가 오면 거기서 바로 바꾼다</summary>
    public void MoveTo(string wp, Vector3 target, float? yawDeg)
    {
        goalWp = wp;
        goalPos = new Vector3(target.x, transform.position.y, target.z);
        hasGoalYaw = yawDeg.HasValue;
        goalYaw = yawDeg ?? 0f;
        phase = FlatDistance(transform.position, goalPos) < 0.005f ? Phase.FinalTurn : Phase.Turning;
        Debug.Log($"[AGV1] {wp}로 출발 {goalPos}" + (hasGoalYaw ? $" yaw {goalYaw:F0}" : ""), this);
    }

    /// <summary>E_STOP: 그 자리에서 멈춘다 (도착 보고 없음)</summary>
    public void Halt()
    {
        if (phase != Phase.Idle) Debug.Log($"[AGV1] 정지 ({goalWp}로 가던 중)", this);
        phase = Phase.Idle;
    }

    /// <summary>E_STOP 해제 초기화: 대기 위치(HOME)로 바로 옮긴다</summary>
    public void ResetToHome()
    {
        phase = Phase.Idle;
        if (TryGetStop("HOME", out var p, out var y))
        {
            transform.position = new Vector3(p.x, transform.position.y, p.z);
            Yaw = y;
        }
        CurrentWp = "HOME";
    }

    void Update()
    {
        float dt = Time.deltaTime;
        float v = 0f, wDeg = 0f;
        bool arrived = false;

        switch (phase)
        {
            case Phase.Turning:
            {
                Vector3 to = goalPos - transform.position;
                float want = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                wDeg = TurnToward(want, dt, out bool done);
                if (done) phase = Phase.Driving;
                break;
            }
            case Phase.Driving:
            {
                Vector3 to = goalPos - transform.position;
                to.y = 0f;
                float dist = to.magnitude;
                float step = Mathf.Min(speed * dt, dist);
                transform.position += to.normalized * step;
                v = dt > 0f ? step / dt : 0f;
                if (dist - step <= 0.001f)
                {
                    transform.position = goalPos;
                    if (hasGoalYaw) phase = Phase.FinalTurn;
                    else arrived = true;
                }
                break;
            }
            case Phase.FinalTurn:
            {
                if (!hasGoalYaw) { arrived = true; break; }
                wDeg = TurnToward(goalYaw, dt, out bool done);
                if (done) arrived = true;
                break;
            }
        }

        SpinWheels(v, wDeg * Mathf.Deg2Rad, dt);

        if (arrived)
        {
            phase = Phase.Idle;
            CurrentWp = goalWp;
            Debug.Log($"[AGV1] {goalWp} 도착", this);
            Arrived?.Invoke(goalWp);
        }
    }

    /// <summary>yaw를 want 쪽으로 돌린다. 돌린 각속도(도/s)를 돌려준다</summary>
    float TurnToward(float want, float dt, out bool done)
    {
        float d = Mathf.DeltaAngle(Yaw, want);
        float step = Mathf.Clamp(d, -turnSpeedDeg * dt, turnSpeedDeg * dt);
        Yaw += step;
        done = Mathf.Abs(d - step) <= 0.1f;
        if (done) Yaw = want;
        return dt > 0f ? step / dt : 0f;
    }

    void SpinWheels(float v, float wRad, float dt)
    {
        if (wheelRadius <= 0f) return;
        float vL = v + wRad * trackWidth * 0.5f;
        float vR = v - wRad * trackWidth * 0.5f;
        float aL = vL / wheelRadius * Mathf.Rad2Deg * dt;
        float aR = vR / wheelRadius * Mathf.Rad2Deg * dt;
        if (leftWheels != null) foreach (var w in leftWheels) if (w != null) w.Rotate(wheelAxis, aL, Space.Self);
        if (rightWheels != null) foreach (var w in rightWheels) if (w != null) w.Rotate(wheelAxis, aR, Space.Self);
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
