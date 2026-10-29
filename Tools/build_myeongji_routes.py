"""Bake distances and a schematic map from the placed Myeongji anchors."""
import json, math, heapq
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
root=Path(__file__).resolve().parents[1]
directory=root/'Docs/MapLayout/Myeongji'
layout=json.loads((directory/'layout.json').read_text(encoding='utf-8-sig'))
anchors=json.loads((directory/'anchors.json').read_text(encoding='utf-8-sig'))
roads=layout['roads'];nodes=[];edges={};splits=[[] for _ in roads]
def dist(a,b):return math.hypot(a[0]-b[0],a[1]-b[1])
def node(p):
 for i,q in enumerate(nodes):
  if dist(p,q)<.015:return i
 nodes.append(tuple(p));edges[len(nodes)-1]={};return len(nodes)-1
def edge(i,j):
 if i==j:return
 w=dist(nodes[i],nodes[j]);edges[i][j]=min(edges[i].get(j,math.inf),w);edges[j][i]=min(edges[j].get(i,math.inf),w)
def project(p,r):
 a,b=r['a'],r['b'];dx=b[0]-a[0];dy=b[1]-a[1];t=max(0,min(1,((p[0]-a[0])*dx+(p[1]-a[1])*dy)/(dx*dx+dy*dy)))
 return (a[0]+dx*t,a[1]+dy*t)
def split(i,p):
 n=node(p);splits[i].append(n);return n
for i,r in enumerate(roads):
 split(i,r['a']);split(i,r['b'])
# Explicit near-end junctions bridge the small authored centreline gaps inside road widths.
for i,r in enumerate(roads):
 for p in [r['a'],r['b']]:
  for j,s in enumerate(roads):
   if i==j:continue
   q=project(p,s)
   if dist(p,q)<=s['width']*.5+.05:edge(split(i,p),split(j,q))
for i,r in enumerate(roads):
 for j,s in enumerate(roads[i+1:],i+1):
  a,b=r['a'],r['b'];c,d=s['a'],s['b'];u=(b[0]-a[0],b[1]-a[1]);v=(d[0]-c[0],d[1]-c[1]);den=u[0]*v[1]-u[1]*v[0]
  if abs(den)<1e-9:continue
  w=(c[0]-a[0],c[1]-a[1]);t=(w[0]*v[1]-w[1]*v[0])/den;q=(w[0]*u[1]-w[1]*u[0])/den
  if 0<=t<=1 and 0<=q<=1:
   p=(a[0]+t*u[0],a[1]+t*u[1]);split(i,p);split(j,p)
for a in anchors:
 p=(a['position'][0],a['position'][2]);q=(a['roadPoint'][0],a['roadPoint'][2]);i=next(i for i,r in enumerate(roads) if r['id']==a['road'])
 a['node']=node(p);edge(a['node'],split(i,q))
for i,r in enumerate(roads):
 ordered=sorted(set(splits[i]),key=lambda n:dist(nodes[n],r['a']))
 for a,b in zip(ordered,ordered[1:]):edge(a,b)
def distances(start):
 costs={start:0};queue=[(0,start)]
 while queue:
  cost,n=heapq.heappop(queue)
  if cost>costs[n]:continue
  for dest,w in edges[n].items():
   nc=cost+w
   if nc<costs.get(dest,math.inf):costs[dest]=nc;heapq.heappush(queue,(nc,dest))
 return costs
shops=[a for a in anchors if a['type']=='shop'];doors=[a for a in anchors if a['type']=='door']
allcosts={a['node']:distances(a['node']) for a in anchors}
if any(len(d)!=len(nodes) for d in allcosts.values()):raise RuntimeError('Disconnected road graph')
def matrix(left,right):return [round(allcosts[a['node']][b['node']],2) for a in left for b in right]
data={'zoneIds':list('ABCD'),'zoneNames':['A 정문 앞','B 북쪽 상가','C 남쪽 주택','D 동쪽 골목'],'shopNames':[a['shopName'] for a in shops],'shopZones':[a['zone'] for a in shops],'doorZones':[a['zone'] for a in doors],'shopToDoor':matrix(shops,doors),'doorToDoor':matrix(doors,doors)}
(directory/'route_table.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf8')
(directory/'road_graph.json').write_text(json.dumps({'nodes':nodes,'edges':[(a,b,w) for a,ee in edges.items() for b,w in ee.items() if a<b],'anchorNodes':{a['name']:a['node'] for a in anchors}},indent=2),encoding='utf8')
W,H=1080,952
im=Image.new('RGB',(W,H),'#c4c7b5');draw=ImageDraw.Draw(im)
def xy(x,z):return ((x+10)/135*W,(9-z)/119*H)
for r in roads:
 a,b=xy(*r['a']),xy(*r['b']);width=round(r['width']*W/135)
 draw.line([a,b],fill='#728088',width=width)
 for x,y in [a,b]:draw.ellipse((x-width/2,y-width/2,x+width/2,y+width/2),fill='#728088')
for lot in layout['lots']:
 ident,family,x,z,w,d,floors,angle=lot
 if angle%180:w,d=d,w
 zone='A' if ident.startswith('A') else 'B' if ident.startswith('B') else 'C' if ident[0] in 'EH' else 'D'
 colors={'A':'#c1a489','B':'#a4b5c4','C':'#a1b89e','D':'#b8a4b7'}
 a=xy(x-w/2,z+d/2);b=xy(x+w/2,z-d/2)
 draw.rectangle([a,b],fill=colors[zone],outline='#7d847e',width=2)
font=ImageFont.truetype('C:/Windows/Fonts/arialbd.ttf',30)
for label,x,z in [('A',13,-29),('B',72,-36),('C',32,-70),('D',95,-88)]:
 p=xy(x,z);draw.text(p,label,font=font,fill='#293b45',anchor='mm',stroke_width=2,stroke_fill='#edf0e6')
home=next(a for a in anchors if a['type']=='home');x,y=xy(home['position'][0],home['position'][2]);draw.polygon([(x,y-9),(x-10,y),(x-7,y),(x-7,y+8),(x+7,y+8),(x+7,y),(x+10,y)],fill='#273c4a')
out=root/'Assets/_Game/Art/Myeongji/Map';out.mkdir(parents=True,exist_ok=True);im.save(out/'MyeongjiMap.png')
print(json.dumps({'nodes':len(nodes),'edges':sum(map(len,edges.values()))//2,'shops':len(shops),'doors':len(doors),'routeRangeMeters':[min(data['shopToDoor']),max(data['shopToDoor'])],'connected':True}))
