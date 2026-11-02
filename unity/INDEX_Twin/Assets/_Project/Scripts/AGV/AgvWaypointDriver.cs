// AgvWaypointDriver.cs : AGV1 웨이포인트 주행 + RViz TF (FR-13). scope unity-agv
// /agv_1_goal은 Scripts/ROS/AgvLink.cs가 받아 MoveTo()를 부른다. 이름만 오는 goal(P1_DOCK 등)은 정지 지점 표에서 찾는다.
// 주행: 목표 쪽으로 제자리 회전 → 직진 → (방향이 주어지면) 마지막 방향 맞춤. 물리 없이 Transform으로 움직인다.
// RViz: /tf (map → agv_1_link), /agv_1_joint_states 를 publishPeriodSec(기본 0.1초 = 10 Hz)마다 보낸다.

using RosMessageTypes.BuiltinInterfaces;
using RosMessageTypes.Geometry;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using RosMessageTypes.Tf2;
using System;
using Unity.Robotics.ROSTCPConnector;
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

    [Header("정지 지점 (Unity 좌표) - 이름만 오는 goal은 여기서 찾는다")]
    [SerializeField]
    private StopPoint[] stops =
    {
        new StopPoint { name = "HOME",    position = new Vector3(-16.5f, 0f, -0.75f), yawDeg = 90f },
        new StopPoint { name = "P1_DOCK", position = new Vector3(-19f,   0f, -6.5f),  yawDeg = -90f },
        new StopPoint { name = "P2_DOCK", position = new Vector3(-19f,   0f,  5.4f),  yawDeg = -90f },
        new StopPoint { name = "FINAL",   position = new Vector3(-23f,   0f, -0.75f), yawDeg = -90f },
    };

    private ROSConnection ros;

    [Header("주행 속도 설정")]
    public float speed = 1.0f;
    public float turnSpeedDeg = 180f;

    [Header("RViz TF 및 오도메트리 설정")]
    public string agvBaseFrame = "agv_1_link";
    public string[] wheelJointNames = new string[] {
        "bone_wheel_l_back", "bone_wheel_l_front",
        "bone_wheel_r_back", "bone_wheel_r_front"
    };
    public float wheelRadius = 0.1365f;
    public Transform[] leftWheels;
    public Transform[] rightWheels;
    public Vector3 wheelAxis = Vector3.right;
    public float publishPeriodSec = 0.1f;   // 10 Hz

    private float wheelAngle = 0f;
    private float publishTimer = 0f;

    public event Action<string> Arrived;
    public string CurrentWp { get; private set; } = "HOME";

    private string targetWpName = "";
    private Vector3 targetPosition;
    private float targetYaw;
    private bool hasTargetYaw;
    private bool isMoving = false;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<TFMessageMsg>("/tf");
        ros.RegisterPublisher<JointStateMsg>("/agv_1_joint_states");
    }

    public bool TryGetStop(string wp, out Vector3 position, out float yawDeg)
    {
        foreach (var s in stops)
        {
            if (s.name != wp) continue;
            position = s.position;
            yawDeg = s.yawDeg;
            return true;
        }
        position = Vector3.zero;
        yawDeg = 0f;
        return false;
    }

    public void MoveTo(string wp, Vector3 target, float? yawDeg)
    {
        targetWpName = wp;
        // AgvLink가 이미 축 변환(ROS->Unity)을 해서 넘겨준 target을 그대로 사용합니다.
        targetPosition = new Vector3(target.x, transform.position.y, target.z);
        hasTargetYaw = yawDeg.HasValue;
        targetYaw = yawDeg ?? 0f;
        isMoving = true;
        Debug.Log("[AGV1] " + wp + "로 출발 " + targetPosition, this);
    }

    public void Halt()
    {
        isMoving = false;
    }

    public void ResetToHome()
    {
        isMoving = false;
        Vector3 p;
        float yaw;
        if (TryGetStop("HOME", out p, out yaw))
        {
            transform.position = new Vector3(p.x, transform.position.y, p.z);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
        CurrentWp = "HOME";
    }

    void Update()
    {
        if (isMoving) Drive();

        publishTimer += Time.deltaTime;
        if (publishTimer >= publishPeriodSec)
        {
            publishTimer = 0f;
            PublishTF();
            PublishJointStates();
        }
    }

    void Drive()
    {
        Vector3 dir = targetPosition - transform.position;
        dir.y = 0;

        if (dir.magnitude > 0.02f)
        {
            // 1. 방향 전환: 목표를 볼 때까지 제자리 회전 (옆으로 미끄러지지 않게)
            Quaternion lookRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, lookRot, turnSpeedDeg * Time.deltaTime);
            if (Quaternion.Angle(transform.rotation, lookRot) > 1f) return;

            // 2. 전진 및 바퀴 회전
            Vector3 prevPos = transform.position;
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, speed * Time.deltaTime);

            float movedDist = Vector3.Distance(prevPos, transform.position);
            float angleStep = (movedDist / wheelRadius) * Mathf.Rad2Deg;
            wheelAngle += movedDist / wheelRadius;

            if (leftWheels != null) foreach (var w in leftWheels) if (w != null) w.Rotate(wheelAxis, angleStep, Space.Self);
            if (rightWheels != null) foreach (var w in rightWheels) if (w != null) w.Rotate(wheelAxis, angleStep, Space.Self);
            return;
        }

        // 3. 도착: 위치를 맞추고, 방향이 주어졌으면 돌린 뒤 도착 처리
        transform.position = targetPosition;
        if (hasTargetYaw)
        {
            Quaternion finalRot = Quaternion.Euler(0, targetYaw, 0);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, finalRot, turnSpeedDeg * Time.deltaTime);
            if (Quaternion.Angle(transform.rotation, finalRot) > 0.5f) return;
            transform.rotation = finalRot;
        }
        FinishMove();
    }

    private void FinishMove()
    {
        isMoving = false;
        CurrentWp = targetWpName;
        Debug.Log("[AGV1] " + targetWpName + " 도착", this);
        if (Arrived != null) Arrived(targetWpName);
    }

    void PublishTF()
    {
        System.DateTimeOffset now = System.DateTimeOffset.UtcNow;
        var timeMsg = new TimeMsg { sec = (int)now.ToUnixTimeSeconds(), nanosec = (uint)(now.Millisecond * 1000000) };
        var tfMsg = new TransformStampedMsg
        {
            header = new HeaderMsg { stamp = timeMsg, frame_id = "map" },
            child_frame_id = agvBaseFrame,
            transform = new TransformMsg
            {
                translation = new Vector3Msg(transform.position.z, -transform.position.x, transform.position.y),
                rotation = new QuaternionMsg(transform.rotation.z, -transform.rotation.x, transform.rotation.y, -transform.rotation.w)
            }
        };
        ros.Publish("/tf", new TFMessageMsg(new TransformStampedMsg[] { tfMsg }));
    }

    void PublishJointStates()
    {
        System.DateTimeOffset now = System.DateTimeOffset.UtcNow;
        var timeMsg = new TimeMsg { sec = (int)now.ToUnixTimeSeconds(), nanosec = (uint)(now.Millisecond * 1000000) };
        double[] positions = new double[wheelJointNames.Length];
        for (int i = 0; i < positions.Length; i++) positions[i] = wheelAngle;
        var jointState = new JointStateMsg { header = new HeaderMsg { stamp = timeMsg }, name = wheelJointNames, position = positions, velocity = new double[0], effort = new double[0] };
        ros.Publish("/agv_1_joint_states", jointState);
    }
}