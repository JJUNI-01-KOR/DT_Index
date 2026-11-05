"""감시 시간 (SRS v3.1 FR-02, FR-13, 부록 A.3). rclpy 없이 순수 Python → pytest로 바로 시험.

기다리는 보고가 감시 시간 안에 오지 않으면 E_STOP 원인을 돌려준다.
감시 시간은 RUNNING 동안만 세고, RUNNING으로 돌아오면 처음부터 다시 센다.
"""

from .fsm import CAUSE_TIMEOUT_AGV, CAUSE_TIMEOUT_WEIGHT  # noqa: F401  expired()가 돌려줄 값


class Watchdog:

    def __init__(self, weight_sec=15.0, agv_sec=30.0):
        self.weight_sec = weight_sec
        self.agv_sec = agv_sec
        self.weight_since = None   # 팔1 투입 성공 시각 (무게 보고 대기 중이면)
        self.agv_since = None      # PN_DOCK·FINAL을 보낸 시각 (적재 여부 변화 대기 중이면)

    def start_weight(self, now):
        """팔1 투입 성공 (FR-02)."""
        self.weight_since = now

    def stop_weight(self):
        """그 방석의 무게 보고 수신."""
        self.weight_since = None

    def start_agv(self, now):
        """PN_DOCK 또는 FINAL을 보냄 (FR-13)."""
        self.agv_since = now

    def stop_agv(self):
        """/agv_1_loaded가 기대한 값으로 바뀜."""
        self.agv_since = None

    def restart(self, now):
        """RUNNING으로 돌아옴. 기다리던 감시는 처음부터 다시 센다."""
        if self.weight_since is not None:
            self.weight_since = now
        if self.agv_since is not None:
            self.agv_since = now

    def restart_weight(self, now):
        """컨베이어가 다시 돌면 무게 감시만 처음부터 다시 센다."""
        if self.weight_since is not None:
            self.weight_since = now

    def expired(self, now, conveyor_on=True):
        """초과한 감시가 있으면 E_STOP 원인, 없으면 None. 무게 감시는 컨베이어가 돌 때만 본다."""
        if (conveyor_on and self.weight_since is not None
                and now - self.weight_since > self.weight_sec):
            return CAUSE_TIMEOUT_WEIGHT
        if self.agv_since is not None and now - self.agv_since > self.agv_sec:
            return CAUSE_TIMEOUT_AGV
        return None

    def reset(self):
        """E_STOP 해제 때 초기화 (FR-16 6번)."""
        self.weight_since = None
        self.agv_since = None
