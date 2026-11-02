"""방석 적재 판단 (SRS v3.1 FR-10, FR-11, FR-18). rclpy 없이 순수 Python → pytest로 바로 시험. 담당 D

palletizer 노드 하나가 레인 하나를 맡는다.
  palletizer_1: lane=ok → 정상 레인 끝, 팔2, 파레트1
  palletizer_2: lane=ng → 불량 레인 끝, 팔3, 파레트2
노드는 받은 토픽을 여기에 넘기고, 여기서 돌려준 일(팔 호출, placed·fault 발행)만 한다.

적재 수는 직접 세지 않는다. factory_manager의 /pallet_N_count가 기준이다.
  - 놓을 높이 = 파레트 상면 + 적재 수 × 방석 높이 (적재 수 0 → 1단 = 상면)
  - 성공하면 count가 바뀐 것을 본 다음에 다음 방석을 부른다 (안 기다리면 같은 높이에 또 놓는다)
"""
from collections import deque

RUNNING, IDLE, E_STOP = 'RUNNING', 'IDLE', 'E_STOP'
READY = 'READY'

# 레인별 설정. 좌표는 dt_bringup/config/line_coords.yaml (pick_ok·pick_ng, pallet_1·pallet_2)
LANES = {
    'ok': {'arm': 2, 'pallet': 1, 'pick_name': 'pick_ok', 'topic': '/conveyor_1_end_ok'},
    'ng': {'arm': 3, 'pallet': 2, 'pick_name': 'pick_ng', 'topic': '/conveyor_1_end_ng'},
}

# 팔 응답을 처리한 결과 = 노드가 할 일
PLACED = 'placed'   # /pallet_N_placed 발행, count가 바뀌길 기다림
FAULT = 'fault'     # /factory/fault 발행, 요청은 들고 있음 (retry를 기다림)
HOLD = 'hold'       # E_STOP으로 중단. 고장 아님. 들고 있다가 해제(E_STOP → IDLE) 때 비움
BUSY = 'busy'       # 팔이 다른 일 중. 고장 아님. busy_retry_sec 뒤 같은 요청
DROP = 'drop'       # no_cushion. 다시 해도 실패하므로 그 id는 버림
STALE = 'stale'     # 이미 끝낸 요청(20초 지남·해제)의 늦은 응답. 무시


def lane_config(lane, coords):
    """lane(ok·ng)과 라인 좌표표(dict) → 팔·파레트 번호, 토픽, 집는 자리, 파레트 자리.
    pick·pallet은 (x, y, z, yaw). 없거나 lane이 틀리면 ValueError."""
    if lane not in LANES:
        raise ValueError(f"lane은 ok 또는 ng여야 함 (받은 값: {lane!r})")
    cfg = dict(LANES[lane])
    for key, name in (('pick', cfg['pick_name']), ('pallet_pose', f"pallet_{cfg['pallet']}")):
        v = coords.get(name)
        if not isinstance(v, dict):
            raise ValueError(f'라인 좌표표에 {name}이 없음')
        cfg[key] = (float(v['x']), float(v['y']), float(v['z']), float(v.get('yaw', 0.0)))
    return cfg


def place_z(pallet_top, level, height):
    """놓을 높이 (방석 바닥). level = 지금 적재 수 (0이면 1단)."""
    return pallet_top + level * height


class Call:
    """팔에 보낸 요청 하나. level = 보낼 때 적재 수."""

    def __init__(self, cid, level, seq, sent_at):
        self.cid = cid
        self.level = level
        self.seq = seq
        self.sent_at = sent_at


class Stacker:

    def __init__(self, capacity=5, busy_retry_sec=1.0, warn_every_sec=5.0, recent=20):
        self.capacity = capacity
        self.busy_retry_sec = busy_retry_sec
        self.warn_every_sec = warn_every_sec
        self.queue = []                     # 도착한 방석 id. 앞부터 부른다
        self.recent = deque(maxlen=recent)  # 방금 놓은 방석 id. 같은 id가 다시 오면 무시
        self.state = None                   # 마지막 /factory/state. None = 아직 못 받음
        self.status = None                  # 마지막 /pallet_N_status
        self.count = None                   # 마지막 /pallet_N_count
        self.inflight = None                # 응답을 기다리는 요청 (Call)
        self.held = None                    # 실패해서 들고 있는 요청 (Call)
        self.held_reason = None             # FAULT · HOLD · BUSY
        self.retry = False                  # /factory/retry를 받아 한 번 다시 보내기로 함
        self.retry_at = None                # BUSY면 이 시각부터 다시 보냄
        self.wait_level = None              # 놓은 뒤 count가 이 값에서 바뀌길 기다림
        self.wait_since = None
        self.warned_at = None
        self.seq = 0                        # 요청 번호. 늦게 온 응답을 가려낸다

    # ── 받은 값 ──────────────────────────────────────────────
    def on_arrived(self, cid):
        """레인 끝 1번 칸에 도착한 방석 id. 새로 기억했으면 True.
        대기열에 있거나, 호출 중이거나, 들고 있거나, 방금 놓은 id면 무시하고 False
        (Unity가 끊겼다 붙으면 집는 자리에 있는 방석 id를 다시 보낸다)."""
        if not cid or self.knows(cid):
            return False
        self.queue.append(cid)
        return True

    def knows(self, cid):
        return (cid in self.queue or cid in self.recent
                or (self.inflight is not None and self.inflight.cid == cid)
                or (self.held is not None and self.held.cid == cid))

    def on_count(self, count):
        """/pallet_N_count. 놓은 뒤 기다리던 값에서 바뀌었으면 True (다음 방석을 불러도 됨)."""
        self.count = count
        if self.wait_level is not None and count != self.wait_level:
            self._stop_waiting()
            return True
        return False

    def on_status(self, status):
        """/pallet_N_status READY · OUT · REFILLING."""
        self.status = status

    def on_state(self, state):
        """/factory/state. E_STOP → IDLE이면 모두 비우고 True (FR-16 해제 초기화).
        Unity가 해제할 때 레인 위 방석을 모두 지우므로 기억한 id도 버린다."""
        before, self.state = self.state, state
        if before == E_STOP and state == IDLE:
            self.reset()
            return True
        return False

    def on_retry(self):
        """/factory/retry가 내 팔. 들고 있는 요청이 있으면 RUNNING일 때 한 번 다시 보내기로 하고 True.
        들고 있는 요청이 없으면 무시하고 False."""
        if self.held is None:
            return False
        self.retry = True
        return True

    def reset(self):
        """해제 초기화. count·status도 비워서 새 값을 받을 때까지 부르지 않는다."""
        self.queue.clear()
        self.recent.clear()
        self.inflight = None    # 응답이 늦게 와도 seq가 안 맞아 무시된다
        self._release()
        self._stop_waiting()
        self.count = None
        self.status = None

    # ── 부르기 ───────────────────────────────────────────────
    def blocked(self, now):
        """지금 팔을 부를 수 없는 이유. 부를 수 있으면 None."""
        if self.state is None or self.status is None or self.count is None:
            return '공정 상태·파레트 상태·적재 수를 아직 못 받음'
        if self.state != RUNNING:
            return f'공정 {self.state}'
        if self.inflight is not None:
            return '팔 호출 중'
        if self.wait_level is not None:
            return f'적재 수 {self.wait_level} → {self.wait_level + 1} 확인 기다림'
        if self.status != READY:
            return f'파레트 {self.status}'
        if self.count >= self.capacity:
            return f'파레트 가득 ({self.count}/{self.capacity}), 방석은 기억만 함'
        if self.held is not None:
            if self.retry:
                return None
            if self.held_reason == BUSY:
                return None if now >= self.retry_at else '팔 busy, 잠시 뒤 다시'
            if self.held_reason == HOLD:
                return 'E_STOP으로 멈춘 요청을 들고 있음 (해제 기다림)'
            return '실패한 요청을 들고 있음 (조치 완료 → retry 기다림)'
        if not self.queue:
            return '부를 방석 없음'
        return None

    def next_call(self, now):
        """부를 수 있으면 보낼 요청(Call)을 돌려주고 호출 중으로 바꾼다. 아니면 None.
        들고 있는 요청이 있으면 그것(같은 방석, 같은 단)을, 없으면 대기열 맨 앞 방석을 보낸다."""
        if self.blocked(now) is not None:
            return None
        if self.held is not None:
            cid = self.held.cid
            self._release()
        else:
            cid = self.queue.pop(0)
        self.seq += 1
        self.inflight = Call(cid, self.count, self.seq, now)
        return self.inflight

    def on_response(self, seq, success, message, now):
        """팔 응답. (결과, 방석 id)를 돌려준다. 결과는 PLACED · FAULT · HOLD · BUSY · DROP · STALE."""
        call = self.inflight
        if call is None or call.seq != seq:
            # 20초가 지나 포기한 요청이 늦게 성공했으면 방석은 이미 파레트 위에 있다 → 놓은 것으로 친다
            if success and self.held is not None and self.held.seq == seq:
                late = self.held
                self._release()
                return self._placed(late, now)
            return STALE, message
        self.inflight = None
        if success:
            return self._placed(call, now)
        if message == 'E_STOP':
            self._hold(call, HOLD)
            return HOLD, call.cid
        if message == 'busy':
            self._hold(call, BUSY)
            self.retry_at = now + self.busy_retry_sec
            return BUSY, call.cid
        if message == 'no_cushion':
            return DROP, call.cid
        self._hold(call, FAULT)     # timeout · ik_failed:<단계> · 그 밖의 실패
        return FAULT, call.cid

    def check_timeout(self, now, timeout_sec):
        """응답이 timeout_sec 넘게 없으면 그 요청을 실패로 들고 돌려준다 (fault 발행은 노드). 아니면 None.
        E_STOP 중에는 보지 않는다 (해제 때 비운다)."""
        call = self.inflight
        if call is None or self.state == E_STOP or now - call.sent_at < timeout_sec:
            return None
        self.inflight = None
        self._hold(call, FAULT)
        return call

    def wait_warning(self, now):
        """count 확인을 warn_every_sec 넘게 기다리면 기다린 시간(초). warn_every_sec마다 한 번. 아니면 None."""
        if self.wait_level is None:
            return None
        last = self.warned_at if self.warned_at is not None else self.wait_since
        if now - last < self.warn_every_sec:
            return None
        self.warned_at = now
        return now - self.wait_since

    # ── 내부 ─────────────────────────────────────────────────
    def _placed(self, call, now):
        self.recent.append(call.cid)
        self.wait_level = call.level
        self.wait_since = now
        self.warned_at = None
        return PLACED, call.cid

    def _hold(self, call, reason):
        self.held = call
        self.held_reason = reason
        self.retry = False
        self.retry_at = None

    def _release(self):
        self.held = None
        self.held_reason = None
        self.retry = False
        self.retry_at = None

    def _stop_waiting(self):
        self.wait_level = None
        self.wait_since = None
        self.warned_at = None
