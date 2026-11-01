// LaneEnd.cs : 레인 끝 스토퍼와 집는 자리 (FR-09). scope unity-line
// ROS를 모른다. 토픽 발행은 Scripts/ROS/LaneEndLink.cs가 한다 (CONVENTIONS 3절).
//
// 붙이는 곳: 레인 끝 Stopper(Trigger 콜라이더). 같은 오브젝트에 LaneEndLink도 붙인다.
//   pickSpot = BufferZone1 (1번 칸, 로봇팔이 집는 자리)
//   waitSpot = BufferZone2 (2번 칸, 줄 서는 자리)
//
// 동작
//   1. 방석(태그 Cushion)이 Stopper에 닿으면 멈추고(Kinematic) 빈 칸으로 옮긴다. 1번 → 2번 순.
//      칸 판 윗면에 방석 바닥이 닿게 놓는다 (방석 중심 높이 = 판 윗면 + 방석 높이/2).
//   2. 1번 칸에 들어갈 때마다 ArrivedAtPickSpot을 1회 알린다 → LaneEndLink가 /conveyor_1_end_<lane> 발행.
//   3. 두 칸이 다 차 있으면 닿은 자리에서 멈춰 줄 선다 (경고 로그). FR-04 레인 용량으로 원래는 생기지 않는다.
//   4. 1번 칸 방석이 다른 곳에 놓이면(팔이 내려놓아 물리가 다시 켜지고 leaveDistance보다 멀어짐),
//      ConfirmPicked/NotifyPlaced가 불리면, 또는 삭제되면 2번 칸 → 1번 칸, 줄 맨 앞 → 2번 칸으로 당긴다.
//      팔이 15초 자체 중단으로 방석을 1번 칸 근처에 되돌려 놓으면 칸에 다시 고정한다 (id는 다시 안 보냄).
//   5. ResetAll(): 칸과 줄의 방석을 모두 없앤다 (FR-16 해제 초기화). LaneEndLink가 E_STOP → IDLE에서 부른다.
//   6. (11/01 A) 감지 보강: Trigger 이벤트 말고도 매 물리 프레임 Stopper 영역 + 위로 extraDetectHeight까지 검사한다.
//      납작한 방석(0.1 m)이 칸 판 위에 똑바로 내려앉으면 Stopper 윗면에 안 닿아 놓치던 문제 해결.
//   7. (11/01 A) 칸 2개가 다 차면 벨트 위 방석을 레인 끝 앞(holdDistance)에서 세워 둔다.
//      예전에는 3번째 방석이 벨트 끝에서 기울며 떨어지다 그 자리에서 Kinematic으로 굳었다.
//      칸이 비면 다시 놓아줘서 컨베이어가 밀어 내린다. (레인 용량 3 = 집는 자리 + 대기 + 벨트 위 1)
//
// 로봇팔 서버(ArmPickPlaceServer)에서 쓰는 법
//   - 칸의 방석은 Kinematic이라 KinematicGripper.Grab()(물리 켜진 것만 찾음)으로는 안 잡힌다.
//     요청의 box_id로 LaneEnd.TryGetAtPick(id, out var cushion) → gripper.Attach(rb)로 잡는다.
//   - 놓기 성공 뒤 LaneEnd.NotifyPlaced(cushion)를 부르면 바로 당긴다 (안 불러도 4번 규칙으로 당겨짐).

using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]   // Conveyor(0)가 민 다음에 세워 둔다
[RequireComponent(typeof(Collider))]
public class LaneEnd : MonoBehaviour
{
    [Header("칸")]
    [Tooltip("1번 칸: 로봇팔이 집는 자리 (BufferZone1)")]
    [SerializeField] private Transform pickSpot;
    [Tooltip("2번 칸: 줄 서는 자리 (BufferZone2)")]
    [SerializeField] private Transform waitSpot;

    [Header("방석")]
    [SerializeField] private string cushionTag = "Cushion";
    [Tooltip("1번 칸 방석이 물리가 켜진 채 수평으로 이 거리(m)보다 멀리 있으면 팔이 다른 곳에 놓은 것으로 본다. 가까우면 칸에 다시 고정")]
    [SerializeField] private float leaveDistance = 0.5f;

    [Header("감지 · 대기 (11/01 A)")]
    [Tooltip("Stopper 영역 위로 이 높이(m)까지도 방석 도착으로 본다 (납작한 방석이 Stopper에 안 닿는 문제)")]
    [SerializeField] private float extraDetectHeight = 0.15f;
    [Tooltip("칸 2개가 다 찼을 때, Stopper 중심에서 수평으로 이 거리(m) 안에 들어온 벨트 위 방석을 세워 둔다")]
    [SerializeField] private float holdDistance = 1.0f;
    [Tooltip("칸 판 윗면보다 이만큼(m) 높이 있는 방석만 '벨트 위'로 본다 (떨어지는 중인 방석은 세우지 않음)")]
    [SerializeField] private float onBeltMinHeight = 0.2f;

    [Header("로그")]
    [Tooltip("로그에 붙는 이름 (ok, ng)")]
    [SerializeField] private string laneLabel = "";

    /// <summary>방석이 1번 칸(집는 자리)에 들어올 때마다 1회</summary>
    public event Action<GameObject> ArrivedAtPickSpot;

    /// <summary>레인 끝에 있는 방석 수(칸 2개 + 줄)가 바뀔 때</summary>
    public event Action<int> CountChanged;

    public GameObject AtPick => atPick;
    public GameObject AtWait => atWait;
    public Transform PickSpot => pickSpot;
    public string LaneLabel => laneLabel;
    /// <summary>칸 2개가 다 차서 줄 선 방석 (보통 비어 있음)</summary>
    public IReadOnlyList<GameObject> Queue => queue;
    public int Count => (atPick != null ? 1 : 0) + (atWait != null ? 1 : 0) + queue.Count;

    private GameObject atPick;
    private GameObject atWait;
    private readonly List<GameObject> queue = new List<GameObject>();
    private int lastCount;

    private static readonly List<LaneEnd> all = new List<LaneEnd>();

    private string Tag => $"[LaneEnd {(string.IsNullOrEmpty(laneLabel) ? name : laneLabel)}]";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ClearStatics() => all.Clear();

    void Reset()
    {
        // 같은 부모 아래 BufferZone1, BufferZone2를 자동으로 찾는다
        if (transform.parent == null) return;
        pickSpot = transform.parent.Find("BufferZone1");
        waitSpot = transform.parent.Find("BufferZone2");
    }

    void Awake()
    {
        var col = GetComponent<Collider>();
        if (!col.isTrigger) Debug.LogWarning($"{Tag} 콜라이더가 Trigger가 아님. Is Trigger를 켜야 방석이 닿은 것을 안다", this);
        if (pickSpot == null || waitSpot == null)
        {
            Debug.LogError($"{Tag} pickSpot(BufferZone1)·waitSpot(BufferZone2)이 연결되지 않음", this);
            enabled = false;
        }
    }

    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable() => all.Remove(this);

    void OnTriggerEnter(Collider other)
    {
        if (!enabled) return;
        var rb = other.attachedRigidbody;
        Arrive(rb != null ? rb.gameObject : other.gameObject);
    }

    private readonly Collider[] probe = new Collider[32];
    private Collider stopperCol;
    private bool holding;

    void FixedUpdate()
    {
        if (stopperCol == null) stopperCol = GetComponent<Collider>();
        Bounds b = stopperCol.bounds;

        // 6. 감지 보강: Stopper 영역 + 위쪽 extraDetectHeight
        Vector3 c = b.center + Vector3.up * (extraDetectHeight * 0.5f);
        Vector3 half = b.extents + Vector3.up * (extraDetectHeight * 0.5f);
        int n = Physics.OverlapBoxNonAlloc(c, half, probe, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var rb = probe[i].attachedRigidbody;
            if (rb == null || rb.isKinematic) continue;
            Arrive(rb.gameObject);
        }

        // 7. 칸이 다 차면 벨트 위 방석을 세워 둔다
        bool full = atPick != null && atWait != null;
        if (full != holding)
        {
            holding = full;
            Debug.Log($"{Tag} {(full ? "칸 2개 다 참 → 벨트 위 방석 대기" : "칸 빔 → 벨트 위 방석 다시 이동")}");
        }
        if (!full) return;

        var slot = pickSpot.GetComponent<Collider>();
        float slotTop = slot != null ? slot.bounds.max.y : pickSpot.position.y;
        Vector3 center = b.center;
        n = Physics.OverlapSphereNonAlloc(center, holdDistance + 0.5f, probe, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var rb = probe[i].attachedRigidbody;
            if (rb == null || rb.isKinematic || !rb.gameObject.CompareTag(cushionTag)) continue;
            var col = probe[i];
            if (col.bounds.min.y < slotTop + onBeltMinHeight) continue;          // 떨어지는 중이거나 칸 높이
            if (FlatDistance(rb.position, center) > holdDistance) continue;
            var v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(0f, v.y, 0f);
            rb.angularVelocity = Vector3.zero;
        }
    }

    void Arrive(GameObject go)
    {
        if (!enabled || go == null) return;
        if (!go.CompareTag(cushionTag)) return;
        if (go == atPick || go == atWait || queue.Contains(go)) return;   // 이미 맡은 방석

        if (atPick == null)
        {
            atPick = go;
            Seat(go, pickSpot);
            Debug.Log($"{Tag} {go.name} → 1번 칸 (집는 자리)");
            ArrivedAtPickSpot?.Invoke(go);
        }
        else if (atWait == null)
        {
            atWait = go;
            Seat(go, waitSpot);
            Debug.Log($"{Tag} {go.name} → 2번 칸 (대기)");
        }
        else
        {
            Freeze(go);
            go.transform.rotation = Quaternion.Euler(0f, go.transform.eulerAngles.y, 0f);   // 기울어진 채 굳지 않게
            queue.Add(go);
            Debug.LogWarning($"{Tag} 칸 2개가 다 차서 {go.name}이(가) 닿은 자리에서 대기 (줄 {queue.Count}). 레인 용량(FR-04) 확인", this);
        }
        NotifyCount();
    }

    void Update()
    {
        bool changed = queue.RemoveAll(g => g == null || !g.activeInHierarchy) > 0;

        if (!ReferenceEquals(atWait, null) && (atWait == null || !atWait.activeInHierarchy))
        {
            atWait = null;
            changed = true;
        }

        if (!ReferenceEquals(atPick, null))
        {
            if (atPick == null || !atPick.activeInHierarchy)
            {
                atPick = null;                       // 삭제되거나 꺼짐
                changed = true;
            }
            else
            {
                var rb = atPick.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic)   // 그리퍼가 놓았다
                {
                    if (FlatDistance(atPick.transform.position, pickSpot.position) > leaveDistance)
                    {
                        Debug.Log($"{Tag} {atPick.name} 다른 곳에 놓임 → 1번 칸 비움");
                        atPick = null;
                        changed = true;
                    }
                    else
                    {
                        Seat(atPick, pickSpot);       // 팔 자체 중단으로 되돌려 놓음 → 다시 고정
                        Debug.Log($"{Tag} {atPick.name} 1번 칸에 다시 고정");
                    }
                }
            }
        }

        if (Refill()) changed = true;
        if (changed) NotifyCount();
    }

    /// <summary>팔 서버가 놓기 성공 뒤 부른다. 1번 칸 방석이면 칸을 비우고 뒤 방석을 당긴다</summary>
    public bool ConfirmPicked(GameObject cushion)
    {
        if (cushion == null || cushion != atPick) return false;
        atPick = null;
        Refill();
        NotifyCount();
        return true;
    }

    /// <summary>어느 레인인지 몰라도 된다. 이 방석을 1번 칸에 둔 LaneEnd를 찾아 ConfirmPicked</summary>
    public static bool NotifyPlaced(GameObject cushion)
    {
        foreach (var l in all)
            if (l.atPick != null && l.atPick == cushion) return l.ConfirmPicked(cushion);
        return false;
    }

    /// <summary>1번 칸에 있는 방석을 id(오브젝트 이름)로 찾는다. 팔2·3이 요청의 box_id로 쓴다</summary>
    public static bool TryGetAtPick(string cushionId, out GameObject cushion)
    {
        foreach (var l in all)
        {
            if (l.atPick != null && l.atPick.name == cushionId)
            {
                cushion = l.atPick;
                return true;
            }
        }
        cushion = null;
        return false;
    }

    /// <summary>칸과 줄의 방석을 모두 없앤다 (FR-16 해제 초기화)</summary>
    public void ResetAll()
    {
        int n = 0;
        if (atPick != null) { Destroy(atPick); n++; }
        if (atWait != null) { Destroy(atWait); n++; }
        foreach (var g in queue) if (g != null) { Destroy(g); n++; }
        atPick = null;
        atWait = null;
        queue.Clear();
        NotifyCount();
        if (n > 0) Debug.Log($"{Tag} 초기화: 방석 {n}개 삭제");
    }

    // ── 내부 ──

    /// <summary>1번 칸이 비었으면 2번 칸 → 1번, 줄 맨 앞 → 2번으로 당긴다. 움직였으면 true</summary>
    bool Refill()
    {
        bool moved = false;
        while (true)
        {
            if (atPick == null && atWait != null)
            {
                atPick = atWait;
                atWait = null;
                Seat(atPick, pickSpot);
                Debug.Log($"{Tag} {atPick.name} 2번 칸 → 1번 칸 (집는 자리)");
                ArrivedAtPickSpot?.Invoke(atPick);
                moved = true;
                continue;
            }
            if (atWait == null && queue.Count > 0)
            {
                atWait = queue[0];
                queue.RemoveAt(0);
                if (atWait == null) continue;        // 줄에 있던 방석이 이미 삭제됨
                Seat(atWait, waitSpot);
                Debug.Log($"{Tag} {atWait.name} 줄 → 2번 칸");
                moved = true;
                continue;
            }
            return moved;
        }
    }

    /// <summary>방석을 멈추고 칸 판 윗면 위에 바닥이 닿게 놓는다</summary>
    static void Seat(GameObject go, Transform spot)
    {
        Freeze(go);
        var t = go.transform;
        t.rotation = Quaternion.Euler(0f, spot.eulerAngles.y, 0f);
        Physics.SyncTransforms();

        var col = go.GetComponentInChildren<Collider>();
        float half = col != null && col.enabled ? t.position.y - col.bounds.min.y : 0f;
        var spotCol = spot.GetComponent<Collider>();
        float floorY = spotCol != null ? spotCol.bounds.max.y : spot.position.y;

        t.position = new Vector3(spot.position.x, floorY + half + 0.001f, spot.position.z);
        Physics.SyncTransforms();
    }

    static void Freeze(GameObject go)
    {
        var rb = go.GetComponent<Rigidbody>();
        if (rb == null || rb.isKinematic) return;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    void NotifyCount()
    {
        int c = Count;
        if (c == lastCount) return;
        lastCount = c;
        CountChanged?.Invoke(c);
    }

    [ContextMenu("테스트: 1번 칸 방석을 팔이 가져간 것으로 처리")]
    void TestTakeFromPick()
    {
        if (!Application.isPlaying || atPick == null) return;
        var go = atPick;
        ConfirmPicked(go);
        Destroy(go);
    }
}
