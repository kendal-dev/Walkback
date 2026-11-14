// Written with Claude Code (AI), directed and tested by team MARSXR
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace Walkback
{
    /// <summary>site.json, exactly as session B writes it (data contract, guion §22.7).</summary>
    [Serializable]
    public class SiteInfo
    {
        public string name;
        public double centerLat;
        public double centerLon;
        public float sideMeters;
        public int heightmapSize;
        public int gridSize;
        public float minElevation;
        public float maxElevation;
        public SitePoint habitat;
        public string crs;
        public SiteSource[] sources;
    }

    [Serializable]
    public class SitePoint
    {
        public float x;
        public float y;
    }

    /// <summary>One per layer: where the data comes from. The legend shows it; the jury reads it.</summary>
    [Serializable]
    public class SiteSource
    {
        public string layer;
        public string mission;
        public string instrument;
        public string resolution;
        public string id;
        public string credit;
    }

    /// <summary>stops.json: one stop of the mission (data contract, guion §22.7). x, y in local meters.</summary>
    [Serializable]
    public class StopInfo
    {
        public string id;
        public string name;
        public float x;
        public float y;
        /// <summary>Under 20 words, in English.</summary>
        public string science;
        public StopTask[] tasks;
        public string risk;
        public string photoCredit;
        /// <summary>Panorama id: the photo is StreamingAssets/Site/panorama_[id].jpg (may be empty).</summary>
        public string panorama;
    }

    [Serializable]
    public class StopTask
    {
        public string name;
        public float minutes;
    }

    [Serializable]
    class StopList
    {
        public StopInfo[] stops;
    }

    /// <summary>
    /// The square kilometer the app works on: heights on the 257 x 257 grid (row 0 = north,
    /// x east, y north, meters from the south-west corner) plus where they come from.
    /// </summary>
    public class SiteData
    {
        public SiteInfo Info;
        /// <summary>Heights in meters, index = row * GridSize + column, row 0 = north.</summary>
        public float[] Heights;
        public int GridSize;
        public float SideMeters;
        public float MinElevation;
        public float MaxElevation;
        /// <summary>False while the synthetic test terrain stands in for session B's package.</summary>
        public bool IsMarsData;
        /// <summary>Why the real package was not used (empty when it was).</summary>
        public string FallbackReason = "";
        /// <summary>The stops of the mission, from stops.json, or two TEST STOPs while it is missing.</summary>
        public StopInfo[] Stops = new StopInfo[0];
        /// <summary>False while the stops are the labelled test stand-ins.</summary>
        public bool StopsAreReal;

        public float NodeSpacing { get { return SideMeters / (GridSize - 1); } }
        public Vector2 Habitat { get { return Info != null && Info.habitat != null ? new Vector2(Info.habitat.x, Info.habitat.y) : new Vector2(SideMeters / 2f, SideMeters / 2f); } }

        /// <summary>Local meters of node (column, row): x = c * spacing, y = side - r * spacing.</summary>
        public Vector2 NodeToMeters(int column, int row)
        {
            return new Vector2(column * NodeSpacing, SideMeters - row * NodeSpacing);
        }

        /// <summary>The source of one layer by name ("elevation", "ortho"...), or null.</summary>
        public SiteSource Source(string layer)
        {
            if (Info == null || Info.sources == null) return null;
            foreach (var s in Info.sources)
                if (s != null && string.Equals(s.layer, layer, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        /// <summary>The elevation source: the one named "elevation", "dem", "height" or "heightmap", else the first.</summary>
        public SiteSource ElevationSource
        {
            get
            {
                foreach (string name in new[] { "elevation", "dem", "height", "heightmap", "terrain" })
                {
                    var s = Source(name);
                    if (s != null) return s;
                }
                return Info != null && Info.sources != null && Info.sources.Length > 0 ? Info.sources[0] : null;
            }
        }
    }

    /// <summary>
    /// Reads Assets/StreamingAssets/Site/ (session B). When the package is not there yet,
    /// returns the synthetic test terrain and says so: nothing invented is shown as Mars data.
    /// </summary>
    public static class SiteLoader
    {
        public const string SiteFolder = "Site";
        public const int ContractGridSize = 257;
        public const int ContractHeightmapSize = 513;
        public const float ContractSideMeters = 1000f;

        public static SiteData Load()
        {
            SiteData data = null;
            string why = "";
            try
            {
                byte[] json = ReadStreamingAsset(SiteFolder + "/site.json");
                byte[] r16 = json != null ? ReadStreamingAsset(SiteFolder + "/heightmap.r16") : null;
                if (json == null) why = "no site.json in StreamingAssets/Site yet";
                else if (r16 == null) why = "site.json found but heightmap.r16 is missing";
                else data = FromPackage(System.Text.Encoding.UTF8.GetString(json), r16);
            }
            catch (Exception e)
            {
                why = "the site package could not be read: " + e.Message;
            }
            if (data == null)
            {
                WalkbackLog.Warn("Using the synthetic test terrain: " + why);
                data = Synthetic();
                data.FallbackReason = why;
            }
            LoadStops(data);
            return data;
        }

        /// <summary>Reads stops.json (a JSON list, or {"stops": [...]}); without it, two labelled TEST STOPs.</summary>
        public static void LoadStops(SiteData data)
        {
            try
            {
                byte[] bytes = ReadStreamingAsset(SiteFolder + "/stops.json");
                if (bytes != null)
                {
                    data.Stops = ParseStops(System.Text.Encoding.UTF8.GetString(bytes));
                    data.StopsAreReal = data.Stops.Length > 0;
                    if (data.StopsAreReal)
                    {
                        WalkbackLog.Info("Stops loaded: " + data.Stops.Length);
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                WalkbackLog.Warn("stops.json could not be read: " + e.Message);
            }
            data.Stops = TestStops();
            data.StopsAreReal = false;
        }

        public static StopInfo[] ParseStops(string json)
        {
            json = json.Trim();
            if (json.StartsWith("[")) json = "{\"stops\":" + json + "}";
            json = Regex.Replace(json, "\"(id|panorama)\"\\s*:\\s*(-?[0-9][0-9.eE+-]*)", "\"$1\": \"$2\"");
            var list = JsonUtility.FromJson<StopList>(json);
            return list != null && list.stops != null ? list.stops : new StopInfo[0];
        }

        /// <summary>TODO(H1): stand-ins until session B's stops.json lands. Not real places; the name says so.</summary>
        public static StopInfo[] TestStops()
        {
            return new[]
            {
                new StopInfo { id = "test1", name = "TEST STOP 1", x = 720f, y = 760f, science = "Test stop on the synthetic terrain, not a real place.",
                    tasks = new[] { new StopTask { name = "Test task", minutes = 20f } }, risk = "", photoCredit = "", panorama = "" },
                new StopInfo { id = "test2", name = "TEST STOP 2", x = 430f, y = 180f, science = "Test stop on the synthetic terrain, not a real place.",
                    tasks = new[] { new StopTask { name = "Test task", minutes = 15f } }, risk = "", photoCredit = "", panorama = "" },
            };
        }

        /// <summary>Builds the site from the two files of the contract. Throws if they break it.</summary>
        public static SiteData FromPackage(string siteJson, byte[] heightmap)
        {
            var info = ParseSiteJson(siteJson);
            int hm = info.heightmapSize > 0 ? info.heightmapSize : ContractHeightmapSize;
            int grid = info.gridSize > 0 ? info.gridSize : ContractGridSize;
            if (heightmap.Length != hm * hm * 2)
                throw new InvalidDataException("heightmap.r16 is " + heightmap.Length + " bytes, expected " + (hm * hm * 2) + " (" + hm + " x " + hm + " x 2)");
            if (!(info.maxElevation > info.minElevation))
                throw new InvalidDataException("site.json: maxElevation must be above minElevation");

            float[] full = DecodeR16(heightmap, hm, info.minElevation, info.maxElevation);
            var data = new SiteData
            {
                Info = info,
                GridSize = grid,
                SideMeters = info.sideMeters > 0f ? info.sideMeters : ContractSideMeters,
                Heights = Resample(full, hm, grid),
                IsMarsData = true,
            };
            MinMax(data.Heights, out data.MinElevation, out data.MaxElevation);
            WalkbackLog.Info("Site loaded: " + info.name + ", " + hm + " px heightmap -> " + grid + " nodes, " + data.MinElevation.ToString("F1") + " to " + data.MaxElevation.ToString("F1") + " m");
            return data;
        }

        public static SiteInfo ParseSiteJson(string json)
        {
            // JsonUtility cannot put a number into a string field: quote these if B wrote numbers.
            json = Regex.Replace(json, "\"(resolution|id)\"\\s*:\\s*(-?[0-9][0-9.eE+-]*)", "\"$1\": \"$2\"");
            var info = JsonUtility.FromJson<SiteInfo>(json);
            if (info == null) throw new InvalidDataException("site.json is empty or not JSON");
            return info;
        }

        /// <summary>
        /// 16-bit little-endian, first row = north; height = min + v / 65535 * (max - min).
        /// Index of the result = row * size + column.
        /// </summary>
        public static float[] DecodeR16(byte[] bytes, int size, float min, float max)
        {
            var heights = new float[size * size];
            float span = max - min;
            for (int i = 0; i < heights.Length; i++)
            {
                int v = bytes[2 * i] | (bytes[2 * i + 1] << 8);
                heights[i] = min + v / 65535f * span;
            }
            return heights;
        }

        /// <summary>From the heightmap (513) to the grid (257): node (c, r) = pixel (2c, 2r), bilinear if not exact.</summary>
        public static float[] Resample(float[] source, int sourceSize, int gridSize)
        {
            if (sourceSize == gridSize) return (float[])source.Clone();
            var result = new float[gridSize * gridSize];
            float scale = (sourceSize - 1) / (float)(gridSize - 1);
            for (int r = 0; r < gridSize; r++)
            {
                float sy = r * scale;
                int y0 = Mathf.Min((int)sy, sourceSize - 1), y1 = Mathf.Min(y0 + 1, sourceSize - 1);
                float fy = sy - y0;
                for (int c = 0; c < gridSize; c++)
                {
                    float sx = c * scale;
                    int x0 = Mathf.Min((int)sx, sourceSize - 1), x1 = Mathf.Min(x0 + 1, sourceSize - 1);
                    float fx = sx - x0;
                    float top = Mathf.Lerp(source[y0 * sourceSize + x0], source[y0 * sourceSize + x1], fx);
                    float bottom = Mathf.Lerp(source[y1 * sourceSize + x0], source[y1 * sourceSize + x1], fx);
                    result[r * gridSize + c] = Mathf.Lerp(top, bottom, fy);
                }
            }
            return result;
        }

        /// <summary>
        /// TODO(H1): the stand-in until session B's package lands. Same shape as the contract
        /// (257 x 257 nodes over 1 km). NOT Mars data; the panel says so while it is on screen.
        /// </summary>
        public static SiteData Synthetic()
        {
            int n = ContractGridSize;
            float side = ContractSideMeters;
            float step = side / (n - 1);
            var data = new SiteData
            {
                Info = new SiteInfo
                {
                    name = "Test terrain",
                    sideMeters = side,
                    heightmapSize = ContractHeightmapSize,
                    gridSize = n,
                    habitat = new SitePoint { x = 250f, y = 500f },
                    crs = "local meters (synthetic)",
                    sources = new SiteSource[0],
                },
                GridSize = n,
                SideMeters = side,
                Heights = new float[n * n],
                IsMarsData = false,
            };
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    data.Heights[r * n + c] = SyntheticHeight(c * step, side - r * step);
            MinMax(data.Heights, out data.MinElevation, out data.MaxElevation);
            data.Info.minElevation = data.MinElevation;
            data.Info.maxElevation = data.MaxElevation;
            return data;
        }

        /// <summary>Synthetic height in meters at local (x east, y north), both 0..1000.</summary>
        public static float SyntheticHeight(float x, float y)
        {
            float h = 40f * Mathf.Sin(x / 260f) * Mathf.Cos(y / 310f);
            h += 25f * Mathf.Sin((x + y) / 90f);
            // A north-south ridge with a pass in the middle.
            float ridge = 60f * Mathf.Exp(-Sq(x - 650f) / (2f * Sq(60f)));
            float pass = 1f - 0.8f * Mathf.Exp(-Sq(y - 500f) / (2f * Sq(70f)));
            h += ridge * pass;
            // A shallow bowl.
            h -= 30f * Mathf.Exp(-(Sq(x - 300f) + Sq(y - 300f)) / (2f * Sq(80f)));
            return h;
        }

        /// <summary>
        /// Bytes of a file under StreamingAssets (e.g. "Site/panorama_88.jpg"), or null if it is not there.
        /// StreamingAssets is inside the APK on the Quest: read with a web request there.
        /// </summary>
        public static byte[] ReadStreamingAsset(string relative)
        {
            string path = Application.streamingAssetsPath + "/" + relative;
            if (path.Contains("://"))
            {
                using (var request = UnityWebRequest.Get(path))
                {
                    var operation = request.SendWebRequest();
                    var watch = Stopwatch.StartNew();
                    while (!operation.isDone)
                    {
                        if (watch.ElapsedMilliseconds > 10000) throw new TimeoutException("reading " + relative + " took more than 10 s");
                        Thread.Sleep(1);
                    }
                    if (request.result != UnityWebRequest.Result.Success) return null;
                    return request.downloadHandler.data;
                }
            }
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        static void MinMax(float[] values, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            foreach (float v in values)
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }

        static float Sq(float v) { return v * v; }
    }
}
