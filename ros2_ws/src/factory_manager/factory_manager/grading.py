"""무게 판정 (SRS v3.1 FR-07). rclpy 없이 순수 Python → pytest로 바로 시험."""

import json
import math 

OK, NG = 'ok', 'ng'


def parse_weight(text):
    """/scale_1_weight JSON 문자열 → (방석 id, kg). 읽을 수 없거나 kg가 없으면 kg는 None."""
    # (FR-06, 07): json.loads, {"id": "C012", "kg": 1.02}
    try:
        data = json.loads(text)
    except (TypeError, ValueError):
        return None, None
    if not isinstance(data, dict):
        return None, None

    cushion = data.get('id')
    if not isinstance(cushion, str) or not cushion:
        cushion = None

    kg = data.get('kg')
    if isinstance(kg, bool) or not isinstance(kg, (int, float)) or not math.isfinite(kg):
        kg = None
    return cushion, kg

def diverter_cmd(cushion, lane):
    """/diverter_1_cmd JSON 문자열 {"id": "C012", "lane": "ok"} (FR-08)."""
    return json.dumps({'id': cushion, 'lane': lane})


class Grader:
    """정상 범위(하한·상한 포함) 안이면 ok, 밖이거나 무게가 없으면 ng (경고는 노드가 남김)."""

    def __init__(self, min_kg, max_kg):
        if min_kg > max_kg:
            raise ValueError(f'정상 무게 하한 {min_kg}이 상한 {max_kg}보다 큽니다')
        self.min_kg = min_kg
        self.max_kg = max_kg

    def grade(self, kg):
        """kg → OK 또는 NG. 경계값은 정상 (TC-09)Unity는 소수 둘째 자리까지 보내므로(FR-06) 둘째 자리로 맞춘 뒤 비교한다."""
        # (FR-07)
        if kg is None:
            return NG
        kg= round(kg,2)
        return OK if  round(self.min_kg, 2) <= kg <= round(self.max_kg, 2) else NG
