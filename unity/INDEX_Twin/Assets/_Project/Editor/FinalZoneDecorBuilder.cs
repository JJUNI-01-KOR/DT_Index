// FinalZoneDecorBuilder.cs : 최종 구역(Pallet_Finish) 꾸미기 (11/03 A). 담당 A · scope scene
// 메뉴 INDEX > Factory > Build Final Zone Decor   (다시 누르면 지우고 새로 만듦)
//      INDEX > Factory > Remove Final Zone Decor
//
// AGV1은 도킹 위치(x -19)에서 서쪽(-X)으로 와서 FINAL(-23, -0.75)에 서고, 끌던 파레트는 뒤쪽(동쪽, x -22)에 내려놓는다
// (dt_bringup/config/agv_waypoints.yaml). 그래서 들어오는 동쪽은 비워 두고, 서쪽·북쪽·남쪽에만 세운다.
//
// 만드는 것 (새 루트 "FinalZoneDecor" 하나, 프리팹으로 저장해 씬에는 인스턴스만)
//   - 어두운 바닥 패드 + 노랑·검정 경고 테두리 + 안쪽 흰 모서리 표시
//   - 파레트가 놓이는 자리 점선 (x -22) + "DROP" 글자
//   - 남쪽 바닥 글자 "FINAL ZONE" (기본 카메라에서 읽힘). 진입 화살표는 FactoryProps AGV 주행선이 그림
//   - 북쪽 안내판 (기둥 2개 + "FINAL ZONE / SHIPPING"), 북서 모서리 신호등 탑(초록·주황·빨강)
//   - 서쪽 모서리 볼라드 2개 (AGV 정지 위치 뒤)
// 함께 고치는 것 (11/03 이슈 3): Pallet_Finish 바닥판이 y 0.1에 있어 내려놓은 파레트가 바닥판에 파묻혀 보였다.
//   바닥판 높이만 y 0.004로 내린다 (FinalZone은 x·z 거리만 보므로 동작은 같다). lowerFinishPlate로 끌 수 있다.
// 콜라이더 없음, 그림자 끔. AGV 길(z -0.75 줄) 위에는 바닥 그림만 있다.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using SB = FactoryShellBuilder;

public static class FinalZoneDecorBuilder
{
    public const string RootName = "FinalZoneDecor";
    const string PrefabPath = "Assets/_Project/Prefabs/Environment/FinalZoneDecor.prefab";
    const bool lowerFinishPlate = true;

    static readonly Vector3 DefaultCenter = new Vector3(-23f, 0f, -0.75f);   // Pallet_Finish를 못 찾을 때
    static readonly Vector3 DropOffset = new Vector3(1f, 0f, 0f);             // AGV 정지 위치 → 파레트 놓이는 자리

    // 바닥 테두리 범위 (중심 기준, m): 서쪽 1.5 · 동쪽 1.8 · 남북 1.25
    const float West = 1.5f, East = 1.8f, Half = 1.25f, Border = 0.2f;
    const float AreaTop = 0.011f;   // Environment/Area의 Work Area 판 윗면 (두께 0.01)

    [MenuItem("INDEX/Factory/Build Final Zone Decor", priority = 30)]
    public static void Build()
    {
        SB.MakeMaterials();
        var mPad = SB.Mat("Final_Pad", new Color(0.16f, 0.17f, 0.19f), 0.1f, 0f);
        var mWhite = SB.Mat("Factory_LineWhite", new Color(0.92f, 0.92f, 0.90f), 0.1f, 0f);
        var mGreen = SB.Mat("Final_LightGreen", new Color(0.1f, 0.8f, 0.3f), 0.5f, 0f, new Color(0.2f, 1.8f, 0.5f));
        var mAmberOff = SB.Mat("Final_LightAmberOff", new Color(1f, 0.765f, 0f), 0.5f, 0f);    // 11/03 A가 Inspector에서 고른 색
        var mRedOff = SB.Mat("Final_LightRedOff", new Color(1f, 0.022f, 0f), 0.5f, 0f);       // 11/03 A가 Inspector에서 고른 색
        var mBoard = SB.Mat("Factory_Banner", new Color(0.08f, 0.12f, 0.20f), 0.2f, 0f);

        // 중심: Pallet_Finish (FinalZone) 위치
        var zone = Object.FindFirstObjectByType<FinalZone>(FindObjectsInactive.Include);
        var finish = zone != null ? zone.gameObject : GameObject.Find("Pallet_Finish");
        Vector3 c = finish != null ? finish.transform.position : DefaultCenter;
        c.y = AreaTop;   // 라인 안쪽 바닥(Work Area 판, 윗면 y 0.01) 위에 그린다. 0이면 판 밑에 묻혀 안 보임

        Remove(false);
        var root = new GameObject(RootName);
        root.transform.position = c;   // 아래 좌표는 모두 중심 기준 로컬 (회전 없음)

        // ── 바닥: 패드 + 경고 테두리 + 모서리 표시 ──
        float w = West + East, cx = (East - West) * 0.5f;
        SB.LocalBox(root, "Pad", new Vector3(cx, 0.002f, 0f), new Vector3(w, 0.003f, Half * 2f), mPad);
        HazardEdge(root, new Vector3(cx, 0f, -Half), w + Border, true);
        HazardEdge(root, new Vector3(cx, 0f, Half), w + Border, true);
        HazardEdge(root, new Vector3(-West, 0f, 0f), Half * 2f, false);
        // 동쪽(입구)은 점선 정지선만
        for (float z = -Half + 0.15f; z < Half; z += 0.4f)
            SB.LocalBox(root, "EntryDash", new Vector3(East, 0.006f, z), new Vector3(0.1f, 0.004f, 0.22f), mWhite);
        foreach (var (sx, sz) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })   // 안쪽 모서리 ㄱ자 (꼭짓점에서 안쪽으로 0.3 m)
        {
            float xc = sx < 0 ? -West + 0.2f : East - 0.2f, zc = sz * (Half - 0.2f);
            SB.LocalBox(root, "CornerA", new Vector3(xc - sx * 0.15f, 0.006f, zc), new Vector3(0.3f, 0.004f, 0.06f), mWhite);
            SB.LocalBox(root, "CornerB", new Vector3(xc, 0.006f, zc - sz * 0.15f), new Vector3(0.06f, 0.004f, 0.3f), mWhite);
        }

        // ── 파레트 놓이는 자리: 흰 점선 사각형 + DROP ──
        Vector3 d = DropOffset;
        DashedSquare(root, new Vector3(d.x, 0f, d.z), 1.2f, mWhite);
        var drop = FactoryProps.Text(root, "DropText", "DROP", new Vector2(0.9f, 0.25f), new Color(1f, 1f, 1f, 0.8f));
        drop.transform.localPosition = new Vector3(d.x, 0.008f, d.z - 0.75f);
        drop.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        // ── 남쪽 바닥 글자 + 동쪽 진입 화살표 ──
        var title = FactoryProps.Text(root, "Title", "FINAL ZONE", new Vector2(w * 0.9f, 0.5f), SB.mDoorFrame.GetColor("_BaseColor"));
        title.transform.localPosition = new Vector3(cx, 0.008f, -Half - 0.55f);
        title.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        // 진입 화살표는 FactoryProps의 AGV 주행선(실제 경로: 도킹 위치에서 대각선으로 들어옴)이 그린다

        // ── 북쪽 안내판 (남쪽을 봄) ──
        float bz = Half + 0.45f;
        foreach (float px in new[] { cx - 1.1f, cx + 1.1f })
        {
            SB.LocalBox(root, "SignPost", new Vector3(px, 1.0f, bz), new Vector3(0.08f, 2.0f, 0.08f), SB.mSteel);
            SB.LocalBox(root, "SignFoot", new Vector3(px, 0.02f, bz), new Vector3(0.3f, 0.04f, 0.3f), SB.mCap);
        }
        SB.LocalBox(root, "SignBoard", new Vector3(cx, 1.75f, bz), new Vector3(2.5f, 0.7f, 0.05f), mBoard);
        SB.LocalBox(root, "SignStripe", new Vector3(cx, 1.43f, bz - 0.03f), new Vector3(2.5f, 0.06f, 0.01f), SB.mDoorFrame);
        var sign = FactoryProps.Text(root, "SignText", "FINAL ZONE\n<size=60%>SHIPPING  ·  AGV1 DROP-OFF</size>", new Vector2(2.3f, 0.55f), Color.white);
        sign.transform.localPosition = new Vector3(cx, 1.78f, bz - 0.035f);

        // ── 북서 모서리 신호등 탑 ──
        var tower = SB.Group(root, "StackLight");
        tower.transform.localPosition = new Vector3(-West - 0.25f, 0f, Half + 0.25f);
        SB.LocalBox(tower, "Pole", new Vector3(0f, 0.8f, 0f), new Vector3(0.06f, 1.6f, 0.06f), SB.mSteel);
        SB.LocalBox(tower, "Foot", new Vector3(0f, 0.02f, 0f), new Vector3(0.25f, 0.04f, 0.25f), SB.mCap);
        Material[] lamps = { mGreen, mAmberOff, mRedOff };
        for (int i = 0; i < 3; i++)
            SB.Prim(tower, PrimitiveType.Cylinder, "Light", new Vector3(0f, 1.68f + i * 0.13f, 0f), new Vector3(0.14f, 0.06f, 0.14f), lamps[i]);
        SB.Prim(tower, PrimitiveType.Cylinder, "Cap", new Vector3(0f, 2.06f, 0f), new Vector3(0.15f, 0.02f, 0.15f), SB.mCap);

        // ── 서쪽 볼라드 (AGV 정지 위치 뒤) ──
        foreach (float z in new[] { -Half + 0.2f, Half - 0.2f })
        {
            var b = SB.Group(root, "Bollard");
            b.transform.localPosition = new Vector3(-West - 0.25f, 0f, z);
            SB.Prim(b, PrimitiveType.Cylinder, "Post", new Vector3(0f, 0.45f, 0f), new Vector3(0.16f, 0.45f, 0.16f), SB.mDoorFrame);
            SB.Prim(b, PrimitiveType.Cylinder, "Band", new Vector3(0f, 0.65f, 0f), new Vector3(0.165f, 0.05f, 0.165f), SB.mDoorStripe);
            SB.Prim(b, PrimitiveType.Cylinder, "Top", new Vector3(0f, 0.92f, 0f), new Vector3(0.17f, 0.02f, 0.17f), SB.mDoorStripe);
        }

        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
        }

        // 이슈 3: 바닥판 내리기
        string fixMsg = "";
        if (lowerFinishPlate && finish != null && finish.transform.position.y > 0.01f)
        {
            Undo.RecordObject(finish.transform, "Lower Pallet_Finish");
            var p = finish.transform.position;
            fixMsg = $" / {finish.name} 바닥판 높이 {p.y:F3} → 0.004 (파레트 파묻힘 수정)";
            finish.transform.position = new Vector3(p.x, 0.004f, p.z);
        }

        SB.EnsureFolder("Assets/_Project/Prefabs/Environment");
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
        Undo.RegisterCreatedObjectUndo(root, "Build Final Zone Decor");
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log($"[FinalZoneDecorBuilder] 최종 구역 꾸밈 (중심 {c.x:F2}, {c.z:F2}){fixMsg}. 씬을 저장하세요 (Ctrl+S)");
    }

    [MenuItem("INDEX/Factory/Remove Final Zone Decor", priority = 41)]
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
        if (log) Debug.Log(n > 0 ? "[FinalZoneDecorBuilder] 최종 구역 꾸밈 지움 (Pallet_Finish 높이는 Ctrl+Z로 되돌림)" : "[FinalZoneDecorBuilder] 지울 것 없음");
    }

    /// <summary>노랑·검정 번갈아 칠한 테두리 한 변</summary>
    static void HazardEdge(GameObject root, Vector3 center, float len, bool alongX)
    {
        int n = Mathf.Max(2, Mathf.RoundToInt(len / 0.3f));
        float seg = len / n;
        for (int i = 0; i < n; i++)
        {
            float o = -len * 0.5f + (i + 0.5f) * seg;
            Vector3 p = center + (alongX ? new Vector3(o, 0.005f, 0f) : new Vector3(0f, 0.005f, o));
            Vector3 s = alongX ? new Vector3(seg, 0.004f, Border) : new Vector3(Border, 0.004f, seg);
            SB.LocalBox(root, "Hazard", p, s, i % 2 == 0 ? SB.mDoorFrame : SB.mDoorStripe);
        }
    }

    static void DashedSquare(GameObject root, Vector3 c, float size, Material m)
    {
        float h = size * 0.5f;
        for (float o = -h + 0.1f; o < h; o += 0.25f)
        {
            SB.LocalBox(root, "Dash", c + new Vector3(o, 0.007f, -h), new Vector3(0.14f, 0.004f, 0.04f), m);
            SB.LocalBox(root, "Dash", c + new Vector3(o, 0.007f, h), new Vector3(0.14f, 0.004f, 0.04f), m);
            SB.LocalBox(root, "Dash", c + new Vector3(-h, 0.007f, o), new Vector3(0.04f, 0.004f, 0.14f), m);
            SB.LocalBox(root, "Dash", c + new Vector3(h, 0.007f, o), new Vector3(0.04f, 0.004f, 0.14f), m);
        }
    }

    /// <summary>서쪽(-X)을 가리키는 꺾쇠 화살표</summary>
    static void Chevron(GameObject root, Vector3 p, Material m)
    {
        var g = SB.Group(root, "Chevron");
        g.transform.localPosition = p;
        for (int s = -1; s <= 1; s += 2)
        {
            var a = SB.Prim(g, PrimitiveType.Cube, "Arm", new Vector3(0.12f, 0f, s * 0.2f), new Vector3(0.5f, 0.004f, 0.1f), m);
            a.transform.localRotation = Quaternion.Euler(0f, -s * 38f, 0f);   // 끝이 -X 쪽에서 만남
        }
    }
}
