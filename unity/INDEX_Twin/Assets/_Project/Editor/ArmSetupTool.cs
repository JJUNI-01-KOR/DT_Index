// ArmSetupTool.cs : 로봇팔1(투입) 씬 배치 (에디터 전용). 담당 A · scope unity-arm
// 메뉴: Tools/INDEX/로봇팔1 만들기 (적재함 → 투입 위치)
//   1) 팔3처럼 빈 오브젝트 "RobotArm_1" 아래에 Arm_Fairino 프리팹(FR20)을 놓고 ArmRig armId = 1
//      (예전 자리표시 큐브 RobotArm_1은 이름을 바꿔 끈다. 지우지 않음)
//   2) ArmPickPlace.feedBin ↔ CushionBin.feedArm 서로 연결
//   3) 팔 베이스에서 투입 위치·적재함 칸까지 수평 거리를 Console에 출력 (FR20 도달 1.854 m)
//   4) 자동 홈: 적재함과 투입 위치 사이 위쪽을 홈으로 (ArmPickPlace.useAutoHome, homeToolPos)
//   5) 베이스 방향 맞추기: 베이스 관절이 0일 때 팔이 작업 구역(적재함과 투입 위치 가운데)을 보도록 Arm_Fairino를 Y축으로 돌린다.
//      베이스 관절 한계(±175° 근처) 끝에 작업 구역이 걸리면 팔이 반대로 한 바퀴 돌아가기 때문
//   이미 armId 1 팔이 있으면 연결만 다시 한다. Ctrl+Z로 되돌릴 수 있음

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ArmSetupTool
{
    const string ArmPrefabPath = "Assets/_Project/Prefabs/Arm/Arm_Fairino.prefab";
    // 팔1 베이스. 적재함(3.04~3.46, 0.74~1.16)과 투입 위치(1.5, 0.625, -0.5) 사이. 둘 다 1.3 m 안쪽
    static readonly Vector3 DefaultArmPos = new Vector3(2.5f, 0f, 0.3f);
    static readonly Vector3 FeedPoint = new Vector3(1.5f, 0.625f, -0.5f);   // line_coords.yaml feed_point (Unity)
    const float Reach = 1.854f;

    [MenuItem("Tools/INDEX/로봇팔1 만들기 (적재함 → 투입 위치)")]
    static void Setup()
    {
        if (Application.isPlaying) { Debug.LogWarning("[팔1 설정] Play를 멈추고 실행해"); return; }

        // ── 1) 팔1 찾기 / 만들기 ──
        ArmRig rig1 = null;
        foreach (var r in Object.FindObjectsByType<ArmRig>())
            if (r.ArmId == 1) { rig1 = r; break; }

        if (rig1 == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArmPrefabPath);
            if (prefab == null) { Debug.LogError($"[팔1 설정] 프리팹 없음: {ArmPrefabPath}"); return; }

            // 예전 자리표시(큐브) RobotArm_1은 끄고 이름만 바꾼다
            var old = GameObject.Find("RobotArm_1");
            if (old != null && old.GetComponentInChildren<ArmRig>(true) == null)
            {
                Undo.RecordObject(old, "팔1 자리표시 끄기");
                old.name = "RobotArm_1 (예전 자리표시)";
                old.SetActive(false);
            }

            var root = new GameObject("RobotArm_1");
            Undo.RegisterCreatedObjectUndo(root, "로봇팔1 만들기");
            root.transform.position = DefaultArmPos;

            var arm = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            Undo.RegisterCreatedObjectUndo(arm, "로봇팔1 만들기");
            arm.transform.localPosition = Vector3.zero;
            arm.transform.localRotation = Quaternion.identity;

            rig1 = arm.GetComponent<ArmRig>();
            var so = new SerializedObject(rig1);
            so.FindProperty("armId").intValue = 1;
            so.ApplyModifiedProperties();
        }

        var pp = rig1.GetComponent<ArmPickPlace>();
        if (pp == null) { Debug.LogError("[팔1 설정] Arm_Fairino에 ArmPickPlace가 없음"); return; }

        // ── 2) 적재함 ↔ 팔1 연결 ──
        var bin = Object.FindAnyObjectByType<CushionBin>();
        if (bin == null) Debug.LogWarning("[팔1 설정] 씬에 CushionBin이 없음. 먼저 'Tools/INDEX/적재함 만들기'");
        else
        {
            var a = new SerializedObject(pp);
            a.FindProperty("feedBin").objectReferenceValue = bin;
            // 자동 홈: 적재함 가운데 위와 투입 위치의 중간, 둘보다 넉넉히 위 (공구가 아래를 보는 대기 자세)
            Vector3 binTop = bin.SlotBottom(0);
            Vector3 mid = (new Vector3(bin.transform.position.x, 0f, bin.transform.position.z) + new Vector3(FeedPoint.x, 0f, FeedPoint.z)) * 0.5f;
            mid.y = Mathf.Max(binTop.y, FeedPoint.y) + 0.6f;
            a.FindProperty("useAutoHome").boolValue = true;
            a.FindProperty("homeToolPos").vector3Value = mid;
            a.ApplyModifiedProperties();
            var b = new SerializedObject(bin);
            b.FindProperty("feedArm").objectReferenceValue = pp;
            b.ApplyModifiedProperties();
        }

        // ── 베이스 방향 맞추기 ──
        if (bin != null) AlignBaseYaw(rig1, (new Vector3(bin.transform.position.x, 0f, bin.transform.position.z) + new Vector3(FeedPoint.x, 0f, FeedPoint.z)) * 0.5f);

        Selection.activeGameObject = rig1.transform.parent != null ? rig1.transform.parent.gameObject : rig1.gameObject;
        EditorSceneManager.MarkSceneDirty(rig1.gameObject.scene);

        // ── 3) 거리 확인 ──
        Vector3 basePos = rig1.transform.position;
        string Dist(Vector3 p)
        {
            float d = Vector2.Distance(new Vector2(basePos.x, basePos.z), new Vector2(p.x, p.z));
            return $"{d:F2} m{(d > Reach - 0.3f ? " ⚠ 멀다" : "")}";
        }
        string binInfo = "";
        if (bin != null)
        {
            float near = float.MaxValue, far = 0f;
            for (int i = 0; i < bin.Capacity; i++)
            {
                Vector3 s = bin.SlotBottom(i);
                float d = Vector2.Distance(new Vector2(basePos.x, basePos.z), new Vector2(s.x, s.z));
                near = Mathf.Min(near, d); far = Mathf.Max(far, d);
            }
            binInfo = $" · 적재함 칸 {near:F2} ~ {far:F2} m";
        }
        Debug.Log($"[팔1 설정] 완료: 팔1 베이스 Unity {basePos} (armId 1, 서비스 /arm_1_pick_place) · " +
                  $"투입 위치까지 {Dist(FeedPoint)}{binInfo} (FR20 도달 {Reach} m)\n" +
                  $"좌표표 arm_1_base (ROS): {{x: {basePos.z:F3}, y: {-basePos.x:F3}, z: {basePos.y:F3}, yaw: 0.0}}. 저장은 Ctrl+S");
    }

    static readonly float[] ElbowUp = { 0f, -1.0f, 1.5f, -0.5f, 1.57f, 0f };   // ArmPickPlace.ElbowUpA와 같은 기본 자세

    /// <summary>베이스 관절 0 + 기본 자세에서 공구가 향하는 수평 방향이 workCenter 쪽이 되도록 팔을 Y축으로 돌린다</summary>
    static void AlignBaseYaw(ArmRig rig, Vector3 workCenter)
    {
        if (rig.Joints == null || rig.Joints.Length != 6 || rig.ToolPoint == null) { Debug.LogWarning("[팔1 설정] 관절·ToolPoint가 없어 방향 맞추기를 건너뜀"); return; }
        var ik = new ArmIk(rig.Joints, rig.ToolPoint);
        ik.ForwardWorld(ElbowUp, out Vector3 p, out _);
        Vector3 b = rig.Joints[0].transform.position;
        float yaw0 = Mathf.Atan2(p.x - b.x, p.z - b.z) * Mathf.Rad2Deg;
        float want = Mathf.Atan2(workCenter.x - b.x, workCenter.z - b.z) * Mathf.Rad2Deg;
        float delta = Mathf.DeltaAngle(yaw0, want);
        if (Mathf.Abs(delta) < 1f) { Debug.Log("[팔1 설정] 베이스 방향: 이미 작업 구역을 봄"); return; }
        Undo.RecordObject(rig.transform, "팔1 베이스 방향");
        rig.transform.RotateAround(b, Vector3.up, delta);
        Debug.Log($"[팔1 설정] 베이스 방향: 작업 구역 쪽으로 {delta:F0}° 돌림 (지금 Y 회전 {rig.transform.eulerAngles.y:F0}°)");
    }
}
