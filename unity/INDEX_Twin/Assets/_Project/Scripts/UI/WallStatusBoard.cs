// WallStatusBoard.cs : 공장 북쪽 벽 실시간 상태 전광판 (11/03 A). 담당 A · scope unity-ui
// FactoryProps(메뉴 INDEX > Factory > Build Shell + Props)가 전광판을 만들고 이 스크립트를 붙인다. 손으로 붙일 일 없음.
//
// 상태 패널(StatusPanel)이 받은 /factory/stats를 그대로 읽어 크게 보여 준다. Unity에서 따로 세지 않는다 (FR-20과 같음).
//   STATE · FED · OK · NG · YIELD · PALLET 1/2 · AGV1 명령 · 마지막 판정
// ROS를 직접 부르지 않는다. StatusPanel·RosBridge가 없거나 아직 못 받았으면 "waiting"으로 둔다.
// 0.5초마다 다시 그린다. 글자는 자식 "Body"(TextMeshPro).

using System.Text;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class WallStatusBoard : MonoBehaviour
{
    [SerializeField] private float refreshSec = 0.5f;
    [SerializeField] private int palletCapacity = 5;

    private TMP_Text body;
    private StatusPanel panel;
    private float next;
    private string shown;

    void Awake()
    {
        var t = transform.Find("Body");
        body = t != null ? t.GetComponent<TMP_Text>() : GetComponentInChildren<TMP_Text>();
    }

    void Update()
    {
        if (body == null || Time.unscaledTime < next) return;
        next = Time.unscaledTime + refreshSec;
        if (panel == null) panel = FindFirstObjectByType<StatusPanel>();

        string text = Compose();
        if (text != shown) { body.text = text; shown = text; }
    }

    string Compose()
    {
        var s = panel != null ? panel.Last : null;
        var bridge = RosBridge.Instance;
        string state = bridge != null && !string.IsNullOrEmpty(bridge.FactoryState) ? bridge.FactoryState : s?.state;
        if (s == null && string.IsNullOrEmpty(state)) return "<color=#7C8591>waiting for /factory/stats</color>";

        bool stale = panel != null && panel.Stale;
        string grey = "#7C8591";
        var sb = new StringBuilder();
        string sc = stale ? grey : StatusPanel.StateColor(state);
        sb.Append($"<size=130%><color={sc}><b>{(string.IsNullOrEmpty(state) ? "-" : state)}</b></color></size>");
        if (state == "E_STOP" && s != null) sb.Append($"   <color=#FF6B6B>{StatusPanel.EStopText(s.estop)}</color>");
        if (s != null && !string.IsNullOrEmpty(s.fault)) sb.Append($"   <color=#FFB547>FAULT {s.fault}</color>");
        sb.Append('\n');

        if (s != null)
        {
            int done = s.ok + s.ng;
            string yield = done > 0 ? $"{100f * s.ok / done:F0}%" : "-";
            sb.Append($"FED <b>{s.fed}</b>    <color=#5BE08A>OK <b>{s.ok}</b></color>    <color=#FF6B6B>NG <b>{s.ng}</b></color>    YIELD <b>{yield}</b>    BIN <b>{s.bin_left}</b>\n");
            sb.Append($"PALLET 1  <b>{s.pallet_1}/{palletCapacity}</b>      PALLET 2  <b>{s.pallet_2}/{palletCapacity}</b>");
            string agv = s.agv == null || string.IsNullOrEmpty(s.agv.cmd) ? "-" : s.agv.cmd;
            sb.Append($"      AGV1  <color=#6CB4FF><b>{agv}</b></color>\n");
            if (s.last != null && !string.IsNullOrEmpty(s.last.id))
                sb.Append($"LAST  {s.last.id}  {s.last.kg:F2} kg  <color={(s.last.lane == "ok" ? "#5BE08A" : "#FF6B6B")}>{(s.last.lane == "ok" ? "OK" : "NG")}</color>");
        }
        string text = sb.ToString();
        if (stale) text = System.Text.RegularExpressions.Regex.Replace(text, "<color=#[0-9A-Fa-f]{6}>", $"<color={grey}>");
        return text;
    }
}
