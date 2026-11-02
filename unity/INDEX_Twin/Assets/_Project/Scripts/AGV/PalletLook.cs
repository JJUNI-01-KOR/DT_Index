// PalletLook.cs : 파레트 판(Plane)을 화면에서만 숨긴다. 담당 A · scope unity-agv
// 겉모양은 자식 "Visual"(Editor/PalletVisualBuilder가 넣는 나무 파레트)이 보여 준다.
// 판의 Renderer는 끄지 않고(forceRenderingOff) 그리지만 않는다 → Renderer.bounds가 그대로라서
// AgvHitch가 판 경계로 위의 방석을 찾는 것, MeshCollider로 방석이 놓이는 것이 바뀌지 않는다.
// 에디터에서도 숨긴다 (ExecuteAlways). 이 컴포넌트를 빼면 판이 다시 보인다.

using UnityEngine;

[ExecuteAlways, DisallowMultipleComponent]
public class PalletLook : MonoBehaviour
{
    void OnEnable() => Apply(true);
    void OnDisable() => Apply(false);

    void Apply(bool hide)
    {
        var r = GetComponent<Renderer>();
        if (r != null) r.forceRenderingOff = hide;
    }
}
