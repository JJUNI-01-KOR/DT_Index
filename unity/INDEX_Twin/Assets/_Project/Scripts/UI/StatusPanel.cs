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
// 탭 4개 (11/03 A)
//   [MAIN] STATUS+PRODUCTION · [LINE] · [PALLETS] · [AGV1]. 장비를 클릭해 열면 MAIN부터 보인다. 탭을 눌러 바꾼다.
//   탭 아이콘은 Play 때 코드로 그린다 (이미지 파일 없음). E_STOP·ERROR면 MAIN 탭 아이콘이 빨갛게.
// 단축키 (11/03 A)
//   Tab = 패널 열기/닫기 · 1~4 = MAIN/LINE/PALLETS/AGV1 탭 (닫혀 있으면 열면서) · Esc = 닫기
//   카메라(WASD·Q/E·화살표)와 메뉴로 돌아가기(M)는 피했다.
// 열고 닫기 (11/02 A)
//   장비를 클릭해 열면 SelectionChanged로 알린다 → CameraFocus가 카메라를 그 장비로 당긴다 (11/03 A)
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

    [Tooltip("파레트 적재 기준 (factory.yaml pallet_capacity)")]
    [SerializeField] private int palletCapacity = 5;
    [Tooltip("라인 투입 중지 기준 (factory.yaml lane_capacity_count)")]
    [SerializeField] private int laneCapacity = 15;

    [Tooltip("Tab·1~4·Esc 단축키 사용")]
    [SerializeField] private bool enableHotkeys = true;

    public enum Tab { Main, Line, Pallets, Agv }
    static readonly string[] TabNames = { "MAIN", "LINE", "PALLETS", "AGV1" };

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
    /// <summary>클릭해서 연 장비 (ArmRig 또는 AgvWaypointDriver). 닫혔거나 알람으로 열렸으면 null</summary>
    public Component Selected => selected;
    /// <summary>선택한 장비가 바뀔 때 (열기: 장비, 닫기·알람 열기: null). CameraFocus가 구독 (11/03 A)</summary>
    public event Action<Component> SelectionChanged;
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
    private Tab tab = Tab.Main;
    private Image[] tabBg;
    private Image[] tabIcon;
    private TMP_Text[] tabLabel;

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
        HandleKeys();
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
        switch (tab)
        {
            case Tab.Main:
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
                Row(sb, "Yield", YieldText(s), null);
                Row(sb, "Last", LastText(s?.last), s?.last != null && !string.IsNullOrEmpty(s.last.id) ? (s.last.lane == "ok" ? OkColor : NgColor) : null);
                break;

            case Tab.Line:
                Section(sb, "LINE");
                Row(sb, "Main", N(s?.main));
                Row(sb, "OK lane", N(s?.lane_ok), OkColor);
                Row(sb, "NG lane", N(s?.lane_ng), NgColor);
                Section(sb, "LOAD");
                Row(sb, "Main + OK", Fill(s == null ? (int?)null : s.main + s.lane_ok, laneCapacity), FillColor(s == null ? 0 : s.main + s.lane_ok, laneCapacity));
                Row(sb, "Main + NG", Fill(s == null ? (int?)null : s.main + s.lane_ng, laneCapacity), FillColor(s == null ? 0 : s.main + s.lane_ng, laneCapacity));
                break;

            case Tab.Pallets:
                Section(sb, "PALLETS");
                Row(sb, "Pallet 1", Fill(s?.pallet_1, palletCapacity), FillColor(s?.pallet_1 ?? 0, palletCapacity));
                Row(sb, "Pallet 2", Fill(s?.pallet_2, palletCapacity), FillColor(s?.pallet_2 ?? 0, palletCapacity));
                Section(sb, "AGV QUEUE");
                Row(sb, "Queue", QueueText(s?.queue));
                break;

            case Tab.Agv:
                Section(sb, "AGV1");
                var a = s?.agv;
                Row(sb, "Command", a == null || string.IsNullOrEmpty(a.cmd) ? "-" : a.cmd, a != null && !string.IsNullOrEmpty(a.cmd) ? "#6CB4FF" : null);
                Row(sb, "Loaded", a == null ? "-" : a.loaded ? "yes" : "no", a != null && a.loaded ? OkColor : null);
                Row(sb, "Towing", a == null || a.pallet <= 0 ? "-" : $"Pallet {a.pallet}");
                Row(sb, "Cycle", s == null || s.agv_cycle_sec <= 0f ? "-" : $"{s.agv_cycle_sec:F1} s");
                break;
        }
        UpdateTabs(state, lost);

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
        sb.Append($"<color={LabelColor}>  {label}</color><pos=46%><color={color ?? ValueColor}>{value}</color>\n");
    }

    /// <summary>"3 / 5  ■■■□□" 꼴. 막대는 '|' 글자로 그린다 (기본 글꼴에 있는 글자만 씀)</summary>
    static string Fill(int? v, int cap)
    {
        if (!v.HasValue) return "-";
        if (cap <= 0) return v.Value.ToString();
        int n = Mathf.Clamp(v.Value, 0, cap);
        int seg = Mathf.Min(cap, 10);
        int on = Mathf.RoundToInt((float)n / cap * seg);
        var bar = new StringBuilder();
        // 11/03 A 테스트: 글자 간격을 좁히면 막대가 한 덩어리로 뭉쳐 안 보여서 간격을 벌림
        bar.Append("<cspace=0.12em><size=115%>");
        for (int i = 0; i < seg; i++) bar.Append(i < on ? "|" : "<alpha=#40>|<alpha=#FF>");
        bar.Append("</size></cspace>");
        return $"{v.Value} / {cap}   <b>{bar}</b>";
    }

    static string FillColor(int v, int cap)
    {
        if (cap <= 0) return null;
        float r = (float)v / cap;
        return r >= 1f ? NgColor : r >= 0.7f ? WarnColor : OkColor;
    }

    static string YieldText(Stats s)
    {
        if (s == null || s.ok + s.ng <= 0) return "-";
        return $"{100f * s.ok / (s.ok + s.ng):F0} %";
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
        tab = Tab.Main;
        shown = "";
        if (titleText != null) titleText.text = DeviceName(device);
        panelRoot.gameObject.SetActive(true);
        Render();
        SelectionChanged?.Invoke(device);
    }

    public void Close()
    {
        if (panelRoot == null) return;
        panelRoot.gameObject.SetActive(false);
        selected = null;
        SelectionChanged?.Invoke(null);
    }

    /// <summary>Tab = 열기/닫기, 1~4 = 탭 바꾸기(닫혀 있으면 열기), Esc = 닫기</summary>
    void HandleKeys()
    {
        if (!enableHotkeys || panelRoot == null) return;
        var k = UnityEngine.InputSystem.Keyboard.current;
        if (k == null) return;

        if (k.tabKey.wasPressedThisFrame)
        {
            if (IsOpen) Close(); else Open();
            return;
        }
        if (k.escapeKey.wasPressedThisFrame && IsOpen) { Close(); return; }

        int pick = -1;
        if (k.digit1Key.wasPressedThisFrame || k.numpad1Key.wasPressedThisFrame) pick = 0;
        else if (k.digit2Key.wasPressedThisFrame || k.numpad2Key.wasPressedThisFrame) pick = 1;
        else if (k.digit3Key.wasPressedThisFrame || k.numpad3Key.wasPressedThisFrame) pick = 2;
        else if (k.digit4Key.wasPressedThisFrame || k.numpad4Key.wasPressedThisFrame) pick = 3;
        if (pick < 0) return;
        if (!IsOpen) Open();
        SetTab((Tab)pick);
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
        b.offsetMax = new Vector2(-18f, -(TitleH + 16f + TabH + 16f));
        body = NewText(b, font, fontSize, TextAlignmentOptions.TopLeft);
        body.lineSpacing = 14f;

        BuildTabs(panel, font);

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
    const float TabH = 64f;

    // ───────────────────────── 탭 ─────────────────────────

    /// <summary>탭을 바꾼다 (MAIN · LINE · PALLETS · AGV1)</summary>
    public void SetTab(Tab t)
    {
        tab = t;
        shown = "";
        Render();
    }

    void BuildTabs(RectTransform panel, TMP_FontAsset font)
    {
        int n = TabNames.Length;
        tabBg = new Image[n];
        tabIcon = new Image[n];
        tabLabel = new TMP_Text[n];
        var bar = NewRect("Tabs", panel);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.offsetMin = new Vector2(26f, -(TitleH + 16f + TabH));
        bar.offsetMax = new Vector2(-18f, -(TitleH + 16f));

        for (int i = 0; i < n; i++)
        {
            var r = NewRect("Tab_" + TabNames[i], bar);
            r.anchorMin = new Vector2((float)i / n, 0f);
            r.anchorMax = new Vector2((float)(i + 1) / n, 1f);
            r.offsetMin = new Vector2(i == 0 ? 0f : 3f, 0f);
            r.offsetMax = new Vector2(i == n - 1 ? 0f : -3f, 0f);
            tabBg[i] = r.gameObject.AddComponent<Image>();
            var btn = r.gameObject.AddComponent<Button>();
            btn.targetGraphic = tabBg[i];
            var t = (Tab)i;
            btn.onClick.AddListener(() => SetTab(t));

            var ic = NewRect("Icon", r);
            ic.anchorMin = ic.anchorMax = new Vector2(0.5f, 1f);
            ic.pivot = new Vector2(0.5f, 1f);
            ic.anchoredPosition = new Vector2(0f, -6f);
            ic.sizeDelta = new Vector2(30f, 30f);
            tabIcon[i] = ic.gameObject.AddComponent<Image>();
            tabIcon[i].sprite = TabIcons.Get(t);
            tabIcon[i].preserveAspect = true;
            tabIcon[i].raycastTarget = false;

            var lr = NewRect("Label", r);
            lr.anchorMin = new Vector2(0f, 0f);
            lr.anchorMax = new Vector2(1f, 0f);
            lr.pivot = new Vector2(0.5f, 0f);
            lr.offsetMin = new Vector2(0f, 4f);
            lr.offsetMax = new Vector2(0f, 24f);
            tabLabel[i] = NewText(lr, font, fontSize * 0.68f, TextAlignmentOptions.Center);
            tabLabel[i].fontStyle = FontStyles.Bold;
            tabLabel[i].text = enableHotkeys ? $"<alpha=#88>{i + 1}<alpha=#FF> {TabNames[i]}" : TabNames[i];
        }
    }

    /// <summary>선택한 탭은 주황, 나머지는 어둡게. 알람이면 MAIN 아이콘 빨강</summary>
    void UpdateTabs(string state, bool lost)
    {
        if (tabBg == null) return;
        bool alarm = lost || state == "E_STOP" || state == "ERROR";
        for (int i = 0; i < tabBg.Length; i++)
        {
            bool on = (int)tab == i;
            tabBg[i].color = on ? new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.9f) : new Color(1f, 1f, 1f, 0.07f);
            Color fg = on ? new Color(0.05f, 0.06f, 0.08f, 1f) : HexColor(LabelColor);
            if (i == (int)Tab.Main && alarm && !on) fg = HexColor(NgColor);
            tabIcon[i].color = fg;
            tabLabel[i].color = fg;
        }
    }
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

/// <summary>탭 아이콘 4개를 코드로 그린다 (흰색 64×64, Image.color로 물들임). 한 번 만들고 다시 씀</summary>
static class TabIcons
{
    const int S = 64;
    static Sprite[] cache;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ClearStatics() => cache = null;

    public static Sprite Get(StatusPanel.Tab t)
    {
        if (cache == null) cache = new Sprite[4];
        int i = (int)t;
        if (cache[i] == null) cache[i] = Make(t);
        return cache[i];
    }

    static Sprite Make(StatusPanel.Tab t)
    {
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "TabIcon_" + t };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);       // y는 아래가 0
                float d = Shape(t, p);
                byte a = (byte)(Mathf.Clamp01(0.5f - d) * 255f);
                px[y * S + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }

    // 거리(픽셀). 0보다 작으면 안쪽
    static float Box(Vector2 p, Vector2 c, Vector2 half, float r = 0f)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + new Vector2(r, r);
        return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
    }
    static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;
    static float Ring(float d, float w) => Mathf.Abs(d) - w * 0.5f;
    static float U(float a, float b) => Mathf.Min(a, b);
    static float Cut(float a, float b) => Mathf.Max(a, -b);

    static float Shape(StatusPanel.Tab t, Vector2 p)
    {
        switch (t)
        {
            case StatusPanel.Tab.Main:      // 원 안 느낌표 (상태)
                {
                    float d = Ring(Circle(p, new Vector2(32, 32), 25f), 5f);
                    d = U(d, Box(p, new Vector2(32, 37), new Vector2(3.5f, 11f), 3f));
                    d = U(d, Circle(p, new Vector2(32, 19), 4f));
                    return d;
                }
            case StatusPanel.Tab.Line:      // 컨베이어 벨트 + 롤러 + 박스
                {
                    float belt = Ring(Box(p, new Vector2(32, 22), new Vector2(28f, 10f), 10f), 4f);
                    float d = belt;
                    for (int k = 0; k < 3; k++) d = U(d, Circle(p, new Vector2(18 + 14 * k, 22), 3.5f));
                    d = U(d, Box(p, new Vector2(30, 46), new Vector2(11f, 10f), 2f));
                    return d;
                }
            case StatusPanel.Tab.Pallets:   // 파레트 위 박스 3개
                {
                    float d = Box(p, new Vector2(32, 9), new Vector2(28f, 3f));                 // 판
                    d = U(d, Box(p, new Vector2(10, 4), new Vector2(4f, 3f)));                  // 다리
                    d = U(d, Box(p, new Vector2(32, 4), new Vector2(4f, 3f)));
                    d = U(d, Box(p, new Vector2(54, 4), new Vector2(4f, 3f)));
                    d = U(d, Box(p, new Vector2(20, 24), new Vector2(10.5f, 10.5f), 2f));
                    d = U(d, Box(p, new Vector2(44, 24), new Vector2(10.5f, 10.5f), 2f));
                    d = U(d, Box(p, new Vector2(32, 48), new Vector2(10.5f, 10.5f), 2f));
                    return d;
                }
            default:                        // AGV: 차체 + 바퀴 + 센서
                {
                    float body = Box(p, new Vector2(32, 30), new Vector2(28f, 11f), 5f);
                    float d = Cut(body, Box(p, new Vector2(46, 32), new Vector2(6f, 3f), 1.5f));   // 창
                    d = U(d, Circle(p, new Vector2(16, 14), 7f));
                    d = U(d, Circle(p, new Vector2(48, 14), 7f));
                    d = U(d, Box(p, new Vector2(20, 46), new Vector2(3f, 5f)));                    // 센서 기둥
                    d = U(d, Circle(p, new Vector2(20, 53), 5f));
                    return d;
                }
        }
    }
}