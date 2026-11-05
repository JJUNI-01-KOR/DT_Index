using System.Linq;
using UnityEngine;

/// <summary>
/// 흡착(진공) 그리퍼 — 집기 단순화 (SRS 2.5).
/// 물리로 빨아들이지 않고, 패드에 닿은 박스를 Kinematic으로 바꿔 ToolPoint를 따라다니게 한다.
/// - Grab(): 흡착 패드 근처 박스를 찾아 붙임 (흡착 ON)
/// - Release(): 박스를 떼고 물리 다시 켬 (흡착 OFF)
/// - 흡착 중이면 패드 색이 바뀜 (시연용 표시)
/// Arm_Fairino 프리팹 루트에 ArmRig와 같이 붙인다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ArmRig))]
public class KinematicGripper : MonoBehaviour
{
    [Header("흡착")]
    [Tooltip("ToolPoint(패드 표면)에서 이 반경(m) 안의 박스를 집는다. 박스 윗면에 닿을 만큼만 작게")]
    [SerializeField] private float grabRadius = 0.03f;

    [Tooltip("집을 수 있는 레이어 (박스 레이어만 체크 권장). Everything이면 아무 Rigidbody나")]
    [SerializeField] private LayerMask grabLayers = ~0;

    [Tooltip("들고 있는 동안 박스 콜라이더를 꺼서 팔·다른 박스와 부딪히지 않게")]
    [SerializeField] private bool disableCollidersWhileHeld = true;

    [Header("흡착 표시 (선택)")]
    [Tooltip("흡착 패드 MeshRenderer. 흡착 중이면 색이 바뀜")]
    [SerializeField] private Renderer padRenderer;
    [SerializeField] private Color padIdleColor = new Color(0.15f, 0.15f, 0.15f);
    [SerializeField] private Color padActiveColor = new Color(0.2f, 0.85f, 0.3f);

    private ArmRig rig;
    private Rigidbody held;
    private Collider[] heldColliders;
    private Vector3 heldLocalPos;      // ToolPoint 기준 박스 위치
    private Quaternion heldLocalRot;   // ToolPoint 기준 박스 회전
    private MaterialPropertyBlock mpb;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    public bool IsHolding => held != null;
    public Rigidbody Held => held;

    void Awake()
    {
        rig = GetComponent<ArmRig>();
        mpb = new MaterialPropertyBlock();
        SetPadColor(padIdleColor);
    }

    /// <summary>패드 근처에서 가장 가까운 박스를 흡착한다. 성공하면 true</summary>
    public bool Grab()
    {
        if (IsHolding) return true;

        var tp = rig.ToolPoint;
        if (tp == null) { Debug.LogError($"[{rig.ArmName}] ToolPoint가 비어 있음", this); return false; }

        var target = Physics.OverlapSphere(tp.position, grabRadius, grabLayers, QueryTriggerInteraction.Ignore)
            .Select(c => c.attachedRigidbody)
            .Where(rb => rb != null && !rb.isKinematic)
            .OrderBy(rb => (rb.ClosestPointOnBounds(tp.position) - tp.position).sqrMagnitude)
            .FirstOrDefault();

        if (target == null)
        {
            Debug.LogWarning($"[{rig.ArmName}] 흡착할 박스가 없음 (반경 {grabRadius} m)", this);
            return false;
        }

        Attach(target);
        return true;
    }

    /// <summary>특정 박스를 바로 흡착 (box_id로 이미 찾은 경우)</summary>
    public void Attach(Rigidbody box)
    {
        var tp = rig.ToolPoint;
        held = box;
        if (!held.isKinematic)   // 레인 끝 칸의 방석은 이미 Kinematic이라 속도를 건드리면 경고가 난다
        {
            held.linearVelocity = Vector3.zero;
            held.angularVelocity = Vector3.zero;
        }
        held.isKinematic = true;

        // 지금 붙은 그대로의 상대 위치를 기억 → 팔을 따라 같이 움직임
        heldLocalPos = Quaternion.Inverse(tp.rotation) * (held.position - tp.position);
        heldLocalRot = Quaternion.Inverse(tp.rotation) * held.rotation;

        heldColliders = held.GetComponentsInChildren<Collider>();
        if (disableCollidersWhileHeld)
            foreach (var c in heldColliders) c.enabled = false;

        SetPadColor(padActiveColor);
    }

    /// <summary>흡착 해제. 놓은 박스를 돌려준다</summary>
    public Rigidbody Release()
    {
        if (!IsHolding) return null;

        var box = held;
        if (heldColliders != null)
            foreach (var c in heldColliders) if (c != null) c.enabled = true;

        box.isKinematic = false;
        box.linearVelocity = Vector3.zero;
        box.angularVelocity = Vector3.zero;

        held = null;
        heldColliders = null;
        SetPadColor(padIdleColor);
        return box;
    }

    void FixedUpdate()
    {
        if (!IsHolding) return;
        var tp = rig.ToolPoint;
        held.MovePosition(tp.position + tp.rotation * heldLocalPos);
        held.MoveRotation(tp.rotation * heldLocalRot);
    }

    void SetPadColor(Color c)
    {
        if (padRenderer == null) return;
        padRenderer.GetPropertyBlock(mpb);
        mpb.SetColor(BaseColorId, c);
        padRenderer.SetPropertyBlock(mpb);
    }

    void OnDrawGizmosSelected()
    {
        var r = GetComponent<ArmRig>();
        if (r == null || r.ToolPoint == null) return;
        Gizmos.color = IsHolding ? Color.green : Color.yellow;
        Gizmos.DrawWireSphere(r.ToolPoint.position, grabRadius);
    }

    // ── 테스트용 ──
    [ContextMenu("테스트: 흡착 ON")]  void TestGrab() => Grab();
    [ContextMenu("테스트: 흡착 OFF")] void TestRelease() => Release();
}
