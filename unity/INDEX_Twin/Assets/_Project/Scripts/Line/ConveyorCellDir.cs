using UnityEngine;

/// <summary>
/// 칸 하나의 흐름 방향을 직접 지정할 때 그 칸에 붙인다 (선택).
/// 코너가 반대로 꺾이거나, 한 구간 안에서 특정 칸만 방향이 다를 때 사용.
/// 선택하면 칸 위에 노란 화살표로 방향이 보인다.
/// </summary>
[DisallowMultipleComponent]
public class ConveyorCellDir : MonoBehaviour
{
    [Tooltip("이 칸에서 박스를 미는 방향 (이 오브젝트 로컬 축)")]
    [SerializeField] private Vector3 localDirection = Vector3.forward;

    public Vector3 LocalDirection => localDirection;

    void OnDrawGizmosSelected()
    {
        Vector3 d = transform.TransformDirection(localDirection);
        d.y = 0f;
        if (d.sqrMagnitude < 1e-6f) return;
        d.Normalize();
        Vector3 p = transform.position + Vector3.up * 0.6f;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(p, p + d * 0.5f);
        Gizmos.DrawSphere(p + d * 0.5f, 0.04f);
    }
}
