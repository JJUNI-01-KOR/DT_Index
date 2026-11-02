// 상태 패널 (UI-03, FR-20). 담당 A · scope unity-ui
// /factory/state와 /factory/stats(JSON, SRS 부록 A.4)만 보고 표시한다. Unity에서 따로 세지 않는다.
// ROS를 직접 부르지 않고 RosBridge 이벤트만 구독한다 (CONVENTIONS 3절).
//
// 표시 (오른쪽 위)
//   STATUS   공정 상태(색), E_STOP 원인, 고장 장비
//   PRODUCTION  Bin left, Fed, OK, NG, Last(id · kg · OK/NG)
//   LINE     Main, OK lane, NG lane
//   PALLETS  Pallet 1, Pallet 2, Queue
//   AGV1     Command, Loaded, Towing, Cycle
//   맨 아래  [Fix] [Clear] 버튼 (제어 패널 버튼을 이 패널로 옮겨 붙임, 동작은 ControlPanel 그대로), 마지막으로 받은 뒤 지난 시간
// 열고 닫기 (11/02 A)
//   처음에는 닫혀 있다. 로봇팔이나 AGV를 클릭하면 열리고 제목에 그 장비 이름이 나온다. [X]나 같은 장비를 다시 클릭하면 닫힌다.
//   E_STOP·ERROR가 되면 [Clear]·[Fix]를 누를 수 있게 저절로 열린다 (autoOpenOnAlarm). 위쪽 가운데 배너는 늘 보인다.
//   클릭 판정은 각 장비의 Renderer 경계 상자와 마우스 광선이 만나는지로 본다 (콜라이더가 없어도 됨). UI 위 클릭은 무시.
// /factory/stats를 staleSec(3초) 넘게 못 받으면 값을 모두 회색으로 바꾼다 (FR-20 예외 처리).
// TMP 기본 글꼴에 한글이 없어 영어로 쓴다 (ControlPanel과 같음).
//
// 준비: HUD(Canvas)에 이 스크립트를 붙인다. 패널은 Play 때 코드로 만든다 (씬에 UI 오브젝트를 두지 않음).
//       글꼴은 같은 Canvas의 TMP 글자에서 가져온다. 씬에 RosBridge가 하나 있어야 한다.

using System;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class StatusPanel : MonoBehaviour
{
    [Tooltip("이 시간(초) 넘게 /factory/stats를 못 받으면 값을 회색으로")]
    [SerializeField] private float staleSec = 3f;
    [Tooltip("패널 크기 (1920×1080 기준)")]
    [SerializeField] private Vector2 size = new Vector2(430f, 820f);
    [Tooltip("오른쪽 위 모서리에서 떨어진 거리")]
    [SerializeField] private Vector2 margin = new Vector2(32f, 32f);
    [SerializeField] private float fontSize = 21f;
    [Tooltip("패널로 옮겨 붙일 [Fix] 버튼 (비우면 HUD에서 이름 Fix_Button으로 찾음)")]
    [SerializeField] private Button fixButton;
    [Tooltip("패널로 옮겨 붙일 [Clear] 버튼 (비우면 HUD에서 이름 Clear_Button으로 찾음)")]
    [SerializeField] private Button clearButton;
    [Tooltip("처음에 닫아 둘지 (로봇팔·AGV를 클릭하면 열림)")]
    [SerializeField] private bool startHidden = true;
    [Tooltip("E_STOP·ERROR가 되면 저절로 열지")]
    [SerializeField] private bool autoOpenOnAlarm = true;

    // ── /factory/stats (부록 A.4) ──
    [Serializable] public class LastGrade { public string id; public float kg; public string lane; }
    [Serializable] public class AgvInfo { public string cmd; public bool loaded; public int pallet; }
    [Serializable]
    public class Stats
    {
        public string state, fault, estop;
        public int bin_left, fed, ok, ng;
        public LastGrade last;
        public int main, lane_ok, lane_ng, pallet_1, pallet_2;
        public int[] queue;
        public AgvInfo agv;
        public float agv_cycle_sec;
    }

    /// <summary>마지막으로 해석한 stats (없으면 null)</summary>
    public Stats Last { get; private set; }
    /// <summary>패널이 열려 있는지</summary>
    public bool IsOpen => panelRoot != null && panelRoot.gameObject.activeSelf;
    /// <summary>/factory/stats를 staleSec 넘게 못 받았는지</summary>
    public bool Stale => lastStatsTime < 0f || Time.realtimeSinceStartup - lastStatsTime > staleSec;

    // 색
    static readonly Color PanelColor = new Color(0.03f, 0.05f, 0.08f, 0.74f);   // Title Panel과 같음
    static readonly Color AccentColor = new Color(1f, 0.55f, 0.15f, 1f);
    const string Grey = "#7C8591";
    const string LabelColor = "#B8C9E0";
    const string ValueColor = "#FFFFFF";
    const string OkColor = "#5BE08A";
    const string NgColor = "#FF6B6B";
    const string WarnColor = "#FFB547";

    private RosBridge bridge;
    private TMP_Text body;
    private TMP_Text footer;
    private float lastStatsTime = -1f;
    private string lastJson = "";
    private string shown = "";
    private float nextFooter;
    private RectTransform panelRoot;
    private TMP_Text titleText;
    private Component selected;          // 클릭해서 연 장비 (ArmRig 또는 AGV). 알람으로 열렸으면 null
    private string lastAlarmState = "";

    void Start()
    {
        BuildUi();
        Hook();
        if (panelRoot != null && startHidden) panelRoot.gameObject.SetActive(false);
        Render();
    }

    void OnDestroy()
    {
        if (bridge != null) bridge.StatsReceived -= OnStats;
    }

    void Hook()
    {
        if (bridge != null || RosBridge.Instance == null) return;
        bridge = RosBridge.Instance;
        bridge.StatsReceived += OnStats;
        if (!string.IsNullOrEmpty(bridge.LastStatsJson)) OnStats(bridge.LastStatsJson);
    }

    void Update()
    {
        Hook();
        HandleClick();
        AutoOpen();
        // 1초마다 다시 그림: 회색 전환·"지난 시간"·상태 변화 반영
        if (Time.unscaledTime >= nextFooter)
        {
            nextFooter = Time.unscaledTime + 0.25f;
            Render();
        }
    }

    void OnStats(string json)
    {
        lastStatsTime = Time.realtimeSinceStartup;
        if (json == lastJson) return;
        lastJson = json;
        try
        {
            Last = JsonUtility.FromJson<Stats>(StripNulls(json));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[StatusPanel] /factory/stats JSON 해석 실패: {e.Message}", this);
        }
        Render();
    }

    /// <summary>JsonUtility는 null을 못 읽는 경우가 있어 "키": null 을 지운다 (지운 값은 기본값 = 없음)</summary>
    static string StripNulls(string json)
    {
        string s = Regex.Replace(json, "\"\\w+\"\\s*:\\s*null\\s*,?", "");
        return Regex.Replace(s, ",\\s*}", "}");
    }

    // ───────────────────────── 그리기 ─────────────────────────

    void Render()
    {
        if (body == null) return;
        bool stale = Stale;
        var s = Last;
        string state = bridge != null && bridge.FactoryState != "" ? bridge.FactoryState : s?.state;
        bool lost = bridge != null && bridge.RosLost;

        var sb = new StringBuilder();
        Section(sb, "STATUS");
        Row(sb, "State", string.IsNullOrEmpty(state) ? "-" : state, stale && !lost ? null : StateColor(state));
        if (lost) Row(sb, "Cause", "ROS2 LOST", NgColor);
        else if (state == "E_STOP") Row(sb, "Cause", EStopText(s?.estop), NgColor);
        if (s != null && !string.IsNullOrEmpty(s.fault)) Row(sb, "Fault", s.fault, WarnColor);

        Section(sb, "PRODUCTION");
        Row(sb, "Bin left", N(s?.bin_left));
        Row(sb, "Fed", N(s?.fed));
        Row(sb, "OK", N(s?.ok), OkColor);
        Row(sb, "NG", N(s?.ng), NgColor);
        Row(sb, "Last", LastText(s?.last), s?.last != null && !string.IsNullOrEmpty(s.last.id) ? (s.last.lane == "ok" ? OkColor : NgColor) : null);

        Section(sb, "LINE");
        Row(sb, "Main", N(s?.main));
        Row(sb, "OK lane", N(s?.lane_ok));
        Row(sb, "NG lane", N(s?.lane_ng));

        Section(sb, "PALLETS");
        Row(sb, "Pallet 1", N(s?.pallet_1));
        Row(sb, "Pallet 2", N(s?.pallet_2));
        Row(sb, "Queue", QueueText(s?.queue));

        Section(sb, "AGV1");
        var a = s?.agv;
        Row(sb, "Command", a == null || string.IsNullOrEmpty(a.cmd) ? "-" : a.cmd);
        Row(sb, "Loaded", a == null ? "-" : a.loaded ? "yes" : "no");
        Row(sb, "Towing", a == null || a.pallet <= 0 ? "-" : $"Pallet {a.pallet}");
        Row(sb, "Cycle", s == null || s.agv_cycle_sec <= 0f ? "-" : $"{s.agv_cycle_sec:F1} s");

        string text = sb.ToString();
        if (stale) text = Regex.Replace(text, "<color=#[0-9A-Fa-f]{6}>", $"<color={Grey}>");
        if (text != shown)
        {
            body.text = text;
            shown = text;
        }

        if (footer != null)
        {
            string f = lastStatsTime < 0f ? "waiting for /factory/stats"
                : stale ? $"no /factory/stats for {Time.realtimeSinceStartup - lastStatsTime:F0} s"
                : $"updated {Time.realtimeSinceStartup - lastStatsTime:F1} s ago";
            footer.text = f;
            footer.color = stale ? HexColor(NgColor) : HexColor(Grey);
        }
    }

    static void Section(StringBuilder sb, string title)
    {
        if (sb.Length > 0) sb.Append("<size=45%> </size>\n");
        sb.Append($"<color=#FF8C26><b>{title}</b></color>\n");
    }

    static void Row(StringBuilder sb, string label, string value, string color = null)
    {
        sb.Append($"<color={LabelColor}>  {label}</color><pos=48%><color={color ?? ValueColor}>{value}</color>\n");
    }

    static string N(int? v) => v.HasValue ? v.Value.ToString() : "-";

    static string LastText(LastGrade l)
    {
        if (l == null || string.IsNullOrEmpty(l.id)) return "-";
        return $"{l.id}  {l.kg:F2} kg  {(l.lane == "ok" ? "OK" : "NG")}";
    }

    static string QueueText(int[] q)
    {
        if (q == null || q.Length == 0) return "empty";
        var sb = new StringBuilder();
        for (int i = 0; i < q.Length; i++) sb.Append(i == 0 ? "" : " > ").Append("P").Append(q[i]);
        return sb.ToString();
    }

    /// <summary>E_STOP 원인 (SRS 4.1, 부록 A.4)</summary>
    public static string EStopText(string estop)
    {
        switch (estop)
        {
            case "button": return "Emergency button";
            case "heartbeat": return "Communication lost";
            case "timeout_weight": return "Weight report timeout";
            case "timeout_agv": return "AGV1 timeout";
            case "startup": return "factory_manager started";
            case null: case "": return "-";
            default: return estop;
        }
    }

    public static string StateColor(string state)
    {
        switch (state)
        {
            case "RUNNING": return OkColor;
            case "IDLE": return "#6CB4FF";
            case "ERROR": return WarnColor;
            case "E_STOP": return NgColor;
            default: return ValueColor;
        }
    }

    static Color HexColor(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;

    // ───────────────────────── 열고 닫기 ─────────────────────────

    /// <summary>패널을 연다. device가 있으면 제목에 그 장비 이름</summary>
    public void Open(Component device = null)
    {
        if (panelRoot == null) return;
        selected = device;
        if (titleText != null) titleText.text = DeviceName(device);
        panelRoot.gameObject.SetActive(true);
        Render();
    }

    public void Close()
    {
        if (panelRoot == null) return;
        panelRoot.gameObject.SetActive(false);
        selected = null;
    }

    /// <summary>로봇팔·AGV 클릭 → 열기. 같은 장비를 다시 클릭하면 닫기</summary>
    void HandleClick()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es != null && es.IsPointerOverGameObject()) return;     // UI 위 클릭
        var cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        Component hit = null;
        float best = float.MaxValue;
        foreach (var arm in FindObjectsByType<ArmRig>(FindObjectsSortMode.None)) Test(arm, ray, ref hit, ref best);
        foreach (var agv in FindObjectsByType<AgvWaypointDriver>(FindObjectsSortMode.None)) Test(agv, ray, ref hit, ref best);
        if (hit == null) return;

        if (IsOpen && selected == hit) Close();
        else Open(hit);
    }

    /// <summary>device 아래 Renderer 중 광선과 가장 가까이 만나는 거리</summary>
    static void Test(Component device, Ray ray, ref Component hit, ref float best)
    {
        foreach (var r in device.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (r.bounds.IntersectRay(ray, out float d) && d < best) { best = d; hit = device; }
        }
    }

    /// <summary>E_STOP·ERROR가 되는 순간 한 번 연다 ([Clear]·[Fix]가 패널 안에 있으므로)</summary>
    void AutoOpen()
    {
        if (!autoOpenOnAlarm || bridge == null) return;
        string st = bridge.FactoryState;
        bool alarm = st == "E_STOP" || st == "ERROR";
        if (alarm && st != lastAlarmState && !IsOpen) Open();
        lastAlarmState = alarm ? st : "";
    }

    static string DeviceName(Component device)
    {
        if (device is ArmRig arm) return $"ROBOT ARM {arm.ArmId}";
        if (device is AgvWaypointDriver) return "AGV1";
        return "FACTORY";
    }

    // ───────────────────────── UI 만들기 ─────────────────────────

    void BuildUi()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { Debug.LogError("[StatusPanel] Canvas 아래에 붙여야 함 (HUD)", this); enabled = false; return; }
        TMP_FontAsset font = null;
        var sample = canvas.GetComponentInChildren<TMP_Text>(true);
        if (sample != null) font = sample.font;

        var panel = NewRect("Status Panel", canvas.transform);
        panelRoot = panel;
        panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-margin.x, -margin.y);
        panel.sizeDelta = size;
        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = PanelColor;
        bg.raycastTarget = false;

        var accent = NewRect("Accent", panel);
        accent.anchorMin = new Vector2(0f, 0f);
        accent.anchorMax = new Vector2(0f, 1f);
        accent.pivot = new Vector2(0f, 0.5f);
        accent.sizeDelta = new Vector2(8f, 0f);
        accent.anchoredPosition = Vector2.zero;
        var ai = accent.gameObject.AddComponent<Image>();
        ai.color = AccentColor;
        ai.raycastTarget = false;

        var b = NewRect("Body", panel);
        b.anchorMin = new Vector2(0f, 0f);
        b.anchorMax = new Vector2(1f, 1f);
        b.offsetMin = new Vector2(26f, 44f + ButtonRowH + 12f);
        b.offsetMax = new Vector2(-18f, -(TitleH + 10f));
        body = NewText(b, font, fontSize, TextAlignmentOptions.TopLeft);

        // 제목 줄: 장비 이름 + [X]
        var t = NewRect("Title", panel);
        t.anchorMin = new Vector2(0f, 1f);
        t.anchorMax = new Vector2(1f, 1f);
        t.pivot = new Vector2(0.5f, 1f);
        t.offsetMin = new Vector2(26f, -TitleH - 8f);
        t.offsetMax = new Vector2(-60f, -8f);
        titleText = NewText(t, font, fontSize * 1.25f, TextAlignmentOptions.MidlineLeft);
        titleText.fontStyle = FontStyles.Bold;
        titleText.text = "FACTORY";

        var x = NewRect("Close", panel);
        x.anchorMin = x.anchorMax = new Vector2(1f, 1f);
        x.pivot = new Vector2(1f, 1f);
        x.anchoredPosition = new Vector2(-10f, -10f);
        x.sizeDelta = new Vector2(40f, 40f);
        var xi = x.gameObject.AddComponent<Image>();
        xi.color = new Color(1f, 1f, 1f, 0.08f);
        var xb = x.gameObject.AddComponent<Button>();
        xb.onClick.AddListener(Close);
        var xt = NewText(NewRect("X", x), font, fontSize * 1.1f, TextAlignmentOptions.Center);
        var xtr = (RectTransform)xt.transform;
        xtr.anchorMin = Vector2.zero; xtr.anchorMax = Vector2.one; xtr.offsetMin = xtr.offsetMax = Vector2.zero;
        xt.text = "X";

        // [Fix] [Clear]: 제어 패널 버튼을 이 패널 아래로 옮긴다 (ControlPanel이 계속 켜고 끄고 발행함)
        if (fixButton == null) fixButton = FindButton(canvas.transform, "Fix_Button");
        if (clearButton == null) clearButton = FindButton(canvas.transform, "Clear_Button");
        Dock(fixButton, panel, 0f, 0.5f);
        Dock(clearButton, panel, 0.5f, 1f);

        var f = NewRect("Footer", panel);
        f.anchorMin = new Vector2(0f, 0f);
        f.anchorMax = new Vector2(1f, 0f);
        f.pivot = new Vector2(0.5f, 0f);
        f.offsetMin = new Vector2(26f, 12f);
        f.offsetMax = new Vector2(-18f, 40f);
        footer = NewText(f, font, fontSize * 0.75f, TextAlignmentOptions.BottomLeft);
    }

    const float TitleH = 40f;
    const float ButtonRowH = 56f;

    /// <summary>버튼을 패널 아래 줄 [x0, x1] (패널 폭 비율) 칸으로 옮긴다</summary>
    static void Dock(Button button, RectTransform panel, float x0, float x1)
    {
        if (button == null) return;
        var rt = (RectTransform)button.transform;
        rt.SetParent(panel, false);
        rt.anchorMin = new Vector2(x0, 0f);
        rt.anchorMax = new Vector2(x1, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.offsetMin = new Vector2(x0 == 0f ? 26f : 6f, 44f);
        rt.offsetMax = new Vector2(x1 == 1f ? -18f : -6f, 44f + ButtonRowH);
        rt.localScale = Vector3.one;
    }

    static Button FindButton(Transform root, string name)
    {
        foreach (var b in root.GetComponentsInChildren<Button>(true))
            if (b.name == name) return b;
        Debug.LogWarning($"[StatusPanel] HUD에서 {name} 버튼을 못 찾음", root);
        return null;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static TMP_Text NewText(RectTransform rt, TMP_FontAsset font, float size, TextAlignmentOptions align)
    {
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Truncate;
        t.raycastTarget = false;
        return t;
    }
}
