// PalletVisualBuilder.cs : 나무 파레트 겉모양을 만든다. 담당 A · scope unity-agv
// 메뉴 INDEX > Build Pallet Visual
//   1. Prefabs/AGV/PalletVisual.prefab 을 새로 만든다 (1.0 × 1.0 m, 높이 7.5 cm, 콜라이더 없음)
//        윗판 7장 (가로 방향) / 받침목 3줄 (세로 방향, 컨베이어 진행 방향) / 밑판 3장
//        기준점 = 윗판 윗면 가운데. 그래서 파레트 판(Plane) 위치 그대로 붙이면 방석이 놓이는 높이가 바뀌지 않는다.
//   2. 씬의 파레트(PalletSlot.pallet, SpareRail.sparePallet)마다 "Visual" 자식으로 넣고, 원래 판(Plane)은 PalletLook이 화면에서만 숨긴다.
//      판의 MeshCollider와 Renderer는 그대로 둔다 → 방석이 판 위에 놓이고, AgvHitch가 판 경계로 방석을 찾는 것도 그대로.
//   예비 파레트를 새로 만들 때(SpareRail)는 이 파레트를 본뜨므로 겉모양도 같이 따라간다.
// 다시 눌러도 된다 (Visual을 지우고 다시 넣음). Ctrl+Z로 되돌릴 수 있다.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PalletVisualBuilder
{
    const string PrefabDir = "Assets/_Project/Prefabs/AGV";
    const string PrefabPath = PrefabDir + "/PalletVisual.prefab";
    const string MatDir = "Assets/_Project/Materials";
    const string VisualName = "Visual";

    // 모양 (m). 기준점 = 윗판 윗면 (파레트 판 높이 0.1 → 바닥 0.025, 컨베이어 롤러 윗면과 맞음)
    const float Size = 1.0f;
    const float TopT = 0.02f;           // 윗판 두께
    const int TopBoards = 7;            // 윗판 수
    const float TopBoardW = 0.10f;      // 윗판 폭
    const float RunnerW = 0.10f;        // 받침목 폭
    const float RunnerH = 0.04f;        // 받침목 높이
    const float BottomT = 0.015f;       // 밑판 두께
    const float BottomBoardW = 0.10f;   // 밑판 폭

    [MenuItem("INDEX/Build Pallet Visual")]
    public static void Build()
    {
        var wood = MakeMaterial("Pallet_Wood", new Color(0.80f, 0.64f, 0.44f), 0.15f);
        var woodDark = MakeMaterial("Pallet_Wood_Dark", new Color(0.62f, 0.47f, 0.30f), 0.12f);
        var prefab = BuildPrefab(wood, woodDark);

        int n = 0;
        foreach (var p in FindPallets())
        {
            Attach(p, prefab);
            n++;
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log($"[PalletVisualBuilder] {PrefabPath} 만들고 파레트 {n}개에 붙임. 씬을 저장하세요 (Ctrl+S)");
    }

    static GameObject BuildPrefab(Material wood, Material woodDark)
    {
        if (!AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.CreateFolder("Assets/_Project/Prefabs", "AGV");

        var root = new GameObject("PalletVisual");
        float half = Size * 0.5f;

        // 윗판: X 방향으로 길게, Z 방향으로 7장
        float gap = (Size - TopBoards * TopBoardW) / (TopBoards - 1);
        for (int i = 0; i < TopBoards; i++)
        {
            float z = -half + TopBoardW * 0.5f + i * (TopBoardW + gap);
            Box(root, $"Top_{i}", new Vector3(0f, -TopT * 0.5f, z), new Vector3(Size, TopT, TopBoardW), i % 2 == 0 ? wood : woodDark);
        }

        // 받침목: Z 방향으로 길게 (컨베이어 진행 방향), X 방향으로 3줄
        float runnerY = -TopT - RunnerH * 0.5f;
        float[] xs = { -half + RunnerW * 0.5f, 0f, half - RunnerW * 0.5f };
        for (int i = 0; i < xs.Length; i++)
            Box(root, $"Runner_{i}", new Vector3(xs[i], runnerY, 0f), new Vector3(RunnerW, RunnerH, Size), woodDark);

        // 밑판: X 방향으로 길게, 앞·가운데·뒤 3장
        float bottomY = -TopT - RunnerH - BottomT * 0.5f;
        float[] zs = { -half + BottomBoardW * 0.5f, 0f, half - BottomBoardW * 0.5f };
        for (int i = 0; i < zs.Length; i++)
            Box(root, $"Bottom_{i}", new Vector3(0f, bottomY, zs[i]), new Vector3(Size, BottomT, BottomBoardW), wood);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>씬의 파레트: 파레트 자리에 놓인 것 + 예비 자리에 놓인 것</summary>
    static System.Collections.Generic.List<GameObject> FindPallets()
    {
        var list = new System.Collections.Generic.List<GameObject>();
        foreach (var s in Object.FindObjectsByType<PalletSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Add(list, new SerializedObject(s).FindProperty("pallet")?.objectReferenceValue as GameObject);
        foreach (var r in Object.FindObjectsByType<SpareRail>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Add(list, new SerializedObject(r).FindProperty("sparePallet")?.objectReferenceValue as GameObject);
        return list;
    }

    static void Add(System.Collections.Generic.List<GameObject> list, GameObject g)
    {
        if (g != null && !list.Contains(g)) list.Add(g);
    }

    static void Attach(GameObject pallet, GameObject prefab)
    {
        var old = pallet.transform.Find(VisualName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var v = (GameObject)PrefabUtility.InstantiatePrefab(prefab, pallet.transform);
        Undo.RegisterCreatedObjectUndo(v, "Build Pallet Visual");
        v.name = VisualName;
        v.transform.localPosition = Vector3.zero;          // 판 윗면 = 윗판 윗면
        v.transform.localRotation = Quaternion.identity;
        Vector3 s = pallet.transform.lossyScale;             // 판은 0.1배 (Plane 10 m → 1 m). 겉모양은 실제 크기로
        v.transform.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);

        if (pallet.GetComponent<PalletLook>() == null) Undo.AddComponent<PalletLook>(pallet);
        Debug.Log($"[PalletVisualBuilder] {pallet.name}에 겉모양 붙임", pallet);
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

    static Material MakeMaterial(string name, Color c, float smooth)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(m);
        return m;
    }
}
