// CameraFocus.cs : 장비 클릭 → 카메라 줌 (11/03 A). 담당 A · scope unity-ui
// StatusPanel에서 로봇팔·AGV를 클릭해 패널이 열리면, 카메라가 그 장비 쪽으로 부드럽게 당겨진다.
// 패널을 닫으면([X] 또는 같은 장비 다시 클릭) 클릭 전 시점으로 돌아간다.
//   - AGV처럼 움직이는 장비는 따라간다. WASD로 직접 움직이면 따라가기를 멈춘다.
//     따라가는 기준은 장비 루트 위치 + 클릭 때 잰 중심 오프셋 (11/03 A 테스트: 경계 상자 중심을 매 프레임 쓰면
//     로봇팔이 움직이거나 방석을 들 때마다 초점이 같이 흔들렸다. 팔 루트는 안 움직이니 이제 고정)
//   - 알람으로 저절로 열린 패널(장비 없음)은 카메라를 건드리지 않는다.
//   - 마우스 휠 줌, Q/E 회전은 줌 된 상태에서도 그대로 된다.
// 카메라 조작은 Main Camera의 DemoCamera(Conveyor System 에셋)가 한다. 그 파일은 고치지 않는다.
//   DemoCamera는 ConveyorSystem.Demo 어셈블리(autoReferenced 꺼짐)라 타입을 직접 못 쓰므로
//   이름으로 찾아 Focus·targetDistance·MinDistance·MaxDistance 필드만 리플렉션으로 읽고 쓴다.
//
// 붙이는 곳: Main Camera (DemoCamera가 붙은 오브젝트). statusPanel은 비우면 씬에서 찾는다.

using System.Collections;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class CameraFocus : MonoBehaviour
{
    [Tooltip("비우면 씬에서 찾는다")]
    [SerializeField] private StatusPanel statusPanel;
    [Tooltip("이동 시간 (초)")]
    [SerializeField] private float moveSec = 0.6f;
    [Tooltip("장비 크기에 곱해 거리를 정한다 (클수록 멀리)")]
    [SerializeField] private float fitScale = 2.2f;
    [Tooltip("줌 거리 범위 (m)")]
    [SerializeField] private Vector2 distanceRange = new Vector2(5f, 14f);
    [Tooltip("움직이는 장비(AGV)를 따라갈지")]
    [SerializeField] private bool follow = true;

    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private MonoBehaviour demoCamera;        // ConveyorSystem.Demo.DemoCamera
    private FieldInfo focusField, targetDistField, minDistField, maxDistField;

    private Component target;
    private Vector3 targetOffset;     // 장비 루트 → 클릭 때 경계 상자 중심
    private bool hasSaved;
    private Vector3 savedFocus;
    private float savedDistance;
    private bool following;
    private Vector3 lastFocusSet;
    private Coroutine moving;

    void Awake()
    {
        foreach (var mb in GetComponents<MonoBehaviour>())
            if (mb != null && mb.GetType().Name == "DemoCamera") { demoCamera = mb; break; }
        if (demoCamera != null)
        {
            var t = demoCamera.GetType();
            focusField = t.GetField("Focus", Flags);
            targetDistField = t.GetField("targetDistance", Flags);
            minDistField = t.GetField("MinDistance", Flags);
            maxDistField = t.GetField("MaxDistance", Flags);
        }
        if (demoCamera == null || focusField == null || targetDistField == null)
        {
            Debug.LogWarning($"[{name}] DemoCamera(Focus·targetDistance)를 못 찾아 카메라 줌을 쓰지 않음", this);
            demoCamera = null;
        }
    }

    void OnEnable()
    {
        if (statusPanel == null) statusPanel = FindFirstObjectByType<StatusPanel>();
        if (statusPanel != null) statusPanel.SelectionChanged += OnSelectionChanged;
        else Debug.LogWarning($"[{name}] StatusPanel이 없어 카메라 줌을 쓰지 않음", this);
    }

    void OnDisable()
    {
        if (statusPanel != null) statusPanel.SelectionChanged -= OnSelectionChanged;
    }

    void OnSelectionChanged(Component device)
    {
        if (demoCamera == null) return;

        if (device != null)
        {
            // 처음 줌할 때만 원래 시점을 저장 (팔 → AGV로 바로 넘어가도 원래 시점은 유지)
            if (!hasSaved)
            {
                savedFocus = GetFocus();
                savedDistance = GetTargetDistance();
                hasSaved = true;
            }
            target = device;
            Bounds b = DeviceBounds(device);
            targetOffset = b.center - device.transform.position;
            float dist = Mathf.Clamp(b.size.magnitude * fitScale, distanceRange.x, distanceRange.y);
            MoveTo(b.center, dist, startFollow: true);
        }
        else if (hasSaved)
        {
            target = null;
            hasSaved = false;
            MoveTo(savedFocus, savedDistance, startFollow: false);
        }
    }

    void Update()
    {
        if (!following || moving != null || target == null || demoCamera == null) return;
        // WASD로 직접 움직였으면 따라가기 멈춤
        if ((GetFocus() - lastFocusSet).sqrMagnitude > 1e-6f) { following = false; return; }
        SetFocus(TargetCenter());
    }

    Vector3 TargetCenter() => target.transform.position + targetOffset;

    void MoveTo(Vector3 focus, float distance, bool startFollow)
    {
        if (moving != null) StopCoroutine(moving);
        following = false;
        moving = StartCoroutine(Move(focus, distance, startFollow));
    }

    IEnumerator Move(Vector3 toFocus, float toDist, bool startFollow)
    {
        Vector3 fromFocus = GetFocus();
        float fromDist = GetTargetDistance();
        for (float t = 0f; t < moveSec; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / moveSec);
            // 움직이는 장비면 도착 지점을 매 프레임 다시 잡는다
            if (startFollow && follow && target != null) toFocus = TargetCenter();
            SetFocus(Vector3.Lerp(fromFocus, toFocus, k));
            SetTargetDistance(Mathf.Lerp(fromDist, toDist, k));
            yield return null;
        }
        SetFocus(toFocus);
        SetTargetDistance(toDist);
        moving = null;
        following = startFollow && follow;
    }

    Vector3 GetFocus() => (Vector3)focusField.GetValue(demoCamera);

    void SetFocus(Vector3 f)
    {
        focusField.SetValue(demoCamera, f);
        lastFocusSet = f;
    }

    float GetTargetDistance() => (float)targetDistField.GetValue(demoCamera);

    void SetTargetDistance(float d)
    {
        float lo = minDistField != null ? (float)minDistField.GetValue(demoCamera) : 0f;
        float hi = maxDistField != null ? (float)maxDistField.GetValue(demoCamera) : float.MaxValue;
        targetDistField.SetValue(demoCamera, Mathf.Clamp(d, lo, hi));
    }

    /// <summary>장비 아래 켜진 Renderer를 모두 합친 경계 상자</summary>
    static Bounds DeviceBounds(Component device)
    {
        bool any = false;
        var b = new Bounds(device.transform.position, Vector3.one);
        foreach (var r in device.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }
}
