"""AGV1 경로 순서와 명령 번역 (SRS v3.1 FR-13, 부록 A.6). rclpy 없이 순수 Python → pytest로 바로 시험.

factory_manager의 /agv_1_cmd(HOME·P1_DOCK·P2_DOCK·FINAL)를 받아서, agv_waypoints.yaml의
경로점들을 거쳐 최종 정지 지점까지 가는 /agv_1_goal을 순서대로 만들어 낸다. 도킹 지점에
도착하면 연결(attach), FINAL에 도착하면 분리(detach)를 자동으로 붙이고, 그 결과로
/agv_1_loaded를 언제 True/False로 바꿔야 하는지도 판단한다.
"""

HOME, FINAL = 'HOME', 'FINAL'
DOCKS = ('P1_DOCK', 'P2_DOCK')

ROUTE_KEY = {
    'P1_DOCK': 'to_p1_dock',
    'P2_DOCK': 'to_p2_dock',
    'FINAL': 'to_final',
    'HOME': 'to_home',
}


class AgvRoute:
    """stops: {이름: {x,y,yaw}}, route: {to_xxx: [{x,y,yaw}, ...]} (agv_waypoints.yaml)."""

    def __init__(self, stops, route):
        self.stops = stops
        self.route = route
        self.loaded = False       # 마지막으로 보고한 /agv_1_loaded
        self.loaded_from = None   # 마지막으로 상차가 확인된 도킹 지점
        self.cmd = None           # 지금 수행 중인 /agv_1_cmd. 끝나면 None
        self.steps = []           # 남은 경로점들 (맨 끝이 최종 정지 지점)
        self.pending_hitch = None  # 경로 다 가면 보낼 hitch 명령 ('attach'/'detach'/None)

    def start(self, cmd):
        """/agv_1_cmd 수신. 적재 여부와 안 맞으면 (None, 경고문구), 맞으면 (goal, None)."""
        if cmd in DOCKS and self.loaded:
            return None, f'적재 중인데 {cmd} 받음 (무시)'
        if cmd == FINAL and not self.loaded:
            return None, 'FINAL 받았는데 적재 안 됨 (무시)'
        if cmd == HOME and self.loaded:
            return None, '적재 중인데 HOME 받음 (무시)'

        self.cmd = cmd
        via = self.route.get(ROUTE_KEY[cmd], [])
        # 이탈 좌표는 YAML에만 둔다. 기존 YAML은 공통 to_final로 동작한다.
        if cmd == FINAL and self.loaded_from in DOCKS:
            key = f'to_final_from_{self.loaded_from.lower()}'
            via = self.route.get(key, via)
        # Unity AgvLink는 좌표를 줘도 wp 이름이 없으면 무시한다. 경로점은 '<cmd>_via', 마지막은 cmd 이름 그대로.
        self.steps = [(f'{cmd}_via', p) for p in via] + [(cmd, self.stops[cmd])]
        self.pending_hitch = 'attach' if cmd in DOCKS else ('detach' if cmd == FINAL else None)
        return self._next_goal(), None

    def on_arrived(self):
        """/agv_1_status ARRIVED 수신. 다음 goal, hitch 명령, 또는 끝(None) 중 하나."""
        if self.steps:
            return self._next_goal()
        if self.pending_hitch:
            hitch, self.pending_hitch = self.pending_hitch, None
            return {'hitch': hitch}
        self.cmd = None
        return None

    def on_attached(self):
        """ATTACHED 수신. /agv_1_loaded에 발행할 값(True) 또는 None(이번 cmd와 무관)."""
        if self.cmd not in DOCKS:
            return None
        self.loaded = True
        self.loaded_from = self.cmd
        return True

    def on_detached(self):
        """DETACHED 수신. /agv_1_loaded에 발행할 값(False) 또는 None."""
        if self.cmd != FINAL:
            return None
        self.loaded = False
        self.loaded_from = None
        return False

    def on_estop_cleared(self):
        """/factory/state가 E_STOP → IDLE. 경로를 버리고 HOME·적재없음으로 되돌린다 (FR-16)."""
        self.cmd = None
        self.steps = []
        self.pending_hitch = None
        self.loaded = False
        self.loaded_from = None

    def _next_goal(self):
        name, step = self.steps.pop(0)
        return {'wp': name, 'x': step['x'], 'y': step['y'], 'yaw': step['yaw']}
