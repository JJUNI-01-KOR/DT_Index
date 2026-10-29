from factory_manager.line_rules import FeedGate, SlotWait


def test_gate_stop_resume():
    g = FeedGate(4, 3)
    assert g.update(3)
    assert not g.update(4)
    assert not g.update(5)
    assert g.update(3)


def test_gate_keeps_state():
    g = FeedGate(5, 2)  # 값을 바꾸면 3, 4에서는 직전 판단 유지
    assert g.update(3)
    assert not g.update(5)
    assert not g.update(3)
    assert g.update(2)


def test_wait_blocks_until_enter():
    s = SlotWait()
    s.update('reject_wait')
    assert s.blocked()
    s.update('reject')
    assert not s.blocked()


def test_both_waits_need_both_enter():  # FR-10 예외 처리
    s = SlotWait()
    s.update('reject_wait')
    s.update('end_wait')
    s.update('reject')
    assert s.blocked()
    s.update('end_2')
    assert not s.blocked()


def test_enter_without_wait_does_not_block():  # FR-07 인수 기준
    s = SlotWait()
    s.update('reject')
    s.update('end_1')
    assert not s.blocked()


def test_unknown_zone():
    s = SlotWait()
    assert not s.update('reject_full')
    assert not s.blocked()
