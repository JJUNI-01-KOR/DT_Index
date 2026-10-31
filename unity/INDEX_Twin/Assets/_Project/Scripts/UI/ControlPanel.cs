// 제어 패널 (UI-02, FR-01, FR-16, FR-18). 담당 D · scope unity-ui
// ROS를 직접 부르지 않고 RosBridge를 통해 발행한다 (CONVENTIONS 3절).
//
// 버튼 4개 (10/31 결정: 비상정지와 해제를 버튼 2개로 나눔)
//   [Start]      /factory/start true            IDLE일 때만 누를 수 있음
//   [Emergency]  /factory/emergency_stop true   E_STOP이 아닐 때 누를 수 있음
//   [Clear]      /factory/emergency_stop false  E_STOP일 때만 누를 수 있음 (해제 → factory_manager가 IDLE로)
//   [Fix]        /factory/fault_clear (장비 id)  ERROR이고 고장 장비가 있을 때만. 3초 동안 누르고 있어야 보냄 (HoldButton)
//                고장 장비는 /factory/stats의 fault (RosBridge.CurrentFault). 버튼 글자에 장비와 남은 시간을 보여 준다
// 버튼 상태는 내가 누른 것으로 정하지 않고 /factory/state(RosBridge.FactoryState)를 보고 매 프레임 정한다.
// factory_manager가 E_STOP으로 시작하므로 처음 화면은 [Clear]만 켜져 있다. 통신 끊김·시간 초과로 E_STOP이 돼도 같다.
// ROS2와 연결이 안 돼 있으면 모든 버튼이 꺼진다. statusText를 연결하면 "ROS2 연결 · 상태"를 보여 준다 (선택).
//
// 준비: HUD에 이 스크립트를 붙이고 버튼 4개를 끌어 놓는다. [Fix]에는 HoldButton이 없으면 Play 때 자동으로 붙는다.
//       씬에 RosBridge가 하나 있어야 한다.

using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ControlPanel : MonoBehaviour
{
    [Tooltip("[Start] 버튼. 누르면 /factory/start true 1번")]
    [SerializeField] private Button startButton;
    [Tooltip("[Emergency] 버튼. 누르면 /factory/emergency_stop true")]
    [SerializeField] private Button emergencyButton;
    [Tooltip("[Clear] 버튼 (해제). 누르면 /factory/emergency_stop false")]
    [SerializeField] private Button clearButton;
    [Tooltip("[Fix] 버튼 (3초 조치). ERROR일 때 3초 누르면 /factory/fault_clear(고장 장비)")]
    [SerializeField] private Button fixButton;
    [Tooltip("(선택) ROS2 연결과 공정 상태를 보여 줄 글자")]
    [SerializeField] private TMP_Text statusText;

    private HoldButton fixHold;
    private TMP_Text fixLabel;
    private string fixLabelDefault = "";
    private string shownStatus;

    void Start()
    {
        if (startButton == null || emergencyButton == null || clearButton == null)
        {
            Debug.LogError("[ControlPanel] Start·Emergency·Clear 버튼 칸 중 비어 있는 것이 있음", this);
            enabled = false;
            return;
        }
        startButton.onClick.AddListener(OnStartClicked);
        emergencyButton.onClick.AddListener(OnEmergencyClicked);
        clearButton.onClick.AddListener(OnClearClicked);

        if (fixButton != null)
        {
            fixHold = fixButton.GetComponent<HoldButton>();
            if (fixHold == null) fixHold = fixButton.gameObject.AddComponent<HoldButton>();
            fixHold.Completed += OnFixHeld;
            fixLabel = fixButton.GetComponentInChildren<TMP_Text>(true);
            if (fixLabel != null) fixLabelDefault = fixLabel.text;
        }
        Refresh();
    }

    void OnDestroy()
    {
        if (startButton != null) startButton.onClick.RemoveListener(OnStartClicked);
        if (emergencyButton != null) emergencyButton.onClick.RemoveListener(OnEmergencyClicked);
        if (clearButton != null) clearButton.onClick.RemoveListener(OnClearClicked);
        if (fixHold != null) fixHold.Completed -= OnFixHeld;
    }

    void Update() => Refresh();

    /// <summary>공정 상태와 연결 상태로 버튼을 켜고 끈다</summary>
    void Refresh()
    {
        var bridge = RosBridge.Instance;
        bool connected = bridge != null && bridge.Connected;
        string state = bridge != null ? bridge.FactoryState : "";
        string fault = bridge != null ? bridge.CurrentFault : "";

        startButton.interactable = connected && state == "IDLE";
        emergencyButton.interactable = connected && state != "E_STOP";
        clearButton.interactable = connected && state == "E_STOP";

        if (fixButton != null)
        {
            bool canFix = connected && state == "ERROR" && fault != "";
            fixButton.interactable = canFix;
            if (fixLabel != null)
            {
                string label = !canFix ? fixLabelDefault
                    : fixHold != null && fixHold.Holding ? $"{fault} {fixHold.Remaining:F1}s"
                    : $"Fix {fault}";
                if (fixLabel.text != label) fixLabel.text = label;
            }
        }

        if (statusText != null)
        {
            string s = !connected ? "ROS2 끊김" : $"ROS2 연결 · {(state == "" ? "상태 기다리는 중" : state)}";
            if (s != shownStatus) { statusText.text = s; shownStatus = s; }
        }
    }

    void OnStartClicked()
    {
        if (!HasBridge()) return;
        RosBridge.Instance.PublishStart();
    }

    void OnEmergencyClicked()
    {
        if (!HasBridge()) return;
        RosBridge.Instance.PublishEmergencyStop(true);
    }

    void OnClearClicked()
    {
        if (!HasBridge()) return;
        RosBridge.Instance.PublishEmergencyStop(false);
    }

    void OnFixHeld()
    {
        if (!HasBridge()) return;
        RosBridge.Instance.PublishFaultClear(RosBridge.Instance.CurrentFault);
    }

    bool HasBridge()
    {
        if (RosBridge.Instance != null) return true;
        Debug.LogError("[ControlPanel] 씬에 RosBridge가 없음 (빈 오브젝트에 RosBridge 컴포넌트를 붙인다)", this);
        return false;
    }
}
