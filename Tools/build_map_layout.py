"""Extract the supplied HTML's road graph without running JavaScript.

Produces a planning layout, not a Unity scene. Heights are a game-design proposal;
intersection flattening and pedestrian access still need geometry authoring.
Usage: python Tools/build_map_layout.py <source.html>
Requires Pillow for the review PNG; JSON/CSV preserve unrounded coordinates.
"""
import csv
import hashlib
import html
import json
import math
from pathlib import Path
import re
import sys
from PIL import Image, ImageDraw, ImageFont

source = Path(sys.argv[1])
raw = source.read_bytes()
data = None
for match in re.finditer(r'\b(?:const|let|var) D\s*=\s*', raw.decode('utf-8')):
    try:
        candidate = json.JSONDecoder().raw_decode(raw.decode('utf-8')[match.end():])[0]
        if isinstance(candidate, dict) and 'roads' in candidate:
            data = candidate
            break
    except ValueError:
        pass
if data is None:
    raise ValueError('Expected HTML map data D was not found')

out = Path(__file__).resolve().parents[1] / 'Docs' / 'MapLayout'
out.mkdir(parents=True, exist_ok=True)
nodes = data['nodes']
origin = nodes[data['home']['node']]

def height(p):
    # Continuous plane fitted to district reference heights A8/B12/C0/D4.
    # Clamping leaves broad low/high areas. NOT measured real-world elevation.
    return max(0., min(12., 19.835444 - .00575527707*p[0] - .0616864818*p[1]))

def world(p):
    return [p[0]-origin[0], height(p), origin[1]-p[1]]

roads = []
for road in data['roads']:
    samples, distance = [], 0.
    pts = road['pts']
    for a, b in zip(pts, pts[1:]):
        length = math.dist(a, b)
        count = math.ceil(length)
        for i in range(count):
            t = i/count
            p = [a[k]+(b[k]-a[k])*t for k in range(2)]
            samples.append({'distance': distance+length*t, 'position': world(p)})
        distance += length
    samples.append({'distance': distance, 'position': world(pts[-1])})
    grades = [100*abs(b['position'][1]-a['position'][1])/(b['distance']-a['distance'])
              for a,b in zip(samples,samples[1:])]
    roads.append({**road, 'vehicle_allowed': not road['id'].startswith('L'),
                  'length_m': distance, 'max_grade_percent': max(grades),
                  'world_points': [world(p) for p in pts], 'profile': samples})

seen = {0}
pending = [0]
while pending:
    for neighbor, _ in data['adj'][pending.pop()]:
        if neighbor not in seen:
            seen.add(neighbor)
            pending.append(neighbor)
destinations = data['stores'] + data['doors'] + [data['home']]
assert all(p['node'] in seen for p in destinations), 'Disconnected delivery destination'
assert len(seen) == len(nodes), 'Disconnected road graph'
assert len(roads) == 22 and sum(not r['vehicle_allowed'] for r in roads) == 12
assert max(r['max_grade_percent'] for r in roads) <= 7

def endpoint(p):
    x,y = nodes[p['node']]
    lateral = p['side']*(p['w']/2-1)
    return [x-p['u'][1]*lateral, y+p['u'][0]*lateral]

payload = {'status': 'proposal_before_intersection_flattening',
           'source_sha256': hashlib.sha256(raw).hexdigest(),
           'coordinate_rule': 'Unity X=rawX-homeNodeX; Z=homeNodeY-rawY; 1 unit=1m; no display rotation',
           'origin_raw': origin, 'height_rule': 'clamp(19.835444 - 0.00575527707*x - 0.0616864818*y, 0, 12)',
           'nodes': [world(p) for p in nodes], 'adj': data['adj'], 'roads': roads,
           'stores': [{**p,'world_position':world(endpoint(p))} for p in data['stores']],
           'doors': [{**p,'world_position':world(endpoint(p))} for p in data['doors']],
           'home': {**data['home'],'world_position':world(endpoint(data['home']))},
           'connected_nodes': len(seen)}
(out/'road_elevation.json').write_text(json.dumps(payload,ensure_ascii=False,indent=2),encoding='utf-8')
with (out/'road_summary.csv').open('w',encoding='utf-8-sig',newline='') as f:
    writer = csv.writer(f)
    writer.writerow(['road','width_m','length_m','vehicle_allowed','max_grade_percent','start_height','end_height'])
    for r in roads:
        writer.writerow([r['id'],r['w'],round(r['length_m'],2),r['vehicle_allowed'],round(r['max_grade_percent'],2),round(height(r['pts'][0]),2),round(height(r['pts'][-1]),2)])

# Render both an editable vector and a matching raster review artifact.
W,H = 1700,1120
im = Image.new('RGB',(W,H),'#f7f8fa')
draw = ImageDraw.Draw(im)
font_path = 'C:/Windows/Fonts/malgun.ttf'
svg = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">', '<rect width="100%" height="100%" fill="#f7f8fa"/>']
def text(x,y,value,size=18,color='#283443'):
    draw.text((x,y),value,font=ImageFont.truetype(font_path,size),fill=color)
    svg.append(f'<text x="{x}" y="{y+size}" font-family="Malgun Gothic,sans-serif" font-size="{size}" fill="{color}">{html.escape(value)}</text>')
def line(points,color,width=2):
    draw.line(points,fill=color,width=max(1,round(width)))
    ps=' '.join(f'{x:.2f},{y:.2f}' for x,y in points)
    svg.append(f'<polyline points="{ps}" fill="none" stroke="{color}" stroke-width="{width}" stroke-linejoin="round"/>')
def dot(p,color,r=4):
    x,y=p; draw.ellipse((x-r,y-r,x+r,y+r),fill=color)
    svg.append(f'<circle cx="{x}" cy="{y}" r="{r}" fill="{color}"/>')
def screen(p): return (55+p[0]*1.95,145+p[1]*1.95)

text(45,25,'4구역 도로 · 고도 배치 초안',34)
text(45,78,'HTML 원본 좌표 | 큰길 10개 · 골목 12개 | 높이는 게임용 제안이며 실제 지형이 아닙니다',20)
for key,z in data['zones'].items():
    points=[screen(p) for p in z['poly']]
    rgb=tuple(round(c*.22+255*.78) for c in z['color'])
    color='#%02x%02x%02x'%rgb
    draw.polygon(points,fill=color)
    svg.append('<polygon points="'+' '.join(f'{x},{y}' for x,y in points)+f'" fill="{color}"/>')
    center=[sum(p[k] for p in z['poly'])/len(z['poly']) for k in range(2)]
for r in roads:
    line([screen(p) for p in r['pts']], '#ffffff',r['w']*1.95+5)
    for a,b in zip(r['pts'],r['pts'][1:]):
        h=height([(a[k]+b[k])/2 for k in range(2)])/12
        color=('#%02x%02x%02x'%(round(70+110*h),round(137-48*h),round(164-70*h))) if r['vehicle_allowed'] else '#d89924'
        line([screen(a),screen(b)],color,r['w']*1.95)
    middle=r['pts'][len(r['pts'])//2] if len(r['pts'])>2 else [(r['pts'][0][k]+r['pts'][1][k])/2 for k in range(2)]
    x,y=screen(middle)
    text(x+5,y+3,r['id'],15)
for p in data['doors']: dot(screen(endpoint(p)),'#566775',3)
for p in data['stores']: dot(screen(endpoint(p)),'#182c41',6)
for key,z in data['zones'].items():
    center=[sum(p[k] for p in z['poly'])/len(z['poly']) for k in range(2)]
    x,y=screen(center); x-=28; y-=35
    draw.rectangle((x-5,y-2,x+145,y+33),fill='#ffffff')
    svg.append(f'<rect x="{x-5}" y="{y-2}" width="150" height="35" fill="white"/>')
    text(x,y,f'{key} / {height(center):.1f}m',22)
dot(screen(endpoint(data['home'])),'#d63546',8)
x,y=screen(endpoint(data['home']));text(x-20,y+12,'집',18)
line([(65,1015),(260,1015)],'#283443',3);text(115,1020,'100m',16)
text(320,1000,'큰 점: 가게 12 / 작은 점: 배달 문 32 / 빨강: 집',18)
text(320,1034,'상단이 높은 지형 / 골목은 차량 진입 제외',18)
text(1050,135,'도로별 길이와 최대 경사',24)
text(1050,177,'ID       길이       경사     차량',17)
for i,r in enumerate(roads):
    text(1050,209+i*25,f"{r['id']:<5}   {r['length_m']:6.1f}m    {r['max_grade_percent']:4.1f}%    {'가능' if r['vehicle_allowed'] else '제외'}",16)
text(1050,786,'높이 0~12m · 기본면 최대 경사 6.20%',18)
text(1050,824,'다음 지형 제작에서 확인할 것',21)
for i,v in enumerate(['교차로 평탄화 뒤 연결부 경사 재계산','보도·가게 입구 높이와 연석 연결','도로 폭과 별도 보행 공간 확보','내리막 제동 거리와 골목 시야 확보']):
    text(1050,867+i*34,'• '+v,17)
text(45,1080,f'연결 확인: {len(seen)}/{len(nodes)} 노드 · 모든 가게/배달 문 연결 | 물리 통행 검증 전',17)
svg.append('</svg>')
(out/'road_elevation.svg').write_text('\n'.join(svg),encoding='utf-8')
im.save(out/'road_elevation.png')
print(json.dumps({'roads':len(roads),'connected_nodes':len(seen),'max_grade':max(r['max_grade_percent'] for r in roads),'output':str(out)}))
