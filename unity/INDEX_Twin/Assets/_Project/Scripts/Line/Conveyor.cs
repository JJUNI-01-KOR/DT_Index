using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 컨베이어 벨트 (FR-03, FR-04). 담당 A · scope unity-line
/// - SetRunning(true/false)로 가동·정지. 정지는 즉시 (속도 0) → 0.5초 이내 조건 충족
/// - 벨트 위 박스를 마찰로 실어 나른다 (물리 방식)
///   → B·C의 스토퍼가 박스를 막으면 박스는 멈추고 벨트만 미끄러진다
/// - 벨트 위 박스 수가 바뀌면 BoxCountChanged 이벤트 (ROS 발행은 ROS/ConveyorRosLink.cs)
///
/// 준비: 벨트 윗면 오브젝트에 BoxCollider(Trigger 아님) + 이 스크립트.
///       Rigidbody(Kinematic)는 자동으로 붙는다.
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

    [Tooltip("Play 시작할 때 가동 상태 (테스트용. 실제는 ROS 명령으로)")]
    [SerializeField] private bool runOnStart = false;

    [Header("박스 세기")]
    [Tooltip("벨트 윗면에서 이 높이(m)까지 있는 Rigidbody를 벨트 위 박스로 센다")]
    [SerializeField] private float countHeight = 0.6f;

    [Tooltip("셀 대상 레이어 (박스 레이어만 체크 권장)")]
    [SerializeField] private LayerMask boxLayers = ~0;

    [Tooltip("세는 주기 (초)")]
    [SerializeField] private float countIntervalSec = 0.1f;

    [Header("벨트 텍스처 스크롤 (선택, 보여주기용)")]
    [SerializeField] private Renderer beltRenderer;
    [SerializeField] private float textureScrollPerMeter = 1f;

    private Rigidbody rb;
    private BoxCollider beltCollider;
    private float countTimer;
    private float texOffset;
    private Material beltMat;
    private readonly HashSet<Rigidbody> onBelt = new HashSet<Rigidbody>();
    private readonly Collider[] hits = new Collider[64];

    public bool IsRunning { get; private set; }
    public int BoxCount { get; private set; }
    public float SpeedMps => speedMps;

    /// <summary>가동 상태가 바뀌면 (true=가동)</summary>
    public event Action<bool> RunningChanged;
    /// <summary>벨트 위 박스 수가 바뀌면</summary>
    public event Action<int> BoxCountChanged;

    Vector3 WorldDir => transform.TransformDirection(localDirection).normalized;

    void Awake()
    {
        beltCollider = GetComponent<BoxCollider>();
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        if (beltRenderer != null) beltMat = beltRenderer.material;
    }

    void Start()
    {
        IsRunning = runOnStart;
        RunningChanged?.Invoke(IsRunning);
        CountBoxes(force: true);
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
        // 벨트 이동: 벨트를 뒤로 순간이동시킨 뒤 원래 자리로 MovePosition
        // → 벨트 자체는 제자리, 위에 올라간 물체만 마찰로 앞으로 끌려간다 (흔한 Unity 컨베이어 기법)
        if (IsRunning && speedMps > 0f)
        {
            Vector3 pos = rb.position;
            rb.position -= WorldDir * speedMps * Time.fixedDeltaTime;
            rb.MovePosition(pos);
        }

        countTimer += Time.fixedDeltaTime;
        if (countTimer >= countIntervalSec)
        {
            countTimer = 0f;
            CountBoxes(force: false);
        }
    }

    void Update()
    {
        if (beltMat == null || !IsRunning) return;
        texOffset += speedMps * textureScrollPerMeter * Time.deltaTime;
        beltMat.mainTextureOffset = new Vector2(0f, texOffset);
    }

    void CountBoxes(bool force)
    {
        // 벨트 윗면 위쪽 공간을 상자 모양으로 검사
        Vector3 size = Vector3.Scale(beltCollider.size, transform.lossyScale);
        Vector3 topCenter = transform.TransformPoint(beltCollider.center) + transform.up * (size.y * 0.5f + countHeight * 0.5f);
        Vector3 half = new Vector3(Mathf.Abs(size.x) * 0.5f, countHeight * 0.5f, Mathf.Abs(size.z) * 0.5f);

        int n = Physics.OverlapBoxNonAlloc(topCenter, half, hits, transform.rotation, boxLayers, QueryTriggerInteraction.Ignore);

        onBelt.Clear();
        for (int i = 0; i < n; i++)
        {
            var body = hits[i].attachedRigidbody;
            if (body == null || body == rb || body.isKinematic) continue; // 팔이 들고 있는 박스(Kinematic)는 제외
            onBelt.Add(body);
        }

        if (force || onBelt.Count != BoxCount)
        {
            BoxCount = onBelt.Count;
            BoxCountChanged?.Invoke(BoxCount);
        }
    }

    void OnDrawGizmosSelected()
    {
        var bc = GetComponent<BoxCollider>();
        if (bc == null) return;
        Vector3 size = Vector3.Scale(bc.size, transform.lossyScale);
        Vector3 topCenter = transform.TransformPoint(bc.center) + transform.up * (size.y * 0.5f + countHeight * 0.5f);
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.25f);
        Gizmos.matrix = Matrix4x4.TRS(topCenter, transform.rotation, Vector3.one);
        Gizmos.DrawCube(Vector3.zero, new Vector3(Mathf.Abs(size.x), countHeight, Mathf.Abs(size.z)));
        Gizmos.matrix = Matrix4x4.identity;

        // 흐름 방향 화살표
        Gizmos.color = Color.cyan;
        Vector3 from = topCenter;
        Vector3 dir = transform.TransformDirection(localDirection).normalized;
        Gizmos.DrawLine(from, from + dir * 0.5f);
        Gizmos.DrawSphere(from + dir * 0.5f, 0.03f);
    }

    // ── 테스트용 ──
    [ContextMenu("테스트: 가동")] void TestRun() => SetRunning(true);
    [ContextMenu("테스트: 정지")] void TestStop() => SetRunning(false);
}
