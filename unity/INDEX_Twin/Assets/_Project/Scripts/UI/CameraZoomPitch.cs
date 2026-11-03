// CameraZoomPitch.cs : 줌에 따라 카메라 각도를 자연스럽게 바꾼다 (11/03 A). 담당 A · scope unity-ui
// Main Camera(DemoCamera와 같은 오브젝트)에 붙인다. 메뉴 INDEX > Factory > Fit Camera가 붙이고 값을 채운다.
//
// DemoCamera는 거리만 바꾸고 각도는 고정이라, 휠로 당기면 위에서 콕 찍어 내려다보는 화면이 된다.
// 그래서 거리에 따라 각도를 바꾼다.
//   MinDistance(가까이)  → nearPitch  (낮게, 장비를 옆에서 보는 느낌)
//   defaultDistance      → defaultPitch (기본 시점)
//   MaxDistance(멀리)    → farPitch   (높게, 전체를 내려다봄)
// 위·아래 화살표로 직접 기울인 만큼은 그대로 더해 준다 (손으로 바꾼 각도를 덮어쓰지 않음).
// 클릭 줌(CameraFocus)도 거리를 바꾸므로 같이 적용된다.
// DemoCamera는 다른 어셈블리라 CameraFocus처럼 이름으로 찾아 리플렉션으로 값을 읽고 쓴다.

using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]   // DemoCamera.LateUpdate 다음에
public class CameraZoomPitch : MonoBehaviour
{
    [SerializeField] private float defaultDistance = 40f;
    [SerializeField] private float defaultPitch = 52f;
    [Tooltip("가장 가까이 당겼을 때 각도")]
    [SerializeField] private float nearPitch = 36f;
    [Tooltip("가장 멀리 뺐을 때 각도")]
    [SerializeField] private float farPitch = 62f;

    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private MonoBehaviour demo;
    private FieldInfo fDist, fPitch, fMin, fMax;
    private MethodInfo mApply;
    private float lastSet = float.NaN;
    private float userOffset;

    void Awake()
    {
        foreach (var mb in GetComponents<MonoBehaviour>())
            if (mb != null && mb.GetType().Name == "DemoCamera") { demo = mb; break; }
        if (demo == null) { Debug.LogWarning($"[{name}] DemoCamera가 없어 줌 각도 조절을 쓰지 않음", this); enabled = false; return; }
        var t = demo.GetType();
        fDist = t.GetField("Distance", Flags);
        fPitch = t.GetField("Pitch", Flags);
        fMin = t.GetField("MinDistance", Flags);
        fMax = t.GetField("MaxDistance", Flags);
        mApply = t.GetMethod("Apply", Flags);
        if (fDist == null || fPitch == null) { enabled = false; return; }
    }

    void LateUpdate()
    {
        float d = (float)fDist.GetValue(demo);
        float p = (float)fPitch.GetValue(demo);
        float minD = fMin != null ? (float)fMin.GetValue(demo) : defaultDistance * 0.3f;
        float maxD = fMax != null ? (float)fMax.GetValue(demo) : defaultDistance * 1.3f;

        // 화살표로 기울인 만큼 (지난 프레임에 내가 넣은 값과의 차이)
        if (!float.IsNaN(lastSet)) userOffset = Mathf.Clamp(userOffset + (p - lastSet), -25f, 25f);

        float baseP = d <= defaultDistance
            ? Mathf.Lerp(nearPitch, defaultPitch, Mathf.InverseLerp(minD, defaultDistance, d))
            : Mathf.Lerp(defaultPitch, farPitch, Mathf.InverseLerp(defaultDistance, maxD, d));
        float want = Mathf.Clamp(baseP + userOffset, 15f, 85f);

        if (Mathf.Abs(want - p) > 0.01f)
        {
            fPitch.SetValue(demo, want);
            mApply?.Invoke(demo, null);
        }
        lastSet = want;
    }
}
