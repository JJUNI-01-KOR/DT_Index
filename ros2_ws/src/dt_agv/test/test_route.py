"""AGV1 경로 단위 테스트 (ROS 없이). SRS v3.1 FR-13, 부록 A.6."""
import pytest

from dt_agv.route import AgvRoute, DOCKS, FINAL, HOME

STOPS = {
    'HOME': {'x': -0.75, 'y': 16.5, 'yaw': -1.571},
    'P1_DOCK': {'x': -6.5, 'y': 19.0, 'yaw': 1.571},
    'P2_DOCK': {'x': 5.4, 'y': 19.0, 'yaw': 1.571},
    'FINAL': {'x': -0.75, 'y': 23.0, 'yaw': 1.571},
}
ROUTE = {
    'to_p1_dock': [{'x': -0.75, 'y': 19.0, 'yaw': 0.0}],
    'to_p2_dock': [{'x': -0.75, 'y': 19.0, 'yaw': 0.0}],
    'to_final': [],
    'to_home': [],
}


def new_route():
    return AgvRoute(STOPS, ROUTE)


def test_dock_goes_through_via_point_then_stop():
    r = new_route()
    goal, warn = r.start('P1_DOCK')
    assert warn is None
    assert goal == {'wp': 'P1_DOCK_via', 'x': -0.75, 'y': 19.0, 'yaw': 0.0}   # 경로점 먼저
    assert r.on_arrived() == {'wp': 'P1_DOCK', **STOPS['P1_DOCK']}            # 그다음 정지 지점
    assert r.on_arrived() == {'hitch': 'attach'}                             # 도킹이니 자동 attach
    assert r.on_arrived() is None                                           # 더 할 거 없음


def test_final_has_no_via_point():
    """to_final: [] 이면 경로점 없이 바로 정지 지점."""
    r = new_route()
    r.loaded = True
    goal, warn = r.start(FINAL)
    assert warn is None
    assert goal == {'wp': 'FINAL', **STOPS['FINAL']}
    assert r.on_arrived() == {'hitch': 'detach'}


def test_attach_sets_loaded_true():
    r = new_route()
    r.start('P1_DOCK')
    r.on_arrived()
    r.on_arrived()  # hitch attach
    assert r.on_attached() is True
    assert r.loaded is True


def test_detach_sets_loaded_false():
    r = new_route()
    r.loaded = True
    r.start(FINAL)
    r.on_arrived()  # hitch detach
    assert r.on_detached() is False
    assert r.loaded is False


def test_attached_ignored_outside_dock_cmd():
    """FINAL·HOME 중에 ATTACHED가 와도(잘못된 보고) /agv_1_loaded를 바꾸지 않는다."""
    r = new_route()
    assert r.on_attached() is None  # cmd가 None일 때


def test_detached_ignored_outside_final_cmd():
    r = new_route()
    r.start('P1_DOCK')
    assert r.on_detached() is None  # cmd가 FINAL이 아닐 때


def test_reject_dock_while_loaded():
    r = new_route()
    r.loaded = True
    goal, warn = r.start('P1_DOCK')
    assert goal is None and warn is not None


def test_reject_final_while_not_loaded():
    r = new_route()
    goal, warn = r.start(FINAL)
    assert goal is None and warn is not None


def test_reject_home_while_loaded():
    r = new_route()
    r.loaded = True
    goal, warn = r.start(HOME)
    assert goal is None and warn is not None


def test_estop_cleared_resets_everything():
    r = new_route()
    r.start('P1_DOCK')
    r.on_arrived()
    r.on_arrived()
    r.on_attached()
    r.on_estop_cleared()
    assert r.cmd is None and r.steps == [] and r.pending_hitch is None and r.loaded is False


def test_docks_tuple_has_both_pallets():
    assert DOCKS == ('P1_DOCK', 'P2_DOCK')


def test_every_goal_has_nonempty_wp():
    """Unity AgvLink는 좌표를 줘도 wp 이름이 없으면 goal을 무시한다 (AgvLink.cs:102)."""
    r = new_route()
    goal, _ = r.start('P1_DOCK')
    assert goal.get('wp')
    nxt = r.on_arrived()
    assert nxt.get('wp')


@pytest.mark.parametrize('dock,x', [('P1_DOCK', -6.5), ('P2_DOCK', 5.4)])
def test_loaded_dock_selects_yaml_exit_before_final(dock, x):
    exits = {
        'to_final_from_p1_dock': [{'x': -6.5, 'y': 21.0, 'yaw': 1.571}],
        'to_final_from_p2_dock': [{'x': 5.4, 'y': 21.0, 'yaw': 1.571}],
    }
    r = AgvRoute(STOPS, {**ROUTE, **exits})
    r.start(dock)
    r.on_arrived()
    assert r.on_arrived() == {'hitch': 'attach'}
    assert r.on_attached() is True
    goal, warn = r.start(FINAL)
    assert warn is None
    assert goal == {'wp': 'FINAL_via', 'x': x, 'y': 21.0, 'yaw': 1.571}
    assert r.on_arrived() == {'wp': FINAL, **STOPS[FINAL]}
    assert r.on_arrived() == {'hitch': 'detach'}
    assert r.on_detached() is False
    assert r.loaded_from is None


def test_legacy_yaml_after_attach_uses_common_final_route():
    r = new_route()
    r.start('P2_DOCK')
    r.on_attached()
    goal, warn = r.start(FINAL)
    assert warn is None
    assert goal == {'wp': FINAL, **STOPS[FINAL]}


def test_estop_discards_loaded_dock_before_next_cycle():
    r = new_route()
    r.start('P1_DOCK')
    r.on_attached()
    assert r.loaded_from == 'P1_DOCK'
    r.on_estop_cleared()
    assert r.loaded_from is None
    r.start('P2_DOCK')
    r.on_attached()
    assert r.loaded_from == 'P2_DOCK'
