"""AGV1 이동 명령 (SRS v3.1 FR-13, 부록 A.6). rclpy 없이 순수 Python → pytest로 바로 시험.

factory_manager는 /agv_1_loaded(적재 여부)만 보고 명령을 고른다. 경로와 연결·분리는 agv_1_controller가 맡는다.
"""

HOME, FINAL = 'HOME', 'FINAL'


def dock(pallet):
    """파레트 번호 → 도킹 명령 (1 → P1_DOCK)."""
    return f'P{pallet}_DOCK'


class AgvDispatcher:

    def __init__(self):
        self.loaded = None        # 마지막 /agv_1_loaded. None = 아직 못 받음
        self.carrying = None      # 운반 중 파레트 번호. 없으면 None
        self.last_cmd = None      # 마지막으로 보낸 /agv_1_cmd
        self.dock_sent_at = None  # 마지막으로 도킹 명령을 보낸 시각. None = 아직 안 보냄
        self.last_cycle_sec = None  # 마지막으로 끝난 사이클의 소요시간. None = 아직 안 부름

    def try_dispatch(self, running, front_pallet, now=None):
        """출동 검사 (FR-13 2번). RUNNING, 운반 중 파레트 없음, 적재 없음, 맨 앞 파레트가 READY·가득이면
        PN_DOCK 명령을 돌려준다. 아니면 None.
        RUNNING이 될 때, 대기열·자리 상태·적재 여부가 바뀔 때, 1초마다 부른다."""
        if not running or self.carrying is not None or self.loaded is not False or front_pallet is None:
            return None
        self.carrying = front_pallet
        self.last_cmd = dock(front_pallet)
        self.dock_sent_at = now
        return self.last_cmd

    def on_loaded(self, loaded, running, front_pallet, now=None):
        """/agv_1_loaded 수신. false → true면 FINAL, true → false면 운반 중 파레트를 비우고
        다음 PN_DOCK 또는 HOME. 보낼 명령이 없으면 None (FR-13 4·6번)."""
        before = self.loaded
        self.loaded = loaded
        if self.carrying is None:
            return None
        if before is False and loaded is True:
            self.last_cmd = FINAL
            return FINAL
        if before is True and loaded is False: #분리 된 순간 
            if self.dock_sent_at is not None and now is not None:
                self.last_cycle_sec = now - self.dock_sent_at
                self.dock_sent_at = None
            self.carrying = None
            next_cmd = self.try_dispatch(running, front_pallet, now)
            if next_cmd is not None:
                return next_cmd
            self.last_cmd = HOME
            return HOME
        return None

    def reset(self):
        """E_STOP 해제 때 초기화 (FR-16 6번)."""
        self.loaded = None 
        self.carrying = None
        self.last_cmd = None
        self.dock_sent_at = None
        self.last_cycle_sec = None

