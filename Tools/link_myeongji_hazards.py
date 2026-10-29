"""Associate hazard spots with shop-to-door routes on the imported map."""
import json,heapq,math
from pathlib import Path
base=Path(__file__).resolve().parents[1]/'Docs/MapLayout/Myeongji'
graph=json.loads((base/'road_graph.json').read_text(encoding='utf-8-sig'))
anchors=json.loads((base/'anchors.json').read_text(encoding='utf-8-sig'))
bake=json.loads((base/'hazard_candidates.json').read_text(encoding='utf-8-sig'))
nodes=graph['nodes'];edges={i:[] for i in range(len(nodes))}
for a,b,cost in graph['edges']:edges[a].append((b,cost));edges[b].append((a,cost))
def tree(start):
 costs={start:0};previous={};q=[(0,start)]
 while q:
  cost,n=heapq.heappop(q)
  if cost>costs[n]:continue
  for dest,w in edges[n]:
   if cost+w<costs.get(dest,math.inf):costs[dest]=cost+w;previous[dest]=n;heapq.heappush(q,(cost+w,dest))
 return previous
def distance(p,a,b):
 dx,dy=b[0]-a[0],b[1]-a[1];d=dx*dx+dy*dy
 t=max(0,min(1,((p[0]-a[0])*dx+(p[1]-a[1])*dy)/d)) if d else 0
 return math.hypot(p[0]-a[0]-t*dx,p[1]-a[1]-t*dy)
shops=[a for a in anchors if a['type']=='shop'];doors=[a for a in anchors if a['type']=='door']
for spot in bake['spots']:spot['routes']=[]
covered=0
for si,shop in enumerate(shops):
 start=graph['anchorNodes'][shop['name']];previous=tree(start)
 for di,door in enumerate(doors):
  dest=graph['anchorNodes'][door['name']];path=[dest]
  while path[-1]!=start:path.append(previous[path[-1]])
  segments=list(zip(path,path[1:]));route=si*len(doors)+di;hit=False
  for spot in bake['spots']:
   p=(spot['position']['x'],spot['position']['z'])
   if any(distance(p,nodes[a],nodes[b])<=7 for a,b in segments):spot['routes'].append(route);hit=True
  covered+=hit
(base/'hazard_candidates.json').write_text(json.dumps(bake,ensure_ascii=False,indent=2),encoding='utf8')
summary={'spots':len(bake['spots']),'candidates':len(bake['candidates']),'coveredRoutes':covered,'totalRoutes':len(shops)*len(doors),'spotsWithoutRoutes':sum(not s['routes'] for s in bake['spots'])}
(base/'hazard_route_summary.json').write_text(json.dumps(summary,indent=2),encoding='utf8')
print(json.dumps(summary))
