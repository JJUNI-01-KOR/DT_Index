using System;
using UnityEngine;

/// <summary>
/// 무게 센서 (Scanner Arch 아래, Quality Check 칸에 둔다). SRS FR-06
/// Trigger에 들어온 방석의 무게(Rigidbody.mass, kg)를 방석당 1회 재서 Scanned 이벤트로 알린다.
/// 정상·불량 판정은 하지 않는다 → factory_manager(FR-07)가 하고 /diverter_1_cmd로 보낸다.
///   WeightScanner → ScaleLink(/scale_1_weight) → factory_manager → DiverterLink(/diverter_1_cmd) → Diverter
///
/// 준비: 빈 오브젝트 + BoxCollider(Is Trigger) + 이 스크립트. Quality Check 칸 위를 덮게.
///
/// 11/02 A: 판정 코드(minWeightKg) 삭제. ROS 없이 Unity만 시험할 때는 "시험용 로컬 판정"을 켠다 (기본 꺼짐).
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class WeightScanner : MonoBehaviour
{
    [Tooltip("검사 대상 태그 (비우면 아무 Rigidbody나)")]
    [SerializeField] private string targetTag = "Cushion";

    [Tooltip("측정 표시등 (선택). 잴 때 노랑, 판정이 오면 정상 초록 · 불량 빨강")]
    [SerializeField] private Renderer lamp;

    [Header("시험용 로컬 판정 (ROS 없이 Unity만 돌릴 때만 켠다. 시연·통합에서는 꺼둔다)")]
    [SerializeField] private bool localJudgeForTest = false;
    [SerializeField] private float testMinKg = 0.8f;
    [SerializeField] private float testMaxKg = 1.2f;

    /// <summary>잴 때마다 (결과표, 무게kg). ScaleLink가 구독해서 ROS로 보낸다</summary>
    public event Action<ItemSort, float> Scanned;

    public int ScanCount { get; private set; }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    void OnTriggerEnter(Collider other)
    {
        var rb = other.attachedRigidbody;
        if (rb == null || rb.isKinematic) return;   // 팔이 들고 지나가는 건 무시
        if (!string.IsNullOrEmpty(targetTag) && !rb.CompareTag(targetTag)) return;

        var sort = ItemSort.Of(rb);
        if (sort.Weighed) return;                     // 방석당 1회 (FR-06 예외 처리)

        float kg = (float)Math.Round(rb.mass, 2);     // 소수 둘째 자리
        sort.Weighed = true;
        sort.WeightKg = kg;
        ScanCount++;

        if (localJudgeForTest && !sort.Inspected)
        {
            sort.Inspected = true;
            sort.Reject = kg < testMinKg || kg > testMaxKg;
            sort.Reason = $"[시험용 로컬 판정] {kg:F2}kg (정상 {testMinKg:F2}~{testMaxKg:F2})";
            Debug.LogWarning($"[weight_scanner] {rb.name}: {sort.Reason} → {(sort.Reject ? "ng" : "ok")}", this);
            SetLamp(sort.Reject ? Color.red : Color.green);
        }
        else
        {
            Debug.Log($"[weight_scanner] {rb.name}: {kg:F2}kg 측정 → 판정 대기 (factory_manager)", this);
            SetLamp(Color.yellow);
        }

        Scanned?.Invoke(sort, kg);
    }

    /// <summary>DiverterLink가 판정을 받으면 표시등을 바꾼다 (선택)</summary>
    public void ShowResult(bool reject) => SetLamp(reject ? Color.red : Color.green);

    void SetLamp(Color c)
    {
        if (lamp == null) return;
        var mpb = new MaterialPropertyBlock();
        lamp.GetPropertyBlock(mpb);
        mpb.SetColor(BaseColorId, c);
        lamp.SetPropertyBlock(mpb);
    }

    void OnDrawGizmos()
    {
        var bc = GetComponent<BoxCollider>();
        if (bc == null) return;
        Gizmos.color = new Color(1f, 0.85f, 0f, 0.3f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(bc.center, bc.size);
    }
}