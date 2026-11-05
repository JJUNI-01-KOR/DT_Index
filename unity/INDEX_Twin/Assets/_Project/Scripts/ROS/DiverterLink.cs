// DiverterLink.cs : ROS2 → 분기 게이트 연결 (FR-08). 담당 A · scope unity-ros
// ROS 통신은 Scripts/ROS/ 에서만 한다 (CONVENTIONS 3절).
//
//   구독: /diverter_1_cmd (std_msgs/String) JSON {"id":"C012","lane":"ok"}. ok 정상 레인 · ng 불량 레인. 방석당 1회
//
// 받은 명령을 그 방석의 결과표(ItemSort)에 적는다. Diverter는 결과표만 읽는다 (서로 직접 부르지 않음).
//   - 같은 id 명령을 다시 받으면 처음 것을 따른다 (FR-08 예외 처리)
//   - 방석을 아직 못 찾으면 id별로 기억해 두고 찾을 때까지 다시 시도한다
//   - 명령 없이 갈림길에 닿은 방석은 Diverter가 불량 레인으로 보낸다 (uncheckedGoesReject)
//
// 씬의 Diverter에 Play 때 자동으로 붙는다. Main.unity를 고치지 않아도 된다.
//
// 확인 (VM, Unity Play 중):
//   ros2 topic info /diverter_1_cmd            → Subscription count 1
//   ros2 topic pub --once /diverter_1_cmd std_msgs/msg/String "{data: '{\"id\":\"C001\",\"lane\":\"ng\"}'}"

using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class DiverterLink : MonoBehaviour
{
    [Tooltip("방석 태그")]
    [SerializeField] private string cushionTag = "Cushion";

    [Tooltip("방석을 못 찾은 명령을 이 시간(초)까지 다시 시도한다")]
    [SerializeField] private float pendingTimeoutSec = 10f;

    [Serializable]
    private class Cmd { public string id; public string lane; }

    private struct Pending { public bool reject; public float since; }

    private ROSConnection ros;
    private WeightScanner scanner;
    private readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();
    private readonly HashSet<string> applied = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AttachToDiverter()
    {
        var found = UnityEngine.Object.FindAnyObjectByType<Diverter>();
        if (found != null && found.GetComponent<DiverterLink>() == null)
            found.gameObject.AddComponent<DiverterLink>();
    }

    void Start()
    {
        scanner = UnityEngine.Object.FindAnyObjectByType<WeightScanner>();
        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<StringMsg>(TopicNames.Diverter1Cmd, m => HandleCmdJson(m.data));
    }

    void OnDestroy()
    {
        if (ros != null) ros.Unsubscribe(TopicNames.Diverter1Cmd);
    }

    /// <summary>/diverter_1_cmd JSON 처리. 시험용으로 Inspector ⋮ 없이 다른 스크립트에서 불러도 된다</summary>
    public void HandleCmdJson(string json)
    {
        Cmd c;
        try { c = JsonUtility.FromJson<Cmd>(json); }
        catch (Exception e)
        {
            Debug.LogWarning($"[분기 연결] JSON을 읽을 수 없음: {json} ({e.Message})", this);
            return;
        }
        if (c == null || string.IsNullOrEmpty(c.id) || (c.lane != "ok" && c.lane != "ng"))
        {
            Debug.LogWarning($"[분기 연결] id나 lane(ok/ng)이 없음, 무시: {json}", this);
            return;
        }

        if (applied.Contains(c.id) || pending.ContainsKey(c.id))
        {
            Debug.LogWarning($"[분기 연결] {c.id} 명령을 다시 받음, 처음 명령을 따름 (FR-08)", this);
            return;
        }

        bool reject = c.lane == "ng";
        Debug.Log($"[ROS→Unity] {TopicNames.Diverter1Cmd} {json}", this);
        if (!TryApply(c.id, reject))
            pending[c.id] = new Pending { reject = reject, since = Time.time };
    }

    void Update()
    {
        if (pending.Count == 0) return;
        var done = new List<string>();
        foreach (var kv in pending)
        {
            if (TryApply(kv.Key, kv.Value.reject)) done.Add(kv.Key);
            else if (Time.time - kv.Value.since > pendingTimeoutSec)
            {
                Debug.LogWarning($"[분기 연결] {kv.Key} 방석을 {pendingTimeoutSec:F0}초 동안 못 찾아 명령을 버림", this);
                done.Add(kv.Key);
            }
        }
        foreach (var id in done) pending.Remove(id);
    }

    bool TryApply(string id, bool reject)
    {
        var cushion = FindCushion(id);
        if (cushion == null) return false;

        var sort = ItemSort.Of(cushion);
        if (sort == null) return false;
        applied.Add(id);

        if (sort.Inspected)
        {
            Debug.LogWarning($"[분기 연결] {id} 이미 판정됨 ({sort.Reason}), 이번 명령은 무시", this);
            return true;
        }

        sort.Inspected = true;
        sort.Reject = reject;
        sort.Reason = $"ROS lane={(reject ? "ng" : "ok")} ({sort.WeightKg:F2}kg)";
        if (scanner != null) scanner.ShowResult(reject);
        return true;
    }

    Cushion FindCushion(string id)
    {
        foreach (var g in GameObject.FindGameObjectsWithTag(cushionTag))
        {
            var c = g.GetComponent<Cushion>();
            if (c != null && c.CushionId == id) return c;
        }
        return null;
    }
}
