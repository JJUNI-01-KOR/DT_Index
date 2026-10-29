using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 적재함 (FR-02, FR-05). 담당 A · scope unity-line
/// - Fill(): 박스 20개 (정상 14 : 규격 초과 6), 놓는 순서는 랜덤, 크기는 범위 안에서 랜덤
/// - box_id는 BOX_001부터 순서대로, 다시 채워도 이어서 증가 (중복 없음)
/// - 칸 1번부터 순서대로 꺼냄 (FR-02 "적재함 칸 순서대로")
/// - 비면 자동으로 다시 채움 (FR-05)
/// - 남은 수가 바뀌면 CountChanged 이벤트 (ROS 발행은 ROS/BoxBinRosLink.cs)
///
/// 칸 배치: 이 오브젝트 위치 = 1번 칸 중심 바닥. 로컬 +X로 열, 로컬 +Z로 행.
/// 실제 좌표는 라인 좌표표(D)에 맞춰 이 오브젝트를 옮기면 된다.
/// </summary>
[DisallowMultipleComponent]
public class BoxBin : MonoBehaviour
{
    [Header("프리팹")]
    [SerializeField] private Box normalPrefab;
    [SerializeField] private Box oversizePrefab;

    [Header("수량 (NFR-09: Inspector에서 변경)")]
    [SerializeField] private int normalCount = 14;
    [SerializeField] private int oversizeCount = 6;

    [Header("칸 배치")]
    [SerializeField] private int columns = 5;               // 로컬 +X 방향 칸 수
    [SerializeField] private float slotPitchM = 0.30f;      // 칸 중심 간격 (가장 큰 박스 폭보다 커야 함)

    [Header("박스 크기 범위 (m) — 규격 높이는 B와 합의")]
    [Tooltip("크기 검사대 규격 높이 (B 담당 값과 같게)")]
    [SerializeField] private float gateHeightM = 0.30f;
    [Tooltip("규격 높이에서 최소 이만큼 떨어진 높이만 만든다 (FR-06: 2 cm)")]
    [SerializeField] private float gateMarginM = 0.02f;
    [SerializeField] private Vector2 widthRangeM = new Vector2(0.18f, 0.25f);
    [SerializeField] private Vector2 depthRangeM = new Vector2(0.18f, 0.25f);
    [SerializeField] private Vector2 normalHeightRangeM = new Vector2(0.15f, 0.26f);
    [SerializeField] private Vector2 oversizeHeightRangeM = new Vector2(0.34f, 0.40f);

    [Header("동작")]
    [SerializeField] private bool fillOnStart = true;
    [SerializeField] private bool autoRefill = true;
    [Tooltip("비고 나서 다시 채우기까지 (초). FR-05: 1초 이내")]
    [SerializeField] private float refillDelaySec = 0.5f;
    [Tooltip("0이면 매번 다른 랜덤. 숫자를 넣으면 매번 같은 순서 (테스트용)")]
    [SerializeField] private int randomSeed = 0;

    private static int nextIdNumber = 1;                    // BOX_001부터, 재충전해도 리셋 안 함
    private readonly List<Box> slots = new List<Box>();     // 칸 순서대로 (빈 칸은 null)
    private int nextSlot;                                   // 다음에 꺼낼 칸 번호 (0부터)
    private System.Random rng;
    private bool refilling;

    public int Count { get; private set; }
    public int Capacity => normalCount + oversizeCount;

    /// <summary>남은 박스 수가 바뀌면 (/bin/count)</summary>
    public event Action<int> CountChanged;

    void Start()
    {
        rng = randomSeed == 0 ? new System.Random() : new System.Random(randomSeed);
        if (fillOnStart) Fill();
        else SetCount(0);
    }

    /// <summary>적재함을 가득 채운다 (정상 14 + 초과 6, 순서 랜덤)</summary>
    [ContextMenu("채우기")]
    public void Fill()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Play 중에만 동작해", this); return; }
        if (normalPrefab == null || oversizePrefab == null) { Debug.LogError("[bin] 박스 프리팹이 비어 있음", this); return; }

        ClearRemaining();

        // 정상/초과를 섞은 목록 (Fisher–Yates)
        var kinds = new List<bool>();
        for (int i = 0; i < normalCount; i++) kinds.Add(false);
        for (int i = 0; i < oversizeCount; i++) kinds.Add(true);
        for (int i = kinds.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
        }

        slots.Clear();
        for (int i = 0; i < kinds.Count; i++)
        {
            bool over = kinds[i];
            Vector3 size = new Vector3(
                Rand(widthRangeM),
                over ? Rand(oversizeHeightRangeM) : Rand(normalHeightRangeM),
                Rand(depthRangeM));

            Vector3 floor = SlotFloorPosition(i);
            var box = Instantiate(over ? oversizePrefab : normalPrefab,
                                  floor + transform.up * (size.y * 0.5f),
                                  transform.rotation, transform);
            box.Init($"BOX_{nextIdNumber++:000}", size, over);
            slots.Add(box);
        }

        nextSlot = 0;
        refilling = false;
        SetCount(slots.Count);
        Debug.Log($"[bin] 채움: {normalCount}개 정상 + {oversizeCount}개 초과, {slots[0].BoxId} ~ {slots[slots.Count - 1].BoxId}", this);
    }

    /// <summary>다음에 꺼낼 박스 (꺼내지는 않음). 없으면 null</summary>
    public Box PeekNext()
    {
        for (int i = nextSlot; i < slots.Count; i++)
            if (slots[i] != null) return slots[i];
        return null;
    }

    /// <summary>
    /// 다음 박스를 꺼낸다 (로봇팔1이 흡착한 직후 호출). 남은 수 감소 → /bin/count.
    /// 박스는 적재함 자식에서 빠지고 위치가 Arm으로 바뀐다.
    /// </summary>
    public Box TakeNext()
    {
        while (nextSlot < slots.Count && slots[nextSlot] == null) nextSlot++;
        if (nextSlot >= slots.Count) return null;

        var box = slots[nextSlot];
        slots[nextSlot] = null;
        nextSlot++;

        box.transform.SetParent(null, true);
        box.Location = BoxLocation.Arm;
        SetCount(Count - 1);

        if (Count == 0 && autoRefill && !refilling)
            StartCoroutine(RefillAfterDelay());

        return box;
    }

    IEnumerator RefillAfterDelay()
    {
        refilling = true;
        yield return new WaitForSeconds(refillDelaySec);
        Fill();
    }

    void ClearRemaining()
    {
        foreach (var b in slots)
            if (b != null) Destroy(b.gameObject);
        slots.Clear();
    }

    void SetCount(int c)
    {
        Count = c;
        CountChanged?.Invoke(c);
    }

    /// <summary>i번 칸(0부터)의 바닥 중심 월드 좌표</summary>
    public Vector3 SlotFloorPosition(int i)
    {
        int col = i % columns;
        int row = i / columns;
        return transform.TransformPoint(new Vector3(col * slotPitchM, 0f, row * slotPitchM));
    }

    float Rand(Vector2 range) => Mathf.Lerp(range.x, range.y, (float)rng.NextDouble());

    void OnValidate()
    {
        // FR-06: 규격 높이와 2 cm 미만 차이 나는 박스를 만들지 않는다
        if (normalHeightRangeM.y > gateHeightM - gateMarginM)
            Debug.LogWarning($"[bin] 정상 박스 최대 높이 {normalHeightRangeM.y} m가 규격 높이-{gateMarginM} m({gateHeightM - gateMarginM})보다 큼 (FR-06)", this);
        if (oversizeHeightRangeM.x < gateHeightM + gateMarginM)
            Debug.LogWarning($"[bin] 초과 박스 최소 높이 {oversizeHeightRangeM.x} m가 규격 높이+{gateMarginM} m({gateHeightM + gateMarginM})보다 작음 (FR-06)", this);
        if (slotPitchM < Mathf.Max(widthRangeM.y, depthRangeM.y))
            Debug.LogWarning("[bin] 칸 간격이 가장 큰 박스보다 작아서 박스끼리 겹침", this);
        columns = Mathf.Max(1, columns);
    }

    void OnDrawGizmos()
    {
        int n = Mathf.Max(1, normalCount + oversizeCount);
        for (int i = 0; i < n; i++)
        {
            Vector3 p = SlotFloorPosition(i);
            Gizmos.color = i == 0 ? Color.green : new Color(1f, 0.8f, 0f);
            Gizmos.matrix = Matrix4x4.TRS(p, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.up * 0.005f, new Vector3(slotPitchM * 0.9f, 0.01f, slotPitchM * 0.9f));
        }
        Gizmos.matrix = Matrix4x4.identity;
    }

    // ── 테스트용 ──
    [ContextMenu("테스트: 하나 꺼내기")]
    void TestTake()
    {
        var b = TakeNext();
        if (b == null) { Debug.Log("[bin] 비어 있음", this); return; }
        Debug.Log($"[bin] 꺼냄 {b.BoxId} ({(b.IsOversize ? "초과" : "정상")}, H={b.HeightM:F2} m), 남은 {Count}", this);
        b.transform.position += Vector3.up * 0.6f; // 눈으로 보이게 살짝 띄움
    }
}
