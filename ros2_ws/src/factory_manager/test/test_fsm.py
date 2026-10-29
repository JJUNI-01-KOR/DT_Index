from factory_manager.fsm import E_STOP, Fsm, IDLE, PALLET_FULL, RUNNING


def test_start_from_idle():
    f = Fsm()
    assert f.on_start() and f.state == RUNNING


def test_start_ignored_when_running():
    f = Fsm()
    f.on_start()
    assert not f.on_start() and f.state == RUNNING


def test_start_with_full_pallet():
    f = Fsm()
    f.pallets[2] = 12  # 출고 팔레트
    f.on_start()
    assert f.state == PALLET_FULL


def test_start_with_full_reject_pallet():  # 불량 팔레트만 12여도 가득
    f = Fsm()
    f.pallets[1] = 12
    f.on_start()
    assert f.state == PALLET_FULL


def test_estop_then_release():
    f = Fsm()
    f.on_start()
    f.on_estop(True)
    assert f.state == E_STOP
    f.on_estop(False)
    assert f.state == IDLE


def test_estop_release_with_full_pallet():  # FR-16 6단계: 적재 수는 유지
    f = Fsm()
    f.on_start()
    f.pallets[2] = 12
    f.on_estop(True)
    f.on_estop(False)
    assert f.state == PALLET_FULL and f.pallets[2] == 12
