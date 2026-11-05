"""AGV1 명령 단위 테스트 (ROS 없이). SRS v3.1 FR-13, 부록 A.6, TC-17, TC-41."""
from factory_manager.agv_dispatch import AgvDispatcher, FINAL, HOME, dock
from factory_manager.pallet_queue import PalletBook


def idle_agv():
    """적재 없음을 보고받은 AGV."""
    a = AgvDispatcher()
    a.on_loaded(False, True, None)
    return a


def test_dock_name():
    assert dock(1) == 'P1_DOCK' and dock(2) == 'P2_DOCK'


def test_dispatch_sets_carrying():
    a = idle_agv()
    assert a.try_dispatch(True, 2) == 'P2_DOCK'
    assert a.carrying == 2 and a.last_cmd == 'P2_DOCK'


def test_no_dispatch_without_front_pallet():
    assert idle_agv().try_dispatch(True, None) is None


def test_no_dispatch_when_not_running():
    """RUNNING이 아니면 새 도킹 명령을 보내지 않는다."""
    a = idle_agv()
    assert a.try_dispatch(False, 1) is None and a.carrying is None


def test_no_dispatch_before_first_loaded():
    """적재 여부를 아직 못 받았으면 출동하지 않는다."""
    assert AgvDispatcher().try_dispatch(True, 1) is None


def test_no_dispatch_twice():
    """1초마다 검사해도 도킹 명령은 한 번만 나간다."""
    a = idle_agv()
    a.try_dispatch(True, 1)
    assert a.try_dispatch(True, 2) is None and a.carrying == 1


def test_no_dispatch_when_loaded():
    a = idle_agv()
    a.on_loaded(True, True, None)
    assert a.try_dispatch(True, 1) is None


def test_final_when_attached():
    a = idle_agv()
    a.try_dispatch(True, 1)
    assert a.on_loaded(True, True, None) == FINAL


def test_repeated_loaded_sends_nothing():
    """1초마다 오는 같은 값으로는 명령을 다시 보내지 않는다."""
    a = idle_agv()
    a.try_dispatch(True, 1)
    assert a.on_loaded(False, True, None) is None
    a.on_loaded(True, True, None)
    assert a.on_loaded(True, True, None) is None


def test_home_when_detached_and_no_next():
    a = idle_agv()
    a.try_dispatch(True, 1)
    a.on_loaded(True, True, None)
    assert a.on_loaded(False, True, None) == HOME
    assert a.carrying is None and a.last_cmd == HOME


def test_next_dock_when_detached():
    a = idle_agv()
    a.try_dispatch(True, 2)
    a.on_loaded(True, True, None)
    assert a.on_loaded(False, True, 1) == 'P1_DOCK'
    assert a.carrying == 1


def test_no_command_without_carrying():
    """운반 중 파레트가 없으면 적재 여부가 바뀌어도 FINAL·HOME을 보내지 않는다."""
    a = idle_agv()
    assert a.on_loaded(True, True, None) is None
    assert a.on_loaded(False, True, None) is None


def test_reset_clears_loaded():
    """해제 뒤 적재 여부를 새로 받기 전에는 변화로 보지 않는다 (FR-16)."""
    a = idle_agv()
    a.try_dispatch(True, 1)
    a.on_loaded(True, True, None)
    a.reset()
    assert (a.loaded, a.carrying, a.last_cmd) == (None, None, None)
    assert a.on_loaded(False, False, None) is None


def test_error_finishes_unloading_then_redispatch():
    """파레트1 운반 중 대기열 [2]에서 ERROR: 하역까지 하고 HOME, RUNNING이 되면 P2_DOCK (TC-41)."""
    a = idle_agv()
    a.try_dispatch(True, 1)
    assert a.on_loaded(True, False, 2) == FINAL      # 끌던 파레트는 ERROR여도 FINAL
    assert a.on_loaded(False, False, 2) == HOME      # RUNNING이 아니면 다음 출동 대신 HOME
    assert a.try_dispatch(False, 2) is None
    assert a.try_dispatch(True, 2) == 'P2_DOCK'      # RUNNING으로 돌아옴


def test_dock_sent_at_set_on_dispatch():
    a = idle_agv()
    a.try_dispatch(True, 1, now=100)
    assert a.dock_sent_at == 100


def test_cycle_sec_recorded_on_detach():
    """PN_DOCK을 보낸 시각부터 분리될 때까지의 시간."""
    a = idle_agv()
    a.try_dispatch(True, 1, now=100)
    a.on_loaded(True, True, None, now=120)             # FINAL
    a.on_loaded(False, True, None, now=150)            # 분리
    assert a.last_cycle_sec == 50
    assert a.dock_sent_at is None


def test_next_dock_sent_at_after_chained_dispatch():
    """분리되며 바로 다음 PN_DOCK이 나갈 때, 그 시각이 다음 사이클의 시작 시각이 된다."""
    book = PalletBook(capacity=1)
    book.on_status(1, 'READY')
    book.on_status(2, 'READY')
    book.on_placed(2)
    book.on_placed(1)
    a = idle_agv()
    a.try_dispatch(True, book.front_ready(), now=0)
    book.pop_front()
    a.on_loaded(True, True, book.front_ready(), now=30)         # FINAL
    cmd = a.on_loaded(False, True, book.front_ready(), now=50)  # 분리, 바로 다음 P1_DOCK
    book.pop_front()
    assert cmd == 'P1_DOCK'
    assert a.last_cycle_sec == 50
    assert a.dock_sent_at == 50


def test_reset_clears_cycle_fields():
    a = idle_agv()
    a.try_dispatch(True, 1, now=100)
    a.on_loaded(True, True, None, now=120)
    a.on_loaded(False, True, None, now=150)
    a.reset()
    assert (a.dock_sent_at, a.last_cycle_sec) == (None, None)


def test_cycle_sec_none_without_now():
    """now를 안 넘겨도(기존 호출 방식) 에러 없이 그대로 None."""
    a = idle_agv()
    a.try_dispatch(True, 1)
    a.on_loaded(True, True, None)
    a.on_loaded(False, True, None)
    assert a.last_cycle_sec is None


def test_pallet2_then_pallet1_order():
    """파레트2, 파레트1 순서로 차면 P2_DOCK → FINAL → P1_DOCK → FINAL → HOME (TC-17)."""
    book = PalletBook(capacity=1)
    book.on_status(1, 'READY')
    book.on_status(2, 'READY')
    a = idle_agv()
    sent = []

    def send(cmd):
        if cmd is None:
            return
        sent.append(cmd)
        if cmd.endswith('_DOCK'):
            book.pop_front()    # ④에서 노드가 할 일

    book.on_placed(2)
    book.on_placed(1)
    send(a.try_dispatch(True, book.front_ready()))
    send(a.try_dispatch(True, book.front_ready()))    # 적재 여부가 바뀌기 전에는 나가지 않음
    send(a.on_loaded(True, True, book.front_ready()))
    send(a.on_loaded(False, True, book.front_ready()))
    send(a.on_loaded(True, True, book.front_ready()))
    send(a.on_loaded(False, True, book.front_ready()))
    assert sent == ['P2_DOCK', FINAL, 'P1_DOCK', FINAL, HOME]
    assert book.queue == []