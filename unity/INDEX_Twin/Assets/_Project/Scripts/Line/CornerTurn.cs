using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 코너 꺾기 (CornerConveyor 칸에 둔다). 담당 A · scope unity-line
/// Diverter와 같은 방식: 방석 중심이 코너 중앙에 올 때까지는 들어오는 방향(하늘색)으로 직진,
/// 중앙을 지나면 나가는 방향(초록)으로 꺾는다. 벽 쪽으로 밀리는 속도가 남지 않아서 끼지 않는다.
///
/// 동작 원리: Conveyor가 매 프레임 방석을 민 다음에 이 스크립트가 실행돼서(DefaultExecutionOrder)
/// 영역 안 방석의 수평 속도를 덮어쓴다. 영역을 벗어나면 나가는 쪽 Conveyor가 이어서 옮긴다.
///
/// 준비: 코너 칸 아래 빈 오브젝트 + BoxCollider(Is Trigger) + 이 스크립트. 코너 칸 위를 덮게.
/// 선택하면 하늘색(들어오는 방향), 초록(나가는 방향) 화살표가 보인다. 화살표가 실제 흐름과 맞게 방향을 고른다.
/// 컨베이어가 정지(/conveyor/cmd false)면 방석도 멈춘다.
///
/// 11/02 A: 감지 보강. 씬의 코너 Trigger(1×1×1)가 칸 바닥 상자와 높이가 같아서 윗면이 벨트 면(y 0.5)에서 끝난다.
///   방석은 벨트 면 위에 떠 있어서 Trigger에 거의 안 닿고 → OnTriggerEnter가 안 불려 코너를 못 꺾고 멈췄다가,
///   뒤 방석이 밀어줘야 움직였다. 이제 Trigger 이벤트와 별개로 매 물리 프레임 코너 칸 영역 + 위로 detectAbove(m)까지
///   직접 검사해서 방석을 잡는다 (LaneEnd 감지 보강과 같은 방식). 씬은 안 고쳐도 된다.
///
/// 11/02 A: 걸림 풀기. 위 보강 뒤에도 코너 출구(다음 칸과의 경계)에서 방석이 서는 현상이 남았다.
///   Play 중 확인: 방석 앞 끝이 칸 경계(x -6.00)에 딱 붙어 속도 0, 5 cm 들어 올리면 바로 다시 나감
///   → 바닥 경계에서 물리 접촉에 걸리는 것. 이 칸에서 방석이 가야 할 만큼 안 움직이는 시간이 stuckSec을 넘으면
///   위로 nudgeUp, 진행 방향으로 nudgeForward만큼 살짝 옮겨 경계를 넘게 한다. 컨베이어가 서 있을 때(v = 0)는 하지 않는다.
/// </summary>
[DefaultExecutionOrder(100)]   // Conveyor(기본 0)보다 나중에 실행
[RequireComponent(typeof(BoxCollider))]
public class CornerTurn : MonoBehaviour
{
    [Tooltip("켜면 방향을 월드 축으로 적는다 (Scene 오른쪽 위 축 표시 기준: 빨강 X, 파랑 Z). 끄면 이 오브젝트 로컬 축")]
    [SerializeField] private bool worldAxes = true;

    [Tooltip("방석이 들어오는 방향")]
    [SerializeField] private Vector3 inDirection = Vector3.forward;

    [Tooltip("코너 중앙을 지난 뒤 나가는 방향")]
    [SerializeField] private Vector3 outDirection = Vector3.right;

    [Tooltip("가동·정지와 속도를 따라갈 컨베이어. 비우면 부모에서 찾는다")]
    [SerializeField] private Conveyor follow;

    [Tooltip("follow가 없을 때 쓰는 속도 (m/s)")]
    [SerializeField] private float speedMps = 0.2f;

    [Tooltip("옆으로 어긋난 방석을 칸 중앙 줄로 당기는 세기 (0이면 안 당김)")]
    [SerializeField] private float centering = 2f;

    [Tooltip("이 태그만 대상으로 (비우면 Rigidbody 전부)")]
    [SerializeField] private string itemTag = "Cushion";

    [Tooltip("11/02 A: Trigger 윗면 위로 이 높이(m)까지도 방석을 찾는다. 0이면 Trigger 이벤트만 씀")]
    [SerializeField] private float detectAbove = 0.3f;

    private readonly Collider[] probe = new Collider[16];

    [Header("걸림 풀기 (11/02 A)")]
    [Tooltip("이 시간(초) 동안 기대 이동의 30%도 못 가면 걸린 것으로 본다. 0이면 안 씀")]
    [SerializeField] private float stuckSec = 0.3f;
    [Tooltip("걸렸을 때 위로 옮기는 거리 (m)")]
    [SerializeField] private float nudgeUp = 0.01f;
    [Tooltip("걸렸을 때 진행 방향으로 옮기는 거리 (m)")]
    [SerializeField] private float nudgeForward = 0.01f;

    private class Track { public Vector3 last; public float stuck; public int nudges; }
    private readonly Dictionary<Rigidbody, Track> tracks = new Dictionary<Rigidbody, Track>();

    private readonly HashSet<Rigidbody> inside = new HashSet<Rigidbody>();
    private BoxCollider zone;

    void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    void Awake()
    {
        zone = GetComponent<BoxCollider>();
        if (!zone.isTrigger) Debug.LogWarning($"[corner_turn:{name}] BoxCollider의 Is Trigger를 켜야 해", this);
        if (follow == null) follow = GetComponentInParent<Conveyor>();
    }

    void OnTriggerEnter(Collider other)
    {
        var rb = other.attachedRigidbody;
        if (rb == null) return;
        if (!string.IsNullOrEmpty(itemTag) && !other.CompareTag(itemTag) && !rb.CompareTag(itemTag)) return;
        inside.Add(rb);
    }

    void OnTriggerExit(Collider other)
    {
        var rb = other.attachedRigidbody;
        if (rb != null) inside.Remove(rb);
    }

    void FixedUpdate()
    {
        inside.RemoveWhere(rb => rb == null);
        var targets = new HashSet<Rigidbody>(inside);
        if (detectAbove > 0f) AddByOverlap(targets);
        // 코너를 떠난 방석은 추적에서 뺀다
        if (tracks.Count > 0)
        {
            var gone = new List<Rigidbody>();
            foreach (var k in tracks.Keys) if (k == null || !targets.Contains(k)) gone.Add(k);
            foreach (var k in gone) tracks.Remove(k);
        }
        if (targets.Count == 0) return;

        Vector3 inDir = Flat(Dir(inDirection));
        Vector3 outDir = Flat(Dir(outDirection));
        Vector3 c = transform.TransformPoint(zone.center);
        float v = follow != null ? (follow.IsRunning ? follow.SpeedMps : 0f) : speedMps;

        foreach (var rb in targets)
        {
            if (rb.isKinematic) continue;   // 팔이 들고 있는 방석 제외

            Vector3 fromCenter = rb.position - c; fromCenter.y = 0f;
            // 중앙 전: 들어오는 방향, 중앙 후: 나가는 방향
            Vector3 dir = Vector3.Dot(fromCenter, inDir) < 0f ? inDir : outDir;

            // 진행 방향 옆으로 어긋난 만큼 중앙 줄로 당긴다 (벽에 붙지 않게)
            Vector3 side = fromCenter - dir * Vector3.Dot(fromCenter, dir);
            Vector3 h = dir * v - side * (centering * v);

            // 수평 속도는 전부 교체, 수직(중력)은 유지
            rb.linearVelocity = new Vector3(h.x, rb.linearVelocity.y, h.z);
            rb.angularVelocity = Vector3.zero;
            if (v > 0f) rb.WakeUp();

            if (stuckSec > 0f) Unstick(rb, dir, v);
        }
    }

    /// <summary>진행 방향으로 기대만큼 안 가는 시간이 stuckSec을 넘으면 살짝 들어 올려 앞으로 옮긴다</summary>
    void Unstick(Rigidbody rb, Vector3 dir, float v)
    {
        if (!tracks.TryGetValue(rb, out var tr)) { tracks[rb] = new Track { last = rb.position }; return; }
        float moved = Vector3.Dot(rb.position - tr.last, dir);
        tr.last = rb.position;
        if (v <= 0f) { tr.stuck = 0f; return; }                 // 컨베이어 정지 중
        float dt = Time.fixedDeltaTime;
        tr.stuck = moved < v * dt * 0.3f ? tr.stuck + dt : 0f;
        if (tr.stuck < stuckSec) return;

        rb.position += Vector3.up * nudgeUp + dir * nudgeForward;
        tr.last = rb.position;
        tr.stuck = 0f;
        tr.nudges++;
        if (tr.nudges == 1)
            Debug.Log($"[corner_turn:{transform.parent?.name}] {rb.name} 경계에 걸림 → 살짝 들어서 넘김", this);
    }

    /// <summary>코너 칸 영역(Trigger 상자)을 위로 detectAbove만큼 늘려서, 중심이 그 안에 있는 방석을 더한다</summary>
    void AddByOverlap(HashSet<Rigidbody> set)
    {
        var t = zone.transform;
        Vector3 size = Vector3.Scale(zone.size, t.lossyScale);
        size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
        Vector3 c = t.TransformPoint(zone.center) + t.up * (detectAbove * 0.5f);
        Vector3 half = new Vector3(size.x, size.y + detectAbove, size.z) * 0.5f;
        int n = Physics.OverlapBoxNonAlloc(c, half, probe, t.rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var rb = probe[i].attachedRigidbody;
            if (rb == null || rb.isKinematic) continue;
            if (!string.IsNullOrEmpty(itemTag) && !probe[i].CompareTag(itemTag) && !rb.CompareTag(itemTag)) continue;
            // 방석 중심이 코너 칸 바닥 범위 안일 때만 (옆 칸에 조금 걸친 방석은 그 칸 컨베이어가 민다)
            Vector3 local = Quaternion.Inverse(t.rotation) * (rb.position - t.TransformPoint(zone.center));
            if (Mathf.Abs(local.x) > size.x * 0.5f || Mathf.Abs(local.z) > size.z * 0.5f) continue;
            set.Add(rb);
        }
    }

    Vector3 Dir(Vector3 d) => worldAxes ? d : transform.TransformDirection(d);

    static Vector3 Flat(Vector3 d) { d.y = 0f; return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.zero; }

    void OnDrawGizmos()
    {
        var bc = GetComponent<BoxCollider>();
        if (bc == null) return;
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(bc.center, bc.size);
        Gizmos.matrix = Matrix4x4.identity;

        Vector3 c = transform.TransformPoint(bc.center) + Vector3.up * 0.2f;
        Vector3 i = Flat(Dir(inDirection));
        Vector3 o = Flat(Dir(outDirection));
        DrawArrow(c - i * 0.6f, i, Color.cyan);
        DrawArrow(c, o, Color.green);
    }

    static void DrawArrow(Vector3 from, Vector3 dir, Color col)
    {
        Gizmos.color = col;
        Gizmos.DrawLine(from, from + dir * 0.6f);
        Gizmos.DrawSphere(from + dir * 0.6f, 0.04f);
    }
}