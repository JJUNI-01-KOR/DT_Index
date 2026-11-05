// ConveyorLoad.cs : 메인 라인·정상 레인·불량 레인 위 방석 수 (SRS v3.1 FR-04, WBS 3.2). 담당 A · scope unity-line
// ROS를 모른다. /conveyor_1_load 발행은 Scripts/ROS/ConveyorLink.cs가 한다 (CONVENTIONS 3절).
//
// 세는 규칙 (SRS 용어 정의, FR-04)
//   main : 투입 위치부터 방석이 레인에 완전히 들어가기 전까지. 분기 게이트(Diverter) 위도 main
//   ok   : 정상 레인에 들어온 뒤부터 집는 자리까지 (레인 끝 1번·2번 칸, 줄 포함)
//   ng   : 불량 레인, ok와 같음
//   로봇팔이 들고 있는 방석은 세지 않는다 (Kinematic이라 Conveyor가 안 잡고, 레인 끝 칸 방석은 그리퍼 확인)
//   한 방석이 main과 레인 경계에 걸치면 main으로 센다 (아직 "완전히" 안 들어감)
//   벨트 끝에서 칸으로 떨어지는 중인 방석은 레인 끝 대기 영역(holdDistance) 안이면 그 레인으로 센다
//
// 연결 (비워 두면 Start에서 이름으로 자동 연결, Main 씬 기준)
//   main : Conveyor "Line 1" + 모든 Diverter
//   ok   : Conveyor "right", "Line 3" + LaneEnd(Lane Label "ok")
//   ng   : Conveyor "left",  "Line 2" + LaneEnd(Lane Label "ng")
//
// 값이 바뀌면 Changed(main, ok, ng). 1초마다 다시 보내는 건 ConveyorLink가 한다.
//
// 11/02 A: 레인 끝 칸(1번 집는 자리 + 2번 대기) 중 찬 칸 수 OkEnd·NgEnd (0~2)도 센다.
//   /conveyor_1_load에 ok_end·ng_end로 같이 보내고, factory_manager는 어느 한쪽이라도 칸이 다 차면
//   컨베이어를 멈추고(/conveyor/cmd false, 상태는 RUNNING 유지) 팔1 투입도 멈춘다. 칸이 비면 다시 돈다.

using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(200)]   // Conveyor(0)·Diverter·LaneEnd(100)가 이번 물리 프레임을 끝낸 뒤 센다
public class ConveyorLoad : MonoBehaviour
{
    [Header("메인 라인 (투입 ~ 분기 게이트)")]
    [SerializeField] private Conveyor[] mainConveyors;
    [SerializeField] private Diverter[] mainDiverters;

    [Header("정상 레인 (ok)")]
    [SerializeField] private Conveyor[] okConveyors;
    [SerializeField] private LaneEnd okLaneEnd;

    [Header("불량 레인 (ng)")]
    [SerializeField] private Conveyor[] ngConveyors;
    [SerializeField] private LaneEnd ngLaneEnd;

    [Header("자동 연결 이름 (위 칸이 비었을 때)")]
    [SerializeField] private string[] mainNames = { "Line 1" };
    [SerializeField] private string[] okNames = { "right", "Line 3" };
    [SerializeField] private string[] ngNames = { "left", "Line 2" };

    [Header("기타")]
    [SerializeField] private string cushionTag = "Cushion";
    [Tooltip("레인 끝 Stopper 중심에서 이 거리(m) 안의 떨어지는 중인 방석도 그 레인으로 센다")]
    [SerializeField] private float laneEndRadius = 1.0f;

    public int Main { get; private set; }
    public int Ok { get; private set; }
    public int Ng { get; private set; }
    /// <summary>정상 레인 끝 칸(집는 자리·대기) 중 방석이 있는 칸 수 (0~2)</summary>
    public int OkEnd { get; private set; }
    /// <summary>불량 레인 끝 칸 중 방석이 있는 칸 수 (0~2)</summary>
    public int NgEnd { get; private set; }

    /// <summary>(main, ok, ng)가 바뀔 때</summary>
    public event Action<int, int, int> Changed;

    private readonly HashSet<Rigidbody> mainSet = new HashSet<Rigidbody>();
    private readonly HashSet<Rigidbody> okSet = new HashSet<Rigidbody>();
    private readonly HashSet<Rigidbody> ngSet = new HashSet<Rigidbody>();
    private KinematicGripper[] grippers = Array.Empty<KinematicGripper>();
    private readonly Collider[] probe = new Collider[32];
    private bool first = true;

    void Start()
    {
        AutoWire();
        grippers = FindObjectsByType<KinematicGripper>();
        Debug.Log($"[방석 수] 연결: 메인 라인 = {Names(mainConveyors)} + 분기 칸 {mainDiverters.Length}개 · " +
                  $"정상 레인 = {Names(okConveyors)} + {(okLaneEnd != null ? "레인 끝 " + okLaneEnd.transform.parent?.name : "레인 끝 없음")} · " +
                  $"불량 레인 = {Names(ngConveyors)} + {(ngLaneEnd != null ? "레인 끝 " + ngLaneEnd.transform.parent?.name : "레인 끝 없음")}", this);
    }

    void FixedUpdate()
    {
        mainSet.Clear(); okSet.Clear(); ngSet.Clear();

        // 메인: 벨트 + 분기 칸
        AddConveyors(mainConveyors, mainSet);
        foreach (var d in mainDiverters)
            if (d != null) foreach (var rb in d.Inside) if (Countable(rb)) mainSet.Add(rb);

        // 레인: 벨트 + 레인 끝 (칸 2개, 줄, 떨어지는 중)
        AddConveyors(okConveyors, okSet);
        AddLaneEnd(okLaneEnd, okSet);
        AddConveyors(ngConveyors, ngSet);
        AddLaneEnd(ngLaneEnd, ngSet);

        // 경계에 걸친 방석은 main (아직 레인에 완전히 안 들어감)
        okSet.ExceptWith(mainSet);
        ngSet.ExceptWith(mainSet);

        int m = mainSet.Count, o = okSet.Count, n = ngSet.Count;
        int oe = SlotsFilled(okLaneEnd), ne = SlotsFilled(ngLaneEnd);
        if (first || m != Main || o != Ok || n != Ng || oe != OkEnd || ne != NgEnd)
        {
            first = false;
            Main = m; Ok = o; Ng = n; OkEnd = oe; NgEnd = ne;
            Changed?.Invoke(m, o, n);
        }
    }

    /// <summary>
    /// 메인 라인·두 레인 위 방석을 모두 없앤다 (FR-16 해제 초기화, 11/05 BUG-003). 없앤 개수를 돌려준다.
    /// 지난 물리 프레임에 센 방석 기준이고, 팔이 들고 있는 방석은 건드리지 않는다. 레인 끝 칸·줄은 LaneEnd.ResetAll이 따로 비운다.
    /// </summary>
    public int ClearAll()
    {
        var all = new HashSet<Rigidbody>(mainSet);
        all.UnionWith(okSet);
        all.UnionWith(ngSet);
        int n = 0;
        foreach (var rb in all)
            if (rb != null) { Destroy(rb.gameObject); n++; }
        mainSet.Clear(); okSet.Clear(); ngSet.Clear();
        return n;
    }

    // ── 내부 ──

    void AddConveyors(Conveyor[] list, HashSet<Rigidbody> set)
    {
        if (list == null) return;
        foreach (var c in list)
            if (c != null) foreach (var rb in c.BodiesOnBelt) if (Countable(rb)) set.Add(rb);
    }

    void AddLaneEnd(LaneEnd end, HashSet<Rigidbody> set)
    {
        if (end == null) return;
        AddGo(end.AtPick, set);
        AddGo(end.AtWait, set);
        foreach (var g in end.Queue) AddGo(g, set);

        // 벨트 끝에서 칸으로 떨어지는 중 (아직 칸에 안 잡힘, 벨트 위도 아님)
        var stopper = end.GetComponent<Collider>();
        Vector3 c = stopper != null ? stopper.bounds.center : end.transform.position;
        int k = Physics.OverlapSphereNonAlloc(c, laneEndRadius, probe, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < k; i++)
        {
            var rb = probe[i].attachedRigidbody;
            if (rb != null && !rb.isKinematic && Countable(rb)) set.Add(rb);
        }
    }

    int SlotsFilled(LaneEnd end)
    {
        if (end == null) return 0;
        int c = 0;
        if (InSlot(end.AtPick)) c++;
        if (InSlot(end.AtWait)) c++;
        return c;
    }

    bool InSlot(GameObject g)
    {
        if (g == null) return false;
        var rb = g.GetComponent<Rigidbody>();
        return rb == null || !IsHeld(rb);   // 팔이 들고 있으면 빈 칸으로 본다
    }

    void AddGo(GameObject g, HashSet<Rigidbody> set)
    {
        if (g == null) return;
        var rb = g.GetComponent<Rigidbody>();
        if (rb != null && !IsHeld(rb)) set.Add(rb);
    }

    bool Countable(Rigidbody rb) =>
        rb != null && (string.IsNullOrEmpty(cushionTag) || rb.CompareTag(cushionTag)) && !IsHeld(rb);

    bool IsHeld(Rigidbody rb)
    {
        foreach (var g in grippers) if (g != null && g.Held == rb) return true;
        return false;
    }

    void AutoWire()
    {
        var all = FindObjectsByType<Conveyor>();
        if (mainConveyors == null || mainConveyors.Length == 0) mainConveyors = ByName(all, mainNames);
        if (okConveyors == null || okConveyors.Length == 0) okConveyors = ByName(all, okNames);
        if (ngConveyors == null || ngConveyors.Length == 0) ngConveyors = ByName(all, ngNames);
        if (mainDiverters == null || mainDiverters.Length == 0) mainDiverters = FindObjectsByType<Diverter>();

        foreach (var l in FindObjectsByType<LaneEnd>())
        {
            if (okLaneEnd == null && l.LaneLabel == "ok") okLaneEnd = l;
            if (ngLaneEnd == null && l.LaneLabel == "ng") ngLaneEnd = l;
        }
        if (okLaneEnd == null || ngLaneEnd == null)
            Debug.LogWarning("[방석 수] 레인 끝(LaneEnd)을 못 찾음. Stopper의 Lane Label이 ok / ng인지 확인", this);
    }

    static Conveyor[] ByName(Conveyor[] all, string[] names)
    {
        var r = new List<Conveyor>();
        foreach (var n in names)
            foreach (var c in all)
                if (c.name == n && !r.Contains(c)) r.Add(c);
        return r.ToArray();
    }

    static string Names(Conveyor[] list)
    {
        if (list == null || list.Length == 0) return "(없음)";
        var s = new List<string>();
        foreach (var c in list) if (c != null) s.Add(c.name);
        return string.Join("+", s);
    }

    [ContextMenu("지금 값 출력")]
    void PrintNow() => Debug.Log($"[방석 수] 메인 {Main}개, 정상 레인 {Ok}개 (끝 칸 {OkEnd}/2), 불량 레인 {Ng}개 (끝 칸 {NgEnd}/2)", this);
}