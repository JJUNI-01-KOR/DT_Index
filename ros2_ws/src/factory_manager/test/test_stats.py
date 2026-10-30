"""생산 통계 단위 테스트 (ROS 없이). SRS v3.1 FR-07, FR-20, 6장."""
from factory_manager.stats import Stats


def test_counts_ok_and_ng():
    s = Stats()
    s.on_graded('C001', 1.00, 'ok')
    s.on_graded('C002', 1.20, 'ng')
    s.on_graded('C003', 0.95, 'ok')
    assert (s.ok, s.ng) == (2, 1)


def test_last_is_latest():
    s = Stats()
    s.on_graded('C001', 1.00, 'ok')
    s.on_graded('C002', 1.20, 'ng')
    assert s.last == {'id': 'C002', 'kg': 1.20, 'lane': 'ng'}


def test_no_weight_counts_as_ng():
    """무게가 없어 불량 처리된 방석도 불량 수에 들어간다 (FR-07 예외)."""
    s = Stats()
    s.on_graded('C001', None, 'ng')
    assert s.ng == 1 and s.last['kg'] is None


def test_reset_clears():
    """E_STOP 해제 때 0으로 (SRS 6장)."""
    s = Stats()
    s.on_graded('C001', 1.00, 'ok')
    s.reset()
    assert (s.ok, s.ng, s.last) == (0, 0, None)