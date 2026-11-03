// ControlBar.cs : 제어 버튼 8개를 화면 오른쪽 아래에 늘 보이게 모아 둔다 (UI-02, FR-01, 16, 18, 19). 담당 B · scope unity-ui
// Play 때 ControlPanel이 있는 오브젝트(HUD)에 자동으로 붙는다 (씬 수정 없음).
//   윗줄    [Arm1][Arm2][Arm3][Line]   고장 주입 (회색). RUNNING이고 ROS2 연결이 정상일 때만 켜짐 → /factory/inject_fault (FR-19)
//   가운데  [Fix][Emergency]           (주황·빨강)
//   아랫줄  [Start][Clear]             (초록·파랑)
// Start·Emergency·Fix·Clear는 씬의 제어 패널 버튼을 옮기고 색만 바꾼다. 켜고 끄기와 발행은 ControlPanel 그대로.
// 고장 주입 4개는 [Clear] 모양을 복사해 만든다. [Line]이 보내는 장비 id는 conveyor (SRS 토픽 값 그대로).
// ControlPanel이 만든 연결 상태 글자("ROS2 Status")는 숨긴다. 상태 패널 MAIN 탭에 State와 ROS2 LOST가 나오므로 겹친다.
// 꺼진 버튼은 어둡고 반투명하게 보인다 (Color Tint).
// 화면 위쪽 정리 (11/03 B, Main.unity 값은 그대로 두고 Play 때만 덮어씀):
//   Title Panel(Team_Index) 폭 760 → titleWidth(600), 상태 배너(StatusBanner)를 위쪽 정가운데로 (offset x 150 → 0)
//
// 확인 (VM):
//   ros2 topic echo /factory/inject_fault        ← RUNNING에서 [Arm2] 1번에 data: arm_2 1번

using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]   // ControlPanel.Start(Fix에 HoldButton 붙이기, 연결 상태 글자 만들기)가 끝난 뒤에 옮긴다
public class ControlBar : MonoBehaviour
{
    [Tooltip("화면 오른쪽 아래 모서리에서 떨어진 거리 (1920×1080 기준)")]
    [SerializeField] private Vector2 margin = new Vector2(30f, 30f);
    [Tooltip("버튼 묶음 폭")]
    [SerializeField] private float width = 460f;
    [Tooltip("Start·Clear·Fix·Emergency 줄 높이")]
    [SerializeField] private float rowH = 64f;
    [Tooltip("고장 주입 줄 높이")]
    [SerializeField] private float injectRowH = 48f;
    [Tooltip("버튼 사이 간격")]
    [SerializeField] private float gap = 8f;
    [Tooltip("Title Panel(Team_Index) 폭. 0이면 씬 값 그대로")]
    [SerializeField] private float titleWidth = 600f;
    [Tooltip("상태 배너를 위쪽 정가운데로 옮길지")]
    [SerializeField] private bool centerBanner = true;

    static readonly Color StartColor = new Color(0.36f, 0.88f, 0.54f);      // #5BE08A (RUNNING 색)
    static readonly Color EmergencyColor = new Color(0.90f, 0.22f, 0.21f);  // #E53935
    static readonly Color FixColor = new Color(1f, 0.71f, 0.28f);           // #FFB547 (ERROR 색)
    static readonly Color ClearColor = new Color(0.42f, 0.71f, 1f);         // #6CB4FF (IDLE 색)
    static readonly Color InjectColor = new Color(0.62f, 0.65f, 0.70f);     // 회색
    static readonly Color DarkText = new Color(0.05f, 0.06f, 0.08f, 1f);

    static readonly string[] InjectDevices = { "arm_1", "arm_2", "arm_3", "conveyor" };
    static readonly string[] InjectLabels = { "Arm1", "Arm2", "Arm3", "Line" };

    private Button[] injectButtons;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoAttach()
    {
        var panel = FindAnyObjectByType<ControlPanel>();
        if (panel != null && panel.GetComponent<ControlBar>() == null)
            panel.gameObject.AddComponent<ControlBar>();
    }

    void Start()
    {
        var c = GetComponentInParent<Canvas>();
        if (c == null)
        {
            Debug.LogWarning("[ControlBar] Canvas 아래가 아니라 버튼을 옮기지 않음", this);
            enabled = false;
            return;
        }
        var root = c.rootCanvas.transform;
        var start = FindButton(root, "StartButton");
        var emergency = FindButton(root, "Error_Button");
        var fix = FindButton(root, "Fix_Button");
        var clear = FindButton(root, "Clear_Button");

        float half = (width - gap) / 2f;
        float y0 = 0f;                   // [Start] [Clear]
        float y1 = y0 + rowH + gap;      // [Fix] [Emergency]
        float y2 = y1 + rowH + gap;      // 고장 주입 4개

        Place(start, root, 0f, y0, half, rowH);
        Place(clear, root, half + gap, y0, half, rowH);
        Place(fix, root, 0f, y1, half, rowH);
        Place(emergency, root, half + gap, y1, half, rowH);
        Paint(start, StartColor, DarkText);
        Paint(clear, ClearColor, DarkText);
        Paint(fix, FixColor, DarkText);
        Paint(emergency, EmergencyColor, Color.white);

        if (clear != null)
        {
            int n = InjectDevices.Length;
            float w = (width - gap * (n - 1)) / n;
            injectButtons = new Button[n];
            for (int i = 0; i < n; i++)
            {
                var b = Instantiate(clear, root);
                b.name = "Inject_" + InjectDevices[i];
                b.onClick = new Button.ButtonClickedEvent();     // 복사된 [Clear] 동작 지우기
                var label = b.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    float max = label.fontSize;
                    label.text = InjectLabels[i];
                    label.enableAutoSizing = true;
                    label.fontSizeMax = max;
                }
                Place(b, root, (w + gap) * i, y2, w, injectRowH);
                Paint(b, InjectColor, DarkText);
                string device = InjectDevices[i];
                b.onClick.AddListener(() =>
                {
                    var bridge = RosBridge.Instance;
                    if (bridge != null) bridge.PublishInjectFault(device);
                });
                injectButtons[i] = b;
            }
        }
        else Debug.LogWarning("[ControlBar] Clear_Button이 없어 고장 주입 버튼을 만들지 않음", this);

        TidyTop(root);
    }

    /// <summary>Title Panel 폭을 줄이고 상태 배너를 위쪽 정가운데로 (StatusBanner가 Start에서 배너를 만든 뒤라 찾을 수 있다)</summary>
    void TidyTop(Transform root)
    {
        var title = FindRect(root, "Title Panel");
        if (title != null && titleWidth > 0f)
        {
            float cut = title.sizeDelta.x - titleWidth;
            title.sizeDelta = new Vector2(titleWidth, title.sizeDelta.y);
            foreach (RectTransform child in title)   // Title, Subtitle 글자 칸도 같이 줄임
                if (child.anchorMin.x == child.anchorMax.x && child.sizeDelta.x > cut)
                    child.sizeDelta = new Vector2(child.sizeDelta.x - cut, child.sizeDelta.y);
        }
        if (centerBanner)
        {
            var banner = FindRect(root, "Status Banner");
            if (banner != null) banner.anchoredPosition = new Vector2(0f, banner.anchoredPosition.y);
        }

        // 연결 상태 글자는 숨긴다 (상태 패널 MAIN 탭에 State·ROS2 LOST가 나온다)
        var status = FindRect(root, "ROS2 Status");
        if (status != null) status.gameObject.SetActive(false);
    }

    static RectTransform FindRect(Transform root, string name)
    {
        foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
            if (rt.name == name) return rt;
        Debug.LogWarning($"[ControlBar] HUD에서 {name}을(를) 못 찾음", root);
        return null;
    }

    void Update()
    {
        if (injectButtons == null) return;
        var bridge = RosBridge.Instance;
        bool can = bridge != null && bridge.Healthy && bridge.FactoryState == "RUNNING";
        foreach (var b in injectButtons)
            if (b != null && b.interactable != can) b.interactable = can;
    }

    /// <summary>버튼을 묶음 안 (x, y) 칸(묶음 왼쪽 아래 기준), w × h 크기로 옮긴다. 묶음은 화면 오른쪽 아래에 붙는다</summary>
    void Place(Button b, Transform parent, float x, float y, float w, float h)
    {
        if (b == null) return;
        var rt = (RectTransform)b.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(-(margin.x + width - (x + w)), margin.y + y);
        rt.localScale = Vector3.one;
    }

    /// <summary>버튼 바탕색과 글자색. 꺼지면 어둡고 반투명하게 (Color Tint 곱)</summary>
    static void Paint(Button b, Color fill, Color text)
    {
        if (b == null) return;
        if (b.targetGraphic != null) b.targetGraphic.color = fill;
        var cb = b.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        cb.selectedColor = Color.white;
        cb.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.5f);
        b.colors = cb;
        var label = b.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.color = text;
    }

    static Button FindButton(Transform root, string name)
    {
        foreach (var b in root.GetComponentsInChildren<Button>(true))
            if (b.name == name) return b;
        Debug.LogWarning($"[ControlBar] HUD에서 {name} 버튼을 못 찾음", root);
        return null;
    }
}
