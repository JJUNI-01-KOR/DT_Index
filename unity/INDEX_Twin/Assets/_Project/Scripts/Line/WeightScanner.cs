using System;
using UnityEngine;

/// <summary>
/// 무게 스캐너 (Scanner Arch 아래, Quality Check 칸에 둔다).
/// Trigger에 들어온 물건의 무게(Rigidbody.mass, kg)를 재서
/// 기준보다 가벼우면 불량(Reject = true)으로 적는다. 실제 분기는 Diverter가 한다.
///
/// 준비: 빈 오브젝트 + BoxCollider(Is Trigger) + 이 스크립트. Quality Check 칸 위를 덮게.
/// 물건 무게는 각 물건 Rigidbody의 Mass 값.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class WeightScanner : MonoBehaviour
{
    [Tooltip("이 무게(kg) 미만이면 불량")]
    [SerializeField] private float minWeightKg = 0.8f;

    [Tooltip("검사 대상 태그 (비우면 아무 Rigidbody나)")]
    [SerializeField] private string targetTag = "Box";

    [Tooltip("판정 표시등 (선택). 정상 초록, 불량 빨강")]
    [SerializeField] private Renderer lamp;

    /// <summary>검사할 때마다 (결과표, 무게kg). ROS 연결은 여기에 구독</summary>
    public event Action<ItemSort, float> Scanned;

    public int PassCount { get; private set; }
    public int RejectCount { get; private set; }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    void OnTriggerEnter(Collider other)
    {
        var rb = other.attachedRigidbody;
        if (rb == null || rb.isKinematic) return;   // 팔이 들고 지나가는 건 무시
        if (!string.IsNullOrEmpty(targetTag) && !rb.CompareTag(targetTag)) return;

        var sort = ItemSort.Of(rb);
        if (sort.Inspected) return;                   // 한 번만 검사

        float kg = rb.mass;
        sort.Inspected = true;
        sort.Reject = kg < minWeightKg;
        sort.Reason = $"weight {kg:F2}kg {(sort.Reject ? "<" : ">=")} {minWeightKg:F2}kg";

        if (sort.Reject) RejectCount++; else PassCount++;
        Debug.Log($"[weight_scanner] {rb.name}: {sort.Reason} → {(sort.Reject ? "불량(오른쪽)" : "정상(왼쪽)")}", this);
        SetLamp(sort.Reject ? Color.red : Color.green);
        Scanned?.Invoke(sort, kg);
    }

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
