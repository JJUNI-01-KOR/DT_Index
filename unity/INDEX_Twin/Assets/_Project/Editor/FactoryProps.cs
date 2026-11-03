// FactoryProps.cs : 공장 부가 소품 (11/03 A). 담당 A · scope scene
// FactoryShellBuilder.Build()가 부른다 (메뉴 INDEX > Factory > Build Shell + Props). 혼자 부르는 메뉴는 없다.
//
// 놓는 곳: Area 바깥 ~ 벽 사이 통로(Margin 3.5 m)와 벽면. 라인·AGV 길(Area 안)에는 아무것도 놓지 않는다.
//   벽 소품(현수막·표지판·시계·환풍기·케이블 트레이·분전반·소화전함)은 그 벽 그룹 아래 → 카메라 쪽 벽과 같이 숨는다.
//   바닥 소품(보행 통로·지게차·빈 파레트 더미·박스 파레트·작업대·공구함·분리수거함·콘·제어실·하역 구역)은 Props 그룹.
// 11/03 A (2차): 라인 안쪽 바닥 표시를 더했다 (바닥 그림·납작한 것만, AGV 길은 막지 않음)
//   AGV 주행선(주황 점선 + AGV 글자), AGV 대기 칸(HOME) + 충전기, 로봇팔 작업 구역 경고선
//   북쪽 벽 실시간 상태 전광판 (WallStatusBoard가 /factory/stats를 상태 패널과 같이 보여 줌)
// 콜라이더 없음, 그림자 끔 (FactoryShellBuilder가 마지막에 일괄 처리).
// 글자는 TextMeshPro 3D (기본 글꼴에 한글이 없어 영어).

using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using SB = FactoryShellBuilder;

public static class FactoryProps
{
    static Material mWalk, mWhite, mCardboard, mTape, mWood, mDesk, mScreen, mCone, mGreenSign, mBlueSign, mYellowSign,
                    mBanner, mPanelGray, mLedGreen, mLedRed, mAmber, mClockFace;

    static Rect inside, area;
    static GameObject wS, wN, wW, wE;

    public static void Build(GameObject root, GameObject south, GameObject north, GameObject west, GameObject east,
                             Rect insideXZ, Rect areaXZ, List<float> xs, List<float> zs)
    {
        inside = insideXZ; area = areaXZ;
        wS = south; wN = north; wW = west; wE = east;
        Materials();
        var props = SB.Group(root, "Props");

        float x0 = inside.xMin, x1 = inside.xMax, z0 = inside.yMin, z1 = inside.yMax;
        float cz = inside.center.y;

        // ── 바닥: 보행 통로 (북·남·동 벽을 따라). 서쪽은 하역 구역 ──
        var walk = SB.Group(props, "Walkway");
        float sx = Mathf.Lerp(x0, x1, 0.62f);   // 북쪽 통로 동편 = 박스 파레트·작업대 자리
        Strip(walk, new Rect(x0 + 4.5f, z1 - 1.8f, (sx - 1.0f) - (x0 + 4.5f), 1.2f));           // 북 (지게차 자리 ~ 박스 파레트 앞)
        Strip(walk, new Rect(x0 + 4.5f, z0 + 0.75f, (x1 - 3.8f) - (x0 + 4.5f), 1.1f));          // 남 (빈 파레트 더미 ~ 제어실 앞)
        Strip(walk, new Rect(x1 - 1.8f, z0 + 3.4f, 1.2f, (z1 - 1.8f) - (z0 + 3.4f)));           // 동 (제어실 위 ~ 북쪽 끝)

        // ── 서쪽: 하역 구역 (셔터 앞 줄무늬 + LOADING 글자 + 콘) ──
        var dock = SB.Group(props, "LoadingZone");
        float lx0 = x0 + 0.2f, lx1 = Mathf.Min(area.xMin - 0.3f, x0 + 3.0f);
        Rect lz = new Rect(lx0, cz - 3f, lx1 - lx0, 6f);
        Outline(dock, lz, 0.1f, SB.mDoorFrame);
        for (float z = lz.yMin + 0.4f; z < lz.yMax - 0.3f; z += 0.7f)
            SB.Box(dock, "Hatch", new Vector3(lz.center.x, 0.004f, z), new Vector3(lz.width - 0.5f, 0.006f, 0.3f), SB.mDoorFrame);
        FloorText(dock, "LOADING", new Vector3(lz.center.x, 0.01f, lz.yMax + 0.6f), new Vector2(lz.width, 0.6f), 0f, SB.mDoorFrame.GetColor("_BaseColor"));
        Cone(dock, new Vector3(lx1 - 0.2f, 0f, lz.yMin - 0.3f));
        Cone(dock, new Vector3(lx1 - 0.2f, 0f, lz.yMax + 0.3f));

        // ── 북서: 지게차 주차 칸 + 지게차 ──
        var park = SB.Group(props, "ForkliftParking");
        Rect pk = new Rect(x0 + 0.5f, z1 - 3.3f, 3.2f, 2.9f);
        Outline(park, pk, 0.08f, mWhite);
        FloorText(park, "FORKLIFT", new Vector3(pk.center.x, 0.01f, pk.yMin - 0.35f), new Vector2(2.4f, 0.45f), 0f, Color.white);
        Forklift(park, new Vector3(pk.center.x - 0.55f, 0f, pk.center.y), 90f);   // 포크가 +X 쪽으로 길어 몸통을 조금 뒤로

        // ── 남서: 빈 파레트 더미 + 핸드 파레트 트럭 ──
        var pal = SB.Group(props, "EmptyPallets");
        for (int k = 0; k < 3; k++)
            PalletStack(pal, new Vector3(x0 + 1.0f + k * 1.35f, 0f, z0 + 1.0f), 4 + k * 2, k * 4f);
        PalletJack(pal, new Vector3(x0 + 1.3f, 0f, z0 + 2.6f), 15f);

        // ── 북쪽 통로: 박스 파레트 3개 + 작업대 + 공구함 ──
        var stock = SB.Group(props, "Stock");
        for (int k = 0; k < 3; k++) BoxPallet(stock, new Vector3(sx + k * 1.5f, 0f, z1 - 2.7f), k);
        var work = SB.Group(props, "Workshop");
        Workbench(work, new Vector3(x1 - 6.5f, 0f, z1 - 0.75f));
        ToolCabinet(work, new Vector3(x1 - 4.6f, 0f, z1 - 0.55f));

        // ── 남쪽: 분리수거함 (사람 문 옆) ──
        var bins = SB.Group(props, "Bins");
        Color[] bc = { new Color(0.15f, 0.35f, 0.75f), new Color(0.15f, 0.55f, 0.25f), new Color(0.95f, 0.75f, 0.10f) };
        for (int k = 0; k < 3; k++) Bin(bins, new Vector3(x0 + 5.6f + k * 0.75f, 0f, z0 + 0.38f), bc[k], k);

        // ── 남동 모서리: 제어실 ──
        ControlRoom(SB.Group(props, "ControlRoom"), new Rect(x1 - 3.4f, z0 + 0.2f, 3.2f, 2.9f));

        // ── 벽: 북 (카메라 정면, 제일 잘 보임) ──
        float bannerY = Mathf.Min(SB.WallH * 0.62f, 8.5f);
        float front = 2f * (SB.ColW + 0.08f);   // 기둥 앞으로 (기둥이 현수막을 가로지르지 않게)
        Banner(wN, Face('N', inside.center.x, bannerY, front), new Vector2(15f, 1.8f), "INDEX  SMART  FACTORY", mBanner, Color.white);
        Clock(wN, Face('N', Mid(xs, 1), 3.3f, 0.06f));   // 케이블 트레이(4.2)·배관(5.3) 아래
        SignBoard(wN, Face('N', Mid(xs, 2), 2.6f, 0.04f), "SAFETY FIRST", mGreenSign, Color.white);
        SignBoard(wN, Face('N', Mid(xs, 3), 2.6f, 0.04f), "PPE REQUIRED", mBlueSign, Color.white);
        SignBoard(wN, Face('N', Mid(xs, 0), 2.6f, 0.04f), "CAUTION\nFORKLIFTS", mYellowSign, Color.black);
        CableTray(wN, 'N', x0 + 0.5f, x1 - 0.5f, 4.2f);
        for (int i = 0; i < xs.Count - 1; i += 2) Fan(wN, Face('N', Mid(xs, i), SB.WallH - 2.6f, 0.1f));
        FireHose(wN, Face('N', Mid(xs, xs.Count - 2) - 1.2f, 1.1f, 0.25f));
        StatusBoard(wN, Face('N', Mid(xs, 4), 3.0f, 1.0f));   // 케이블 트레이(4.2) 아래, 트레이보다 앞

        // ── 라인 안쪽 바닥 표시 (11/03 A 2차) ──
        FloorMarks(SB.Group(props, "FloorMarks"));

        // ── 벽: 동 ──
        Banner(wE, Face('E', cz + 2f, bannerY - 0.6f, front), new Vector2(9f, 1.3f), "LINE A  -  CUSHION SORTING", mBanner, Color.white);
        for (int k = 0; k < 3; k++) ElectricPanel(wE, Face('E', cz - 3.6f + k * 0.85f, 1.0f, 0.4f), k);
        SignBoard(wE, Face('E', cz - 2.75f, 2.55f, 0.03f), "DANGER\nHIGH VOLTAGE", mYellowSign, Color.black, 0.9f);
        CableTray(wE, 'E', z0 + 0.5f, z1 - 0.5f, 4.2f);
        Fan(wE, Face('E', Mid(zs, 0), SB.WallH - 2.6f, 0.1f));
        FireHose(wE, Face('E', cz + 5.5f, 1.1f, 0.25f));

        // ── 벽: 서 (하역장) ──
        SignBoard(wW, Face('W', cz, 5.0f + 1.25f, 0.06f), "SHIPPING DOCK", mBanner, Color.white, 3.2f);
        DockLight(wW, Face('W', cz - 3.1f, 3.2f, 0.25f));
        DockLight(wW, Face('W', cz + 3.1f, 3.2f, 0.25f));
        Fan(wW, Face('W', Mid(zs, zs.Count - 2), SB.WallH - 2.6f, 0.1f));

        // ── 벽: 남 (보통 숨지만 돌려 볼 때) ──
        SignBoard(wS, Face('S', x0 + 4f, 2.75f, 0.03f), "EXIT", mGreenSign, Color.white, 0.8f);
        Clock(wS, Face('S', Mid(xs, xs.Count / 2), 4.6f, 0.06f));
    }

    // ── 위치 ──

    /// <summary>벽 안쪽 면 앞 (d = 물건 두께). along: N·S면 x, W·E면 z</summary>
    static Vector3 Face(char wall, float along, float y, float d)
    {
        switch (wall)
        {
            case 'N': return new Vector3(along, y, inside.yMax - d * 0.5f - 0.01f);
            case 'S': return new Vector3(along, y, inside.yMin + d * 0.5f + 0.01f);
            case 'W': return new Vector3(inside.xMin + d * 0.5f + 0.01f, y, along);
            default: return new Vector3(inside.xMax - d * 0.5f - 0.01f, y, along);
        }
    }

    static float Mid(List<float> s, int i)
    {
        i = Mathf.Clamp(i, 0, s.Count - 2);
        return (s[i] + s[i + 1]) * 0.5f;
    }

    static GameObject Sub(GameObject parent, string name, Vector3 worldPos, Quaternion rot)
    {
        var g = SB.Group(parent, name);
        g.transform.SetPositionAndRotation(worldPos, rot);
        return g;
    }

    // ── 바닥 그림 ──

    static void Strip(GameObject parent, Rect r)
    {
        if (r.width <= 0.1f || r.height <= 0.1f) return;
        SB.Box(parent, "Walk", new Vector3(r.center.x, 0.003f, r.center.y), new Vector3(r.width, 0.004f, r.height), mWalk);
        bool alongX = r.width > r.height;
        if (alongX)
        {
            SB.Box(parent, "Line", new Vector3(r.center.x, 0.006f, r.yMin), new Vector3(r.width, 0.004f, 0.06f), mWhite);
            SB.Box(parent, "Line", new Vector3(r.center.x, 0.006f, r.yMax), new Vector3(r.width, 0.004f, 0.06f), mWhite);
            for (float x = r.xMin + 3f; x < r.xMax - 1f; x += 8f) Walker(parent, new Vector3(x, 0.008f, r.center.y), 0f);
        }
        else
        {
            SB.Box(parent, "Line", new Vector3(r.xMin, 0.006f, r.center.y), new Vector3(0.06f, 0.004f, r.height), mWhite);
            SB.Box(parent, "Line", new Vector3(r.xMax, 0.006f, r.center.y), new Vector3(0.06f, 0.004f, r.height), mWhite);
            for (float z = r.yMin + 3f; z < r.yMax - 1f; z += 8f) Walker(parent, new Vector3(r.center.x, 0.008f, z), 90f);
        }
    }

    /// <summary>보행 통로 표시 (발자국 대신 화살표 두 개)</summary>
    static void Walker(GameObject parent, Vector3 p, float yaw)
    {
        var g = Sub(parent, "WalkMark", p, Quaternion.Euler(0f, yaw, 0f));
        for (int s = -1; s <= 1; s += 2)
        {
            var a = SB.Prim(g, PrimitiveType.Cube, "Arrow", new Vector3(s * 0.35f, 0f, 0.08f), new Vector3(0.3f, 0.004f, 0.06f), mWhite);
            a.transform.localRotation = Quaternion.Euler(0f, s * 40f, 0f);
            var b = SB.Prim(g, PrimitiveType.Cube, "Arrow", new Vector3(s * 0.35f, 0f, -0.08f), new Vector3(0.3f, 0.004f, 0.06f), mWhite);
            b.transform.localRotation = Quaternion.Euler(0f, -s * 40f, 0f);
        }
    }

    static void Outline(GameObject parent, Rect r, float w, Material m, float baseY = 0f)
    {
        float y = baseY + 0.005f;
        SB.Box(parent, "Edge", new Vector3(r.center.x, y, r.yMin), new Vector3(r.width + w, 0.005f, w), m);
        SB.Box(parent, "Edge", new Vector3(r.center.x, y, r.yMax), new Vector3(r.width + w, 0.005f, w), m);
        SB.Box(parent, "Edge", new Vector3(r.xMin, y, r.center.y), new Vector3(w, 0.005f, r.height), m);
        SB.Box(parent, "Edge", new Vector3(r.xMax, y, r.center.y), new Vector3(w, 0.005f, r.height), m);
    }

    /// <summary>바닥에 눕힌 글자. yaw 0 = 남쪽(기본 카메라)에서 읽힘</summary>
    internal static TextMeshPro FloorText(GameObject parent, string s, Vector3 pos, Vector2 size, float yaw, Color c)
    {
        var t = Text(parent, "FloorText", s, size, c);
        t.transform.SetPositionAndRotation(pos, Quaternion.Euler(90f, yaw, 0f));
        return t;
    }

    internal static TextMeshPro Text(GameObject parent, string name, string s, Vector2 size, Color c)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent.transform, false);
        var t = g.AddComponent<TextMeshPro>();
        t.text = s;
        t.alignment = TextAlignmentOptions.Center;
        t.enableAutoSizing = true;
        t.fontSizeMin = 0.1f;
        t.fontSizeMax = 300f;
        t.fontStyle = FontStyles.Bold;
        t.color = c;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.rectTransform.sizeDelta = size;
        return t;
    }

    // ── 벽 소품 ──

    static void Banner(GameObject wall, Vector3 p, Vector2 size, string s, Material back, Color c)
    {
        var g = Sub(wall, "Banner", p, wall.transform.rotation);
        SB.LocalBox(g, "Back", Vector3.zero, new Vector3(size.x, size.y, 0.06f), back);
        SB.LocalBox(g, "Stripe", new Vector3(0f, -size.y * 0.5f + 0.06f, -0.035f), new Vector3(size.x, 0.12f, 0.01f), SB.mDoorFrame);
        var t = Text(g, "Text", s, new Vector2(size.x * 0.92f, size.y * 0.7f), c);
        t.transform.localPosition = new Vector3(0f, 0.04f, -0.045f);
    }

    static void SignBoard(GameObject wall, Vector3 p, string s, Material back, Color c, float w = 1.4f)
    {
        var g = Sub(wall, "Sign", p, wall.transform.rotation);
        float h = s.Contains("\n") ? 0.75f : 0.5f;
        SB.LocalBox(g, "Back", Vector3.zero, new Vector3(w, h, 0.03f), back);
        var t = Text(g, "Text", s, new Vector2(w * 0.88f, h * 0.8f), c);
        t.transform.localPosition = new Vector3(0f, 0f, -0.02f);
    }

    static void Clock(GameObject wall, Vector3 p)
    {
        var g = Sub(wall, "Clock", p, wall.transform.rotation);
        var rim = SB.Prim(g, PrimitiveType.Cylinder, "Rim", Vector3.zero, new Vector3(0.7f, 0.03f, 0.7f), SB.mDoorStripe);
        rim.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var face = SB.Prim(g, PrimitiveType.Cylinder, "Face", new Vector3(0f, 0f, -0.032f), new Vector3(0.6f, 0.005f, 0.6f), mClockFace);
        face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var hh = SB.LocalBox(g, "Hour", new Vector3(0.06f, 0.06f, -0.04f), new Vector3(0.025f, 0.18f, 0.01f), SB.mDoorStripe);
        hh.transform.localRotation = Quaternion.Euler(0f, 0f, -45f);
        var mh = SB.LocalBox(g, "Minute", new Vector3(-0.05f, 0.1f, -0.045f), new Vector3(0.018f, 0.25f, 0.01f), SB.mDoorStripe);
        mh.transform.localRotation = Quaternion.Euler(0f, 0f, 25f);
    }

    static void Fan(GameObject wall, Vector3 p)
    {
        var g = Sub(wall, "Fan", p, wall.transform.rotation);
        SB.LocalBox(g, "Frame", Vector3.zero, new Vector3(1.4f, 1.4f, 0.1f), SB.mCap);
        var hub = SB.Prim(g, PrimitiveType.Cylinder, "Hole", new Vector3(0f, 0f, -0.051f), new Vector3(1.2f, 0.005f, 1.2f), SB.mDoorStripe);
        hub.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        for (int k = 0; k < 4; k++)
        {
            var b = SB.LocalBox(g, "Blade", new Vector3(0f, 0f, -0.07f), new Vector3(0.18f, 1.05f, 0.01f), SB.mPipeGray);
            b.transform.localRotation = Quaternion.Euler(0f, 0f, 45f * k + 10f);
        }
        for (int k = -2; k <= 2; k++)   // 보호망
            SB.LocalBox(g, "Guard", new Vector3(0f, k * 0.25f, -0.09f), new Vector3(1.25f, 0.02f, 0.02f), SB.mCap);
    }

    static void CableTray(GameObject wall, char w, float a, float b, float y)
    {
        Vector3 p0 = Face(w, a, y, 0.5f), p1 = Face(w, b, y, 0.5f);
        var g = Sub(wall, "CableTray", (p0 + p1) * 0.5f, wall.transform.rotation);
        float len = Mathf.Abs(b - a);
        SB.LocalBox(g, "Tray", Vector3.zero, new Vector3(len, 0.05f, 0.4f), SB.mPipeGray);
        SB.LocalBox(g, "Lip", new Vector3(0f, 0.06f, -0.2f), new Vector3(len, 0.12f, 0.02f), SB.mPipeGray);
        SB.LocalBox(g, "Cables", new Vector3(0f, 0.05f, 0f), new Vector3(len, 0.05f, 0.3f), SB.mDoorStripe);
        for (float x = -len * 0.5f + 1f; x < len * 0.5f; x += 3f)   // 받침
            SB.LocalBox(g, "Bracket", new Vector3(x, -0.08f, 0.05f), new Vector3(0.04f, 0.12f, 0.45f), SB.mCap);
    }

    static void ElectricPanel(GameObject wall, Vector3 p, int k)
    {
        var g = Sub(wall, "ElectricPanel", p, wall.transform.rotation);
        SB.LocalBox(g, "Body", Vector3.zero, new Vector3(0.8f, 2.0f, 0.4f), mPanelGray);
        SB.LocalBox(g, "Door", new Vector3(0f, 0f, -0.205f), new Vector3(0.74f, 1.9f, 0.01f), SB.mPipeGray);
        SB.LocalBox(g, "Handle", new Vector3(0.28f, 0f, -0.22f), new Vector3(0.03f, 0.18f, 0.03f), SB.mDoorStripe);
        SB.LocalBox(g, "LedG", new Vector3(-0.2f, 0.75f, -0.215f), new Vector3(0.06f, 0.06f, 0.02f), mLedGreen);
        SB.LocalBox(g, "LedR", new Vector3(-0.08f, 0.75f, -0.215f), new Vector3(0.06f, 0.06f, 0.02f), k == 1 ? mLedRed : SB.mCap);
        SB.LocalBox(g, "Label", new Vector3(0f, 0.45f, -0.215f), new Vector3(0.3f, 0.2f, 0.01f), mYellowSign);
    }

    static void FireHose(GameObject wall, Vector3 p)
    {
        var g = Sub(wall, "FireHoseCabinet", p, wall.transform.rotation);
        SB.LocalBox(g, "Box", Vector3.zero, new Vector3(0.8f, 1.0f, 0.25f), SB.mPipeRed);
        SB.LocalBox(g, "Glass", new Vector3(0f, 0.05f, -0.13f), new Vector3(0.6f, 0.6f, 0.01f), SB.mGlass);
        var t = Text(g, "Text", "FIRE HOSE", new Vector2(0.7f, 0.16f), Color.white);
        t.transform.localPosition = new Vector3(0f, -0.38f, -0.14f);
    }

    static void DockLight(GameObject wall, Vector3 p)
    {
        var g = Sub(wall, "DockLight", p, wall.transform.rotation);
        SB.LocalBox(g, "Arm", new Vector3(0f, 0f, -0.25f), new Vector3(0.08f, 0.08f, 0.5f), SB.mCap);
        SB.LocalBox(g, "Head", new Vector3(0f, -0.08f, -0.5f), new Vector3(0.3f, 0.2f, 0.25f), SB.mCap);
        SB.LocalBox(g, "Glow", new Vector3(0f, -0.19f, -0.5f), new Vector3(0.25f, 0.01f, 0.2f), mAmber);
    }

    // ── 바닥 소품 ──

    static void Cone(GameObject parent, Vector3 p)
    {
        var g = Sub(parent, "Cone", p, Quaternion.identity);
        SB.LocalBox(g, "Base", new Vector3(0f, 0.015f, 0f), new Vector3(0.38f, 0.03f, 0.38f), SB.mDoorStripe);
        float[] r = { 0.15f, 0.12f, 0.09f, 0.06f, 0.035f };
        for (int i = 0; i < r.Length; i++)
            SB.Prim(g, PrimitiveType.Cylinder, "Ring", new Vector3(0f, 0.08f + i * 0.12f, 0f), new Vector3(r[i] * 2f, 0.06f, r[i] * 2f), i == 2 ? mWhite : mCone);
    }

    static void Forklift(GameObject parent, Vector3 p, float yaw)
    {
        var g = Sub(parent, "Forklift", p, Quaternion.Euler(0f, yaw, 0f));   // 로컬 +Z = 앞 (포크 쪽)
        SB.LocalBox(g, "Body", new Vector3(0f, 0.6f, 0f), new Vector3(1.05f, 0.75f, 1.7f), SB.mDoorFrame);
        SB.LocalBox(g, "Counterweight", new Vector3(0f, 0.75f, -0.85f), new Vector3(1.05f, 0.75f, 0.35f), SB.mDoorStripe);
        SB.LocalBox(g, "Seat", new Vector3(0f, 1.1f, -0.2f), new Vector3(0.5f, 0.35f, 0.45f), SB.mDoorStripe);
        SB.LocalBox(g, "Steer", new Vector3(0f, 1.25f, 0.35f), new Vector3(0.3f, 0.05f, 0.05f), SB.mDoorStripe);
        foreach (var (x, z) in new[] { (-0.45f, -0.55f), (0.45f, -0.55f), (-0.45f, 0.5f), (0.45f, 0.5f) })
            SB.LocalBox(g, "GuardPost", new Vector3(x, 1.5f, z), new Vector3(0.06f, 1.5f, 0.06f), SB.mDoorStripe);
        SB.LocalBox(g, "GuardRoof", new Vector3(0f, 2.25f, -0.02f), new Vector3(1.0f, 0.05f, 1.15f), SB.mDoorStripe);
        SB.LocalBox(g, "Mast_L", new Vector3(-0.3f, 1.25f, 0.95f), new Vector3(0.08f, 2.4f, 0.12f), SB.mDoorStripe);
        SB.LocalBox(g, "Mast_R", new Vector3(0.3f, 1.25f, 0.95f), new Vector3(0.08f, 2.4f, 0.12f), SB.mDoorStripe);
        SB.LocalBox(g, "Carriage", new Vector3(0f, 0.35f, 1.05f), new Vector3(0.8f, 0.5f, 0.05f), SB.mDoorStripe);
        SB.LocalBox(g, "Fork_L", new Vector3(-0.22f, 0.06f, 1.6f), new Vector3(0.12f, 0.04f, 1.1f), SB.mPipeGray);
        SB.LocalBox(g, "Fork_R", new Vector3(0.22f, 0.06f, 1.6f), new Vector3(0.12f, 0.04f, 1.1f), SB.mPipeGray);
        SB.Prim(g, PrimitiveType.Cylinder, "Beacon", new Vector3(0f, 2.32f, -0.4f), new Vector3(0.12f, 0.05f, 0.12f), mAmber);
        foreach (var (x, z, r) in new[] { (-0.55f, 0.55f, 0.3f), (0.55f, 0.55f, 0.3f), (-0.55f, -0.6f, 0.25f), (0.55f, -0.6f, 0.25f) })
        {
            var w = SB.Prim(g, PrimitiveType.Cylinder, "Wheel", new Vector3(x, r, z), new Vector3(r * 2f, 0.1f, r * 2f), SB.mDoorStripe);
            w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }
    }

    static void Pallet(GameObject parent, Vector3 localPos, float yaw)
    {
        var g = SB.Group(parent, "Pallet");
        g.transform.localPosition = localPos;
        g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        SB.LocalBox(g, "Top", new Vector3(0f, 0.13f, 0f), new Vector3(1.2f, 0.025f, 1.0f), mWood);
        for (int k = -1; k <= 1; k++)
        {
            SB.LocalBox(g, "Block", new Vector3(0f, 0.07f, k * 0.42f), new Vector3(1.2f, 0.1f, 0.12f), mWood);
            SB.LocalBox(g, "Bottom", new Vector3(k * 0.5f, 0.012f, 0f), new Vector3(0.12f, 0.025f, 1.0f), mWood);
        }
    }

    static void PalletStack(GameObject parent, Vector3 p, int n, float yaw)
    {
        var g = Sub(parent, "PalletStack", p, Quaternion.Euler(0f, 90f + yaw, 0f));
        for (int i = 0; i < n; i++) Pallet(g, new Vector3(Jit(i) * 0.03f, i * 0.145f, Jit(i + 7) * 0.03f), Jit(i + 3) * 3f);
    }

    static void PalletJack(GameObject parent, Vector3 p, float yaw)
    {
        var g = Sub(parent, "PalletJack", p, Quaternion.Euler(0f, yaw, 0f));
        SB.LocalBox(g, "Fork_L", new Vector3(-0.2f, 0.04f, 0.6f), new Vector3(0.16f, 0.06f, 1.15f), SB.mPipeRed);
        SB.LocalBox(g, "Fork_R", new Vector3(0.2f, 0.04f, 0.6f), new Vector3(0.16f, 0.06f, 1.15f), SB.mPipeRed);
        SB.LocalBox(g, "Body", new Vector3(0f, 0.2f, 0f), new Vector3(0.6f, 0.3f, 0.2f), SB.mPipeRed);
        var h = SB.LocalBox(g, "Handle", new Vector3(0f, 0.75f, -0.2f), new Vector3(0.05f, 1.0f, 0.05f), SB.mDoorStripe);
        h.transform.localRotation = Quaternion.Euler(-15f, 0f, 0f);
        SB.LocalBox(g, "Grip", new Vector3(0f, 1.22f, -0.33f), new Vector3(0.35f, 0.05f, 0.05f), SB.mDoorStripe);
    }

    static void BoxPallet(GameObject parent, Vector3 p, int seed)
    {
        var g = Sub(parent, "BoxPallet", p, Quaternion.Euler(0f, seed * 2f, 0f));
        Pallet(g, Vector3.zero, 0f);
        int layers = 2 + seed % 2;
        for (int l = 0; l < layers; l++)
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                {
                    float sx = 0.56f, sy = 0.4f, sz = 0.47f;
                    var b = SB.LocalBox(g, "Box", new Vector3((i - 0.5f) * 0.6f + Jit(l * 9 + i * 3 + j + seed) * 0.02f,
                        0.145f + sy * 0.5f + l * sy, (j - 0.5f) * 0.5f), new Vector3(sx, sy - 0.01f, sz), mCardboard);
                    SB.LocalBox(b.transform.parent.gameObject, "Tape", b.transform.localPosition + new Vector3(0f, sy * 0.5f, 0f),
                        new Vector3(sx, 0.006f, 0.06f), mTape);
                }
    }

    static void Workbench(GameObject parent, Vector3 p)
    {
        var g = Sub(parent, "Workbench", p, Quaternion.identity);
        SB.LocalBox(g, "Top", new Vector3(0f, 0.9f, 0f), new Vector3(2.0f, 0.05f, 0.75f), mDesk);
        SB.LocalBox(g, "Shelf", new Vector3(0f, 0.25f, 0f), new Vector3(1.9f, 0.03f, 0.65f), SB.mPipeGray);
        foreach (var (x, z) in new[] { (-0.95f, -0.33f), (0.95f, -0.33f), (-0.95f, 0.33f), (0.95f, 0.33f) })
            SB.LocalBox(g, "Leg", new Vector3(x, 0.45f, z), new Vector3(0.05f, 0.9f, 0.05f), SB.mSteel);
        SB.LocalBox(g, "Vise", new Vector3(-0.7f, 1.0f, -0.25f), new Vector3(0.2f, 0.15f, 0.25f), SB.mSteel);
        SB.LocalBox(g, "Toolbox", new Vector3(0.5f, 1.02f, 0.05f), new Vector3(0.5f, 0.2f, 0.25f), SB.mPipeRed);
        // 벽 공구판
        SB.LocalBox(g, "Pegboard", new Vector3(0f, 1.7f, 0.33f), new Vector3(1.9f, 1.0f, 0.03f), mPanelGray);
        Color[] tc = { new Color(0.8f, 0.1f, 0.1f), new Color(0.1f, 0.3f, 0.8f), new Color(0.9f, 0.7f, 0.1f), new Color(0.2f, 0.2f, 0.2f) };
        for (int k = 0; k < 8; k++)
            SB.LocalBox(g, "Tool", new Vector3(-0.8f + k * 0.23f, 1.6f + (k % 2) * 0.25f, 0.3f), new Vector3(0.05f, 0.3f, 0.03f),
                k % 4 == 0 ? SB.mPipeRed : k % 4 == 1 ? mBlueSign : k % 4 == 2 ? SB.mDoorFrame : SB.mDoorStripe);
    }

    static void ToolCabinet(GameObject parent, Vector3 p)
    {
        var g = Sub(parent, "ToolCabinet", p, Quaternion.identity);
        SB.LocalBox(g, "Body", new Vector3(0f, 0.55f, 0f), new Vector3(0.75f, 1.1f, 0.5f), SB.mPipeRed);
        for (int k = 0; k < 5; k++)
        {
            SB.LocalBox(g, "Drawer", new Vector3(0f, 0.2f + k * 0.18f, -0.255f), new Vector3(0.68f, 0.012f, 0.01f), SB.mDoorStripe);
            SB.LocalBox(g, "Pull", new Vector3(0f, 0.25f + k * 0.18f, -0.26f), new Vector3(0.3f, 0.02f, 0.02f), SB.mPipeGray);
        }
        for (int s = -1; s <= 1; s += 2)
        {
            var w = SB.Prim(g, PrimitiveType.Cylinder, "Caster", new Vector3(s * 0.3f, 0.04f, -0.15f), new Vector3(0.08f, 0.02f, 0.08f), SB.mDoorStripe);
            w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }
    }

    static void Bin(GameObject parent, Vector3 p, Color c, int k)
    {
        var m = FactoryShellBuilder.Mat("Factory_Bin" + k, c, 0.2f, 0f);
        var g = Sub(parent, "Bin", p, Quaternion.identity);
        SB.Prim(g, PrimitiveType.Cylinder, "Body", new Vector3(0f, 0.4f, 0f), new Vector3(0.55f, 0.4f, 0.55f), m);
        SB.Prim(g, PrimitiveType.Cylinder, "Lid", new Vector3(0f, 0.82f, 0f), new Vector3(0.6f, 0.02f, 0.6f), m);
        SB.LocalBox(g, "Label", new Vector3(0f, 0.55f, -0.28f), new Vector3(0.25f, 0.18f, 0.01f), mWhite);
    }

    static void ControlRoom(GameObject g, Rect r)
    {
        float h = 3.0f, t = 0.12f;
        // 바닥·지붕
        SB.Box(g, "Floor", new Vector3(r.center.x, 0.05f, r.center.y), new Vector3(r.width, 0.1f, r.height), SB.mConcrete);
        SB.Box(g, "Roof", new Vector3(r.center.x, h + 0.08f, r.center.y), new Vector3(r.width + 0.2f, 0.16f, r.height + 0.2f), SB.mCap);
        // 안쪽 두 면 (벽 쪽 두 면은 건물 벽): 아래 패널 + 위 유리
        // 북쪽 면 (z = yMax)
        SB.Box(g, "Wall_N_Low", new Vector3(r.center.x, 0.55f, r.yMax), new Vector3(r.width, 1.0f, t), SB.mPanel);
        SB.Box(g, "Wall_N_Glass", new Vector3(r.center.x, 0.55f + 0.5f + 0.75f, r.yMax), new Vector3(r.width, 1.5f, 0.04f), SB.mGlass);
        SB.Box(g, "Wall_N_Top", new Vector3(r.center.x, h - 0.25f, r.yMax), new Vector3(r.width, 0.5f, t), SB.mPanel);
        // 서쪽 면 (x = xMin): 문 + 유리
        SB.Box(g, "Wall_W_Low", new Vector3(r.xMin, 0.55f, r.center.y + 0.45f), new Vector3(t, 1.0f, r.height - 0.9f), SB.mPanel);
        SB.Box(g, "Wall_W_Glass", new Vector3(r.xMin, 1.8f, r.center.y + 0.45f), new Vector3(0.04f, 1.5f, r.height - 0.9f), SB.mGlass);
        SB.Box(g, "Wall_W_Top", new Vector3(r.xMin, h - 0.25f, r.center.y), new Vector3(t, 0.5f, r.height), SB.mPanel);
        SB.Box(g, "Door", new Vector3(r.xMin, 1.15f, r.yMin + 0.5f), new Vector3(0.06f, 2.1f, 0.9f), SB.mSteel);
        // 모서리 기둥
        SB.Box(g, "Post", new Vector3(r.xMin, h * 0.5f, r.yMax), new Vector3(0.12f, h, 0.12f), SB.mCap);
        // 안: 책상 + 모니터 2개 + 의자
        SB.Box(g, "Desk", new Vector3(r.center.x + 0.4f, 0.85f, r.yMax - 0.55f), new Vector3(2.0f, 0.05f, 0.7f), mDesk);
        SB.Box(g, "DeskBase", new Vector3(r.center.x + 0.4f, 0.45f, r.yMax - 0.35f), new Vector3(1.9f, 0.75f, 0.3f), SB.mPipeGray);
        for (int k = 0; k < 2; k++)
        {
            float x = r.center.x + k * 0.7f;
            SB.Box(g, "Monitor", new Vector3(x, 1.18f, r.yMax - 0.7f), new Vector3(0.6f, 0.38f, 0.04f), SB.mDoorStripe);
            SB.Box(g, "Screen", new Vector3(x, 1.18f, r.yMax - 0.725f), new Vector3(0.55f, 0.33f, 0.01f), mScreen);
        }
        SB.Box(g, "Chair", new Vector3(r.center.x + 0.35f, 0.5f, r.yMax - 1.35f), new Vector3(0.5f, 0.1f, 0.5f), SB.mDoorStripe);
        SB.Box(g, "ChairBack", new Vector3(r.center.x + 0.35f, 0.8f, r.yMax - 1.6f), new Vector3(0.5f, 0.55f, 0.08f), SB.mDoorStripe);
        // 지붕 위 실외기 + 간판
        SB.Box(g, "ACUnit", new Vector3(r.center.x - 0.4f, h + 0.45f, r.center.y), new Vector3(1.0f, 0.6f, 0.6f), SB.mPipeGray);
        SB.Prim(g, PrimitiveType.Cylinder, "ACFan", new Vector3(r.center.x - 0.4f, h + 0.76f, r.center.y), new Vector3(0.45f, 0.01f, 0.45f), SB.mDoorStripe);
        var sign = Text(g, "Sign", "CONTROL ROOM", new Vector2(r.width * 0.9f, 0.35f), Color.white);
        // 간판은 지붕 위 북쪽 끝에 남쪽(기본 카메라)을 보고 선다
        SB.Box(g, "SignBack", new Vector3(r.center.x, h + 0.42f, r.yMax - 0.05f), new Vector3(r.width * 0.95f, 0.45f, 0.04f), mBanner);
        sign.transform.SetPositionAndRotation(new Vector3(r.center.x, h + 0.42f, r.yMax - 0.075f), Quaternion.identity);
    }

    // ── 11/03 A 2차: 바닥 표시·전광판 ──

    // AGV 정지 지점 (Unity 좌표). dt_bringup/config/agv_waypoints.yaml 주석의 Unity 값과 같다. HOME은 씬의 AGV1 위치를 쓴다
    static readonly Vector2 AgvTurn = new Vector2(-19f, -0.75f);
    static readonly Vector2 AgvP1 = new Vector2(-19f, -6.5f);
    static readonly Vector2 AgvP2 = new Vector2(-19f, 5.4f);
    static readonly Vector2 AgvFinal = new Vector2(-23f, -0.75f);

    // 라인 안쪽 바닥(Environment/Area의 Work Area 판, 두께 0.01)의 윗면. 그 위에 그려야 보인다
    const float AreaTop = 0.011f;

    static void FloorMarks(GameObject g)
    {
        var mLane = SB.Mat("Factory_AgvLane", new Color(1f, 0.55f, 0.08f), 0.2f, 0f);
        var mWhite2 = mWhite;

        // AGV 대기 위치: 씬의 AGV1 (AgvWaypointDriver) 위치. 없으면 yaml HOME
        var agv = Object.FindFirstObjectByType<AgvWaypointDriver>(FindObjectsInactive.Include);
        Vector2 home = agv != null ? new Vector2(agv.transform.position.x, agv.transform.position.z) : new Vector2(-16.5f, -0.75f);

        // 주행선: FINAL ↔ HOME (가로), P1_DOCK ↔ P2_DOCK (세로, 가운데 줄 x -19)
        // 11/03 A: agv_waypoints.yaml + dt_agv/route.py 실제 경로대로 (일방통행 루프)
        //   출동(빈 AGV, 주황): HOME → 경유점(-19, -0.75) → P1_DOCK 또는 P2_DOCK
        //   견인(파레트 끌고, 파랑): 도킹 위치 → FINAL 대각선 직선 (to_final 경유점 없음)
        //   복귀(초록): FINAL → HOME 직선. 출동선과 같은 줄이라 남쪽으로 0.35 m 비켜 그린다
        var mTow = SB.Mat("Factory_AgvTow", new Color(0.20f, 0.55f, 1.0f), 0.2f, 0f);
        var mBack = SB.Mat("Factory_AgvReturn", new Color(0.25f, 0.85f, 0.40f), 0.2f, 0f);
        Vector2 off = new Vector2(0f, -0.35f);
        Lane(g, home, AgvTurn, mLane);
        Lane(g, AgvTurn, AgvP1, mLane);
        Lane(g, AgvTurn, AgvP2, mLane);
        Lane(g, AgvP1, AgvFinal, mTow);
        Lane(g, AgvP2, AgvFinal, mTow);
        Lane(g, AgvFinal + off, home + off, mBack);

        Color cOut = mLane.GetColor("_BaseColor"), cTow = mTow.GetColor("_BaseColor"), cBack = mBack.GetColor("_BaseColor");
        FactoryProps.FloorText(g, "TO DOCK", new Vector3(AgvTurn.x + 0.55f, AreaTop + 0.012f, (AgvTurn.y + AgvP2.y) * 0.5f), new Vector2(1.4f, 0.28f), -90f, cOut);
        FactoryProps.FloorText(g, "TO DOCK", new Vector3(AgvTurn.x + 0.55f, AreaTop + 0.012f, (AgvTurn.y + AgvP1.y) * 0.5f), new Vector2(1.4f, 0.28f), -90f, cOut);
        Vector2 t1 = Vector2.Lerp(AgvP1, AgvFinal, 0.5f), t2 = Vector2.Lerp(AgvP2, AgvFinal, 0.5f);
        FactoryProps.FloorText(g, "TOWING", new Vector3(t1.x - 0.6f, AreaTop + 0.012f, t1.y), new Vector2(1.2f, 0.28f), 0f, cTow);
        FactoryProps.FloorText(g, "TOWING", new Vector3(t2.x - 0.6f, AreaTop + 0.012f, t2.y), new Vector2(1.2f, 0.28f), 0f, cTow);
        FactoryProps.FloorText(g, "RETURN", new Vector3((AgvFinal.x + AgvTurn.x) * 0.5f, AreaTop + 0.012f, AgvFinal.y - 0.75f), new Vector2(1.2f, 0.28f), 0f, cBack);

        // HOME 칸 + 충전기 (주행선 북쪽 옆, 길 위가 아님)
        Outline(g, new Rect(home.x - 0.7f, home.y - 0.6f, 1.4f, 1.2f), 0.06f, mWhite2, AreaTop);
        FactoryProps.FloorText(g, "HOME", new Vector3(home.x, AreaTop + 0.012f, home.y - 0.85f), new Vector2(1.0f, 0.28f), 0f, Color.white);
        var ch = Sub(g, "Charger", new Vector3(home.x, 0f, home.y + 1.05f), Quaternion.identity);
        SB.LocalBox(ch, "Body", new Vector3(0f, 0.45f, 0f), new Vector3(0.6f, 0.9f, 0.3f), mPanelGray);
        SB.LocalBox(ch, "Face", new Vector3(0f, 0.55f, -0.155f), new Vector3(0.45f, 0.5f, 0.01f), mBanner);
        SB.LocalBox(ch, "Led", new Vector3(0f, 0.72f, -0.165f), new Vector3(0.25f, 0.05f, 0.01f), mLedGreen);
        SB.LocalBox(ch, "Bolt", new Vector3(0f, 0.5f, -0.165f), new Vector3(0.08f, 0.2f, 0.01f), SB.mDoorFrame);
        SB.LocalBox(ch, "Base", new Vector3(0f, 0.02f, 0f), new Vector3(0.7f, 0.04f, 0.4f), SB.mCap);
        SB.LocalBox(ch, "Contact", new Vector3(0f, 0.15f, -0.3f), new Vector3(0.3f, 0.08f, 0.3f), SB.mDoorFrame);

        // 로봇팔 작업 구역: 팔 베이스 둘레 노랑·검정 경고선 (바닥 그림만)
        foreach (var arm in Object.FindObjectsByType<ArmRig>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            Vector3 a = arm.transform.position;
            HazardRect(g, new Rect(a.x - 1.5f, a.z - 1.5f, 3f, 3f), 0.12f);
            FactoryProps.FloorText(g, "ROBOT ZONE", new Vector3(a.x, AreaTop + 0.012f, a.z - 1.75f), new Vector2(1.6f, 0.26f), 0f, SB.mDoorFrame.GetColor("_BaseColor"));
        }
    }

    /// <summary>점선 + 진행 방향 꺾쇠 (a → b)</summary>
    static void Lane(GameObject g, Vector2 a, Vector2 b, Material m)
    {
        Dashed(g, a, b, m);
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 1.5f) return;
        Vector2 u = d / len;
        float yaw = Mathf.Atan2(u.x, u.y) * Mathf.Rad2Deg;
        int n = Mathf.Max(1, Mathf.FloorToInt(len / 2.5f));
        for (int k = 1; k <= n; k++)
        {
            Vector2 c = a + u * (len * k / (n + 1f));
            var ch = Sub(g, "LaneArrow", new Vector3(c.x, AreaTop + 0.007f, c.y), Quaternion.Euler(0f, yaw, 0f));
            for (int sgn = -1; sgn <= 1; sgn += 2)   // 끝이 진행 방향(+Z)에서 만나는 꺾쇠
            {
                var arm = SB.Prim(ch, PrimitiveType.Cube, "Arm", new Vector3(sgn * 0.13f, 0f, -0.1f), new Vector3(0.07f, 0.004f, 0.36f), m);
                arm.transform.localRotation = Quaternion.Euler(0f, -sgn * 35f, 0f);
            }
        }
    }

    static void Dashed(GameObject g, Vector2 a, Vector2 b, Material m)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 0.1f) return;
        Vector2 u = d / len;
        float yaw = Mathf.Atan2(u.x, u.y) * Mathf.Rad2Deg;
        for (float t = 0.2f; t < len - 0.2f; t += 1.0f)
        {
            float l = Mathf.Min(0.6f, len - 0.2f - t);
            Vector2 c = a + u * (t + l * 0.5f);
            var s = SB.Box(g, "LaneDash", new Vector3(c.x, AreaTop + 0.006f, c.y), new Vector3(0.1f, 0.004f, l), m);
            s.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
    }

    static void HazardRect(GameObject g, Rect r, float w)
    {
        int nx = Mathf.Max(2, Mathf.RoundToInt(r.width / 0.3f)), nz = Mathf.Max(2, Mathf.RoundToInt(r.height / 0.3f));
        for (int i = 0; i < nx; i++)
        {
            float x = r.xMin + (i + 0.5f) * r.width / nx;
            var m = i % 2 == 0 ? SB.mDoorFrame : SB.mDoorStripe;
            SB.Box(g, "Hazard", new Vector3(x, AreaTop + 0.005f, r.yMin), new Vector3(r.width / nx, 0.004f, w), m);
            SB.Box(g, "Hazard", new Vector3(x, AreaTop + 0.005f, r.yMax), new Vector3(r.width / nx, 0.004f, w), m);
        }
        for (int i = 0; i < nz; i++)
        {
            float z = r.yMin + (i + 0.5f) * r.height / nz;
            var m = i % 2 == 0 ? SB.mDoorStripe : SB.mDoorFrame;
            SB.Box(g, "Hazard", new Vector3(r.xMin, AreaTop + 0.005f, z), new Vector3(w, 0.004f, r.height / nz), m);
            SB.Box(g, "Hazard", new Vector3(r.xMax, AreaTop + 0.005f, z), new Vector3(w, 0.004f, r.height / nz), m);
        }
    }

    /// <summary>실시간 상태 전광판 (북쪽 벽). 글자는 WallStatusBoard가 Play 중에 채운다</summary>
    static void StatusBoard(GameObject wall, Vector3 p)
    {
        var g = Sub(wall, "StatusBoard", p, wall.transform.rotation);
        var mScreenDark = SB.Mat("Factory_BoardScreen", new Color(0.02f, 0.03f, 0.05f), 0.6f, 0f, new Color(0.01f, 0.02f, 0.04f));
        SB.LocalBox(g, "Frame", Vector3.zero, new Vector3(4.0f, 1.9f, 0.12f), SB.mDoorStripe);
        SB.LocalBox(g, "Screen", new Vector3(0f, 0f, -0.065f), new Vector3(3.8f, 1.7f, 0.01f), mScreenDark);
        SB.LocalBox(g, "TopBar", new Vector3(0f, 0.72f, -0.072f), new Vector3(3.8f, 0.26f, 0.005f), SB.mDoorFrame);
        var title = Text(g, "Title", "LINE A  LIVE STATUS", new Vector2(3.5f, 0.2f), new Color(0.06f, 0.06f, 0.07f));
        title.transform.localPosition = new Vector3(0f, 0.72f, -0.078f);
        var body = Text(g, "Body", "waiting for /factory/stats", new Vector2(3.5f, 1.25f), Color.white);
        body.transform.localPosition = new Vector3(0f, -0.13f, -0.075f);
        body.alignment = TextAlignmentOptions.MidlineLeft;
        body.fontStyle = FontStyles.Normal;
        body.richText = true;
        // 받침대 2개 (벽에서 앞으로 나온 것을 잡아 주는 팔)
        foreach (float x in new[] { -1.5f, 1.5f })
            SB.LocalBox(g, "Arm", new Vector3(x, 0f, 0.3f), new Vector3(0.06f, 0.06f, 0.5f), SB.mCap);
        g.AddComponent<WallStatusBoard>();
    }

    static float Jit(int i) => Mathf.Sin(i * 12.9898f) * 0.5f;   // 매번 같은 값 (다시 만들어도 모양이 같게)

    static void Materials()
    {
        mWalk = SB.Mat("Factory_Walkway", new Color(0.20f, 0.42f, 0.30f), 0.15f, 0f);
        mWhite = SB.Mat("Factory_LineWhite", new Color(0.92f, 0.92f, 0.90f), 0.1f, 0f);
        mCardboard = SB.Mat("Factory_Cardboard", new Color(0.63f, 0.47f, 0.31f), 0.05f, 0f);
        mTape = SB.Mat("Factory_Tape", new Color(0.78f, 0.66f, 0.45f), 0.3f, 0f);
        var wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Pallet_Wood.mat");
        mWood = wood != null ? wood : SB.Mat("Factory_Wood", new Color(0.66f, 0.49f, 0.30f), 0.1f, 0f);
        mDesk = SB.Mat("Factory_Desk", new Color(0.55f, 0.50f, 0.45f), 0.2f, 0f);
        mScreen = SB.Mat("Factory_Screen", new Color(0.10f, 0.25f, 0.45f), 0.6f, 0f, new Color(0.15f, 0.45f, 0.85f));
        mCone = SB.Mat("Factory_ConeOrange", new Color(0.98f, 0.40f, 0.05f), 0.2f, 0f);
        mGreenSign = SB.Mat("Factory_SignGreen", new Color(0.05f, 0.55f, 0.25f), 0.2f, 0f);
        mBlueSign = SB.Mat("Factory_SignBlue", new Color(0.05f, 0.30f, 0.70f), 0.2f, 0f);
        mYellowSign = SB.Mat("Factory_SignYellow", new Color(0.98f, 0.80f, 0.10f), 0.2f, 0f);
        mBanner = SB.Mat("Factory_Banner", new Color(0.08f, 0.12f, 0.20f), 0.2f, 0f);
        mPanelGray = SB.Mat("Factory_PanelGray", new Color(0.70f, 0.72f, 0.72f), 0.2f, 0.1f);
        mLedGreen = SB.Mat("Factory_LedGreen", new Color(0.1f, 0.9f, 0.3f), 0.5f, 0f, new Color(0.2f, 1.6f, 0.5f));
        mLedRed = SB.Mat("Factory_LedRed", new Color(0.9f, 0.1f, 0.1f), 0.5f, 0f, new Color(1.6f, 0.15f, 0.1f));
        mAmber = SB.Mat("Factory_Amber", new Color(1f, 0.6f, 0.1f), 0.5f, 0f, new Color(1.8f, 1.0f, 0.15f));
        mClockFace = SB.Mat("Factory_ClockFace", new Color(0.95f, 0.95f, 0.93f), 0.3f, 0f);
    }
}
