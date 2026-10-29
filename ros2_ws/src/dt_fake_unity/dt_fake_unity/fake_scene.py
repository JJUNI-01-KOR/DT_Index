"""가짜 Unity 장면 상태 — rclpy 없는 순수 Python (CONVENTIONS 2절). 담당 D · scope `fake`

fake_unity_node.py가 이 클래스를 감싸서 토픽·서비스로 내보낸다.
pytest로 ROS 없이 시험한다: colcon test --packages-select dt_fake_unity

흉내 내는 Unity 장비
  - 적재함(BoxBin)   : 20개, 로봇팔1이 하나씩 꺼내 컨베이어에 올림. 비어 있을 때 꺼내면 다시 채움
  - 컨베이어          : 올라간 박스가 line_travel_sec 뒤 칸에 도착
  - 불량 칸(RejectSlot): 1칸. 차 있으면 다음 불량 박스는 reject_wait
  - 대기 칸(EndSlots) : 2칸(end_1, end_2). 다 차면 end_wait. 로봇팔3은 먼저 온 박스부터 집음
  - AGV1             : 상차 구역 ↔ 출고 팔레트 구역 왕복, 박스 1개 실음

로봇팔 서비스는 1단계 약속대로 항상 성공한다 (fail_arm_id 제외).
빈 칸에서 집으라는 요청처럼 순서가 이상한 호출은 경고(warnings)로만 알린다.
"""

ZONE_REJECT = 'reject'
ZONE_REJECT_WAIT = 'reject_wait'
ZONE_END_WAIT = 'end_wait'

KIND_NORMAL = 'N'
KIND_REJECT = 'R'


def parse_box_sequence(text):
    """'N,N,R' → ['N', 'N', 'R']. 비었거나 N·R 말고 다른 글자가 있으면 ValueError."""
    seq = [s.strip().upper() for s in str(text).split(',') if s.strip()]
    if not seq:
        raise ValueError('box_sequence가 비어 있습니다 (예: "N,N,R")')
    bad = [s for s in seq if s not in (KIND_NORMAL, KIND_REJECT)]
    if bad:
        raise ValueError(f'box_sequence에는 N(정상)과 R(규격 초과)만 씁니다: {bad}')
    return seq


class FakeScene:
    """Unity 장면에서 일어나는 일을 숫자로만 흉내 낸다.

    바꾸는 메서드는 (결과, events, warnings)를 돌려준다.
      events   : [(topic_key, zone, box_id)]  topic_key = 'detect_1' | 'detect_2'
      warnings : 로그로만 남길 문장
    """

    END_SLOT_COUNT = 2

    def __init__(self, box_sequence='N,R', bin_capacity=20):
        if int(bin_capacity) < 1:
            raise ValueError('bin_capacity는 1 이상이어야 합니다')
        self.seq = parse_box_sequence(box_sequence)
        self.bin_capacity = int(bin_capacity)
        self.bin = self.bin_capacity      # 적재함 박스 수 → /bin/count
        self.on_line = 0                  # 컨베이어 위 박스 수 → /conveyor/box_count
        self.box_no = 0                   # 마지막으로 꺼낸 박스 번호 (BOX_001부터, 중복 없음)
        self.kinds = {}                   # box_id → 'N' | 'R'

        self.reject_slot = None           # 불량 칸에 있는 박스
        self.reject_waiting = []          # 불량 칸이 차서 기다리는 박스 (먼저 온 순)
        self.end_slots = [None] * self.END_SLOT_COUNT   # end_1, end_2
        self.end_order = []               # 대기 칸 박스, 도착 순
        self.end_waiting = []             # 대기 칸이 다 차서 기다리는 박스

        self.agv_at_load = True           # True = 상차 구역
        self.agv_moving = False
        self.agv_cargo = None             # AGV 위 박스

    # ── 로봇팔1: 적재함 → 컨베이어 ─────────────────────────
    def pick_from_bin(self):
        """박스 하나를 꺼내 컨베이어에 올린다. 돌려주는 값: (box_id, [], warnings)."""
        warnings = []
        if self.bin <= 0:
            self.bin = self.bin_capacity
            warnings.append(f'적재함이 비어 있어 {self.bin_capacity}개로 다시 채움')
        self.bin -= 1
        self.box_no += 1
        box_id = f'BOX_{self.box_no:03d}'
        self.kinds[box_id] = self.seq[(self.box_no - 1) % len(self.seq)]
        self.on_line += 1
        return box_id, [], warnings

    # ── 컨베이어 끝: 칸 도착 ─────────────────────────────────
    def box_arrives(self, box_id):
        """박스가 칸 앞에 도착. 불량이면 불량 칸, 정상이면 대기 칸으로 간다."""
        kind = self.kinds.get(box_id)
        if kind is None:
            return None, [], [f'모르는 박스 도착: {box_id}']
        if kind == KIND_REJECT:
            if self.reject_slot is None:
                self.reject_slot = box_id
                return box_id, [('detect_1', ZONE_REJECT, box_id)], []
            self.reject_waiting.append(box_id)
            return box_id, [('detect_1', ZONE_REJECT_WAIT, box_id)], []

        free = self._free_end_slot()
        if free is not None:
            self._put_end(free, box_id)
            return box_id, [('detect_2', f'end_{free + 1}', box_id)], []
        self.end_waiting.append(box_id)
        return box_id, [('detect_2', ZONE_END_WAIT, box_id)], []

    # ── 로봇팔2: 불량 칸 → 불량 팔레트 ──────────────────────
    def pick_reject(self, box_id=''):
        warnings, events = [], []
        if self.reject_slot is None:
            return '', [], ['불량 칸이 비어 있는데 로봇팔2 집기 요청 (1단계라 성공으로 응답)']
        picked = self.reject_slot
        if box_id and box_id != picked:
            warnings.append(f'요청 box_id {box_id} ≠ 불량 칸 박스 {picked}')
        self.reject_slot = None
        self.on_line = max(0, self.on_line - 1)
        if self.reject_waiting:
            nxt = self.reject_waiting.pop(0)
            self.reject_slot = nxt
            events.append(('detect_1', ZONE_REJECT, nxt))
        return picked, events, warnings

    # ── 로봇팔3: 대기 칸 → AGV1 ─────────────────────────────
    def pick_end(self, box_id=''):
        warnings, events = [], []
        if not self.end_order:
            return '', [], ['대기 칸이 비어 있는데 로봇팔3 집기 요청 (1단계라 성공으로 응답)']
        if box_id and box_id in self.end_order:
            picked = box_id
        else:
            if box_id:
                warnings.append(f'요청 box_id {box_id}가 대기 칸에 없어 먼저 온 박스를 집음')
            picked = self.end_order[0]
        if not self.agv_at_load or self.agv_moving:
            warnings.append('AGV1이 상차 구역에 없는데 로봇팔3 상차 요청')
        if self.agv_cargo is not None:
            warnings.append(f'AGV1에 이미 {self.agv_cargo}가 있는데 상차 요청')

        slot = self.end_slots.index(picked)
        self.end_slots[slot] = None
        self.end_order.remove(picked)
        self.on_line = max(0, self.on_line - 1)
        self.agv_cargo = picked
        if self.end_waiting:
            nxt = self.end_waiting.pop(0)
            self._put_end(slot, nxt)
            events.append(('detect_2', f'end_{slot + 1}', nxt))
        return picked, events, warnings

    # ── 로봇팔4: AGV1 → 출고 팔레트 ─────────────────────────
    def pick_from_agv(self, box_id=''):
        warnings = []
        if self.agv_at_load or self.agv_moving:
            warnings.append('AGV1이 하역 위치에 도착하지 않았는데 로봇팔4 집기 요청')
        if self.agv_cargo is None:
            return '', [], warnings + ['AGV1이 비어 있는데 로봇팔4 집기 요청 (1단계라 성공으로 응답)']
        picked = self.agv_cargo
        self.agv_cargo = None
        return picked, [], warnings

    # ── AGV1 ────────────────────────────────────────────────
    def agv_depart(self):
        """/agv_1_goal을 받았을 때. 돌려주는 값: 이번 이동이 하역 구역으로 가는지."""
        to_unload = self.agv_at_load
        self.agv_moving = True
        return to_unload

    def agv_arrive(self, to_unload):
        """도착. 돌려주는 값: 발행할 /agv_1_status (하역 구역 ARRIVED, 상차 구역 IDLE)."""
        self.agv_moving = False
        self.agv_at_load = not to_unload
        return 'ARRIVED' if to_unload else 'IDLE'

    # ── 내부 ────────────────────────────────────────────────
    def _free_end_slot(self):
        for i, box in enumerate(self.end_slots):
            if box is None:
                return i
        return None

    def _put_end(self, slot, box_id):
        self.end_slots[slot] = box_id
        self.end_order.append(box_id)
