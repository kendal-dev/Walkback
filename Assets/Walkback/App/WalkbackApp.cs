// Written with Claude Code (AI), directed and tested by team MARSXR
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace Walkback
{
    /// <summary>
    /// Entry point. In the headset: Meta camera rig with passthrough.
    /// Without a headset (Play in the editor): a desktop camera looks at the table.
    /// </summary>
    public partial class WalkbackApp : MonoBehaviour
    {
        // Each piece keeps its fields and per-frame work in its own file (WalkbackApp.<Piece>.cs).
        // Start hooks run after the mode (headset or desktop) is known. Unimplemented hooks compile to nothing.
        partial void StartRoute();
        partial void StartPanelPiece();
        partial void StartGlobe();
        partial void StartPointer();
        partial void UpdateRoute();
        partial void UpdatePanelPiece();
        partial void UpdateGlobe();
        partial void UpdatePointer();

        public GameObject cameraRig;
        public Camera desktopCamera;
        public Transform table;
        public Transform mapRoot;
        public MissionPanel panel;
        public GaleTerrain terrain;

        public bool DesktopMode { get; private set; }

        /// <summary>Writes the site line and the legend of the current layer on the panel.</summary>
        public void RefreshPanel()
        {
            if (panel == null || terrain == null || terrain.Site == null) return;
            panel.ShowSite(terrain.SourceLine(), terrain.Site.IsMarsData);
            panel.ShowLegend(terrain.LegendText(terrain.Layer));
        }

        void OnLayerChanged(MapLayer layer)
        {
            RefreshPanel();
            WalkbackLog.Info("Layer shown: " + layer);
        }

        void OnDestroy()
        {
            if (terrain != null) terrain.LayerChanged -= OnLayerChanged;
        }

        bool started;
        float fpsWindowStart;
        int fpsFrames;
        float fpsWorst = float.MaxValue;

        void Awake()
        {
            WalkbackLog.Init();
            WalkbackLog.Info("Walkback " + Application.version + " on " + SystemInfo.deviceModel + " (" + SystemInfo.operatingSystem + "), Unity " + Application.unityVersion);
            WalkbackLog.Info("Log file: " + WalkbackLog.FilePath);
        }

        IEnumerator Start()
        {
            if (terrain != null) terrain.LayerChanged += OnLayerChanged;
            RefreshPanel();

            // The XR device needs a couple of frames to report itself.
            yield return null;
            yield return null;
            DesktopMode = !XRSettings.isDeviceActive;
            if (cameraRig != null) cameraRig.SetActive(!DesktopMode);
            if (desktopCamera != null) desktopCamera.enabled = DesktopMode;
            WalkbackLog.Info(DesktopMode
                ? "Mode: desktop (no headset found)"
                : "Mode: headset " + XRSettings.loadedDeviceName);
            StartRoute();
            StartPanelPiece();
            StartGlobe();
            StartPointer();
            started = true;
            fpsWindowStart = Time.realtimeSinceStartup;
        }

        void Update()
        {
            // Desktop mode: keys 1, 2, 3 switch the map layer (the pointer and panel buttons come later).
            var keyboard = Keyboard.current;
            if (DesktopMode && terrain != null && keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) terrain.ShowLayer(MapLayer.Height);
                if (keyboard.digit2Key.wasPressedThisFrame) terrain.ShowLayer(MapLayer.Slope);
                if (keyboard.digit3Key.wasPressedThisFrame) terrain.ShowLayer(MapLayer.Return);
            }

            if (started)
            {
                UpdatePointer();
                UpdateGlobe();
                UpdateRoute();
                UpdatePanelPiece();
            }

            fpsFrames++;
            if (Time.unscaledDeltaTime > 0f)
                fpsWorst = Mathf.Min(fpsWorst, 1f / Time.unscaledDeltaTime);
            float elapsed = Time.realtimeSinceStartup - fpsWindowStart;
            if (elapsed >= 10f)
            {
                WalkbackLog.Info("fps avg " + (fpsFrames / elapsed).ToString("F1") + " worst " + fpsWorst.ToString("F1"));
                fpsWindowStart = Time.realtimeSinceStartup;
                fpsFrames = 0;
                fpsWorst = float.MaxValue;
            }
        }
    }
}
