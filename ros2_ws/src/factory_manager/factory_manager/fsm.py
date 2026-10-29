"""공정 상태 규칙 (SRS 부록 A.2). rclpy 없이 순수 Python → pytest로 바로 시험.

메서드는 상태가 바뀌면 True를 돌려준다. 장비 명령은 노드가 정한다.
지금은 IDLE → RUNNING과 E_STOP만. 나머지 화살표(PALLET_FULL, ERROR)는 5.2~5.7에서 더한다.
"""

IDLE, RUNNING, PALLET_FULL, ERROR, E_STOP = 'IDLE', 'RUNNING', 'PALLET_FULL', 'ERROR', 'E_STOP'


class Fsm:

    def __init__(self, pallet_capacity=12):
        self.state = IDLE
        self.prev = None  # ERROR 전 상태 (5.7에서 씀)
        self.fault = None
        self.pallets = {1: 0, 2: 0}  # 1 = 불량 팔레트(factory_manager가 셈), 2 = 출고 팔레트(/pallet_2_count)
        self.capacity = pallet_capacity

    def full(self):
        """어느 팔레트든 12개면 가득 (FR-14)."""
        return any(n >= self.capacity for n in self.pallets.values())

    def _go(self, new):
        if new == self.state:
            return False
        self.state = new
        return True

    def on_start(self):
        """[동작] (FR-01). IDLE에서만 받는다."""
        if self.state != IDLE:
            return False
        return self._go(PALLET_FULL if self.full() else RUNNING)

    def on_estop(self, pressed):
        """비상정지 (FR-16). 해제하면 팔레트가 가득이면 PALLET_FULL, 아니면 IDLE. 적재 수는 지우지 않는다."""
        if pressed:
            return self._go(E_STOP)
        if self.state != E_STOP:
            return False
        return self._go(PALLET_FULL if self.full() else IDLE)
