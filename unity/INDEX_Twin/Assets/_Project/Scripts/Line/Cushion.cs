using UnityEngine;

/// <summary>방석이 지금 어디 있는지 (SRS 6장 데이터 요구사항)</summary>
public enum CushionLocation { Bin, Arm, Conveyor, Slot, Agv, Pallet }

/// <summary>
/// 방석 1개의 데이터 (SRS v3.1 FR-02, 부록 A.5). 담당 A · scope unity-line
/// (v2.2의 Box.cs를 이름만 바꾼 것. .meta guid는 그대로라 프리팹 연결 유지)
/// - id: C001 형식. 적재함이 채울 때 순서대로, 다시 채워도 이어서 (중복 없음)
/// - 무게 kg (소수 둘째 자리). Rigidbody.mass에도 같은 값 → WeightScanner가 읽음
/// - 불량 여부는 생성할 때 정한 정답값 (판정은 factory_manager가 무게로 함. 시험·로그용)
/// 프리팹: 1×1×1 큐브 + BoxCollider + Rigidbody + 이 스크립트, 태그 Cushion. 크기는 BoxBin(적재함)이 생성할 때 정한다.
/// GameObject 이름 = id (ArmPickPlace가 이름으로 찾음)
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public class Cushion : MonoBehaviour
{
    [SerializeField] private string cushionId;
    [SerializeField] private float weightKg;
    [SerializeField] private bool isDefect;
    [SerializeField] private Vector3 sizeM;       // x=가로, y=높이, z=세로 (m)
    [SerializeField] private CushionLocation location = CushionLocation.Bin;

    public string CushionId => cushionId;
    public float WeightKg => weightKg;
    public bool IsDefect => isDefect;
    public Vector3 SizeM => sizeM;
    public float HeightM => sizeM.y;
    public CushionLocation Location { get => location; set => location = value; }
    public Rigidbody Body { get; private set; }

    /// <summary>윗면 중앙 (흡착 위치). 월드 좌표</summary>
    public Vector3 TopCenter => transform.position + transform.up * (sizeM.y * 0.5f);

    /// <summary>바닥 중앙. 월드 좌표</summary>
    public Vector3 BottomCenter => transform.position - transform.up * (sizeM.y * 0.5f);

    void Awake() => Body = GetComponent<Rigidbody>();

    /// <summary>BoxBin이 생성 직후 호출. 크기는 localScale로 적용 (프리팹은 1×1×1 큐브 기준)</summary>
    public void Init(string id, Vector3 size, float kg, bool defect)
    {
        cushionId = id;
        sizeM = size;
        weightKg = kg;
        isDefect = defect;
        location = CushionLocation.Bin;
        name = id;
        transform.localScale = size;
        if (Body == null) Body = GetComponent<Rigidbody>();
        Body.mass = kg;
    }
}
