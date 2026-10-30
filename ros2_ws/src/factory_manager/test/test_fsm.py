from factory_manager.fsm import (
    CAUSE_BUTTON, CAUSE_HEARTBEAT, CAUSE_STARTUP, CAUSE_TIMEOUT_WEIGHT, E_STOP, Fsm, IDLE, RUNNING)


def released():
    """실행 직후 E_STOP을 해제해 IDLE로 만든 Fsm."""
    f = Fsm()
    f.on_estop(False)
    return f


def test_starts_in_estop():
    f = Fsm()
    assert f.state == E_STOP and f.estop == CAUSE_STARTUP


def test_start_ignored_before_release():
    f = Fsm()
    assert not f.on_start() and f.state == E_STOP


def test_release_to_idle():
    f = Fsm()
    assert f.on_estop(False) and f.state == IDLE and f.estop is None


def test_start_from_idle():
    f = released()
    assert f.on_start() and f.state == RUNNING


def test_start_ignored_when_running():
    f = released()
    f.on_start()
    assert not f.on_start() and f.state == RUNNING


def test_estop_from_any_state():
    for prepare in (lambda f: None, lambda f: f.on_start()):
        f = released()
        prepare(f)
        assert f.on_estop(True) and f.state == E_STOP and f.estop == CAUSE_BUTTON


def test_estop_keeps_first_cause():
    f = released()
    f.on_start()
    f.to_estop(CAUSE_TIMEOUT_WEIGHT)
    assert not f.to_estop(CAUSE_HEARTBEAT) and f.estop == CAUSE_TIMEOUT_WEIGHT


def test_release_ignored_when_not_estop():
    f = released()
    assert not f.on_estop(False) and f.state == IDLE


def test_release_clears_faults():
    f = released()
    f.faults = ['arm_2']
    f.on_estop(True)
    f.on_estop(False)
    assert f.faults == [] and f.fault() is None
