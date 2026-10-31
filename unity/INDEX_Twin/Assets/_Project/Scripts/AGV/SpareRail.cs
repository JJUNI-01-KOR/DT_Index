// SpareRail.cs : 예비 파레트 레일 (FR-15). 담당 D · scope unity-agv
// 파레트 자리가 비면(OUT) 예비 자리의 빈 파레트를 자리로 옮기고(REFILLING), 다 옮기면 예비 자리에 새 빈 파레트를 만든다.
// 예비 자리에는 항상 빈 파레트가 1개 있다. 파레트1은 바깥쪽(-Z), 파레트2는 바깥쪽(+Z) 레일.
// 붙이는 곳: 예비 자리 빈 오브젝트 (위치 = 예비 파레트 중심). sparePallet에 지금 놓인 예비 파레트를 연결한다.
// 새 빈 파레트는 Play 시작 때 sparePallet을 본떠 숨겨 둔 원본에서 만든다.

using System;
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class SpareRail : MonoBehaviour
{
    [Tooltip("지금 예비 자리에 있는 빈 파레트")]
    [SerializeField] private GameObject sparePallet;
    [Tooltip("예비 자리 → 파레트 자리 이동 시간 (초)")]
    [SerializeField] private float moveSec = 3f;

    private GameObject template;     // 빈 파레트 원본 (숨김)
    private float palletY;
    private Coroutine moving;
    private GameObject movingPallet;
    private int made;

    void Awake()
    {
        if (sparePallet == null) { Debug.LogWarning($"[{name}] 예비 파레트가 연결되지 않음", this); return; }
        palletY = sparePallet.transform.position.y;
        template = Instantiate(sparePallet, transform);
        template.name = sparePallet.name + "_Template";
        template.SetActive(false);
    }

    /// <summary>예비 파레트를 slot 위치로 옮기기 시작한다. 도착하면 done(그 파레트)</summary>
    public void Refill(Transform slot, Action<GameObject> done)
    {
        if (moving != null) return;
        if (sparePallet == null) MakeSpare();
        if (sparePallet == null) return;
        moving = StartCoroutine(Move(slot, done));
    }

    IEnumerator Move(Transform slot, Action<GameObject> done)
    {
        movingPallet = sparePallet;
        sparePallet = null;
        Vector3 from = movingPallet.transform.position;
        Vector3 to = new Vector3(slot.position.x, from.y, slot.position.z);
        float t = 0f;
        while (t < moveSec)
        {
            t += Time.deltaTime;
            movingPallet.transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / moveSec));
            yield return null;
        }
        movingPallet.transform.position = to;
        var arrived = movingPallet;
        movingPallet = null;
        moving = null;
        MakeSpare();
        done?.Invoke(arrived);
    }

    /// <summary>E_STOP: 옮기던 파레트를 그 자리에서 멈춘다</summary>
    public void Halt()
    {
        if (moving != null) { StopCoroutine(moving); moving = null; }
    }

    /// <summary>E_STOP 해제 초기화: 빈 파레트를 slot에 바로 놓고 돌려준다. 예비 자리도 다시 채운다</summary>
    public GameObject PlaceNow(Transform slot)
    {
        Halt();
        var p = movingPallet != null ? movingPallet : sparePallet;
        if (p == null) { MakeSpare(); p = sparePallet; }
        if (p == null) return null;
        if (p == sparePallet) sparePallet = null;
        movingPallet = null;
        p.transform.position = new Vector3(slot.position.x, p.transform.position.y, slot.position.z);
        MakeSpare();
        return p;
    }

    void MakeSpare()
    {
        if (sparePallet != null || template == null) return;
        made++;
        sparePallet = Instantiate(template, new Vector3(transform.position.x, palletY, transform.position.z), template.transform.rotation);
        sparePallet.name = $"{name}_Pallet_{made}";
        sparePallet.SetActive(true);
    }
}
