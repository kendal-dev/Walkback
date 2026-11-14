"""Pack the Gale site package into JS files the pitch deck can load from file:// (no fetch)."""
import base64, heapq, io, json, math, sys
import numpy as np
from PIL import Image

site_dir, out_dir, glb = sys.argv[1], sys.argv[2], sys.argv[3]
site = json.load(open(f"{site_dir}/site.json", encoding="utf-8"))
stops = json.load(open(f"{site_dir}/stops.json", encoding="utf-8"))
trav = json.load(open(f"{site_dir}/curiosity_traverse.json", encoding="utf-8"))

n = site["heightmapSize"]
raw = np.fromfile(f"{site_dir}/heightmap.r16", dtype="<u2").reshape(n, n)
lo, hi = site["minElevation"], site["maxElevation"]
h = lo + raw.astype(np.float64) / 65535.0 * (hi - lo)  # meters, row 0 = north

# 257 grid for the 3D mesh and the route
g = site["gridSize"]
hg = h[::2, ::2]
assert hg.shape == (g, g)
side = site["sideMeters"]
step = side / (g - 1)

# Slope per node (degrees)
gy, gx = np.gradient(hg, step)
slope = np.degrees(np.arctan(np.hypot(gx, gy)))

def node(x, y):  # local meters -> (row, col)
    return int(round((side - y) / step)), int(round(x / step))

# A* on 8 neighbours: distance cost, no node over 20 deg and no step over 20 deg
LIM = 20.0
def astar(a, b):
    nb = [(-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1)]
    def hcost(p): return math.hypot(p[0] - b[0], p[1] - b[1]) * step
    openq = [(hcost(a), 0.0, a)]
    best = {a: 0.0}; came = {}
    while openq:
        _, c, p = heapq.heappop(openq)
        if p == b:
            path = [p]
            while p in came: p = came[p]; path.append(p)
            return path[::-1]
        if c > best.get(p, 1e18): continue
        for dr, dc in nb:
            q = (p[0] + dr, p[1] + dc)
            if not (0 <= q[0] < g and 0 <= q[1] < g): continue
            if slope[q] > LIM: continue
            d = math.hypot(dr, dc) * step
            if math.degrees(math.atan(abs(hg[q] - hg[p]) / d)) > LIM: continue
            nc = c + d
            if nc < best.get(q, 1e18):
                best[q] = nc; came[q] = p
                heapq.heappush(openq, (nc + hcost(q), nc, q))
    return None

hab = site["habitat"]
routes = {}
for s in stops:
    path = astar(node(hab["x"], hab["y"]), node(s["x"], s["y"]))
    routes[s["id"]] = [[c * step, side - r * step] for r, c in path] if path else None

def jpg_b64(path, width, q=82):
    im = Image.open(path).convert("RGB")
    im = im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)
    buf = io.BytesIO(); im.save(buf, "JPEG", quality=q)
    return "data:image/jpeg;base64," + base64.b64encode(buf.getvalue()).decode()

q16 = np.round((hg - hg.min()) / (hg.max() - hg.min()) * 65535).astype("<u2")
gale = {
    "grid": g, "side": side,
    "hMin": float(hg.min()), "hMax": float(hg.max()),
    "heights": base64.b64encode(q16.tobytes()).decode(),
    "ortho": jpg_b64(f"{site_dir}/ortho.jpg", 1536),
    "habitat": hab,
    "stops": [{"id": s["id"], "name": s["name"], "x": s["x"], "y": s["y"]} for s in stops],
    "traverse": [[p["x"], p["y"]] for p in trav],
    "routes": routes,
    "slopeLimit": LIM,
}
with open(f"{out_dir}/gale.js", "w", encoding="utf-8") as f:
    f.write("// Generated from the Walkback Gale site package (DATA.md). Do not edit by hand.\n")
    f.write("window.GALE = " + json.dumps(gale) + ";\n")
with open(f"{out_dir}/globe.js", "w", encoding="utf-8") as f:
    f.write("// Viking MDIM 2.1 colour mosaic, NASA/JPL/USGS. Left edge 180 W.\n")
    f.write("window.GLOBE_TEX = " + json.dumps(jpg_b64(f"{site_dir}/mars_global.jpg", 2048, 80)) + ";\n")
with open(f"{out_dir}/astronaut.js", "w", encoding="utf-8") as f:
    f.write("// NASA 3D Resources, Astronaut.glb (public domain).\n")
    f.write("window.ASTRONAUT_GLB = " + json.dumps(base64.b64encode(open(glb, "rb").read()).decode()) + ";\n")

for k, v in routes.items():
    print(k, "route nodes:", len(v) if v else None)
print("slope over 20:", round(float((slope > LIM).mean()) * 100, 1), "%")
