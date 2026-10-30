// ArmPickPlace.cs : 로봇팔 집기·놓기 동작 (FR-02, FR-10, FR-11, FR-16, FR-18). 담당 D · scope unity-arm
// ROS를 모른다. /arm_N_pick_place 서비스는 Scripts/ROS/ArmPickPlaceServer.cs가 받아 Run()을 부른다.
// Arm_Fairino 프리팹 루트에 ArmRig, KinematicGripper와 같이 붙인다 (팔 3대 공통).
//
// 순서
//   방석 찾기 → 관절값 4개 미리 계산(ArmIk) → 집기 위 → 집기 → 붙이기 → 집기 위 → 놓기 위 → 놓기 → 떼기 → 놓기 위
//   → 성공 응답 → 홈 자세 (응답 뒤, 다음 요청이 오면 거기서 바로 출발)
//   - 좌표: pickBottom·placeBottom은 방석 바닥 중심 (line_coords.yaml 기준). ToolPoint(패드 표면)를 방석 윗면에 맞춘다.
//   - 집을 방석: box_id가 있으면 그 이름의 방석(레인 끝 1번 칸 먼저), 비었으면(팔1) pickBottom에서 가장 가까운 방석.
//     칸에 놓인 방석은 Kinematic이라 Grab()이 아니라 Attach(rb)로 붙인다.
//   - 방향: 공구는 항상 아래를 본다. 방석이 정육면체라 yaw는 90° 단위 중 지금 손목에서 가장 가까운 쪽을 고른다.
//   - 미리 계산한 관절값 중 하나라도 못 찾으면(닿지 않음) 움직이지 않고 실패 ik_failed:<단계>.
//   - 시간 제한(timeoutSec, 기본 15초): 놓기 전에 넘으면 그 자리에서 멈추고, 든 방석을 집기 전 자리로 되돌리고, 홈으로 간 뒤 실패.
//   - E_STOP: EmergencyStop()이면 그 자리에서 멈추고(방석은 든 채) 진행 중 요청을 실패(E_STOP).
//     ResetAfterEStop()에서 든 방석을 없애고 홈으로 (FR-16 해제 초기화).
// 결과 메시지: 성공이면 방석 id. 실패면 busy · E_STOP · no_cushion · ik_failed:<단계> · timeout

using System;
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(ArmRig), typeof(KinematicGripper))]
public class ArmPickPlace : MonoBehaviour
{
    [Header("동작")]
    [Tooltip("이 시간(초) 안에 놓기까지 못 마치면 스스로 멈추고 실패. 호출 쪽 제한(20초)보다 짧게")]
    [SerializeField] private float timeoutSec = 15f;
    [Tooltip("집기·놓기 바로 위 접근 높이 (m)")]
    [SerializeField] private float approachHeight = 0.3f;
    [Tooltip("관절이 목표에서 이 각도(도) 안에 0.15초 머물면 도착으로 본다 (팔 처짐 허용)")]
    [SerializeField] private float settleToleranceDeg = 1.5f;
    [Tooltip("붙이고 뗀 뒤 잠깐 기다리는 시간 (초)")]
    [SerializeField] private float gripDwellSec = 0.2f;
    [Tooltip("놓을 때 방석 바닥을 놓을 자리보다 이만큼(m) 위에서 뗀다 (아래 방석·파레트와 겹치지 않게)")]
    [SerializeField] private float placeClearance = 0.005f;

    [Header("방석")]
    [SerializeField] private string cushionTag = "Cushion";
    [Tooltip("box_id가 비었을 때(팔1) pick 위치에서 방석을 찾는 반경 (m)")]
    [SerializeField] private float searchRadius = 0.3f;

    /// <summary>요청을 처리 중이면 true (놓기 위로 물러날 때까지)</summary>
    public bool Busy => running != null;

    /// <summary>방석을 붙였을 때 (적재함 수 갱신 등에 쓴다)</summary>
    public event Action<GameObject> Picked;

    /// <summary>방석을 놓았을 때</summary>
    public event Action<GameObject> Placed;

    private ArmRig rig;
    private KinematicGripper grip;
    private ArmIk ik;
    private Coroutine running;
    private Action<bool, string> done;
    private bool eStopped;
    private bool timedOut;

    // 시간 초과 때 방석 되돌리기
    private GameObject held;
    private Vector3 heldOrigPos;
    private Quaternion heldOrigRot;
    private bool heldOrigKinematic;

    private static readonly float[] ElbowUpA = { 0f, -1.0f, 1.5f, -0.5f, 1.57f, 0f };
    private static readonly float[] ElbowUpB = { 3.14f, -1.0f, 1.5f, -0.5f, 1.57f, 0f };

    private string Tag => $"[{(rig != null ? rig.ArmName : name)} pick_place]";

    void Awake()
    {
        rig = GetComponent<ArmRig>();
        grip = GetComponent<KinematicGripper>();
    }

    ArmIk Ik
    {
        get
        {
            if (ik == null) ik = new ArmIk(rig.Joints, rig.ToolPoint);
            return ik;
        }
    }

    /// <summary>
    /// 집기·놓기 1회. 끝나면 onDone(성공 여부, 메시지)을 한 번 부른다.
    /// pickBottom·placeBottom: 방석 바닥 중심 (월드), placeYawDeg: 놓은 방석 방향 (월드 yaw, 도)
    /// </summary>
    public void Run(string cushionId, Vector3 pickBottom, Vector3 placeBottom, float placeYawDeg, Action<bool, string> onDone)
    {
        if (eStopped) { onDone?.Invoke(false, "E_STOP"); return; }
        if (Busy) { onDone?.Invoke(false, "busy"); return; }

        // ── 1. 방석 찾기 ──
        var cushion = FindCushion(cushionId, pickBottom);
        var rb = cushion != null ? cushion.GetComponent<Rigidbody>() : null;
        var col = cushion != null ? cushion.GetComponentInChildren<Collider>() : null;
        if (rb == null || col == null)
        {
            Debug.LogWarning($"{Tag} 방석을 찾지 못함 (id '{cushionId}', pick {pickBottom})", this);
            onDone?.Invoke(false, "no_cushion");
            return;
        }

        // ── 2. 목표 자세와 관절값 미리 계산 ──
        Bounds b = col.bounds;
        float h = b.size.y;
        Vector3 up = Vector3.up;
        Vector3 pickTop = new Vector3(b.center.x, b.max.y, b.center.z);
        Vector3 placeTop = placeBottom + up * (h + placeClearance);

        // yaw는 90° 단위 4가지 중 손목이 덜 도는 쪽부터 시도 (손목 관절 한계에 걸리면 다음 후보)
        var q0 = rig.GetJointPositions();
        string failed = "pick_above";
        float[] qPa = null, qP = null, qLa = null, qL = null;
        bool planned = false;
        foreach (float pickYaw in Candidates90(cushion.transform.eulerAngles.y, CurrentToolYaw()))
        {
            if (!Plan(pickTop + up * approachHeight, pickYaw, q0, out qPa)) { failed = "pick_above"; continue; }
            if (!Plan(pickTop, pickYaw, qPa, out qP)) { failed = "pick"; continue; }
            foreach (float placeYaw in Candidates90(placeYawDeg, pickYaw))
            {
                if (!Plan(placeTop + up * approachHeight, placeYaw, qPa, out qLa)) { failed = "place_above"; continue; }
                if (!Plan(placeTop, placeYaw, qLa, out qL)) { failed = "place"; continue; }
                planned = true;
                break;
            }
            if (planned) break;
        }
        if (!planned)
        {
            Debug.LogWarning($"{Tag} 팔이 닿지 않음: {failed} (pick {pickTop}, place {placeTop})", this);
            onDone?.Invoke(false, "ik_failed:" + failed);
            return;
        }

        done = onDone;
        timedOut = false;
        running = StartCoroutine(Sequence(cushion, rb, qPa, qP, qLa, qL));
    }

    /// <summary>E_STOP: 그 자리에서 멈추고 진행 중인 요청을 E_STOP으로 끝낸다. 든 방석은 그대로 든다</summary>
    public void EmergencyStop()
    {
        eStopped = true;
        if (running != null) { StopCoroutine(running); running = null; }
        Freeze();
        Debug.Log($"{Tag} E_STOP: 정지", this);
        Finish(false, "E_STOP");
    }

    /// <summary>E_STOP 해제 초기화 (FR-16): 든 방석을 없애고 홈 자세로</summary>
    public void ResetAfterEStop()
    {
        eStopped = false;
        if (running != null) { StopCoroutine(running); running = null; }
        done = null;
        if (grip.IsHolding)
        {
            var rb = grip.Release();
            if (rb != null) Destroy(rb.gameObject);
        }
        held = null;
        rig.GoHome();
        Debug.Log($"{Tag} 해제 초기화: 홈 자세로", this);
    }

    // ── 동작 ──

    IEnumerator Sequence(GameObject cushion, Rigidbody rb, float[] qPa, float[] qP, float[] qLa, float[] qL)
    {
        float t0 = Time.time;
        string id = cushion.name;
        Debug.Log($"{Tag} 시작: {id}", this);

        yield return MoveTo(qPa, t0); if (timedOut) { AbortTimeout(); yield break; }
        yield return MoveTo(qP, t0); if (timedOut) { AbortTimeout(); yield break; }

        heldOrigPos = cushion.transform.position;
        heldOrigRot = cushion.transform.rotation;
        heldOrigKinematic = rb.isKinematic;
        grip.Attach(rb);
        held = cushion;
        Picked?.Invoke(cushion);
        yield return new WaitForSeconds(gripDwellSec);

        yield return MoveTo(qPa, t0); if (timedOut) { AbortTimeout(); yield break; }
        yield return MoveTo(qLa, t0); if (timedOut) { AbortTimeout(); yield break; }
        yield return MoveTo(qL, t0); if (timedOut) { AbortTimeout(); yield break; }

        grip.Release();
        held = null;
        Placed?.Invoke(cushion);
        LaneEnd.NotifyPlaced(cushion);   // 레인 끝 1번 칸 방석이었으면 뒤 방석을 당긴다
        yield return new WaitForSeconds(gripDwellSec);

        // 놓은 뒤에는 시간 제한 없이 물러난다 (되돌릴 방석이 없다)
        yield return MoveTo(qLa, float.MaxValue);
        timedOut = false;

        Debug.Log($"{Tag} 성공: {id} ({Time.time - t0:F1}초)", this);
        rig.GoHome();          // 응답 전에 홈 이동을 건다. 응답 직후 새 요청이 오면 그 요청의 이동이 덮어쓴다
        Finish(true, id);
    }

    IEnumerator MoveTo(float[] q, float t0)
    {
        rig.SetJointTargets(q);
        yield return new WaitForFixedUpdate();
        float settled = 0f;
        while (true)
        {
            if (t0 != float.MaxValue && Time.time - t0 > timeoutSec) { timedOut = true; yield break; }
            if (!rig.IsMoving) yield break;
            settled = WithinTolerance(q) ? settled + Time.deltaTime : 0f;
            if (settled >= 0.15f) yield break;
            yield return null;
        }
    }

    void AbortTimeout()
    {
        Freeze();
        if (held != null)
        {
            var c = held;
            var rb = grip.Release();
            c.transform.SetPositionAndRotation(heldOrigPos, heldOrigRot);
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = heldOrigKinematic;
            }
            Physics.SyncTransforms();
            held = null;
            Debug.LogWarning($"{Tag} {timeoutSec}초 초과: 멈추고 {c.name}을(를) 집기 전 자리로 되돌림", this);
        }
        else
        {
            Debug.LogWarning($"{Tag} {timeoutSec}초 초과: 멈춤", this);
        }
        rig.GoHome();
        Finish(false, "timeout");
    }

    void Finish(bool ok, string msg)
    {
        running = null;
        var d = done;
        done = null;
        d?.Invoke(ok, msg);
    }

    void Freeze()
    {
        var now = rig.GetJointPositions();
        rig.SetJointTargetsImmediate(now);
    }

    bool WithinTolerance(float[] q)
    {
        var now = rig.GetJointPositions();
        for (int i = 0; i < q.Length; i++)
            if (Mathf.Abs(now[i] - q[i]) * Mathf.Rad2Deg > settleToleranceDeg) return false;
        return true;
    }

    // ── 계산 ──

    bool Plan(Vector3 toolPos, float yawDeg, float[] prev, out float[] q)
    {
        q = new float[rig.Joints.Length];
        var rot = Quaternion.LookRotation(Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward, Vector3.down);
        var seeds = new[] { prev, rig.HomeRad, ElbowUpA, ElbowUpB };
        return Ik.Solve(toolPos, rot, seeds, q, out _, out _);
    }

    float CurrentToolYaw()
    {
        Vector3 f = rig.ToolPoint.forward;
        f.y = 0f;
        return f.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    /// <summary>baseYaw + 90°×k (k = 0~3)를 refYaw에 가까운 순서로</summary>
    static float[] Candidates90(float baseYaw, float refYaw)
    {
        var c = new float[4];
        for (int k = 0; k < 4; k++) c[k] = baseYaw + 90f * k;
        System.Array.Sort(c, (a, b) => Mathf.Abs(Mathf.DeltaAngle(a, refYaw)).CompareTo(Mathf.Abs(Mathf.DeltaAngle(b, refYaw))));
        return c;
    }

    GameObject FindCushion(string id, Vector3 near)
    {
        if (!string.IsNullOrEmpty(id))
        {
            if (LaneEnd.TryGetAtPick(id, out var atLane)) return atLane;
            foreach (var g in GameObject.FindGameObjectsWithTag(cushionTag))
                if (g.name == id && !IsHeld(g)) return g;
            return null;
        }

        GameObject best = null;
        float bestD = searchRadius;
        foreach (var g in GameObject.FindGameObjectsWithTag(cushionTag))
        {
            if (IsHeld(g)) continue;
            var c = g.GetComponentInChildren<Collider>();
            Vector3 bottom = c != null ? new Vector3(c.bounds.center.x, c.bounds.min.y, c.bounds.center.z) : g.transform.position;
            float d = Vector3.Distance(bottom, near);
            if (d < bestD) { bestD = d; best = g; }
        }
        return best;
    }

    static bool IsHeld(GameObject g)
    {
        var rb = g.GetComponent<Rigidbody>();
        if (rb == null) return false;
        foreach (var k in FindObjectsByType<KinematicGripper>(FindObjectsSortMode.None))
            if (k.Held == rb) return true;
        return false;
    }

    // ── 시험: Play 중 컴포넌트 우클릭 ──
    [Header("시험 (Play 중 우클릭 메뉴)")]
    [SerializeField] private string testCushionId = "";
    [SerializeField] private Vector3 testPickBottom;
    [SerializeField] private Vector3 testPlaceBottom;
    [SerializeField] private float testPlaceYaw;

    [ContextMenu("시험: 집기·놓기 1회 (시험 칸 값)")]
    void TestRun()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Play 중에만 동작해", this); return; }
        Run(testCushionId, testPickBottom, testPlaceBottom, testPlaceYaw,
            (ok, msg) => Debug.Log($"{Tag} 시험 결과: success={ok}, message={msg}", this));
    }
}
