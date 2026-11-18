// Written with Claude Code (AI), directed and tested by team MARSXR
using UnityEngine;

namespace Walkback
{
    /// <summary>
    /// v1.1: a small card over the pointer's dot while it is on the bare ground of the map at the table:
    /// minutes back to the habitat from there and the slope (GroundProbe), so the astronaut sees what a
    /// stop there would cost before tapping. Built at runtime from the panel's materials.
    /// </summary>
    public partial class WalkbackApp
    {
        public const float ProbeWidthMm = 440f;
        public const float ProbeHeightMm = 76f;
        /// <summary>The card floats this far above the dot (meters in the room)...</summary>
        public const float ProbeLift = 0.05f;
        /// <summary>...and this share of the way toward the eye, so the pillars' labels never cover it.</summary>
        public const float ProbeTowardEye = 0.3f;

        GlobeLabel probe;
        public GlobeLabel ProbeCard { get { return probe; } }
        public bool ProbeShown { get { return probe != null && probe.gameObject.activeSelf; } }

        void EnsureProbe()
        {
            if (probe != null || panel == null) return;
            probe = GlobeLabel.Create(transform, "GroundProbe", ProbeWidthMm, ProbeHeightMm, new[] { 22, 16 },
                new[] { WalkbackTheme.Text, WalkbackTheme.TextSoft }, panel.backPlateMaterial, panel.uiMaterial, TextAnchor.MiddleCenter);
            probe.gameObject.SetActive(false);
        }

        /// <summary>Shows the card for a point on the terrain (room space), or hides it (null). The test calls it.</summary>
        public void ShowProbeAt(Vector3? world, Vector3 eye)
        {
            EnsureProbe();
            if (probe == null) return;
            if (world == null || terrain == null || terrain.Site == null)
            {
                if (probe.gameObject.activeSelf) probe.gameObject.SetActive(false);
                return;
            }
            Vector2 meters = WorldToMapMeters(world.Value);
            GroundProbe.Reading r = GroundProbe.Read(terrain.Site, terrain.Slope, terrain.ReturnMinutes, meters,
                terrain.ReturnReachMinutes, WhatIfTankPercent, CanEditStops && UserStops.Refusal(terrain.Site, meters) == null);
            probe.SetLines(r.first, r.second);
            probe.lines[0].color = GroundProbe.ColorOf(r.kind);
            probe.transform.position = Vector3.Lerp(world.Value + Vector3.up * ProbeLift, eye, ProbeTowardEye);
            probe.Face(eye);
            if (!probe.gameObject.activeSelf) probe.gameObject.SetActive(true);
        }

        void TickGroundProbe()
        {
            Camera cam = StopPillars.ViewerCamera();
            bool show = cam != null && PointerOnGround() && !MapControlsLocked && !MissionActive && (siteJump == null || !siteJump.Inside) &&
                        (tableControls == null || !tableControls.Dragging);
            ShowProbeAt(show ? pointer.HitPoint : (Vector3?)null, cam != null ? cam.transform.position : Vector3.zero);
        }
    }
}
