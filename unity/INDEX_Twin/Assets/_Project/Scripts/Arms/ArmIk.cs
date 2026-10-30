// ArmIk.cs : 6축 팔 역기구학 (Unity IK, SRS 2.5). 담당 D · scope unity-arm
// ArmRig의 ArticulationBody 관절 6개와 ToolPoint만 보고 계산한다. 팔을 움직이지는 않는다.
//
// 정기구학 (Product of Exponentials)
//   관절이 모두 0일 때 각 관절의 회전축(앵커 X축)과 축 위의 점, ToolPoint 자세를 베이스(base_link) 기준으로 구해 둔다.
//   관절값 q가 주어지면 끝 관절부터 첫 관절 순서로 그 축 둘레로 돌린다.
// 역기구학 (감쇠 최소제곱, DLS)
//   목표 ToolPoint 위치·방향과의 오차를 자코비안(수치 미분)으로 줄여 간다. 관절 한계 안으로 자르고,
//   시작값(seed) 근처의 해를 찾는다. 시작값을 여러 개 주면 수렴한 해 중 첫 시작값에 가장 가까운 것을 고른다.

using UnityEngine;

public class ArmIk
{
    /// <summary>ArticulationBody 관절값(+) 방향과 앵커 X축 회전 방향의 관계. Play 시험으로 정한 값</summary>
    public static float JointSign = 1f;

    public int MaxIterations = 300;
    public float PosTolerance = 0.002f;        // m
    public float RotToleranceDeg = 0.5f;       // 도
    public float RotWeight = 0.3f;             // 방향 오차 1 rad를 위치 몇 m로 볼지
    public float Damping = 0.05f;              // DLS λ
    public float MaxStepRad = 0.2f;            // 한 번에 관절 하나가 움직이는 최대값

    public int JointCount => n;

    readonly Transform baseT;
    readonly int n;
    readonly Vector3[] axis;
    readonly Vector3[] pivot;
    readonly float[] lo, hi;
    readonly bool[] limited;
    readonly Vector3 toolPos0;
    readonly Quaternion toolRot0;

    public ArmIk(ArticulationBody[] joints, Transform toolPoint)
    {
        n = joints.Length;
        axis = new Vector3[n];
        pivot = new Vector3[n];
        lo = new float[n];
        hi = new float[n];
        limited = new bool[n];
        baseT = joints[0].transform.parent;

        // 관절 0일 때의 사슬을 앵커 정보로 계산 (지금 자세와 무관)
        Vector3 p = Vector3.zero;
        Quaternion r = Quaternion.identity;
        for (int i = 0; i < n; i++)
        {
            var j = joints[i];
            Vector3 pj = p + r * j.parentAnchorPosition;
            Quaternion rj = r * j.parentAnchorRotation;
            axis[i] = (rj * Vector3.right).normalized;
            pivot[i] = pj;

            r = rj * Quaternion.Inverse(j.anchorRotation);
            p = pj - r * j.anchorPosition;

            var d = j.xDrive;
            limited[i] = j.twistLock == ArticulationDofLock.LimitedMotion && d.upperLimit > d.lowerLimit;
            lo[i] = d.lowerLimit * Mathf.Deg2Rad;
            hi[i] = d.upperLimit * Mathf.Deg2Rad;
        }

        // ToolPoint는 마지막 관절 링크에 고정
        var last = joints[n - 1].transform;
        Vector3 relPos = last.InverseTransformPoint(toolPoint.position);
        Quaternion relRot = Quaternion.Inverse(last.rotation) * toolPoint.rotation;
        toolPos0 = p + r * relPos;
        toolRot0 = r * relRot;
    }

    /// <summary>관절값(rad) → ToolPoint 월드 위치·방향</summary>
    public void ForwardWorld(float[] q, out Vector3 pos, out Quaternion rot)
    {
        Fk(q, out pos, out rot);
        pos = baseT.TransformPoint(pos);
        rot = baseT.rotation * rot;
    }

    void Fk(float[] q, out Vector3 pos, out Quaternion rot)
    {
        pos = toolPos0;
        rot = toolRot0;
        for (int i = n - 1; i >= 0; i--)
        {
            var rr = Quaternion.AngleAxis(JointSign * q[i] * Mathf.Rad2Deg, axis[i]);
            pos = pivot[i] + rr * (pos - pivot[i]);
            rot = rr * rot;
        }
    }

    /// <summary>
    /// 목표 ToolPoint 월드 자세의 관절값을 찾는다. seeds 중 수렴한 해 가운데 seeds[0]에 가장 가까운 것을 result에 넣는다.
    /// 못 찾으면 false, 가장 오차가 작았던 해와 그 오차를 돌려준다.
    /// </summary>
    public bool Solve(Vector3 targetPos, Quaternion targetRot, float[][] seeds, float[] result, out float posErr, out float rotErrDeg)
    {
        Vector3 tp = baseT.InverseTransformPoint(targetPos);
        Quaternion tr = Quaternion.Inverse(baseT.rotation) * targetRot;

        bool found = false;
        float bestDist = float.MaxValue, bestErr = float.MaxValue;
        posErr = float.MaxValue;
        rotErrDeg = float.MaxValue;
        var q = new float[n];

        foreach (var seed in seeds)
        {
            if (seed == null || seed.Length != n) continue;
            System.Array.Copy(seed, q, n);
            bool ok = Iterate(tp, tr, q, out float pe, out float re);
            float dist = 0f;
            for (int i = 0; i < n; i++) dist += Mathf.Abs(q[i] - seeds[0][i]);

            if (ok && (!found || dist < bestDist))
            {
                found = true;
                bestDist = dist;
                System.Array.Copy(q, result, n);
                posErr = pe;
                rotErrDeg = re;
            }
            else if (!found && pe + re * 0.01f < bestErr)
            {
                bestErr = pe + re * 0.01f;
                System.Array.Copy(q, result, n);
                posErr = pe;
                rotErrDeg = re;
            }
        }
        return found;
    }

    bool Iterate(Vector3 tp, Quaternion tr, float[] q, out float posErr, out float rotErrDeg)
    {
        var e = new float[6];
        var J = new float[6, n];
        var qh = new float[n];
        const float h = 1e-4f;

        for (int it = 0; it < MaxIterations; it++)
        {
            Fk(q, out Vector3 p, out Quaternion r);
            Vector3 ep = tp - p;
            Vector3 er = RotVec(tr * Quaternion.Inverse(r));
            posErr = ep.magnitude;
            rotErrDeg = er.magnitude * Mathf.Rad2Deg;
            if (posErr < PosTolerance && rotErrDeg < RotToleranceDeg) return true;

            e[0] = ep.x; e[1] = ep.y; e[2] = ep.z;
            e[3] = er.x * RotWeight; e[4] = er.y * RotWeight; e[5] = er.z * RotWeight;

            for (int j = 0; j < n; j++)
            {
                System.Array.Copy(q, qh, n);
                qh[j] += h;
                Fk(qh, out Vector3 p2, out Quaternion r2);
                Vector3 dp = (p2 - p) / h;
                Vector3 dr = RotVec(r2 * Quaternion.Inverse(r)) / h;
                J[0, j] = dp.x; J[1, j] = dp.y; J[2, j] = dp.z;
                J[3, j] = dr.x * RotWeight; J[4, j] = dr.y * RotWeight; J[5, j] = dr.z * RotWeight;
            }

            // dq = Jᵀ (J Jᵀ + λ²I)⁻¹ e
            var A = new float[6, 6];
            for (int a = 0; a < 6; a++)
                for (int b = 0; b < 6; b++)
                {
                    float s = 0f;
                    for (int j = 0; j < n; j++) s += J[a, j] * J[b, j];
                    A[a, b] = s + (a == b ? Damping * Damping : 0f);
                }
            var y = SolveLinear(A, e);
            if (y == null) break;

            for (int j = 0; j < n; j++)
            {
                float dq = 0f;
                for (int a = 0; a < 6; a++) dq += J[a, j] * y[a];
                q[j] += Mathf.Clamp(dq, -MaxStepRad, MaxStepRad);
                if (limited[j]) q[j] = Mathf.Clamp(q[j], lo[j], hi[j]);
            }
        }

        Fk(q, out Vector3 pf, out Quaternion rf);
        posErr = (tp - pf).magnitude;
        rotErrDeg = RotVec(tr * Quaternion.Inverse(rf)).magnitude * Mathf.Rad2Deg;
        return posErr < PosTolerance && rotErrDeg < RotToleranceDeg;
    }

    /// <summary>회전 → 회전 벡터 (축 × 각도, rad). 180°를 넘으면 반대 방향으로</summary>
    static Vector3 RotVec(Quaternion q)
    {
        if (q.w < 0f) { q.x = -q.x; q.y = -q.y; q.z = -q.z; q.w = -q.w; }
        Vector3 v = new Vector3(q.x, q.y, q.z);
        float s = v.magnitude;
        if (s < 1e-7f) return 2f * v;
        float angle = 2f * Mathf.Atan2(s, q.w);
        return v / s * angle;
    }

    /// <summary>6×6 가우스 소거 (부분 피벗)</summary>
    static float[] SolveLinear(float[,] A, float[] b)
    {
        const int N = 6;
        var M = new float[N, N + 1];
        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < N; j++) M[i, j] = A[i, j];
            M[i, N] = b[i];
        }
        for (int c = 0; c < N; c++)
        {
            int piv = c;
            for (int r = c + 1; r < N; r++) if (Mathf.Abs(M[r, c]) > Mathf.Abs(M[piv, c])) piv = r;
            if (Mathf.Abs(M[piv, c]) < 1e-9f) return null;
            if (piv != c) for (int k = c; k <= N; k++) { float t = M[c, k]; M[c, k] = M[piv, k]; M[piv, k] = t; }
            for (int r = c + 1; r < N; r++)
            {
                float f = M[r, c] / M[c, c];
                for (int k = c; k <= N; k++) M[r, k] -= f * M[c, k];
            }
        }
        var x = new float[N];
        for (int i = N - 1; i >= 0; i--)
        {
            float s = M[i, N];
            for (int k = i + 1; k < N; k++) s -= M[i, k] * x[k];
            x[i] = s / M[i, i];
        }
        return x;
    }
}
