// HudPlayLayout.cs : Play 때 ControlBar가 바꾸는 HUD 배치를 씬에 미리 넣는다 (11/03 A). 담당 A · scope unity-ui
// 메뉴 INDEX > UI > Apply Play Layout to Scene
//
// ControlBar(담당 B)는 Play 때만 버튼 4개를 오른쪽 아래로 모으고 색을 칠하고, Title Panel 폭을 줄인다.
// 그래서 Play 전 Game 화면은 흰 버튼이 네 귀퉁이에 있는 옛날 배치로 보였다.
// 이 메뉴는 ControlBar의 Place·Paint를 그대로 불러 같은 값을 씬에 저장한다 → Play 전·후 화면이 같다.
//   - ControlBar 파일은 안 고친다. 값(여백·폭·색)도 ControlBar에서 읽으니 B가 바꾸면 메뉴만 다시 누르면 된다.
//   - Play 때 ControlBar가 같은 값으로 한 번 더 옮기므로 결과는 같다 (두 번 해도 그대로).
//   - Play 때 새로 생기는 것(고장 주입 4개, 상태 배너, 상태 패널)은 씬에 만들지 않는다.
// Ctrl+Z로 되돌릴 수 있다. 누른 뒤 Ctrl+S.

using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class HudPlayLayout
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("INDEX/UI/Apply Play Layout to Scene", priority = 20)]
    public static void Apply()
    {
        if (Application.isPlaying) { Debug.LogWarning("[HudPlayLayout] Play를 끄고 누르세요"); return; }
        var panel = UnityEngine.Object.FindAnyObjectByType<ControlPanel>(FindObjectsInactive.Include);
        if (panel == null) { Debug.LogWarning("[HudPlayLayout] ControlPanel(HUD)이 씬에 없음"); return; }
        var canvas = panel.GetComponentInParent<Canvas>(true);
        if (canvas == null) { Debug.LogWarning("[HudPlayLayout] HUD가 Canvas 아래가 아님"); return; }
        Transform root = canvas.rootCanvas.transform;

        Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Apply Play Layout");

        // ControlBar 값(여백·폭·줄 높이·색)을 그대로 쓰려고 임시로 붙였다 뗀다. 편집 모드라 Start는 안 돈다
        var tmpGo = new GameObject("_ControlBarTmp") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var bar = tmpGo.AddComponent<ControlBar>();
            var t = typeof(ControlBar);
            float width = (float)t.GetField("width", Any).GetValue(bar);
            float rowH = (float)t.GetField("rowH", Any).GetValue(bar);
            float gap = (float)t.GetField("gap", Any).GetValue(bar);
            float titleWidth = (float)t.GetField("titleWidth", Any).GetValue(bar);
            var place = t.GetMethod("Place", Any);
            var paint = t.GetMethod("Paint", Any);
            Color C(string n) => (Color)t.GetField(n, Any).GetValue(null);

            var start = Find(root, "StartButton");
            var emergency = Find(root, "Error_Button");
            var fix = Find(root, "Fix_Button");
            var clear = Find(root, "Clear_Button");

            float half = (width - gap) / 2f;
            float y0 = 0f, y1 = y0 + rowH + gap;   // ControlBar.Start와 같은 배치
            void P(Button b, float x, float y) { if (b != null) place.Invoke(bar, new object[] { b, root, x, y, half, rowH }); }
            void K(Button b, Color f, Color txt)
            {
                if (b == null) return;
                paint.Invoke(null, new object[] { b, f, txt });
                if (b.targetGraphic != null) EditorUtility.SetDirty(b.targetGraphic);
                var l = b.GetComponentInChildren<TMP_Text>(true);
                if (l != null) EditorUtility.SetDirty(l);
                EditorUtility.SetDirty(b);
                EditorUtility.SetDirty(b.transform);
            }
            P(start, 0f, y0); P(clear, half + gap, y0); P(fix, 0f, y1); P(emergency, half + gap, y1);
            K(start, C("StartColor"), C("DarkText"));
            K(clear, C("ClearColor"), C("DarkText"));
            K(fix, C("FixColor"), C("DarkText"));
            K(emergency, C("EmergencyColor"), Color.white);

            // Title Panel 폭 (ControlBar.TidyTop과 같은 방식)
            var title = FindRect(root, "Title Panel");
            if (title != null && titleWidth > 0f && Math.Abs(title.sizeDelta.x - titleWidth) > 0.01f)
            {
                float cut = title.sizeDelta.x - titleWidth;
                title.sizeDelta = new Vector2(titleWidth, title.sizeDelta.y);
                foreach (RectTransform child in title)
                    if (child.anchorMin.x == child.anchorMax.x && child.sizeDelta.x > cut)
                    {
                        child.sizeDelta = new Vector2(child.sizeDelta.x - cut, child.sizeDelta.y);
                        EditorUtility.SetDirty(child);
                    }
                EditorUtility.SetDirty(title);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(tmpGo); }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[HudPlayLayout] Play 때와 같은 HUD 배치를 씬에 넣음 (버튼 4개 오른쪽 아래, 색, Title 폭). 씬을 저장하세요 (Ctrl+S)");
    }

    static Button Find(Transform root, string name)
    {
        foreach (var b in root.GetComponentsInChildren<Button>(true)) if (b.name == name) return b;
        Debug.LogWarning($"[HudPlayLayout] {name} 버튼을 못 찾음");
        return null;
    }

    static RectTransform FindRect(Transform root, string name)
    {
        foreach (var rt in root.GetComponentsInChildren<RectTransform>(true)) if (rt.name == name) return rt;
        return null;
    }
}
