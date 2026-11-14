// Written with Claude Code (AI), directed and tested by team MARSXR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Walkback.EditorTools
{
    /// <summary>
    /// Builds the whole scene from code: Meta camera rig with passthrough, virtual table,
    /// map, mission panel and desktop camera. Nobody clicks anything in Unity.
    /// Menu: Walkback > Build Scene.
    /// </summary>
    public static partial class SceneBuilder
    {
        // Each piece adds its own objects from its own file (SceneBuilder.<Piece>.cs), so pieces
        // written in parallel never edit this file. An unimplemented hook compiles to nothing.
        static partial void BuildRoute(WalkbackApp app);
        static partial void BuildPanelPiece(WalkbackApp app);
        static partial void BuildGlobe(WalkbackApp app);
        static partial void BuildPointer(WalkbackApp app);

        public const string ScenePath = "Assets/Walkback/Scenes/Walkback.unity";
        public const string MaterialFolder = "Assets/Walkback/Materials";
        public const string RigPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";

        // Layout in meters. The user stands at the origin looking along +z.
        public const float TableHeight = 0.75f;
        public const float TableDiameter = 1.5f;
        public const float TableDistance = 1.0f;
        public const float MapSideOnTable = 1.0f;
        public static readonly Vector3 PanelCenter = new Vector3(0f, 1.50f, 1.95f);
        public static readonly Vector3 DesktopEye = new Vector3(0f, 1.60f, -0.35f);

        [MenuItem("Walkback/Build Scene")]
        public static void BuildFromMenu()
        {
            Build();
            Debug.Log("[Walkback] Scene built: " + ScenePath);
        }

        public static WalkbackApp Build()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh();

            Material tableMaterial = MakeMaterial("Table", "Universal Render Pipeline/Lit", WalkbackTheme.Table, 0.15f);
            Material terrainMaterial = MakeMaterial("Terrain", "Universal Render Pipeline/Lit", Color.white, 0f);
            Material backPlateMaterial = MakeMaterial("PanelBack", "Universal Render Pipeline/Unlit", WalkbackTheme.PanelBack, 0f);
            Material uiMaterial = MakeMaterial("PanelUI", "Walkback/UI", Color.white, 0f);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Light
            var lightObject = new GameObject("Sun");
            var sun = lightObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.None;
            lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.45f);

            // Meta camera rig with passthrough
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            if (rigPrefab == null) throw new FileNotFoundException("Meta camera rig prefab not found", RigPrefabPath);
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            rig.name = "OVRCameraRig";
            var manager = rig.GetComponent<OVRManager>();
            manager.isInsightPassthroughEnabled = true;
            var managerData = new SerializedObject(manager);
            managerData.FindProperty("_trackingOriginType").intValue = (int)OVRManager.TrackingOrigin.FloorLevel;
            managerData.ApplyModifiedPropertiesWithoutUndo();
            // In this SDK version the passthrough layer is always the background (underlay).
            rig.AddComponent<OVRPassthroughLayer>();
            foreach (var eye in rig.GetComponentsInChildren<Camera>(true))
            {
                // Transparent black: where nothing is drawn, the room shows through.
                eye.clearFlags = CameraClearFlags.SolidColor;
                eye.backgroundColor = new Color(0f, 0f, 0f, 0f);
                eye.nearClipPlane = 0.05f;
            }

            var projectConfig = OVRProjectConfig.CachedProjectConfig;
            if (projectConfig.insightPassthroughSupport == OVRProjectConfig.FeatureSupport.None)
            {
                projectConfig.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
                OVRProjectConfig.CommitProjectConfig(projectConfig);
            }

            // Virtual table
            var table = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            table.name = "Table";
            table.transform.position = new Vector3(0f, TableHeight / 2f, TableDistance);
            table.transform.localScale = new Vector3(TableDiameter, TableHeight / 2f, TableDiameter);
            table.GetComponent<MeshRenderer>().sharedMaterial = tableMaterial;

            // Map: one square kilometer scaled to the table, relief not exaggerated
            var mapRoot = new GameObject("MapRoot");
            mapRoot.transform.position = new Vector3(0f, TableHeight + 0.01f, TableDistance);
            // The site side is read from site.json (1000 m by contract); the map is always 1 m on the table.
            var site = SiteLoader.Load();
            mapRoot.transform.localScale = Vector3.one * (MapSideOnTable / site.SideMeters);
            var terrainObject = new GameObject("Terrain", typeof(MeshFilter), typeof(MeshRenderer), typeof(GaleTerrain));
            terrainObject.transform.SetParent(mapRoot.transform, false);
            var terrainRenderer = terrainObject.GetComponent<MeshRenderer>();
            terrainRenderer.sharedMaterial = terrainMaterial;
            terrainRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Mission panel behind the map
            var panelObject = new GameObject("MissionPanel");
            panelObject.transform.position = PanelCenter;
            var panel = panelObject.AddComponent<MissionPanel>();
            panel.Build(backPlateMaterial, uiMaterial);
            panel.ShowSite(site.IsMarsData ? "Site: " + site.Info.name : "TEST TERRAIN · synthetic shape for development · not Mars data", site.IsMarsData);

            // Desktop camera (Play without a headset, and the automatic test)
            var desktopObject = new GameObject("DesktopCamera");
            var desktopCamera = desktopObject.AddComponent<Camera>();
            desktopCamera.clearFlags = CameraClearFlags.SolidColor;
            desktopCamera.backgroundColor = new Color(0.20f, 0.21f, 0.23f, 1f);
            desktopCamera.fieldOfView = 60f;
            desktopCamera.nearClipPlane = 0.05f;
            desktopCamera.depth = 10f;
            desktopCamera.enabled = false;
            desktopObject.transform.position = DesktopEye;
            desktopObject.transform.LookAt(new Vector3(0f, 1.05f, 1.4f));

            // App root
            var appObject = new GameObject("WalkbackApp");
            var app = appObject.AddComponent<WalkbackApp>();
            app.cameraRig = rig;
            app.desktopCamera = desktopCamera;
            app.table = table.transform;
            app.mapRoot = mapRoot.transform;
            app.panel = panel;
            app.terrain = terrainObject.GetComponent<GaleTerrain>();

            BuildRoute(app);
            BuildPanelPiece(app);
            BuildGlobe(app);
            BuildPointer(app);   // last: it looks for every pointer target the others made

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            return app;
        }

        /// <summary>Creates or updates Assets/Walkback/Materials/[name].mat. Pieces use it for their own materials.</summary>
        public static Material MakeMaterial(string name, string shaderName, Color color, float smoothness)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.InvalidOperationException("Shader not found: " + shaderName);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
