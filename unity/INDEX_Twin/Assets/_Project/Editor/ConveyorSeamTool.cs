// ConveyorSeamTool.cs : 컨베이어 칸 이음새 검사·높이 맞추기 (에디터 전용). 담당 A · scope unity-line
// 메뉴: Tools/INDEX/컨베이어 이음새 검사 · Tools/INDEX/컨베이어 이음새 높이 맞추기
//
// 검사: 씬의 Conveyor마다 칸(바로 아래 자식) 바닥 콜라이더 윗면 높이, 칸 사이 틈·겹침, 옆 어긋남을 Console에 출력
// 맞추기: 칸 바닥 윗면 높이를 그 구간의 중앙값으로 맞춘다 (칸 루트 Transform의 Y만 옮김, Ctrl+Z 가능)
//         틈·옆 어긋남은 자동으로 안 고친다 (출력만 보고 손으로)

using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class ConveyorSeamTool
{
    const float HeightTol = 0.002f;   // 2 mm 넘게 다르면 표시
    const float GapTol = 0.005f;      // 5 mm 넘는 틈·겹침 표시
    const float SideTol = 0.005f;     // 5 mm 넘는 옆 어긋남 표시

    class CellInfo
    {
        public Transform root;
        public Bounds b;
        public float top;
        public float along, alongMin, alongMax, side;
    }

    [MenuItem("Tools/INDEX/컨베이어 이음새 검사")]
    static void Check() => Run(false);

    [MenuItem("Tools/INDEX/컨베이어 이음새 높이 맞추기")]
    static void Fix() => Run(true);

    static void Run(bool fix)
    {
        var sb = new StringBuilder();
        int bad = 0, fixedCount = 0;

        foreach (var conv in Object.FindObjectsByType<Conveyor>())
        {
            var cells = Collect(conv);
            if (cells.Count < 2) { sb.AppendLine($"── {Path(conv.transform)} : 칸 {cells.Count}개 (건너뜀)"); continue; }

            // 흐름 방향: 칸 중심들이 가장 길게 퍼진 수평 축 (코너가 섞인 구간도 첫 칸→끝 칸 방향 기준)
            Vector3 dir = Flat(cells.Last().b.center - cells.First().b.center);
            if (dir == Vector3.zero) dir = Vector3.forward;
            Vector3 sideAxis = Vector3.Cross(Vector3.up, dir);

            foreach (var c in cells)
            {
                c.along = Vector3.Dot(c.b.center, dir);
                c.alongMin = c.along - HalfExtentAlong(c.b, dir);
                c.alongMax = c.along + HalfExtentAlong(c.b, dir);
                c.side = Vector3.Dot(c.b.center, sideAxis);
            }
            cells.Sort((a, b) => a.along.CompareTo(b.along));

            float medianTop = Median(cells.Select(c => c.top));
            float medianSide = Median(cells.Select(c => c.side));

            sb.AppendLine($"── {Path(conv.transform)} : 칸 {cells.Count}개, 윗면 중앙값 y={medianTop:F4}");

            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                float dy = c.top - medianTop;
                sb.AppendLine($"   {c.root.name,-22} 윗면 {c.top:F4}  중심 ({c.b.center.x:F3}, {c.b.center.z:F3})  크기 ({c.b.size.x:F3}, {c.b.size.z:F3})");
                if (Mathf.Abs(dy) > HeightTol)
                {
                    bad++;
                    sb.AppendLine($"   ⚠ 높이 {c.root.name}: 윗면 {c.top:F4} ({dy * 1000f:+0.0;-0.0} mm)");
                    if (fix)
                    {
                        Undo.RecordObject(c.root, "컨베이어 이음새 높이 맞추기");
                        c.root.position -= Vector3.up * dy;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(c.root);
                        fixedCount++;
                    }
                }

                float ds = c.side - medianSide;
                if (Mathf.Abs(ds) > SideTol && !IsCornerLike(c.root))
                {
                    bad++;
                    sb.AppendLine($"   ⚠ 옆 어긋남 {c.root.name}: {ds * 1000f:+0.0;-0.0} mm");
                }

                if (i + 1 < cells.Count)
                {
                    var n = cells[i + 1];
                    float gap = n.alongMin - c.alongMax;
                    if (gap > GapTol) { bad++; sb.AppendLine($"   ⚠ 틈 {c.root.name} → {n.root.name}: {gap * 1000f:F1} mm"); }
                    else if (gap < -GapTol) sb.AppendLine($"   · 겹침 {c.root.name} → {n.root.name}: {-gap * 1000f:F1} mm (겹침은 괜찮음, 높이만 같으면 됨)");
                }
            }
        }

        if (fix && fixedCount > 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            foreach (var conv in Object.FindObjectsByType<Conveyor>()) conv.FitToChildren();
        }

        string head = fix ? $"[이음새] 높이 {fixedCount}칸 맞춤 (Ctrl+Z로 되돌리기 가능). 저장은 Ctrl+S" : $"[이음새] 검사 결과: 문제 {bad}건";
        Debug.Log(head + "\n" + sb);
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "..", "SeamReport.txt"), head + "\n" + sb);
    }

    static List<CellInfo> Collect(Conveyor conv)
    {
        var list = new List<CellInfo>();
        foreach (Transform root in conv.transform)
        {
            if (!root.gameObject.activeInHierarchy) continue;
            Collider floor = root.GetComponents<Collider>().FirstOrDefault(c => c.enabled && !c.isTrigger);
            Bounds b;
            if (floor != null) b = floor.bounds;
            else
            {
                var cols = root.GetComponentsInChildren<Collider>()
                    .Where(c => c.enabled && !c.isTrigger && (c.attachedRigidbody == null || c.attachedRigidbody.isKinematic))
                    .ToArray();
                if (cols.Length == 0) continue;
                // 벽(레일)이 섞이면 윗면이 높게 나오니까, 가장 낮은 윗면을 가진 콜라이더 = 바닥
                var low = cols.OrderBy(c => c.bounds.max.y).First();
                b = low.bounds;
            }
            list.Add(new CellInfo { root = root, b = b, top = b.max.y });
        }
        return list;
    }

    static bool IsCornerLike(Transform t)
    {
        string n = t.name.ToLowerInvariant();
        return n.Contains("corner") || n.Contains("joiner") || n.Contains("cross");
    }

    static float HalfExtentAlong(Bounds b, Vector3 dir) =>
        Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z;

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        if (v.sqrMagnitude < 1e-8f) return Vector3.zero;
        // 축에 가까우면 축으로 (격자 배치 라인)
        v.Normalize();
        if (Mathf.Abs(v.x) > 0.98f) return new Vector3(Mathf.Sign(v.x), 0, 0);
        if (Mathf.Abs(v.z) > 0.98f) return new Vector3(0, 0, Mathf.Sign(v.z));
        return v;
    }

    static float Median(IEnumerable<float> xs)
    {
        var a = xs.OrderBy(x => x).ToArray();
        return a.Length == 0 ? 0f : a[a.Length / 2];
    }

    static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
}
