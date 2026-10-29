from factory_manager.line_rules import FeedGate


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
