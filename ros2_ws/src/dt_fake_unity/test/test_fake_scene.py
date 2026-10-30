"""fake_scene 단위 테스트 (ROS 없이). colcon test --packages-select dt_fake_unity"""
import pytest

from dt_fake_unity.fake_scene import FakeScene, WEIGHT_KG, parse_sequence


def test_parse_sequence():
    assert parse_sequence(' n, D ,N ') == ['N', 'D', 'N']
    with pytest.raises(ValueError):
        parse_sequence('')
    with pytest.raises(ValueError):
        parse_sequence('N,R')


def test_initial_values():
    s = FakeScene('N,D', 20)
    assert s.bin == 20 and s.load() == {'main': 0, 'ok': 0, 'ng': 0}
    assert s.pallet_status == {1: 'READY', 2: 'READY'}


def test_pick_from_bin_ids_and_weights():
    s = FakeScene('N,D', 20)
    first, _, _ = s.pick_from_bin()
    second, _, _ = s.pick_from_bin()
    assert (first, second) == ('C001', 'C002')
    assert s.weights == {'C001': WEIGHT_KG['N'], 'C002': WEIGHT_KG['D']}
    assert s.bin == 18 and s.load()['main'] == 2


def test_refill_when_empty():
    s = FakeScene('N', 1)
    s.pick_from_bin()
    cushion, _, warns = s.pick_from_bin()
    assert cushion == 'C002' and s.bin == 0 and warns


def test_reset_keeps_bin():
    s = FakeScene('N', 20)
    s.pick_from_bin()
    s.reset()
    assert s.bin == 19 and s.load()['main'] == 0
