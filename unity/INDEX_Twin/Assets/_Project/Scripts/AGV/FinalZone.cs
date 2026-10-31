// FinalZone.cs : 최종 구역 (FR-14). 담당 D · scope unity-agv
// AGV1이 분리한 파레트와 그 위 방석을 deleteDelaySec(기본 3초) 뒤 삭제한다. AGV 복귀를 기다리지 않는다.
// 삭제 전에 E_STOP이 오면 해제 초기화(ResetAll)에서 함께 삭제한다 (FR-16).
// 붙이는 곳: 최종 구역 바닥 (Pallet_Finish). 중심에서 acceptRadius 안에 내려놓은 파레트를 받는다.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class FinalZone : MonoBehaviour
{
    [SerializeField] private float deleteDelaySec = 3f;
    [Tooltip("중심에서 이 거리(m) 안에 내려놓은 파레트를 받는다")]
    [SerializeField] private float acceptRadius = 2f;

    private readonly List<GameObject> pending = new List<GameObject>();
    private static readonly List<FinalZone> all = new List<FinalZone>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ClearStatics() => all.Clear();

    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable() => all.Remove(this);

    public static FinalZone Containing(Vector3 pos)
    {
        foreach (var z in all)
        {
            Vector3 d = pos - z.transform.position;
            d.y = 0f;
            if (d.magnitude <= z.acceptRadius) return z;
        }
        return null;
    }

    /// <summary>내려놓은 파레트를 받아 deleteDelaySec 뒤 삭제</summary>
    public void Accept(GameObject pallet)
    {
        pending.Add(pallet);
        StartCoroutine(DeleteLater(pallet));
    }

    IEnumerator DeleteLater(GameObject pallet)
    {
        yield return new WaitForSeconds(deleteDelaySec);
        if (pallet != null)
        {
            Debug.Log($"[최종 구역] {deleteDelaySec}초 지나 삭제: {pallet.name} (방석 {CountCushions(pallet)}개)", this);
            Destroy(pallet);
        }
        pending.Remove(pallet);
    }

    /// <summary>E_STOP 해제 초기화: 기다리던 파레트를 바로 삭제</summary>
    public void ResetAll()
    {
        StopAllCoroutines();
        foreach (var p in pending) if (p != null) Destroy(p);
        pending.Clear();
    }

    public static void ResetAllZones()
    {
        foreach (var z in all) z.ResetAll();
    }

    static int CountCushions(GameObject pallet)
    {
        int n = 0;
        foreach (Transform t in pallet.GetComponentsInChildren<Transform>()) if (t.CompareTag("Cushion")) n++;
        return n;
    }
}
