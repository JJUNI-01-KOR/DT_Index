// FactoryShellBuilder.cs : Area 크기에 맞춰 공장 건물(벽·기둥·지붕 트러스·조명·셔터 등)을 만든다. 담당 A · scope scene (11/03 A)
// 메뉴 INDEX > Factory > Build Shell + Props : 만들기 (다시 누르면 지우고 새로 만듦)
//      INDEX > Factory > Remove Shell          : 지우기
//
// - 기존 오브젝트는 하나도 건드리지 않는다. 새 루트 "FactoryShell" 하나만 만든다.
// - 크기: 씬의 Environment/Area 아래 Renderer를 모두 합친 범위 + margin. 못 찾으면 기본값(11/03 Main 씬 Area 값).
// - 결과는 프리팹(Assets/_Project/Prefabs/Environment/FactoryShell.prefab)으로 저장하고 씬에는 그 인스턴스 하나만 둔다
//   → Main.unity에는 프리팹 인스턴스 몇 줄만 바뀌어 팀원과 씬 충돌이 적다.
// - 콜라이더 없음 (AGV·방석·클릭 판정에 영향 없음). 그림자도 끔 (벽 그림자가 라인을 어둡게 만들지 않게).
// - 카메라 쪽 벽은 Play 중 FactoryWallCutaway가 자동으로 숨긴다 (밖에서 안을 내려다보는 시점이라).
// - 재질은 Assets/_Project/Materials/Factory/ 에 만든다 (URP Lit, 텍스처 없음).
// - 11/03 A (2차): 층고 = 기본 카메라 시점에서 먼 쪽 벽이 화면 위쪽을 빈틈없이 채우는 높이 (최소 12 m).
//   지붕(트러스·천장 등)은 카메라가 공장 밖에 있거나 지붕보다 높으면 FactoryWallCutaway가 숨긴다 → 위에서 볼 때 천장이 안 보임.
//   카메라는 메뉴 INDEX > Factory > Fit Camera 로 먼저 맞춘다 (FactoryCameraFit). 부가 소품은 FactoryProps가 같이 만든다.
// - 메뉴는 INDEX > Factory 아래로 모았다. Build All = 카메라 맞추기 → 건물·소품 → 최종 구역 꾸미기

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class FactoryShellBuilder
{
    const string RootName = "FactoryShell";
    const string MatDir = "Assets/_Project/Materials/Factory";
    const string PrefabDir = "Assets/_Project/Prefabs/Environment";
    const string PrefabPath = PrefabDir + "/FactoryShell.prefab";

    // ── 치수 (m) ──
    internal const float Margin = 3.5f;   // Area 끝선 → 벽 안쪽 면 (소품·통로 자리. 바닥판 60 m 안에 들게)
    const float MinWallH = 12f;           // 최소 층고
    internal static float WallH = 12f;    // 벽 높이 = 트러스 아래면 (Build 때 계산)
    internal const float WallT = 0.25f;   // 벽 두께
    const float PlinthH = 1.2f;       // 아래 콘크리트 띠
    const float WindowH = 0.9f;       // 위쪽 창 띠 높이
    const float CapH = 0.35f;         // 맨 위 마감
    const float RibPitch = 1.0f;      // 골강판 세로 골 간격
    const float BayPitch = 6.0f;      // 기둥 간격 (목표값, 길이에 맞춰 조정)
    internal const float ColW = 0.36f;    // H형강 폭
    const float TrussH = 1.4f;        // 지붕 트러스 높이
    const float LampDrop = 1.2f;      // 천장 등: 트러스 아래면에서 내려온 길이
    const float DoorW = 5.0f, DoorH = 5.0f;   // 큰 셔터

    // Area를 못 찾을 때 (11/03 Main 씬 Work Area 6칸 범위)
    static readonly Rect DefaultArea = Rect.MinMaxRect(-25.9f, -12.15f, 9.2f, 11.19f);

    internal static Material mConcrete, mPanel, mRib, mGlass, mCap, mSteel, mTruss, mLampBody, mLampGlow,
                    mDoor, mDoorFrame, mDoorStripe, mPipeRed, mPipeGray, mExt, mSign, mGround;

    [MenuItem("INDEX/Factory/Build All (Camera + Shell + Final Zone)", priority = 0)]
    public static void BuildAll()
    {
        FactoryCameraFit.Fit();
        Build();
        FinalZoneDecorBuilder.Build();
    }

    [MenuItem("INDEX/Factory/Build Shell + Props", priority = 20)]
    public static void Build()
    {
        Rect area = WorkRect(out string from);
        MakeMaterials();
        Remove(false);

        float x0 = area.xMin - Margin, x1 = area.xMax + Margin;
        float z0 = area.yMin - Margin, z1 = area.yMax + Margin;
        string camFrom;
        float fill = FarWallFillHeight(new Rect(x0, z0, x1 - x0, z1 - z0), out camFrom);
        WallH = Mathf.Max(MinWallH, Mathf.Ceil(fill + 1f));

        var root = new GameObject(RootName);
        // 건물 밖 땅 (바닥판 60 m 밖이 허공으로 보이지 않게). 기존 바닥보다 2 cm 아래
        Prim(root, PrimitiveType.Cube, "Ground", new Vector3((x0 + x1) * 0.5f, -0.03f, (z0 + z1) * 0.5f),
             new Vector3(160f, 0.02f, 160f), mGround);
        var cut = root.AddComponent<FactoryWallCutaway>();

        // ── 벽 4면 (안쪽 면이 x0·x1·z0·z1에 오도록) ──
        var south = Wall(root, "Wall_S", new Vector3((x0 + x1) * 0.5f, 0f, z0 - WallT * 0.5f), x1 - x0 + WallT * 2f, Vector3.back);
        var north = Wall(root, "Wall_N", new Vector3((x0 + x1) * 0.5f, 0f, z1 + WallT * 0.5f), x1 - x0 + WallT * 2f, Vector3.forward);
        var west = Wall(root, "Wall_W", new Vector3(x0 - WallT * 0.5f, 0f, (z0 + z1) * 0.5f), z1 - z0, Vector3.left);
        var east = Wall(root, "Wall_E", new Vector3(x1 + WallT * 0.5f, 0f, (z0 + z1) * 0.5f), z1 - z0, Vector3.right);

        // ── 기둥 (벽 안쪽에 붙임). 각 벽 그룹에 넣어 벽과 같이 숨긴다 ──
        var xs = Stations(x0, x1, BayPitch);
        var zs = Stations(z0, z1, BayPitch);
        foreach (float x in xs)
        {
            Column(south, new Vector3(x, 0f, z0 + ColW * 0.5f), 0f);
            Column(north, new Vector3(x, 0f, z1 - ColW * 0.5f), 0f);
        }
        for (int i = 1; i < zs.Count - 1; i++)
        {
            Column(west, new Vector3(x0 + ColW * 0.5f, 0f, zs[i]), 90f);
            Column(east, new Vector3(x1 - ColW * 0.5f, 0f, zs[i]), 90f);
        }

        // ── 큰 셔터(서쪽 가운데), 사람 문(남쪽), 배관(북쪽), 소화기(기둥) ──
        Shutter(west, new Vector3(x0 + 0.02f, 0f, (z0 + z1) * 0.5f), Vector3.right);
        PersonDoor(south, new Vector3(x0 + 4f, 0f, z0 + 0.02f), Vector3.forward);
        Pipes(north, x0, x1, z1 - 0.45f);
        for (int i = 1; i < xs.Count - 1; i += 2) Extinguisher(north, new Vector3(xs[i] + 0.3f, 0f, z1 - ColW - 0.12f));
        for (int i = 1; i < xs.Count - 1; i += 2) Extinguisher(south, new Vector3(xs[i] + 0.3f, 0f, z0 + ColW + 0.12f));

        // ── 지붕 트러스(남북 방향, 기둥 줄마다) + 중도리 + 천장 등 ──
        var roof = Group(root, "Roof");
        foreach (float x in xs) Truss(roof, x, z0, z1);
        for (int k = 0; k <= 4; k++)
        {
            float z = Mathf.Lerp(z0, z1, k / 4f);
            Box(roof, "Purlin", new Vector3((x0 + x1) * 0.5f, WallH + TrussH * (1f - Mathf.Abs(k - 2) / 2f) + 0.05f, z),
                new Vector3(x1 - x0, 0.12f, 0.12f), mTruss);
        }
        var lamps = Group(roof, "Lamps");   // 지붕과 같이 숨김
        for (int i = 0; i < xs.Count - 1; i++)
            for (int k = 0; k < zs.Count - 1; k++)
                Lamp(lamps, new Vector3((xs[i] + xs[i + 1]) * 0.5f, WallH - LampDrop, (zs[k] + zs[k + 1]) * 0.5f));

        // ── 부가 소품 (FactoryProps): 벽 소품은 벽 그룹에, 바닥 소품은 Props 그룹에 ──
        FactoryProps.Build(root, south, north, west, east, new Rect(x0, z0, x1 - x0, z1 - z0), area, xs, zs);

        cut.Setup(new[] { south, north, west, east },
                  new[] { Vector3.back, Vector3.forward, Vector3.left, Vector3.right },
                  roof.transform, WallH, new Rect(x0, z0, x1 - x0, z1 - z0));

        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
        }

        // 프리팹으로 저장하고 씬에는 인스턴스로 둔다
        EnsureFolder(PrefabDir);
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
        Undo.RegisterCreatedObjectUndo(root, "Build Factory Shell");
        Selection.activeGameObject = root;

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log($"[FactoryShellBuilder] 공장 건물 만듦 ({from}): 안쪽 {x1 - x0:F1} × {z1 - z0:F1} m, 층고 {WallH:F1} m (먼 벽 채움 {fill:F1} m, {camFrom}), " +
                  $"기둥 {xs.Count * 2 + (zs.Count - 2) * 2}개, 등 {(xs.Count - 1) * (zs.Count - 1)}개. 씬을 저장하세요 (Ctrl+S)");
    }

    [MenuItem("INDEX/Factory/Remove Shell", priority = 40)]
    public static void RemoveMenu() => Remove(true);

    static void Remove(bool log)
    {
        int n = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null || t.parent != null || t.name != RootName) continue;
            Undo.DestroyObjectImmediate(t.gameObject);
            n++;
        }
        if (n > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        if (log) Debug.Log(n > 0 ? "[FactoryShellBuilder] 공장 건물 지움" : "[FactoryShellBuilder] 지울 공장 건물 없음");
    }

    /// <summary>기본 카메라 시점에서 화면 맨 위 광선이 먼 쪽 벽에 닿는 높이.
    /// 벽이 이보다 높으면 화면 위쪽이 허공 대신 벽으로 채워진다</summary>
    static float FarWallFillHeight(Rect inside, out string from)
    {
        if (!FactoryCameraFit.TryGetView(out Vector3 camPos, out Quaternion rot, out float fov))
        {
            from = "카메라 못 찾음";
            return MinWallH;
        }
        from = $"카메라 높이 {camPos.y:F1} m";
        Vector3 top = rot * Quaternion.Euler(-fov * 0.5f, 0f, 0f) * Vector3.forward;   // 화면 맨 위 광선
        float best = 0f;
        // 먼 벽 4개 평면 중 광선이 실내 쪽에서 닿는 곳의 높이
        foreach (var (n, d) in new[] { (Vector3.forward, inside.yMax), (Vector3.back, -inside.yMin), (Vector3.right, inside.xMax), (Vector3.left, -inside.xMin) })
        {
            float dn = Vector3.Dot(top, n);
            if (dn <= 1e-4f) continue;                              // 그 벽 쪽으로 안 감
            float t = (d - Vector3.Dot(camPos, n)) / dn;
            if (t <= 0f) continue;
            Vector3 hit = camPos + top * t;
            if (hit.x < inside.xMin - 0.5f || hit.x > inside.xMax + 0.5f || hit.z < inside.yMin - 0.5f || hit.z > inside.yMax + 0.5f) continue;
            best = Mathf.Max(best, hit.y);
        }
        return best;
    }

    /// <summary>Area + 끝에 걸친 장비 범위 (카메라 맞추기에도 쓴다)</summary>
    internal static Rect WorkRect(out string from) => IncludeEquipment(FindArea(out from));

    // ── Area 범위 ──
    static Rect FindArea(out string from)
    {
        var area = GameObject.Find("Environment/Area");
        if (area != null)
        {
            var rs = area.GetComponentsInChildren<Renderer>(true);
            if (rs.Length > 0)
            {
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                from = "Environment/Area";
                return Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
            }
        }
        from = "기본값 (Environment/Area 못 찾음)";
        Debug.LogWarning("[FactoryShellBuilder] Environment/Area를 못 찾아 기본 크기로 만듦");
        return DefaultArea;
    }

    /// <summary>Area 끝에 걸쳐 있는 장비(랙·팔·컨베이어 끝 등)가 벽에 묻히지 않게 범위를 넓힌다.
    /// Area에서 4 m 안에 있는 Renderer만 본다 (바닥판·하늘·멀리 있는 것 제외)</summary>
    static Rect IncludeEquipment(Rect area)
    {
        Rect r = area;
        var far = new Rect(area.xMin - 4f, area.yMin - 4f, area.width + 8f, area.height + 8f);
        foreach (var rd in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (rd == null || !rd.enabled || rd.GetComponentInParent<FactoryWallCutaway>() != null) continue;
            if (rd.transform.root.name == FinalZoneDecorBuilder.RootName) continue;
            if (rd.transform.root.name == "Environment" && rd.name == "Floor") continue;
            if (rd is ParticleSystemRenderer || rd.gameObject.layer == 5) continue;       // UI
            Bounds b = rd.bounds;
            if (b.size.x > 30f || b.size.z > 30f) continue;                              // 바닥판 같은 큰 것
            if (!far.Contains(new Vector2(b.center.x, b.center.z))) continue;
            r.xMin = Mathf.Min(r.xMin, b.min.x - 0.3f); r.xMax = Mathf.Max(r.xMax, b.max.x + 0.3f);
            r.yMin = Mathf.Min(r.yMin, b.min.z - 0.3f); r.yMax = Mathf.Max(r.yMax, b.max.z + 0.3f);
        }
        if (r != area)
            Debug.Log($"[FactoryShellBuilder] Area 밖으로 나온 장비가 있어 범위를 넓힘: X {r.xMin:F1}~{r.xMax:F1}, Z {r.yMin:F1}~{r.yMax:F1}");
        return r;
    }

    /// <summary>a~b를 pitch에 가깝게 같은 간격으로 나눈 점 (양 끝 포함)</summary>
    internal static List<float> Stations(float a, float b, float pitch)
    {
        int n = Mathf.Max(1, Mathf.RoundToInt((b - a) / pitch));
        var list = new List<float>();
        for (int i = 0; i <= n; i++) list.Add(Mathf.Lerp(a, b, (float)i / n));
        return list;
    }

    // ── 벽: 콘크리트 띠 + 골강판 + 창 띠 + 마감. normal = 바깥쪽 ──
    static GameObject Wall(GameObject root, string name, Vector3 center, float length, Vector3 normal)
    {
        var g = Group(root, name);
        g.transform.position = center;
        g.transform.rotation = Quaternion.LookRotation(normal, Vector3.up);   // 로컬 +Z = 바깥, 로컬 X = 벽 길이 방향
        float panelH = WallH - PlinthH - WindowH - CapH;
        LocalBox(g, "Plinth", new Vector3(0f, PlinthH * 0.5f, 0f), new Vector3(length, PlinthH, WallT), mConcrete);
        LocalBox(g, "Panel", new Vector3(0f, PlinthH + panelH * 0.5f, 0.03f), new Vector3(length, panelH, WallT - 0.06f), mPanel);
        float wy = PlinthH + panelH;
        LocalBox(g, "Window", new Vector3(0f, wy + WindowH * 0.5f, 0.05f), new Vector3(length, WindowH, WallT - 0.1f), mGlass);
        LocalBox(g, "Cap", new Vector3(0f, WallH - CapH * 0.5f, 0f), new Vector3(length, CapH, WallT + 0.04f), mCap);

        // 골강판 세로 골 (안쪽 면)과 창 문설주
        int ribs = Mathf.FloorToInt(length / RibPitch);
        for (int i = 1; i < ribs; i++)
        {
            float x = -length * 0.5f + i * (length / ribs);
            LocalBox(g, "Rib", new Vector3(x, PlinthH + panelH * 0.5f, -WallT * 0.5f - 0.015f), new Vector3(0.06f, panelH, 0.03f), mRib);
            if (i % 3 == 0) LocalBox(g, "Mullion", new Vector3(x, wy + WindowH * 0.5f, -WallT * 0.5f + 0.03f), new Vector3(0.08f, WindowH, 0.06f), mCap);
        }
        // 콘크리트 띠 위 걸레받이 선
        LocalBox(g, "Trim", new Vector3(0f, PlinthH, -WallT * 0.5f - 0.02f), new Vector3(length, 0.08f, 0.04f), mCap);
        return g;
    }

    // ── H형강 기둥 ──
    static void Column(GameObject parent, Vector3 pos, float yaw)
    {
        var g = Group(parent, "Column");
        g.transform.position = pos;
        g.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        float h = WallH + 0.2f;
        LocalBox(g, "Flange_A", new Vector3(-ColW * 0.5f + 0.02f, h * 0.5f, 0f), new Vector3(0.04f, h, ColW), mSteel);
        LocalBox(g, "Flange_B", new Vector3(ColW * 0.5f - 0.02f, h * 0.5f, 0f), new Vector3(0.04f, h, ColW), mSteel);
        LocalBox(g, "Web", new Vector3(0f, h * 0.5f, 0f), new Vector3(ColW, h, 0.03f), mSteel);
        LocalBox(g, "BasePlate", new Vector3(0f, 0.02f, 0f), new Vector3(ColW + 0.16f, 0.04f, ColW + 0.16f), mCap);
        // 기둥 아래 노랑 보호대
        LocalBox(g, "Guard", new Vector3(0f, 0.5f, 0f), new Vector3(ColW + 0.06f, 1.0f, ColW + 0.06f), mDoorFrame);
    }

    // ── 지붕 트러스 (남북 방향, 가운데가 높은 삼각) ──
    static void Truss(GameObject roof, float x, float z0, float z1)
    {
        var g = Group(roof, "Truss");
        float zc = (z0 + z1) * 0.5f, half = (z1 - z0) * 0.5f;
        float yb = WallH, yt = WallH + TrussH;
        Box(g, "Bottom", new Vector3(x, yb, zc), new Vector3(0.14f, 0.14f, z1 - z0), mTruss);
        // 위 현재 2개 (기울임)
        float slope = Mathf.Atan2(TrussH, half) * Mathf.Rad2Deg;
        float len = Mathf.Sqrt(half * half + TrussH * TrussH);
        var a = Box(g, "Top_S", new Vector3(x, (yb + yt) * 0.5f, zc - half * 0.5f), new Vector3(0.14f, 0.14f, len), mTruss);
        a.transform.rotation = Quaternion.Euler(-slope, 0f, 0f);
        var b = Box(g, "Top_N", new Vector3(x, (yb + yt) * 0.5f, zc + half * 0.5f), new Vector3(0.14f, 0.14f, len), mTruss);
        b.transform.rotation = Quaternion.Euler(slope, 0f, 0f);
        // 세로재
        int n = 6;
        for (int i = 1; i < n; i++)
        {
            float t = (float)i / n;
            float z = Mathf.Lerp(z0, z1, t);
            float h = TrussH * (1f - Mathf.Abs(t - 0.5f) * 2f);
            if (h > 0.05f) Box(g, "Post", new Vector3(x, yb + h * 0.5f, z), new Vector3(0.08f, h, 0.08f), mTruss);
        }
    }

    // ── 천장 등 (갓 + 발광면 + 매달린 줄) ──
    static void Lamp(GameObject parent, Vector3 pos)
    {
        var g = Group(parent, "Lamp");
        g.transform.position = pos;
        LocalBox(g, "Wire", new Vector3(0f, (WallH - pos.y) * 0.5f, 0f), new Vector3(0.02f, WallH - pos.y, 0.02f), mSteel);
        var body = Prim(g, PrimitiveType.Cylinder, "Body", Vector3.zero, new Vector3(0.7f, 0.06f, 0.7f), mLampBody);
        body.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        Prim(g, PrimitiveType.Cylinder, "Glow", new Vector3(0f, -0.015f, 0f), new Vector3(0.6f, 0.01f, 0.6f), mLampGlow);
    }

    // ── 큰 셔터 (벽 안쪽 면에 붙임). inward = 실내 쪽 ──
    static void Shutter(GameObject parent, Vector3 pos, Vector3 inward)
    {
        var g = Group(parent, "Shutter");
        g.transform.position = pos;
        g.transform.rotation = Quaternion.LookRotation(inward, Vector3.up);
        LocalBox(g, "Curtain", new Vector3(0f, DoorH * 0.5f, 0.02f), new Vector3(DoorW, DoorH, 0.04f), mDoor);
        for (float y = 0.25f; y < DoorH; y += 0.25f)
            LocalBox(g, "Slat", new Vector3(0f, y, 0.045f), new Vector3(DoorW - 0.05f, 0.025f, 0.01f), mRib);
        LocalBox(g, "Box", new Vector3(0f, DoorH + 0.3f, 0.2f), new Vector3(DoorW + 0.4f, 0.6f, 0.4f), mCap);
        LocalBox(g, "Frame_L", new Vector3(-DoorW * 0.5f - 0.1f, DoorH * 0.5f, 0.06f), new Vector3(0.2f, DoorH, 0.12f), mDoorFrame);
        LocalBox(g, "Frame_R", new Vector3(DoorW * 0.5f + 0.1f, DoorH * 0.5f, 0.06f), new Vector3(0.2f, DoorH, 0.12f), mDoorFrame);
        // 바닥 경고선 (노랑·검정 번갈아)
        int n = 10;
        for (int i = 0; i < n; i++)
            LocalBox(g, "Stripe", new Vector3(-DoorW * 0.5f + (i + 0.5f) * DoorW / n, 0.006f, 0.6f),
                new Vector3(DoorW / n, 0.008f, 0.25f), i % 2 == 0 ? mDoorFrame : mDoorStripe);
        // 초록 비상구 표시등 대신 위에 신호등 (빨강)
        Prim(g, PrimitiveType.Cylinder, "Beacon", new Vector3(DoorW * 0.5f + 0.4f, DoorH + 0.2f, 0.12f), new Vector3(0.18f, 0.08f, 0.18f), mSign);
    }

    static void PersonDoor(GameObject parent, Vector3 pos, Vector3 inward)
    {
        var g = Group(parent, "PersonDoor");
        g.transform.position = pos;
        g.transform.rotation = Quaternion.LookRotation(inward, Vector3.up);
        LocalBox(g, "Leaf", new Vector3(0f, 1.05f, 0.02f), new Vector3(1.0f, 2.1f, 0.04f), mSteel);
        LocalBox(g, "Frame", new Vector3(0f, 2.15f, 0.03f), new Vector3(1.2f, 0.1f, 0.06f), mCap);
        LocalBox(g, "Exit", new Vector3(0f, 2.45f, 0.03f), new Vector3(0.5f, 0.2f, 0.03f), mExt);   // 비상구 표시 (초록)
    }

    static void Pipes(GameObject parent, float x0, float x1, float z)
    {
        var g = Group(parent, "Pipes");
        float len = x1 - x0 - 0.6f;
        var p1 = Prim(g, PrimitiveType.Cylinder, "Pipe_Fire", Vector3.zero, new Vector3(0.18f, len * 0.5f, 0.18f), mPipeRed);
        p1.transform.SetPositionAndRotation(new Vector3((x0 + x1) * 0.5f, 5.3f, z), Quaternion.Euler(0f, 0f, 90f));
        var p2 = Prim(g, PrimitiveType.Cylinder, "Pipe_Air", Vector3.zero, new Vector3(0.12f, len * 0.5f, 0.12f), mPipeGray);
        p2.transform.SetPositionAndRotation(new Vector3((x0 + x1) * 0.5f, 5.65f, z), Quaternion.Euler(0f, 0f, 90f));
    }

    static void Extinguisher(GameObject parent, Vector3 pos)
    {
        var g = Group(parent, "Extinguisher");
        g.transform.position = pos;
        Prim(g, PrimitiveType.Cylinder, "Tank", new Vector3(0f, 0.32f, 0f), new Vector3(0.16f, 0.25f, 0.16f), mPipeRed);
        Prim(g, PrimitiveType.Cylinder, "Top", new Vector3(0f, 0.6f, 0f), new Vector3(0.07f, 0.04f, 0.07f), mDoorStripe);
        LocalBox(g, "Sign", new Vector3(0f, 1.15f, 0f), new Vector3(0.3f, 0.3f, 0.02f), mSign);
    }

    // ── 재질 ──
    internal static void MakeMaterials()
    {
        EnsureFolder(MatDir);
        mConcrete = Mat("Factory_Concrete", new Color(0.46f, 0.46f, 0.45f), 0.1f, 0f);
        mPanel = Mat("Factory_Panel", new Color(0.62f, 0.66f, 0.70f), 0.25f, 0.2f);
        mRib = Mat("Factory_Rib", new Color(0.52f, 0.56f, 0.60f), 0.25f, 0.2f);
        mGlass = Mat("Factory_Glass", new Color(0.55f, 0.72f, 0.85f), 0.8f, 0f, new Color(0.25f, 0.35f, 0.45f));
        mCap = Mat("Factory_Cap", new Color(0.22f, 0.24f, 0.27f), 0.2f, 0.3f);
        mSteel = Mat("Factory_Steel", new Color(0.24f, 0.32f, 0.42f), 0.3f, 0.5f);
        mTruss = Mat("Factory_Truss", new Color(0.33f, 0.36f, 0.40f), 0.25f, 0.4f);
        mLampBody = Mat("Factory_LampBody", new Color(0.18f, 0.18f, 0.20f), 0.3f, 0.5f);
        mLampGlow = Mat("Factory_LampGlow", Color.white, 0f, 0f, new Color(2.2f, 2.1f, 1.9f));
        mDoor = Mat("Factory_Shutter", new Color(0.70f, 0.72f, 0.74f), 0.3f, 0.4f);
        mDoorFrame = Mat("Factory_SafetyYellow", new Color(0.98f, 0.76f, 0.10f), 0.2f, 0f);
        mDoorStripe = Mat("Factory_SafetyBlack", new Color(0.06f, 0.06f, 0.07f), 0.2f, 0f);
        mPipeRed = Mat("Factory_FireRed", new Color(0.80f, 0.12f, 0.10f), 0.35f, 0.1f);
        mPipeGray = Mat("Factory_PipeGray", new Color(0.55f, 0.57f, 0.60f), 0.35f, 0.5f);
        mExt = Mat("Factory_ExitGreen", new Color(0.10f, 0.75f, 0.30f), 0.2f, 0f, new Color(0.1f, 0.9f, 0.35f));
        mSign = Mat("Factory_SignRed", new Color(0.85f, 0.10f, 0.10f), 0.2f, 0f, new Color(0.6f, 0.05f, 0.05f));
        mGround = Mat("Factory_Ground", new Color(0.20f, 0.21f, 0.22f), 0.05f, 0f);
        AssetDatabase.SaveAssets();
    }

    internal static Material Mat(string name, Color c, float smooth, float metal, Color? emission = null)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        m.SetFloat("_SpecularHighlights", 0f);
        m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        m.SetFloat("_EnvironmentReflections", 0f);
        m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else
        {
            m.DisableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    // ── 도우미 ──
    internal static GameObject Group(GameObject parent, string name)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent.transform, false);
        return g;
    }

    /// <summary>월드 좌표 상자 (parent 회전 없음 가정)</summary>
    internal static GameObject Box(GameObject parent, string name, Vector3 worldPos, Vector3 size, Material mat)
    {
        var g = Prim(parent, PrimitiveType.Cube, name, Vector3.zero, size, mat);
        g.transform.position = worldPos;
        return g;
    }

    /// <summary>parent 로컬 좌표 상자</summary>
    internal static GameObject LocalBox(GameObject parent, string name, Vector3 localPos, Vector3 size, Material mat) =>
        Prim(parent, PrimitiveType.Cube, name, localPos, size, mat);

    internal static GameObject Prim(GameObject parent, PrimitiveType type, string name, Vector3 localPos, Vector3 scale, Material mat)
    {
        var g = GameObject.CreatePrimitive(type);
        g.name = name;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(parent.transform, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = scale;
        g.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return g;
    }

    internal static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
