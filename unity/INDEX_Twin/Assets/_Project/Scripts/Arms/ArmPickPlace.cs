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
//
// 11/01 A (팔1 투입, FR-02): feedBin(적재함)을 연결하면 box_id가 빈 요청은 pick_pose 대신
//   적재함의 "다음 칸"(위층부터) 방석을 집는다. factory_manager는 팔1에 pick_pose를 비워 보낸다.
//   집으면 적재함이 Picked 이벤트로 남은 수를 줄이고(/bin/count), 시간 초과로 되돌리면 같은 칸으로 돌려놓는다
//   (실패하면 칸 순서를 넘기지 않음). 팔2·3은 feedBin을 비워 두면 예전과 똑같다.
//
// 11/01 A (자연스러운 움직임):
//   - 관절 최단 경로: IK가 같은 자세를 360° 돌아간 각도로 풀어도, 직전 관절값에서 가장 가까운 각도로 바꾼다.
//     (예전엔 적재함 → 벨트로 갈 때 베이스가 반대쪽(랙 쪽)으로 크게 돌았다)
//   - 자동 홈(useAutoHome): Play 시작 때 homeToolPos 위에서 공구가 아래를 보는 자세를 IK로 구해 홈으로 쓴다.
//     팔1은 적재함과 투입 위치 사이 위쪽 (Tools/INDEX/로봇팔1 만들기가 채움). 예전 홈은 팔3 기준이라 랙 쪽을 봤다

using System;
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-10)]   // ArmRig.Start(홈으로 이동 시작)보다 먼저 자동 홈을 정한다
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
    [Tooltip("box_id로 찾은 방석이 pick 위치에서 이보다(m) 멀면 다른 곳의 방석으로 보고 no_cushion (팔2·3)")]
    [SerializeField] private float maxPickOffset = 0.5f;
    [Tooltip("팔1 전용: 적재함. 연결하면 box_id가 빈 요청은 적재함의 다음 칸(위층부터) 방석을 집는다")]
    [SerializeField] private CushionBin feedBin;

    [Header("자동 홈 (11/01 A)")]
    [Tooltip("켜면 Play 시작 때 homeToolPos 위에서 공구가 아래를 보는 자세를 홈으로 쓴다")]
    [SerializeField] private bool useAutoHome = false;
    [Tooltip("홈 자세에서 공구 끝(ToolPoint)이 있을 월드 위치. 작업 구역 사이 위쪽")]
    [SerializeField] private Vector3 homeToolPos;

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

    void Start()
    {
        if (useAutoHome) ComputeAutoHome();
    }

    /// <summary>
    /// 자동 홈: 팔꿈치가 위로 접힌 기본 자세(ElbowUpA)에서 베이스만 돌려 homeToolPos 쪽을 보게 한 뒤,
    /// 그 근처에서 공구 끝이 homeToolPos에 오는 자세를 IK로 다듬는다. 못 찾으면 방향만 돌린 기본 자세를 쓴다.
    /// (베이스를 작업 구역 쪽으로 두면 적재함·벨트로 갈 때 관절 한계를 넘어 반대로 크게 도는 일이 없다)
    /// </summary>
    void ComputeAutoHome()
    {
        Vector3 b = rig.Joints[0].transform.position;
        float Yaw(float[] q)
        {
            Ik.ForwardWorld(q, out Vector3 p, out _);
            return Mathf.Atan2(p.x - b.x, p.z - b.z);
        }

        // 베이스 관절(+) 방향이 월드 yaw를 어느 쪽으로 돌리는지
        var probe = (float[])ElbowUpA.Clone();
        probe[0] = 0f;
        float yaw0 = Yaw(probe);
        probe[0] = 0.2f;
        float sign = Mathf.Sign(Mathf.DeltaAngle(yaw0 * Mathf.Rad2Deg, Yaw(probe) * Mathf.Rad2Deg));
        if (sign == 0f) sign = 1f;

        float target = Mathf.Atan2(homeToolPos.x - b.x, homeToolPos.z - b.z);
        var seed = (float[])ElbowUpA.Clone();
        seed[0] = sign * Mathf.DeltaAngle(yaw0 * Mathf.Rad2Deg, target * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        Unwrap(seed, new float[seed.Length]);   // 관절 한계 안, 0에 가까운 쪽으로

        float[] best = null;
        float bestD = float.MaxValue;
        var q = new float[seed.Length];
        for (int y = 0; y < 4; y++)
        {
            var rot = Quaternion.LookRotation(Quaternion.Euler(0f, y * 90f, 0f) * Vector3.forward, Vector3.down);
            if (!Ik.Solve(homeToolPos, rot, new[] { seed }, q, out _, out _)) continue;
            Unwrap(q, seed);
            // 베이스가 작업 구역 방향에서 45° 넘게 벗어난 해(팔을 뒤로 젖혀 닿는 자세)는 버린다
            if (Mathf.Abs(q[0] - seed[0]) > 45f * Mathf.Deg2Rad) continue;
            float d = 0f;
            for (int i = 0; i < q.Length; i++) d += Mathf.Abs(q[i] - seed[i]);
            if (d < bestD) { bestD = d; best = (float[])q.Clone(); }
        }
        string how = best != null ? "IK로 맞춤" : "IK 실패 → 방향만 돌린 기본 자세";
        if (best == null) best = seed;

        rig.SetHomeRad(best);
        Debug.Log($"{Tag} 자동 홈 ({how}): 공구 끝 {homeToolPos} 쪽, 관절(rad) {string.Join(", ", System.Array.ConvertAll(best, v => v.ToString("F2")))}", this);
    }

    /// <summary>q의 각 관절을 360° 차이 나는 같은 각도 중 prev에 가장 가까운 값으로 (관절 한계 안). 최단 경로로 돌게</summary>
    void Unwrap(float[] q, float[] prev)
    {
        var joints = rig.Joints;
        const float TwoPi = Mathf.PI * 2f;
        for (int i = 0; i < q.Length; i++)
        {
            var d = joints[i].xDrive;
            bool limited = joints[i].twistLock == ArticulationDofLock.LimitedMotion && d.upperLimit > d.lowerLimit;
            float lo = d.lowerLimit * Mathf.Deg2Rad, hi = d.upperLimit * Mathf.Deg2Rad;
            float best = q[i], bestD = Mathf.Abs(q[i] - prev[i]);
            for (int k = -2; k <= 2; k++)
            {
                if (k == 0) continue;
                float c = q[i] + k * TwoPi;
                if (limited && (c < lo || c > hi)) continue;
                float dd = Mathf.Abs(c - prev[i]);
                if (dd < bestD) { bestD = dd; best = c; }
            }
            q[i] = best;
        }
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
        // 11/01 A: 집기 위·놓기 위를 같은 높이(둘 중 높은 쪽 + approachHeight)로 → 수직으로 올라가서, 수평으로 돌고, 수직으로 내려간다
        float safeY = Mathf.Max(pickTop.y, placeTop.y) + approachHeight;
        Vector3 pickAbove = new Vector3(pickTop.x, safeY, pickTop.z);
        Vector3 placeAbove = new Vector3(placeTop.x, safeY, placeTop.z);

        // yaw는 90° 단위 4가지 중 손목이 덜 도는 쪽부터 시도 (손목 관절 한계에 걸리면 다음 후보)
        var q0 = rig.GetJointPositions();
        string failed = "pick_above";
        float[] qPa = null, qP = null, qLa = null, qL = null;
        bool planned = false;
        // 11/01 A: 먼저 손목(wrist1·wrist2)이 크게 뒤집히지 않는 해만 찾고, 없을 때만 제한을 푼다
        for (int pass = 0; pass < 2 && !planned; pass++)
        {
        foreach (float pickYaw in Candidates90(cushion.transform.eulerAngles.y, CurrentToolYaw()))
        {
            strictWrist = false;   // 홈 → 집기 위는 자유 (홈 자세와 손목이 달라도 됨)
            if (!Plan(pickAbove, pickYaw, q0, out qPa)) { failed = "pick_above"; continue; }
            strictWrist = pass == 0;   // 집은 뒤부터는 손목 고정 우선
            if (!Plan(pickTop, pickYaw, qPa, out qP)) { failed = "pick"; continue; }
            foreach (float placeYaw in Candidates90(placeYawDeg, pickYaw))
            {
                if (!Plan(placeAbove, placeYaw, qPa, out qLa)) { failed = "place_above"; continue; }
                if (!Plan(placeTop, placeYaw, qLa, out qL)) { failed = "place"; continue; }
                planned = true;
                break;
            }
            if (planned) break;
        }
        }
        strictWrist = false;
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

        var placedRb = grip.Release();
        held = null;
        if (placedRb != null && feedBin == null) SettleOnPallet(placedRb);
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
            if (feedBin != null) feedBin.Return(c.GetComponent<Cushion>());   // 적재함 방석이면 같은 칸으로 (칸 순서 유지)
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

    [Tooltip("손목(wrist1·wrist2)이 한 구간에서 이 각도(도)보다 많이 돌면 그 해는 피한다 (불필요한 손목 뒤집기 방지)")]
    [SerializeField] private float maxWristTurnDeg = 60f;
    private bool strictWrist;

    bool Plan(Vector3 toolPos, float yawDeg, float[] prev, out float[] q)
    {
        q = new float[rig.Joints.Length];
        var rot = Quaternion.LookRotation(Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward, Vector3.down);
        var seeds = new[] { prev, rig.HomeRad, ElbowUpA, ElbowUpB };
        if (!Ik.Solve(toolPos, rot, seeds, q, out _, out _)) return false;
        Unwrap(q, prev);   // 같은 자세라도 직전 관절값에서 가장 짧게 도는 각도로
        if (strictWrist)
            for (int i = 3; i <= 4 && i < q.Length; i++)   // 3 = wrist1, 4 = wrist2
                if (Mathf.Abs(q[i] - prev[i]) > maxWristTurnDeg * Mathf.Deg2Rad) return false;
        return true;
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
        if (string.IsNullOrEmpty(id) && feedBin != null)
        {
            var next = feedBin.PeekNext();             // 팔1: 적재함 다음 칸 (pick_pose는 쓰지 않음)
            return next != null ? next.gameObject : null;
        }
        if (!string.IsNullOrEmpty(id))
        {
            GameObject found = null;
            if (LaneEnd.TryGetAtPick(id, out var atLane)) found = atLane;
            else
                foreach (var g in GameObject.FindGameObjectsWithTag(cushionTag))
                    if (g.name == id && !IsHeld(g)) { found = g; break; }
            if (found == null) return null;
            // 11/02 D: 같은 id라도 pick_pose에서 멀면(다른 레인·적재함) 이 팔이 집을 방석이 아니다 → no_cushion
            float off = Vector3.Distance(Bottom(found), near);
            if (off > maxPickOffset)
            {
                Debug.LogWarning($"{Tag} 방석 '{id}'이(가) pick 위치에서 {off:F2} m 떨어져 있음 (허용 {maxPickOffset:F2} m). 다른 곳의 방석이라 집지 않음", this);
                return null;
            }
            return found;
        }

        GameObject best = null;
        float bestD = searchRadius;
        foreach (var g in GameObject.FindGameObjectsWithTag(cushionTag))
        {
            if (IsHeld(g)) continue;
            float d = Vector3.Distance(Bottom(g), near);
            if (d < bestD) { bestD = d; best = g; }
        }
        return best;
    }

    // 11/02 D: 방석 프리팹은 벨트용 무마찰 재질(Belt_NoFriction)이라 파레트 위에서 계속 미끄러져 쌓은 단이 무너진다.
    //          팔2·3(적재함이 없는 팔)이 파레트에 놓은 방석은 마찰 재질로 바꾼다. 팔1은 벨트에 놓으므로 그대로 둔다
    private static PhysicsMaterial placedMaterial;

    static void SettleOnPallet(Rigidbody rb)
    {
        if (placedMaterial == null)
        {
            placedMaterial = new PhysicsMaterial("Cushion_OnPallet")
            {
                dynamicFriction = 0.8f,
                staticFriction = 0.9f,
                frictionCombine = PhysicsMaterialCombine.Maximum,   // 상대가 무마찰이어도 이 값을 쓴다
                bounciness = 0f,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }
        foreach (var c in rb.GetComponentsInChildren<Collider>()) c.sharedMaterial = placedMaterial;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    /// <summary>방석 바닥 중심 (콜라이더 기준, 없으면 transform 위치)</summary>
    static Vector3 Bottom(GameObject g)
    {
        var c = g.GetComponentInChildren<Collider>();
        return c != null ? new Vector3(c.bounds.center.x, c.bounds.min.y, c.bounds.center.z) : g.transform.position;
    }

    static bool IsHeld(GameObject g)
    {
        var rb = g.GetComponent<Rigidbody>();
        if (rb == null) return false;
        foreach (var k in FindObjectsByType<KinematicGripper>())
            if (k.Held == rb) return true;
        return false;
    }

    // ── 시험: Play 중 컴포넌트 우클릭 ──
    [Header("시험 (Play 중 우클릭 메뉴)")]
    [SerializeField] private string testCushionId = "";
    [SerializeField] private Vector3 testPickBottom;
    [SerializeField] private Vector3 testPlaceBottom;
    [SerializeField] private float testPlaceYaw;

    [Tooltip("팔1 투입 시험의 놓을 위치 (line_coords.yaml feed_point, Unity 좌표)")]
    [SerializeField] private Vector3 testFeedPoint = new Vector3(1.5f, 0.625f, -0.5f);

    [ContextMenu("시험: 팔1 투입 1회 (적재함 다음 칸 → 투입 위치)")]
    void TestFeed()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Play 중에만 동작해", this); return; }
        if (feedBin == null) { Debug.LogWarning($"{Tag} Feed Bin(적재함)이 연결되지 않음. Tools/INDEX/로봇팔1 만들기", this); return; }
        Run("", Vector3.zero, testFeedPoint, 0f,
            (ok, msg) => Debug.Log($"{Tag} 투입 시험 결과: success={ok}, message={msg}, 적재함 남은 {feedBin.Count}", this));
    }

    [ContextMenu("시험: 집기·놓기 1회 (시험 칸 값)")]
    void TestRun()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Play 중에만 동작해", this); return; }
        Run(testCushionId, testPickBottom, testPlaceBottom, testPlaceYaw,
            (ok, msg) => Debug.Log($"{Tag} 시험 결과: success={ok}, message={msg}", this));
    }
}
