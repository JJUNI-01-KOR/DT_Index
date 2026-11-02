from factory_manager.fsm import CAUSE_TIMEOUT_AGV, CAUSE_TIMEOUT_WEIGHT
from factory_manager.watchdog import Watchdog


def test_nothing_waiting():
    assert Watchdog().expired(100.0) is None


def test_weight_expires_after_limit():
    w = Watchdog(15.0, 30.0)
    w.start_weight(0.0)
    assert w.expired(15.0) is None
    assert w.expired(15.1) == CAUSE_TIMEOUT_WEIGHT


def test_weight_report_stops_timer():
    w = Watchdog(15.0, 30.0)
    w.start_weight(0.0)
    w.stop_weight()
    assert w.expired(100.0) is None


def test_agv_expires_after_limit():
    w = Watchdog(15.0, 30.0)
    w.start_agv(0.0)
    assert w.expired(30.0) is None
    assert w.expired(30.1) == CAUSE_TIMEOUT_AGV


def test_restart_counts_again():  # TC-40
    w = Watchdog(15.0, 30.0)
    w.start_weight(0.0)
    w.restart(14.0)
    assert w.expired(20.0) is None
    assert w.expired(29.1) == CAUSE_TIMEOUT_WEIGHT


def test_restart_does_not_start_idle_timer():
    w = Watchdog(15.0, 30.0)
    w.restart(10.0)
    assert w.expired(100.0) is None