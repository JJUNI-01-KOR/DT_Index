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

    private float wheelAngle = 0f;

    public event Action<string> Arrived;
    public string CurrentWp { get; private set; } = "HOME";

    private string targetWpName = "";
    private Vector3 targetPosition;
    private float targetYaw;
    private bool hasTargetYaw;
    private bool isMoving = false;

    private Vector3 homePosition = new Vector3(-16.5f, 0f, -0.75f);
    private float homeYaw = 90f;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<TFMessageMsg>("/tf");
        ros.RegisterPublisher<JointStateMsg>("/agv_1_joint_states");
    }

    public bool TryGetStop(string wp, out Vector3 position, out float yawDeg)
    {
        position = Vector3.zero;
        yawDeg = 0f;
        return false;
    }

    public void MoveTo(string wp, Vector3 target, float? yawDeg)
    {
        targetWpName = wp;
        // AgvLink가 이미 완벽하게 축 변환(ROS->Unity)을 해서 넘겨준 target을 그대로 사용합니다.
        targetPosition = new Vector3(target.x, transform.position.y, target.z);
        hasTargetYaw = yawDeg.HasValue;
        targetYaw = yawDeg ?? 0f;
        isMoving = true;
    }

    public void Halt()
    {
        isMoving = false;
    }

    public void ResetToHome()
    {
        isMoving = false;
        transform.position = homePosition;
        transform.rotation = Quaternion.Euler(0, homeYaw, 0);
        CurrentWp = "HOME";
    }

    void Update()
    {
        if (isMoving)
        {
            // 1. 방향 전환
            Vector3 dir = targetPosition - transform.position;
            dir.y = 0;
            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion lookRot = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, lookRot, turnSpeedDeg * Time.deltaTime);
            }

            // 2. 전진 및 바퀴 회전
            float moveStep = speed * Time.deltaTime;
            Vector3 prevPos = transform.position;
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveStep);

            float movedDist = Vector3.Distance(prevPos, transform.position);
            float angleStep = (movedDist / wheelRadius) * Mathf.Rad2Deg;
            wheelAngle += movedDist / wheelRadius;

            if (leftWheels != null) foreach (var w in leftWheels) if (w != null) w.Rotate(wheelAxis, angleStep, Space.Self);
            if (rightWheels != null) foreach (var w in rightWheels) if (w != null) w.Rotate(wheelAxis, angleStep, Space.Self);

            // 3. 도착 판정
            Vector3 currentFlat = new Vector3(transform.position.x, 0, transform.position.z);
            Vector3 targetFlat = new Vector3(targetPosition.x, 0, targetPosition.z);

            if (Vector3.Distance(currentFlat, targetFlat) < 0.02f)
            {
                if (hasTargetYaw)
                {
                    Quaternion finalRot = Quaternion.Euler(0, targetYaw, 0);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, finalRot, turnSpeedDeg * Time.deltaTime);
                    if (Quaternion.Angle(transform.rotation, finalRot) < 0.5f) FinishMove();
                }
                else FinishMove();
            }
        }

        PublishTF();
        PublishJointStates();
    }

    private void FinishMove()
    {
        isMoving = false;
        CurrentWp = targetWpName;
        Arrived?.Invoke(targetWpName);
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