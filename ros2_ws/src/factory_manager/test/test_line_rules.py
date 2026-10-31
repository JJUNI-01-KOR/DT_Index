
"""투입 조건 단위 테스트 (ROS 없이). SRS v3.1 FR-02, FR-04, TC-05."""
import pytest

from factory_manager.line_rules import FeedRule, parse_load


def rule_with_load(main=0, ok=0, ng=0):
    """시각 0에 레인 방석 수를 받은 FeedRule."""
    r = FeedRule(lane_capacity=3, load_timeout_sec=3.0)
    r.on_load({'main': main, 'ok': ok, 'ng': ng}, 0.0)
    return r


# ── parse_load: /conveyor_1_load 글자 → {"main", "ok", "ng"} ──

def test_parse_load_normal():
    assert parse_load('{"main": 1, "ok": 2, "ng": 0}') == {'main': 1, 'ok': 2, 'ng': 0}


@pytest.mark.parametrize('text', [
    'abc',                               # JSON이 아님
    '[1, 2, 3]',                         # 객체가 아님
    '{"main": 1, "ok": 2}',              # 키 없음
    '{"main": "1", "ok": 2, "ng": 0}',   # 숫자가 아님
    '{"main": true, "ok": 2, "ng": 0}',  # true가 1로 읽히지 않게
    '{"main": -1, "ok": 2, "ng": 0}',    # 음수
])
def test_parse_load_bad(text):
    assert parse_load(text) is None


# ── can_feed: 조건 5개 ──

def test_can_feed_when_all_ok():
    assert rule_with_load().can_feed(5, False, 1.0) is True


def test_no_feed_when_bin_empty():
    assert rule_with_load().can_feed(0, False, 1.0) is False


def test_no_feed_when_arm_busy():
    assert rule_with_load().can_feed(5, True, 1.0) is False


def test_no_feed_before_first_load():
    assert FeedRule().can_feed(5, False, 1.0) is False


def test_no_feed_when_load_is_old():
    """/conveyor_1_load를 3초 이상 못 받으면 요청하지 않는다 (FR-04 예외)."""
    r = rule_with_load()
    assert r.can_feed(5, False, 2.9) is True
    assert r.can_feed(5, False, 3.0) is False


@pytest.mark.parametrize('main, ok, ng, expected', [
    (0, 2, 2, True),
    (1, 1, 1, True),
    (0, 3, 0, False),   # 정상 레인이 용량
    (0, 0, 3, False),   # 불량 레인이 용량
    (1, 2, 0, False),   # main + ok = 3
    (1, 0, 2, False),   # main + ng = 3
    (3, 0, 0, False),   # 메인 라인 방석은 두 레인 모두에 더한다
])
def test_lane_capacity(main, ok, ng, expected):
    assert rule_with_load(main, ok, ng).can_feed(5, False, 1.0) is expected


def test_stop_and_resume_by_load():
    """ok=3이면 멈추고, ok=2로 내려가면 다시 나간다 (TC-05)."""
    r = rule_with_load(ok=3)
    assert r.can_feed(5, False, 1.0) is False
    r.on_load({'main': 0, 'ok': 2, 'ng': 0}, 2.0)
    assert r.can_feed(5, False, 2.0) is True


# ── on_fed, on_weight: 직전 방석의 무게 보고 대기 ──

def test_wait_for_weight_after_fed():
    r = rule_with_load()

def test_wait_for_weight_after_fed():
    r = rule_with_load()
    r.on_fed('C001')
    assert r.can_feed(5, False, 1.0) is False
    assert r.on_weight('C001') is True
    assert r.can_feed(5, False, 1.0) is True


def test_other_cushion_does_not_release():
    """다른 방석의 무게 보고로는 대기를 풀지 않는다."""
    r = rule_with_load()
    r.on_fed('C002')
    assert r.on_weight('C001') is False
    assert r.waiting_id == 'C002'


def test_weight_without_waiting():
    assert rule_with_load().on_weight('C001') is False


def test_reset_clears():
    """E_STOP 해제 때 대기와 받은 값을 지운다 (FR-16 6번)."""
    r = rule_with_load()
    r.on_fed('C001')
    r.reset()
    assert (r.load, r.load_time, r.waiting_id) == (None, None, None)
