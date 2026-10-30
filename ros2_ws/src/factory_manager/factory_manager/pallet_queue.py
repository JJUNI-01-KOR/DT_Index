"""파레트 적재 수와 운반 대기열 (SRS v3.1 FR-12, FR-15). rclpy 없이 순수 Python → pytest로 바로 시험.

적재 수와 대기열은 factory_manager만 바꾼다. palletizer는 /pallet_N_count를 받아 쓰기만 한다.
"""

READY, OUT, REFILLING = 'READY', 'OUT', 'REFILLING'


class PalletBook:

    def __init__(self, capacity=5):
        self.capacity = capacity
        self.counts = {1: 0, 2: 0}            # 파레트별 적재 수
        self.status = {1: None, 2: None}      # 마지막 /pallet_N_status
        self.queue = []                       # 운반 대기열, 먼저 찬 파레트가 앞

    def on_placed(self, pallet):
        """/pallet_N_placed 수신. 적재 수 +1, 적재 기준에 닿는 순간 한 번만 대기열 끝에 넣는다.
        대기열에 넣었으면 True. 이미 적재 기준이면 바꾸지 않는다 (경고는 노드가 남김)."""
        # TODO(FR-12)
        raise NotImplementedError

    def on_status(self, pallet, status):
        """/pallet_N_status 수신. READY가 아닌 값에서 READY로 바뀌면 적재 수 0, 0으로 되돌렸으면 True.
        1초마다 다시 오는 같은 READY로는 되돌리지 않는다."""
        # TODO(FR-12, 15)
        raise NotImplementedError

    def front_ready(self):
        """대기열 맨 앞 파레트가 READY이고 가득 차 있으면 그 번호, 아니면 None (FR-13 2번)."""
        # TODO(FR-13)
        raise NotImplementedError

    def pop_front(self):
        """대기열 맨 앞을 뺀다 (PN_DOCK을 보낼 때)."""
        return self.queue.pop(0)

    def reset(self):
        """E_STOP 해제 때 초기화 (FR-16 6번)."""
        self.counts = {1: 0, 2: 0}
        self.queue = []
