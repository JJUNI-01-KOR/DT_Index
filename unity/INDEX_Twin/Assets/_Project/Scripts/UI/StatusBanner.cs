// 경고 배너 (UI-03 경고 배너, FR-16 ~ FR-18). 담당 A · scope unity-ui
// 화면 위쪽 가운데에 지금 해야 할 일을 한 줄로 띄운다. RUNNING이고 문제가 없으면 숨긴다.
//
//   ROS2 연결 안 됨             회색  "ROS2 disconnected"
//   Heartbeat 끊김 (FR-17)     빨강  "ROS2 LOST - no heartbeat 3 s -> E-STOP"
//   E_STOP (FR-16)             빨강  "E-STOP : <원인> - press Clear"   원인은 /factory/stats의 estop
//   ERROR (FR-18)              주황  "ERROR : <고장 장비> - hold Fix 3 s"
//   /factory/stats 3초 못 받음  회색  "No /factory/stats for 3 s"
//   IDLE                       파랑  "IDLE - press Start"
// 판단은 하지 않고 RosBridge(FactoryState, RosLost, Connected, CurrentFault)와 StatusPanel 값만 보고 고른다.
// TMP 기본 글꼴에 한글이 없어 영어로 쓴다.
//
// 준비: HUD(Canvas)에 이 스크립트를 붙인다. 배너는 Play 때 코드로 만든다. StatusPanel이 같은 HUD에 있으면 stats 끊김도 띄운다.

using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class StatusBanner : MonoBehaviour
{
    [Tooltip("배너 크기 (1920×1080 기준). 왼쪽 위 타이틀과 오른쪽 위 상태 패널 사이")]
    [SerializeField] private Vector2 size = new Vector2(600f, 56f);
    [Tooltip("화면 위쪽 가운데에서 떨어진 거리 (x는 오른쪽으로 민 값)")]
    [SerializeField] private Vector2 offset = new Vector2(150f, -32f);
    [SerializeField] private float fontSize = 24f;
    [Tooltip("빨강·주황 배너를 깜빡일지")]
    [SerializeField] private bool blink = true;

    static readonly Color Red = new Color(0.78f, 0.12f, 0.12f, 0.92f);
    static readonly Color Amber = new Color(0.90f, 0.52f, 0.08f, 0.92f);
    static readonly Color Blue = new Color(0.12f, 0.36f, 0.70f, 0.85f);
    static readonly Color Grey = new Color(0.25f, 0.27f, 0.30f, 0.90f);

    private RectTransform root;
    private Image bg;
    private TMP_Text label;
    private StatusPanel panel;
    private string shown;

    void Start()
    {
        BuildUi();
        panel = GetComponent<StatusPanel>();
    }

    void Update()
    {
        if (root == null) return;
        Pick(out string text, out Color color, out bool urgent);

        bool show = text != null;
        if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
        if (!show) { shown = null; return; }

        if (text != shown) { label.text = text; shown = text; }
        float a = urgent && blink ? Mathf.Lerp(0.65f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f)) : 1f;
        color.a *= a;
        bg.color = color;
    }

    /// <summary>지금 띄울 배너. 없으면 text = null</summary>
    void Pick(out string text, out Color color, out bool urgent)
    {
        text = null; color = Grey; urgent = false;
        var bridge = RosBridge.Instance;
        // 11/05 BUG-002: 연결됐다가 끊기면(Heartbeat 3초) TCP도 같이 끊겨 Connected가 false가 되므로 RosLost를 먼저 본다.
        //   한 번도 연결 안 된 경우(RosLost는 첫 수신 전에는 false)만 "ROS2 disconnected" (TC_DT_003 · TC_DT_041)
        if (bridge != null && bridge.RosLost) { text = "ROS2 LOST - no heartbeat 3 s -> E-STOP"; color = Red; urgent = true; return; }
        if (bridge == null || !bridge.Connected) { text = "ROS2 disconnected"; return; }

        string state = bridge.FactoryState;
        if (state == "E_STOP")
        {
            string cause = StatusPanel.EStopText(panel != null && panel.Last != null ? panel.Last.estop : null);
            text = cause == "-" ? "E-STOP - press Clear" : $"E-STOP : {cause} - press Clear";
            color = Red; urgent = true; return;
        }
        if (state == "ERROR")
        {
            string fault = bridge.CurrentFault;
            text = string.IsNullOrEmpty(fault) ? "ERROR - waiting for fault device" : $"ERROR : {fault} - hold Fix 3 s";
            color = Amber; urgent = true; return;
        }
        if (panel != null && panel.Stale && state != "") { text = "No /factory/stats for 3 s"; return; }
        if (state == "IDLE") { text = "IDLE - press Start"; color = Blue; return; }
        if (state == "") { text = "Waiting for /factory/state"; return; }
        // RUNNING: 숨김
    }

    void BuildUi()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { Debug.LogError("[StatusBanner] Canvas 아래에 붙여야 함 (HUD)", this); enabled = false; return; }
        var sample = canvas.GetComponentInChildren<TMP_Text>(true);

        var go = new GameObject("Status Banner", typeof(RectTransform));
        root = (RectTransform)go.transform;
        root.SetParent(canvas.transform, false);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.anchoredPosition = offset;
        root.sizeDelta = size;
        bg = go.AddComponent<Image>();
        bg.raycastTarget = false;

        var tg = new GameObject("Text", typeof(RectTransform));
        var trt = (RectTransform)tg.transform;
        trt.SetParent(root, false);
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(16f, 0f);
        trt.offsetMax = new Vector2(-16f, 0f);
        label = tg.AddComponent<TextMeshProUGUI>();
        if (sample != null) label.font = sample.font;
        label.fontSize = fontSize;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        go.SetActive(false);
    }
}
