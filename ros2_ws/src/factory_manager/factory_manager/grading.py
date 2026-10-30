"""무게 판정 (SRS v3.1 FR-07). rclpy 없이 순수 Python → pytest로 바로 시험."""

OK, NG = 'ok', 'ng'


def parse_weight(text):
    """/scale_1_weight JSON 문자열 → (방석 id, kg). 읽을 수 없거나 kg가 없으면 kg는 None."""
    # TODO(FR-06, 07): json.loads, {"id": "C012", "kg": 1.02}
    raise NotImplementedError


class Grader:
    """정상 범위(하한·상한 포함) 안이면 ok, 밖이거나 무게가 없으면 ng (경고는 노드가 남김)."""

    def __init__(self, min_kg, max_kg):
        self.min_kg = min_kg
        self.max_kg = max_kg

    def grade(self, kg):
        """kg → OK 또는 NG. 경계값은 정상 (TC-09)."""
        # TODO(FR-07)
        raise NotImplementedError
