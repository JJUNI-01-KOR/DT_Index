"""/factory/stats JSON (SRS v3.1 FR-20, 부록 A.4). rclpy 없이 순수 Python → pytest로 바로 시험.

factory_manager가 기록한 값과 받은 값을 그대로 담는다. 상태 패널은 표시만 하고 따로 세지 않는다.
"""


class Stats:

    def __init__(self):
        self.fed = 0       # 투입 수
        self.ok = 0        # 정상 수 (판정할 때 +1)
        self.ng = 0        # 불량 수 (판정할 때 +1)
        self.last = None   # 마지막 판정 {"id", "kg", "lane"}

    def to_json(self, fsm, bin_left, load, pallets, agv, agv_cycle_sec):
        """부록 A.4 형식의 JSON 문자열.
        키: state, fault, estop, bin_left, fed, ok, ng, last, main, lane_ok, lane_ng,
            pallet_1, pallet_2, queue, agv{cmd, loaded, pallet}, agv_cycle_sec"""
        # TODO(FR-20)
        raise NotImplementedError

    def reset(self):
        """E_STOP 해제 때 투입 수, 정상 수, 불량 수를 0으로 (SRS 6장)."""
        self.fed = self.ok = self.ng = 0
        self.last = None
