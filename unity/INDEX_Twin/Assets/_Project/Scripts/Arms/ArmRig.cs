using System.Linq;
using RosMessageTypes.BuiltinInterfaces;
using RosMessageTypes.Geometry;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using RosMessageTypes.Tf2;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;

[DisallowMultipleComponent]
public class ArmRig : MonoBehaviour
{
    [SerializeField, Range(1, 4)]
    private int armId = 1;

    [Tooltip("비워두면 Awake에서 자동으로 찾는다")]
    [SerializeField]
    private ArticulationBody[] joints = new ArticulationBody[6];

    [Tooltip("흡착 기준점")]
    [SerializeField]
    private Transform toolPoint;

    [Tooltip("홈 자세: 라디안 6개")]
    [SerializeField]
    private float[] homeRad = new float[6];

    [Tooltip("Play 시작 시 홈 자세로 이동")]
    [SerializeField]
    private bool goHomeOnStart = true;

    [Header("관절 드라이브")]
    [SerializeField] private float stiffness = 10000f;
    [SerializeField] private float damping = 100f;
    [SerializeField] private float forceLimit = 1000f;

    [Tooltip("관절 드라이브 damping의 최소값")]
    [SerializeField]
    private float minDamping = 1500f;

    [Tooltip("시작할 때 홈 자세까지 이동하는 시간. 0이면 순간이동")]
    [SerializeField]
    private float startMoveSec = 2.5f;

    [Tooltip("팔 링크의 중력을 비활성화")]
    [SerializeField]
    private bool gravityCompensation = true;

    [Header("부드러운 이동")]
    [SerializeField] private float jointSpeedDegPerSec = 60f;
    [SerializeField] private float minMoveTimeSec = 0.4f;
    [SerializeField] private float arriveToleranceDeg = 0.5f;

    [Header("RViz 동기화")]
    [Tooltip("이 팔 내부의 실제 base_link 오브젝트를 연결")]
    [SerializeField]
    private Transform rosBaseLink;

    // 이동 상태
    private float[] startDeg;
    private float[] goalDeg;
    private float moveDuration;
    private float moveElapsed;

    // ROS 발행
    private ROSConnection ros;
    private string jointStateTopic;
    private float publishTimer;

    private const float PUBLISH_INTERVAL = 0.1f;

    private readonly string[] jointNames =
    {
        "j1", "j2", "j3", "j4", "j5", "j6"
    };

    public int ArmId => armId;
    public Transform ToolPoint => toolPoint;
    public Transform GripPoint => toolPoint;
    public float[] HomeRad => homeRad;
    public ArticulationBody[] Joints => joints;
    public bool IsMoving { get; private set; }
    public string ArmName => $"arm_{armId}";

    void Awake()
    {
        if (joints == null ||
            joints.Length != 6 ||
            joints.Any(j => j == null))
        {
            AutoFindJoints();
        }

        if (!HasValidJoints())
        {
            Debug.LogError(
                $"[{ArmName}] 회전 관절 6개를 연결해야 합니다. " +
                "ArmRig 우클릭 → '관절 자동 찾기'를 실행하세요.",
                this);

            enabled = false;
            return;
        }

        ApplyDriveSettings();
        EnsureBuffers();

        // 로봇 베이스 고정
        var root = GetComponentInChildren<ArticulationBody>();

        if (root != null && root.isRoot)
        {
            root.immovable = true;
        }

        if (gravityCompensation)
        {
            foreach (var ab in GetComponentsInChildren<ArticulationBody>())
            {
                ab.useGravity = false;
            }
        }
    }

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        jointStateTopic = TopicNames.ArmJointStates(armId);

        ros.RegisterPublisher<JointStateMsg>(jointStateTopic);
        ros.RegisterPublisher<TFMessageMsg>("/tf");

        if (rosBaseLink == null)
        {
            Debug.LogError(
                $"[{ArmName}] ArmRig의 Ros Base Link에 " +
                "이 팔의 base_link를 연결하세요.",
                this);
        }

        if (goHomeOnStart &&
            homeRad != null &&
            homeRad.Length == joints.Length)
        {
            if (startMoveSec <= 0f)
            {
                TeleportTo(homeRad);
            }
            else
            {
                SetJointTargets(homeRad);
                moveDuration = Mathf.Max(moveDuration, startMoveSec);
            }
        }
    }

    bool HasValidJoints()
    {
        return joints != null &&
               joints.Length == 6 &&
               joints.All(j => j != null);
    }

    [ContextMenu("관절 자동 찾기")]
    public void AutoFindJoints()
    {
        joints = GetComponentsInChildren<ArticulationBody>()
            .Where(a => a.jointType == ArticulationJointType.RevoluteJoint)
            .Take(6)
            .ToArray();

        if (joints.Length != 6)
        {
            Debug.LogWarning(
                $"[{ArmName}] 회전 관절을 {joints.Length}개 찾았습니다. " +
                "6개가 필요합니다.",
                this);
        }
        else
        {
            Debug.Log(
                $"[{ArmName}] 관절 6개: " +
                string.Join(", ", joints.Select(j => j.name)),
                this);
        }
    }

    void ApplyDriveSettings()
    {
        foreach (var joint in joints)
        {
            var drive = joint.xDrive;

            drive.stiffness = stiffness;
            drive.damping = Mathf.Max(damping, minDamping);
            drive.forceLimit = forceLimit;

            joint.xDrive = drive;
        }
    }

    public void TeleportTo(float[] radians)
    {
        if (!CheckLength(radians))
            return;

        EnsureBuffers();

        for (int i = 0; i < joints.Length; i++)
        {
            float deg = radians[i] * Mathf.Rad2Deg;

            goalDeg[i] = deg;
            startDeg[i] = deg;

            WriteDriveTarget(i, deg);

            joints[i].jointPosition =
                new ArticulationReducedSpace(radians[i]);

            joints[i].jointVelocity =
                new ArticulationReducedSpace(0f);
        }

        moveElapsed = 0f;
        moveDuration = 0f;
        IsMoving = false;
    }

    public void SetHomeRad(float[] radians)
    {
        if (!CheckLength(radians))
            return;

        homeRad = (float[])radians.Clone();
    }

    public void GoHome()
    {
        SetJointTargets(homeRad);
    }

    public void SetJointTargets(float[] radians)
    {
        if (!CheckLength(radians))
            return;

        EnsureBuffers();

        for (int i = 0; i < joints.Length; i++)
        {
            startDeg[i] = joints[i].xDrive.target;
            goalDeg[i] = radians[i] * Mathf.Rad2Deg;
        }

        float maxDelta = 0f;

        for (int i = 0; i < joints.Length; i++)
        {
            maxDelta = Mathf.Max(
                maxDelta,
                Mathf.Abs(goalDeg[i] - startDeg[i]));
        }

        moveDuration = Mathf.Max(
            minMoveTimeSec,
            maxDelta / Mathf.Max(1f, jointSpeedDegPerSec));

        moveElapsed = 0f;
        IsMoving = true;
    }

    public void SetJointTargetsImmediate(float[] radians)
    {
        if (!CheckLength(radians))
            return;

        EnsureBuffers();

        for (int i = 0; i < joints.Length; i++)
        {
            goalDeg[i] = radians[i] * Mathf.Rad2Deg;
            startDeg[i] = goalDeg[i];

            WriteDriveTarget(i, goalDeg[i]);
        }

        moveElapsed = 0f;
        moveDuration = 0f;
        IsMoving = true;
    }

    void FixedUpdate()
    {
        // 움직이지 않을 때에도 관절 상태와 베이스 TF를 발행
        if (ros != null)
        {
            publishTimer += Time.fixedDeltaTime;

            if (publishTimer >= PUBLISH_INTERVAL)
            {
                PublishJointStates();
                publishTimer = 0f;
            }
        }

        if (!IsMoving)
            return;

        moveElapsed += Time.fixedDeltaTime;

        float t = moveDuration > 0f
            ? Mathf.Clamp01(moveElapsed / moveDuration)
            : 1f;

        float s = SmootherStep(t);

        float dsdt = moveDuration > 0f && t < 1f
            ? SmootherStepDeriv(t) / moveDuration
            : 0f;

        bool allReached = t >= 1f;

        for (int i = 0; i < joints.Length; i++)
        {
            float target = Mathf.Lerp(
                startDeg[i],
                goalDeg[i],
                s);

            float velocity =
                (goalDeg[i] - startDeg[i]) * dsdt;

            WriteDriveTarget(i, target, velocity);

            if (joints[i].jointPosition.dofCount == 0)
            {
                allReached = false;
                continue;
            }

            float actualDeg =
                joints[i].jointPosition[0] * Mathf.Rad2Deg;

            if (Mathf.Abs(actualDeg - goalDeg[i]) >
                arriveToleranceDeg)
            {
                allReached = false;
            }
        }

        if (allReached)
        {
            IsMoving = false;
        }
    }

    private void PublishJointStates()
    {
        if (ros == null || rosBaseLink == null)
            return;

        // 11/05 BUG-007: 끊겨 있는 동안 보내면 큐에 쌓였다가 다시 붙을 때 발행 등록보다 먼저 도착해
        //   endpoint가 "Not registered to publish topic"을 낸다. 연결돼 있을 때만 보낸다 (주기 발행이라 놓쳐도 다음 주기에 나감)
        if (!ros.HasConnectionThread || ros.HasConnectionError)
            return;

        if (!HasValidJoints() ||
            joints.Any(j => j.jointPosition.dofCount == 0))
        {
            return;
        }

        // 기존 AGV와 동일하게 UTC 시각 사용
        System.DateTimeOffset now =
            System.DateTimeOffset.UtcNow;

        var stamp = new TimeMsg
        {
            sec = (int)now.ToUnixTimeSeconds(),
            nanosec = (uint)(now.Millisecond * 1000000)
        };

        var jointMsg = new JointStateMsg
        {
            header = new HeaderMsg
            {
                stamp = stamp,
                frame_id = ""
            },
            name = jointNames,
            position = GetJointPositions()
                .Select(v => (double)v)
                .ToArray(),
            velocity = new double[0],
            effort = new double[0]
        };

        // base_link 월드 좌표를 기존 프로젝트 변환 함수로 변환
        var rosPosition =
            CoordConverter.ToRosPoint(rosBaseLink.position);

        var rosRotation =
            CoordConverter.ToRosRotation(rosBaseLink.rotation);

        var baseTf = new TransformStampedMsg
        {
            header = new HeaderMsg
            {
                stamp = stamp,
                frame_id = "map"
            },
            child_frame_id = $"arm_{armId}/base_link",
            transform = new TransformMsg
            {
                translation = new Vector3Msg(
                    rosPosition.x,
                    rosPosition.y,
                    rosPosition.z),
                rotation = rosRotation
            }
        };

        ros.Publish(
            "/tf",
            new TFMessageMsg(
                new TransformStampedMsg[] { baseTf }));

        ros.Publish(jointStateTopic, jointMsg);
    }

    static float SmootherStep(float t)
    {
        return t * t * t *
               (t * (t * 6f - 15f) + 10f);
    }

    static float SmootherStepDeriv(float t)
    {
        return 30f * t * t *
               (t * (t - 2f) + 1f);
    }

    void EnsureBuffers()
    {
        int count = joints.Length;

        if (goalDeg != null &&
            goalDeg.Length == count &&
            startDeg != null &&
            startDeg.Length == count)
        {
            return;
        }

        goalDeg = joints
            .Select(j => j.xDrive.target)
            .ToArray();

        startDeg = (float[])goalDeg.Clone();
        IsMoving = false;
    }

    void WriteDriveTarget(
        int index,
        float deg,
        float velocityDegPerSec = 0f)
    {
        var drive = joints[index].xDrive;

        drive.target = deg;
        drive.targetVelocity = velocityDegPerSec;

        joints[index].xDrive = drive;
    }

    bool CheckLength(float[] radians)
    {
        if (!HasValidJoints())
        {
            Debug.LogError(
                $"[{ArmName}] 관절 참조가 올바르지 않습니다. " +
                "ArmRig 우클릭 → '관절 자동 찾기'를 실행하세요.",
                this);

            return false;
        }

        if (radians != null &&
            radians.Length == joints.Length)
        {
            return true;
        }

        Debug.LogError(
            $"[{ArmName}] 관절값 개수가 맞지 않습니다: " +
            $"{radians?.Length} (필요: {joints.Length})",
            this);

        return false;
    }

    public float[] GetJointPositions()
    {
        return joints.Select(j =>
            j.jointPosition.dofCount > 0
                ? j.jointPosition[0]
                : 0f
        ).ToArray();
    }

    // 기존 호출 코드와의 호환용 double 버전
    public void SetJointTargets(double[] radians)
    {
        SetJointTargets(ToFloat(radians));
    }

    public void SetJointTargetsImmediate(double[] radians)
    {
        SetJointTargetsImmediate(ToFloat(radians));
    }

    public double[] GetJointRad()
    {
        return GetJointPositions()
            .Select(v => (double)v)
            .ToArray();
    }

    static float[] ToFloat(double[] values)
    {
        return values?
            .Select(v => (float)v)
            .ToArray();
    }

    [ContextMenu("테스트: 홈 자세")]
    void TestHome()
    {
        TestMove(homeRad);
    }

    [ContextMenu("테스트: 관절 전부 0")]
    void TestZero()
    {
        TestMove(new float[6]);
    }

    [ContextMenu("현재 관절값을 Console에 출력 (홈 자세 정할 때)")]
    void PrintJoints()
    {
        if (!HasValidJoints())
        {
            Debug.LogWarning(
                $"[{ArmName}] 관절 참조를 먼저 연결하세요.",
                this);
            return;
        }

        Debug.Log(
            $"[{ArmName}] 관절(rad): " +
            string.Join(
                ", ",
                GetJointPositions()
                    .Select(v => v.ToString("F3"))),
            this);
    }

    [ContextMenu("테스트: 자세 A")]
    void TestPoseA()
    {
        TestMove(
            new[] { 0.5f, -0.5f, 0.8f, 0f, 0.5f, 0f });
    }

    [ContextMenu("테스트: 자세 B")]
    void TestPoseB()
    {
        TestMove(
            new[] { -0.8f, -0.3f, 1.2f, -0.6f, -0.4f, 1.0f });
    }

    void TestMove(float[] radians)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "테스트 자세는 Play 중에만 동작합니다.",
                this);
            return;
        }

        SetJointTargets(radians);
    }
}
