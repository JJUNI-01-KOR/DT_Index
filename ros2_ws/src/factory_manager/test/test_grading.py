"""무게 판정 단위 테스트 (ROS 없이). SRS v3.1 FR-07, FR-08, TC-09.
colcon test --packages-select factory_manager"""
import json

import pytest

from factory_manager.grading import Grader, NG, OK, diverter_cmd, parse_weight


# ── parse_weight: /scale_1_weight 글자 → (id, kg) ──

def test_parse_normal():
    assert parse_weight('{"id": "C012", "kg": 1.02}') == ('C012', 1.02)


def test_parse_broken_json():
    """JSON을 못 읽어도 죽지 않는다 (FR-07 예외 처리)."""
    assert parse_weight('abc') == (None, None)
    assert parse_weight('[1, 2]') == (None, None)


def test_parse_missing_kg():
    assert parse_weight('{"id": "C001"}') == ('C001', None)


def test_parse_missing_id():
    assert parse_weight('{"kg": 1.0}') == (None, 1.0)


def test_parse_bad_kg():
    """숫자가 아닌 무게는 None (true가 1 kg로 읽히지 않게)."""
    assert parse_weight('{"id": "C001", "kg": "1.0"}') == ('C001', None)
    assert parse_weight('{"id": "C001", "kg": true}') == ('C001', None)
    assert parse_weight('{"id": "C001", "kg": NaN}') == ('C001', None)


# ── Grader.grade: 무게 → ok / ng (TC-09) ──

@pytest.fixture
def grader():
    return Grader(0.80, 1.20)   # SRS v3.1 부록 A.5 제안값


@pytest.mark.parametrize('kg, lane', [
    (0.80, OK),   # 하한 = 정상
    (1.20, OK),   # 상한 = 정상
    (1.00, OK),
    (0.79, NG),   # 하한보다 0.01 작음
    (1.21, NG),   # 상한보다 0.01 큼
    (0.70, NG),   # 불량 무게 범위 (부록 A.5)
    (1.30, NG),
])
def test_grade_boundaries(grader, kg, lane):
    assert grader.grade(kg) == lane


def test_grade_no_weight_is_ng(grader):
    """무게가 없으면 불량 (FR-07 예외 처리)."""
    assert grader.grade(None) == NG


def test_grade_float_error_at_boundary(grader):
    """1.1 + 0.1 = 1.2000000000000002 이어도 경계값 정상."""
    assert grader.grade(1.1 + 0.1) == OK


def test_grader_rejects_reversed_range():
    with pytest.raises(ValueError):
        Grader(1.20, 0.80)


# ── diverter_cmd: (id, lane) → /diverter_1_cmd 글자 ──

def test_diverter_cmd_format():
    """SRS 4.4 형식. 키 이름이 Unity와의 약속이라 글자를 다시 읽어 확인한다."""
    assert json.loads(diverter_cmd('C012', OK)) == {'id': 'C012', 'lane': 'ok'}
    assert json.loads(diverter_cmd('C013', NG)) == {'id': 'C013', 'lane': 'ng'}
