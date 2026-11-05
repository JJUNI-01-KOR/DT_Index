using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 분기기 (JoinerConveyor 칸에 둔다). 검사 결과에 따라 좌/우로 보낸다.
/// - 정상 → Pass Direction (기본 0,0,-1)
/// - 불량 → Reject Direction (기본 0,0,1)
/// - 검사 안 받은 물건 → Unchecked Goes Reject 설정에 따름
///
/// 동작 원리: 메인 Conveyor가 매 프레임 물건을 앞으로 밀면, 이 스크립트가 그 다음에 실행돼서
/// (DefaultExecutionOrder) 영역 안 물건의 속도를 좌/우 방향으로 덮어쓴다.
/// 영역을 벗어나면 좌/우 가지의 Conveyor가 이어서 옮긴다.
///
/// 준비: 빈 오브젝트 + BoxCollider(Is Trigger) + 이 스크립트. Joiner 칸 위를 덮게.
/// 선택하면 하늘색(들어오는 방향), 초록(정상), 빨강(불량) 화살표가 보인다.
/// 물건 중심이 칸 중앙에 올 때까지는 직진하고, 그 다음 좌/우로 꺾는다.
///
/// 11/02 A: 판정은 ROS(factory_manager → DiverterLink)가 결과표(ItemSort)에 적는다 (SRS FR-08).
///          방향은 방석이 칸 중앙을 지나는 순간 한 번 정하고 고정한다 (늦게 온 명령으로 꺾이는 도중 방향이 바뀌지 않게).
///          그때까지 명령이 없으면 Unchecked Goes Reject에 따라 불량 레인 + 경고 로그 (FR-08 예외 처리).
/// </summary>
[DefaultExecutionOrder(100)]   // Conveyor(기본 0)보다 나중에 실행
[RequireComponent(typeof(BoxCollider))]
public class Diverter : MonoBehaviour
{
    [Tooltip("정상품이 갈 방향 (이 오브젝트 로컬 축)")]
    [SerializeField] private Vector3 passDirection = Vector3.back;      // 0,0,-1

    [Tooltip("불량품이 갈 방향 (이 오브젝트 로컬 축)")]
    [SerializeField] private Vector3 rejectDirection = Vector3.forward; // 0,0,1

    [Tooltip("물건이 들어오는 방향 (이 오브젝트 로컬 축). 칸 중앙까지는 이 방향으로 직진한 뒤 꺾는다")]
    [SerializeField] private Vector3 inDirection = Vector3.left;

    [Tooltip("옆으로 보내는 속도 (m/s)")]
    [SerializeField] private float speedMps = 0.3f;

    [Tooltip("검사 안 받은 물건을 불량 쪽으로 보낼지 (안전하게는 true)")]
    [SerializeField] private bool uncheckedGoesReject = true;

    [Tooltip("분기 동작 켜기/끄기")]
    [SerializeField] private bool active = true;

    private readonly HashSet<Rigidbody> inside = new HashSet<Rigidbody>();
    private readonly Dictionary<Rigidbody, bool> locked = new Dictionary<Rigidbody, bool>();   // 칸 중앙에서 정한 방향 (true = 불량)

    public bool Active { get => active; set => active = value; }

    /// <summary>지금 분기 칸 위에 있는 물건. ConveyorLoad가 메인 라인 수로 센다 (분기 게이트 위 = 메인)</summary>
    public IEnumerable<Rigidbody> Inside => inside;

    void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    void OnTriggerEnter(Collider other)
    {
        var rb = other.attachedRigidbody;
        if (rb == null) return;
        inside.Add(rb);
    }

    /// <summary>칸 중앙에서 방향을 한 번 정한다</summary>
    bool Decide(Rigidbody rb)
    {
        var sort = rb.GetComponent<ItemSort>();
        bool isReject;
        if (sort != null && sort.Inspected)
        {
            isReject = sort.Reject;
            Debug.Log($"[diverter] {rb.name}: {sort.Reason} → {(isReject ? "불량 레인" : "정상 레인")}", this);
        }
        else
        {
            isReject = uncheckedGoesReject;
            Debug.LogWarning($"[diverter] {rb.name}: 분기 명령 없이 갈림길 도착 → {(isReject ? "불량 레인" : "정상 레인")} (FR-08)", this);
        }
        locked[rb] = isReject;
        return isReject;
    }

    void OnTriggerExit(Collider other)
    {
        var rb = other.attachedRigidbody;
        if (rb == null) return;
        inside.Remove(rb);
        locked.Remove(rb);
    }

    void FixedUpdate()
    {
        if (!active) return;
        inside.RemoveWhere(rb => rb == null);
        if (locked.Count > 0)
        {
            var gone = new List<Rigidbody>();
            foreach (var k in locked.Keys) if (k == null) gone.Add(k);
            foreach (var k in gone) locked.Remove(k);
        }

        Vector3 pass = transform.TransformDirection(passDirection).normalized;
        Vector3 reject = transform.TransformDirection(rejectDirection).normalized;
        Vector3 inDir = transform.TransformDirection(inDirection); inDir.y = 0f; inDir.Normalize();
        var bc = GetComponent<BoxCollider>();
        Vector3 zoneCenter = transform.TransformPoint(bc.center);

        foreach (var rb in inside)
        {
            if (rb.isKinematic) continue;
            Vector3 dir;

            // 칸 중앙에 오기 전에는 계속 직진 (가장자리만 걸쳤을 때 미리 꺾여서 레일에 끼는 것 방지)
            Vector3 fromCenter = rb.position - zoneCenter; fromCenter.y = 0f;
            if (!locked.TryGetValue(rb, out bool isReject) && Vector3.Dot(fromCenter, inDir) < 0f)
            {
                // 직진하면서 옆으로 어긋난 건 칸 중앙 줄로 살짝 맞춘다
                Vector3 side = fromCenter - inDir * Vector3.Dot(fromCenter, inDir);
                dir = (inDir - side * 2f).normalized;
            }
            else
            {
                if (!locked.ContainsKey(rb)) isReject = Decide(rb);   // 중앙을 지나는 순간 한 번
                dir = isReject ? reject : pass;
            }

            // 수평 속도는 전부 분기 방향으로 교체, 수직(중력)은 유지
            Vector3 vertical = Vector3.Project(rb.linearVelocity, Vector3.up);
            rb.linearVelocity = vertical + dir * speedMps;
            rb.angularVelocity = Vector3.zero;
        }
    }

    void OnDrawGizmos()
    {
        var bc = GetComponent<BoxCollider>();
        if (bc == null) return;
        Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.3f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(bc.center, bc.size);
        Gizmos.matrix = Matrix4x4.identity;

        Vector3 c = transform.TransformPoint(bc.center) + Vector3.up * 0.2f;
        DrawArrow(c, transform.TransformDirection(passDirection).normalized, Color.green);
        DrawArrow(c, transform.TransformDirection(rejectDirection).normalized, Color.red);
        DrawArrow(c - transform.TransformDirection(inDirection).normalized * 0.6f, transform.TransformDirection(inDirection).normalized, Color.cyan);
    }

    static void DrawArrow(Vector3 from, Vector3 dir, Color col)
    {
        Gizmos.color = col;
        Gizmos.DrawLine(from, from + dir * 0.6f);
        Gizmos.DrawSphere(from + dir * 0.6f, 0.04f);
    }
}