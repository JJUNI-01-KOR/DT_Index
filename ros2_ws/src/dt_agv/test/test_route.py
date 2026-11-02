"""AGV1 경로 단위 테스트 (ROS 없이). SRS v3.1 FR-13, 부록 A.6."""
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
    assert goal == {'x': -0.75, 'y': 19.0, 'yaw': 0.0}       # 경로점 먼저
    assert r.on_arrived() == STOPS['P1_DOCK']                # 그다음 정지 지점
    assert r.on_arrived() == {'hitch': 'attach'}             # 도킹이니 자동 attach
    assert r.on_arrived() is None                            # 더 할 거 없음


def test_final_has_no_via_point():
    """to_final: [] 이면 경로점 없이 바로 정지 지점."""
    r = new_route()
    r.loaded = True
    goal, warn = r.start(FINAL)
    assert warn is None
    assert goal == STOPS['FINAL']
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
