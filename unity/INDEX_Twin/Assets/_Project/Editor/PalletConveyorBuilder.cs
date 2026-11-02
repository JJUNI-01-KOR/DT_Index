// PalletConveyorBuilder.cs : 파레트 자리 ↔ 예비 자리 사이 컨베이어 모양을 만든다. 담당 A · scope unity-agv
// 메뉴 INDEX > Build Pallet Conveyors
//   씬의 SpareRail마다, 그 레일을 쓰는 PalletSlot까지 롤러 컨베이어를 깐다 (두 파레트를 다 덮는 길이).
//   SpareRail 아래 "ConveyorVisual"을 새로 만들고(있으면 지우고 다시), 롤러 목록을 SpareRail.rollers에 넣는다.
//   콜라이더는 없다 — 파레트 이동은 SpareRail이 한다. AGV 길도 막지 않는다.
// 다시 눌러도 된다 (자리를 옮긴 뒤 다시 누르면 새 위치로 맞춘다). Ctrl+Z로 되돌릴 수 있다.

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PalletConveyorBuilder
{
    const string VisualName = "ConveyorVisual";
    const string MatDir = "Assets/_Project/Materials";

    // 모양 (m). 파레트(1 × 1 m 판) 윗면이 y 0.1
    const float PalletSize = 1.0f;
    const float RollerR = 0.012f;      // 롤러 반지름 (낮은 롤러: 파레트 밑판이 0.025에 얹힘)
    const float RollerTop = 0.025f;    // 롤러 윗면 = 나무 파레트 밑판 바닥 (PalletVisualBuilder, 판 0.1 - 높이 0.075)
    const float RollerPitch = 0.08f;   // 롤러 간격
    const float RollerLen = 0.96f;     // 롤러 길이 (파레트 폭 안)
    const float FrameW = 0.05f;        // 옆 프레임 두께
    const float FrameTop = 0.045f;     // 옆 프레임 윗면 (파레트 밑판을 옆에서 잡아 주는 턱)
    const float FrameGap = 0.53f;      // 중심 → 옆 프레임 중심
    const float BaseH = 0.008f;        // 바닥판 높이

    [MenuItem("INDEX/Build Pallet Conveyors")]
    public static void Build()
    {
        var rails = Object.FindObjectsByType<SpareRail>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (rails.Length == 0) { Debug.LogWarning("[PalletConveyorBuilder] 씬에 SpareRail이 없음"); return; }

        var slots = Object.FindObjectsByType<PalletSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var frameMat = MakeMaterial("PalletConveyor_Frame", new Color(0.30f, 0.33f, 0.36f), 0.15f, 0f);
        var rollerMat = MakeMaterial("PalletConveyor_Roller", new Color(0.45f, 0.47f, 0.50f), 0.1f, 0f);   // 무광: 반사가 눈에 띄지 않게
        var baseMat = MakeMaterial("PalletConveyor_Base", new Color(0.12f, 0.12f, 0.13f), 0.05f, 0f);

        int built = 0;
        foreach (var rail in rails)
        {
            var slot = FindSlot(slots, rail);
            if (slot == null) { Debug.LogWarning($"[PalletConveyorBuilder] {rail.name}을 쓰는 PalletSlot이 없음 → 건너뜀", rail); continue; }
            BuildOne(rail, slot, frameMat, rollerMat, baseMat);
            built++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log($"[PalletConveyorBuilder] 컨베이어 {built}개 만듦. 씬을 저장하세요 (Ctrl+S)");
    }

    static PalletSlot FindSlot(PalletSlot[] slots, SpareRail rail)
    {
        foreach (var s in slots)
        {
            var p = new SerializedObject(s).FindProperty("spareRail");
            if (p != null && p.objectReferenceValue == rail) return s;
        }
        return null;
    }

    static void BuildOne(SpareRail rail, PalletSlot slot, Material frameMat, Material rollerMat, Material baseMat)
    {
        var old = rail.transform.Find(VisualName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        Vector3 a = rail.transform.position;   // 예비 자리 (출발)
        Vector3 b = slot.transform.position;   // 파레트 자리 (도착)
        a.y = 0f; b.y = 0f;
        Vector3 dir = b - a;
        float gap = dir.magnitude;
        if (gap < 0.01f) { Debug.LogWarning($"[PalletConveyorBuilder] {rail.name}과 {slot.name} 위치가 같음", rail); return; }
        dir /= gap;
        float len = gap + PalletSize;          // 두 파레트를 다 덮는다

        var root = new GameObject(VisualName);
        Undo.RegisterCreatedObjectUndo(root, "Build Pallet Conveyor");
        root.transform.SetParent(rail.transform, false);
        root.transform.position = (a + b) * 0.5f;                    // 바닥 (y 0)
        root.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);   // 로컬 +Z = 파레트가 가는 쪽

        // 바닥판, 옆 프레임 2개, 앞뒤 끝판
        Box(root, "Base", new Vector3(0f, BaseH * 0.5f, 0f), new Vector3(FrameGap * 2f, BaseH, len), baseMat);
        Box(root, "Frame_L", new Vector3(-FrameGap, FrameTop * 0.5f, 0f), new Vector3(FrameW, FrameTop, len), frameMat);
        Box(root, "Frame_R", new Vector3(FrameGap, FrameTop * 0.5f, 0f), new Vector3(FrameW, FrameTop, len), frameMat);
        Box(root, "End_In", new Vector3(0f, 0.0125f, -len * 0.5f), new Vector3(FrameGap * 2f + FrameW, 0.025f, FrameW), frameMat);
        Box(root, "End_Out", new Vector3(0f, 0.0125f, len * 0.5f), new Vector3(FrameGap * 2f + FrameW, 0.025f, FrameW), frameMat);

        // 롤러: 축은 진행 방향과 수직(로컬 X). SpareRail이 로컬 Y(실린더 축) 둘레로 돌린다
        var rollerParent = new GameObject("Rollers");
        rollerParent.transform.SetParent(root.transform, false);
        int n = Mathf.Max(2, Mathf.FloorToInt((len - FrameW * 2f) / RollerPitch));
        float start = -(n - 1) * RollerPitch * 0.5f;
        var rollers = new List<Transform>();
        var cyl = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        // 실린더 축(로컬 Y)을 up × dir 로 두면, 양(+)으로 돌릴 때 윗면이 dir 쪽으로 간다
        Quaternion axis = Quaternion.FromToRotation(Vector3.up, Vector3.Cross(Vector3.up, Vector3.forward));
        for (int i = 0; i < n; i++)
        {
            var r = new GameObject($"Roller_{i:00}");
            r.transform.SetParent(rollerParent.transform, false);
            r.transform.localPosition = new Vector3(0f, RollerTop - RollerR, start + i * RollerPitch);
            r.transform.localRotation = axis;
            r.transform.localScale = new Vector3(RollerR * 2f, RollerLen * 0.5f, RollerR * 2f);  // 실린더 기본 높이 2
            r.AddComponent<MeshFilter>().sharedMesh = cyl;
            r.AddComponent<MeshRenderer>().sharedMaterial = rollerMat;
            rollers.Add(r.transform);
        }

        var so = new SerializedObject(rail);
        var arr = so.FindProperty("rollers");
        arr.arraySize = rollers.Count;
        for (int i = 0; i < rollers.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = rollers[i];
        so.FindProperty("rollerRadius").floatValue = RollerR;
        so.ApplyModifiedProperties();

        Debug.Log($"[PalletConveyorBuilder] {rail.name} → {slot.name}: 길이 {len:F2} m, 롤러 {n}개", rail);
    }

    static void Box(GameObject root, string name, Vector3 pos, Vector3 size, Material mat)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(root.transform, false);
        g.transform.localPosition = pos;
        g.transform.localScale = size;
        g.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    static Material MakeMaterial(string name, Color c, float smooth, float metal)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        // 무광: 하이라이트·환경 반사 끔 (조명이 비쳐 번쩍이지 않게)
        m.SetFloat("_SpecularHighlights", 0f);
        m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        m.SetFloat("_EnvironmentReflections", 0f);
        m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        EditorUtility.SetDirty(m);
        return m;
    }
}
