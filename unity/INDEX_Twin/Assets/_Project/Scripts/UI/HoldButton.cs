// HoldButton.cs : 길게 누르기 버튼 (FR-18 3초 조치). 담당 D · scope unity-ui
// 버튼(Selectable)과 같은 오브젝트에 붙인다. holdSec 동안 손을 떼지 않고 누르고 있으면 Completed를 1번 부른다.
// 중간에 떼거나 버튼 밖으로 나가거나 버튼이 꺼지면 처음부터 다시.

using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [SerializeField] private float holdSec = 3f;

    /// <summary>holdSec을 다 채웠을 때 1번</summary>
    public event Action Completed;

    public bool Holding { get; private set; }

    /// <summary>남은 시간 (초). 누르고 있지 않으면 holdSec</summary>
    public float Remaining => Holding ? Mathf.Max(0f, holdSec - (Time.unscaledTime - downAt)) : holdSec;

    private Selectable selectable;
    private float downAt;

    void Awake() => selectable = GetComponent<Selectable>();

    public void OnPointerDown(PointerEventData e)
    {
        if (selectable != null && !selectable.IsInteractable()) return;
        Holding = true;
        downAt = Time.unscaledTime;
    }

    public void OnPointerUp(PointerEventData e) => Holding = false;
    public void OnPointerExit(PointerEventData e) => Holding = false;

    void Update()
    {
        if (!Holding) return;
        if (selectable != null && !selectable.IsInteractable()) { Holding = false; return; }
        if (Time.unscaledTime - downAt >= holdSec)
        {
            Holding = false;
            Completed?.Invoke();
        }
    }
}
