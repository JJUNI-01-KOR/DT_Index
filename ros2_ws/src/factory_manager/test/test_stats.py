"""생산 통계 단위 테스트 (ROS 없이). SRS v3.1 FR-07, FR-20, 6장."""
import json

from factory_manager.agv_dispatch import AgvDispatcher
from factory_manager.fsm import Fsm
from factory_manager.pallet_queue import PalletBook
from factory_manager.stats import Stats

ALL_KEYS = {'state', 'fault', 'estop', 'bin_left', 'fed', 'ok', 'ng', 'last',
            'main', 'lane_ok', 'lane_ng', 'pallet_1', 'pallet_2', 'queue',
            'agv', 'agv_cycle_sec'}


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


def test_to_json_has_all_keys():
    """부록 A.4 키 15개(agv 안 3개 포함)가 전부 있다 (FR-20)."""
    out = Stats().to_json(Fsm(), 10, None, PalletBook(), AgvDispatcher(), None)
    data = json.loads(out)
    assert set(data.keys()) == ALL_KEYS
    assert set(data['agv'].keys()) == {'cmd', 'loaded', 'pallet'}


def test_to_json_maps_values():
    s = Stats()
    s.on_graded('C001', 1.00, 'ok')
    pallets = PalletBook(capacity=1)
    pallets.on_placed(1)
    agv = AgvDispatcher()
    agv.on_loaded(False, True, None)
    agv.try_dispatch(True, 1, now=100)
    data = json.loads(s.to_json(Fsm(), 7, {'main': 1, 'ok': 2, 'ng': 3}, pallets, agv, agv.last_cycle_sec))
    assert data['state'] == 'E_STOP'
    assert data['fault'] is None
    assert data['estop'] == 'startup'
    assert data['bin_left'] == 7
    assert (data['fed'], data['ok'], data['ng']) == (0, 1, 0)
    assert data['last'] == {'id': 'C001', 'kg': 1.00, 'lane': 'ok'}
    assert (data['main'], data['lane_ok'], data['lane_ng']) == (1, 2, 3)
    assert data['pallet_1'] == 1
    assert data['queue'] == [1]
    assert data['agv'] == {'cmd': 'P1_DOCK', 'loaded': False, 'pallet': 1}
    assert data['agv_cycle_sec'] is None


def test_to_json_load_none():
    """/conveyor_1_load를 아직 못 받았으면 main·lane_ok·lane_ng는 0."""
    data = json.loads(Stats().to_json(Fsm(), 0, None, PalletBook(), AgvDispatcher(), None))
    assert (data['main'], data['lane_ok'], data['lane_ng']) == (0, 0, 0)