"""투입 조건 (SRS v3.1 FR-02, FR-04, 부록 A.3). rclpy 없이 순수 Python → pytest로 바로 시험."""
import json 

def parse_load(text):
    """/conveyor_1_load JSON 문자열 → {"main", "ok", "ng"}. 읽을 수 없으면 None."""
    try:
        data = json.loads(text)
    except (TypeError, ValueError):
        return None
    if not isinstance(data, dict):
        return None 
    
    load = {}
    for key in ('main', 'ok', 'ng'):
        value = data.get(key)
        if isinstance(value, bool) or not isinstance(value, int) or value < 0:
            return None
        load[key] = value
    return load 

class FeedRule:
    """로봇팔1에 다음 투입을 요청해도 되는지 판단한다.

    조건: 적재함에 방석 있음, 직전 방석의 무게 보고 받음, 로봇팔1 사용 중 아님,
          main + ok와 main + ng가 모두 레인 용량 미만, /conveyor_1_load를 load_timeout_sec 안에 받음.
    """

    def __init__(self, lane_capacity=3, load_timeout_sec=3.0):
        self.lane_capacity = lane_capacity
        self.load_timeout_sec = load_timeout_sec
        self.load = None          # 마지막 /conveyor_1_load {"main", "ok", "ng"}
        self.load_time = None     # 그 값을 받은 시각
        self.waiting_id = None    # 투입했는데 아직 무게 보고가 없는 방석 id

    def on_load(self, load, now):
        """/conveyor_1_load 수신 (FR-04)."""
        self.load = load
        self.load_time = now

    def on_fed(self, cushion_id):
        """팔1 투입 성공. 그 방석의 무게 보고를 기다린다 (FR-02)."""
        # TODO(FR-02)
        self.waiting_id = cushion_id

    def on_weight(self, cushion_id):
        """무게 보고 수신. 기다리던 방석이면 대기를 푼다 (FR-02)."""
        if self.waiting_id is None or cushion_id != self.waiting_id:
            return False
        self.waiting_id = None
        return True

    def can_feed(self, bin_count, arm_busy, now):
        """위 조건을 모두 만족하면 True."""
        if bin_count <= 0 or arm_busy or self.waiting_id is not None:
            return False
        if self.load is None or now - self.load_time >= self.load_timeout_sec:
            return False
        main = self.load['main']  # 무게를 재기 전에는 어느 레인으로 갈지 모르므로 두 레인을 모두 본다
        return main + self.load['ok'] < self.lane_capacity and main + self.load['ng'] < self.lane_capacity

    def reset(self):
        """E_STOP 해제 때 초기화 (FR-16 6번)."""
        self.load = None
        self.load_time = None
        self.waiting_id = None
