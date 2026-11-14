// Written with Claude Code (AI), directed and tested by team MARSXR
using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Walkback
{
    /// <summary>
    /// The square kilometer on the table: a 257 x 257 mesh from session B's site package
    /// (or the synthetic test terrain until it lands), relief not exaggerated, and three paints
    /// computed from the heights: height, slope and return on foot.
    /// Mesh units are meters; the parent scales the kilometer down to the table. North is +z.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class GaleTerrain : MonoBehaviour
    {
        public SiteData Site { get; private set; }
        /// <summary>Slope in degrees at every node, row 0 = north.</summary>
        public float[] Slope { get; private set; }
        /// <summary>Minutes back to the habitat from every node, row 0 = north.</summary>
        public float[] ReturnMinutes { get; private set; }
        /// <summary>True while the return layer comes from the stand-in, not session C's model.</summary>
        public bool ReturnIsPlaceholder { get; private set; }
        public MapLayer Layer { get; private set; }
        /// <summary>Milliseconds each layer took to paint (0 = not painted yet).</summary>
        public readonly long[] PaintMilliseconds = new long[3];

        public event Action<MapLayer> LayerChanged;

        Mesh mesh;
        readonly Texture2D[] paints = new Texture2D[3];
        MaterialPropertyBlock block;

        void Awake()
        {
            Build();
        }

        void OnDestroy()
        {
            DestroySafe(mesh);
            for (int i = 0; i < paints.Length; i++) DestroySafe(paints[i]);
        }

        /// <summary>Loads the site package (or the synthetic stand-in) and builds everything.</summary>
        public void Build()
        {
            Build(SiteLoader.Load());
        }

        public void Build(SiteData site)
        {
            var watch = Stopwatch.StartNew();
            Site = site;
            BuildMesh();
            Slope = TerrainLayers.SlopeDegrees(site.Heights, site.GridSize, site.NodeSpacing);
            ReturnMinutes = ReturnStandIn.Minutes(site);
            ReturnIsPlaceholder = true;
            for (int i = 0; i < paints.Length; i++)
            {
                DestroySafe(paints[i]);
                paints[i] = null;
                PaintMilliseconds[i] = 0;
            }
            ShowLayer(Layer);
            WalkbackLog.Info("Terrain built in " + watch.ElapsedMilliseconds + " ms: " + (site.IsMarsData ? site.Info.name : "synthetic test terrain") +
                ", relief " + (site.MaxElevation - site.MinElevation).ToString("F1") + " m");
        }

        public void ShowLayer(MapLayer layer)
        {
            Layer = layer;
            if (block == null) block = new MaterialPropertyBlock();
            block.SetTexture("_BaseMap", LayerTexture(layer));
            GetComponent<MeshRenderer>().SetPropertyBlock(block);
            if (LayerChanged != null) LayerChanged(layer);
        }

        /// <summary>The paint of one layer; painted the first time it is asked for.</summary>
        public Texture2D LayerTexture(MapLayer layer)
        {
            int i = (int)layer;
            if (paints[i] != null) return paints[i];

            var watch = Stopwatch.StartNew();
            int size = TerrainLayers.PaintSize;
            int n = Site.GridSize;
            Color32[] pixels;
            switch (layer)
            {
                case MapLayer.Slope:
                    pixels = TerrainLayers.PaintSlope(Slope, n, size);
                    break;
                case MapLayer.Return:
                    Vector2 h = Site.Habitat;
                    pixels = TerrainLayers.PaintReturn(ReturnMinutes, n, size, new Vector2(h.x / Site.SideMeters, h.y / Site.SideMeters));
                    break;
                default:
                    pixels = TerrainLayers.PaintHeight(Site.Heights, n, Site.MinElevation, Site.MaxElevation, size);
                    break;
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Paint_" + layer,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels32(pixels);
            // In the headset the CPU copy is dropped (12 MB saved); the editor test keeps it to read pixels.
            texture.Apply(true, Application.isPlaying);
            paints[i] = texture;
            PaintMilliseconds[i] = Math.Max(1, watch.ElapsedMilliseconds);
            WalkbackLog.Info("Layer " + layer + " painted in " + PaintMilliseconds[i] + " ms");
            return texture;
        }

        /// <summary>One line under the verdict: what this terrain is and where it comes from.</summary>
        public string SourceLine()
        {
            if (!Site.IsMarsData)
                return "TEST TERRAIN · synthetic shape for development · not Mars data";
            var info = Site.Info;
            string place = (string.IsNullOrEmpty(info.name) ? "Mars" : info.name.ToUpperInvariant()) + " · " + (Site.SideMeters / 1000f).ToString("0.#") + " km²";
            place += " · " + Math.Abs(info.centerLat).ToString("F2") + "° " + (info.centerLat < 0 ? "S" : "N") +
                     ", " + Math.Abs(info.centerLon).ToString("F2") + "° " + (info.centerLon < 0 ? "W" : "E");
            var source = Site.ElevationSource;
            if (source != null)
                place += " · heights: " + Join(" ", source.mission, source.instrument) + (string.IsNullOrEmpty(source.resolution) ? "" : ", " + source.resolution);
            return place;
        }

        /// <summary>What the colors of a layer mean, in the panel's rich text.</summary>
        public string LegendText(MapLayer layer)
        {
            switch (layer)
            {
                case MapLayer.Slope:
                    return "SLOPE   " +
                        Colored(TerrainLayers.SlopeGentle, "under " + TerrainLayers.GentleSlopeDegrees.ToString("0") + "°") + "  ·  " +
                        Colored(TerrainLayers.SlopeCareful, TerrainLayers.GentleSlopeDegrees.ToString("0") + "–" + TerrainLayers.SlopeLimitDegrees.ToString("0") + "° with care") + "  ·  " +
                        Colored(TerrainLayers.SlopeNoGo, "over " + TerrainLayers.SlopeLimitDegrees.ToString("0") + "° no-go, hatched") +
                        "\nComputed from the heights, node by node (" + Site.NodeSpacing.ToString("F1") + " m apart).";
                case MapLayer.Return:
                    float worst = 0f;
                    foreach (float m in ReturnMinutes) if (!float.IsInfinity(m) && m > worst) worst = m;
                    string text = "RETURN ON FOOT   white dot = habitat, brighter = closer  ·  ring every " +
                        TerrainLayers.ReturnRingMinutes.ToString("0") + " min  ·  farthest " + worst.ToString("0") + " min";
                    if (ReturnIsPlaceholder)
                        text += "\n" + Colored(WalkbackTheme.Watch, ReturnStandIn.Label);
                    return text;
                default:
                    return "HEIGHT   dark = low " + Site.MinElevation.ToString("F0") + " m  ·  light = high " + Site.MaxElevation.ToString("F0") + " m  ·  " +
                        "contour line every " + TerrainLayers.ContourMeters.ToString("0") + " m\nRelief " + (Site.MaxElevation - Site.MinElevation).ToString("F0") +
                        " m over " + Site.SideMeters.ToString("0") + " m, not exaggerated.";
            }
        }

        void BuildMesh()
        {
            int n = Site.GridSize;
            float side = Site.SideMeters;
            float step = Site.NodeSpacing;
            float min = Site.MinElevation;
            float[] heights = Site.Heights;

            var vertices = new Vector3[n * n];
            var uv = new Vector2[n * n];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    int i = r * n + c;
                    vertices[i] = new Vector3(c * step - side / 2f, heights[i] - min, side / 2f - r * step);
                    uv[i] = new Vector2(c / (float)(n - 1), 1f - r / (float)(n - 1));
                }
            }

            var triangles = new int[(n - 1) * (n - 1) * 6];
            int t = 0;
            for (int r = 0; r < n - 1; r++)
            {
                for (int c = 0; c < n - 1; c++)
                {
                    int a = r * n + c, b = a + 1, d = a + n, e = d + 1;
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = e;
                    triangles[t++] = a; triangles[t++] = e; triangles[t++] = d;
                }
            }

            DestroySafe(mesh);
            mesh = new Mesh { name = "GaleTerrain", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        static string Colored(Color color, string text)
        {
            return "<color=" + TerrainLayers.Hex((Color32)color) + ">" + text + "</color>";
        }

        static string Join(string separator, params string[] parts)
        {
            string result = "";
            foreach (string p in parts)
            {
                if (string.IsNullOrEmpty(p)) continue;
                result += (result.Length > 0 ? separator : "") + p;
            }
            return result;
        }

        static void DestroySafe(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
