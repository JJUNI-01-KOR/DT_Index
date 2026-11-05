using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 이음새 없는 벨트 바닥 (ConveyorGroup이 있는 루트 "Conveyor"에 Play 시작 때 자동으로 붙는다. 직접 붙여도 됨). 담당 A · scope unity-line
///
/// 문제: 칸마다 바닥 BoxCollider가 따로 있으면, 높이가 똑같아도 PhysX가 칸 경계를 "모서리"로 보고
///       방석이 지나갈 때 걸리는 접촉을 만든다(ghost collision). 그래서 칸 경계에서 멈칫하거나 넘어진다.
/// 해결: Play 시작 때 자식 아래 칸 바닥들의 윗면을 모아 꼭짓점을 공유하는 메쉬 하나(MeshCollider)로 만든다.
///       한 메쉬 안에서 평평하게 이어진 경계는 PhysX가 모서리로 보지 않아서 걸리지 않는다.
///       원래 칸 바닥은 Trigger로 바꿔 Conveyor의 영역 계산에만 쓰게 둔다. 씬 파일은 바뀌지 않는다.
///
/// 대상 바닥: 이 오브젝트 아래의 Trigger가 아닌 콜라이더 중
///   - 윗면 높이가 floorTopY(자동이면 가장 많은 높이)와 heightTol 안
///   - 가로·세로가 minFootprint 이상 (벽·레일 같은 얇은 것 제외)
///   - 움직이는 Rigidbody(방석)가 아님
/// </summary>
[DefaultExecutionOrder(-50)]
public class BeltFloorMesh : MonoBehaviour
{
    [Tooltip("켜면 Play 시작 때 이음새 없는 바닥을 만든다")]
    [SerializeField] private bool build = true;

    [Tooltip("바닥 윗면 높이 (월드 y). 0이면 자동 (가장 많은 높이)")]
    [SerializeField] private float floorTopY = 0f;

    [Tooltip("이 높이 차이(m) 안이면 같은 바닥으로 본다")]
    [SerializeField] private float heightTol = 0.005f;

    [Tooltip("가로·세로가 이보다 작은 콜라이더는 바닥이 아님 (레일 제외)")]
    [SerializeField] private float minFootprint = 0.5f;

    private readonly List<Collider> sources = new List<Collider>();
    private GameObject floorGo;

    // 씬에 따로 안 붙여도 되게: Play 시작 때 ConveyorGroup이 있는 오브젝트에 자동으로 붙인다
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoAttach()
    {
        foreach (var g in FindObjectsByType<ConveyorGroup>())
            if (g.GetComponent<BeltFloorMesh>() == null) g.gameObject.AddComponent<BeltFloorMesh>();
    }

    void Start()
    {
        if (build) Build();
    }

    void OnDestroy()
    {
        if (floorGo != null) Destroy(floorGo);
    }

    [ContextMenu("다시 만들기 (Play 중)")]
    void Build()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Play 중에만 동작해", this); return; }
        if (floorGo != null) Destroy(floorGo);
        sources.Clear();

        // 1) 후보 바닥 찾기
        var cands = new List<Collider>();
        foreach (var c in GetComponentsInChildren<Collider>())
        {
            if (!c.enabled || c.isTrigger) continue;
            if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
            var b = c.bounds;
            if (b.size.x < minFootprint || b.size.z < minFootprint) continue;
            cands.Add(c);
        }
        if (cands.Count == 0) { Debug.LogWarning("[벨트 바닥] 바닥 후보 없음", this); return; }

        float top = floorTopY;
        if (Mathf.Approximately(top, 0f))
        {
            // 가장 많이 나오는 윗면 높이 (1 mm 단위)
            var hist = new Dictionary<int, int>();
            foreach (var c in cands)
            {
                int k = Mathf.RoundToInt(c.bounds.max.y * 1000f);
                hist[k] = hist.TryGetValue(k, out int n) ? n + 1 : 1;
            }
            int best = 0, bestN = -1;
            foreach (var kv in hist) if (kv.Value > bestN) { best = kv.Key; bestN = kv.Value; }
            top = best / 1000f;
        }
        foreach (var c in cands)
            if (Mathf.Abs(c.bounds.max.y - top) <= heightTol) sources.Add(c);

        // 2) 윗면 사각형들을 꼭짓점 공유(1 mm 단위로 합침) 메쉬로
        var verts = new List<Vector3>();
        var tris = new List<int>();
        var index = new Dictionary<long, int>();
        int V(float x, float z)
        {
            long kx = Mathf.RoundToInt(x * 1000f), kz = Mathf.RoundToInt(z * 1000f);
            long key = (kx << 32) ^ (kz & 0xffffffffL);
            if (!index.TryGetValue(key, out int i))
            {
                i = verts.Count;
                verts.Add(new Vector3(kx / 1000f, top, kz / 1000f));
                index[key] = i;
            }
            return i;
        }

        // 칸끼리 경계가 맞물리도록 모든 x, z 경계값으로 잘라서 넣는다 (T자 이음 방지)
        var xs = new SortedSet<int>();
        var zs = new SortedSet<int>();
        foreach (var c in sources)
        {
            var b = c.bounds;
            xs.Add(Mathf.RoundToInt(b.min.x * 1000f)); xs.Add(Mathf.RoundToInt(b.max.x * 1000f));
            zs.Add(Mathf.RoundToInt(b.min.z * 1000f)); zs.Add(Mathf.RoundToInt(b.max.z * 1000f));
        }
        var xa = new List<int>(xs);
        var za = new List<int>(zs);
        var filled = new HashSet<long>();

        foreach (var c in sources)
        {
            var b = c.bounds;
            int x0 = Mathf.RoundToInt(b.min.x * 1000f), x1 = Mathf.RoundToInt(b.max.x * 1000f);
            int z0 = Mathf.RoundToInt(b.min.z * 1000f), z1 = Mathf.RoundToInt(b.max.z * 1000f);
            for (int i = xa.IndexOf(x0); i >= 0 && i + 1 < xa.Count && xa[i] < x1; i++)
            for (int j = za.IndexOf(z0); j >= 0 && j + 1 < za.Count && za[j] < z1; j++)
            {
                long cellKey = ((long)i << 32) | (uint)j;
                if (!filled.Add(cellKey)) continue;            // 겹친 칸은 한 번만
                float ax = xa[i] / 1000f, bx = xa[i + 1] / 1000f;
                float az = za[j] / 1000f, bz = za[j + 1] / 1000f;
                int v00 = V(ax, az), v01 = V(ax, bz), v11 = V(bx, bz), v10 = V(bx, az);
                // 위에서 봤을 때 시계 방향 = 위를 보는 면
                tris.Add(v00); tris.Add(v01); tris.Add(v11);
                tris.Add(v00); tris.Add(v11); tris.Add(v10);
            }
        }

        var mesh = new Mesh { name = "BeltFloorMesh" };
        if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        floorGo = new GameObject("BeltFloorMesh (runtime)");     // 부모 없음 → Conveyor 칸으로 잡히지 않음
        floorGo.layer = gameObject.layer;
        var mc = floorGo.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.sharedMaterial = sources[0].sharedMaterial;

        // 3) 원래 칸 바닥은 Trigger로 (Conveyor 영역 계산은 Awake에서 이미 끝남)
        foreach (var c in sources) c.isTrigger = true;

        Debug.Log($"[벨트 바닥] 이음새 없는 바닥: 칸 바닥 {sources.Count}개 → 메쉬 1개 (삼각형 {tris.Count / 3}개, 윗면 y={top:F3})", this);
    }
}
