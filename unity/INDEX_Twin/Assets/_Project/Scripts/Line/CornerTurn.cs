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
        if (inside.Count == 0) return;

        Vector3 inDir = Flat(Dir(inDirection));
        Vector3 outDir = Flat(Dir(outDirection));
        Vector3 c = transform.TransformPoint(zone.center);
        float v = follow != null ? (follow.IsRunning ? follow.SpeedMps : 0f) : speedMps;

        foreach (var rb in inside)
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