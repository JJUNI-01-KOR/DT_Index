using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 컨베이어 벨트 (FR-03, FR-04). 담당 A · scope unity-line
/// - SetRunning(true/false)로 가동·정지. 정지하면 박스의 흐름 방향 속도를 바로 0으로 → 0.5초 이내
/// - 벨트 위 박스(Rigidbody)의 "흐름 방향 속도"만 매 물리 프레임 speedMps로 맞춘다
///   · 옆 방향 속도(푸셔가 미는 힘)와 중력은 그대로 둔다
///   · 스토퍼에 막히면 박스는 물리적으로 멈춘다 (벨트가 계속 밀어도 통과 못 함)
/// - 벨트 위 박스 수가 바뀌면 BoxCountChanged 이벤트 (ROS 발행은 ROS/ConveyorLink.cs)
///
/// 준비: 벨트 윗면 오브젝트에 BoxCollider(Trigger 아님, 박스가 올라설 바닥) + 이 스크립트.
///       Local Direction = 박스가 흘러갈 방향 (이 오브젝트 로컬 축).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public class Conveyor : MonoBehaviour
{
    [Header("동작")]
    [Tooltip("벨트 속도 (m/s). SRS 기본 0.2")]
    [SerializeField] private float speedMps = 0.2f;

    [Tooltip("박스가 흘러가는 방향 (이 오브젝트 로컬 축). 기본 +Z(파란 화살표)")]
    [SerializeField] private Vector3 localDirection = Vector3.forward;

    [Tooltip("벨트 위 박스가 굴러가지 않게 회전을 막는다")]
    [SerializeField] private bool stopTumbling = true;

    [Tooltip("Play 시작할 때 가동 상태 (테스트용. 실제는 ROS 명령으로)")]
    [SerializeField] private bool runOnStart = false;

    [Header("박스 세기")]
    [Tooltip("벨트 윗면에서 이 높이(m)까지 있는 Rigidbody를 벨트 위 박스로 본다")]
    [SerializeField] private float countHeight = 0.6f;

    [Tooltip("대상 레이어 (박스 레이어만 체크 권장)")]
    [SerializeField] private LayerMask boxLayers = ~0;

    [Tooltip("Box 태그가 붙은 것만 대상으로 (가이드 3.2). 끄면 Rigidbody 전부")]
    [SerializeField] private bool onlyBoxTag = true;

    [Header("벨트 텍스처 스크롤 (선택, 보여주기용)")]
    [SerializeField] private Renderer beltRenderer;
    [SerializeField] private float textureScrollPerMeter = 1f;

    private BoxCollider beltCollider;
    private float texOffset;
    private Material beltMat;
    private readonly HashSet<Rigidbody> onBelt = new HashSet<Rigidbody>();
    private readonly Collider[] hits = new Collider[64];

    public bool IsRunning { get; private set; }
    public int BoxCount { get; private set; } = -1;
    public float SpeedMps => speedMps;

    /// <summary>가동 상태가 바뀌면 (true=가동)</summary>
    public event Action<bool> RunningChanged;
    /// <summary>벨트 위 박스 수가 바뀌면</summary>
    public event Action<int> BoxCountChanged;

    Vector3 WorldDir => transform.TransformDirection(localDirection).normalized;

    void Awake()
    {
        beltCollider = GetComponent<BoxCollider>();
        if (beltRenderer != null) beltMat = beltRenderer.material;
    }

    void Start()
    {
        IsRunning = runOnStart;
        RunningChanged?.Invoke(IsRunning);
    }

    /// <summary>가동(true)·정지(false). 같은 명령이 다시 오면 그대로 유지 (FR-03 예외 처리)</summary>
    public void SetRunning(bool run)
    {
        if (IsRunning == run) return;
        IsRunning = run;
        Debug.Log($"[conveyor] {(run ? "가동" : "정지")}", this);
        RunningChanged?.Invoke(run);
    }

    void FixedUpdate()
    {
        FindBoxesOnBelt();

        Vector3 dir = WorldDir;
        float v = IsRunning ? speedMps : 0f;
        foreach (var body in onBelt)
        {
            Vector3 cur = body.linearVelocity;
            // 흐름 방향 성분만 바꾼다. 옆(푸셔)·위아래(중력) 속도는 그대로
            body.linearVelocity = cur - dir * Vector3.Dot(cur, dir) + dir * v;
            // 벨트 바닥 마찰 때문에 박스가 앞으로 넘어지며 굴러가는 것 방지:
            // 벨트 위에서는 회전을 없앤다 (박스는 미끄러지듯 이동)
            if (stopTumbling) body.angularVelocity = Vector3.zero;
            if (IsRunning) body.WakeUp();
        }

        if (onBelt.Count != BoxCount)
        {
            BoxCount = onBelt.Count;
            BoxCountChanged?.Invoke(BoxCount);
        }
    }

    void Update()
    {
        if (beltMat == null || !IsRunning) return;
        texOffset += speedMps * textureScrollPerMeter * Time.deltaTime;
        beltMat.mainTextureOffset = new Vector2(0f, texOffset);
    }

    void FindBoxesOnBelt()
    {
        // 벨트 윗면 위쪽 공간을 상자 모양으로 검사
        Vector3 size = Vector3.Scale(beltCollider.size, transform.lossyScale);
        Vector3 topCenter = transform.TransformPoint(beltCollider.center) + transform.up * (Mathf.Abs(size.y) * 0.5f + countHeight * 0.5f);
        Vector3 half = new Vector3(Mathf.Abs(size.x) * 0.5f, countHeight * 0.5f, Mathf.Abs(size.z) * 0.5f);

        int n = Physics.OverlapBoxNonAlloc(topCenter, half, hits, transform.rotation, boxLayers, QueryTriggerInteraction.Ignore);

        onBelt.Clear();
        for (int i = 0; i < n; i++)
        {
            var body = hits[i].attachedRigidbody;
            if (body == null || body.isKinematic) continue;          // 팔이 들고 있는 박스(Kinematic)는 제외
            if (onlyBoxTag && !hits[i].CompareTag("Box") && !body.CompareTag("Box")) continue;
            onBelt.Add(body);
        }
    }

    void OnDrawGizmosSelected()
    {
        var bc = GetComponent<BoxCollider>();
        if (bc == null) return;
        Vector3 size = Vector3.Scale(bc.size, transform.lossyScale);
        Vector3 topCenter = transform.TransformPoint(bc.center) + transform.up * (Mathf.Abs(size.y) * 0.5f + countHeight * 0.5f);
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.25f);
        Gizmos.matrix = Matrix4x4.TRS(topCenter, transform.rotation, Vector3.one);
        Gizmos.DrawCube(Vector3.zero, new Vector3(Mathf.Abs(size.x), countHeight, Mathf.Abs(size.z)));
        Gizmos.matrix = Matrix4x4.identity;

        // 흐름 방향 화살표
        Gizmos.color = Color.cyan;
        Vector3 dir = transform.TransformDirection(localDirection).normalized;
        Gizmos.DrawLine(topCenter, topCenter + dir * 0.5f);
        Gizmos.DrawSphere(topCenter + dir * 0.5f, 0.03f);
    }

    /// <summary>
    /// 빈 오브젝트 아래에 칸들을 넣었을 때: 자식들의 바닥 콜라이더를 다 덮도록
    /// 이 오브젝트의 BoxCollider(Trigger)를 자동으로 맞춘다. 윗면 높이 = 칸 콜라이더 윗면.
    /// </summary>
    [ContextMenu("자식 칸에 맞춰 벨트 영역 자동 설정")]
    void FitToChildren()
    {
        var bc = GetComponent<BoxCollider>();
        bool any = false;
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;

        foreach (var c in GetComponentsInChildren<Collider>(true))
        {
            if (c == bc || c.isTrigger || !c.enabled || !c.gameObject.activeInHierarchy) continue;
            if (c.GetComponentInParent<Conveyor>() != this) continue;
            Bounds b = c.bounds;   // 월드 AABB
            // 8개 꼭짓점을 이 오브젝트 로컬로 바꿔서 범위 계산
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 local = transform.InverseTransformPoint(corner);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
                any = true;
            }
        }

        if (!any)
        {
            Debug.LogWarning("[conveyor] 자식 칸에서 바닥 콜라이더(Trigger 아님)를 못 찾음. 칸들을 이 오브젝트 아래에 넣었는지 확인", this);
            return;
        }

#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(bc, "Fit Conveyor Area");
#endif
        const float thick = 0.02f;
        bc.isTrigger = true;   // 영역 표시용. 바닥은 각 칸 콜라이더가 맡는다
        bc.center = new Vector3((min.x + max.x) * 0.5f, max.y - thick * 0.5f, (min.z + max.z) * 0.5f);
        bc.size = new Vector3(max.x - min.x, thick, max.z - min.z);
        Debug.Log($"[conveyor] 벨트 영역 설정: 크기 {bc.size.x:F2} × {bc.size.z:F2} m, 윗면 로컬 y {max.y:F2}", this);
    }

    // ── 테스트용 ──
    [ContextMenu("테스트: 가동")] void TestRun() => SetRunning(true);
    [ContextMenu("테스트: 정지")] void TestStop() => SetRunning(false);
}