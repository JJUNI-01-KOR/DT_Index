from factory_manager.fsm import (
    CAUSE_BUTTON, CAUSE_HEARTBEAT, CAUSE_STARTUP, CAUSE_TIMEOUT_WEIGHT, ERROR, E_STOP, Fsm, IDLE, RUNNING, parse_fault)


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


def running_fsm():
    f = Fsm()
    f.on_estop(False)
    f.on_start()
    return f


def test_fault_goes_error():
    f = running_fsm()
    assert f.on_fault('arm_2') is True
    assert f.state == ERROR and f.fault() == 'arm_2'


def test_other_device_clear_ignored():  # TC-26
    f = running_fsm()
    f.on_fault('arm_2')
    assert f.on_fault_clear('arm_3') is False
    assert f.state == ERROR


def test_second_fault_keeps_error_until_all_cleared():
    f = running_fsm()
    f.on_fault('arm_2')
    f.on_fault('arm_3')
    f.on_fault_clear('arm_2')
    assert f.state == ERROR and f.fault() == 'arm_3'
    assert f.on_fault_clear('arm_3') is True
    assert f.state == RUNNING


def test_parse_fault():
    assert parse_fault('{"device":"arm_2","reason":"pick_failed"}') == 'arm_2'
    assert parse_fault('이상한 값') is None
