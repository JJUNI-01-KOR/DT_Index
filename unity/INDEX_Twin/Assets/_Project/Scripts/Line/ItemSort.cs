using UnityEngine;

/// <summary>
/// 라인 위 물건(박스·사과·택배 등)에 붙는 검사 결과표.
/// 센서(WeightScanner, ColorSensor)가 적고, 분기기(Diverter)가 읽는다.
/// 프리팹에 미리 안 붙여도 된다. 센서가 처음 검사할 때 자동으로 붙인다.
/// </summary>
[DisallowMultipleComponent]
public class ItemSort : MonoBehaviour
{
    /// <summary>검사를 받았는지</summary>
    public bool Inspected { get; set; }

    /// <summary>true = 불량</summary>
    public bool Reject { get; set; }

    /// <summary>판정 이유 (로그·대시보드용). 예: "weight 0.42kg < 0.80kg"</summary>
    public string Reason { get; set; } = "";

    public static ItemSort Of(Component c)
    {
        var rb = c.GetComponentInParent<Rigidbody>();
        if (rb == null) return null;
        var s = rb.GetComponent<ItemSort>();
        return s != null ? s : rb.gameObject.AddComponent<ItemSort>();
    }
}
