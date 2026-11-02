#!/usr/bin/env python3
"""Unity Main.unity → config/factory_layout.yaml (컨베이어 칸 위치). 담당 D

RViz 공장 배치(factory_layout_markers.py)가 읽는다. Main 씬에서 컨베이어를 옮기면 다시 실행한다.
ROS가 없어도 python3만 있으면 된다 (Unity YAML을 직접 읽음, 표준 라이브러리만 씀).

    python3 tools/export_unity_layout.py ../../../unity/INDEX_Twin/Assets/_Project/Scenes/Main.unity config/factory_layout.yaml

뽑는 것: "Conveyor" 아래 프리팹 칸(1 m 모듈)마다 위치·방향. 좌표 변환은 CoordConverter와 같다.
    ROS x = Unity z, ROS y = -Unity x, ROS yaw = -Unity yaw
파레트 자리·팔·집는 자리 같은 나머지는 dt_bringup/config/line_coords.yaml(공식 좌표표)을 그대로 쓴다.
"""
import math
import re
import sys


def load_scene(path):
    text = open(path, encoding='utf-8').read()
    objs = {}
    for b in re.split(r'(?m)^--- !u!', text)[1:]:
        m = re.match(r'(\d+) &(-?\d+)( stripped)?\n(\w+):', b)
        if m:
            objs[int(m.group(2))] = (m.group(4), b, bool(m.group(3)))
    return objs


def nums(txt):
    return {k: float(v) for k, v in re.findall(r'(\w+): (-?[\d.eE+-]+)', txt)}


def field(b, name):
    m = re.search(r'(?m)^  ' + re.escape(name) + r': (.*)$', b)
    return m.group(1) if m else ''


def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz)


def qrot(q, v):
    x, y, z, w = q
    vx, vy, vz = v
    t = (2 * (y * vz - z * vy), 2 * (z * vx - x * vz), 2 * (x * vy - y * vx))
    return (vx + w * t[0] + y * t[2] - z * t[1], vy + w * t[1] + z * t[0] - x * t[2],
            vz + w * t[2] + x * t[1] - y * t[0])


def main(scene, out):
    objs = load_scene(scene)
    name, tf, prefab, stripped = {}, {}, {}, {}
    for fid, (typ, b, st) in objs.items():
        if typ == 'GameObject' and not st:
            name[fid] = field(b, 'm_Name')
        elif typ == 'Transform' and not st:
            tf[fid] = dict(go=int(re.search(r'm_GameObject: \{fileID: (-?\d+)\}', b).group(1)),
                           parent=int(re.search(r'm_Father: \{fileID: (-?\d+)\}', b).group(1)),
                           pos=nums(field(b, 'm_LocalPosition')), rot=nums(field(b, 'm_LocalRotation')),
                           scale=nums(field(b, 'm_LocalScale')),
                           kids=[int(x) for x in re.findall(r'- \{fileID: (-?\d+)\}', b.split('m_Children:')[1].split('m_Father')[0])])
        elif typ == 'Transform' and st:
            stripped[fid] = int(re.search(r'm_PrefabInstance: \{fileID: (-?\d+)\}', b).group(1))
        elif typ == 'PrefabInstance':
            mods = {}
            for k, v in re.findall(r'propertyPath: (\S+)\n\s+value: (.*)', b):
                mods.setdefault(k, v.strip())
            g = lambda k, d: float(mods.get(k) or d)
            prefab[fid] = dict(name=mods.get('m_Name', '?'),
                               parent=int(re.search(r'm_TransformParent: \{fileID: (-?\d+)\}', b).group(1)),
                               pos={a: g('m_LocalPosition.' + a, 0) for a in 'xyz'},
                               rot={a: g('m_LocalRotation.' + a, 1 if a == 'w' else 0) for a in 'xyzw'},
                               scale={a: g('m_LocalScale.' + a, 1) for a in 'xyz'})

    def world(t):
        if t == 0:
            return (0.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0), (1.0, 1.0, 1.0)
        n = tf[t] if t in tf else prefab[stripped[t]]
        pp, pr, ps = world(n['parent'])
        lp, lr, ls = n['pos'], n['rot'], n['scale']
        rp = qrot(pr, (lp['x'] * ps[0], lp['y'] * ps[1], lp['z'] * ps[2]))
        return ((pp[0] + rp[0], pp[1] + rp[1], pp[2] + rp[2]),
                qmul(pr, (lr['x'], lr['y'], lr['z'], lr['w'])),
                (ps[0] * ls['x'], ps[1] * ls['y'], ps[2] * ls['z']))

    roots = [t for t, v in tf.items() if name.get(v['go']) == 'Conveyor']
    if not roots:
        sys.exit('Conveyor 오브젝트를 찾지 못함')
    cells, seen, stack = [], set(), [roots[0]]
    while stack:
        t = stack.pop()
        if t in stripped:
            (ux, uy, uz), (qx, qy, qz, qw), _ = world(t)
            uyaw = math.degrees(math.atan2(2 * (qw * qy + qx * qz), 1 - 2 * (qy * qy + qx * qx)))
            key = (round(uz, 2), round(-ux, 2))
            if key not in seen:
                seen.add(key)
                cells.append((key[0], key[1], round(math.radians(-uyaw), 4) + 0.0, prefab[stripped[t]]['name']))
        elif t in tf:
            stack.extend(tf[t]['kids'])
    cells.sort()
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        f.write('# factory_layout.yaml : Unity Main 씬에서 뽑은 공장 배치 (ROS 좌표, m · rad). 직접 고치지 않는다.\n')
        f.write('# 만든 방법: python3 tools/export_unity_layout.py <Main.unity> config/factory_layout.yaml\n')
        f.write('# conveyor_cells: 컨베이어 1 m 모듈 중심 (프리팹 이름은 Unity Hierarchy 이름)\n')
        f.write('conveyor_cell_size: 1.0\n')
        f.write('conveyor_belt_top: 0.5\n')
        f.write('conveyor_cells:\n')
        for x, y, yaw, n in cells:
            f.write(f'  - {{x: {x:.2f}, y: {y:.2f}, yaw: {yaw:.4f}}}   # {n}\n')
    print(f'{len(cells)} cells → {out}')


if __name__ == '__main__':
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
