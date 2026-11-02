"""방석 적재 판단 단위 테스트 (ROS 없이). SRS v3.1 FR-10, 11, 16, 18. 스펙 [시험] 1~7번을 판단 단계에서 확인한다."""
import os

import pytest
import yaml

from dt_palletizer.stacker import (BUSY, DROP, FAULT, HOLD, PLACED, STALE, Stacker,
                                   lane_config, place_z)

COORDS = os.path.join(os.path.dirname(__file__), '..', '..', 'dt_bringup', 'config', 'line_coords.yaml')


def running(capacity=5, count=0):
    """RUNNING, 파레트 READY, 적재 수 count를 받은 상태."""
    s = Stacker(capacity)
    s.on_state('E_STOP')
    s.on_state('IDLE')
    s.on_state('RUNNING')
    s.on_status('READY')
    s.on_count(count)
    return s


def place_one(s, now=0.0):
    """맨 앞 방석을 불러 성공시키고 count를 +1 받는다. 놓은 단(0부터)을 돌려준다."""
    call = s.next_call(now)
    assert call is not None
    assert s.on_response(call.seq, True, call.cid, now + 1) == (PLACED, call.cid)
    s.on_count(s.count + 1)
    return call.level


# ── 1. 정상 방석 5개가 1~5단으로 ──────────────────────────────
def test_five_cushions_stack_one_to_five():
    s = running()
    for i in range(5):
        s.on_arrived(f'C{i:03d}')
    levels = [place_one(s, now=i * 10.0) for i in range(5)]
    assert levels == [0, 1, 2, 3, 4]
    assert [round(place_z(0.1, lv, 0.1), 3) for lv in levels] == [0.1, 0.2, 0.3, 0.4, 0.5]


def test_waits_for_count_before_next_call():
    """성공 뒤 count가 c+1이 되기 전에는 다음 방석을 부르지 않는다 (같은 높이에 또 놓기 방지)."""
    s = running()
    s.on_arrived('C001')
    s.on_arrived('C002')
    call = s.next_call(0.0)
    s.on_response(call.seq, True, 'C001', 1.0)
    assert s.next_call(1.1) is None
    assert s.on_count(0) is False          # 1초마다 오는 옛 값
    assert s.next_call(1.2) is None
    assert s.on_count(1) is True
    nxt = s.next_call(1.3)
    assert nxt.cid == 'C002' and nxt.level == 1


def test_one_call_at_a_time():
    s = running()
    s.on_arrived('C001')
    s.on_arrived('C002')
    assert s.next_call(0.0).cid == 'C001'
    assert s.next_call(0.1) is None


def test_needs_first_values_before_calling():
    """count·status·state를 한 번씩 받기 전에는 부르지 않는다."""
    s = Stacker()
    s.on_arrived('C001')
    s.on_state('RUNNING')
    s.on_status('READY')
    assert s.next_call(0.0) is None
    s.on_count(0)
    assert s.next_call(0.1).cid == 'C001'


@pytest.mark.parametrize('state', ['IDLE', 'ERROR', 'E_STOP'])
def test_calls_only_when_running(state):
    s = running()
    s.on_state(state)
    s.on_arrived('C001')
    assert s.next_call(0.0) is None


@pytest.mark.parametrize('status', ['OUT', 'REFILLING'])
def test_calls_only_when_pallet_ready(status):
    s = running()
    s.on_status(status)
    s.on_arrived('C001')
    assert s.next_call(0.0) is None


# ── 2·3. 5개 차면 멈추고, 교체 후 기억한 방석부터 1단 ────────────
def test_full_pallet_stops_and_new_pallet_restarts_from_level_one():
    s = running()
    for i in range(7):
        s.on_arrived(f'C{i:03d}')
    for i in range(5):
        place_one(s, now=i * 10.0)
    assert s.count == 5
    assert s.next_call(60.0) is None                 # 6번째부터 부르지 않음
    assert s.queue == ['C005', 'C006']               # 기억만 함
    s.on_status('OUT')
    s.on_status('REFILLING')
    s.on_status('READY')
    assert s.next_call(70.0) is None                 # count가 아직 5 (옛 값)
    s.on_count(0)
    call = s.next_call(71.0)
    assert call.cid == 'C005' and call.level == 0    # 1단부터


# ── 4·5. 실패 처리와 retry ───────────────────────────────────
@pytest.mark.parametrize('message', ['timeout', 'ik_failed:approach', 'exception:x'])
def test_failure_holds_request_and_retry_sends_same_once(message):
    s = running(count=2)
    s.on_arrived('C001')
    s.on_arrived('C002')
    call = s.next_call(0.0)
    assert s.on_response(call.seq, False, message, 1.0) == (FAULT, 'C001')
    assert s.next_call(2.0) is None                  # 들고 있는 동안 다음 방석 안 부름
    s.on_state('ERROR')
    assert s.on_retry() is True
    assert s.next_call(3.0) is None                  # RUNNING일 때만
    s.on_state('RUNNING')
    again = s.next_call(4.0)
    assert again.cid == 'C001' and again.level == 2  # 같은 방석, 같은 단
    assert s.on_response(again.seq, False, 'timeout', 5.0) == (FAULT, 'C001')
    assert s.next_call(6.0) is None                  # retry는 한 번만


def test_retry_without_held_request_is_ignored():
    s = running()
    assert s.on_retry() is False
    s.on_arrived('C001')
    place_one(s)
    assert s.on_retry() is False


def test_no_response_within_timeout_is_failure():
    s = running()
    s.on_arrived('C001')
    call = s.next_call(0.0)
    assert s.check_timeout(19.9, 20.0) is None
    lost = s.check_timeout(20.0, 20.0)
    assert lost is call and s.held is call
    assert s.on_response(call.seq, False, 'timeout', 25.0) == (STALE, 'timeout')


def test_late_success_of_given_up_request_counts_as_placed():
    """20초가 지나 포기했는데 나중에 성공이 오면 방석은 파레트 위에 있으므로 놓은 것으로 친다."""
    s = running()
    s.on_arrived('C001')
    call = s.next_call(0.0)
    s.check_timeout(20.0, 20.0)
    assert s.on_response(call.seq, True, 'C001', 21.0) == (PLACED, 'C001')
    assert s.held is None and s.wait_level == 0


def test_estop_message_is_not_a_fault():
    s = running()
    s.on_arrived('C001')
    call = s.next_call(0.0)
    assert s.on_response(call.seq, False, 'E_STOP', 1.0) == (HOLD, 'C001')
    assert s.next_call(2.0) is None


def test_busy_retries_after_one_second():
    s = running()
    s.on_arrived('C001')
    call = s.next_call(0.0)
    assert s.on_response(call.seq, False, 'busy', 1.0) == (BUSY, 'C001')
    assert s.next_call(1.5) is None
    assert s.next_call(2.0).cid == 'C001'


def test_no_cushion_drops_id_and_moves_on():
    s = running()
    s.on_arrived('C001')
    s.on_arrived('C002')
    call = s.next_call(0.0)
    assert s.on_response(call.seq, False, 'no_cushion', 1.0) == (DROP, 'C001')
    assert s.next_call(1.1).cid == 'C002'


# ── 6. E_STOP → 해제 → 시작 ─────────────────────────────────
def test_estop_then_idle_clears_everything():
    s = running(count=3)
    s.on_arrived('C001')
    s.on_arrived('C002')
    call = s.next_call(0.0)
    s.on_state('E_STOP')
    assert s.check_timeout(100.0, 20.0) is None      # E_STOP 중에는 시간 초과로 보지 않음
    s.on_response(call.seq, False, 'E_STOP', 1.0)
    s.on_arrived('C003')
    assert s.next_call(2.0) is None
    assert s.on_state('IDLE') is True
    assert s.queue == [] and s.held is None and s.inflight is None and s.wait_level is None
    assert s.count is None and s.status is None      # 새 값을 받을 때까지 안 부름
    s.on_state('RUNNING')
    s.on_arrived('C010')
    assert s.next_call(3.0) is None
    s.on_status('READY')
    s.on_count(0)
    call = s.next_call(4.0)
    assert call.cid == 'C010' and call.level == 0


def test_response_after_reset_is_ignored():
    s = running()
    s.on_arrived('C001')
    call = s.next_call(0.0)
    s.on_state('E_STOP')
    s.on_state('IDLE')
    assert s.on_response(call.seq, True, 'C001', 1.0)[0] == STALE


def test_waiting_for_count_is_cleared_by_reset_and_warns():
    s = running()
    s.on_arrived('C001')
    call = s.next_call(0.0)
    s.on_response(call.seq, True, 'C001', 1.0)
    assert s.wait_warning(5.9) is None
    assert s.wait_warning(6.0) == pytest.approx(5.0)
    assert s.wait_warning(7.0) is None               # 5초마다 한 번
    s.on_state('E_STOP')
    s.on_state('IDLE')
    assert s.wait_level is None


# ── 7. 같은 id 두 번 ────────────────────────────────────────
def test_same_id_twice_is_called_once():
    s = running()
    assert s.on_arrived('C001') is True
    assert s.on_arrived('C001') is False             # 대기열에 있음
    call = s.next_call(0.0)
    assert s.on_arrived('C001') is False             # 호출 중
    s.on_response(call.seq, True, 'C001', 1.0)
    assert s.on_arrived('C001') is False             # 방금 놓음
    assert s.queue == []


# ── 설정 ────────────────────────────────────────────────────
@pytest.mark.skipif(not os.path.exists(COORDS), reason='저장소 밖에서 실행 (line_coords.yaml 없음)')
def test_lane_config_from_line_coords():
    with open(COORDS, encoding='utf-8') as f:
        coords = yaml.safe_load(f)
    ok = lane_config('ok', coords)
    ng = lane_config('ng', coords)
    assert (ok['arm'], ok['pallet'], ok['topic']) == (2, 1, '/conveyor_1_end_ok')
    assert (ng['arm'], ng['pallet'], ng['topic']) == (3, 2, '/conveyor_1_end_ng')
    assert ok['pick'][:3] == (-6.876, 15.5, 0.111)
    assert ng['pallet_pose'][:3] == (5.4, 18.0, 0.1)


def test_lane_config_rejects_bad_lane_and_missing_coords():
    with pytest.raises(ValueError):
        lane_config('xx', {})
    with pytest.raises(ValueError):
        lane_config('ok', {'pick_ok': {'x': 0, 'y': 0, 'z': 0}})
