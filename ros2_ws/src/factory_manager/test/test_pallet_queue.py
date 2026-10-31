"""파레트 적재 수와 대기열 단위 테스트 (ROS 없이). SRS v3.1 FR-12, FR-15, TC-15, TC-16."""
from factory_manager.pallet_queue import PalletBook


def test_count_up_and_queue_once():
    """5번째에만 대기열에 한 번 들어간다 (TC-15)."""
    b = PalletBook(capacity=5)
    results = [b.on_placed(1) for _ in range(5)]
    assert results == [False, False, False, False, True]
    assert b.counts[1] == 5 and b.queue == [1]


def test_placed_when_full_is_ignored():
    """가득 찬 뒤에 또 오면 적재 수와 대기열을 바꾸지 않는다 (FR-12 예외)."""
    b = PalletBook(capacity=5)
    for _ in range(5):
        b.on_placed(1)
    assert b.on_placed(1) is False
    assert b.counts[1] == 5 and b.queue == [1]


def test_first_full_goes_first():
    """먼저 찬 파레트가 대기열 앞에 선다 (FR-13 인수 기준: 파레트2, 파레트1 순서)."""
    b = PalletBook(capacity=2)
    b.on_placed(2)
    b.on_placed(1)
    b.on_placed(2)    # 파레트2가 먼저 참
    b.on_placed(1)
    assert b.queue == [2, 1]


def test_capacity_one_for_m2():
    """적재 기준이 1이면 첫 방석에 바로 대기열에 들어간다 (M2 확인용)."""
    b = PalletBook(capacity=1)
    assert b.on_placed(1) is True and b.queue == [1]

def full_pallet(capacity=5, pallet=1):
    """파레트 자리가 READY이고 가득 찬 PalletBook."""
    b = PalletBook(capacity=capacity)
    b.on_status(pallet, 'READY')
    for _ in range(capacity):
        b.on_placed(pallet)
    return b


def test_repeated_ready_keeps_count():
    """1초마다 오는 같은 READY로는 적재 수가 바뀌지 않는다 (TC-15)."""
    b = full_pallet()
    assert b.on_status(1, 'READY') is False
    assert b.counts[1] == 5 and b.queue == [1]


def test_first_ready_does_not_reset():
    """처음 받은 READY는 바뀐 것으로 보지 않는다."""
    b = PalletBook(capacity=5)
    b.on_placed(1)
    assert b.on_status(1, 'READY') is False
    assert b.counts[1] == 1


def test_refill_resets_count_once():
    """OUT → REFILLING → READY면 0이 되고, 이어 오는 READY로는 다시 되돌리지 않는다 (TC-16)."""
    b = full_pallet()
    b.pop_front()     # AGV에 맡겨 대기열에서 빠짐
    assert b.on_status(1, 'OUT') is False
    assert b.on_status(1, 'REFILLING') is False
    assert b.counts[1] == 5    # READY가 되기 전까지 5로 남는다 (부록 A.4)
    assert b.on_status(1, 'READY') is True
    assert b.counts[1] == 0
    assert b.on_status(1, 'READY') is False
    assert b.queue == []


def test_refill_removes_pallet_from_queue():
    """대기열에 남은 채 비워져도 다시 찼을 때 두 번 들어가지 않는다 (TC-15에 이은 TC-16)."""
    b = full_pallet()
    b.on_status(1, 'OUT')
    b.on_status(1, 'READY')
    assert b.queue == []
    for _ in range(5):
        b.on_placed(1)
    assert b.queue == [1]


def test_front_ready_empty_queue():
    assert PalletBook(capacity=5).front_ready() is None


def test_front_ready_when_ready_and_full():
    assert full_pallet(pallet=2).front_ready() == 2


def test_front_ready_needs_ready_status():
    """자리 상태가 READY가 아니면 출동하지 않는다."""
    b = full_pallet()
    b.on_status(1, 'OUT')
    assert b.front_ready() is None


def test_front_ready_only_checks_front():
    """맨 앞이 READY가 아니면 뒤 파레트가 READY여도 앞지르지 않는다."""
    b = PalletBook(capacity=1)
    b.on_status(1, 'READY')
    b.on_status(2, 'READY')
    b.on_placed(2)
    b.on_placed(1)
def test_front_ready_needs_ready_status():
    """자리 상태가 READY가 아니면 출동하지 않는다."""
    b = full_pallet()
    b.on_status(1, 'OUT')
    assert b.front_ready() is None


def test_front_ready_only_checks_front():
    """맨 앞이 READY가 아니면 뒤 파레트가 READY여도 앞지르지 않는다."""
    b = PalletBook(capacity=1)
    b.on_status(1, 'READY')
    b.on_status(2, 'READY')
    b.on_placed(2)
    b.on_placed(1)
    b.on_status(2, 'OUT')
    assert b.queue == [2, 1] and b.front_ready() is None

