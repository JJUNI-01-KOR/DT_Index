"""fake_scene 단위 테스트 (ROS 없이). colcon test --packages-select dt_fake_unity"""
import pytest

from dt_fake_unity.fake_scene import FakeScene, parse_box_sequence


def test_parse_box_sequence():
    assert parse_box_sequence(' n, R ,N ') == ['N', 'R', 'N']
    with pytest.raises(ValueError):
        parse_box_sequence('')
    with pytest.raises(ValueError):
        parse_box_sequence('N,X')


def test_initial_values():
    s = FakeScene('N,R', bin_capacity=20)
    assert (s.bin, s.on_line) == (20, 0)
    assert s.agv_at_load and s.agv_cargo is None


def test_arm1_ids_and_counts():
    s = FakeScene('N', bin_capacity=20)
    box, events, warns = s.pick_from_bin()
    assert box == 'BOX_001' and events == [] and warns == []
    assert (s.bin, s.on_line) == (19, 1)
    assert s.pick_from_bin()[0] == 'BOX_002'


def test_bin_refills_only_when_empty():
    s = FakeScene('N', bin_capacity=2)
    s.pick_from_bin()
    s.pick_from_bin()
    assert s.bin == 0                      # 0도 한 번은 보인다
    box, _, warns = s.pick_from_bin()
    assert box == 'BOX_003' and s.bin == 1 and warns


def test_sequence_decides_zone():
    s = FakeScene('N,R')
    n, _, _ = s.pick_from_bin()
    r, _, _ = s.pick_from_bin()
    assert s.box_arrives(n)[1] == [('detect_2', 'end_1', n)]
    assert s.box_arrives(r)[1] == [('detect_1', 'reject', r)]


def test_reject_slot_wait_then_enter():
    s = FakeScene('R')
    a = s.pick_from_bin()[0]
    b = s.pick_from_bin()[0]
    s.box_arrives(a)
    assert s.box_arrives(b)[1] == [('detect_1', 'reject_wait', b)]
    picked, events, _ = s.pick_reject()
    assert picked == a
    assert events == [('detect_1', 'reject', b)]
    assert s.on_line == 1


def test_end_slots_fill_wait_and_fifo():
    s = FakeScene('N')
    boxes = [s.pick_from_bin()[0] for _ in range(3)]
    zones = [s.box_arrives(b)[1][0][1] for b in boxes]
    assert zones == ['end_1', 'end_2', 'end_wait']
    picked, events, warns = s.pick_end()
    assert picked == boxes[0] and warns == []                 # 먼저 온 박스부터
    assert events == [('detect_2', 'end_1', boxes[2])]        # 비운 칸으로 대기 박스가 들어감
    assert s.agv_cargo == boxes[0]
    assert s.end_order == [boxes[1], boxes[2]]


def test_agv_round_trip_and_arm4():
    s = FakeScene('N')
    box = s.pick_from_bin()[0]
    s.box_arrives(box)
    s.pick_end()
    assert s.agv_depart() is True
    assert s.agv_arrive(True) == 'ARRIVED'
    picked, _, warns = s.pick_from_agv()
    assert picked == box and warns == [] and s.agv_cargo is None
    assert s.agv_depart() is False
    assert s.agv_arrive(False) == 'IDLE'
    assert s.agv_at_load


def test_out_of_order_calls_warn_but_do_not_break():
    s = FakeScene('N')
    assert s.pick_reject() == ('', [], s.pick_reject()[2])
    box, _, warns = s.pick_end()
    assert box == '' and warns
    box, _, warns = s.pick_from_agv()
    assert box == '' and warns
    assert s.on_line == 0
