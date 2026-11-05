"""/factory/stats JSON (SRS v3.1 FR-20, 부록 A.4). rclpy 없이 순수 Python → pytest로 바로 시험.

factory_manager가 기록한 값과 받은 값을 그대로 담는다. 상태 패널은 표시만 하고 따로 세지 않는다.

11/05 (BUG-003): 이번 회차(마지막 Clear 뒤)에 투입 완료가 온 방석만 정상·불량 수에 센다.
  Clear 전에 투입된 방석이 Clear 뒤에 판정되면 투입 수 ≠ 정상 수 + 불량 수가 됐다.
  - 판정이 투입 완료보다 먼저 오면 보류했다가 투입 완료 때 센다
  - Clear 때 명단과 보류를 비운다 → Unity를 다시 켜 id가 C001부터 다시 매겨져도 새 방석은 센다
"""
import json

class Stats:

    def __init__(self):
        self.fed = 0       # 투입 수
        self.ok = 0        # 정상 수 (판정할 때 +1)
        self.ng = 0        # 불량 수 (판정할 때 +1)
        self.last = None   # 마지막 판정 {"id", "kg", "lane"}
        self.fed_ids = set()  # 이번 회차에 투입 완료가 온 방석
        self.pending = {}     # 투입 완료 전에 판정이 온 방석 {id: (kg, lane)}

    def on_fed(self, cushion):
        """투입 완료 1건 (FR-02). 투입 수 +1. 먼저 온 판정이 보류돼 있으면 지금 센다."""
        self.fed += 1
        self.fed_ids.add(cushion)
        if cushion in self.pending:
            kg, lane = self.pending.pop(cushion)
            self._count(cushion, kg, lane)

    def on_graded(self, cushion, kg, lane):
        """판정 1건 기록 (FR-07 2번). 이번 회차에 투입된 방석이면 바로 세고 True,
        아직 투입 완료가 안 왔으면 보류하고 False (투입 완료가 오면 그때 센다)."""
        if cushion not in self.fed_ids:
            self.pending[cushion] = (kg, lane)
            return False
        self._count(cushion, kg, lane)
        return True

    def _count(self, cushion, kg, lane):
        """정상 수 또는 불량 수 +1, 마지막 판정 갱신."""
        if lane == 'ok':
            self.ok += 1
        else:
            self.ng += 1
        self.last = {'id': cushion, 'kg': kg, 'lane': lane}

    def to_json(self, fsm, bin_left, load, pallets, agv, agv_cycle_sec):
        """부록 A.4 형식의 JSON 문자열.
        키: state, fault, estop, bin_left, fed, ok, ng, last, main, lane_ok, lane_ng,
            pallet_1, pallet_2, queue, agv{cmd, loaded, pallet}, agv_cycle_sec"""
        #(FR-20)
        main, lane_ok, lane_ng = (load['main'], load['ok'], load['ng']) if load else (0, 0, 0)
        data = {
            'state': fsm.state,
            'fault': fsm.fault(),
            'estop': fsm.estop,
            'bin_left': bin_left,
            'fed': self.fed,
            'ok': self.ok,
            'ng': self.ng,
            'last': self.last,
            'main': main,
            'lane_ok': lane_ok,
            'lane_ng': lane_ng,
            'pallet_1': pallets.counts[1],
            'pallet_2': pallets.counts[2],
            'queue': pallets.queue,
            'agv': {
                'cmd': agv.last_cmd,
                'loaded': agv.loaded,
                'pallet': agv.carrying,
            },
            'agv_cycle_sec': agv_cycle_sec,
        }
        return json.dumps(data)

    def reset(self):
        """E_STOP 해제 때 투입 수, 정상 수, 불량 수를 0으로 (SRS 6장).
        명단과 보류도 비운다 → Clear 전에 투입된 방석은 나중에 판정돼도 세지 않는다."""
        self.fed = self.ok = self.ng = 0
        self.last = None
        self.fed_ids.clear()
        self.pending.clear()
