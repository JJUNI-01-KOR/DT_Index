"""라인 규칙 (SRS 부록 A.3). rclpy 없이 순수 Python → pytest로 바로 시험."""


class FeedGate:
    """로봇팔1 투입 허용 (FR-04). 4개 이상이면 닫고, 3개 이하가 되면 연다."""

    def __init__(self, stop_count=4, resume_count=3):
        self.stop = stop_count
        self.resume = resume_count
        self.paused = False

    def update(self, on_line):
        """라인 위 박스 수를 받아 투입해도 되면 True."""
        if on_line >= self.stop:
            self.paused = True
        elif on_line <= self.resume:
            self.paused = False
        return not self.paused


class SlotWait:
    """칸 가득 대기 (FR-10). 칸이 차서 박스가 기다리는 쪽이 있으면 컨베이어를 멈춘다."""

    WAIT = {'reject_wait': 'reject', 'end_wait': 'end'}
    ENTER = {'reject': 'reject', 'end_1': 'end', 'end_2': 'end'}

    def __init__(self):
        self.waiting = set()  # 기다리는 쪽: 'reject', 'end'

    def update(self, zone):
        """감지 이벤트 zone을 받아 기다리는 쪽을 고친다. 모르는 zone이면 False."""
        if zone in self.WAIT:
            self.waiting.add(self.WAIT[zone])
        elif zone in self.ENTER:
            self.waiting.discard(self.ENTER[zone])
        else:
            return False
        return True

    def blocked(self):
        """기다리는 박스가 있으면 True (컨베이어를 멈춰야 함)."""
        return bool(self.waiting)

    def reset(self):
        """비상정지 해제 때 칸 상태 초기화 (FR-16)."""
        self.waiting.clear()
