// Written with Claude Code (AI), directed and tested by team MARSXR
using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Walkback
{
    /// <summary>
    /// The route piece of the app: pillars at the habitat and the stops, the straight line between
    /// them, and the safe route once session C's RoutePlanner exists.
    /// </summary>
    public partial class WalkbackApp
    {
        public RouteView routeView;
        public StopPillars stopPillars;

        partial void StartRoute()
        {
            ShowRouteStartState(true);
            // TODO(H2, Fable): when session C's RoutePlanner lands in Assets/Walkback/Model/, plan
            // habitat -> stops -> habitat with it here. Until then: PLAN B (MissionFallback), an estimate
            // over the real relief, labelled as such on the verdict card (rule 1).
            PlanMission(true);
            // After the globe opening the map lands on the table: let the route draw itself then.
            MapLanded += () => { if (Plan != null && Plan.Path.Count >= 2) ShowPlannedRoute(Plan.Path.ToArray(), true); };
        }

        /// <summary>The plan B mission plan on the map and the verdict card, or null before PlanMission.</summary>
        public MissionFallback.Plan Plan { get; private set; }
        /// <summary>The tank the plan starts with, in percent of a full one. 100 = the real plan; less = a WHAT IF.</summary>
        public float WhatIfTankPercent { get; private set; } = 100f;
        static readonly float[] WhatIfSteps = { 100f, 15f, 8f };
        int whatIfStep;

        /// <summary>
        /// Plans habitat -> stops in order -> habitat over the relief (MissionFallback), draws the amber
        /// route, writes the verdict card and, in a WHAT IF, paints in red the ground too far to walk back from.
        /// </summary>
        public void PlanMission(bool animate)
        {
            if (terrain == null || terrain.Site == null || terrain.Site.Stops == null || terrain.Site.Stops.Length == 0) return;
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Plan = MissionFallback.Make(terrain.Site, terrain.Slope, WhatIfTankPercent);
                if (Plan.Path.Count >= 2) ShowPlannedRoute(Plan.Path.ToArray(), animate);
                else ClearPlannedRoute();
                ShowPlanOnPanel();
                terrain.SetReturnReach(WhatIfTankPercent < 100f ? MissionFallback.ReachMinutes(WhatIfTankPercent) : float.PositiveInfinity);
                WalkbackLog.Info("Mission planned in " + watch.ElapsedMilliseconds + " ms (plan B): " + MissionFallback.LogLine(Plan));
            }
            catch (Exception e)
            {
                Plan = null;
                WalkbackLog.Warn("Mission plan B failed, the verdict stays NO ROUTE YET: " + e.Message);
            }
        }

        /// <summary>Writes the current plan on the verdict card (nothing if there is no plan).</summary>
        public void ShowPlanOnPanel()
        {
            if (panel == null || Plan == null) return;
            VerdictState state;
            string headline, summary, note;
            MissionFallback.Describe(Plan, out state, out headline, out summary, out note);
            // v1.1: the tank came from a SUITS telemetry server, not from a WHAT IF.
            if (SuitsLive && tankNote != null) note = tankNote;
            panel.ShowVerdict(state, headline, summary, note);
            // The same plan as charts: oxygen, time and height along the route (empty when there is no route).
            panel.ShowCharts(terrain != null ? terrain.Site : null, Plan);
        }

        /// <summary>
        /// WHAT IF the suit fails: the same plan starting with less oxygen (100 % -> 15 % -> 8 % -> 100 %).
        /// With less than a full tank the map switches to the return layer, where red = too far to walk back.
        /// Back at 100 % the map shows again the layer it had before the WHAT IF.
        /// </summary>
        public void NextWhatIf()
        {
            if (SuitsLive)
            {
                WalkbackLog.Info("What if: off, the tank comes from SUITS TSS");
                return;
            }
            whatIfStep = (whatIfStep + 1) % WhatIfSteps.Length;
            SetWhatIfTank(WhatIfSteps[whatIfStep]);
        }

        /// <summary>The layer the map showed when the WHAT IF began (kept from 15 % to 8 %).</summary>
        MapLayer layerBeforeWhatIf = MapLayer.Photo;
        bool whatIfChangedLayer;

        public void SetWhatIfTank(float percent)
        {
            bool wasFull = WhatIfTankPercent >= 100f;
            WhatIfTankPercent = Mathf.Clamp(percent, 0f, 100f);
            PlanMission(false);
            if (terrain != null && WhatIfTankPercent < 100f)
            {
                if (wasFull)
                {
                    layerBeforeWhatIf = terrain.Layer;
                    whatIfChangedLayer = true;
                }
                terrain.ShowLayer(MapLayer.Return);
            }
            else if (terrain != null && whatIfChangedLayer)
            {
                // The full tank again: the layer of before comes back (it stayed on RETURN, without any red, and
                // the user had to press PHOTO). Not if they chose another layer in the meantime.
                whatIfChangedLayer = false;
                if (terrain.Layer == MapLayer.Return && layerBeforeWhatIf != MapLayer.Return) terrain.ShowLayer(layerBeforeWhatIf);
            }
            WalkbackLog.Info("What if: tank at " + WhatIfTankPercent.ToString("0") + " %");
        }

        /// <summary>After the mission, the WHAT IF button waits this long before it answers again.</summary>
        public const float WhatIfQuietSeconds = 1f;
        float missionSeenAt = -10f;

        /// <summary>
        /// May the WHAT IF button act now? Not while the map is locked (globe opening, 360° photo, mission).
        /// Not while a fade runs: START MISSION fades for half a second before the map locks, and a click of
        /// the stick in that time re-planned with 15 % of a tank, so the mission began with that tank.
        /// Not in the first second after the mission: there the same click of the left stick changes the
        /// view, and one click too many at BACK TO TABLE started a WHAT IF.
        /// </summary>
        public static bool WhatIfAllowed(bool mapLocked, bool fading, float secondsSinceMission)
        {
            return !mapLocked && !fading && secondsSinceMission >= WhatIfQuietSeconds;
        }

        partial void UpdateRoute()
        {
            // WHAT IF: F on the keyboard, or a click of the left thumbstick. Not while the map is locked
            // (globe opening, inside a 360° photo), nor during a fade or right after the mission.
            if (MissionActive) missionSeenAt = Time.unscaledTime;
            if (!WhatIfAllowed(MapControlsLocked, mission != null && mission.Fading, Time.unscaledTime - missionSeenAt)) return;
            var keyboard = Keyboard.current;
            bool key = keyboard != null && keyboard.fKey.wasPressedThisFrame;
            bool stick = !DesktopMode && OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch);
            if (key || stick) NextWhatIf();
        }

        /// <summary>
        /// What the map shows before any route is planned: the pillars and the straight dotted line
        /// habitat -> stops -> habitat. No amber route. The test calls it with grow = false.
        /// </summary>
        public void ShowRouteStartState(bool grow)
        {
            if (terrain == null || terrain.Site == null) return;
            if (stopPillars != null) stopPillars.Build(this, grow);
            if (routeView != null)
            {
                routeView.Bind(terrain.Site);
                routeView.ClearRoute();
                routeView.ShowStraightLine(StopWaypoints());
            }
        }

        /// <summary>Habitat, every stop in order, habitat again, in local meters (x east, y north).</summary>
        public Vector2[] StopWaypoints()
        {
            SiteData site = terrain.Site;
            var points = new Vector2[site.Stops.Length + 2];
            points[0] = site.Habitat;
            for (int i = 0; i < site.Stops.Length; i++) points[i + 1] = new Vector2(site.Stops[i].x, site.Stops[i].y);
            points[points.Length - 1] = site.Habitat;
            return points;
        }

        /// <summary>
        /// The safe route from session C's RoutePlanner, in local meters (x east, y north from the
        /// south-west corner): the amber ribbon draws itself on the map.
        /// </summary>
        public void ShowPlannedRoute(Vector2[] pointsMeters, bool animate = true)
        {
            if (routeView == null || terrain == null || terrain.Site == null) return;
            routeView.Bind(terrain.Site);
            routeView.ShowRoute(pointsMeters, animate);
        }

        public void ClearPlannedRoute()
        {
            if (routeView != null) routeView.ClearRoute();
        }
    }
}
