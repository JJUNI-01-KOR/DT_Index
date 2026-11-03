// 고장 장비 머리 위 경고 아이콘과 [Fix] (UI-02, FR-18 3번). 담당 B · scope unity-ui
// Play 때 ControlPanel이 있는 오브젝트에 자동으로 붙고, 같은 Canvas에 아이콘과 [Fix]를 만든다 (씬 수정 없음).
// ERROR이고 ROS2 연결이 정상이면 /factory/stats의 fault(RosBridge.CurrentFault) 장비 위에 아이콘을 띄운다.
//   arm_1 ~ arm_3: 그 로봇팔 위, conveyor: 컨베이어 전체의 가운데 위
// 아이콘을 클릭하면 오른쪽에 [Fix]가 펼쳐진다 (다시 클릭하면 접힘). [Fix]를 3초 누르면 /factory/fault_clear(장비 id).
// 새 고장이 되면 접힌 상태로 시작한다. 상태 패널의 [Fix]와 같은 일을 한다 (둘 중 아무거나 써도 됨).
// 아이콘: Resources/UI/FaultWarning (Sprite). 없으면 노란 네모 + "!"
// 장비가 카메라 뒤에 있으면 숨긴다.

using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class FaultMarker : MonoBehaviour
{
    [Tooltip("아이콘 크기 (px, 1920×1080 기준)")]
    [SerializeField] private float size = 64f;
    [Tooltip("장비 꼭대기 위로 띄우는 높이 (m)")]
    [SerializeField] private float headroom = 0.4f;

    const string IconPath = "UI/FaultWarning";   // Assets/_Project/Resources/UI/FaultWarning.png (Sprite)
    static readonly Color WarnColor = new Color(1f, 0.71f, 0.28f, 0.95f);   // StatusPanel WarnColor와 같음
    static readonly Color FixColor = new Color(0.03f, 0.05f, 0.08f, 0.85f);  // 상태 패널 배경과 비슷

    private Canvas canvas;
    private RectTransform marker;
    private CanvasGroup markerGroup;
    private Button iconButton;
    private CanvasGroup fixGroup;
    private Button fixButton;
    private HoldButton fixHold;
    private TMP_Text fixLabel;
    private string current = "";
    private bool expanded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoAttach()
    {
        var panel = FindAnyObjectByType<ControlPanel>();
        if (panel != null && panel.GetComponent<FaultMarker>() == null)
            panel.gameObject.AddComponent<FaultMarker>();
    }

    void Start()
    {
        var c = GetComponentInParent<Canvas>();
        if (c == null)
        {
            Debug.LogWarning("[FaultMarker] Canvas 아래가 아니라 경고 아이콘을 만들지 않음", this);
            enabled = false;
            return;
        }
        canvas = c.rootCanvas;
        var sample = canvas.GetComponentInChildren<TMP_Text>(true);
        TMP_FontAsset font = sample != null ? sample.font : null;

        // 아이콘 (클릭하면 [Fix] 펼치기)
        var go = new GameObject("Fault Marker", typeof(RectTransform), typeof(Image), typeof(Button), typeof(CanvasGroup));
        marker = (RectTransform)go.transform;
        marker.SetParent(canvas.transform, false);
        marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
        marker.pivot = new Vector2(0.5f, 0f);              // 아래 가운데가 장비 머리 위
        marker.sizeDelta = new Vector2(size, size);
        marker.SetAsLastSibling();

        var image = go.GetComponent<Image>();
        var icon = Resources.Load<Sprite>(IconPath);
        if (icon != null)
        {
            image.sprite = icon;
            image.color = Color.white;
            image.preserveAspect = true;
        }
        else
        {
            image.color = WarnColor;
            NewLabel(marker, font, size * 0.6f, Color.black).text = "!";
            Debug.LogWarning($"[FaultMarker] Resources/{IconPath} 스프라이트가 없어 노란 네모로 표시", this);
        }
        iconButton = go.GetComponent<Button>();
        iconButton.targetGraphic = image;
        iconButton.onClick.AddListener(() => expanded = !expanded);
        markerGroup = go.GetComponent<CanvasGroup>();

        // [Fix] (아이콘 오른쪽, 3초 누르기)
        var fx = new GameObject("Fault Fix", typeof(RectTransform), typeof(Image), typeof(Button), typeof(CanvasGroup));
        var frt = (RectTransform)fx.transform;
        frt.SetParent(marker, false);
        frt.anchorMin = frt.anchorMax = new Vector2(1f, 0.5f);
        frt.pivot = new Vector2(0f, 0.5f);
        frt.anchoredPosition = new Vector2(8f, 0f);
        frt.sizeDelta = new Vector2(size * 2f, size * 0.7f);
        var fixImage = fx.GetComponent<Image>();
        fixImage.color = FixColor;
        fixButton = fx.GetComponent<Button>();
        fixButton.targetGraphic = fixImage;
        fixGroup = fx.GetComponent<CanvasGroup>();
        fixHold = fx.AddComponent<HoldButton>();
        fixHold.Completed += OnHeld;
        fixLabel = NewLabel(frt, font, size * 0.35f, Color.white);

        Show(false, false);
    }

    void OnDestroy()
    {
        if (fixHold != null) fixHold.Completed -= OnHeld;
    }

    void Update()
    {
        if (marker == null) return;
        var bridge = RosBridge.Instance;
        string fault = bridge != null && bridge.Healthy && bridge.FactoryState == "ERROR" ? bridge.CurrentFault : "";
        if (fault != current)
        {
            current = fault;
            expanded = false;                      // 새 고장은 접힌 상태로 시작
        }
        bool show = current != "" && TryHead(current, out Vector3 head) && Place(head);
        Show(show, show && expanded);
        if (show && expanded) fixLabel.text = fixHold.Holding ? $"{fixHold.Remaining:F1}s" : "Fix";
    }

    /// <summary>숨겨도 오브젝트는 켜 둔다 (HoldButton이 꺼진 버튼을 보고 누르기를 처음부터로 돌리게)</summary>
    void Show(bool icon, bool fix)
    {
        markerGroup.alpha = icon ? 1f : 0f;
        markerGroup.blocksRaycasts = icon;
        markerGroup.interactable = icon;
        iconButton.interactable = icon;

        fixGroup.alpha = fix ? 1f : 0f;
        fixGroup.blocksRaycasts = fix;
        fixGroup.interactable = fix;
        fixButton.interactable = fix;
    }

    void OnHeld()
    {
        var bridge = RosBridge.Instance;
        if (bridge != null && current != "") bridge.PublishFaultClear(current);
    }

    static TMP_Text NewLabel(RectTransform parent, TMP_FontAsset font, float fontSize, Color color)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var label = go.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.fontSize = fontSize;
        label.fontStyle = FontStyles.Bold;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return label;
    }

    /// <summary>장비 id → 그 장비 꼭대기 위치 (Renderer 경계 상자). 못 찾으면 false</summary>
    static bool TryHead(string id, out Vector3 head)
    {
        head = default;
        Bounds b = default;
        bool any = false;
        if (id.StartsWith("arm_"))
        {
            foreach (var arm in FindObjectsByType<ArmRig>())
                if (arm.ArmName == id) { Encapsulate(arm, ref b, ref any); break; }
        }
        else if (id == "conveyor")
        {
            foreach (var conv in FindObjectsByType<Conveyor>()) Encapsulate(conv, ref b, ref any);
        }
        if (!any) return false;
        head = new Vector3(b.center.x, b.max.y, b.center.z);
        return true;
    }

    static void Encapsulate(Component c, ref Bounds b, ref bool any)
    {
        foreach (var r in c.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
    }

    /// <summary>월드 위치 → 화면 위 아이콘 자리. 카메라 뒤면 false</summary>
    bool Place(Vector3 world)
    {
        var cam = Camera.main;
        if (cam == null) return false;
        Vector3 sp = cam.WorldToScreenPoint(world + Vector3.up * headroom);
        if (sp.z <= 0f) return false;
        var uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform, sp, uiCam, out Vector2 local))
            return false;
        marker.anchoredPosition = local;
        return true;
    }
}
