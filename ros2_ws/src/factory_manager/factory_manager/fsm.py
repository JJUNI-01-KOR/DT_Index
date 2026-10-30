"""공정 상태 규칙 (SRS v3.1 부록 A.2). rclpy 없이 순수 Python → pytest로 바로 시험.

메서드는 상태가 바뀌면 True를 돌려준다. 장비 명령은 노드가 정한다.
지금은 시작(E_STOP), 해제, [시작], 비상정지·통신 끊김·감시 시간 E_STOP까지. ERROR와 고장 목록은 TODO.
"""

IDLE, RUNNING, ERROR, E_STOP = 'IDLE', 'RUNNING', 'ERROR', 'E_STOP'

# E_STOP 원인 (/factory/stats의 estop, SRS v3.1 부록 A.4)
CAUSE_BUTTON = 'button'                  # [비상정지] (FR-16)
CAUSE_HEARTBEAT = 'heartbeat'            # 통신 끊김 3초 (FR-17)
CAUSE_TIMEOUT_WEIGHT = 'timeout_weight'  # 무게 보고 감시 시간 15초 (FR-02)
CAUSE_TIMEOUT_AGV = 'timeout_agv'        # AGV1 감시 시간 30초 (FR-13)
CAUSE_STARTUP = 'startup'                # factory_manager 실행 (FR-17)


class Fsm:

    def __init__(self):
        self.state = E_STOP          # 실행하면 E_STOP으로 시작한다 (FR-17 5번)
        self.estop = CAUSE_STARTUP   # E_STOP 원인. E_STOP이 아니면 None
        self.faults = []             # 고장 목록, 먼저 난 순서 (FR-18). ERROR가 아니면 빈 목록

    def _go(self, new):
        if new == self.state:
            return False
        self.state = new
        return True

    def fault(self):
        """첫 고장 장비 (/factory/stats의 fault). 없으면 None."""
        return self.faults[0] if self.faults else None

    def on_start(self):
        """[시작] (FR-01). IDLE에서만 받는다."""
        if self.state != IDLE:
            return False
        return self._go(RUNNING)

    def on_estop(self, pressed):
        """[비상정지] 누름(True)·해제(False) (FR-16). 해제하면 IDLE, 고장 목록도 비운다."""
        if pressed:
            return self.to_estop(CAUSE_BUTTON)
        if self.state != E_STOP:
            return False
        self.estop = None
        self.faults = []
        return self._go(IDLE)

    def to_estop(self, cause):
        """어느 상태에서든 E_STOP으로 (비상정지, 통신 끊김, 감시 시간 초과). 이미 E_STOP이면 원인을 바꾸지 않는다."""
        if self.state == E_STOP:
            return False
        self.estop = cause
        return self._go(E_STOP)

    def on_fault(self, device):
        """집기 실패·고장 주입 (FR-18, 19). RUNNING → ERROR, ERROR 중이면 고장 목록 뒤에 추가."""
        # TODO(FR-18): RUNNING이면 faults=[device]로 ERROR, ERROR면 목록에 없을 때만 뒤에 추가
        return False

    def on_fault_clear(self, device):
        """3초 조치 완료 (FR-18). 목록 맨 앞 장비일 때만 빼고, 목록이 비면 RUNNING."""
        # TODO(FR-18): device != fault()면 무시. 비면 RUNNING으로 (재전송은 노드가 정함)
        return False
