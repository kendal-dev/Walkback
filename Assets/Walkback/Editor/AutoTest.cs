// Written with Claude Code (AI), directed and tested by team MARSXR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Walkback.EditorTools
{
    /// <summary>
    /// The test without the headset: builds the scene, checks it and saves screenshots
    /// to TestOutput/. Three ways to run it:
    ///   1. Unity closed:  Unity.exe -batchmode -projectPath . -executeMethod Walkback.EditorTools.AutoTest.RunBatch -logFile TestOutput/unity.log
    ///   2. Unity open:    menu Walkback > Run Auto Test
    ///   3. Unity open:    create the file Temp/walkback-run-test (the editor picks it up by itself)
    /// </summary>
    [InitializeOnLoad]
    public static partial class AutoTest
    {
        const string OutputFolder = "TestOutput";
        const string TriggerFile = "Temp/walkback-run-test";

        static readonly List<string> lines = new List<string>();
        static int passed, failed;
        static double nextTriggerCheck;
        static bool refreshedForTrigger;

        // Each piece adds its checks and screenshots from its own file (AutoTest.<Piece>.cs).
        // Screenshots of a piece are named "<piece>_NN_<what>.png". An unimplemented hook compiles to nothing.
        static partial void CheckRoute(WalkbackApp app);
        static partial void CheckPanelPiece(WalkbackApp app);
        static partial void CheckGlobe(WalkbackApp app);
        static partial void CheckPointer(WalkbackApp app);
        static partial void ShotsRoute(WalkbackApp app);
        static partial void ShotsPanelPiece(WalkbackApp app);
        static partial void ShotsGlobe(WalkbackApp app);
        static partial void ShotsPointer(WalkbackApp app);
        static partial void CheckHud(WalkbackApp app);
        static partial void ShotsHud(WalkbackApp app);
        static partial void CheckMission(WalkbackApp app);
        static partial void ShotsMission(WalkbackApp app);

        static AutoTest()
        {
            EditorApplication.update += WatchTrigger;
        }

        static void WatchTrigger()
        {
            if (EditorApplication.timeSinceStartup < nextTriggerCheck) return;
            nextTriggerCheck = EditorApplication.timeSinceStartup + 1.0;
            if (!File.Exists(TriggerFile)) { refreshedForTrigger = false; return; }
            // Import and compile what changed first; otherwise the test can run the old scripts.
            if (!refreshedForTrigger)
            {
                refreshedForTrigger = true;
                AssetDatabase.Refresh();
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(TriggerFile);
            refreshedForTrigger = false;
            Run();
        }

        /// <summary>Runs one piece's checks; an exception fails that piece only.</summary>
        static void Guard(string piece, Action action)
        {
            try { action(); }
            catch (Exception e) { Check(piece + ": no exception", false, e.GetType().Name + ": " + e.Message + " @ " + FirstFrame(e)); }
        }

        static string FirstFrame(Exception e)
        {
            string trace = e.StackTrace ?? "";
            int end = trace.IndexOf('\n');
            return (end > 0 ? trace.Substring(0, end) : trace).Trim();
        }

        public static void RunBatch()
        {
            bool ok = Run();
            EditorApplication.Exit(ok ? 0 : 1);
        }

        [MenuItem("Walkback/Run Auto Test")]
        public static void RunFromMenu()
        {
            Run();
        }

        public static bool Run()
        {
            lines.Clear();
            passed = failed = 0;
            Directory.CreateDirectory(OutputFolder);
            string reportPath = Path.Combine(OutputFolder, "report.txt");
            var started = DateTime.Now;
            Check("Scripts compile (if not, this report tests the old scripts)", !EditorUtility.scriptCompilationFailed);
            try
            {
                WalkbackApp app = SceneBuilder.Build();
                // v1.1: the verdict's note reads the time at Gale (night / after sunset). The test runs at a fixed
                // Gale morning (08:00 LMST), so its results do not depend on when it runs.
                DateTime galeMorning = FindLocal(new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc), 137.381, 8.0);
                app.ClockUtc = () => galeMorning;
                CheckScene(app);
                Guard("Route", () => CheckRoute(app));
                Guard("Panel", () => CheckPanelPiece(app));
                Guard("Globe", () => CheckGlobe(app));
                Guard("Pointer", () => CheckPointer(app));
                Guard("HUD", () => CheckHud(app));
                Guard("Mission", () => CheckMission(app));
                Screenshots(app);
                Guard("Route shots", () => ShotsRoute(app));
                Guard("Panel shots", () => ShotsPanelPiece(app));
                Guard("Globe shots", () => ShotsGlobe(app));
                Guard("Pointer shots", () => ShotsPointer(app));
                Guard("HUD shots", () => ShotsHud(app));
                Guard("Mission shots", () => ShotsMission(app));
            }
            catch (Exception e)
            {
                Check("No exception during the test", false, e.ToString());
            }

            var report = new StringBuilder();
            report.AppendLine("Walkback automatic test " + started.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine((failed == 0 ? "RESULT: PASS" : "RESULT: FAIL") + " - " + passed + " passed, " + failed + " failed, " + (DateTime.Now - started).TotalSeconds.ToString("F1") + " s");
            foreach (string line in lines) report.AppendLine(line);
            File.WriteAllText(reportPath, report.ToString());
            Debug.Log("[Walkback] " + report);
            return failed == 0;
        }

        static void CheckScene(WalkbackApp app)
        {
            Check("App root exists", app != null);
            Check("Scene saved", File.Exists(SceneBuilder.ScenePath), SceneBuilder.ScenePath);
            Check("Scene is in the build list", EditorBuildSettings.scenes.Length == 1 && EditorBuildSettings.scenes[0].path == SceneBuilder.ScenePath);

            // Meta rig and passthrough
            Check("Camera rig present", app.cameraRig != null && app.cameraRig.GetComponent<OVRCameraRig>() != null);
            var manager = app.cameraRig.GetComponent<OVRManager>();
            Check("Passthrough enabled on the manager", manager != null && manager.isInsightPassthroughEnabled);
            Check("Passthrough layer present", app.cameraRig.GetComponent<OVRPassthroughLayer>() != null);
            Check("Passthrough declared in the project config", OVRProjectConfig.CachedProjectConfig.insightPassthroughSupport != OVRProjectConfig.FeatureSupport.None);
            int floor = new SerializedObject(manager).FindProperty("_trackingOriginType").intValue;
            Check("Tracking origin is the floor", floor == (int)OVRManager.TrackingOrigin.FloorLevel);
            bool eyesClear = true;
            int eyes = 0;
            foreach (var eye in app.cameraRig.GetComponentsInChildren<Camera>(true))
            {
                eyes++;
                if (eye.clearFlags != CameraClearFlags.SolidColor || eye.backgroundColor.a > 0.001f) eyesClear = false;
            }
            Check("Headset cameras clear to transparent", eyes > 0 && eyesClear, eyes + " cameras");

            // Table and map
            Check("Table present", app.table != null);
            var tableBounds = app.table.GetComponent<Renderer>().bounds;
            Check("Table top at 0.75 m", Mathf.Abs(tableBounds.max.y - SceneBuilder.TableHeight) < 0.005f, tableBounds.max.y.ToString("F3") + " m");
            var terrain = app.terrain;
            Check("Terrain present and wired to the app", terrain != null && terrain == app.mapRoot.GetComponentInChildren<GaleTerrain>());
            terrain.Build();
            app.RefreshPanel();
            var site = terrain.Site;
            Note(site.IsMarsData
                ? "DATA: site package from session B: " + site.Info.name
                : "DATA: synthetic test terrain (" + site.FallbackReason + ")");
            Check("Site grid is 257 x 257 over 1000 m (contract)", site.GridSize == 257 && Mathf.Approximately(site.SideMeters, 1000f) && site.Heights.Length == 257 * 257,
                site.GridSize + " nodes, " + site.SideMeters + " m, spacing " + site.NodeSpacing.ToString("F3") + " m");
            var mesh = terrain.GetComponent<MeshFilter>().sharedMesh;
            Check("Terrain mesh is 257 x 257", mesh != null && mesh.vertexCount == 257 * 257, mesh != null ? mesh.vertexCount + " vertices" : "no mesh");
            Check("Terrain mesh has 131072 triangles", mesh != null && mesh.triangles.Length / 3 == 256 * 256 * 2);
            var mapBounds = terrain.GetComponent<Renderer>().bounds;
            Check("Map is 1 m wide on the table", Mathf.Abs(mapBounds.size.x - SceneBuilder.MapSideOnTable) < 0.005f && Mathf.Abs(mapBounds.size.z - SceneBuilder.MapSideOnTable) < 0.005f, mapBounds.size.x.ToString("F3") + " x " + mapBounds.size.z.ToString("F3") + " m");
            Check("Map sits on the table, not inside it", mapBounds.min.y >= tableBounds.max.y, "map bottom " + mapBounds.min.y.ToString("F3") + " m");
            Check("Map fits on the table", new Vector2(mapBounds.extents.x, mapBounds.extents.z).magnitude <= SceneBuilder.TableDiameter / 2f + 0.001f);
            float relief = site.MaxElevation - site.MinElevation;
            Check("Relief is not exaggerated", Mathf.Abs(mapBounds.size.y - relief * SceneBuilder.MapSideOnTable / site.SideMeters) < 0.002f, relief.ToString("F1") + " m of relief -> " + mapBounds.size.y.ToString("F3") + " m on the table");
            Vector3[] vertices = mesh.vertices;
            Check("North is +z (row 0 is the north edge)", vertices[0].z > vertices[256 * 257].z && Mathf.Abs(vertices[0].y - (site.Heights[0] - site.MinElevation)) < 0.001f,
                "row 0 at z = " + vertices[0].z.ToString("F0") + " m");
            Check("East is +x (column 0 is the west edge)", vertices[0].x < vertices[256].x);
            if (!site.IsMarsData)
                Check("Synthetic heights follow the contract layout", Mathf.Approximately(SiteLoader.SyntheticHeight(0f, 1000f), site.Heights[0]) && Mathf.Approximately(SiteLoader.SyntheticHeight(1000f, 0f), site.Heights[257 * 257 - 1]));
            Check("Normals point up", mesh != null && mesh.normals[257 * 128 + 128].y > 0.5f);

            CheckSitePackage(app, site);
            CheckDataContract();
            CheckLayers(app, terrain);

            // Panel
            Check("Panel present", app.panel != null);
            var canvasRect = (RectTransform)app.panel.GetComponentInChildren<Canvas>().transform;
            float width = canvasRect.sizeDelta.x * canvasRect.lossyScale.x, height = canvasRect.sizeDelta.y * canvasRect.lossyScale.y;
            Check("Panel is 1.50 m x 1.00 m", Mathf.Abs(width - WalkbackTheme.PanelWidthMeters) < 0.001f && Mathf.Abs(height - WalkbackTheme.PanelHeightMeters) < 0.001f, width.ToString("F2") + " x " + height.ToString("F2") + " m");
            float titleMeters = app.panel.title.fontSize * canvasRect.lossyScale.y;
            Check("Title is at least 0.05 m tall", titleMeters >= WalkbackTheme.MinTitleHeightMeters, titleMeters.ToString("F3") + " m");
            float verdictMeters = app.panel.verdictTitle.fontSize * canvasRect.lossyScale.y;
            Check("The verdict is the biggest text", verdictMeters > titleMeters, verdictMeters.ToString("F3") + " m");
            bool allText = true, allMaterial = true;
            foreach (var text in app.panel.GetComponentsInChildren<Text>(true))
            {
                if (string.IsNullOrEmpty(text.text) || text.font == null) allText = false;
                if (text.material == null || text.material.shader.name != "Walkback/UI") allMaterial = false;
            }
            Check("Every panel text has content and a font", allText);
            Check("Panel text never writes alpha (no holes in passthrough)", allMaterial);
            float distance = Vector3.Distance(new Vector3(0f, 1.6f, 0f), app.panel.transform.position);
            Check("Panel is 1.6 to 2.2 m from the user", distance >= 1.6f && distance <= 2.2f, distance.ToString("F2") + " m");

            // Desktop mode
            Check("Desktop camera present and off by default", app.desktopCamera != null && !app.desktopCamera.enabled);
        }

        /// <summary>Real package: every contract field and every source named. Synthetic: labelled as such.</summary>
        static void CheckSitePackage(WalkbackApp app, SiteData site)
        {
            string line = app.panel.dataLine.text;
            if (!site.IsMarsData)
            {
                Check("Test terrain is labelled as not Mars data", line.Contains("not Mars data"));
                return;
            }
            var info = site.Info;
            Check("site.json: name, size and elevation range", !string.IsNullOrEmpty(info.name) && info.heightmapSize == 513 && info.gridSize == 257 && info.maxElevation > info.minElevation,
                info.name + ", " + info.heightmapSize + " px, " + info.minElevation + " to " + info.maxElevation + " m");
            Check("site.json: habitat inside the square kilometer", info.habitat != null && info.habitat.x >= 0f && info.habitat.x <= site.SideMeters && info.habitat.y >= 0f && info.habitat.y <= site.SideMeters,
                info.habitat != null ? "x " + info.habitat.x + " m, y " + info.habitat.y + " m" : "no habitat");
            bool allNamed = info.sources != null && info.sources.Length > 0;
            if (info.sources != null)
                foreach (var s in info.sources)
                    if (s == null || string.IsNullOrEmpty(s.layer) || string.IsNullOrEmpty(s.mission) || string.IsNullOrEmpty(s.instrument) || string.IsNullOrEmpty(s.resolution) || string.IsNullOrEmpty(s.credit)) allNamed = false;
            Check("site.json: every source names layer, mission, instrument, resolution and credit", allNamed, (info.sources != null ? info.sources.Length : 0) + " sources");
            var elevation = site.ElevationSource;
            Check("Panel names the mission, instrument and resolution of the heights", elevation != null && line.Contains(elevation.mission) && line.Contains(elevation.instrument) && line.Contains(elevation.resolution), line);
            Check("Real data carries no test-terrain label", !line.Contains("not Mars data") && !line.Contains("TEST TERRAIN"));
            Check("Heights stay inside the site.json range", site.MinElevation >= info.minElevation - 0.01f && site.MaxElevation <= info.maxElevation + 0.01f);
        }

        /// <summary>The contract arithmetic on hand-made inputs, whatever data is on the table.</summary>
        static void CheckDataContract()
        {
            // heightmap.r16: little-endian, first row north, height = min + v / 65535 * (max - min)
            var bytes = new byte[3 * 3 * 2];
            bytes[2 * 1] = 0x02; bytes[2 * 1 + 1] = 0x01;          // pixel (1, 0) = 0x0102 = 258
            bytes[2 * 2] = 0xFF; bytes[2 * 2 + 1] = 0xFF;          // pixel (2, 0) = 65535
            float[] decoded = SiteLoader.DecodeR16(bytes, 3, -4500f, -4400f);
            Check("r16: 0 -> min, 65535 -> max", Mathf.Approximately(decoded[0], -4500f) && Mathf.Approximately(decoded[2], -4400f));
            Check("r16: little-endian byte order", Mathf.Abs(decoded[1] - (-4500f + 258f / 65535f * 100f)) < 0.0005f, decoded[1].ToString("F4") + " m");

            // 513 -> 257: node (c, r) is pixel (2c, 2r)
            var field = new float[513 * 513];
            for (int y = 0; y < 513; y++) for (int x = 0; x < 513; x++) field[y * 513 + x] = x + 1000f * y;
            float[] grid = SiteLoader.Resample(field, 513, 257);
            Check("Heightmap 513 -> grid 257: node (c, r) = pixel (2c, 2r)", Mathf.Approximately(grid[50 * 257 + 100], 200f + 1000f * 100f) && Mathf.Approximately(grid[257 * 257 - 1], 512f + 1000f * 512f));

            // site.json with numbers where strings are expected
            var parsed = SiteLoader.ParseSiteJson("{\"name\":\"T\",\"habitat\":{\"x\":12.5,\"y\":40},\"sources\":[{\"layer\":\"elevation\",\"mission\":\"MRO\",\"resolution\":1.0,\"id\":42}]}");
            Check("site.json parser accepts numeric resolution and id", parsed.sources.Length == 1 && parsed.sources[0].resolution == "1.0" && parsed.sources[0].id == "42" && Mathf.Approximately(parsed.habitat.x, 12.5f));

            // Slope: a plane tilted 20° reads 20°, flat ground reads 0°
            int n = 9;
            float spacing = 1000f / 256f;
            var tilted = new float[n * n];
            var flat = new float[n * n];
            float rise = Mathf.Tan(20f * Mathf.Deg2Rad);
            for (int r = 0; r < n; r++) for (int c = 0; c < n; c++) tilted[r * n + c] = c * spacing * rise;
            float tiltedSlope = TerrainLayers.SlopeDegrees(tilted, n, spacing)[4 * n + 4];
            Check("Slope of a 20° plane is 20°", Mathf.Abs(tiltedSlope - 20f) < 0.01f, tiltedSlope.ToString("F3") + "°");
            Check("Slope of flat ground is 0°", TerrainLayers.SlopeDegrees(flat, n, spacing)[4 * n + 4] < 0.001f);
            // North-south tilt too (rows run south): heights rising to the north
            for (int r = 0; r < n; r++) for (int c = 0; c < n; c++) tilted[r * n + c] = (n - 1 - r) * spacing * rise;
            Check("Slope reads the north-south direction too", Mathf.Abs(TerrainLayers.SlopeDegrees(tilted, n, spacing)[4 * n + 4] - 20f) < 0.01f);

            // Slope bands
            Check("Slope 5° is blue, 15° orange, 25° vermilion",
                Same(TerrainLayers.SlopeColor(5f, 0, 0), TerrainLayers.SlopeGentle) &&
                Same(TerrainLayers.SlopeColor(15f, 0, 0), TerrainLayers.SlopeCareful) &&
                (Same(TerrainLayers.SlopeColor(25f, 7, 0), TerrainLayers.SlopeNoGo) || Same(TerrainLayers.SlopeColor(25f, 7, 0), TerrainLayers.SlopeNoGoHatch)));
            bool hatchA = false, hatchB = false;
            for (int px = 0; px < 14; px++)
            {
                var c = TerrainLayers.SlopeColor(25f, px, 0);
                if (Same(c, TerrainLayers.SlopeNoGo)) hatchA = true;
                if (Same(c, TerrainLayers.SlopeNoGoHatch)) hatchB = true;
            }
            Check("Over 20° is hatched (two shades alternate)", hatchA && hatchB);
            Check("Exactly 20° is still allowed (orange)", Same(TerrainLayers.SlopeColor(20f, 0, 0), TerrainLayers.SlopeCareful));

            // Return stand-in: 1 km on flat ground = 19.3 min (ficha A test number)
            var line = SiteLoader.Synthetic();
            line.Info.habitat = new SitePoint { x = 0f, y = 0f };
            float[] back = ReturnStandIn.Minutes(line);
            Check("Return stand-in: habitat is 0 min", back[256 * 257] < 0.001f);
            Check("Return stand-in: 1 km straight = 19.3 min", Mathf.Abs(back[256 * 257 + 256] - 19.3f) < 0.01f, back[256 * 257 + 256].ToString("F2") + " min");

            // Return fallback (plan B): fastest way back over the grid. Flat ground must give the same pace.
            int fn = line.GridSize;
            for (int i = 0; i < line.Heights.Length; i++) line.Heights[i] = 0f;
            var flatSlope = new float[fn * fn];
            float[] way = ReturnFallback.Minutes(line, flatSlope);
            int southWest = (fn - 1) * fn, southEast = (fn - 1) * fn + (fn - 1), northEast = fn - 1;
            Check("Return fallback: habitat is 0 min", way[southWest] < 0.001f);
            Check("Return fallback: 1 km straight on flat ground = 19.3 min", Mathf.Abs(way[southEast] - 19.3f) < 0.02f, way[southEast].ToString("F2") + " min");
            Check("Return fallback: the diagonal of the square = 1.414 km = 27.3 min", Mathf.Abs(way[northEast] - 19.3f * Mathf.Sqrt(2f)) < 0.03f, way[northEast].ToString("F2") + " min");
            Check("Return fallback: slope factor is 1 on flat ground and under 1 on a 20 % climb", Mathf.Abs(ReturnFallback.SlopeFactor(0f) - 1f) < 1e-5f && ReturnFallback.SlopeFactor(0.2f) < 0.6f && ReturnFallback.SlopeFactor(0.2f) > 0.4f,
                ReturnFallback.SlopeFactor(0.2f).ToString("F3"));
            // A no-go wall from north to south with one gap at the south edge: the far side is reached
            // only around it; with the gap closed, the far side has no way back (infinity).
            int wallColumn = fn / 2;
            for (int r = 0; r < fn - 1; r++) flatSlope[r * fn + wallColumn] = TerrainLayers.SlopeLimitDegrees + 5f;
            float[] around = ReturnFallback.Minutes(line, flatSlope);
            Check("Return fallback: a no-go wall is walked around, never through", around[northEast] > way[northEast] + 3f && !float.IsInfinity(around[northEast]) && float.IsInfinity(around[wallColumn]),
                way[northEast].ToString("F1") + " -> " + around[northEast].ToString("F1") + " min");
            flatSlope[(fn - 1) * fn + wallColumn] = TerrainLayers.SlopeLimitDegrees + 5f;
            float[] closed = ReturnFallback.Minutes(line, flatSlope);
            Check("Return fallback: ground cut off from the habitat has no way back", float.IsInfinity(closed[northEast]) && float.IsInfinity(closed[southEast]) && closed[southWest] < 0.001f);

            // Mission plan B on flat ground: 1 km out and 1 km back, 10 minutes of work at the stop.
            var mfSite = SiteLoader.Synthetic();
            for (int i = 0; i < mfSite.Heights.Length; i++) mfSite.Heights[i] = 0f;
            mfSite.Info.habitat = new SitePoint { x = 0f, y = 0f };
            mfSite.Stops = new[] { new StopInfo { id = "a", name = "A", x = 1000f, y = 0f, tasks = new[] { new StopTask { name = "t", minutes = 10f } } } };
            var mfSlope = new float[fn * fn];
            var mfPlan = MissionFallback.Make(mfSite, mfSlope, 100f);
            float mfLiters = 38.6f * HudEstimate.WalkLitersPerMinute + 10f * HudEstimate.DesignLitersPerMinute;
            Check("Mission plan B: 1 km out and back on flat ground = 2 km, 38.6 min of walking", mfPlan.HasRoute && Mathf.Abs(mfPlan.WalkMinutes - 38.6f) < 0.05f && Mathf.Abs(mfPlan.WalkMeters - 2000f) < 1f,
                mfPlan.WalkMeters.ToString("F0") + " m, " + mfPlan.WalkMinutes.ToString("F2") + " min");
            Check("Mission plan B: oxygen = minutes walked at the walking rate + work at the design rate", Mathf.Abs(mfPlan.UsedLiters - mfLiters) < 0.1f && Mathf.Approximately(mfPlan.WorkMinutes, 10f),
                mfPlan.UsedLiters.ToString("F2") + " L, expected " + mfLiters.ToString("F2") + " L");
            Check("Mission plan B: with a full tank it is VIABLE, and the route starts and ends at the habitat",
                mfPlan.Viable && mfPlan.Path.Count > 100 && mfPlan.Path[0] == mfSite.Habitat && mfPlan.Path[mfPlan.Path.Count - 1] == mfSite.Habitat, mfPlan.Path.Count + " points");
            var mfShort = MissionFallback.Make(mfSite, mfSlope, 8f);
            Check("Mission plan B: with 8 % of a tank it is NOT VIABLE already on arrival at the stop", !mfShort.Viable && mfShort.FailsAtStop == 0 && mfShort.FailsOnArrival, MissionFallback.LogLine(mfShort));
            var mfAfter = MissionFallback.Make(mfSite, mfSlope, 16f);
            Check("Mission plan B: with 16 % of a tank it reaches the stop but cannot do the work there", !mfAfter.Viable && mfAfter.FailsAtStop == 0 && !mfAfter.FailsOnArrival, MissionFallback.LogLine(mfAfter));
            VerdictState mfState;
            string mfHeadline, mfSummary, mfNote;
            MissionFallback.Describe(mfPlan, out mfState, out mfHeadline, out mfSummary, out mfNote);
            Check("Mission plan B: the verdict card gives time, oxygen and what is spare, labelled ESTIMATE", mfState == VerdictState.Viable && mfSummary.Contains("min") && mfSummary.Contains("O2 used") && mfSummary.Contains("spare") && mfNote.StartsWith("ESTIMATE"), mfSummary + " | " + mfNote);
            MissionFallback.Describe(mfShort, out mfState, out mfHeadline, out mfSummary, out mfNote);
            Check("Mission plan B: a WHAT IF says so on the card and names where the oxygen runs short", mfState == VerdictState.NotViable && mfNote.StartsWith("WHAT IF") && mfSummary.Contains("A"), mfSummary + " | " + mfNote);
            for (int r = 0; r < fn; r++) mfSlope[r * fn + fn / 2] = TerrainLayers.SlopeLimitDegrees + 5f;
            var mfWalled = MissionFallback.Make(mfSite, mfSlope, 100f);
            MissionFallback.Describe(mfWalled, out mfState, out mfHeadline, out mfSummary, out mfNote);
            Check("Mission plan B: a stop behind no-go ground gives NO SAFE ROUTE, with the leg named", !mfWalled.HasRoute && !mfWalled.Viable && mfWalled.BrokenLeg == "Habitat -> A" && mfHeadline == "NO SAFE ROUTE", mfSummary);
            Check("Mission plan B: a full tank covers hours of walking, 8 % only minutes", MissionFallback.ReachMinutes(100f) > 400f && MissionFallback.ReachMinutes(8f) > 5f && MissionFallback.ReachMinutes(8f) < 12f,
                MissionFallback.ReachMinutes(8f).ToString("F1") + " min with 8 %");

            // Return paint with a reach: ground farther than the oxygen left covers is red; without a reach, none is.
            int redWith = 0, redWithout = 0;
            foreach (var p in TerrainLayers.PaintReturn(back, 257, 64, Vector2.zero, 10f)) if (Same(p, TerrainLayers.TooFar) || Same(p, TerrainLayers.TooFarHatch)) redWith++;
            foreach (var p in TerrainLayers.PaintReturn(back, 257, 64, Vector2.zero)) if (Same(p, TerrainLayers.TooFar) || Same(p, TerrainLayers.TooFarHatch)) redWithout++;
            Check("Return paint: beyond the reach the ground is red, and only then", redWith > 64 * 64 / 2 && redWithout == 0, redWith + " red px of " + (64 * 64) + " with a 10 min reach");
        }

        static void CheckLayers(WalkbackApp app, GaleTerrain terrain)
        {
            var renderer = terrain.GetComponent<MeshRenderer>();
            var block = new MaterialPropertyBlock();
            var textures = new Texture2D[3];
            string[] names = { "HEIGHT", "SLOPE", "RETURN ON FOOT" };
            foreach (MapLayer layer in new[] { MapLayer.Height, MapLayer.Slope, MapLayer.Return })
            {
                terrain.ShowLayer(layer);
                app.RefreshPanel();
                renderer.GetPropertyBlock(block);
                textures[(int)layer] = terrain.LayerTexture(layer);
                Check("Layer " + layer + " is on the map", block.GetTexture("_BaseMap") == textures[(int)layer] && textures[(int)layer].width == TerrainLayers.PaintSize,
                    "painted in " + terrain.PaintMilliseconds[(int)layer] + " ms");
                Check("Layer " + layer + " has its legend on the panel", app.panel.legendLine.text.StartsWith(names[(int)layer]), app.panel.legendLine.text.Split('\n')[0]);
                Check("Layer " + layer + " paints in under 1.5 s", terrain.PaintMilliseconds[(int)layer] < 1500);
            }
            Check("The three layers are three different paints", textures[0] != textures[1] && textures[1] != textures[2] && textures[0] != textures[2]);

            // Slope bands on this terrain, and the hatching where it is too steep
            int gentle = 0, careful = 0, steep = 0;
            foreach (float s in terrain.Slope)
            {
                if (s < TerrainLayers.GentleSlopeDegrees) gentle++;
                else if (s <= TerrainLayers.SlopeLimitDegrees) careful++;
                else steep++;
            }
            float total = terrain.Slope.Length;
            Note("SLOPE on this terrain: " + (100f * gentle / total).ToString("F0") + " % under 10°, " + (100f * careful / total).ToString("F0") + " % 10-20°, " + (100f * steep / total).ToString("F0") + " % over 20°");
            Color32[] slopePixels = textures[(int)MapLayer.Slope].GetPixels32();
            int hatch = 0, vermilion = 0;
            foreach (var p in slopePixels)
            {
                if (Same(p, TerrainLayers.SlopeNoGoHatch)) hatch++;
                if (Same(p, TerrainLayers.SlopeNoGo)) vermilion++;
            }
            Check("Steep ground shows hatched on the slope paint", steep == 0 || (hatch > 0 && vermilion > 0), hatch + " hatch px, " + vermilion + " vermilion px");

            // Return: the habitat dot and the 5-minute rings
            Color32[] returnPixels = textures[(int)MapLayer.Return].GetPixels32();
            Vector2 h = terrain.Site.Habitat;
            int size = TerrainLayers.PaintSize;
            int hx = Mathf.Clamp(Mathf.RoundToInt(h.x / terrain.Site.SideMeters * size), 0, size - 1);
            int hy = Mathf.Clamp(Mathf.RoundToInt(h.y / terrain.Site.SideMeters * size), 0, size - 1);
            Check("Habitat dot is drawn where site.json puts it", Same(returnPixels[hy * size + hx], new Color32(255, 255, 255, 255)), "pixel " + hx + ", " + hy);
            int ring = 0;
            foreach (var p in returnPixels) if (p.r == 245 && p.g == 245 && p.b == 245) ring++;
            Check("Return paint has the 5-minute rings", ring > 1000, ring + " ring px");
            terrain.ShowLayer(MapLayer.Return);
            app.RefreshPanel();
            Check("Return layer's legend says ESTIMATE (the word on every other number that is not the walking model yet), not PLACEHOLDER",
                !terrain.ReturnIsPlaceholder || (app.panel.legendLine.text.Contains("ESTIMATE: ") && !app.panel.legendLine.text.Contains("PLACEHOLDER")));
            // On the real site every stop must have a way back on foot: a stop on ground the return map
            // marks as cut off (infinity) would show no minutes at all.
            if (terrain.Site.IsMarsData && terrain.Site.StopsAreReal)
            {
                int gn = terrain.Site.GridSize;
                float gs = terrain.Site.NodeSpacing;
                string cutOff = "";
                float farthestStop = 0f;
                foreach (var stop in terrain.Site.Stops)
                {
                    int sc = Mathf.Clamp(Mathf.RoundToInt(stop.x / gs), 0, gn - 1);
                    int sr = Mathf.Clamp(Mathf.RoundToInt((terrain.Site.SideMeters - stop.y) / gs), 0, gn - 1);
                    float m = terrain.ReturnMinutes[sr * gn + sc];
                    if (float.IsInfinity(m) || float.IsNaN(m)) cutOff += (cutOff.Length > 0 ? ", " : "") + stop.name;
                    else farthestStop = Mathf.Max(farthestStop, m);
                }
                Check("Return layer: every stop has a way back to the habitat", cutOff.Length == 0, cutOff.Length == 0 ? "farthest stop " + farthestStop.ToString("F1") + " min" : "no way back from: " + cutOff);
            }

            // Photo: the HiRISE orthophoto draped on the relief (only with session B's package).
            terrain.ShowLayer(MapLayer.Photo);
            app.RefreshPanel();
            if (terrain.HasPhoto)
            {
                renderer.GetPropertyBlock(block);
                var photo = terrain.LayerTexture(MapLayer.Photo);
                Check("Layer Photo: the HiRISE photo lies on the relief at full resolution (4096 px, 24 cm per pixel), anisotropic 16", block.GetTexture("_BaseMap") == photo && photo.width == GaleTerrain.PhotoSize && photo.anisoLevel == GaleTerrain.PhotoAniso && photo.mipmapCount > 1 && terrain.Layer == MapLayer.Photo,
                    "loaded in " + terrain.PaintMilliseconds[(int)MapLayer.Photo] + " ms");
                Color tint = block.GetColor("_BaseColor");
                Check("Layer Photo: tinted with the ground colour Curiosity's Mastcam saw (site.json photoTint), named in the legend",
                    !terrain.PhotoTinted || (Mathf.Abs(tint.r - terrain.PhotoTint.r) < 0.01f && Mathf.Abs(tint.b - terrain.PhotoTint.b) < 0.01f && app.panel.legendLine.text.Contains("Mastcam")),
                    terrain.PhotoTinted ? "tint " + tint.r.ToString("F2") + ", " + tint.g.ToString("F2") + ", " + tint.b.ToString("F2") : "no tint in the package");
                Check("Layer Photo names its mission, instrument and resolution on the panel", app.panel.legendLine.text.StartsWith("PHOTO") && app.panel.legendLine.text.Contains("HiRISE"), app.panel.legendLine.text);
            }
            else Check("No photo in the package: PHOTO falls back to HEIGHT", terrain.Layer == MapLayer.Height);
            terrain.ShowLayer(MapLayer.Height);
            app.RefreshPanel();
            renderer.GetPropertyBlock(block);
            Color plain = block.GetColor("_BaseColor");
            Check("The analysis layers are not tinted (their colours are their legend)", Mathf.Abs(plain.r - 1f) < 0.001f && Mathf.Abs(plain.g - 1f) < 0.001f && Mathf.Abs(plain.b - 1f) < 0.001f);
        }

        static bool Same(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b;
        }

        static void Screenshots(WalkbackApp app)
        {
            var cam = app.desktopCamera;
            Vector3 homePosition = cam.transform.position;
            Quaternion homeRotation = cam.transform.rotation;
            Vector3 mapCenter = app.mapRoot.position;
            Vector3 aboveMap = mapCenter + new Vector3(0f, 1.0f, 0f);

            ShowLayer(app, MapLayer.Photo);
            Shot(cam, "00_overview_photo.png", homePosition, new Vector3(0f, 1.05f, 1.4f));
            ShotDown(cam, "00b_map_photo.png", aboveMap);
            ShowLayer(app, MapLayer.Height);
            Shot(cam, "01_overview.png", homePosition, new Vector3(0f, 1.05f, 1.4f));
            Shot(cam, "02_panel_height.png", new Vector3(0f, 1.5f, 0.35f), SceneBuilder.PanelCenter);
            ShotDown(cam, "03_map_height.png", aboveMap);

            ShowLayer(app, MapLayer.Slope);
            ShotDown(cam, "04_map_slope.png", aboveMap);
            Shot(cam, "05_panel_slope.png", new Vector3(0f, 1.5f, 0.35f), SceneBuilder.PanelCenter);

            ShowLayer(app, MapLayer.Return);
            ShotDown(cam, "06_map_return.png", aboveMap);
            Shot(cam, "07_panel_return.png", new Vector3(0f, 1.5f, 0.35f), SceneBuilder.PanelCenter);

            ShowLayer(app, MapLayer.Height);
            cam.transform.SetPositionAndRotation(homePosition, homeRotation);
        }

        static void ShowLayer(WalkbackApp app, MapLayer layer)
        {
            app.terrain.ShowLayer(layer);
            app.RefreshPanel();
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>Straight down on the map, north at the top of the picture.</summary>
        static void ShotDown(Camera cam, string fileName, Vector3 from)
        {
            Shot(cam, fileName, from, from + Vector3.down, Vector3.forward);
        }

        static void Shot(Camera cam, string fileName, Vector3 from, Vector3 lookAt)
        {
            Shot(cam, fileName, from, lookAt, Vector3.up);
        }

        static void Shot(Camera cam, string fileName, Vector3 from, Vector3 lookAt, Vector3 up)
        {
            const int w = 1920, h = 1080;
            cam.transform.position = from;
            cam.transform.rotation = Quaternion.LookRotation(lookAt - from, up);
            var target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            cam.targetTexture = target;
            cam.Render();
            RenderTexture.active = target;
            var image = new Texture2D(w, h, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            image.Apply();
            cam.targetTexture = null;
            RenderTexture.active = previous;

            string path = Path.Combine(OutputFolder, fileName);
            File.WriteAllBytes(path, image.EncodeToPNG());

            // A blank frame means the camera drew nothing.
            Color32[] pixels = image.GetPixels32();
            Color32 corner = pixels[0];
            int different = 0;
            for (int i = 0; i < pixels.Length; i += 16)
            {
                Color32 p = pixels[i];
                if (Mathf.Abs(p.r - corner.r) + Mathf.Abs(p.g - corner.g) + Mathf.Abs(p.b - corner.b) > 24) different++;
            }
            float share = different / (pixels.Length / 16f);
            Check("Screenshot " + fileName + " is not blank", share > 0.05f, (share * 100f).ToString("F0") + " % of the frame drawn, " + new FileInfo(path).Length + " bytes");

            UnityEngine.Object.DestroyImmediate(image);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }

        /// <summary>A line in the report that is information, not a check.</summary>
        static void Note(string text)
        {
            lines.Add("  ----  " + text);
        }

        static void Check(string name, bool ok, string detail = null)
        {
            if (ok) passed++; else failed++;
            lines.Add((ok ? "  PASS  " : "  FAIL  ") + name + (string.IsNullOrEmpty(detail) ? "" : "  [" + detail + "]"));
        }
    }
}
