using System;
using System.Linq;
using UnityEngine;

/// <summary>
/// 꺾이거나 갈라지는 라인용: 직선 구간 Conveyor 여러 개를 하나처럼 묶는다.
/// - SetRunning → 자식 Conveyor 전부 가동·정지
/// - BoxCount   → 자식 Conveyor 박스 수의 합 (/conveyor/box_count에 이 값이 나감)
///
/// 구조 예:
///   ConveyorLine (ConveyorGroup + ConveyorLink)
///   ├── Seg_Main   (Conveyor, 방향: 메인 흐름)   ← 칸들
///   ├── Seg_Left   (Conveyor, 방향: 왼쪽 가지)  ← 칸들
///   └── Seg_Right  (Conveyor, 방향: 오른쪽 가지) ← 칸들
/// 각 Seg에서 "자식 칸에 맞춰 벨트 영역 자동 설정"을 따로 누른다 (직선이라 딱 맞게 잡힘).
/// </summary>
[DisallowMultipleComponent]
public class ConveyorGroup : MonoBehaviour
{
    private Conveyor[] segments = Array.Empty<Conveyor>();

    public bool IsRunning { get; private set; }
    public int BoxCount { get; private set; } = -1;

    public event Action<bool> RunningChanged;
    public event Action<int> BoxCountChanged;

    void Awake()
    {
        segments = GetComponentsInChildren<Conveyor>(true);
        foreach (var s in segments) s.BoxCountChanged += _ => Recount();
        if (segments.Length == 0) Debug.LogWarning("[conveyor_group] 자식에 Conveyor가 없음", this);
    }

    public void SetRunning(bool run)
    {
        if (IsRunning == run) return;
        IsRunning = run;
        foreach (var s in segments) s.SetRunning(run);
        Debug.Log($"[conveyor_group] {(run ? "가동" : "정지")} (구간 {segments.Length}개)", this);
        RunningChanged?.Invoke(run);
    }

    void Recount()
    {
        int sum = segments.Sum(s => Mathf.Max(0, s.BoxCount));
        if (sum == BoxCount) return;
        BoxCount = sum;
        BoxCountChanged?.Invoke(sum);
    }

    [ContextMenu("테스트: 가동")] void TestRun() => SetRunning(true);
    [ContextMenu("테스트: 정지")] void TestStop() => SetRunning(false);

    [ContextMenu("모든 구간 벨트 영역 자동 설정")]
    void FitAll()
    {
        foreach (var s in GetComponentsInChildren<Conveyor>(true))
            s.SendMessage("FitToChildren", SendMessageOptions.DontRequireReceiver);
    }
}
