// PalletSlot.cs : 파레트 자리 1개 (FR-15). 담당 D · scope unity-agv
// ROS를 모른다. /pallet_N_status 발행은 Scripts/ROS/PalletSlotLink.cs가 한다.
//
// 상태 (SRS 용어)
//   READY      파레트가 자리에 있어 적재 가능
//   OUT        파레트가 자리를 완전히 벗어남 (AGV가 끌고 감)
//   REFILLING  예비 레일(SpareRail)에서 빈 파레트가 들어오는 중
//   READY → (AGV가 연결해 끌고 나감) → OUT → REFILLING → READY
// E_STOP       : 멈춘다. 보충 중이면 그 자리에서, OUT 뒤 보충 대기 중이면 보충을 시작하지 않는다 (해제 초기화까지)
// E_STOP → IDLE: 자리에 있는 파레트 위 방석을 지운다 (factory_manager가 적재 수를 0으로 되돌리므로 맞춘다, FR-16).
//                자리가 비어 있으면 빈 파레트를 바로 놓는다
// 붙이는 곳: 파레트 자리 빈 오브젝트 (위치 = 파레트 중심). pallet에 지금 놓인 파레트, spareRail에 그 자리의 예비 레일을 연결한다.

using System;
using System.Collections.Generic;
using UnityEngine;

public enum PalletState { READY, OUT, REFILLING }

[DisallowMultipleComponent]
public class PalletSlot : MonoBehaviour
{
    [SerializeField, Range(1, 2)] private int slotId = 1;
    [Tooltip("지금 자리에 있는 파레트")]
    [SerializeField] private GameObject pallet;
    [SerializeField] private SpareRail spareRail;
    [Tooltip("파레트 중심이 자리에서 수평으로 이 거리(m)보다 멀어지면 '완전히 벗어남' (파레트 크기 1 m)")]
    [SerializeField] private float leaveDistance = 1.0f;
    [Tooltip("OUT을 보고하고 보충을 시작하기까지 기다리는 시간 (초)")]
    [SerializeField] private float refillDelaySec = 0.5f;
    [SerializeField] private string cushionTag = "Cushion";
    [Tooltip("파레트 윗면에서 이 높이(m)까지의 방석을 파레트 위 방석으로 본다 (5단 = 0.5 m)")]
    [SerializeField] private float stackHeight = 2.0f;

    public int SlotId => slotId;
    public PalletState State { get; private set; } = PalletState.READY;
    public GameObject Pallet => pallet;

    /// <summary>상태가 바뀔 때</summary>
    public event Action<PalletSlot> StateChanged;

    private GameObject leaving;     // AGV에 넘겨준 파레트 (자리를 벗어나는지 지켜본다)
    private float outSince = -1f;
    private bool halted;            // E_STOP ~ 해제 초기화 사이. 상태 판단과 보충을 하지 않는다

    private static readonly List<PalletSlot> all = new List<PalletSlot>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ClearStatics() => all.Clear();

    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable() => all.Remove(this);

    void Start()
    {
        State = pallet != null ? PalletState.READY : PalletState.OUT;
        StateChanged?.Invoke(this);
        if (pallet == null) outSince = Time.time;
    }

    /// <summary>pos에서 radius 안의 가장 가까운 자리</summary>
    public static PalletSlot Nearest(Vector3 pos, float radius)
    {
        PalletSlot best = null;
        float bd = radius;
        foreach (var s in all)
        {
            float d = FlatDistance(s.transform.position, pos);
            if (d <= bd) { bd = d; best = s; }
        }
        return best;
    }

    /// <summary>AGV가 연결할 때 파레트를 넘겨받는다. 상태는 파레트가 자리를 완전히 벗어날 때 OUT으로 바뀐다</summary>
    public GameObject TakePallet()
    {
        if (pallet == null) return null;
        leaving = pallet;
        pallet = null;
        SetState(PalletState.OUT);
        outSince = Time.time;
        return leaving;
    }

    void Update()
    {
        if (halted) return;

        if (State == PalletState.READY && pallet == null)
        {
            // 넘겨준 파레트가 자리를 완전히 벗어났나 (삭제됐어도 벗어난 것으로 본다)
            if (leaving == null || FlatDistance(leaving.transform.position, transform.position) > leaveDistance)
            {
                leaving = null;
                SetState(PalletState.OUT);
                outSince = Time.time;
            }
        }

        if (State == PalletState.OUT && outSince >= 0f && Time.time - outSince >= refillDelaySec)
        {
            outSince = -1f;
            if (spareRail == null) { Debug.LogWarning($"[파레트 자리 {slotId}] 예비 레일이 연결되지 않아 보충 못 함", this); return; }
            SetState(PalletState.REFILLING);
            spareRail.Refill(transform, OnRefilled);
        }
    }

    void OnRefilled(GameObject newPallet)
    {
        pallet = newPallet;
        SetState(PalletState.READY);
    }

    /// <summary>E_STOP: 보충 중이면 그 자리에서 멈추고, 해제 초기화 전까지 보충을 새로 시작하지 않는다</summary>
    public void Halt()
    {
        halted = true;
        if (spareRail != null) spareRail.Halt();
    }

    /// <summary>E_STOP 해제 초기화 (FR-16): 파레트 위 방석을 지우고, 자리가 비어 있으면 빈 파레트를 바로 놓는다</summary>
    public void ResetAfterEStop()
    {
        halted = false;
        leaving = null;
        outSince = -1f;
        if (pallet != null)
        {
            int n = ClearCushions(pallet);
            if (n > 0) Debug.Log($"[파레트 자리 {slotId}] 해제 초기화: 파레트 위 방석 {n}개 삭제", this);
            SetState(PalletState.READY);
            return;
        }
        if (spareRail == null) { SetState(PalletState.OUT); return; }
        pallet = spareRail.PlaceNow(transform);
        SetState(pallet != null ? PalletState.READY : PalletState.OUT);
    }

    /// <summary>파레트 위(윗면 ~ stackHeight)에 있거나 파레트 자식인 방석을 지운다. 지운 수를 돌려준다</summary>
    int ClearCushions(GameObject p)
    {
        var pr = p.GetComponentInChildren<Renderer>();
        if (pr == null) return 0;
        Bounds pb = pr.bounds;
        float top = pb.max.y;
        int n = 0;
        foreach (var c in GameObject.FindGameObjectsWithTag(cushionTag))
        {
            bool onPallet = c.transform.IsChildOf(p.transform);
            if (!onPallet)
            {
                var col = c.GetComponentInChildren<Collider>();
                if (col == null) continue;
                Vector3 cc = col.bounds.center;
                onPallet = cc.x >= pb.min.x && cc.x <= pb.max.x && cc.z >= pb.min.z && cc.z <= pb.max.z
                           && col.bounds.min.y >= top - 0.05f && col.bounds.min.y <= top + stackHeight;
            }
            if (!onPallet) continue;
            Destroy(c);
            n++;
        }
        return n;
    }

    void SetState(PalletState s)
    {
        if (State == s) return;
        State = s;
        Debug.Log($"[파레트 자리 {slotId}] {s}", this);
        StateChanged?.Invoke(this);
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
