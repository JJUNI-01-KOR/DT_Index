// 제어 패널 (UI-02, FR-01). 담당 D · scope unity-ui
// M1: [동작] 버튼만 연결한다. 나머지([비상정지], [불량 팔레트 정리], [출고 팔레트 정리],
//     [고장 주입], [조치 완료])와 상태별 활성화 규칙은 5.4에서 붙인다.
// ROS를 직접 부르지 않고 RosBridge를 통해 발행한다 (CONVENTIONS 3절).
//
// 준비: Canvas(또는 패널 오브젝트)에 이 스크립트를 붙이고 Start Button 칸에 [동작] 버튼을 끌어 놓는다.
//       씬에는 RosBridge 오브젝트가 하나 있어야 한다.

using UnityEngine;
using UnityEngine.UI;

public class ControlPanel : MonoBehaviour
{
    [Tooltip("[동작] 버튼. 누르면 /factory/start true 1번")]
    [SerializeField] private Button startButton;

    void Start()
    {
        if (startButton == null)
        {
            Debug.LogError("[ControlPanel] Start Button 칸이 비어 있음", this);
            enabled = false;
            return;
        }
        startButton.onClick.AddListener(OnStartClicked);
    }

    void OnDestroy()
    {
        if (startButton != null) startButton.onClick.RemoveListener(OnStartClicked);
    }

    void OnStartClicked()
    {
        if (RosBridge.Instance == null)
        {
            Debug.LogError("[ControlPanel] 씬에 RosBridge가 없음 (빈 오브젝트에 RosBridge 컴포넌트를 붙인다)", this);
            return;
        }
        RosBridge.Instance.PublishStart();
    }
}
