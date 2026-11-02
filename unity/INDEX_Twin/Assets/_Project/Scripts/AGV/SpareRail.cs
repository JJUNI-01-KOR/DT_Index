// SpareRail.cs : 예비 파레트 컨베이어 (FR-15). 담당 D · scope unity-agv
// 파레트 자리(1)가 비면(OUT) 예비 자리(2)의 빈 파레트를 컨베이어로 자리까지 옮긴다(REFILLING).
//
//   [자리 1] ◀── 컨베이어 ── [예비 자리 2]
//   1. 로봇팔이 자리 1 파레트를 다 채우면 AGV가 연결해 끌고 간다 → 자리 1 OUT
//   2. 컨베이어가 돌아 예비 자리 2의 파레트를 자리 1로 옮긴다 → READY. 다 오면 컨베이어 멈춤
//   3. 예비 자리 2를 다시 채운다
//      autoSpawnSpare = true  : 새 빈 파레트를 바로 만든다 (AGV가 빈 파레트를 돌려주기 전까지 쓰는 기본값)
//      autoSpawnSpare = false : AGV가 비운 파레트를 예비 자리에 내려놓을 때까지 기다린다 (Accept)
//   예비 파레트가 없을 때 자리가 비면 요청을 기억해 두었다가, 파레트가 들어오는 즉시 옮긴다.
//
// 붙이는 곳: 예비 자리 빈 오브젝트 (위치 = 예비 파레트 중심). sparePallet에 지금 놓인 예비 파레트를 연결한다.
// 컨베이어 모양: 메뉴 INDEX > Build Pallet Conveyors 가 이 오브젝트 아래 "ConveyorVisual"을 만든다 (롤러가 돈다).
// AGV 쪽 연결 (AgvHitch.Detach에서): var rail = SpareRail.Nearest(pos, 1f); if (rail != null) rail.Accept(pallet);

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SpareRail : MonoBehaviour
{
    [Tooltip("지금 예비 자리에 있는 빈 파레트")]
    [SerializeField] private GameObject sparePallet;
    [Tooltip("컨베이어 속도 (m/s). 예비 자리 → 파레트 자리 2 m를 약 4초에 옮긴다")]
    [SerializeField] private float beltSpeed = 0.5f;
    [Tooltip("출발·도착 때 속도를 올리고 줄이는 거리 (m)")]
    [SerializeField] private float easeDist = 0.3f;
    [Tooltip("켜면 예비 파레트를 보낸 뒤 새 빈 파레트를 바로 만든다. AGV가 빈 파레트를 돌려주면 끈다")]
    [SerializeField] private bool autoSpawnSpare = true;
    [Tooltip("돌릴 롤러들 (Build Pallet Conveyors가 채운다)")]
    [SerializeField] private Transform[] rollers = Array.Empty<Transform>();
    [Tooltip("롤러 반지름 (m). 롤러 회전 속도 계산용")]
    [SerializeField] private float rollerRadius = 0.03f;

    /// <summary>컨베이어가 도는 중인지</summary>
    public bool Running => moving != null;
    /// <summary>예비 자리에 파레트가 있는지</summary>
    public bool HasSpare => sparePallet != null;

    private GameObject template;     // 빈 파레트 원본 (숨김)
    private float palletY;
    private Coroutine moving;
    private GameObject movingPallet;
    private int made;
    private Transform pendingSlot;              // 예비 파레트가 없어서 기다리는 요청
    private Action<GameObject> pendingDone;

    private static readonly List<SpareRail> all = new List<SpareRail>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ClearStatics() => all.Clear();

    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable() => all.Remove(this);

    void Awake()
    {
        if (sparePallet == null) { Debug.LogWarning($"[{name}] 예비 파레트가 연결되지 않음", this); return; }
        palletY = sparePallet.transform.position.y;
        template = Instantiate(sparePallet, transform);
        template.name = sparePallet.name + "_Template";
        template.SetActive(false);
    }

    /// <summary>pos에서 radius 안의 가장 가까운 예비 자리 (AGV가 빈 파레트를 내려놓을 곳 찾기)</summary>
    public static SpareRail Nearest(Vector3 pos, float radius)
    {
        SpareRail best = null;
        float bd = radius;
        foreach (var r in all)
        {
            Vector3 d = r.transform.position - pos;
            d.y = 0f;
            if (d.magnitude <= bd) { bd = d.magnitude; best = r; }
        }
        return best;
    }

    /// <summary>예비 파레트를 slot 위치로 옮기기 시작한다. 도착하면 done(그 파레트).
    /// 예비 파레트가 없으면 들어올 때까지 기다렸다가 옮긴다.</summary>
    public void Refill(Transform slot, Action<GameObject> done)
    {
        if (moving != null) return;
        if (sparePallet == null && autoSpawnSpare) MakeSpare();
        if (sparePallet == null)
        {
            pendingSlot = slot;
            pendingDone = done;
            Debug.Log($"[{name}] 예비 파레트 없음 → AGV가 빈 파레트를 내려놓으면 옮김", this);
            return;
        }
        moving = StartCoroutine(Move(slot, done));
    }

    /// <summary>AGV가 비운 파레트를 예비 자리에 내려놓는다. 받으면 true</summary>
    public bool Accept(GameObject pallet)
    {
        if (pallet == null) return false;
        if (sparePallet != null) { Debug.LogWarning($"[{name}] 예비 자리에 이미 파레트가 있음 ({sparePallet.name})", this); return false; }
        if (template != null) palletY = template.transform.position.y;
        pallet.transform.SetParent(null, true);
        pallet.transform.position = new Vector3(transform.position.x, palletY, transform.position.z);
        sparePallet = pallet;
        Debug.Log($"[{name}] 예비 파레트 받음 ({pallet.name})", this);

        if (pendingSlot != null && moving == null)
        {
            var slot = pendingSlot;
            var done = pendingDone;
            pendingSlot = null;
            pendingDone = null;
            moving = StartCoroutine(Move(slot, done));
        }
        return true;
    }

    IEnumerator Move(Transform slot, Action<GameObject> done)
    {
        movingPallet = sparePallet;
        sparePallet = null;
        Vector3 from = movingPallet.transform.position;
        Vector3 to = new Vector3(slot.position.x, from.y, slot.position.z);
        float total = Vector3.Distance(from, to);
        float s = 0f;
        Debug.Log($"[{name}] 컨베이어 가동: {movingPallet.name} → {slot.name} ({total:F1} m)", this);

        while (s < total)
        {
            // 출발·도착 근처에서는 천천히 (최소 20%)
            float edge = Mathf.Min(s, total - s);
            float k = easeDist > 0f ? Mathf.Clamp(edge / easeDist, 0.2f, 1f) : 1f;
            float v = beltSpeed * k;
            s = Mathf.Min(total, s + v * Time.deltaTime);
            movingPallet.transform.position = Vector3.Lerp(from, to, total > 0f ? s / total : 1f);
            SpinRollers(v * Time.deltaTime);
            yield return null;
        }

        movingPallet.transform.position = to;
        var arrived = movingPallet;
        movingPallet = null;
        moving = null;
        Debug.Log($"[{name}] 컨베이어 정지: {arrived.name} 도착", this);
        if (autoSpawnSpare) MakeSpare();
        done?.Invoke(arrived);
    }

    void SpinRollers(float dist)
    {
        if (rollers == null || rollerRadius <= 0f) return;
        // 롤러 축은 로컬 Y (실린더). 파레트가 가는 쪽으로 윗면이 움직이게 돈다
        float deg = dist / rollerRadius * Mathf.Rad2Deg;
        foreach (var r in rollers)
            if (r != null) r.Rotate(0f, deg, 0f, Space.Self);
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
        pendingSlot = null;
        pendingDone = null;
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
