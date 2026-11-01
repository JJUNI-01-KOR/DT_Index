// BinSetupTool.cs : 방석 프리팹 크기 확정 + 적재함 오브젝트 만들기 (에디터 전용). 담당 A · scope unity-line
// 메뉴: Tools/INDEX/적재함 만들기 (방석 0.4×0.4×0.1)
//   1) Cushion 프리팹: Cushion 스크립트 추가, 크기 0.4×0.1×0.4 (localScale), 질량 1.0
//   2) 씬에 CushionBin이 없으면 "CushionBin" 생성 (CushionBin + BinLink), 프리팹·팔1 자동 연결
//   이미 있으면 연결만 다시 한다. Ctrl+Z로 씬 변경은 되돌릴 수 있음 (프리팹 변경은 git으로)

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BinSetupTool
{
    const string CushionPrefabPath = "Assets/_Project/Prefabs/Line/Cushion.prefab";
    static readonly Vector3 CushionSize = new Vector3(0.4f, 0.1f, 0.4f);
    static readonly Vector3 DefaultBinPos = new Vector3(3.25f, 0f, 0.95f);   // 팔1 베이스 (3.25, 0, -0.5)에서 +Z로 1.45 m

    [MenuItem("Tools/INDEX/적재함 만들기 (방석 0.4×0.4×0.1)")]
    static void Setup()
    {
        if (Application.isPlaying) { Debug.LogWarning("[bin_setup] Play를 멈추고 실행해"); return; }

        // ── 1) 방석 프리팹 ──
        var root = PrefabUtility.LoadPrefabContents(CushionPrefabPath);
        if (root == null) { Debug.LogError($"[bin_setup] 프리팹 없음: {CushionPrefabPath}"); return; }
        if (root.GetComponent<Cushion>() == null) root.AddComponent<Cushion>();
        root.transform.localScale = CushionSize;
        var rb = root.GetComponent<Rigidbody>();
        if (rb != null) rb.mass = 1.0f;
        if (!root.CompareTag("Cushion")) Debug.LogWarning("[bin_setup] Cushion 프리팹 태그가 Cushion이 아님 (ArmPickPlace가 못 찾음)");
        PrefabUtility.SaveAsPrefabAsset(root, CushionPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        var prefabBox = AssetDatabase.LoadAssetAtPath<Cushion>(CushionPrefabPath);

        // ── 2) 적재함 ──
        var bin = Object.FindAnyObjectByType<CushionBin>();
        if (bin == null)
        {
            var go = new GameObject("CushionBin");
            Undo.RegisterCreatedObjectUndo(go, "적재함 만들기");
            go.transform.position = DefaultBinPos;
            bin = go.AddComponent<CushionBin>();
        }
        if (bin.GetComponent<BinLink>() == null) Undo.AddComponent<BinLink>(bin.gameObject);

        ArmPickPlace arm1 = null;
        foreach (var rig in Object.FindObjectsByType<ArmRig>())
            if (rig.ArmId == 1) { arm1 = rig.GetComponent<ArmPickPlace>(); break; }

        var so = new SerializedObject(bin);
        so.FindProperty("cushionPrefab").objectReferenceValue = prefabBox;
        if (arm1 != null) so.FindProperty("feedArm").objectReferenceValue = arm1;
        so.ApplyModifiedProperties();

        var link = new SerializedObject(bin.GetComponent<BinLink>());
        link.FindProperty("bin").objectReferenceValue = bin;
        link.ApplyModifiedProperties();

        Selection.activeGameObject = bin.gameObject;
        EditorSceneManager.MarkSceneDirty(bin.gameObject.scene);

        float dist = arm1 != null ? Vector3.Distance(Flat(arm1.transform.position), Flat(bin.transform.position)) : -1f;
        Debug.Log($"[bin_setup] 완료: 방석 프리팹 {CushionSize.x}×{CushionSize.z}×{CushionSize.y} m, 질량 1.0 · 적재함 '{bin.name}' {bin.transform.position}" +
                  (arm1 != null ? $" · 팔1 '{arm1.name}' 연결, 수평 거리 {dist:F2} m (1.8 m 안이어야 함)" : " · ⚠ 팔1(ArmRig armId=1)을 못 찾음, feedArm은 직접 연결") +
                  "\n저장은 Ctrl+S");
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
