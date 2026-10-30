using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 컨베이어 구간 (FR-03, FR-04). 담당 A · scope unity-line
///
/// 이 오브젝트 아래에 넣은 "칸"들(에셋 컨베이어 칸 등)의 바닥 콜라이더를 하나씩 영역으로 쓴다.
/// → 칸이 ㄱ자·ㄷ자로 꺾여 있어도 칸 모양대로 영역이 잡힌다 (큰 사각형 하나로 뭉개지지 않음).
///
/// 칸마다 미는 방향:
///   1) 칸에 ConveyorCellDir이 붙어 있으면 그 방향
///   2) 코너 칸(이름에 "Corner" 또는 에셋 CornerConveyor 스크립트)은 에셋의 Input → Center → Output 경로를 따라 꺾음
///   3) 나머지는 이 구간의 Local Direction
///
/// - SetRunning(true/false)로 가동·정지. 정지하면 흐름 방향 속도를 바로 0 → 0.5초 이내
/// - 흐름 방향 성분만 바꾸고 옆(푸셔)·위아래(중력)는 그대로 → 스토퍼에 물리적으로 막힘
/// - 벨트 위 박스 수가 바뀌면 BoxCountChanged (ROS 발행은 ROS/ConveyorLink.cs)
///
/// 칸이 하나도 없으면(직접 만든 큐브 벨트 등) 이 오브젝트의 BoxCollider를 영역으로 쓴다.
/// </summary>
[DisallowMultipleComponent]
public class Conveyor : MonoBehaviour
{
    [Header("동작")]
    [Tooltip("벨트 속도 (m/s). SRS 기본 0.2")]
    [SerializeField] private float speedMps = 0.2f;

    [Tooltip("기본 흐름 방향 (이 오브젝트 로컬 축). 코너 칸과 ConveyorCellDir 칸은 제외")]
    [SerializeField] private Vector3 localDirection = Vector3.forward;

    [Tooltip("벨트 위 박스가 굴러가지 않게 회전을 막는다")]
    [SerializeField] private bool stopTumbling = true;

    [Tooltip("Play 시작할 때 가동 상태 (테스트용. 실제는 ROS 명령으로)")]
    [SerializeField] private bool runOnStart = false;

    [Header("코너")]
    [Tooltip("코너 칸은 에셋의 Input→Center→Output 경로대로 꺾는다. 반대로 꺾이면 끄고 ConveyorCellDir로 직접 지정")]
    [SerializeField] private bool cornerFollowsAssetPath = true;

    [Tooltip("코너 경로를 거꾸로 (Output→Center→Input). 흐름이 에셋 방향과 반대일 때")]
    [SerializeField] private bool reverseCornerPath = false;

    [Header("박스 세기")]
    [Tooltip("칸 윗면에서 이 높이(m)까지 있는 Rigidbody를 벨트 위 박스로 본다")]
    [SerializeField] private float countHeight = 0.6f;

    [Tooltip("대상 레이어 (박스 레이어만 체크 권장)")]
    [SerializeField] private LayerMask boxLayers = ~0;

    [Tooltip("Box 태그가 붙은 것만 대상으로. 끄면 Rigidbody 전부")]
    [SerializeField] private bool onlyBoxTag = true;
    [Tooltip("옮길 물건의 태그 (예: Box, Cushion)")]
    [SerializeField] private string itemTag = "Box";

    [Header("벨트 텍스처 스크롤 (선택, 보여주기용)")]
    [SerializeField] private Renderer beltRenderer;
    [SerializeField] private float textureScrollPerMeter = 1f;

    // ── 칸 정보 ──
    private class Cell
    {
        public Collider floor;        // 칸 바닥 콜라이더
        public Transform input, center, output; // 코너용 경로 (없으면 null)
        public ConveyorCellDir overrideDir;
        public bool isCorner;
        public bool merged;           // 콜라이더 여러 개를 합친 칸 (코너 등)
        public Collider[] mergedCols;
    }

    private readonly List<Cell> cells = new List<Cell>();
    private readonly Dictionary<Rigidbody, Cell> onBelt = new Dictionary<Rigidbody, Cell>();
    private readonly Dictionary<Rigidbody, float> bestDist = new Dictionary<Rigidbody, float>();
    private readonly Collider[] hits = new Collider[64];
    private float texOffset;
    private Material beltMat;

    public bool IsRunning { get; private set; }
    public int BoxCount { get; private set; } = -1;
    public float SpeedMps => speedMps;

    public event Action<bool> RunningChanged;
    public event Action<int> BoxCountChanged;

    Vector3 DefaultDir => Flat(transform.TransformDirection(localDirection));

    void Awake()
    {
        RebuildCells();
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
        Debug.Log($"[conveyor:{name}] {(run ? "가동" : "정지")}", this);
        RunningChanged?.Invoke(run);
    }

    // ───────────────────────── 칸 찾기 ─────────────────────────

    /// <summary>자식 칸들을 다시 찾는다. 칸을 옮기거나 추가한 뒤 부른다</summary>
    [ContextMenu("자식 칸에 맞춰 벨트 영역 자동 설정")]
    public void FitToChildren()
    {
        RebuildCells();
        int corners = 0;
        foreach (var c in cells) if (c.isCorner) corners++;
        Debug.Log($"[conveyor:{name}] 칸 {cells.Count}개 (코너 {corners}개)", this);
    }

    void RebuildCells()
    {
        cells.Clear();

        // 1) 콜라이더를 "구간의 바로 아래 자식(= 칸 1개)" 단위로 묶는다
        var groups = new Dictionary<Transform, List<Collider>>();
        var order = new List<Transform>();
        foreach (var col in GetComponentsInChildren<Collider>(true))
        {
            if (col.isTrigger || !col.enabled || !col.gameObject.activeInHierarchy) continue;
            if (col.GetComponentInParent<Conveyor>() != this) continue;           // 다른 구간 소속
            var rb = col.attachedRigidbody;
            if (rb != null && !rb.isKinematic) continue;                          // 박스 같은 물체 제외
            if (col.transform == transform) continue;                             // 구간 자신의 콜라이더는 칸이 아님

            var root = CellRootOf(col.transform);
            if (root == null) continue;
            if (!groups.TryGetValue(root, out var list)) { groups[root] = list = new List<Collider>(); order.Add(root); }
            list.Add(col);
        }

        // 2) 칸마다 영역 1개. 칸 루트에 콜라이더가 있으면 그것만(레일 제외), 없으면 자식 콜라이더를 합친다 (코너처럼 큐브 2개로 된 칸)
        foreach (var root in order)
        {
            var list = groups[root];
            int own = FirstSolidIndex(root);
            var cell = new Cell
            {
                overrideDir = FindOverride(root),
                input = FindChild(root, "Input"),
                center = FindChild(root, "Center"),
                output = FindChild(root, "Output"),
            };
            if (own >= 0) cell.floor = root.GetComponents<Collider>()[own];
            else if (list.Count == 1) cell.floor = list[0];
            else
            {
                cell.merged = true;
                cell.mergedCols = list.ToArray();
                cell.floor = list[0];
            }
            cell.isCorner = cell.input != null && cell.center != null && cell.output != null &&
                            (root.name.IndexOf("Corner", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             root.GetComponent("CornerConveyor") != null ||
                             root.GetComponent("CornerConveyorLeft") != null);
            cells.Add(cell);
        }

        // 칸이 없으면 자기 BoxCollider를 영역으로 (직접 만든 벨트용)
        if (cells.Count == 0)
        {
            var self = GetComponent<BoxCollider>();
            if (self != null) cells.Add(new Cell { floor = self });
        }
    }

    static ConveyorCellDir FindOverride(Transform root)
    {
        var d = root.GetComponentInChildren<ConveyorCellDir>(true);
        return d != null ? d : root.GetComponentInParent<ConveyorCellDir>();
    }

    /// <summary>t의 조상 중 이 구간의 바로 아래 자식 (= 칸 루트)</summary>
    Transform CellRootOf(Transform t)
    {
        while (t != null && t.parent != transform) t = t.parent;
        return t;
    }

    /// <summary>t와 이 구간 사이의 부모 중에 바닥 콜라이더(Trigger 아님)를 가진 칸이 있는지</summary>
    bool HasCellColliderAbove(Transform t)
    {
        for (var p = t.parent; p != null && p != transform; p = p.parent)
            if (FirstSolidIndex(p) >= 0) return true;
        return false;
    }

    static int FirstSolidIndex(Transform t)
    {
        var cols = t.GetComponents<Collider>();
        for (int i = 0; i < cols.Length; i++)
            if (cols[i].enabled && !cols[i].isTrigger) return i;
        return -1;
    }

    static Transform FindChild(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && t.name == name) return t;
        return null;
    }

    // ───────────────────────── 이동 ─────────────────────────

    void FixedUpdate()
    {
        FindBoxesOnBelt();

        foreach (var kv in onBelt)
        {
            var body = kv.Key;
            Vector3 dir = DirectionFor(kv.Value, body.position);
            float v = IsRunning ? speedMps : 0f;

            Vector3 cur = body.linearVelocity;
            // 흐름 방향 성분만 바꾼다. 옆(푸셔)·위아래(중력)는 그대로
            body.linearVelocity = cur - dir * Vector3.Dot(cur, dir) + dir * v;
            if (stopTumbling) body.angularVelocity = Vector3.zero;
            if (IsRunning) body.WakeUp();
        }

        if (onBelt.Count != BoxCount)
        {
            BoxCount = onBelt.Count;
            BoxCountChanged?.Invoke(BoxCount);
        }
    }

    Vector3 DirectionFor(Cell cell, Vector3 p)
    {
        if (cell.overrideDir != null)
            return Flat(cell.overrideDir.transform.TransformDirection(cell.overrideDir.LocalDirection));

        if (cell.isCorner && cornerFollowsAssetPath)
        {
            Vector3 a = cell.input.position, c = cell.center.position, b = cell.output.position;
            if (reverseCornerPath) (a, b) = (b, a);
            Vector3 inLeg = Flat(c - a), outLeg = Flat(b - c);
            // 중심점을 지나기 전이면 들어오는 방향, 지나면 나가는 방향
            Vector3 ap = p - a; ap.y = 0f;
            Vector3 ac = c - a; ac.y = 0f;
            float along = Vector3.Dot(ap, inLeg);   // 입구에서 진행한 거리 (m)
            return along < ac.magnitude ? inLeg : outLeg;
        }

        return DefaultDir;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.zero; }

    void FindBoxesOnBelt()
    {
        onBelt.Clear();
        bestDist.Clear();

        foreach (var cell in cells)
        {
            if (cell.floor == null) continue;
            GetZone(cell, out Vector3 center, out Vector3 half, out Quaternion rot);
            int n = Physics.OverlapBoxNonAlloc(center, half, hits, rot, boxLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < n; i++)
            {
                var body = hits[i].attachedRigidbody;
                if (body == null || body.isKinematic) continue;                   // 팔이 들고 있는 박스 제외
                if (onlyBoxTag && !hits[i].CompareTag(itemTag) && !body.CompareTag(itemTag)) continue;

                // 칸 경계에 걸친 박스는 중심이 더 가까운 칸 하나에만 속하게
                float d = new Vector3(body.position.x - center.x, 0f, body.position.z - center.z).sqrMagnitude;
                if (!bestDist.TryGetValue(body, out float old) || d < old)
                {
                    bestDist[body] = d;
                    onBelt[body] = cell;
                }
            }
        }
    }

    /// <summary>칸 바닥 콜라이더 윗면 위쪽 공간 (월드)</summary>
    void GetZone(Cell cell, out Vector3 center, out Vector3 half, out Quaternion rot)
    {
        if (cell.merged)
        {
            // 합친 칸: 전체를 덮는 사각 영역. 바닥 높이는 가장 낮은 윗면 기준
            Bounds all = default; bool first = true; float top = float.MaxValue;
            foreach (var c in cell.mergedCols)
            {
                if (c == null) continue;
                if (first) { all = c.bounds; first = false; } else all.Encapsulate(c.bounds);
                top = Mathf.Min(top, c.bounds.max.y);
            }
            rot = Quaternion.identity;
            center = new Vector3(all.center.x, top + countHeight * 0.5f, all.center.z);
            half = new Vector3(all.extents.x, countHeight * 0.5f, all.extents.z);
            return;
        }

        var col = cell.floor;
        if (col is BoxCollider bc)
        {
            var t = bc.transform;
            Vector3 size = Vector3.Scale(bc.size, t.lossyScale);
            size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            rot = t.rotation;
            Vector3 up = t.up;
            center = t.TransformPoint(bc.center) + up * (size.y * 0.5f + countHeight * 0.5f);
            half = new Vector3(size.x * 0.5f, countHeight * 0.5f, size.z * 0.5f);
        }
        else
        {
            Bounds b = col.bounds;
            rot = Quaternion.identity;
            center = new Vector3(b.center.x, b.max.y + countHeight * 0.5f, b.center.z);
            half = new Vector3(b.extents.x, countHeight * 0.5f, b.extents.z);
        }
    }

    void Update()
    {
        if (beltMat == null || !IsRunning) return;
        texOffset += speedMps * textureScrollPerMeter * Time.deltaTime;
        beltMat.mainTextureOffset = new Vector2(0f, texOffset);
    }

    // ───────────────────────── 표시 ─────────────────────────

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) RebuildCells();   // 에디터에서도 칸을 옮기면 바로 반영

        foreach (var cell in cells)
        {
            if (cell.floor == null) continue;
            GetZone(cell, out Vector3 center, out Vector3 half, out Quaternion rot);

            Gizmos.color = cell.isCorner ? new Color(1f, 0.6f, 0f, 0.25f) : new Color(0f, 0.8f, 1f, 0.25f);
            Gizmos.matrix = Matrix4x4.TRS(center, rot, Vector3.one);
            Gizmos.DrawCube(Vector3.zero, half * 2f);
            Gizmos.matrix = Matrix4x4.identity;

            // 칸마다 흐름 화살표
            Vector3 basePos = center - Vector3.up * (half.y - 0.05f);
            if (cell.isCorner && cornerFollowsAssetPath)
            {
                Vector3 a = cell.input.position, c = cell.center.position, b = cell.output.position;
                if (reverseCornerPath) (a, b) = (b, a);
                a.y = c.y = b.y = basePos.y;
                Gizmos.color = new Color(1f, 0.5f, 0f);
                Gizmos.DrawLine(a, c);
                Gizmos.DrawLine(c, b);
                Gizmos.DrawSphere(b, 0.04f);
            }
            else
            {
                Vector3 d = DirectionFor(cell, basePos);
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(basePos, basePos + d * 0.4f);
                Gizmos.DrawSphere(basePos + d * 0.4f, 0.03f);
            }
        }
    }

    // ── 테스트용 ──
    [ContextMenu("테스트: 가동")] void TestRun() => SetRunning(true);
    [ContextMenu("테스트: 정지")] void TestStop() => SetRunning(false);
}