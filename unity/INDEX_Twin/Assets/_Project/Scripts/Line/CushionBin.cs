using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 적재함 (SRS v3.1 FR-02, FR-05, 부록 A.5). 담당 A · scope unity-line
/// 클래스 이름은 v2.2 그대로 (BinLink가 참조). 내용은 방석 적재함.
///
/// - Fill(): 방석 20개 = 정상 14 : 불량 6, 놓는 순서 랜덤
///   무게: 정상 0.95~1.05 kg, 불량 0.65~0.79 kg 또는 1.25~1.35 kg (소수 둘째 자리)
///   (11/02 A: factory_manager 정상 판정 0.80~1.20 kg과 겹치지 않게 불량 범위를 경계 밖으로 옮김)
/// - id: C001부터 순서대로, 다시 채워도 이어서 (C021, C022 …, 중복 없음)
/// - 배치: 2×2칸 × 5단. 꺼내는 순서 = 위층부터 (5단 4칸 → 4단 4칸 → … → 1단 4칸)
/// - 칸에 있는 방석은 Kinematic (쌓인 채로 흔들리지 않게). 팔이 집으면 KinematicGripper가 가져감
/// - 비면 refillDelaySec 뒤 다시 20개 (FR-05: 1초 이내). /bin/count는 0 → 20
/// - 남은 수가 바뀌면 CountChanged (ROS 발행은 ROS/BinLink.cs)
///
/// 위치: 이 오브젝트 위치 = 적재함 바닥 중심 (2×2 칸 가운데). 로컬 +X 열, 로컬 +Z 행, 위로 단.
///       팔1 반경 1.8 m 안에 둔다 (팔1 베이스 Unity (3.25, 0, -0.5)).
///
/// 11/02 A: 선반에 세워 꽂기 (rackMode, 기본 켜짐)
///   - 방석을 Z축으로 90° 세워 윗면(흡착면)이 faceLocalDir(기본 로컬 -X = 팔1 쪽)을 보게 선반 칸에 꽂는다.
///   - 이 오브젝트 위치 = 선반 앞면(팔 쪽) 가운데, 바닥 높이. 회전 0이면 앞면이 -X를 본다.
///     씬: 선반 bin_slots (-0.05, 0, 3.5) 기준 CushionBin (3.25, 0, 0.35)
///   - 칸: shelfTopY(선반판 윗면 높이, 꺼내는 순서대로) × perShelf(가로로 나란히) × depthRows(앞뒤 줄, 앞줄부터)
///     기본 3칸 × 4개 × 2줄 = 24칸. 앞줄을 전부 꺼낸 뒤 뒷줄을 꺼낸다 (앞 방석에 가려지지 않게).
///   - 팔1(ArmPickPlace)은 방석이 서 있으면 앞에서 수평으로 집고, 빼낸 뒤 손목을 돌려 눕혀서 놓는다.
///   - rackMode를 끄면 예전 2×2×5 쌓기 그대로.
/// </summary>
[DisallowMultipleComponent]
public class CushionBin : MonoBehaviour
{
    [Header("프리팹 (태그 Cushion, Cushion 스크립트)")]
    [SerializeField] private Cushion cushionPrefab;

    [Header("수량 · 비율 (NFR-09: Inspector에서 변경)")]
    [SerializeField] private int normalCount = 14;
    [SerializeField] private int defectCount = 6;

    [Header("무게 범위 (kg, 부록 A.5)")]
    [SerializeField] private Vector2 normalKg = new Vector2(0.95f, 1.05f);
    [SerializeField] private Vector2 defectLightKg = new Vector2(0.65f, 0.79f);
    [SerializeField] private Vector2 defectHeavyKg = new Vector2(1.25f, 1.35f);

    [Header("방석 크기 (m) — 가로, 높이, 세로")]
    [SerializeField] private Vector3 cushionSize = new Vector3(0.4f, 0.1f, 0.4f);

    [Header("칸 배치")]
    [SerializeField] private int columns = 2;          // 로컬 +X
    [SerializeField] private int rows = 2;             // 로컬 +Z
    [SerializeField] private int layers = 5;           // 위로
    [Tooltip("칸 중심 간격 (m). 방석 가로보다 커야 함")]
    [SerializeField] private float slotPitch = 0.42f;
    [Tooltip("단 사이 틈 (m). 0이면 바로 위에 쌓음")]
    [SerializeField] private float layerGap = 0.002f;

    [Header("선반에 세워 꽂기 (11/02 A)")]
    [Tooltip("켜면 선반 칸에 방석을 세워 꽂는다. 끄면 예전 2×2×5 쌓기")]
    [SerializeField] private bool rackMode = true;
    [Tooltip("흡착면(방석 윗면)이 볼 방향 (이 오브젝트 로컬). 팔1 쪽")]
    [SerializeField] private Vector3 faceLocalDir = Vector3.left;
    [Tooltip("선반판 윗면 높이 (m, 이 오브젝트 기준). 꺼내는 순서대로 적는다")]
    [SerializeField] private float[] shelfTopY = { 1.52f, 0.86f, 0.19f };
    [Tooltip("선반 한 칸에 가로로 나란히 꽂는 개수")]
    [SerializeField] private int perShelf = 4;
    [Tooltip("앞뒤 줄 수. 앞줄(팔 쪽)부터 꺼낸다")]
    [SerializeField] private int depthRows = 2;
    [Tooltip("가로 간격 (m, 방석 중심 사이). 방석 폭 0.4보다 커야 함")]
    [SerializeField] private float colPitch = 0.47f;
    [Tooltip("앞뒤 줄 간격 (m). 방석 두께 0.1보다 커야 함")]
    [SerializeField] private float rowPitch = 0.12f;
    [Tooltip("이 오브젝트(선반 앞면)에서 앞줄 방석 흡착면까지 안쪽 거리 (m)")]
    [SerializeField] private float frontInset = 0.05f;
    [Tooltip("선반판과 방석 바닥 사이 틈 (m)")]
    [SerializeField] private float shelfGap = 0.002f;

    [Header("로봇팔1 연결 (선택)")]
    [Tooltip("팔1. 연결하면 이 팔이 적재함 방석을 집을 때 남은 수를 줄인다")]
    [SerializeField] private ArmPickPlace feedArm;

    [Header("동작")]
    [SerializeField] private bool fillOnStart = true;
    [SerializeField] private bool autoRefill = true;
    [Tooltip("비고 나서 다시 채우기까지 (초). FR-05: 1초 이내")]
    [SerializeField] private float refillDelaySec = 0.5f;
    [Tooltip("0이면 매번 다른 랜덤. 숫자를 넣으면 매번 같은 순서 (시험용)")]
    [SerializeField] private int randomSeed = 0;

    private static int nextIdNumber = 1;   // C001부터, 다시 채워도 리셋 안 함

    // Enter Play Mode Options로 Domain Reload를 꺼서 static 값이 Play 사이에 남는다 → Play마다 C001부터
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => nextIdNumber = 1;

    private readonly List<Cushion> slots = new List<Cushion>();            // 꺼내는 순서대로 (빈 칸은 null)
    private readonly Dictionary<Cushion, int> slotOf = new Dictionary<Cushion, int>();
    private System.Random rng;
    private bool refilling;

    public int Count { get; private set; }
    public int Capacity => normalCount + defectCount;
    public int SlotCount => rackMode
        ? (shelfTopY != null ? shelfTopY.Length : 0) * perShelf * depthRows
        : columns * rows * layers;

    /// <summary>남은 방석 수가 바뀌면 (/bin/count)</summary>
    public event Action<int> CountChanged;

    void Start()
    {
        rng = randomSeed == 0 ? new System.Random() : new System.Random(randomSeed);
        if (feedArm != null) feedArm.Picked += OnArmPicked;
        if (fillOnStart) Fill();
        else SetCount(0);
    }

    void OnDestroy()
    {
        if (feedArm != null) feedArm.Picked -= OnArmPicked;
    }

    void Update()
    {
        // 칸에 있던 방석이 밖에서 지워졌으면(시험 등) 꺼낸 것으로 친다
        bool changed = false;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] is null) continue;          // 원래 빈 칸
            if (slots[i] == null)                    // Unity에서 Destroy된 방석
            {
                slots[i] = null;
                changed = true;
            }
        }
        if (changed) Recount();
    }

    // ───────────────────────── 채우기 ─────────────────────────

    /// <summary>적재함을 가득 채운다 (정상 14 + 불량 6, 순서 랜덤)</summary>
    [ContextMenu("채우기")]
    public void Fill()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Play 중에만 동작해", this); return; }
        if (cushionPrefab == null) { Debug.LogError("[적재함] 방석 프리팹이 비어 있음", this); return; }
        if (Capacity > SlotCount) { Debug.LogError($"[적재함] 방석 {Capacity}개가 칸 {SlotCount}개보다 많음", this); return; }

        ClearRemaining();

        // 정상/불량을 섞은 목록 (Fisher–Yates)
        var kinds = new List<bool>();
        for (int i = 0; i < normalCount; i++) kinds.Add(false);
        for (int i = 0; i < defectCount; i++) kinds.Add(true);
        for (int i = kinds.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
        }

        // 아래층부터 만들어야 위층이 바로 올라가므로, 꺼내는 순서의 반대로 생성
        slots.Clear();
        for (int i = 0; i < kinds.Count; i++) slots.Add(null);
        int firstId = nextIdNumber;
        var ids = new string[kinds.Count];
        for (int i = 0; i < kinds.Count; i++) ids[i] = $"C{nextIdNumber++:000}";   // 꺼내는 순서 = id 순서

        int defects = 0;
        for (int i = kinds.Count - 1; i >= 0; i--)
        {
            bool defect = kinds[i];
            float kg = defect ? (rng.Next(2) == 0 ? RandKg(defectLightKg) : RandKg(defectHeavyKg)) : RandKg(normalKg);
            if (defect) defects++;

            var box = Instantiate(cushionPrefab, SlotCenter(i), SlotRotation(i), transform);
            box.Init(ids[i], cushionSize, kg, defect);
            box.Body.isKinematic = true;
            slots[i] = box;
            slotOf[box] = i;
        }

        refilling = false;
        Recount();
        Debug.Log($"[적재함] 채움: 정상 {kinds.Count - defects} + 불량 {defects}, {ids[0]} ~ {ids[ids.Length - 1]}", this);
    }

    float RandKg(Vector2 range) =>
        Mathf.Round(Mathf.Lerp(range.x, range.y, (float)rng.NextDouble()) * 100f) / 100f;

    void ClearRemaining()
    {
        foreach (var b in slots)
            if (b != null) Destroy(b.gameObject);
        slots.Clear();
        slotOf.Clear();
    }

    // ───────────────────────── 꺼내기 ─────────────────────────

    /// <summary>다음에 꺼낼 칸 번호 (0부터). 없으면 -1</summary>
    public int NextSlotIndex
    {
        get
        {
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] != null) return i;
            return -1;
        }
    }

    /// <summary>다음에 꺼낼 방석 (꺼내지는 않음). 없으면 null</summary>
    public Cushion PeekNext()
    {
        int i = NextSlotIndex;
        return i >= 0 ? slots[i] : null;
    }

    /// <summary>다음 방석을 꺼낸다 (시험용. 팔1은 Picked 이벤트로 자동 처리)</summary>
    public Cushion TakeNext()
    {
        var box = PeekNext();
        if (box != null) Take(box);
        return box;
    }

    /// <summary>이 적재함의 방석이면 칸에서 뺀다. 남은 수 감소 → /bin/count</summary>
    public bool Take(Cushion box)
    {
        if (box == null || !slotOf.TryGetValue(box, out int i) || slots[i] != box) return false;
        slots[i] = null;
        box.transform.SetParent(null, true);
        box.Location = CushionLocation.Arm;
        Recount();
        return true;
    }

    /// <summary>
    /// 집기 실패로 방석이 칸에 되돌아왔을 때 (FR-02: 실패하면 칸 순서를 넘기지 않음).
    /// 같은 칸에 다시 넣고 남은 수를 되돌린다.
    /// </summary>
    public bool Return(Cushion box)
    {
        if (box == null || !slotOf.TryGetValue(box, out int i) || slots[i] != null) return false;
        slots[i] = box;
        box.transform.SetParent(transform, true);
        box.transform.SetPositionAndRotation(SlotCenter(i), SlotRotation(i));
        box.Body.isKinematic = true;
        box.Location = CushionLocation.Bin;
        Recount();
        return true;
    }

    /// <summary>방석이 이 적재함 소속인지 (칸에 있든 꺼냈든)</summary>
    public bool Owns(Cushion box) => box != null && slotOf.ContainsKey(box);

    void OnArmPicked(GameObject go)
    {
        var box = go != null ? go.GetComponent<Cushion>() : null;
        if (box != null && Take(box))
            Debug.Log($"[적재함] 팔1이 {box.CushionId} 집음 (무게 {box.WeightKg:F2} kg), 남은 {Count}", this);
    }

    void Recount()
    {
        int c = 0;
        foreach (var b in slots) if (b != null) c++;
        SetCount(c);

        if (c == 0 && autoRefill && !refilling && Application.isPlaying)
            StartCoroutine(RefillAfterDelay());
    }

    IEnumerator RefillAfterDelay()
    {
        refilling = true;
        yield return new WaitForSeconds(refillDelaySec);
        Fill();
    }

    void SetCount(int c)
    {
        if (c == Count && slots.Count > 0) return;
        Count = c;
        CountChanged?.Invoke(c);
    }

    // ───────────────────────── 좌표 ─────────────────────────

    /// <summary>
    /// i번째로 꺼낼 칸 (0부터)의 단·열·행.
    /// 쌓기: 위층부터, 한 층 안에서는 열 → 행. 선반: layer = 선반 칸(shelfTopY 순서), col = 가로, row = 앞뒤 줄(0 = 앞)
    /// </summary>
    public void SlotCell(int i, out int layer, out int col, out int row)
    {
        if (rackMode)
        {
            int shelves = Mathf.Max(1, shelfTopY != null ? shelfTopY.Length : 1);
            int perRow = shelves * perShelf;             // 한 줄(앞뒤)에 들어가는 수
            row = i / perRow;                            // 앞줄을 전부 꺼낸 뒤 뒷줄
            int k = i % perRow;
            layer = k / perShelf;                        // shelfTopY 적은 순서
            col = k % perShelf;
            return;
        }
        int perLayer = columns * rows;
        layer = layers - 1 - i / perLayer;
        int kk = i % perLayer;
        col = kk % columns;
        row = kk / columns;
    }

    Vector3 FaceDirLocal => faceLocalDir.sqrMagnitude < 1e-6f ? Vector3.left : new Vector3(faceLocalDir.x, 0f, faceLocalDir.z).normalized;

    /// <summary>i번째 칸 방석의 회전 (월드). 선반이면 윗면이 흡착면 방향을 보게 세운다</summary>
    public Quaternion SlotRotation(int i)
    {
        if (!rackMode) return transform.rotation;
        Vector3 face = transform.TransformDirection(FaceDirLocal);
        return Quaternion.FromToRotation(transform.up, face) * transform.rotation;
    }

    /// <summary>i번째로 꺼낼 칸의 방석 중심 (월드)</summary>
    public Vector3 SlotCenter(int i)
    {
        SlotCell(i, out int layer, out int col, out int row);
        if (rackMode)
        {
            Vector3 face = FaceDirLocal;                              // 팔 쪽
            Vector3 depth = -face;                                     // 선반 안쪽
            Vector3 side = Vector3.Cross(Vector3.up, face).normalized; // 가로
            float thick = cushionSize.y, tall = cushionSize.x;         // 세우면 두께 = 원래 높이, 높이 = 원래 가로
            float shelfY = (shelfTopY != null && layer < shelfTopY.Length) ? shelfTopY[layer] : 0f;
            Vector3 local = depth * (frontInset + thick * 0.5f + row * rowPitch)
                          + Vector3.up * (shelfY + shelfGap + tall * 0.5f)
                          + side * ((col - (perShelf - 1) * 0.5f) * colPitch);
            return transform.TransformPoint(local);
        }
        float x = (col - (columns - 1) * 0.5f) * slotPitch;
        float z = (row - (rows - 1) * 0.5f) * slotPitch;
        float y = layer * (cushionSize.y + layerGap) + cushionSize.y * 0.5f;
        return transform.TransformPoint(new Vector3(x, y, z));
    }

    /// <summary>i번째로 꺼낼 칸의 방석 바닥 중심 (월드). 좌표표 bin_slots에 쓴다</summary>
    public Vector3 SlotBottom(int i)
    {
        float half = rackMode ? cushionSize.x * 0.5f : cushionSize.y * 0.5f;
        return SlotCenter(i) - transform.up * half;
    }

    /// <summary>좌표표(line_coords.yaml) bin_slots 20줄을 Console에 출력. ROS: x = Unity z, y = -Unity x, z = Unity y</summary>
    [ContextMenu("좌표표 bin_slots 출력 (ROS 좌표)")]
    void PrintBinSlots()
    {
        var sb = new StringBuilder($"bin_slots:                    # 적재함 칸 {Mathf.Min(Capacity, SlotCount)}개, 꺼내는 순서. 방석 바닥 중심{(rackMode ? " (선반에 세움, 흡착면은 팔1 쪽)" : "")}. CushionBin '좌표표 출력'으로 만듦\n");
        int n = Mathf.Min(Capacity, SlotCount);
        for (int i = 0; i < n; i++)
        {
            Vector3 u = SlotBottom(i);
            SlotCell(i, out int layer, out int col, out int row);
            string where = rackMode ? $"선반 {layer + 1}번째 칸, 가로 {col + 1}, {(row == 0 ? "앞" : (row + 1) + "번째")}줄 (세움)" : $"{layer + 1}단 ({col},{row})";
            sb.AppendLine($"  - {{x: {u.z:F3}, y: {-u.x:F3}, z: {u.y:F3}, yaw: 0.0}}   # {i + 1}번: {where}. Unity ({u.x:F3}, {u.y:F3}, {u.z:F3})");
        }
        Debug.Log(sb.ToString(), this);
    }

    void OnValidate()
    {
        columns = Mathf.Max(1, columns);
        rows = Mathf.Max(1, rows);
        layers = Mathf.Max(1, layers);
        perShelf = Mathf.Max(1, perShelf);
        depthRows = Mathf.Max(1, depthRows);
        if (rackMode)
        {
            if (colPitch < cushionSize.z) Debug.LogWarning("[적재함] 가로 간격이 방석 폭보다 작아서 방석끼리 겹침", this);
            if (rowPitch < cushionSize.y) Debug.LogWarning("[적재함] 앞뒤 줄 간격이 방석 두께보다 작아서 방석끼리 겹침", this);
        }
        else if (slotPitch < Mathf.Max(cushionSize.x, cushionSize.z))
            Debug.LogWarning("[적재함] 칸 간격이 방석보다 작아서 방석끼리 겹침", this);
        if (normalCount + defectCount > SlotCount)
            Debug.LogWarning($"[적재함] 방석 {normalCount + defectCount}개가 칸 {SlotCount}개보다 많음", this);
    }

    void OnDrawGizmos()
    {
        int n = Mathf.Min(normalCount + defectCount, Mathf.Max(1, SlotCount));
        for (int i = 0; i < n; i++)
        {
            Gizmos.color = i == 0 ? Color.green : new Color(1f, 0.8f, 0f, 0.6f);
            Gizmos.matrix = Matrix4x4.TRS(SlotCenter(i), SlotRotation(i), Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, cushionSize * 0.98f);
        }
        Gizmos.matrix = Matrix4x4.identity;
    }

    // ── 시험 (Play 중 컴포넌트 우클릭) ──
    [ContextMenu("시험: 하나 꺼내기")]
    void TestTake()
    {
        var b = TakeNext();
        if (b == null) { Debug.Log("[적재함] 비어 있음", this); return; }
        Debug.Log($"[적재함] 꺼냄 {b.CushionId} ({(b.IsDefect ? "불량" : "정상")}, {b.WeightKg:F2} kg), 남은 {Count}", this);
        Destroy(b.gameObject);
    }

    [ContextMenu("시험: 전부 꺼내기 (보충 확인)")]
    void TestTakeAll()
    {
        while (true)
        {
            var b = TakeNext();
            if (b == null) break;
            Destroy(b.gameObject);
        }
        Debug.Log($"[적재함] 전부 꺼냄. {refillDelaySec}초 뒤 다시 채움", this);
    }

    [ContextMenu("시험: 목록 출력")]
    void TestList()
    {
        var sb = new StringBuilder($"[적재함] 남은 {Count}개\n");
        int normal = 0, defect = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            var b = slots[i];
            if (b == null) continue;
            if (b.IsDefect) defect++; else normal++;
            SlotCell(i, out int layer, out _, out _);
            sb.AppendLine($"  {i + 1,2}번 {(rackMode ? "선반" : "")}{layer + 1}{(rackMode ? "칸" : "단")}  {b.CushionId}  {b.WeightKg:F2} kg  {(b.IsDefect ? "불량" : "정상")}");
        }
        sb.Insert(0, $"정상 {normal} / 불량 {defect}  ");
        Debug.Log(sb.ToString(), this);
    }
}