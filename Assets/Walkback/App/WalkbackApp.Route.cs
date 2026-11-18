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
            // v1.1: the stops the astronaut picked last time come back before the first plan.
            RestoreUserStops();
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
            pendingPlan = null;   // a plan made now wins over one still on its way
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                ApplyPlan(MissionFallback.Make(terrain.Site, terrain.Slope, WhatIfTankPercent), animate, watch.ElapsedMilliseconds, "");
            }
            catch (Exception e)
            {
                Plan = null;
                WalkbackLog.Warn("Mission plan B failed, the verdict stays NO ROUTE YET: " + e.Message);
            }
        }

        /// <summary>v1.1: the current plan's point of no return (found = false when the plan fits its tank).</summary>
        public TurnBackPoint.Result TurnBack { get; private set; }

        void ApplyPlan(MissionFallback.Plan plan, bool animate, long milliseconds, string how)
        {
            Plan = plan;
            TurnBack = TurnBackPoint.Find(terrain.Site, terrain.ReturnMinutes, Plan);
            if (Plan.Path.Count >= 2) ShowPlannedRoute(Plan.Path.ToArray(), animate);
            else ClearPlannedRoute();
            ShowPlanOnPanel();
            if (stopPillars != null)
                stopPillars.ShowTurnBack(TurnBack.found ? TurnBack.point : (Vector2?)null,
                    "with " + WhatIfTankPercent.ToString("0") + " % of a tank · ESTIMATE", "with " + WhatIfTankPercent.ToString("0") + " %");
            terrain.SetReturnReach(WhatIfTankPercent < 100f ? MissionFallback.ReachMinutes(WhatIfTankPercent) : float.PositiveInfinity);
            if (TurnBack.found) WalkbackLog.Info("Point of no return: " + TurnBack.metersAlong.ToString("0") + " m along the route, before " + TurnBack.beforeName);
            WalkbackLog.Info("Mission planned in " + milliseconds + " ms" + how + " (plan B): " + MissionFallback.LogLine(Plan));
        }

        // ---- v1.1: planning off the main thread, so a tap on the map does not freeze the headset (~200 ms a plan).

        System.Threading.Tasks.Task<MissionFallback.Plan> pendingPlan;
        bool pendingAnimate;
        System.Diagnostics.Stopwatch pendingWatch;

        /// <summary>True while a plan is being made on a worker thread.</summary>
        public bool PlanPending { get { return pendingPlan != null; } }

        /// <summary>
        /// Plans again on a worker thread (in Play) and shows the result when it is ready; outside Play it plans
        /// at once. A newer request replaces one still on its way. The worker gets a copy of the site's stops.
        /// </summary>
        public void PlanMissionSoon(bool animate)
        {
            if (!Application.isPlaying) { PlanMission(animate); return; }
            if (terrain == null || terrain.Site == null || terrain.Site.Stops == null || terrain.Site.Stops.Length == 0) return;
            SiteData copy = terrain.Site.CopyForPlanning();
            float[] slope = terrain.Slope;
            float tank = WhatIfTankPercent;
            pendingAnimate = animate;
            pendingWatch = System.Diagnostics.Stopwatch.StartNew();
            pendingPlan = System.Threading.Tasks.Task.Run(() => MissionFallback.Make(copy, slope, tank));
        }

        /// <summary>Waits for a plan on its way and shows it (mission start needs the current plan).</summary>
        public void FinishPendingPlan()
        {
            if (pendingPlan == null) return;
            try { pendingPlan.Wait(); } catch (Exception) { }
            ApplyPendingPlan();
        }

        void TickPendingPlan()
        {
            if (pendingPlan != null && pendingPlan.IsCompleted) ApplyPendingPlan();
        }

        void ApplyPendingPlan()
        {
            var task = pendingPlan;
            pendingPlan = null;
            if (task.Status != System.Threading.Tasks.TaskStatus.RanToCompletion)
            {
                WalkbackLog.Warn("Mission plan B failed on the worker, the last plan stays: " + (task.Exception != null ? task.Exception.GetBaseException().Message : task.Status.ToString()));
                return;
            }
            ApplyPlan(task.Result, pendingAnimate, pendingWatch != null ? pendingWatch.ElapsedMilliseconds : 0, " on a worker");
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
            // v1.1: where to turn back, when the plan does not fit its tank.
            else if (TurnBack.found) note = TurnBackPoint.Note(TurnBack, WhatIfTankPercent);
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
            TickPendingPlan();
            TickSuitsDeferred(Time.unscaledTime);
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
