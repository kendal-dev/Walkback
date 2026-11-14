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
    public static class AutoTest
    {
        const string OutputFolder = "TestOutput";
        const string TriggerFile = "Temp/walkback-run-test";

        static readonly List<string> lines = new List<string>();
        static int passed, failed;
        static double nextTriggerCheck;

        static AutoTest()
        {
            EditorApplication.update += WatchTrigger;
        }

        static void WatchTrigger()
        {
            if (EditorApplication.timeSinceStartup < nextTriggerCheck) return;
            nextTriggerCheck = EditorApplication.timeSinceStartup + 1.0;
            if (!File.Exists(TriggerFile)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(TriggerFile);
            Run();
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
            try
            {
                WalkbackApp app = SceneBuilder.Build();
                CheckScene(app);
                Screenshots(app);
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
            var terrain = app.mapRoot.GetComponentInChildren<TestTerrain>();
            Check("Terrain present", terrain != null);
            terrain.Build();
            var mesh = terrain.GetComponent<MeshFilter>().sharedMesh;
            Check("Terrain mesh is 257 x 257", mesh != null && mesh.vertexCount == 257 * 257, mesh != null ? mesh.vertexCount + " vertices" : "no mesh");
            Check("Terrain mesh has 131072 triangles", mesh != null && mesh.triangles.Length / 3 == 256 * 256 * 2);
            var mapBounds = terrain.GetComponent<Renderer>().bounds;
            Check("Map is 1 m wide on the table", Mathf.Abs(mapBounds.size.x - SceneBuilder.MapSideOnTable) < 0.005f && Mathf.Abs(mapBounds.size.z - SceneBuilder.MapSideOnTable) < 0.005f, mapBounds.size.x.ToString("F3") + " x " + mapBounds.size.z.ToString("F3") + " m");
            Check("Map sits on the table, not inside it", mapBounds.min.y >= tableBounds.max.y, "map bottom " + mapBounds.min.y.ToString("F3") + " m");
            Check("Map fits on the table", new Vector2(mapBounds.extents.x, mapBounds.extents.z).magnitude <= SceneBuilder.TableDiameter / 2f + 0.001f);
            float relief = terrain.MaxElevation - terrain.MinElevation;
            Check("Relief is not exaggerated", Mathf.Abs(mapBounds.size.y - relief * SceneBuilder.MapSideOnTable / TestTerrain.SideMeters) < 0.002f, relief.ToString("F0") + " m of relief -> " + mapBounds.size.y.ToString("F3") + " m on the table");
            Check("North is +z (row 0 is the north edge)", Mathf.Approximately(TestTerrain.HeightAt(0f, 1000f), terrain.Heights[0]));
            Check("Normals point up", mesh != null && mesh.normals[257 * 128 + 128].y > 0.5f);

            // Panel
            Check("Panel present", app.panel != null);
            var canvasRect = (RectTransform)app.panel.GetComponentInChildren<Canvas>().transform;
            float width = canvasRect.sizeDelta.x * canvasRect.lossyScale.x, height = canvasRect.sizeDelta.y * canvasRect.lossyScale.y;
            Check("Panel is 1.50 m x 0.70 m", Mathf.Abs(width - WalkbackTheme.PanelWidthMeters) < 0.001f && Mathf.Abs(height - WalkbackTheme.PanelHeightMeters) < 0.001f, width.ToString("F2") + " x " + height.ToString("F2") + " m");
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
            Check("Test terrain is labelled as not Mars data", app.panel.dataLine.text.Contains("not Mars data"));
            float distance = Vector3.Distance(new Vector3(0f, 1.6f, 0f), app.panel.transform.position);
            Check("Panel is 1.6 to 2.2 m from the user", distance >= 1.6f && distance <= 2.2f, distance.ToString("F2") + " m");

            // Desktop mode
            Check("Desktop camera present and off by default", app.desktopCamera != null && !app.desktopCamera.enabled);
        }

        static void Screenshots(WalkbackApp app)
        {
            Canvas.ForceUpdateCanvases();
            var cam = app.desktopCamera;
            Vector3 homePosition = cam.transform.position;
            Quaternion homeRotation = cam.transform.rotation;

            Shot(cam, "01_overview.png", homePosition, new Vector3(0f, 1.05f, 1.4f));
            Shot(cam, "02_panel.png", new Vector3(0f, 1.5f, 0.35f), SceneBuilder.PanelCenter);
            Shot(cam, "03_map.png", new Vector3(0f, 1.75f, 0.55f), new Vector3(0f, 0.8f, 1.0f));

            cam.transform.SetPositionAndRotation(homePosition, homeRotation);
        }

        static void Shot(Camera cam, string fileName, Vector3 from, Vector3 lookAt)
        {
            const int w = 1920, h = 1080;
            cam.transform.position = from;
            cam.transform.LookAt(lookAt);
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

        static void Check(string name, bool ok, string detail = null)
        {
            if (ok) passed++; else failed++;
            lines.Add((ok ? "  PASS  " : "  FAIL  ") + name + (string.IsNullOrEmpty(detail) ? "" : "  [" + detail + "]"));
        }
    }
}
