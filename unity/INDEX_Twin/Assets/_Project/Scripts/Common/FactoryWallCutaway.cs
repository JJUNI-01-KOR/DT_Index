// FactoryWallCutaway.cs : 카메라 쪽 공장 벽을 숨긴다 (11/03 A). 담당 A · scope unity-env
// FactoryShellBuilder(메뉴 INDEX > Build Factory Shell)가 FactoryShell 루트에 붙이고 벽 4개를 채운다. 손으로 붙일 일 없음.
//
// 카메라가 공장 밖 위쪽에서 안을 내려다보므로, 카메라가 있는 쪽 벽(벽의 바깥 방향에 카메라가 있음)을 숨겨
// 라인이 가리지 않게 한다. Q/E로 카메라를 돌리면 숨는 벽도 따라 바뀐다.
// 벽 그룹 안의 기둥·셔터·배관도 같이 숨긴다.
// 지붕(트러스·천장 등)은 카메라가 공장 안에 있고 지붕보다 낮을 때만 보인다. 밖에서 내려다보거나 지붕보다 높으면 숨긴다.

using UnityEngine;

[DisallowMultipleComponent]
public class FactoryWallCutaway : MonoBehaviour
{
    [SerializeField] private GameObject[] walls = new GameObject[0];
    [SerializeField] private Vector3[] outward = new Vector3[0];
    [SerializeField] private Transform roof;
    [Tooltip("카메라가 벽 바깥쪽으로 이 거리(m) 넘게 나가 있으면 숨긴다")]
    [SerializeField] private float hideMargin = 0.5f;
    [Tooltip("숨김 끄기 (모든 벽을 항상 보임)")]
    [SerializeField] private bool alwaysShow = false;
    [Tooltip("지붕 트러스 아래면 높이 (m). Builder가 채운다")]
    [SerializeField] private float roofY = 12f;
    [Tooltip("공장 안쪽 바닥 범위 (x, z). Builder가 채운다")]
    [SerializeField] private Rect footprint;

    private Renderer[][] wallRenderers;
    private Renderer[] roofRenderers;
    private bool[] shown;
    private bool roofShown = true;

    /// <summary>FactoryShellBuilder가 부른다</summary>
    public void Setup(GameObject[] wallGroups, Vector3[] outwardNormals, Transform roofGroup, float roofBottomY, Rect insideXZ)
    {
        walls = wallGroups;
        outward = outwardNormals;
        roof = roofGroup;
        roofY = roofBottomY;
        footprint = insideXZ;
    }

    void Awake()
    {
        wallRenderers = new Renderer[walls.Length][];
        shown = new bool[walls.Length];
        for (int i = 0; i < walls.Length; i++)
        {
            wallRenderers[i] = walls[i] != null ? walls[i].GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            shown[i] = true;
        }
        roofRenderers = roof != null ? roof.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 c = cam.transform.position;

        for (int i = 0; i < walls.Length; i++)
        {
            if (walls[i] == null || i >= outward.Length) continue;
            Vector3 n = outward[i];
            float side = Vector3.Dot(c - walls[i].transform.position, n);
            bool show = alwaysShow || side < hideMargin;
            if (show != shown[i]) { Set(wallRenderers[i], show); shown[i] = show; }
        }

        bool inside = footprint.Contains(new Vector2(c.x, c.z));
        bool roofShow = alwaysShow || (inside && c.y < roofY - 0.5f);
        if (roofShow != roofShown) { Set(roofRenderers, roofShow); roofShown = roofShow; }
    }

    void OnDisable()
    {
        // 컴포넌트를 끄면 다 다시 보이게
        if (wallRenderers != null)
            for (int i = 0; i < wallRenderers.Length; i++) { Set(wallRenderers[i], true); shown[i] = true; }
        if (roofRenderers != null) { Set(roofRenderers, true); roofShown = true; }
    }

    static void Set(Renderer[] rs, bool on)
    {
        foreach (var r in rs) if (r != null) r.enabled = on;
    }
}
