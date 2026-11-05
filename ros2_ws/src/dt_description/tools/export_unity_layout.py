#!/usr/bin/env python3
"""Unity Main.unity → config/factory_layout.yaml (RViz 공장 배치). 담당 D

RViz 공장 배치(factory_layout_markers.py)가 읽는다. Main 씬이나 공장 프리팹을 고치면 다시 실행한다.
ROS가 없어도 python3만 있으면 된다 (Unity YAML을 직접 읽음, 표준 라이브러리만 씀).

    python3 tools/export_unity_layout.py ../../../unity/INDEX_Twin/Assets/_Project/Scenes/Main.unity config/factory_layout.yaml

뽑는 것
  conveyor_cells  "Conveyor" 아래 컨베이어 프리팹 칸(1 m 모듈)마다 위치·방향
  environment     씬과 공장 프리팹(FactoryShell, FinalZoneDecor)의 기본 도형(Cube, Cylinder) 위치·회전·크기·색
                  - 바닥 구역, 경계선, 선반, 볼라드, 받침대, 공장 벽 아랫단, 소품, 바닥 표시(AGV 길 포함)
                  - 꺼진 오브젝트·꺼진 렌더러, 3.2 m보다 높은 것(벽 위쪽·지붕·간판), 아주 작은 것, 넓은 바닥판은 뺀다
  로봇팔·AGV·방석·파레트는 뽑지 않는다 (URDF, line_coords.yaml, 실시간 TF가 맡음)
좌표 변환은 CoordConverter와 같다: ROS x = Unity z, ROS y = -Unity x, ROS z = Unity y
회전 (x, y, z, w)_ROS = (z, -x, y, -w)_Unity, 크기 (x, y, z)_ROS = (z, x, y)_Unity
"""
import math
import os
import re
import sys

CUBE, CYLINDER = '10202', '10206'
PREFAB_INCLUDE = ('FactoryShell.prefab', 'FinalZoneDecor.prefab')   # 안까지 읽는 공장 프리팹
MAX_TOP = 3.2          # m. 이보다 높이 올라가는 도형은 뺀다 (벽 위쪽, 기둥, 지붕, 높은 간판) → 위에서 내려다볼 때 가리지 않게
MIN_SIZE = 0.06        # m. 가장 긴 변이 이보다 짧으면 뺀다
MAX_AREA = 400.0       # m². 이보다 넓은 바닥판은 뺀다 (Ground 160 m × 160 m 등)


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


def gid(b, key):
    m = re.search(key + r': \{fileID: (-?\d+)', b)
    return int(m.group(1)) if m else 0


class Doc:
    """Unity 씬 또는 프리팹 파일 하나."""

    def __init__(self, path):
        self.objs = load_scene(path)
        self.name, self.active, self.tf, self.mesh, self.rend = {}, {}, {}, {}, {}
        self.stripped, self.stripped_src, self.prefab, self.rend_go = {}, {}, {}, {}
        for fid, (typ, b, st) in self.objs.items():
            if typ == 'GameObject' and not st:
                self.name[fid] = field(b, 'm_Name')
                self.active[fid] = field(b, 'm_IsActive') != '0'
            elif typ in ('Transform', 'RectTransform') and not st:
                kids = []
                if 'm_Children:' in b:
                    kids = [int(x) for x in re.findall(r'- \{fileID: (-?\d+)\}', b.split('m_Children:')[1].split('m_Father')[0])]
                self.tf[fid] = dict(go=gid(b, 'm_GameObject'), parent=gid(b, 'm_Father'),
                                    pos=nums(field(b, 'm_LocalPosition')), rot=nums(field(b, 'm_LocalRotation')),
                                    scale=nums(field(b, 'm_LocalScale')), kids=kids)
            elif typ == 'Transform' and st:
                self.stripped[fid] = gid(b, 'm_PrefabInstance')
                self.stripped_src[fid] = gid(b, 'm_CorrespondingSourceObject')   # 원본 프리팹 안의 transform id
            elif typ == 'MeshFilter' and not st:
                m = re.search(r'm_Mesh: \{fileID: (\d+), guid: 0000000000000000e000000000000000', b)
                if m:
                    self.mesh[gid(b, 'm_GameObject')] = m.group(1)
            elif typ == 'MeshRenderer' and not st:
                mats = []
                if 'm_Materials:' in b:
                    mats = re.findall(r'guid: (\w+), type: 2', b.split('m_Materials:')[1].split('m_StaticBatchInfo')[0])
                self.rend[gid(b, 'm_GameObject')] = (field(b, 'm_Enabled') != '0', mats)
                self.rend_go[fid] = gid(b, 'm_GameObject')
            elif typ == 'PrefabInstance':
                # 덮어쓴 값은 대상(원본 프리팹 안의 오브젝트 id)별로 모은다. 루트와 자식의 값을 섞지 않게
                mods = {}
                for tgt, k, v in re.findall(r'- target: \{fileID: (-?\d+)[^\n]*\n\s+propertyPath: (\S+)\n\s+value: ?(.*)', b):
                    mods.setdefault(int(tgt), {})[k] = v.strip()
                src = re.search(r'm_SourcePrefab: \{fileID: \d+, guid: (\w+)', b)
                name = next((m['m_Name'] for m in mods.values() if 'm_Name' in m), '?')
                self.prefab[fid] = dict(name=name, source=src.group(1) if src else '',
                                        parent=gid(b, 'm_TransformParent'), mods=mods)

    def local(self, t):
        if t in self.tf:
            n = self.tf[t]
            return n['parent'], n['pos'], n['rot'], n['scale']
        pi = self.prefab[self.stripped[t]]
        return (pi['parent'],) + override(pi['mods'].get(self.stripped_src[t], {}), {}, {}, {})

    def apply_instance(self, mods):
        """씬의 프리팹 인스턴스가 이 프리팹 안 자식에 덮어쓴 위치·회전·크기·켜짐을 반영한다."""
        for tgt, m in mods.items():
            if tgt in self.tf:
                n = self.tf[tgt]
                n['pos'], n['rot'], n['scale'] = override(m, n['pos'], n['rot'], n['scale'])
            elif tgt in self.name and 'm_IsActive' in m:
                self.active[tgt] = m['m_IsActive'] != '0'
            elif tgt in self.rend_go and 'm_Enabled' in m:
                go = self.rend_go[tgt]
                self.rend[go] = (m['m_Enabled'] != '0', self.rend[go][1])


def override(m, pos, rot, scale):
    """덮어쓴 값 m(propertyPath → value)을 기본 위치·회전·크기에 얹는다."""
    f = lambda k, d: float(m[k]) if m.get(k, '') != '' else d
    return ({a: f('m_LocalPosition.' + a, pos.get(a, 0.0)) for a in 'xyz'},
            {a: f('m_LocalRotation.' + a, rot.get(a, 1.0 if a == 'w' else 0.0)) for a in 'xyzw'},
            {a: f('m_LocalScale.' + a, scale.get(a, 1.0)) for a in 'xyz'})


IDENT = ((0.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0), (1.0, 1.0, 1.0))


def compose(parent, lp, lr, ls):
    pp, pr, ps = parent
    rp = qrot(pr, (lp.get('x', 0) * ps[0], lp.get('y', 0) * ps[1], lp.get('z', 0) * ps[2]))
    return ((pp[0] + rp[0], pp[1] + rp[1], pp[2] + rp[2]),
            qmul(pr, (lr.get('x', 0), lr.get('y', 0), lr.get('z', 0), lr.get('w', 1))),
            (ps[0] * ls.get('x', 1), ps[1] * ls.get('y', 1), ps[2] * ls.get('z', 1)))


def world(doc, t, cache, root_override=None):
    """root_override = (프리팹 루트 transform id, 인스턴스의 월드 변환): 프리팹 안을 인스턴스 자리에 놓을 때."""
    if t == 0:
        return IDENT
    if t in cache:
        return cache[t]
    if root_override and t == root_override[0]:
        w = root_override[1]
    else:
        parent, lp, lr, ls = doc.local(t)
        w = compose(world(doc, parent, cache, root_override), lp, lr, ls)
    cache[t] = w
    return w


def project_assets(scene):
    p = os.path.abspath(scene)
    while p and os.path.basename(p) != 'Assets':
        p = os.path.dirname(p)
    return p


def asset_index(assets):
    """guid → 파일 경로 (.mat, .prefab)"""
    idx = {}
    for root, _, files in os.walk(assets):
        for f in files:
            if f.endswith(('.mat.meta', '.prefab.meta')):
                p = os.path.join(root, f)
                with open(p, encoding='utf-8', errors='ignore') as fh:
                    m = re.search(r'guid: (\w+)', fh.read(300))
                if m:
                    idx[m.group(1)] = p[:-5]
    return idx


def mat_color(path, cache):
    if path in cache:
        return cache[path]
    c = (0.6, 0.6, 0.62)
    try:
        txt = open(path, encoding='utf-8', errors='ignore').read()
        m = re.search(r'- _BaseColor: \{r: ([\d.eE-]+), g: ([\d.eE-]+), b: ([\d.eE-]+)', txt) or \
            re.search(r'- _Color: \{r: ([\d.eE-]+), g: ([\d.eE-]+), b: ([\d.eE-]+)', txt)
        if m:
            c = tuple(round(min(max(float(v), 0.0), 1.0), 3) for v in m.groups())
    except OSError:
        pass
    cache[path] = c
    return c


def shapes(doc, start, cache, group, idx, mcache, out, root_override=None):
    """start부터 내려가며 켜진 Cube·Cylinder를 out에 넣는다."""
    stack = [(start, group)]
    while stack:
        t, grp = stack.pop()
        if t not in doc.tf:
            continue
        go = doc.tf[t]['go']
        if not doc.active.get(go, True):
            continue
        nm = doc.name.get(go, '?')
        if nm in ('Ground', 'Roof'):
            continue
        kind = doc.mesh.get(go)
        en, mats = doc.rend.get(go, (False, []))
        if kind in (CUBE, CYLINDER) and en:
            (ux, uy, uz), (qx, qy, qz, qw), (sx, sy, sz) = world(doc, t, cache, root_override)
            sx, sy, sz = abs(sx), abs(sy), abs(sz)
            h = sy * (2 if kind == CYLINDER else 1)
            # 회전된 도형의 높이 범위 (y축 방향 성분으로 대략)
            top = uy + h / 2
            if top <= MAX_TOP and max(sx, h, sz) >= MIN_SIZE and sx * sz <= MAX_AREA:
                col = mat_color(idx[mats[0]], mcache) if mats and mats[0] in idx else (0.6, 0.6, 0.62)
                out.append(dict(g=grp, n=nm, t='cyl' if kind == CYLINDER else 'box',
                                p=(round(uz, 3), round(-ux, 3), round(uy, 3)),
                                q=(round(qz, 4), round(-qx, 4), round(qy, 4), round(-qw, 4)),
                                s=(round(sz, 3), round(sx, 3), round(h, 3)), c=col))
        top_level = grp if grp != '' else nm
        for k in doc.tf[t]['kids']:
            stack.append((k, top_level))


def main(scene, out):
    doc = Doc(scene)
    assets = project_assets(scene)
    idx = asset_index(assets) if assets else {}
    cache = {}

    # ── 컨베이어 칸 ──
    roots = [t for t, v in doc.tf.items() if doc.name.get(v['go']) == 'Conveyor']
    if not roots:
        sys.exit('Conveyor 오브젝트를 찾지 못함')
    cells, seen, stack = [], set(), [roots[0]]
    while stack:
        t = stack.pop()
        if t in doc.stripped:
            (ux, uy, uz), (qx, qy, qz, qw), _ = world(doc, t, cache)
            uyaw = math.degrees(math.atan2(2 * (qw * qy + qx * qz), 1 - 2 * (qy * qy + qx * qx)))
            key = (round(uz, 2), round(-ux, 2))
            if key not in seen:
                seen.add(key)
                cells.append((key[0], key[1], round(math.radians(-uyaw), 4) + 0.0, doc.prefab[doc.stripped[t]]['name']))
        elif t in doc.tf:
            stack.extend(doc.tf[t]['kids'])
    cells.sort()

    # ── 환경 도형: 씬 오브젝트 ──
    env, mcache = [], {}
    scene_roots = [t for t, v in doc.tf.items() if v['parent'] == 0]
    for t in scene_roots:
        shapes(doc, t, cache, '', idx, mcache, env)
    # ── 환경 도형: 공장 프리팹 인스턴스 ──
    for pid, pi in doc.prefab.items():
        path = idx.get(pi['source'], '')
        if not path.endswith(PREFAB_INCLUDE):
            continue
        pdoc = Doc(path)
        pdoc.apply_instance(pi['mods'])
        proot = [t for t, v in pdoc.tf.items() if v['parent'] == 0][0]
        # 인스턴스 자리 = 부모의 월드 변환 ∘ 루트에 덮어쓴 위치·회전·크기 (안 덮어쓴 값은 프리팹 루트 값)
        r = pdoc.tf[proot]
        inst = compose(world(doc, pi['parent'], cache), *override(pi['mods'].get(proot, {}), r['pos'], r['rot'], r['scale']))
        shapes(pdoc, proot, {}, pi['name'], idx, mcache, env, root_override=(proot, inst))
    env.sort(key=lambda e: (e['g'], e['p']))

    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        f.write('# factory_layout.yaml : Unity Main 씬에서 뽑은 공장 배치 (ROS 좌표, m · rad). 직접 고치지 않는다.\n')
        f.write('# 만든 방법: python3 tools/export_unity_layout.py <Main.unity> config/factory_layout.yaml\n')
        f.write('# conveyor_cells: 컨베이어 1 m 모듈 중심 (프리팹 이름은 Unity Hierarchy 이름)\n')
        f.write('# environment: 기본 도형. g 묶음(맨 위 오브젝트), n 이름, t box|cyl, p 중심, q 회전(x,y,z,w), s 크기(x,y,z), c 색(r,g,b)\n')
        f.write('conveyor_cell_size: 1.0\n')
        f.write('conveyor_belt_top: 0.5\n')
        f.write('conveyor_cells:\n')
        for x, y, yaw, n in cells:
            f.write(f'  - {{x: {x:.2f}, y: {y:.2f}, yaw: {yaw:.4f}}}   # {n}\n')
        f.write('environment:\n')
        for e in env:
            fmt = lambda v: '[' + ', '.join(f'{a:g}' for a in v) + ']'
            name = e['n'].replace('"', "'")
            f.write(f'  - {{g: "{e["g"]}", n: "{name}", t: {e["t"]}, p: {fmt(e["p"])}, q: {fmt(e["q"])}, '
                    f's: {fmt(e["s"])}, c: {fmt(e["c"])}}}\n')
    groups = {}
    for e in env:
        groups[e['g']] = groups.get(e['g'], 0) + 1
    print(f'{len(cells)} cells, {len(env)} shapes {groups} → {out}')


if __name__ == '__main__':
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
