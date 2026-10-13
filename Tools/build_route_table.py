"""Bake main-road distances between delivery points for FourDistrictPrototype.

Usage: python Tools/build_route_table.py <배달_체험판_3D.html>
Output: Docs/MapLayout/route_table.json (read by the Unity route table installer).

The source HTML is only parsed as data, never executed. Distances follow the prototype's
mdist(): shortest path over main-road nodes only, falling back to the full graph (alleys
included) when a point cannot be reached by main roads. Units are the prototype's metres.
"""
import heapq, json, re, sys
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

adjacency = data['adj']
alley = data.get('nodeAlley') or [0] * len(adjacency)


def dijkstra(start, main_only):
    dist = [float('inf')] * len(adjacency)
    dist[start] = 0.0
    queue = [(0.0, start)]
    while queue:
        d, u = heapq.heappop(queue)
        if d > dist[u]:
            continue
        for v, w in adjacency[u]:
            if main_only and alley[v]:
                continue
            nd = d + w
            if nd < dist[v]:
                dist[v] = nd
                heapq.heappush(queue, (nd, v))
    return dist


def main_distance(start):
    main = dijkstra(start, True)
    full = dijkstra(start, False) if any(alley) else main
    return [m if m < float('inf') else f for m, f in zip(main, full)]


stores, doors = data['stores'], data['doors']
store_rows = [main_distance(s['node']) for s in stores]
door_rows = [main_distance(d['node']) for d in doors]
zone_ids = sorted(data['zones'].keys())
payload = {
    'zoneIds': zone_ids,
    'zoneNames': [data['zoneName'][z] for z in zone_ids],
    'shopNames': [s['name'] for s in stores],
    'shopZones': [s['zone'] for s in stores],
    'doorZones': [d['zone'] for d in doors],
    # Row-major: shopToDoor[shop * doorCount + door], doorToDoor[from * doorCount + to].
    'shopToDoor': [round(row[d['node']], 1) for row in store_rows for d in doors],
    'doorToDoor': [round(row[d['node']], 1) for row in door_rows for d in doors],
}
if any(v == float('inf') for v in payload['shopToDoor'] + payload['doorToDoor']):
    raise ValueError('Unreachable delivery point in the road graph')
out = root / 'Docs' / 'MapLayout' / 'route_table.json'
out.write_text(json.dumps(payload, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
values = payload['shopToDoor']
print(json.dumps({'shops': len(stores), 'doors': len(doors), 'shopToDoorMin': min(values), 'shopToDoorMax': max(values)}))
