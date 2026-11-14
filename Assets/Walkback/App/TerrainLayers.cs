// Written with Claude Code (AI), directed and tested by team MARSXR
using UnityEngine;

namespace Walkback
{
    public enum MapLayer { Height = 0, Slope = 1, Return = 2 }

    /// <summary>
    /// The three paints of the map, computed in the app from the heights (guion §3, piece 2):
    /// height with contour lines, slope in three bands (over the limit hatched) and return on foot
    /// with 5-minute rings. Pure functions: no scene needed, the automatic test calls them directly.
    /// Paint textures follow Unity's order: pixel row 0 is the south edge.
    /// </summary>
    public static class TerrainLayers
    {
        /// <summary>1 m of map on the table = 1024 px, about 1 mm per pixel: hatching stays crisp.</summary>
        public const int PaintSize = 1024;

        /// <summary>Slope bands (continuidad §5): up to 10° normal, 10–20° with care, over 20° no-go.</summary>
        public const float GentleSlopeDegrees = 10f;
        public const float SlopeLimitDegrees = 20f;

        public const float ContourMeters = 10f;
        public const float ReturnRingMinutes = 5f;

        const int HatchPeriod = 14;
        const int HatchWidth = 6;

        // Okabe-Ito: blue, orange, vermilion stay apart for color-blind viewers and in grayscale.
        public static readonly Color32 SlopeGentle = new Color32(0x56, 0xB4, 0xE9, 255);
        public static readonly Color32 SlopeCareful = new Color32(0xE6, 0x9F, 0x00, 255);
        public static readonly Color32 SlopeNoGo = new Color32(0xD5, 0x5E, 0x00, 255);
        public static readonly Color32 SlopeNoGoHatch = new Color32(0x6E, 0x2C, 0x00, 255);

        static readonly Color HeightLow = new Color(0.30f, 0.18f, 0.12f);
        static readonly Color HeightMid = new Color(0.64f, 0.40f, 0.24f);
        static readonly Color HeightHigh = new Color(0.94f, 0.82f, 0.64f);

        static readonly Color ReturnNear = new Color(0.55f, 0.95f, 0.62f);
        static readonly Color ReturnFar = new Color(0.08f, 0.28f, 0.30f);
        static readonly Color32 ReturnRing = new Color32(245, 245, 245, 255);
        static readonly Color32 NoReturn = new Color32(70, 70, 74, 255);
        static readonly Color32 NoReturnHatch = new Color32(140, 140, 146, 255);
        static readonly Color32 HabitatFill = new Color32(255, 255, 255, 255);
        static readonly Color32 HabitatEdge = new Color32(20, 20, 24, 255);

        /// <summary>Slope in degrees at every node from central differences (one-sided at the edges).</summary>
        public static float[] SlopeDegrees(float[] heights, int n, float spacing)
        {
            var slope = new float[n * n];
            for (int r = 0; r < n; r++)
            {
                int r0 = Mathf.Max(r - 1, 0), r1 = Mathf.Min(r + 1, n - 1);
                for (int c = 0; c < n; c++)
                {
                    int c0 = Mathf.Max(c - 1, 0), c1 = Mathf.Min(c + 1, n - 1);
                    float dzdx = (heights[r * n + c1] - heights[r * n + c0]) / ((c1 - c0) * spacing);
                    float dzdy = (heights[r0 * n + c] - heights[r1 * n + c]) / ((r1 - r0) * spacing);
                    slope[r * n + c] = Mathf.Atan(Mathf.Sqrt(dzdx * dzdx + dzdy * dzdy)) * Mathf.Rad2Deg;
                }
            }
            return slope;
        }

        /// <summary>The grid value under every paint pixel (bilinear). Result row 0 = south.</summary>
        public static float[] SampleField(float[] grid, int n, int size)
        {
            var field = new float[size * size];
            float scale = (n - 1) / (float)size;
            for (int py = 0; py < size; py++)
            {
                // Pixel center -> grid row (grid row 0 is the north edge = top of the texture).
                float gr = (size - 1 - py + 0.5f) * scale;
                int r0 = Mathf.Min((int)gr, n - 1), r1 = Mathf.Min(r0 + 1, n - 1);
                float fr = gr - r0;
                for (int px = 0; px < size; px++)
                {
                    float gc = (px + 0.5f) * scale;
                    int c0 = Mathf.Min((int)gc, n - 1), c1 = Mathf.Min(c0 + 1, n - 1);
                    float fc = gc - c0;
                    float a = grid[r0 * n + c0], b = grid[r0 * n + c1], d = grid[r1 * n + c0], e = grid[r1 * n + c1];
                    if (float.IsInfinity(a) || float.IsInfinity(b) || float.IsInfinity(d) || float.IsInfinity(e))
                    {
                        // Unreachable nodes must not be smeared into their neighbors.
                        field[py * size + px] = (fc < 0.5f ? (fr < 0.5f ? a : d) : (fr < 0.5f ? b : e));
                        continue;
                    }
                    field[py * size + px] = Mathf.Lerp(Mathf.Lerp(a, b, fc), Mathf.Lerp(d, e, fc), fr);
                }
            }
            return field;
        }

        public static Color32[] PaintHeight(float[] heights, int n, float min, float max, int size)
        {
            float[] field = SampleField(heights, n, size);
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                float k = Mathf.InverseLerp(min, max, field[i]);
                Color c = k < 0.5f ? Color.Lerp(HeightLow, HeightMid, k * 2f) : Color.Lerp(HeightMid, HeightHigh, k * 2f - 1f);
                pixels[i] = c;
            }
            DrawBandLines(field, size, ContourMeters, pixels, 0.62f, null);
            return pixels;
        }

        public static Color32[] PaintSlope(float[] slope, int n, int size)
        {
            float[] field = SampleField(slope, n, size);
            var pixels = new Color32[size * size];
            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    int i = py * size + px;
                    pixels[i] = SlopeColor(field[i], px, py);
                }
            }
            return pixels;
        }

        /// <summary>The color of one slope (degrees) at one pixel; over the limit, diagonal hatching.</summary>
        public static Color32 SlopeColor(float degrees, int px, int py)
        {
            if (degrees < GentleSlopeDegrees) return SlopeGentle;
            if (degrees <= SlopeLimitDegrees) return SlopeCareful;
            return (px + py) % HatchPeriod < HatchWidth ? SlopeNoGoHatch : SlopeNoGo;
        }

        /// <summary>
        /// Minutes to walk back to the habitat. Brighter = closer; a white ring every 5 minutes;
        /// nodes with no way back (infinity) in hatched gray; the habitat as a white dot.
        /// </summary>
        public static Color32[] PaintReturn(float[] minutes, int n, int size, Vector2 habitatUV)
        {
            float[] field = SampleField(minutes, n, size);
            float worst = 0f;
            foreach (float m in minutes) if (!float.IsInfinity(m) && m > worst) worst = m;
            if (worst <= 0f) worst = 1f;

            var pixels = new Color32[size * size];
            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    int i = py * size + px;
                    float m = field[i];
                    if (float.IsInfinity(m) || float.IsNaN(m))
                        pixels[i] = (px - py + 4 * size) % HatchPeriod < HatchWidth ? NoReturnHatch : NoReturn;
                    else
                        pixels[i] = Color.Lerp(ReturnNear, ReturnFar, Mathf.Sqrt(Mathf.Clamp01(m / worst)));
                }
            }
            DrawBandLines(field, size, ReturnRingMinutes, pixels, 0f, ReturnRing);

            // The habitat: a white dot with a dark edge, 12 px (about 12 mm on the table).
            int hx = Mathf.RoundToInt(habitatUV.x * size), hy = Mathf.RoundToInt(habitatUV.y * size);
            for (int dy = -14; dy <= 14; dy++)
            {
                for (int dx = -14; dx <= 14; dx++)
                {
                    int x = hx + dx, y = hy + dy;
                    if (x < 0 || y < 0 || x >= size || y >= size) continue;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d <= 10f) pixels[y * size + x] = HabitatFill;
                    else if (d <= 14f) pixels[y * size + x] = HabitatEdge;
                }
            }
            return pixels;
        }

        /// <summary>
        /// A line wherever the value crosses a multiple of <paramref name="every"/>: the pixel is
        /// darkened by <paramref name="darken"/>, or painted <paramref name="lineColor"/> if given.
        /// </summary>
        static void DrawBandLines(float[] field, int size, float every, Color32[] pixels, float darken, Color32? lineColor)
        {
            var line = new bool[size * size];
            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    int i = py * size + px;
                    float v = field[i];
                    if (float.IsInfinity(v) || float.IsNaN(v)) continue;
                    int band = Mathf.FloorToInt(v / every);
                    bool cross = false;
                    if (px + 1 < size) cross |= Differs(band, field[i + 1], every);
                    if (py + 1 < size) cross |= Differs(band, field[i + size], every);
                    if (cross) { line[i] = true; if (px + 1 < size) line[i + 1] = true; }
                }
            }
            for (int i = 0; i < pixels.Length; i++)
            {
                if (!line[i]) continue;
                if (lineColor.HasValue) { pixels[i] = lineColor.Value; continue; }
                Color32 p = pixels[i];
                pixels[i] = new Color32((byte)(p.r * darken), (byte)(p.g * darken), (byte)(p.b * darken), 255);
            }
        }

        static bool Differs(int band, float neighbor, float every)
        {
            if (float.IsInfinity(neighbor) || float.IsNaN(neighbor)) return false;
            return Mathf.FloorToInt(neighbor / every) != band;
        }

        public static string Hex(Color32 c)
        {
            return "#" + c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2");
        }
    }
}
