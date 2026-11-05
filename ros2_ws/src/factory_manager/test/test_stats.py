"""생산 통계 단위 테스트 (ROS 없이). SRS v3.1 FR-07, FR-20, 6장."""
import json

from factory_manager.agv_dispatch import AgvDispatcher
from factory_manager.fsm import Fsm
from factory_manager.pallet_queue import PalletBook
from factory_manager.stats import Stats

ALL_KEYS = {'state', 'fault', 'estop', 'bin_left', 'fed', 'ok', 'ng', 'last',
            'main', 'lane_ok', 'lane_ng', 'pallet_1', 'pallet_2', 'queue',
            'agv', 'agv_cycle_sec'}


def fed(*ids):
    """이번 회차에 ids를 투입한 Stats."""
    s = Stats()
    for i in ids:
        s.on_fed(i)
    return s


def test_counts_ok_and_ng():
    s = fed('C001', 'C002', 'C003')
    s.on_graded('C001', 1.00, 'ok')
    s.on_graded('C002', 1.20, 'ng')
    s.on_graded('C003', 0.95, 'ok')
    assert (s.ok, s.ng) == (2, 1)


def test_last_is_latest():
    s = fed('C001', 'C002')
    s.on_graded('C001', 1.00, 'ok')
    s.on_graded('C002', 1.20, 'ng')
    assert s.last == {'id': 'C002', 'kg': 1.20, 'lane': 'ng'}


def test_no_weight_counts_as_ng():
    """무게가 없어 불량 처리된 방석도 불량 수에 들어간다 (FR-07 예외)."""
    s = fed('C001')
    s.on_graded('C001', None, 'ng')
    assert s.ng == 1 and s.last['kg'] is None


def test_reset_clears():
    """E_STOP 해제 때 0으로 (SRS 6장)."""
    s = fed('C001')
    s.on_graded('C001', 1.00, 'ok')
    s.reset()
    assert (s.ok, s.ng, s.last) == (0, 0, None)


def test_cushion_fed_before_reset_not_counted():
    """Clear 전에 투입되고 Clear 뒤에 판정된 방석은 세지 않는다 (BUG-003: 투입 ≠ 정상 + 불량)."""
    s = fed('C056')
    s.reset()
    assert s.on_graded('C056', 0.70, 'ng') is False
    assert (s.fed, s.ok, s.ng, s.last) == (0, 0, 0, None)


def test_graded_before_fed_counts_when_fed_arrives():
    """판정이 투입 완료보다 먼저 오면 보류했다가 투입 완료 때 센다."""
    s = Stats()
    assert s.on_graded('C058', 1.00, 'ok') is False
    assert (s.fed, s.ok) == (0, 0)
    s.on_fed('C058')
    assert (s.fed, s.ok, s.ng) == (1, 1, 0)
    assert s.last == {'id': 'C058', 'kg': 1.00, 'lane': 'ok'}


def test_never_fed_cushion_not_counted():
    """투입 완료가 오지 않은 방석(놓고 물러나는 중 E_STOP)은 Clear 뒤 판정돼도 세지 않는다."""
    s = Stats()
    s.on_graded('C060', 1.00, 'ok')   # 보류
    s.reset()                         # Clear: 보류도 버림
    s.on_fed('C061')
    assert (s.fed, s.ok, s.ng) == (1, 0, 0)


def test_ids_restart_after_unity_restart():
    """Unity를 다시 켜 id가 C001부터 다시 매겨져도 Clear 뒤 새 방석은 센다."""
    s = fed('C001', 'C002')
    s.reset()
    s.on_fed('C001')
    assert s.on_graded('C001', 1.00, 'ok') is True
    assert (s.fed, s.ok, s.ng) == (1, 1, 0)

def test_to_json_has_all_keys():
    """부록 A.4 키 15개(agv 안 3개 포함)가 전부 있다 (FR-20)."""
    out = Stats().to_json(Fsm(), 10, None, PalletBook(), AgvDispatcher(), None)
    data = json.loads(out)
    assert set(data.keys()) == ALL_KEYS
    assert set(data['agv'].keys()) == {'cmd', 'loaded', 'pallet'}


def test_to_json_maps_values():
    s = Stats()
    s.on_fed('C001')
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
    assert (data['fed'], data['ok'], data['ng']) == (1, 1, 0)
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