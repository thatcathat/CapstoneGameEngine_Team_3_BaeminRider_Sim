"""Author about 80 hazard slots on the four-district road graph for FourDistrictPrototype.

Usage: python Tools/build_hazard_slots.py <배달_체험판_3D.html>
Output: Docs/MapLayout/hazard_slots.json (read by the Unity hazard slot installer).

The trial build places 450 slots with a seeded random at runtime; the 10/12 meeting approved
about 80. This tool places 80 deterministic slots (seed 13) with the trial build's rules where
they matter: slots sit on plain road nodes away from shops/doors/home, objects vs triggers follow
the 10/13 classification, unlock days and per-day placement ranks follow the v5.2 values, and each
slot lists the shop->door calls whose route passes within 7 m (main-road and full shortest paths).
Every number is a test value. The HTML is parsed as data and never executed.
"""
import heapq, json, math, random, re, sys
from pathlib import Path

root = Path(__file__).resolve().parents[1]
source = Path(sys.argv[1]).read_text(encoding='utf-8')
data = None
for match in re.finditer(r'\b(?:const|let|var) D\s*=\s*', source):
    try:
        candidate = json.JSONDecoder().raw_decode(source[match.end():])[0]
        if isinstance(candidate, dict) and 'roads' in candidate and 'adj' in candidate:
            data = candidate
            break
    except ValueError:
        pass
if data is None:
    raise ValueError('No map D with a road graph')

random.seed(13)
N = data['nodes']
ADJ = data['adj']
ALLEY = data.get('nodeAlley') or [0] * len(N)
origin = N[data['home']['node']]


def unity(p):
    return [round(p[0] - origin[0], 3), round(origin[1] - p[1], 3)]


def dist(a, b):
    return math.hypot(a[0] - b[0], a[1] - b[1])


# ---- types (10/13 classification; unlock days = v5.2 values after its own 'unlock earlier' override)
TYPES = {
    'manhole': dict(obj=True, unlock=1, ranked=True),
    'melon': dict(obj=True, unlock=1, ranked=True),
    'door': dict(obj=True, unlock=1, ranked=True),
    'hydrant': dict(obj=True, unlock=1, ranked=False),
    'duck': dict(obj=False, unlock=1, ranked=False),
    'chicken': dict(obj=False, unlock=1, ranked=False),
    'tire': dict(obj=False, unlock=2, ranked=False),
    'theft': dict(obj=False, unlock=2, ranked=False),
}
MAIN_BAG = {'manhole': 5, 'melon': 2, 'door': 4, 'hydrant': 6, 'tire': 10, 'duck': 12, 'chicken': 7, 'theft': 10}
ALLEY_BAG = {'manhole': 9, 'melon': 8, 'door': 2, 'chicken': 5}
# Combo templates: (name, alley?, count, members[(type, node steps along the road, side)])
COMBOS = [
    ('맨홀 슬라롬', False, 2, [('manhole', 0, 0), ('melon', 5, 1), ('duck', 10, 0), ('chicken', 15, -1)]),
    ('문→맞은편 맨홀', False, 2, [('door', 0, 1), ('manhole', 2, 0), ('hydrant', 4, -1)]),
    ('골목 연타: 맨홀 징검다리', True, 3, [('manhole', 0, 0), ('manhole', 14, 0)]),
    ('골목 연타: 수박 미끄럼→맨홀', True, 3, [('melon', 0, 1), ('manhole', 7, 0)]),
]
SPACING = 14.0      # metres between slots
KEEP_OUT = 12.0     # metres from shops, doors and home
ROUTE_R = 7.0       # trial build T.ROUTE_R

# ---- zone lookup (point in polygon, HTML coordinates)
def zone_of(p):
    for z, info in data['zones'].items():
        poly, inside = info['poly'], False
        j = len(poly) - 1
        for i in range(len(poly)):
            xi, yi = poly[i]; xj, yj = poly[j]
            if (yi > p[1]) != (yj > p[1]) and p[0] < (xj - xi) * (p[1] - yi) / ((yj - yi) or 1e-9) + xi:
                inside = not inside
            j = i
        if inside:
            return z
    return min(data['zones'], key=lambda z: min(dist(p, q) for q in data['zones'][z]['poly']))


# ---- road width per node (nearest named road segment)
segments = []
for r in data['roads']:
    for a, b in zip(r['pts'], r['pts'][1:]):
        segments.append((a, b, r['w'] / 2, r['id']))


def seg_distance(p, a, b):
    ax, ay = b[0] - a[0], b[1] - a[1]
    t = max(0.0, min(1.0, ((p[0] - a[0]) * ax + (p[1] - a[1]) * ay) / ((ax * ax + ay * ay) or 1e-9)))
    return dist(p, (a[0] + ax * t, a[1] + ay * t))


def road_at(p):
    return min(segments, key=lambda s: seg_distance(p, s[0], s[1]))


keep = [N[o['node']] for o in data['stores'] + data['doors'] + [data['home']]]
candidates = [i for i in range(len(N)) if len(ADJ[i]) == 2 and all(dist(N[i], k) >= KEEP_OUT for k in keep)]
used = []


def free(i):
    return all(dist(N[i], N[u]) >= SPACING for u in used)


def walk(i, steps):
    """Follow the road chain from node i; None if it leaves a plain stretch."""
    prev, cur = -1, i
    for _ in range(steps):
        nxt = [v for v, _ in ADJ[cur] if v != prev]
        if len(ADJ[cur]) != 2 or not nxt:
            return None
        prev, cur = cur, nxt[0]
    if len(ADJ[cur]) != 2 or ALLEY[cur] != ALLEY[i] or any(dist(N[cur], k) < KEEP_OUT for k in keep):
        return None
    return cur


slots = []


def add_slot(t, node, side, combo):
    a = N[ADJ[node][0][0]]
    b = N[ADJ[node][1][0]]
    direction = (b[0] - a[0], b[1] - a[1])
    length = math.hypot(*direction) or 1
    seg = road_at(N[node])
    slots.append(dict(type=t, node=node, side=side, combo=combo, alley=bool(ALLEY[node]),
                      zone=zone_of(N[node]), dir=(direction[0] / length, direction[1] / length), hw=seg[2], road=seg[3]))
    used.append(node)


bags = {False: dict(MAIN_BAG), True: dict(ALLEY_BAG)}
combo_id = 0
for name, alley, count, members in COMBOS:
    for _ in range(count):
        pool = [i for i in candidates if bool(ALLEY[i]) == alley and free(i)]
        random.shuffle(pool)
        for start in pool:
            nodes = [walk(start, m[1]) for m in members]
            if None in nodes or len(set(nodes)) != len(nodes):
                continue
            # Members are spaced along one road; only the first one keeps the global spacing rule.
            if not all(free(n) or n in nodes for n in nodes):
                continue
            for (t, _, side), n in zip(members, nodes):
                add_slot(t, n, side, combo_id)
                bags[alley][t] -= 1
            combo_id += 1
            break

# Remaining slots: balance zones by filling the zone with the fewest slots of that road kind first.
for alley in (False, True):
    bag = [t for t, c in bags[alley].items() for _ in range(max(0, c))]
    random.shuffle(bag)
    for t in bag:
        pool = [i for i in candidates if bool(ALLEY[i]) == alley and free(i)]
        if not pool:
            raise ValueError('Ran out of room for slots')
        counts = {z: sum(1 for s in slots if s['zone'] == z and s['alley'] == alley) for z in data['zones']}
        target = min(counts, key=counts.get)
        zoned = [i for i in pool if zone_of(N[i]) == target] or pool
        side = 0 if t in ('manhole', 'tire', 'duck', 'theft') else random.choice((-1, 1))
        add_slot(t, random.choice(zoned), side, -1)

# ---- routes: union of the full and main-road shortest paths for every shop->door call
def dijkstra(start, main_only):
    d = [float('inf')] * len(N); prev = [-1] * len(N); d[start] = 0.0
    q = [(0.0, start)]
    while q:
        du, u = heapq.heappop(q)
        if du > d[u]:
            continue
        for v, w in ADJ[u]:
            if main_only and ALLEY[v]:
                continue
            if du + w < d[v]:
                d[v] = du + w; prev[v] = u; heapq.heappush(q, (d[v], v))
    return prev


def path(prev, target):
    out = []
    while target >= 0:
        out.append(target); target = prev[target]
    return out


door_count = len(data['doors'])
for s in slots:
    s['routes'] = []
for si, store in enumerate(data['stores']):
    full, main = dijkstra(store['node'], False), dijkstra(store['node'], True)
    for di, door in enumerate(data['doors']):
        nodes = set(path(full, door['node'])) | set(path(main, door['node']))
        pts = [N[n] for n in nodes]
        for s in slots:
            p = N[s['node']]
            if any((q[0] - p[0]) ** 2 + (q[1] - p[1]) ** 2 < ROUTE_R * ROUTE_R for q in pts):
                s['routes'].append(si * door_count + di)

# ---- output in Unity coordinates (x right, z forward; the y flip mirrors the direction too)
out = []
for i, s in enumerate(slots):
    info = TYPES[s['type']]
    pos = unity(N[s['node']])
    out.append(dict(
        type=s['type'], isObject=info['obj'], unlockDay=info['unlock'],
        # Golden-ratio rank as in the trial build: ranked objects appear when rank < OBJ_DAY[day].
        rank=round((i * 0.6180339887) % 1, 4) if info['ranked'] else 0.0,
        zone=s['zone'], alley=s['alley'], road=s['road'], x=pos[0], z=pos[1],
        dirX=round(s['dir'][0], 4), dirZ=round(-s['dir'][1], 4), halfWidth=s['hw'], side=s['side'],
        combo=s['combo'], routes=s['routes']))
payload = {'slots': out}
(root / 'Docs' / 'MapLayout' / 'hazard_slots.json').write_text(json.dumps(payload, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
summary = {}
for s in out:
    key = s['type'] + (' (골목)' if s['alley'] else '')
    summary[key] = summary.get(key, 0) + 1
print(json.dumps({'slots': len(out), 'combos': combo_id, 'byType': summary,
                  'byZone': {z: sum(1 for s in out if s['zone'] == z) for z in sorted(data['zones'])},
                  'noRoute': sum(1 for s in out if not s['routes'])}, ensure_ascii=False))
