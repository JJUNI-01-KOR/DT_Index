using UnityEngine;

/// <summary>
/// 라인 위 물건(방석)에 붙는 결과표.
/// - WeightScanner가 무게를 적는다 (Weighed, WeightKg). 판정은 하지 않는다.
/// - DiverterLink가 ROS 판정(/diverter_1_cmd)을 적는다 (Inspected, Reject).
/// - Diverter가 읽어서 레인을 고른다.
/// 프리팹에 미리 안 붙여도 된다. 처음 쓰는 쪽이 자동으로 붙인다.
///
/// 11/02 A: 판정을 factory_manager로 넘기면서 무게 측정(Weighed)과 판정(Inspected)을 나눔 (SRS FR-06 ~ FR-08)
/// </summary>
[DisallowMultipleComponent]
public class ItemSort : MonoBehaviour
{
    /// <summary>무게 센서를 지났는지 (방석당 1회 보고용)</summary>
    public bool Weighed { get; set; }

    /// <summary>잰 무게 kg</summary>
    public float WeightKg { get; set; }

    /// <summary>판정(분기 명령)을 받았는지</summary>
    public bool Inspected { get; set; }

    /// <summary>true = 불량 (ng)</summary>
    public bool Reject { get; set; }

    /// <summary>판정 이유 (로그·대시보드용). 예: "ROS lane=ok (0.95kg)"</summary>
    public string Reason { get; set; } = "";

    public static ItemSort Of(Component c)
    {
        var rb = c.GetComponentInParent<Rigidbody>();
        if (rb == null) return null;
        var s = rb.GetComponent<ItemSort>();
        return s != null ? s : rb.gameObject.AddComponent<ItemSort>();
    }
}