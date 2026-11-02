#!/usr/bin/env python3
"""factory_layout_markers : 공장 배치를 RViz 표시물로 낸다 (FR-13 확인·시연용). 담당 D · scope bringup

    발행: /factory_layout (visualization_msgs/MarkerArray, frame map, transient_local). 시작할 때 + 5초마다

읽는 파일 (좌표는 모두 ROS 좌표, m · rad)
    dt_description/config/factory_layout.yaml   컨베이어 칸 (Unity Main에서 tools/export_unity_layout.py로 뽑음)
    dt_bringup/config/line_coords.yaml          파레트 자리·예비 자리·최종 구역·집는 자리·팔 베이스·적재함·무게 센서·분기
    dt_bringup/config/agv_waypoints.yaml        AGV1 정지 지점(HOME, P1_DOCK, P2_DOCK, FINAL)과 경로점
RViz 글자는 한글 글꼴이 없을 수 있어 영어로 쓴다.
"""
import math
import os

import yaml

GREEN = (0.20, 0.75, 0.30, 1.0)
RED = (0.85, 0.25, 0.20, 1.0)
GRAY = (0.55, 0.55, 0.58, 1.0)
DARK = (0.30, 0.32, 0.36, 1.0)
BLUE = (0.25, 0.50, 0.95, 1.0)
ORANGE = (0.95, 0.60, 0.15, 1.0)
CYAN = (0.20, 0.85, 0.95, 1.0)
WOOD = (0.75, 0.58, 0.38, 1.0)
WHITE = (0.95, 0.95, 0.95, 1.0)


def load(path):
    with open(path, encoding='utf-8') as f:
        return yaml.safe_load(f) or {}


def box(ns, x, y, z, sx, sy, sz, color, yaw=0.0):
    return dict(ns=ns, type='CUBE', pose=(x, y, z, yaw), scale=(sx, sy, sz), color=color)


def text(ns, x, y, z, s, color=WHITE, size=0.35):
    return dict(ns=ns, type='TEXT', pose=(x, y, z, 0.0), scale=(0.0, 0.0, size), color=color, text=s)


def build(layout, coords, wp):
    """세 설정 파일 내용 → 표시물 목록 (rclpy 없이 시험할 수 있게 dict로 만든다)."""
    out = []
    # 컨베이어 (1 m 칸)
    size = float(layout.get('conveyor_cell_size', 1.0))
    top = float(layout.get('conveyor_belt_top', 0.5))
    for c in layout.get('conveyor_cells', []):
        out.append(box('conveyor', c['x'], c['y'], top / 2, size * 0.96, size * 0.96, top, DARK, c.get('yaw', 0.0)))

    def at(key):
        p = coords.get(key)
        return p if isinstance(p, dict) else None

    # 파레트 자리·예비 자리 (1 m 나무 파레트) — 정상 초록, 불량 빨강 테두리 판
    for key, label, color in (('pallet_1', 'PALLET 1 (OK)', GREEN), ('pallet_2', 'PALLET 2 (NG)', RED),
                              ('spare_1', 'SPARE 1', GRAY), ('spare_2', 'SPARE 2', GRAY)):
        p = at(key)
        if p:
            out.append(box('pallet_slot', p['x'], p['y'], 0.01, 1.2, 1.2, 0.02, color))
            out.append(box('pallet_slot', p['x'], p['y'], p['z'] / 2, 1.0, 1.0, p['z'], WOOD))
            out.append(text('label', p['x'], p['y'], 0.8, label))
    p = at('final_zone')
    if p:
        out.append(box('final_zone', p['x'], p['y'], 0.01, 2.0, 2.0, 0.02, BLUE))
        out.append(text('label', p['x'], p['y'], 0.8, 'FINAL ZONE'))

    # 레인 끝 집는 자리 (방석 0.4 × 0.4)
    for key, label, color in (('pick_ok', 'PICK OK', GREEN), ('pick_ng', 'PICK NG', RED)):
        p = at(key)
        if p:
            out.append(box('pick', p['x'], p['y'], p['z'] / 2 + 0.02, 0.4, 0.4, p['z'] + 0.04, color))
            out.append(text('label', p['x'], p['y'], 0.6, label, size=0.25))

    # 로봇팔 받침대
    for n in (1, 2, 3):
        p = at(f'arm_{n}_base')
        if p:
            h = max(p['z'], 0.05)
            out.append(dict(ns='arm', type='CYLINDER', pose=(p['x'], p['y'], h / 2, 0.0), scale=(0.35, 0.35, h), color=ORANGE))
            out.append(text('label', p['x'], p['y'], h + 0.9, f'ARM {n}'))

    # 적재함 (칸 20개의 가운데·범위), 무게 센서, 분기 게이트
    slots = coords.get('bin_slots') or []
    if slots:
        xs = [s['x'] for s in slots]
        ys = [s['y'] for s in slots]
        out.append(box('bin', sum(xs) / len(xs), sum(ys) / len(ys), 0.25,
                       max(xs) - min(xs) + 0.5, max(ys) - min(ys) + 0.5, 0.5, GRAY))
        out.append(text('label', sum(xs) / len(xs), sum(ys) / len(ys), 0.9, 'BIN'))
    for key, label in (('scale_1', 'SCALE'), ('diverter_1', 'DIVERTER')):
        p = at(key)
        if p:
            out.append(box('sensor', p['x'], p['y'], p['z'] + 0.25, 1.1, 0.08, 0.5, CYAN))
            out.append(text('label', p['x'], p['y'], p['z'] + 0.8, label, size=0.25))

    # AGV1 정지 지점 (화살표 = 도착 방향) + 경로
    stops = wp.get('stops') or {}
    for name, s in stops.items():
        out.append(dict(ns='agv_stop', type='ARROW', pose=(s['x'], s['y'], 0.03, s.get('yaw', 0.0)),
                        scale=(0.7, 0.12, 0.12), color=CYAN))
        out.append(text('label', s['x'], s['y'], 0.45, name, CYAN, 0.25))
    route = wp.get('route') or {}

    def path(start, mids, end):
        pts = [start] + list(mids or []) + [end]
        pts = [p for p in pts if p]
        if len(pts) >= 2:
            out.append(dict(ns='agv_route', type='LINE_STRIP', pose=(0.0, 0.0, 0.0, 0.0), scale=(0.04, 0.0, 0.0),
                            color=(0.20, 0.85, 0.95, 0.6), points=[(p['x'], p['y'], 0.02) for p in pts]))
    home, d1, d2, fin = (stops.get(k) for k in ('HOME', 'P1_DOCK', 'P2_DOCK', 'FINAL'))
    path(home, route.get('to_p1_dock'), d1)
    path(home, route.get('to_p2_dock'), d2)
    path(d1, route.get('to_final'), fin)
    path(d2, route.get('to_final'), fin)
    path(fin, route.get('to_home'), home)
    return out


def main():
    import rclpy
    from ament_index_python.packages import get_package_share_directory
    from builtin_interfaces.msg import Duration
    from geometry_msgs.msg import Point
    from rclpy.node import Node
    from rclpy.qos import DurabilityPolicy, QoSProfile
    from std_msgs.msg import ColorRGBA
    from visualization_msgs.msg import Marker, MarkerArray

    kinds = {'CUBE': Marker.CUBE, 'CYLINDER': Marker.CYLINDER, 'ARROW': Marker.ARROW,
             'TEXT': Marker.TEXT_VIEW_FACING, 'LINE_STRIP': Marker.LINE_STRIP}

    class LayoutMarkers(Node):
        def __init__(self):
            super().__init__('factory_layout_markers')
            own = os.path.join(get_package_share_directory('dt_description'), 'config')
            cfg = os.path.join(get_package_share_directory('dt_bringup'), 'config')
            self.declare_parameter('layout_file', os.path.join(own, 'factory_layout.yaml'))
            self.declare_parameter('coords_file', os.path.join(cfg, 'line_coords.yaml'))
            self.declare_parameter('waypoints_file', os.path.join(cfg, 'agv_waypoints.yaml'))
            files = [self.get_parameter(k).value for k in ('layout_file', 'coords_file', 'waypoints_file')]
            items = build(*(load(f) for f in files))
            self.msg = MarkerArray(markers=[self.to_marker(i, it) for i, it in enumerate(items)])
            qos = QoSProfile(depth=1, durability=DurabilityPolicy.TRANSIENT_LOCAL)
            self.pub = self.create_publisher(MarkerArray, '/factory_layout', qos)
            self.publish()
            self.create_timer(5.0, self.publish)
            self.get_logger().info(f'공장 배치 표시물 {len(items)}개 → /factory_layout')

        def to_marker(self, i, it):
            m = Marker()
            m.header.frame_id = 'map'
            m.ns, m.id, m.type, m.action = it['ns'], i, kinds[it['type']], Marker.ADD
            x, y, z, yaw = it['pose']
            m.pose.position.x, m.pose.position.y, m.pose.position.z = float(x), float(y), float(z)
            m.pose.orientation.z, m.pose.orientation.w = math.sin(yaw / 2), math.cos(yaw / 2)
            m.scale.x, m.scale.y, m.scale.z = (float(v) for v in it['scale'])
            m.color = ColorRGBA(r=float(it['color'][0]), g=float(it['color'][1]), b=float(it['color'][2]), a=float(it['color'][3]))
            m.text = it.get('text', '')
            m.points = [Point(x=float(px), y=float(py), z=float(pz)) for px, py, pz in it.get('points', [])]
            m.lifetime = Duration(sec=0)
            m.frame_locked = False
            return m

        def publish(self):
            now = self.get_clock().now().to_msg()
            for m in self.msg.markers:
                m.header.stamp = now
            self.pub.publish(self.msg)

    rclpy.init()
    node = LayoutMarkers()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    node.destroy_node()
    if rclpy.ok():
        rclpy.shutdown()


if __name__ == '__main__':
    main()
