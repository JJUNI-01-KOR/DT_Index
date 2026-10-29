using System.Linq;
using UnityEngine;

/// <summary>
/// 로봇팔 1대의 공통 컴포넌트 (Arm_Fairino 프리팹 루트에 붙임).
/// - armId (1~4)
/// - ArticulationBody 회전 관절 6개 참조 (URDF 관절 순서 그대로)
/// - 관절 목표값 적용 (라디안 입력) + 부드러운 이동
///   · 6개 관절이 "동시에 출발해서 동시에 도착" (동기화)
///   · 천천히 출발 → 가속 → 감속 → 천천히 정지 (S자 속도 곡선)
/// 10/30 제어 방식 결정(ROS2 관절값 / Unity IK)과 상관없이 둘 다 이 컴포넌트를 쓴다.
/// </summary>
[DisallowMultipleComponent]
public class ArmRig : MonoBehaviour
{
    [SerializeField, Range(1, 4)] private int armId = 1;

    [Tooltip("비워두면 Awake에서 자동으로 찾는다 (계층 순서 = URDF 관절 순서)")]
    [SerializeField] private ArticulationBody[] joints = new ArticulationBody[6];

    [Tooltip("그리퍼 끝 위치. wrist3_link 아래 빈 오브젝트 (2.5 그리퍼에서 사용)")]
    [SerializeField] private Transform toolPoint;

    [Header("관절 드라이브 (팔이 처지거나 떨리면 조절)")]
    [SerializeField] private float stiffness = 10000f;
    [SerializeField] private float damping = 100f;
    [SerializeField] private float forceLimit = 1000f;

    [Header("부드러운 이동")]
    [Tooltip("가장 많이 도는 관절의 평균 속도 (도/초). 이 값으로 이동 시간이 정해진다")]
    [SerializeField] private float jointSpeedDegPerSec = 60f;

    [Tooltip("아주 작은 이동도 최소 이 시간(초)은 걸리게 (너무 툭 움직이지 않게)")]
    [SerializeField] private float minMoveTimeSec = 0.4f;

    [Tooltip("실제 관절이 목표에 이만큼(도) 가까워지면 도착으로 본다")]
    [SerializeField] private float arriveToleranceDeg = 0.5f;

    // 이동 한 번의 상태 (단위: 도)
    private float[] startDeg;          // 출발 자세
    private float[] goalDeg;           // 최종 자세
    private float moveDuration;        // 이번 이동에 걸릴 시간
    private float moveElapsed;         // 지금까지 지난 시간

    public int ArmId => armId;
    public Transform ToolPoint => toolPoint;
    public ArticulationBody[] Joints => joints;

    /// <summary>목표 자세로 이동 중이면 true (IDLE/MOVING 상태 보고용)</summary>
    public bool IsMoving { get; private set; }

    /// <summary>토픽/로그용 이름. 예: arm_1 (NFR-08 명명 규칙)</summary>
    public string ArmName => $"arm_{armId}";

    void Awake()
    {
        if (joints == null || joints.Length != 6 || joints.Any(j => j == null))
            AutoFindJoints();

        ApplyDriveSettings();
        EnsureBuffers();

        // 베이스가 바닥으로 떨어지지 않게 루트 고정
        var root = GetComponentInChildren<ArticulationBody>();
        if (root != null && root.isRoot) root.immovable = true;
    }

    [ContextMenu("관절 자동 찾기")]
    public void AutoFindJoints()
    {
        joints = GetComponentsInChildren<ArticulationBody>()
            .Where(a => a.jointType == ArticulationJointType.RevoluteJoint)
            .Take(6)
            .ToArray();

        if (joints.Length != 6)
            Debug.LogWarning($"[{ArmName}] 회전 관절을 {joints.Length}개 찾음 (6개여야 함)", this);
        else
            Debug.Log($"[{ArmName}] 관절 6개: {string.Join(", ", joints.Select(j => j.name))}", this);
    }

    void ApplyDriveSettings()
    {
        foreach (var j in joints)
        {
            if (j == null) continue;
            var d = j.xDrive;
            d.stiffness = stiffness;
            d.damping = damping;
            d.forceLimit = forceLimit;
            j.xDrive = d;
        }
    }

    /// <summary>
    /// 목표 자세(라디안 6개)로 부드럽게 이동 시작.
    /// 이동 중에 새 목표가 오면 "지금 위치"에서 새로 출발한다.
    /// </summary>
    public void SetJointTargets(float[] radians)
    {
        if (!CheckLength(radians)) return;
        EnsureBuffers();

        // 지금 드라이브에 들어 있는 값 = 현재 위치에서 출발
        for (int i = 0; i < joints.Length; i++)
        {
            startDeg[i] = joints[i].xDrive.target;
            goalDeg[i] = radians[i] * Mathf.Rad2Deg;
        }

        // 가장 많이 도는 관절 기준으로 시간 계산 → 나머지 관절은 그 시간에 맞춰 천천히
        float maxDelta = 0f;
        for (int i = 0; i < joints.Length; i++)
            maxDelta = Mathf.Max(maxDelta, Mathf.Abs(goalDeg[i] - startDeg[i]));

        moveDuration = Mathf.Max(minMoveTimeSec, maxDelta / Mathf.Max(1f, jointSpeedDegPerSec));
        moveElapsed = 0f;
        IsMoving = true;
    }

    /// <summary>보간 없이 즉시 목표 자세로 (초기화·리셋용)</summary>
    public void SetJointTargetsImmediate(float[] radians)
    {
        if (!CheckLength(radians)) return;
        EnsureBuffers();

        for (int i = 0; i < joints.Length; i++)
        {
            goalDeg[i] = startDeg[i] = radians[i] * Mathf.Rad2Deg;
            WriteDriveTarget(i, goalDeg[i]);
        }
        moveElapsed = moveDuration = 0f;
        IsMoving = true; // 실제 관절이 도착하면 FixedUpdate에서 false
    }

    void FixedUpdate()
    {
        if (!IsMoving) return;

        moveElapsed += Time.fixedDeltaTime;
        float t = moveDuration > 0f ? Mathf.Clamp01(moveElapsed / moveDuration) : 1f;
        float s = SmootherStep(t);

        bool allReached = t >= 1f;
        for (int i = 0; i < joints.Length; i++)
        {
            WriteDriveTarget(i, Mathf.Lerp(startDeg[i], goalDeg[i], s));

            float actualDeg = joints[i].jointPosition[0] * Mathf.Rad2Deg;
            if (Mathf.Abs(actualDeg - goalDeg[i]) > arriveToleranceDeg)
                allReached = false;
        }

        if (allReached) IsMoving = false;
    }

    /// <summary>
    /// 0→1 S자 곡선. 출발·도착에서 속도와 가속도가 둘 다 0이라 툭 끊기는 느낌이 없다.
    /// 6t^5 - 15t^4 + 10t^3
    /// </summary>
    static float SmootherStep(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    /// <summary>
    /// 이동 계산용 배열을 관절 수에 맞게 준비 (현재 드라이브 목표값으로 채움).
    /// Awake 전에 불리거나, 에디터 재컴파일로 배열이 비어 있어도 안전하게.
    /// </summary>
    void EnsureBuffers()
    {
        int n = joints.Length;
        if (goalDeg != null && goalDeg.Length == n && startDeg != null && startDeg.Length == n) return;
        goalDeg = joints.Select(j => j.xDrive.target).ToArray();
        startDeg = (float[])goalDeg.Clone();
        IsMoving = false;
    }

    void WriteDriveTarget(int i, float deg)
    {
        var d = joints[i].xDrive;
        d.target = deg;
        joints[i].xDrive = d;
    }

    bool CheckLength(float[] radians)
    {
        if (joints == null || joints.Length == 0 || joints.Any(j => j == null))
        {
            Debug.LogError($"[{ArmName}] 관절이 비어 있음. ArmRig 우클릭 → '관절 자동 찾기'", this);
            return false;
        }
        if (radians != null && radians.Length == joints.Length) return true;
        Debug.LogError($"[{ArmName}] 관절값 개수가 맞지 않음: {radians?.Length} (필요 {joints.Length})", this);
        return false;
    }

    /// <summary>현재 관절 위치 (라디안)</summary>
    public float[] GetJointPositions()
    {
        return joints.Select(j => j.jointPosition.dofCount > 0 ? j.jointPosition[0] : 0f).ToArray();
    }

    // ── 테스트용: Play 중 컴포넌트 우클릭 메뉴에서 실행 ──
    [ContextMenu("테스트: 홈 자세 (전부 0)")]
    void TestHome() => TestMove(new float[6]);

    [ContextMenu("테스트: 자세 A")]
    void TestPoseA() => TestMove(new[] { 0.5f, -0.5f, 0.8f, 0f, 0.5f, 0f });

    [ContextMenu("테스트: 자세 B")]
    void TestPoseB() => TestMove(new[] { -0.8f, -0.3f, 1.2f, -0.6f, -0.4f, 1.0f });

    void TestMove(float[] rad)
    {
        if (!Application.isPlaying) { Debug.LogWarning("테스트 자세는 Play 중에만 동작해", this); return; }
        SetJointTargets(rad);
    }
}