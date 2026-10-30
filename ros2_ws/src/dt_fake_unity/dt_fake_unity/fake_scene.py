"""가짜 Unity 장면 상태 — rclpy 없는 순수 Python (CONVENTIONS 2절). 담당 D · scope `fake`

SRS v3.1 기준 뼈대. fake_unity_node.py가 이 클래스를 감싸서 토픽·서비스로 내보낸다.
pytest로 ROS 없이 시험한다: colcon test --packages-select dt_fake_unity

흉내 낼 Unity 장비 (지금 동작하는 것은 적재함과 로봇팔1 꺼내기뿐, 나머지는 TODO)
  - 적재함        : 20개, 로봇팔1이 하나씩 꺼내 투입 위치에 올림. 비어 있을 때 꺼내면 다시 채움 (FR-02, 05)
  - 메인 라인     : 투입 → 무게 센서(/scale_1_weight 1회) → 분기 게이트(/diverter_1_cmd대로) (FR-06, 08)
  - 레인 2개      : ok, ng. 집는 자리에 들어오면 /conveyor_1_end_<lane> (FR-09)
  - 방석 수       : /conveyor_1_load {"main", "ok", "ng"} (FR-04)
  - 파레트 자리 2개: READY · OUT · REFILLING (/pallet_N_status, FR-15)
  - AGV1          : /agv_1_goal의 wp로 이동, hitch로 연결·분리 → /agv_1_status (FR-13)
"""

KIND_NORMAL = 'N'
KIND_DEFECT = 'D'

# 무게 (kg). SRS v3.1 부록 A.5 제안값 범위 안의 고정값 (판정 범위 경계에서 0.05 kg 이상 떨어짐)
WEIGHT_KG = {KIND_NORMAL: 1.00, KIND_DEFECT: 1.20}


def parse_sequence(text):
    """'N,N,D' → ['N', 'N', 'D']. 비었거나 N·D 말고 다른 글자가 있으면 ValueError."""
    seq = [s.strip().upper() for s in str(text).split(',') if s.strip()]
    if not seq:
        raise ValueError('cushion_sequence가 비어 있습니다 (예: "N,N,D")')
    bad = [s for s in seq if s not in (KIND_NORMAL, KIND_DEFECT)]
    if bad:
        raise ValueError(f'cushion_sequence에는 N(정상)과 D(불량)만 씁니다: {bad}')
    return seq


class FakeScene:
    """Unity 장면에서 일어나는 일을 숫자로만 흉내 낸다.

    바꾸는 메서드는 (결과, events, warnings)를 돌려준다.
      events   : [(topic, data)]  노드가 그대로 발행한다
      warnings : 로그로만 남길 문장
    """

    def __init__(self, cushion_sequence='N,D', bin_capacity=20):
        if int(bin_capacity) < 1:
            raise ValueError('bin_capacity는 1 이상이어야 합니다')
        self.seq = parse_sequence(cushion_sequence)
        self.bin_capacity = int(bin_capacity)
        self.bin = self.bin_capacity      # 적재함 방석 수 → /bin/count
        self.no = 0                       # 마지막으로 꺼낸 방석 번호 (C001부터, 중복 없음)
        self.weights = {}                 # 방석 id → kg

        self.main = []                    # 메인 라인 방석 (투입 순)
        self.lanes = {'ok': [], 'ng': []}  # 레인 방석, 맨 앞이 집는 자리
        self.diverter = {}                # 방석 id → lane (/diverter_1_cmd)
        self.pallet_status = {1: 'READY', 2: 'READY'}

    # ── 로봇팔1: 적재함 → 투입 위치 ─────────────────────────
    def pick_from_bin(self):
        """방석 하나를 꺼내 메인 라인에 올린다. 돌려주는 값: (방석 id, [], warnings)."""
        warnings = []
        if self.bin <= 0:
            self.bin = self.bin_capacity
            warnings.append(f'적재함이 비어 있어 {self.bin_capacity}개로 다시 채움')
        self.bin -= 1
        self.no += 1
        cushion = f'C{self.no:03d}'
        self.weights[cushion] = WEIGHT_KG[self.seq[(self.no - 1) % len(self.seq)]]
        self.main.append(cushion)
        return cushion, [], warnings

    # ── 메인 라인 ───────────────────────────────────────────
    def pass_scale(self, cushion):
        """무게 센서 통과 → /scale_1_weight 1회 (FR-06)."""
        # TODO(FR-06)
        raise NotImplementedError

    def on_diverter(self, cushion, lane):
        """/diverter_1_cmd 수신. 방석 id별로 기억한다 (FR-08)."""
        # TODO(FR-08): 같은 id를 다시 받으면 처음 명령 유지
        raise NotImplementedError

    def reach_diverter(self, cushion):
        """갈림길 도착. 명령대로 레인에 넣고, 명령이 없으면 ng + 경고 (FR-08)."""
        # TODO(FR-08)
        raise NotImplementedError

    def load(self):
        """/conveyor_1_load 값 {"main", "ok", "ng"} (FR-04). 그리퍼에 든 방석은 세지 않는다."""
        return {'main': len(self.main), 'ok': len(self.lanes['ok']), 'ng': len(self.lanes['ng'])}

    # ── 로봇팔2·3: 집는 자리 → 파레트 ───────────────────────
    def pick_from_lane(self, lane, cushion=''):
        """집는 자리의 방석을 꺼낸다. 다음 방석이 집는 자리에 오면 /conveyor_1_end_<lane> (FR-09~11)."""
        # TODO(FR-09~11)
        raise NotImplementedError

    # ── 파레트 자리 · AGV1 ──────────────────────────────────
    def on_agv_goal(self, goal):
        """/agv_1_goal {"wp"} 또는 {"hitch"} 수신. 이동·연결·분리 결과를 /agv_1_status로 (FR-13)."""
        # TODO(FR-13, 15): 도킹 위치에서 attach → 그 파레트 자리 OUT → REFILLING → READY
        raise NotImplementedError

    def reset(self):
        """E_STOP 해제 초기화 (FR-16 5번). 적재함 남은 수만 유지한다."""
        self.main = []
        self.lanes = {'ok': [], 'ng': []}
        self.diverter = {}
        self.pallet_status = {1: 'READY', 2: 'READY'}
