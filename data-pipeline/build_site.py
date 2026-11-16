# Written with Claude Code (AI), directed and tested by team MARSXR
"""Builds the site package of the data contract (guion §22.7) from windows already downloaded.

One program for any site: the DEM and the photo can be in any projection GDAL reads; everything is
warped to the local square (meters from the south-west corner, x east, y north, row 0 = north).

Usage, from the project root (GDAL from QGIS; set GDAL_BIN if it lives elsewhere):
  python -I data-pipeline/build_site.py --site data-pipeline/sites/gale.json --dem-source marstrek
      --dem data-pipeline/cache/marstrek/gale_dem_window.tif
      --ortho data-pipeline/cache/ortho/gale_ortho_window.tif
      --waypoints data-pipeline/cache/waypoints/MSL_waypoints.json
      --habitat 514.8,777.0 --out Assets/StreamingAssets/Site --work data-pipeline/cache/work
      --report data-pipeline/reports/gale_controls.json

Writes: site.json, heightmap.r16 (513 x 513), sand.png (257 x 257), ortho.jpg (4096 x 4096),
stops.json, curiosity_traverse.json; and a controls report (sizes, heights, boxwork check).
"""
import argparse
import json
import math
import os
import subprocess

import numpy as np
from PIL import Image

GDAL_BIN = os.environ.get('GDAL_BIN', r'C:\Program Files\QGIS 3.44.15\bin')
MARS_RADIUS = 3396190.0
HEIGHTMAP = 513
GRID = 257
ORTHO = 4096
NODATA = -32767.0
HOLLOW_DEPTH = 0.5      # m below the neighbourhood mean (ficha A)
HOLLOW_BOX = 2          # nodes each side: 5 x 5 nodes = 19.5 m, "about 20 m" (ficha A)


def gdal(tool, *args):
    subprocess.run([os.path.join(GDAL_BIN, tool + '.exe')] + [str(a) for a in args], check=True)


def local_crs(lat, lon):
    """Equirectangular on the Mars 2000 sphere, true scale at the site: meters east/north of the center."""
    return '+proj=eqc +lat_ts=%r +lat_0=%r +lon_0=%r +x_0=0 +y_0=0 +R=%r +units=m +no_defs' % (lat, lat, lon, MARS_RADIUS)


def warp(src, dst, crs, half, n, resampling, ot='Float32', nodata=True):
    """Warps src to an n x n raw raster covering [-half, half] around the center; returns the array."""
    args = ['-q', '-overwrite', '-t_srs', crs, '-te', -half, -half, half, half, '-ts', n, n,
            '-r', resampling, '-ot', ot, '-of', 'ENVI']
    if nodata:
        args += ['-dstnodata', NODATA]
    gdal('gdalwarp', *(args + [src, dst]))
    dtype = '<f4' if ot == 'Float32' else np.uint8
    return np.fromfile(dst, dtype=dtype).reshape(n, n)


def fill_nodata(z):
    """Fills nodata cells (only expected on the outermost edge) from their valid neighbours."""
    z = z.astype(np.float64)
    bad = z <= NODATA + 1
    filled = int(bad.sum())
    for _ in range(20):
        if not bad.any():
            break
        p = np.pad(np.where(bad, 0.0, z), 1, mode='edge')
        w = np.pad((~bad).astype(np.float64), 1, mode='edge')
        s = sum(np.roll(np.roll(p, dy, 0), dx, 1)[1:-1, 1:-1] for dy in (-1, 0, 1) for dx in (-1, 0, 1))
        c = sum(np.roll(np.roll(w, dy, 0), dx, 1)[1:-1, 1:-1] for dy in (-1, 0, 1) for dx in (-1, 0, 1))
        can = bad & (c > 0)
        z[can] = s[can] / c[can]
        bad = bad & ~can
    if bad.any():
        raise SystemExit('DEM window does not cover the square: %d cells without data' % bad.sum())
    return z, filled


def box_mean(z, r):
    p = np.pad(z, r, mode='edge')
    c = np.pad(p.cumsum(0).cumsum(1), ((1, 0), (1, 0)))
    k = 2 * r + 1
    return (c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]) / (k * k)


def slope_deg(g, spacing):
    gy, gx = np.gradient(g, spacing)
    return np.degrees(np.arctan(np.hypot(gx, gy)))


def node_of(x, y, side):
    s = side / (GRID - 1)
    return int(round((side - y) / s)), int(round(x / s))   # row, column


def to_local(easting, northing, lat0, lon0):
    """MSL waypoints are in Equirectangular clon0 on the same sphere (easting = R * lon)."""
    e0 = MARS_RADIUS * math.radians(lon0)
    n0 = MARS_RADIUS * math.radians(lat0)
    return (easting - e0) * math.cos(math.radians(lat0)), northing - n0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--site', required=True)
    ap.add_argument('--dem', required=True)
    ap.add_argument('--dem-source', required=True, help='key of the elevation source in the site file (usgs, marstrek)')
    ap.add_argument('--ortho', required=True)
    ap.add_argument('--waypoints', required=True)
    ap.add_argument('--habitat', required=True, help='x,y in local meters')
    ap.add_argument('--sand', choices=('none', 'dem'), default='none',
                    help='none: plan B, rock everywhere (default); dem: hollows from the DEM (ficha A)')
    ap.add_argument('--extra-sources', default='',
                    help='comma list of more source keys of the site file to list in site.json (globe, panoramas)')
    ap.add_argument('--panorama', action='append', default=[],
                    help='stopId=panoramaId=photoCredit: the stop gets Site/panorama_<panoramaId>.jpg')
    ap.add_argument('--out', required=True)
    ap.add_argument('--work', required=True)
    ap.add_argument('--report', required=True)
    a = ap.parse_args()

    site = json.load(open(a.site, encoding='utf-8'))
    lat0, lon0, side = site['centerLat'], site['centerLon'], float(site['sideMeters'])
    crs = local_crs(lat0, lon0)
    os.makedirs(a.out, exist_ok=True)
    os.makedirs(a.work, exist_ok=True)
    os.makedirs(os.path.dirname(a.report), exist_ok=True)
    w = lambda name: os.path.join(a.work, name)
    report = {'site': site['name'], 'demSource': a.dem_source, 'dem': os.path.basename(a.dem), 'ortho': os.path.basename(a.ortho)}

    # 1. Heights: 513 x 513 points from corner to corner (pixel i at x = i * side / 512).
    hp = side / (HEIGHTMAP - 1)
    z, filled = fill_nodata(warp(a.dem, w('hm513.bin'), crs, side / 2 + hp / 2, HEIGHTMAP, 'average'))
    zmin = math.floor(z.min() * 1000) / 1000
    zmax = math.ceil(z.max() * 1000) / 1000
    v = np.round((z - zmin) / (zmax - zmin) * 65535).clip(0, 65535).astype('<u2')
    v.tofile(os.path.join(a.out, 'heightmap.r16'))
    decoded = zmin + v.astype(np.float64) / 65535 * (zmax - zmin)
    report['heightmap'] = {'min': zmin, 'max': zmax, 'edgeCellsFilled': filled,
                           'quantizationErrorMaxMm': round(float(np.abs(decoded - z).max()) * 1000, 2)}

    # 2. The grid the model uses: node (c, r) = pixel (2c, 2r), exactly as SiteLoader resamples.
    g = decoded[::2, ::2]
    spacing = side / (GRID - 1)
    slope = slope_deg(g, spacing)
    report['slope'] = {'maxDeg': round(float(slope.max()), 1), 'over15pct': round(float((slope > 15).mean() * 100), 2),
                       'over20pct': round(float((slope > 20).mean() * 100), 2), 'over25pct': round(float((slope > 25).mean() * 100), 2)}

    # 3. Sandy hollows (ficha A): a node more than 0.5 m below the mean of its ~20 m surroundings.
    #    --sand none (plan B, guion §22.5): the DEM does not resolve them, so rock everywhere.
    relief = g - box_mean(g, HOLLOW_BOX)
    sand = relief < -HOLLOW_DEPTH
    written = sand if a.sand == 'dem' else np.zeros_like(sand)
    Image.fromarray(np.where(written, 255, 0).astype(np.uint8), 'L').save(os.path.join(a.out, 'sand.png'))
    report['sand'] = {'mode': a.sand, 'demHollowPct': round(float(sand.mean() * 100), 1),
                      'writtenHollowPct': round(float(written.mean() * 100), 1)}

    # 4. Base photo: 4096 x 4096 covering the square edge to edge, linear stretch 0.5-99.5 %.
    o = warp(a.ortho, w('ortho4096.bin'), crs, side / 2, ORTHO, 'cubic', ot='Byte', nodata=False)
    lo, hi = np.percentile(o, [0.5, 99.5])
    o8 = ((o.astype(np.float32) - lo) / max(hi - lo, 1) * 255).clip(0, 255).astype(np.uint8)
    Image.fromarray(o8, 'L').save(os.path.join(a.out, 'ortho.jpg'), quality=90, optimize=True)
    report['ortho'] = {'size': ORTHO, 'metersPerPixel': round(side / ORTHO, 4), 'stretch': [float(lo), float(hi)]}

    # 5. Boxwork check: do the DEM's ridges and hollows line up with bright and dark ground in the photo?
    on = warp(a.ortho, w('ortho257.bin'), crs, side / 2 + spacing / 2, GRID, 'average', ot='Float32', nodata=False).astype(np.float64)
    bright = on - box_mean(on, HOLLOW_BOX)
    r = float(np.corrcoef(relief.ravel(), bright.ravel())[0, 1])
    row_share = float(np.var(relief.mean(axis=1)) / np.var(relief))
    col_share = float(np.var(relief.mean(axis=0)) / np.var(relief))
    report['boxworkCheck'] = {
        'reliefVsBrightnessR': round(r, 3),
        'photoBrightnessInHollows': round(float(on[sand].mean()), 1),
        'photoBrightnessElsewhere': round(float(on[~sand].mean()), 1),
        'reliefVarianceInRowMeansPct': round(row_share * 100, 2),
        'reliefVarianceInColumnMeansPct': round(col_share * 100, 2),
    }

    # 6. Curiosity: the traverse inside the square, and the stops at the rover's positions.
    feats = json.load(open(a.waypoints, encoding='utf-8'))['features']
    pts = []
    for f in feats:
        p = f['properties']
        x, y = to_local(p['easting'], p['northing'], lat0, lon0)
        x, y = x + side / 2, y + side / 2
        pts.append((p['sol'], x, y, p.get('elev_geoid')))
    inside = [(s, x, y, e) for s, x, y, e in pts if 0 <= x <= side and 0 <= y <= side]
    json.dump([{'sol': s, 'x': round(x, 1), 'y': round(y, 1)} for s, x, y, e in inside],
              open(os.path.join(a.out, 'curiosity_traverse.json'), 'w', encoding='utf-8'), indent=1)
    diffs = []
    for s, x, y, e in inside:
        rr, cc = (side - y) / hp, x / hp
        r0, c0 = min(int(rr), HEIGHTMAP - 2), min(int(cc), HEIGHTMAP - 2)
        fr, fc = rr - r0, cc - c0
        h = (decoded[r0, c0] * (1 - fc) + decoded[r0, c0 + 1] * fc) * (1 - fr) + (decoded[r0 + 1, c0] * (1 - fc) + decoded[r0 + 1, c0 + 1] * fc) * fr
        if e is not None:
            diffs.append(h - e)
    diffs = np.array(diffs)
    report['curiosity'] = {'pointsInside': len(inside), 'sols': [inside[0][0], inside[-1][0]] if inside else [],
                           'demMinusRoverElevationMean': round(float(diffs.mean()), 2), 'demMinusRoverElevationStd': round(float(diffs.std()), 2)}

    by_sol = {s: (x, y) for s, x, y, e in pts}
    panoramas = {}
    for p in a.panorama:
        stop_id, pano_id, credit = p.split('=', 2)
        if not os.path.exists(os.path.join(a.out, 'panorama_%s.jpg' % pano_id)):
            raise SystemExit('panorama_%s.jpg is not in %s' % (pano_id, a.out))
        panoramas[stop_id] = (pano_id, credit)
    stops, stop_report = [], []
    for st in site['stops']:
        x, y = by_sol[st['roverWaypointSol']]
        rr, cc = node_of(x, y, side)
        out = {k: st[k] for k in ('id', 'name', 'science', 'tasks', 'risk', 'photoCredit', 'panorama')}
        if st['id'] in panoramas:
            out['panorama'], out['photoCredit'] = panoramas[st['id']]
        out.update({'x': round(x, 1), 'y': round(y, 1)})
        for k in ('tag', 'tagSource', 'scienceSource', 'taskSource', 'nameOrigin'):
            if k in st:
                out[k] = st[k]
        stops.append(out)
        near = slope[max(rr - 3, 0):rr + 4, max(cc - 3, 0):cc + 4]
        stop_report.append({'name': st['name'], 'x': out['x'], 'y': out['y'], 'height': round(float(g[rr, cc]), 2),
                            'relief': round(float(relief[rr, cc]), 2), 'sand': bool(sand[rr, cc]),
                            'slope': round(float(slope[rr, cc]), 1), 'maxSlopeWithin12m': round(float(near.max()), 1),
                            'scienceWords': len(st['science'].split())})
    json.dump(stops, open(os.path.join(a.out, 'stops.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    report['stops'] = stop_report

    # 7. The habitat (a team decision, passed in) and site.json.
    hx, hy = [float(t) for t in a.habitat.split(',')]
    hr, hc = node_of(hx, hy, side)
    near = slope[max(hr - 4, 0):hr + 5, max(hc - 4, 0):hc + 5]
    report['habitat'] = {'x': hx, 'y': hy, 'slope': round(float(slope[hr, hc]), 1), 'maxSlopeWithin15m': round(float(near.max()), 1),
                         'sand': bool(sand[hr, hc]),
                         'straightLineToStops': [round(math.hypot(hx - s['x'], hy - s['y'])) for s in stops]}
    src = site['sources']
    sources = []
    extra = [k for k in a.extra_sources.split(',') if k]
    for key in [a.dem_source, 'ortho', 'sand' if a.sand == 'dem' else 'sand_none', 'traverse'] + extra:
        s = {k: src[key][k] for k in ('layer', 'mission', 'instrument', 'resolution', 'id', 'credit')}
        sources.append(s)
    info = {
        'name': site['name'], 'centerLat': lat0, 'centerLon': lon0, 'sideMeters': side,
        'heightmapSize': HEIGHTMAP, 'gridSize': GRID, 'minElevation': zmin, 'maxElevation': zmax,
        'habitat': {'x': hx, 'y': hy},
        'crs': 'Local meters from the south-west corner, x east, y north; equirectangular on the Mars 2000 sphere '
               '(R 3396190 m), true scale at %.3f, %.3f E' % (lat0, lon0),
        'sources': sources,
    }
    if 'photoTint' in site:
        info['photoTint'] = site['photoTint']
    json.dump(info, open(os.path.join(a.out, 'site.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)

    report['files'] = {f: os.path.getsize(os.path.join(a.out, f)) for f in sorted(os.listdir(a.out)) if not f.endswith('.meta')}
    json.dump(report, open(a.report, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    print(json.dumps(report, indent=1, ensure_ascii=False))


if __name__ == '__main__':
    main()
