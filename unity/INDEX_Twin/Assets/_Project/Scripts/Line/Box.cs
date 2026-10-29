using UnityEngine;

/// <summary>박스가 지금 어디 있는지 (SRS 6장 데이터 요구사항)</summary>
public enum BoxLocation { Bin, Arm, Conveyor, Slot, Agv, Pallet }

/// <summary>정상 / 규격 초과 (작업 가이드의 BoxKind와 같은 이름)</summary>
public enum BoxKind { Normal, Oversize }

/// <summary>
/// 박스 1개의 데이터 (SRS 6장). 담당 A · scope unity-line
/// - box_id: BOX_001 형식, 적재함을 채울 때 순서대로, 중복 없음
/// - 크기 W×H×D (m), 구분 (정상 / 규격 초과), 현재 위치
/// 프리팹: 큐브 + BoxCollider + Rigidbody + 이 스크립트. 크기는 BoxBin이 생성할 때 정한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public class Box : MonoBehaviour
{
    [SerializeField] private string boxId;
    [SerializeField] private Vector3 sizeM;       // x=W, y=H, z=D (m)
    [SerializeField] private bool isOversize;
    [SerializeField] private BoxLocation location = BoxLocation.Bin;

    public string BoxId => boxId;
    public Vector3 SizeM => sizeM;
    public float HeightM => sizeM.y;
    public bool IsOversize => isOversize;
    public BoxKind Kind => isOversize ? BoxKind.Oversize : BoxKind.Normal;
    public BoxLocation Location { get => location; set => location = value; }
    public Rigidbody Body { get; private set; }

    /// <summary>윗면 중앙 (흡착 위치). 월드 좌표</summary>
    public Vector3 TopCenter => transform.position + transform.up * (sizeM.y * 0.5f);

    void Awake() => Body = GetComponent<Rigidbody>();

    /// <summary>BoxBin이 생성 직후 호출. 크기는 localScale로 적용 (프리팹은 1×1×1 큐브 기준)</summary>
    public void Init(string id, Vector3 size, bool oversize)
    {
        boxId = id;
        sizeM = size;
        isOversize = oversize;
        location = BoxLocation.Bin;
        name = id;
        transform.localScale = size;
        if (Body == null) Body = GetComponent<Rigidbody>();
    }
}