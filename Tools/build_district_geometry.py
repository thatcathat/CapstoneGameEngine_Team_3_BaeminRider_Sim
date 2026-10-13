"""Bake shared terrain heights and road paint for FourDistrictPrototype.
Outputs planning data only; the Unity installer creates all Unity assets.
Requires numpy and Pillow. Source is the supplied HTML, never executed.
"""
import json, math, re, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
source = Path(sys.argv[1]).read_text(encoding='utf-8')
data = None
for match in re.finditer(r'\b(?:const|let|var) D\s*=\s*', source):
    try:
        candidate = json.JSONDecoder().raw_decode(source[match.end():])[0]
        if isinstance(candidate, dict) and 'roads' in candidate:
            data = candidate
            break
    except ValueError:
        pass
if data is None: raise ValueError('No map D')
origin = data['nodes'][data['home']['node']]
def point(p): return np.array([p[0]-origin[0], origin[1]-p[1]])
nodes = np.array([point(p) for p in data['nodes']])
roads = [{'id':r['id'],'width':r['w'],'points':[point(p).tolist() for p in r['pts']]} for r in data['roads']]
def destination(p):
    # Move the reference's inset facade marker to the separate pedestrian apron.
    p2 = nodes[p['node']]+np.array([-p['u'][1],-p['u'][0]])*p['side']*(p['w']/2+1.5)
    outward = np.array([-p['u'][1],-p['u'][0]])*p['side']
    return {'x':float(p2[0]),'z':float(p2[1]),'outX':float(outward[0]),'outZ':float(outward[1]),'zone':p.get('zone','C'),'node':p['node']}
stores = [destination(p) for p in data['stores']]
doors = [destination(p) for p in data['doors']]
home = destination(data['home'])

# Merge overlapping flat discs, then use a smooth normalized influence outside them.
anchors = [(p.copy(),7.) for i,p in enumerate(nodes) if len(data['adj'][i])>2]
anchors += [(np.array([p['x'],p['z']]),3.) for p in stores+doors+[home]]
while True:
    merged=False
    for i,(a,ra) in enumerate(anchors):
        for j in range(i+1,len(anchors)):
            b,rb=anchors[j]; distance=np.linalg.norm(b-a)
            if distance < ra+rb+1:
                if distance+min(ra,rb)<=max(ra,rb):
                    center,radius=(a,ra) if ra>=rb else (b,rb)
                else:
                    radius=(distance+ra+rb)/2
                    center=a+(b-a)*(radius-ra)/distance
                anchors[i]=(center,radius);anchors.pop(j);merged=True;break
        if merged: break
    if not merged: break

minimum=np.floor(nodes.min(axis=0)/2)*2-24
maximum=np.ceil(nodes.max(axis=0)/2)*2+24
nx,nz=(maximum-minimum).astype(int)+1
xx,zz=np.meshgrid(np.arange(nx)+minimum[0], np.arange(nz)+minimum[1])
def base(x,z): return np.clip(19.835444-.00575527707*(x+origin[0])-.0616864818*(origin[1]-z),0,12)
anchor_heights=np.array([float(base(*c)) for c,r in anchors])
# Nearby flat areas cannot retain incompatible heights across a tiny gap.
# Relax their target heights before baking instead of making steep connectors.
for iteration in range(120):
    for i,(a,ra) in enumerate(anchors):
        for j in range(i+1,len(anchors)):
            b,rb=anchors[j]
            limit=.025*max(0,float(np.linalg.norm(a-b))-ra-rb)
            delta=anchor_heights[j]-anchor_heights[i]
            if abs(delta)>limit:
                correction=(abs(delta)-limit)*.5*np.sign(delta)
                anchor_heights[i]+=correction;anchor_heights[j]-=correction
values=np.zeros_like(xx,dtype=float); total=np.zeros_like(values)
for (center,radius),target in zip(anchors,anchor_heights):
    distance=np.maximum(np.hypot(xx-center[0],zz-center[1])-radius,0.0001)
    weight=1/(distance*distance)
    values+=float(target)*weight;total+=weight
height=values/total
height-=height.min()
# Measure both triangles in every grid cell, matching the editor's triangulation.
dx=height[:-1,1:]-height[:-1,:-1];dz=height[1:,:-1]-height[:-1,:-1]
dx2=height[1:,1:]-height[1:,:-1];dz2=height[1:,1:]-height[:-1,1:]
unscaled=max(np.hypot(dx,dz).max(),np.hypot(dx2,dz2).max())
scale=min(1.,.068/unscaled)
height*=scale
def sample(x,z):
    fx=np.clip(x-minimum[0],0,nx-1.000001);fz=np.clip(z-minimum[1],0,nz-1.000001)
    ix=int(fx);iz=int(fz);u=fx-ix;v=fz-iz
    a=height[iz,ix];b=height[iz,ix+1];c=height[iz+1,ix];d=height[iz+1,ix+1]
    return float(a+(b-a)*u+(c-a)*v if u+v<=1 else d+(c-d)*(1-u)+(b-d)*(1-v))
for p in stores+doors+[home]: p['y']=sample(p['x'],p['z'])
for r in roads: r['points']=[{'x':p[0],'y':sample(*p),'z':p[1]} for p in r['points']]
flat_check=0
for center,radius in anchors:
    mask=np.hypot(xx-center[0],zz-center[1])<radius-1
    if mask.any(): flat_check=max(flat_check,float(np.ptp(height[mask])))

out=root/'Docs'/'MapLayout';out.mkdir(parents=True,exist_ok=True)
payload={'nx':int(nx),'nz':int(nz),'minX':float(minimum[0]),'minZ':float(minimum[1]),'heights':height.ravel().round(6).tolist(),
         'roads':roads,'stores':stores,'doors':doors,'home':home,
         'flats':[{'x':float(c[0]),'z':float(c[1]),'radius':float(r),'y':sample(*c)} for c,r in anchors],
         'heightScale':scale,'maximumGradePercent':float(unscaled*scale*100),'maximumHeight':float(height.max()),'flatInteriorVariation':flat_check}
(out/'district_geometry.json').write_text(json.dumps(payload,separators=(',',':')),encoding='utf-8')

width=2048;factor=width/(nx-1);length=round((nz-1)*factor)
im=Image.new('RGB',(width,length),(156,173,139));draw=ImageDraw.Draw(im)
def pixel(p): return (round((p[0]-minimum[0])*factor),round((maximum[1]-p[1])*factor))
for key,z in data['zones'].items():
    color=tuple(round(c*.22+180*.78) for c in z['color'])
    draw.polygon([pixel(point(p)) for p in z['poly']],fill=color)
# The source graph also contains short junction links beyond named polylines.
# Paint these underneath the main carriageways so alley entrances remain legible.
for i,neighbors in enumerate(data['adj']):
    for j,_ in neighbors:
        if j>i:
            draw.line([pixel(nodes[i]),pixel(nodes[j])],fill=(188,145,78),width=round(4.5*factor))
for r in roads:
    if r['id'].startswith('L'): continue
    points=[pixel((p['x'],p['z'])) for p in r['points']]
    draw.line(points,fill=(202,197,179),width=round((r['width']+6)*factor),joint='curve')
    for x,y in points:
        radius=(r['width']/2+3)*factor;draw.ellipse((x-radius,y-radius,x+radius,y+radius),fill=(202,197,179))
for r in roads:
    points=[pixel((p['x'],p['z'])) for p in r['points']]
    color=(188,145,78) if r['id'].startswith('L') else (72,82,88)
    draw.line(points,fill=color,width=round(r['width']*factor),joint='curve')
    for x,y in points:
        radius=r['width']/2*factor;draw.ellipse((x-radius,y-radius,x+radius,y+radius),fill=color)
for p in stores+doors+[home]:
    x,y=pixel((p['x'],p['z']));r=1.5*factor
    draw.ellipse((x-r,y-r,x+r,y+r),fill=(230,215,176))
im.save(out/'district_surface.png')
print(json.dumps({k:payload[k] for k in ['nx','nz','heightScale','maximumGradePercent','maximumHeight','flatInteriorVariation']}))
print('Flat areas:',len(anchors))
