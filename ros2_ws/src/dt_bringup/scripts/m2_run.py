#!/usr/bin/env python3
"""M2 리허설 보조 (저장소에 넣지 않음, D 작성).
palletizer(C)·agv_1_controller(C)가 없을 때 그 역할을 대신해 M2 흐름을 끝까지 돌린다.
  1) /factory/emergency_stop false → /factory/start true  (--no-start면 생략)
  2) 정상 레인 끝(/conveyor_1_end_ok) 방석 → /arm_2_pick_place → 파레트1
     불량 레인 끝(/conveyor_1_end_ng) 방석 → /arm_3_pick_place → 파레트2
  3) 두 파레트에 TARGET개씩 놓이면 팔 호출을 멈추고 AGV1을 /agv_1_goal로 직접 움직인다
     HOME → P1_DOCK → 연결 → FINAL → 분리 → P2_DOCK → 연결 → FINAL → 분리 → HOME
실행: python3 m2_run.py [--no-start] [--target 1]
"""
import json, sys, time
import rclpy
from rclpy.node import Node
from std_msgs.msg import Bool, String
from factory_interfaces.srv import PickPlace

TARGET = int(sys.argv[sys.argv.index('--target') + 1]) if '--target' in sys.argv else 1
PICK = {2: (-6.876, 15.5, 0.111), 3: (5.124, 15.5, 0.111)}
PALLET = {2: (-6.5, 18.0), 3: (5.4, 18.0)}          # 팔2 → 파레트1, 팔3 → 파레트2
SLOTS = [(-0.2, -0.2), (0.2, -0.2), (-0.2, 0.2), (0.2, 0.2)]
VIA = {'x': -0.75, 'y': 19.0, 'yaw': 0.0}
AGV_STEPS = [
    ('goal', dict(wp='to_p1_dock_0', **VIA), 'ARRIVED'), ('goal', {'wp': 'P1_DOCK'}, 'ARRIVED'),
    ('hitch', 'attach', 'ATTACHED'), ('goal', {'wp': 'FINAL'}, 'ARRIVED'), ('hitch', 'detach', 'DETACHED'),
    ('wait', 3.5, None),                                   # 최종 구역 파레트가 3초 뒤 사라질 때까지
    ('goal', dict(wp='to_p2_dock_0', **VIA), 'ARRIVED'), ('goal', {'wp': 'P2_DOCK'}, 'ARRIVED'),
    ('hitch', 'attach', 'ATTACHED'), ('goal', {'wp': 'FINAL'}, 'ARRIVED'), ('hitch', 'detach', 'DETACHED'),
    ('wait', 3.5, None), ('goal', {'wp': 'HOME'}, 'ARRIVED'),
]


class M2(Node):
    def __init__(self):
        super().__init__('m2_rehearsal')
        self.t0 = time.time()
        self.cli = {n: self.create_client(PickPlace, f'/arm_{n}_pick_place') for n in (2, 3)}
        self.queue = {2: [], 3: []}
        self.busy = {2: False, 3: False}
        self.placed = {2: 0, 3: 0}
        self.skip2 = '--skip-arm2' in sys.argv          # 팔2가 아직 씬에 없을 때: 파레트1은 빈 채로 운반
        if self.skip2:
            self.placed[2] = TARGET
        self.arms_on = True
        self.create_subscription(String, '/conveyor_1_end_ok', lambda m: self.on_end(2, m.data), 10)
        self.create_subscription(String, '/conveyor_1_end_ng', lambda m: self.on_end(3, m.data), 10)
        self.create_subscription(String, '/agv_1_status', self.on_agv, 10)
        self.create_subscription(String, '/factory/state', self.on_state, 10)
        self.goal_pub = self.create_publisher(String, '/agv_1_goal', 10)
        self.estop_pub = self.create_publisher(Bool, '/factory/emergency_stop', 10)
        self.start_pub = self.create_publisher(Bool, '/factory/start', 10)
        self.state = ''
        self.step = -1
        self.step_t = 0.0
        self.agv_last = None
        self.create_timer(0.2, self.tick)
        if '--no-start' not in sys.argv:
            self.boot = 0
            self.create_timer(1.0, self.boot_tick)
        self.log(f'시작: 파레트마다 {TARGET}개 놓이면 AGV 운반')

    def log(self, s):
        print(f'[{time.time() - self.t0:6.1f}s] {s}', flush=True)

    def boot_tick(self):
        if self.boot == 1:
            self.estop_pub.publish(Bool(data=False)); self.log('해제 보냄')
        elif self.boot == 3:
            self.start_pub.publish(Bool(data=True)); self.log('시작 보냄')
        self.boot += 1

    def on_state(self, m):
        if m.data != self.state:
            self.log(f'공정 상태 {self.state or "-"} → {m.data}')
            self.state = m.data

    def on_end(self, arm, cid):
        if not self.arms_on or (arm == 2 and self.skip2):
            return
        self.log(f'레인 끝 {"정상" if arm == 2 else "불량"}: {cid}')
        self.queue[arm].append(cid)

    def call_arm(self, arm):
        cid = self.queue[arm].pop(0)
        k = self.placed[arm]
        dx, dy = SLOTS[k % 4]
        px, py = PALLET[arm]
        req = PickPlace.Request()
        req.box_id = cid
        req.pick_pose.position.x, req.pick_pose.position.y, req.pick_pose.position.z = PICK[arm]
        req.place_pose.position.x = px + dx
        req.place_pose.position.y = py + dy
        req.place_pose.position.z = 0.1 + 0.1 * (k // 4)
        req.place_pose.orientation.w = 1.0
        self.busy[arm] = True
        t = time.time()
        self.log(f'팔{arm} 호출: {cid} → 파레트{1 if arm == 2 else 2} 칸{k % 4 + 1}')
        fut = self.cli[arm].call_async(req)

        def done(f):
            self.busy[arm] = False
            r = f.result()
            self.log(f'팔{arm} 응답 ({time.time() - t:.1f}초): success={r.success} message={r.message}')
            if r.success:
                self.placed[arm] += 1
            else:
                self.queue[arm].insert(0, cid) if r.message in ('busy',) else None
        fut.add_done_callback(done)

    def on_agv(self, m):
        try:
            self.agv_last = json.loads(m.data)
        except ValueError:
            return
        self.log(f'AGV1 {self.agv_last}')

    def tick(self):
        if self.arms_on:
            for arm in (2, 3):
                if not self.busy[arm] and self.queue[arm] and self.cli[arm].service_is_ready():
                    self.call_arm(arm)
            if self.placed[2] >= TARGET and self.placed[3] >= TARGET and not any(self.busy.values()):
                self.arms_on = False
                self.log(f'파레트1 {self.placed[2]}개, 파레트2 {self.placed[3]}개 → AGV 운반 시작')
                self.next_step()
            return
        if self.step < 0 or self.step >= len(AGV_STEPS):
            return
        kind, arg, expect = AGV_STEPS[self.step]
        if kind == 'wait':
            if time.time() - self.step_t >= arg:
                self.next_step()
            return
        if self.agv_last and self.agv_last.get('state') == expect and (kind == 'hitch' or self.agv_last.get('wp') == arg['wp']):
            self.next_step()
        elif time.time() - self.step_t > 45:
            self.log(f'AGV 단계 시간 초과: {AGV_STEPS[self.step]}')
            self.step = len(AGV_STEPS)

    def next_step(self):
        self.step += 1
        self.step_t = time.time()
        self.agv_last = None
        if self.step >= len(AGV_STEPS):
            self.log('M2 흐름 끝 (AGV1 HOME 도착)')
            return
        kind, arg, _ = AGV_STEPS[self.step]
        if kind == 'goal':
            self.goal_pub.publish(String(data=json.dumps(arg)))
            self.log(f'AGV goal: {arg}')
        elif kind == 'hitch':
            self.goal_pub.publish(String(data=json.dumps({'hitch': arg})))
            self.log(f'AGV hitch: {arg}')


def main():
    rclpy.init()
    node = M2()
    try:
        while rclpy.ok() and node.step < len(AGV_STEPS):
            rclpy.spin_once(node, timeout_sec=0.1)
        for _ in range(10):
            rclpy.spin_once(node, timeout_sec=0.1)
    except KeyboardInterrupt:
        pass
    node.destroy_node()
    rclpy.shutdown()


if __name__ == '__main__':
    main()
