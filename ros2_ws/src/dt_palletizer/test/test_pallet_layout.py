"""pallet_layout 단위 테스트 (ROS 없이). colcon test --packages-select dt_palletizer"""
import math

import pytest

from dt_palletizer.pallet_layout import slot_pose

ORIGIN = (0.0, 0.0, 0.0, 0.0)
PITCH = (0.4, 0.3)
SLOT_Z = 0.25


def test_slot1_bottom_left():
    assert slot_pose(1, ORIGIN, PITCH, SLOT_Z)[:3] == pytest.approx((-0.2, -0.15, 0.125))


def test_slot2_moves_x_only():
    a, b = slot_pose(1, ORIGIN, PITCH, SLOT_Z), slot_pose(2, ORIGIN, PITCH, SLOT_Z)
    assert b[0] - a[0] == pytest.approx(0.4)
    assert b[1:3] == pytest.approx(a[1:3])


def test_slot5_second_layer():
    assert slot_pose(5, ORIGIN, PITCH, SLOT_Z)[2] == pytest.approx(0.375)


def test_slot12_top_right_third_layer():
    assert slot_pose(12, ORIGIN, PITCH, SLOT_Z)[:3] == pytest.approx((0.2, 0.15, 0.625))


def test_yaw_90():
    x, y, *_ = slot_pose(2, (0.0, 0.0, 0.0, math.pi / 2), PITCH, SLOT_Z)
    assert (x, y) == pytest.approx((0.15, 0.2))


def test_out_of_range():
    with pytest.raises(ValueError):
        slot_pose(13, ORIGIN, PITCH, SLOT_Z)
