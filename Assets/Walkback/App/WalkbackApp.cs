// Written with Claude Code (AI), directed and tested by team MARSXR
using System;
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
        partial void StartHud();
        partial void UpdateHud();
        partial void StartMission();
        partial void UpdateMission();

        public GameObject cameraRig;
        public Camera desktopCamera;
        public Transform table;
        public Transform mapRoot;
        public MissionPanel panel;
        public GaleTerrain terrain;

        public bool DesktopMode { get; private set; }

        // ---- Shared between pieces: who raises and who listens ----

        /// <summary>True while something animates the map (the globe opening): table controls must not move it (trap 17).</summary>
        public bool MapControlsLocked;

        /// <summary>The stop chosen on the map or the panel (index into terrain.Site.Stops), or -1.</summary>
        public int SelectedStop { get; private set; } = -1;
        /// <summary>Raised by SelectStop (pillars, panel). The panel shows that stop's card.</summary>
        public event Action<int> StopSelected;
        /// <summary>Raised by RequestJump (the panel's jump button). The site jump shows that stop's panorama.</summary>
        public event Action<int> JumpRequested;

        public void SelectStop(int index)
        {
            SelectedStop = index;
            WalkbackLog.Info("Stop selected: " + index);
            if (StopSelected != null) StopSelected(index);
        }

        /// <summary>The stop whose pillar the pointer is on right now (index into terrain.Site.Stops), or -1.</summary>
        public int PreviewedStop { get; private set; } = -1;
        /// <summary>Raised by PreviewStop and EndPreview: the side board shows that stop's card until the pointer leaves (-1).</summary>
        public event Action<int> StopPreviewed;

        /// <summary>The pointer is on a stop's pillar: show its card as a preview, without selecting it.</summary>
        public void PreviewStop(int index)
        {
            if (index == PreviewedStop) return;
            PreviewedStop = index;
            if (StopPreviewed != null) StopPreviewed(index);
        }

        /// <summary>The pointer left that stop's pillar: the card goes back to the selected stop.</summary>
        public void EndPreview(int index)
        {
            if (index != PreviewedStop) return;
            PreviewedStop = -1;
            if (StopPreviewed != null) StopPreviewed(-1);
        }

        public void RequestJump(int index)
        {
            WalkbackLog.Info("Jump requested: stop " + index);
            if (JumpRequested != null) JumpRequested(index);
        }

        /// <summary>Writes the site line and the legend of the current layer on the panel.</summary>
        public void RefreshPanel()
        {
            if (panel == null || terrain == null || terrain.Site == null) return;
            panel.ShowSite(terrain.SourceLine(), terrain.Site.IsMarsData);
            panel.ShowLegend(terrain.LegendText(terrain.Layer));
        }

        /// <summary>Where the user is, for the fps line of the log: Mars, room, 360 photo, opening or table.</summary>
        public static string WhereName(MissionMode.Mode mode, bool insidePhoto, bool mapLocked)
        {
            if (mode == MissionMode.Mode.Mars) return "Mars";
            if (mode == MissionMode.Mode.Room) return "room";
            if (insidePhoto) return "360 photo";
            return mapLocked ? "opening" : "table";
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
            StartHud();
            StartMission();
            started = true;
            fpsWindowStart = Time.realtimeSinceStartup;
        }

        void Update()
        {
            // Desktop mode: keys 1, 2, 3, 4 switch the map layer (height, slope, return, photo).
            var keyboard = Keyboard.current;
            if (DesktopMode && terrain != null && keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) terrain.ShowLayer(MapLayer.Height);
                if (keyboard.digit2Key.wasPressedThisFrame) terrain.ShowLayer(MapLayer.Slope);
                if (keyboard.digit3Key.wasPressedThisFrame) terrain.ShowLayer(MapLayer.Return);
                if (keyboard.digit4Key.wasPressedThisFrame) terrain.ShowLayer(MapLayer.Photo);
            }

            if (started)
            {
                UpdatePointer();
                UpdateGlobe();
                UpdateRoute();
                UpdatePanelPiece();
                UpdateHud();
                UpdateMission();
            }

            fpsFrames++;
            if (Time.unscaledDeltaTime > 0f)
                fpsWorst = Mathf.Min(fpsWorst, 1f / Time.unscaledDeltaTime);
            float elapsed = Time.realtimeSinceStartup - fpsWindowStart;
            if (elapsed >= 10f)
            {
                // With where the user is, so the log says what Mars costs (it was never measured there).
                WalkbackLog.Info("fps avg " + (fpsFrames / elapsed).ToString("F1") + " worst " + fpsWorst.ToString("F1") + " (" +
                    WhereName(mission != null ? mission.Current : MissionMode.Mode.Table, siteJump != null && siteJump.Inside, MapControlsLocked) + ")");
                fpsWindowStart = Time.realtimeSinceStartup;
                fpsFrames = 0;
                fpsWorst = float.MaxValue;
            }
        }
    }
}
