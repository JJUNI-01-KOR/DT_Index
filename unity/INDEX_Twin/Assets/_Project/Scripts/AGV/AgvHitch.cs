// AgvHitch.cs : AGV1 파레트 연결·분리 (FR-13, FR-14). 담당 D · scope unity-agv
// ROS를 모른다. {"hitch":"attach"|"detach"}는 Scripts/ROS/AgvLink.cs가 받아 Attach()·Detach()를 부른다.
//
// Attach(): attachRadius 안의 가장 가까운 파레트 자리(PalletSlot)에서 파레트를 넘겨받아 AGV 자식으로 붙인다.
//           지금 상대 위치 그대로 붙여서 함께 움직인다 (강체 견인). 파레트 위 방석은 파레트 자식으로 붙이고 Kinematic으로 고정한다.
//           파레트가 자리를 완전히 벗어나면 그 자리는 OUT이 된다 (PalletSlot이 판단).
// Detach(): 파레트를 떼어 그 자리에 둔다. 최종 구역(FinalZone) 안이면 삭제(3초 뒤)를 맡긴다.
// ResetAll(): 끌고 있던 파레트를 없앤다 (FR-16 해제 초기화).

using UnityEngine;

[DisallowMultipleComponent]
public class AgvHitch : MonoBehaviour
{
    [Tooltip("AGV 중심에서 이 거리(m) 안의 파레트 자리에서만 연결한다")]
    [SerializeField] private float attachRadius = 2.0f;
    [SerializeField] private string cushionTag = "Cushion";
    [Tooltip("파레트 윗면에서 이 높이(m)까지의 방석을 파레트에 고정한다 (5단 = 1.5 m)")]
    [SerializeField] private float stackHeight = 2.0f;

    /// <summary>끌고 있는 파레트 (없으면 null)</summary>
    public GameObject Towed { get; private set; }

    public bool Attach(out string error)
    {
        error = null;

        // 1차 방어: 이미 내가 파레트를 끌고 있다면 무시
        if (Towed != null)
        {
            Debug.LogWarning("[AGV1] 이미 파레트가 연결된 상태입니다 (중복 호출 무시)", this);
            return true;
        }

        var slot = PalletSlot.Nearest(transform.position, attachRadius);
        if (slot == null) { error = $"{attachRadius} m 안에 파레트 자리가 없음"; return false; }

        var pallet = slot.TakePallet();

        // 2차 방어: 자리는 찾았지만 파레트가 이미 사라진 경우 (에러 방지 로직)
        if (pallet == null)
        {
            Debug.LogWarning($"[AGV1] 파레트 자리 {slot.SlotId}에 파레트가 없습니다 (중복 요청으로 간주하고 무시)", this);
            return true; // 실패(false) 대신 조용히 성공(true) 처리하여 멈춤 방지
        }

        int n = FixCushions(pallet);
        pallet.transform.SetParent(transform, true);
        Towed = pallet;
        Debug.Log($"[AGV1] 파레트 {slot.SlotId} 연결 ({pallet.name}, 방석 {n}개)", this);
        return true;
    }

    public bool Detach(out string error)
    {
        error = null;
        // 이미 분리된 상태라면 중복 호출로 간주하고 조용히 성공 처리 (에러 방지)
        if (Towed == null)
        {
            Debug.LogWarning("[AGV1] 이미 파레트가 분리된 상태입니다 (중복 호출 무시)", this);
            return true;
        }

        var pallet = Towed;
        Towed = null;
        pallet.transform.SetParent(null, true);

        var zone = FinalZone.Containing(pallet.transform.position);
        if (zone != null) zone.Accept(pallet);
        else Debug.LogWarning($"[AGV1] 최종 구역 밖에서 파레트 분리: {pallet.name} {pallet.transform.position}", this);

        Debug.Log($"[AGV1] 파레트 분리 ({pallet.name})", this);
        return true;
    }

    public void ResetAll()
    {
        if (Towed != null)
        {
            Debug.Log($"[AGV1] 초기화: 끌던 파레트 삭제 ({Towed.name})", this);
            Destroy(Towed);
        }
        Towed = null;
    }

    /// <summary>파레트 위 방석을 파레트 자식으로 붙이고 Kinematic으로 고정. 고정한 수를 돌려준다</summary>
    int FixCushions(GameObject pallet)
    {
        var pr = pallet.GetComponentInChildren<Renderer>();
        if (pr == null) return 0;
        Bounds pb = pr.bounds;
        float top = pb.max.y;
        int n = 0;
        foreach (var c in GameObject.FindGameObjectsWithTag(cushionTag))
        {
            var col = c.GetComponentInChildren<Collider>();
            if (col == null) continue;
            Vector3 cc = col.bounds.center;
            if (cc.x < pb.min.x || cc.x > pb.max.x || cc.z < pb.min.z || cc.z > pb.max.z) continue;
            if (col.bounds.min.y < top - 0.05f || col.bounds.min.y > top + stackHeight) continue;

            var rb = c.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true;
                }
                // 보간(Interpolate)이 켜진 Kinematic 방석은 부모가 움직여도 제자리로 되돌아가 뒤처진다
                rb.interpolation = RigidbodyInterpolation.None;
            }
            c.transform.SetParent(pallet.transform, true);
            n++;
        }
        return n;
    }
}
