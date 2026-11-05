// FactoryCameraFit.cs : 기본 카메라를 라인 전체(컨베이어·로봇팔 3대·AGV 길)가 다 보이게 맞춘다 (11/03 A). 담당 A · scope scene
// 메뉴 INDEX > Factory > Fit Camera
//
// - Main Camera의 DemoCamera(Conveyor System 에셋) 값만 바꾼다: Focus, Distance, Pitch, Yaw, 거리·각도 한계, 이동 한계, 휠 단계.
// - 범위: Environment/Area + 끝에 걸친 장비 (FactoryShellBuilder.WorkRect). 높이 2.6 m(랙·팔)까지 화면 안에 들게.
// - 화면 여백: 왼쪽 위 제목, 아래 버튼 줄을 피해서 가운데 영역에 맞춘다 (16:9, Game 뷰 Full HD 기준).
// - 줌: Main Camera에 CameraZoomPitch를 붙여, 가까이 당기면 각도가 낮아지고(옆에서 보는 느낌) 멀리 빼면 높아지게 한다.
//       휠로 당겨도 위에서 콕 찍어 내려다보는 어색한 화면이 안 나온다.
// - 다시 눌러도 된다 (Ctrl+Z 가능). 장비를 옮긴 뒤 다시 누르면 새 범위에 맞춘다.

using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FactoryCameraFit
{
    // 기본 시점 (도)
    const float Pitch = 52f;         // 내려다보는 각도. 컨베이어 안쪽이 다 보이고 팔 높이도 느껴지는 정도
    const float Yaw = -8f;           // 살짝 비스듬히 (정면이면 평면도처럼 보임)
    const float Aspect = 16f / 9f;
    // 화면에서 라인이 들어갈 영역 (뷰포트 -1~1). 위: 제목·상태 배너, 아래: 버튼 줄
    const float XMax = 0.90f, YMin = -0.78f, YMax = 0.80f;
    const float EquipH = 2.6f;       // 랙·팔 높이까지 화면 안에

    [MenuItem("INDEX/Factory/Fit Camera", priority = 10)]
    public static void Fit()
    {
        var cam = Camera.main;
        var demo = FindDemo(cam);
        if (demo == null) { Debug.LogWarning("[FactoryCameraFit] Main Camera에 DemoCamera가 없음"); return; }

        Rect area = FactoryShellBuilder.WorkRect(out string from);
        Vector3 focus = new Vector3(area.center.x, 0.4f, area.center.y);
        float fov = cam.fieldOfView;
        Quaternion rot = Quaternion.Euler(Pitch, Yaw, 0f);

        // 화면 안에 다 들어오는 가장 가까운 거리 (이분 탐색)
        float lo = 5f, hi = 300f;
        for (int i = 0; i < 40; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (AllInside(area, focus, rot, mid, fov)) hi = mid; else lo = mid;
        }
        float dist = Mathf.Ceil(hi * 10f) / 10f;

        var so = new SerializedObject(demo);
        Set(so, "Focus", focus);
        Set(so, "Distance", dist);
        Set(so, "Pitch", Pitch);
        Set(so, "Yaw", Yaw);
        Set(so, "MinDistance", 6f);                       // 클릭 줌(CameraFocus)으로 가까이 볼 수 있게
        Set(so, "MaxDistance", Mathf.Round(dist * 1.35f));
        Set(so, "MinPitch", 30f);
        Set(so, "MaxPitch", 75f);
        Set(so, "ZoomStep", Mathf.Max(1.5f, dist * 0.06f));
        Set(so, "EnablePan", true);
        Set(so, "PanLimit", new Vector2(area.width * 0.5f, area.height * 0.5f));
        Set(so, "AutoOrbitSpeed", 0f);
        so.ApplyModifiedProperties();
        demo.GetType().GetMethod("Apply", BindingFlags.Instance | BindingFlags.Public)?.Invoke(demo, null);

        // 줌에 따라 각도를 바꾸는 보조 스크립트
        var zp = cam.GetComponent<CameraZoomPitch>();
        if (zp == null) zp = Undo.AddComponent<CameraZoomPitch>(cam.gameObject);
        var zso = new SerializedObject(zp);
        zso.FindProperty("defaultDistance").floatValue = dist;
        zso.FindProperty("defaultPitch").floatValue = Pitch;
        zso.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[FactoryCameraFit] 카메라 맞춤 ({from}, {area.width:F1} × {area.height:F1} m): 초점 ({focus.x:F1}, {focus.z:F1}), " +
                  $"거리 {dist:F1} m, 각도 {Pitch}°, 방향 {Yaw}°, 카메라 높이 {focus.y + dist * Mathf.Sin(Pitch * Mathf.Deg2Rad):F1} m. 씬을 저장하세요 (Ctrl+S)");
    }

    static bool AllInside(Rect a, Vector3 focus, Quaternion rot, float dist, float fov)
    {
        Vector3 pos = focus - rot * Vector3.forward * dist;
        Quaternion inv = Quaternion.Inverse(rot);
        float ty = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad), tx = ty * Aspect;
        foreach (float y in new[] { 0f, EquipH })
            foreach (var c in new[] { new Vector3(a.xMin, y, a.yMin), new Vector3(a.xMax, y, a.yMin), new Vector3(a.xMin, y, a.yMax), new Vector3(a.xMax, y, a.yMax) })
            {
                Vector3 v = inv * (c - pos);
                if (v.z <= 0.1f) return false;
                float sx = v.x / (v.z * tx), sy = v.y / (v.z * ty);
                if (Mathf.Abs(sx) > XMax || sy < YMin || sy > YMax) return false;
            }
        return true;
    }

    /// <summary>DemoCamera 기본 시점 (Focus·Distance·Pitch·Yaw)으로 카메라 위치·방향. FactoryShellBuilder가 층고 계산에 쓴다</summary>
    internal static bool TryGetView(out Vector3 pos, out Quaternion rot, out float fov)
    {
        pos = Vector3.zero; rot = Quaternion.identity; fov = 35f;
        var cam = Camera.main;
        var demo = FindDemo(cam);
        if (demo == null) return false;
        var t = demo.GetType();
        Vector3 focus = (Vector3)t.GetField("Focus").GetValue(demo);
        float dist = (float)t.GetField("Distance").GetValue(demo);
        float pitch = (float)t.GetField("Pitch").GetValue(demo);
        float yaw = (float)t.GetField("Yaw").GetValue(demo);
        rot = Quaternion.Euler(pitch, yaw, 0f);
        pos = focus - rot * Vector3.forward * dist;
        fov = cam.fieldOfView;
        return true;
    }

    static MonoBehaviour FindDemo(Camera cam)
    {
        if (cam == null) return null;
        foreach (var mb in cam.GetComponents<MonoBehaviour>())
            if (mb != null && mb.GetType().Name == "DemoCamera") return mb;
        return null;
    }

    static void Set(SerializedObject so, string name, float v) { var p = so.FindProperty(name); if (p != null) p.floatValue = v; }
    static void Set(SerializedObject so, string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; }
    static void Set(SerializedObject so, string name, Vector2 v) { var p = so.FindProperty(name); if (p != null) p.vector2Value = v; }
    static void Set(SerializedObject so, string name, Vector3 v) { var p = so.FindProperty(name); if (p != null) p.vector3Value = v; }
}
