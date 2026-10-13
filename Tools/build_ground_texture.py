"""Bake the road-free ground colour texture for the four-district terrain.

Usage: python Tools/build_ground_texture.py <배달_체험판_3D.html>
Output: Docs/MapLayout/district_ground.png

Roads, sidewalks and alleys are spline meshes in the scene (RoadNetwork), so the terrain only needs the
zone tints. The image covers exactly the same area as district_surface.png (map UI) from
build_district_geometry.py. The HTML is parsed as data and never executed.
"""
import json, re, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

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
if data is None:
    raise ValueError('No map D')

# Same frame as build_district_geometry.py.
origin = data['nodes'][data['home']['node']]
def point(p): return np.array([p[0] - origin[0], origin[1] - p[1]])
nodes = np.array([point(p) for p in data['nodes']])
minimum = np.floor(nodes.min(axis=0) / 2) * 2 - 24
maximum = np.ceil(nodes.max(axis=0) / 2) * 2 + 24
nx, nz = (maximum - minimum).astype(int) + 1
width = 2048
factor = width / (nx - 1)
length = round((nz - 1) * factor)

def pixel(p): return (round((p[0] - minimum[0]) * factor), round((maximum[1] - p[1]) * factor))

# Supersample, then downsample: soft zone borders without stair steps.
scale = 2
im = Image.new('RGB', (width * scale, length * scale), (156, 173, 139))
draw = ImageDraw.Draw(im)
for key, zone in data['zones'].items():
    color = tuple(round(c * .22 + 180 * .78) for c in zone['color'])
    draw.polygon([tuple(v * scale for v in pixel(point(p))) for p in zone['poly']], fill=color)
im = im.filter(ImageFilter.GaussianBlur(radius=3 * scale)).resize((width, length), Image.LANCZOS)
out = root / 'Docs' / 'MapLayout' / 'district_ground.png'
im.save(out)
print(json.dumps({'width': width, 'height': length, 'output': str(out.relative_to(root))}))
