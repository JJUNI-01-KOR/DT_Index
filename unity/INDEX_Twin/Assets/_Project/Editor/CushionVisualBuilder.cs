// CushionVisualBuilder.cs : 방석 겉모양을 코드로 만든다 (실사풍). 담당 D · scope unity-line
// 메뉴 INDEX > Build Cushion Visual 을 누르면 아래를 새로 만들고 Prefabs/Line/CushionVisual.prefab 을 덮어쓴다.
//   _Project/Models/Cushion/CushionBody.asset    몸통 메시: 모서리가 둥근 판 + 위아래 솜 부풂 + 옆면 볼록 + 가운데 누빔 자국
//   _Project/Models/Cushion/CushionPiping.asset  둘레 파이핑(테두리 끈) 메시
//   _Project/Models/Cushion/Cushion_Weave_*.png  천 짜임 무늬(밝기)·노멀맵, 반복 무늬
//   _Project/Materials/Cushion_Fabric.mat, Cushion_Seam.mat  천 색·파이핑 색
// 크기는 40 × 40 × 10 cm (Cushion 프리팹 충돌 상자와 같음). 콜라이더는 없다 — 물리는 Cushion 프리팹의 BoxCollider가 한다.
// Cushion·Cushion 2 프리팹에는 "Visual" 자식으로 이 프리팹이 들어 있어서, 다시 만들면 자동으로 바뀐다.

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class CushionVisualBuilder
{
    const string Dir = "Assets/_Project/Models/Cushion";
    const string PrefabPath = "Assets/_Project/Prefabs/Line/CushionVisual.prefab";

    // 모양 (m)
    // 옆면 볼록·파이핑까지 합쳐 충돌 상자(±0.2, ±0.05) 안에 들어가게 잡은 값
    const float Hx = 0.186f, Hz = 0.186f; // 가로·세로 절반 (볼록 0.007 + 파이핑 0.008 → 바깥 0.201)
    const float Hy = 0.038f;              // 옆면 높이 절반 (솜 부풂 전)
    const float Round = 0.030f;           // 모서리 둥글기
    const float PuffTop = 0.012f;         // 윗면 부풂 (가장 높은 곳 약 0.049)
    const float PuffBottom = 0.010f;      // 아랫면 부풂 (바닥 약 -0.048, 떠 보이지 않게)
    const float SideBulge = 0.007f;       // 옆면 가운데 볼록
    const float TuftDepth = 0.011f;       // 가운데 누빔 자국 깊이
    const float TuftSigma = 0.028f;       // 누빔 자국 넓이
    const float PipingR = 0.0055f;        // 파이핑 굵기 (반지름)
    const float WeaveTile = 0.05f;        // 천 무늬 1장 = 5 cm
    const int Grid = 40;                  // 면당 격자 수 (메시 파일 약 1.4 MB. 올리면 더 매끈하지만 파일이 커진다)

    [MenuItem("INDEX/Build Cushion Visual")]
    public static void Build()
    {
        Directory.CreateDirectory(Dir);
        var (albedo, normal) = BuildWeaveTextures();
        var fabric = MakeMaterial("Assets/_Project/Materials/Cushion_Fabric.mat", new Color(0.80f, 0.50f, 0.31f), albedo, normal, 0.9f, 0.12f);
        var seam = MakeMaterial("Assets/_Project/Materials/Cushion_Seam.mat", new Color(0.36f, 0.22f, 0.14f), albedo, normal, 0.6f, 0.18f);

        var body = SaveMesh(BuildBody(), "CushionBody");
        var piping = SaveMesh(BuildPiping(), "CushionPiping");
        var sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");

        var root = new GameObject("CushionVisual");
        root.AddComponent<MeshFilter>().sharedMesh = body;
        root.AddComponent<MeshRenderer>().sharedMaterial = fabric;
        AddChild(root, "Piping", piping, seam, Vector3.zero, Vector3.one);
        float topDip = Hy + PuffTop * Puff(0f, 0f) - TuftDepth;
        float botDip = Hy + PuffBottom * Puff(0f, 0f) - TuftDepth * 0.6f;
        AddChild(root, "Button_Top", sphere, seam, new Vector3(0f, topDip + 0.002f, 0f), new Vector3(0.024f, 0.009f, 0.024f));
        AddChild(root, "Button_Bottom", sphere, seam, new Vector3(0f, -botDip - 0.002f, 0f), new Vector3(0.024f, 0.009f, 0.024f));

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[CushionVisualBuilder] 완료: {PrefabPath} (몸통 정점 {body.vertexCount}, 파이핑 정점 {piping.vertexCount})");
    }

    // ── 몸통 ─────────────────────────────────────────

    static float Puff(float x, float z)
    {
        float fx = 1f - Mathf.Pow(Mathf.Clamp01(Mathf.Abs(x) / Hx), 3f);
        float fz = 1f - Mathf.Pow(Mathf.Clamp01(Mathf.Abs(z) / Hz), 3f);
        return Mathf.Max(0f, fx) * Mathf.Max(0f, fz);
    }

    /// <summary>둥근 상자 위의 점(p, 바깥 방향 n)을 솜·볼록·누빔으로 움직인다</summary>
    static Vector3 Shape(Vector3 p, Vector3 n)
    {
        Vector3 q = p;
        // 옆면 볼록: 각 변의 가운데가 가장 많이, 모서리와 위아래 끝은 거의 안 나옴
        if (Mathf.Abs(n.x) + Mathf.Abs(n.z) > 1e-4f)
        {
            float along = Mathf.Abs(n.x) >= Mathf.Abs(n.z) ? p.z / Hz : p.x / Hx;
            float yf = Mathf.Clamp01(1f - (p.y / Hy) * (p.y / Hy));
            q += new Vector3(n.x, 0f, n.z) * (SideBulge * (1f - along * along) * yf);
        }
        // 위·아래 솜
        float f = Puff(p.x, p.z);
        float r2 = p.x * p.x + p.z * p.z;
        float tuft = Mathf.Exp(-r2 / (2f * TuftSigma * TuftSigma));
        if (n.y > 0f)
        {
            float w = Mathf.SmoothStep(0f, 1f, n.y);
            q.y += (PuffTop * f - TuftDepth * tuft) * w;
        }
        else if (n.y < 0f)
        {
            float w = Mathf.SmoothStep(0f, 1f, -n.y);
            q.y -= (PuffBottom * f - TuftDepth * 0.6f * tuft) * w;
        }
        return q;
    }

    static Mesh BuildBody()
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        var h = new Vector3(Hx, Hy, Hz);
        var inner = new Vector3(Hx - Round, Hy - Round, Hz - Round);

        // 면 6개: (바깥 축, u 축, v 축)
        var faces = new (Vector3 d, Vector3 a, Vector3 b)[]
        {
            (Vector3.up, Vector3.right, Vector3.forward), (Vector3.down, Vector3.forward, Vector3.right),
            (Vector3.right, Vector3.forward, Vector3.up), (Vector3.left, Vector3.up, Vector3.forward),
            (Vector3.forward, Vector3.up, Vector3.right), (Vector3.back, Vector3.right, Vector3.up),
        };
        foreach (var (d, a, b) in faces)
        {
            int start = verts.Count;
            for (int j = 0; j <= Grid; j++)
            for (int i = 0; i <= Grid; i++)
            {
                float u = -1f + 2f * i / Grid, v = -1f + 2f * j / Grid;
                Vector3 p = Vector3.Scale(d + a * u + b * v, h);
                Vector3 c = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                Vector3 n = (p - c).normalized;
                Vector3 s = c + n * Round;
                verts.Add(Shape(s, n));
                // 천 무늬: 면마다 평면 투영 (위아래 x·z, 옆면은 둘레 방향·높이)
                Vector2 uv = Mathf.Abs(d.y) > 0.5f ? new Vector2(s.x, s.z)
                           : Mathf.Abs(d.x) > 0.5f ? new Vector2(s.z, s.y) : new Vector2(s.x, s.y);
                uvs.Add(uv / WeaveTile);
            }
            for (int j = 0; j < Grid; j++)
            for (int i = 0; i < Grid; i++)
            {
                int k = start + j * (Grid + 1) + i;
                tris.Add(k); tris.Add(k + Grid + 1); tris.Add(k + 1);
                tris.Add(k + 1); tris.Add(k + Grid + 1); tris.Add(k + Grid + 2);
            }
        }
        var mesh = new Mesh { name = "CushionBody", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        WeldNormals(mesh);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>면 경계에서 겹친 정점의 법선을 평균 → 모서리 이음새가 안 보이게</summary>
    static void WeldNormals(Mesh mesh)
    {
        var v = mesh.vertices; var n = mesh.normals;
        var sum = new Dictionary<Vector3Int, Vector3>();
        System.Func<Vector3, Vector3Int> key = p => new Vector3Int(Mathf.RoundToInt(p.x * 1e5f), Mathf.RoundToInt(p.y * 1e5f), Mathf.RoundToInt(p.z * 1e5f));
        for (int i = 0; i < v.Length; i++) { var k = key(v[i]); sum[k] = (sum.TryGetValue(k, out var s) ? s : Vector3.zero) + n[i]; }
        for (int i = 0; i < v.Length; i++) n[i] = sum[key(v[i])].normalized;
        mesh.normals = n;
    }

    // ── 파이핑: 옆면 가운데 둘레를 도는 둥근 끈 ──────────────

    static Mesh BuildPiping()
    {
        const int along = 240, ring = 10;
        var path = new List<(Vector3 p, Vector3 n)>();
        float ix = Hx - Round, iz = Hz - Round;
        // 둥근 사각형 둘레를 일정 간격으로
        float straightX = 2f * ix, straightZ = 2f * iz, arc = 0.5f * Mathf.PI * Round;
        float per = 2f * (straightX + straightZ) + 4f * arc;
        for (int s = 0; s < along; s++)
        {
            float t = per * s / along;
            Vector3 c, n;
            PerimeterPoint(t, ix, iz, straightX, straightZ, arc, out c, out n);
            Vector3 surf = Shape(c + n * Round, n);            // 몸통 겉면 (y = 0)
            path.Add((surf + n * (PipingR * 0.45f), n));
        }
        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        for (int s = 0; s <= along; s++)
        {
            var (p, n) = path[s % along];
            for (int r = 0; r <= ring; r++)
            {
                float a = 2f * Mathf.PI * r / ring;
                verts.Add(p + (n * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)) * PipingR);
                uvs.Add(new Vector2(per * s / along / (WeaveTile * 0.4f), (float)r / ring * 0.35f));
            }
        }
        for (int s = 0; s < along; s++)
        for (int r = 0; r < ring; r++)
        {
            int k = s * (ring + 1) + r, k2 = k + ring + 1;
            tris.Add(k); tris.Add(k + 1); tris.Add(k2);
            tris.Add(k + 1); tris.Add(k2 + 1); tris.Add(k2);
        }
        var mesh = new Mesh { name = "CushionPiping" };
        mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); WeldNormals(mesh); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>안쪽 사각형(±ix, ±iz) 둘레를 따라 t만큼 간 점(모서리 원의 중심 쪽)과 바깥 방향</summary>
    static void PerimeterPoint(float t, float ix, float iz, float sx, float sz, float arc, out Vector3 c, out Vector3 n)
    {
        // 순서: +x 변(-z→+z), 모서리, +z 변(+x→-x), 모서리, -x 변, 모서리, -z 변, 모서리
        var segs = new (float len, int kind, int side)[] { (sz, 0, 0), (arc, 1, 0), (sx, 0, 1), (arc, 1, 1), (sz, 0, 2), (arc, 1, 2), (sx, 0, 3), (arc, 1, 3) };
        foreach (var (len, kind, side) in segs)
        {
            if (t > len) { t -= len; continue; }
            float f = len > 0f ? t / len : 0f;
            if (kind == 0)
            {
                switch (side)
                {
                    case 0: c = new Vector3(ix, 0f, Mathf.Lerp(-iz, iz, f)); n = Vector3.right; return;
                    case 1: c = new Vector3(Mathf.Lerp(ix, -ix, f), 0f, iz); n = Vector3.forward; return;
                    case 2: c = new Vector3(-ix, 0f, Mathf.Lerp(iz, -iz, f)); n = Vector3.left; return;
                    default: c = new Vector3(Mathf.Lerp(-ix, ix, f), 0f, -iz); n = Vector3.back; return;
                }
            }
            float a0 = side * 0.5f * Mathf.PI;                   // 0: +x→+z, 1: +z→-x, 2: -x→-z, 3: -z→+x
            float ang = a0 + f * 0.5f * Mathf.PI;
            n = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            float cxs = side == 0 || side == 3 ? ix : -ix, czs = side == 0 || side == 1 ? iz : -iz;
            c = new Vector3(cxs, 0f, czs);
            return;
        }
        c = new Vector3(ix, 0f, -iz); n = Vector3.right;
    }

    // ── 천 짜임 텍스처 (반복되는 평직 무늬) ─────────────────

    static (Texture2D albedo, Texture2D normal) BuildWeaveTextures()
    {
        const int size = 512, period = 16;                    // 512 px에 실 32가닥
        var height = new float[size * size];
        var tone = new float[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (float)x / period, v = (float)y / period;
            int cu = Mathf.FloorToInt(u), cv = Mathf.FloorToInt(v);
            float fu = u - cu, fv = v - cv;
            bool warpOver = ((cu + cv) & 1) == 0;
            float hw = Mathf.Sin(Mathf.PI * Mathf.Clamp01((fu - 0.08f) / 0.84f));   // 세로 실 단면
            float hf = Mathf.Sin(Mathf.PI * Mathf.Clamp01((fv - 0.08f) / 0.84f));   // 가로 실 단면
            float h = warpOver ? hw * (0.65f + 0.35f * Mathf.Sin(Mathf.PI * fv)) : hf * (0.65f + 0.35f * Mathf.Sin(Mathf.PI * fu));
            float fiber = (Hash(x, y) - 0.5f) * 0.12f;                           // 실 보풀
            float thread = (Hash(warpOver ? cu : 1000 + cv, 7) - 0.5f) * 0.10f;  // 실마다 색 차이
            height[y * size + x] = Mathf.Clamp01(h + fiber * 0.5f);
            tone[y * size + x] = Mathf.Clamp01(0.80f + 0.20f * h + fiber + thread);
        }
        var albedo = new Texture2D(size, size, TextureFormat.RGB24, true);
        var nrm = new Texture2D(size, size, TextureFormat.RGB24, true);
        const float strength = 3.0f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float t = tone[y * size + x];
            albedo.SetPixel(x, y, new Color(t, t, t));
            float dx = height[y * size + (x + 1) % size] - height[y * size + (x - 1 + size) % size];
            float dy = height[((y + 1) % size) * size + x] - height[((y - 1 + size) % size) * size + x];
            var n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
            nrm.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f));
        }
        string aPath = Dir + "/Cushion_Weave_Albedo.png", nPath = Dir + "/Cushion_Weave_Normal.png";
        File.WriteAllBytes(aPath, albedo.EncodeToPNG());
        File.WriteAllBytes(nPath, nrm.EncodeToPNG());
        Object.DestroyImmediate(albedo); Object.DestroyImmediate(nrm);
        AssetDatabase.ImportAsset(aPath); AssetDatabase.ImportAsset(nPath);
        var ni = (TextureImporter)AssetImporter.GetAtPath(nPath);
        ni.textureType = TextureImporterType.NormalMap; ni.wrapMode = TextureWrapMode.Repeat; ni.anisoLevel = 4; ni.SaveAndReimport();
        var ai = (TextureImporter)AssetImporter.GetAtPath(aPath);
        ai.wrapMode = TextureWrapMode.Repeat; ai.anisoLevel = 4; ai.SaveAndReimport();
        return (AssetDatabase.LoadAssetAtPath<Texture2D>(aPath), AssetDatabase.LoadAssetAtPath<Texture2D>(nPath));
    }

    static float Hash(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
    }

    // ── 공통 ─────────────────────────────────────────

    static Material MakeMaterial(string path, Color tint, Texture2D albedo, Texture2D normal, float bump, float smooth)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", tint);
        m.SetTexture("_BaseMap", albedo);
        m.SetTexture("_BumpMap", normal);
        m.SetFloat("_BumpScale", bump);
        m.EnableKeyword("_NORMALMAP");
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Mesh SaveMesh(Mesh mesh, string name)
    {
        string path = $"{Dir}/{name}.asset";
        var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (old != null)
        {
            // 같은 파일(GUID)을 유지하며 내용만 바꾼다 (CopySerialized는 정점 수가 바뀌면 깨짐)
            old.Clear();
            old.indexFormat = mesh.indexFormat;
            old.SetVertices(mesh.vertices);
            old.SetNormals(mesh.normals);
            old.SetTangents(mesh.tangents);
            old.SetUVs(0, mesh.uv);
            old.SetTriangles(mesh.triangles, 0);
            old.RecalculateBounds();
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(old);
            return old;
        }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static void AddChild(GameObject root, string name, Mesh mesh, Material mat, Vector3 pos, Vector3 scale)
    {
        var g = new GameObject(name);
        g.transform.SetParent(root.transform, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }
}
