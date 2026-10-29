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
