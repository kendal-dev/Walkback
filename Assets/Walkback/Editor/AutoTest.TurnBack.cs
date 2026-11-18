// Written with Claude Code (AI), directed and tested by team MARSXR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Walkback.EditorTools
{
    /// <summary>v1.1 test checks: the point of no return (red TURN BACK BY HERE pillar).</summary>
    public static partial class AutoTest
    {
        static void CheckTurnBack(WalkbackApp app, MissionPanel panel)
        {
            SiteData site = app.terrain.Site;
            app.SetWhatIfTank(100f);
            Check("Turn back: with a full tank the plan fits and there is no red pillar",
                app.Plan != null && app.Plan.Viable && !app.TurnBack.found && app.stopPillars.TurnBack == null);

            foreach (float tank in new[] { 15f, 8f })
            {
                app.SetWhatIfTank(tank);
                MissionFallback.Plan plan = app.Plan;
                TurnBackPoint.Result r = app.TurnBack;
                string label = tank.ToString("0") + " %";
                Check("Turn back (" + label + "): a plan that fails at a stop has a point of no return",
                    plan.FailsAtStop < 0 || r.found, "fails at stop " + plan.FailsAtStop + ", found " + r.found);
                if (!r.found) continue;

                // At the point the oxygen still covers the way home; one step further it does not (or the stop's work does not fit).
                float available = plan.TankLiters - HudEstimate.ReserveLiters;
                float used = UsedAlong(plan, r.pathIndex, false);
                float back = app.terrain.ReturnMinutes[ReturnFallback.NodeOf(site, r.point)] * HudEstimate.WalkLitersPerMinute;
                bool safeHere = available - used >= back - 0.01f;
                bool failsNext;
                if (r.atStopWithoutWork)
                {
                    failsNext = available - UsedAlong(plan, r.pathIndex, true) < back;
                }
                else
                {
                    int next = r.pathIndex + 1;
                    float backNext = app.terrain.ReturnMinutes[ReturnFallback.NodeOf(site, plan.Path[next])] * HudEstimate.WalkLitersPerMinute;
                    failsNext = available - UsedAlong(plan, next, false) < backNext;
                }
                int failLeg = -1;
                for (int i = 0; i < plan.Legs.Count; i++) if (plan.Legs[i].ToStop == plan.FailsAtStop) failLeg = i;
                Check("Turn back (" + label + "): at the point the oxygen still covers the way home, one step on it does not, and it comes no later than the failing stop",
                    safeHere && failsNext && (failLeg < 0 || r.pathIndex <= plan.Legs[failLeg].LastPoint),
                    r.metersAlong.ToString("0") + " m, before " + r.beforeName + (r.atStopWithoutWork ? " (no time for the work)" : ""));

                PillarView red = app.stopPillars.TurnBack;
                Vector3 expected = RouteGeometry.ToMap(site, r.point, 0f);
                Text onPillar = app.stopPillars.TurnBackTagOnPillar;
                bool said = red != null && (red.label != null ? red.label.text == StopPillars.TurnBackText && onPillar == null
                    : onPillar != null && onPillar.text.StartsWith(StopPillars.TurnBackHereText) && onPillar.color == StopPillars.TurnBackColor);
                Canvas.ForceUpdateCanvases();
                bool tagFits = onPillar == null || onPillar.preferredWidth <= ((RectTransform)onPillar.transform.parent).rect.width;
                Check("Turn back (" + label + "): a red pillar stands there, not pressable, and says TURN BACK (its own label, or the tag line of the stop it stands at, fitting its box)",
                    said && tagFits && red.baseColor == StopPillars.TurnBackColor && red.hit == null &&
                    Vector3.Distance(red.transform.localPosition, expected) < 0.5f && red.GetComponentInChildren<Collider>() == null,
                    onPillar != null ? onPillar.text : red != null && red.label != null ? red.label.text : "no pillar");
                Canvas.ForceUpdateCanvases();
                Check("Turn back (" + label + "): the verdict card says where to turn back, in one line",
                    panel.verdictNote.text.StartsWith("WHAT IF " + label + ": ") && panel.verdictNote.text.Contains("ESTIMATE") &&
                    panel.verdictNote.preferredWidth <= ((RectTransform)panel.verdictNote.transform).rect.width,
                    panel.verdictNote.text + " (" + panel.verdictNote.preferredWidth.ToString("0") + " px)");
            }

            // No label covers another from the user's place with the red pillar on the map (tank at 8 %).
            var cam = app.desktopCamera;
            Vector3 wasAt = cam.transform.position;
            Quaternion wasLooking = cam.transform.rotation;
            cam.transform.position = MissionPanel.UserEye;
            cam.transform.rotation = Quaternion.LookRotation(app.mapRoot.position - MissionPanel.UserEye, Vector3.up);
            app.stopPillars.SetGrowth(1f);
            app.stopPillars.FaceCamera(cam);
            Canvas.ForceUpdateCanvases();
            var views = new List<PillarView> { app.stopPillars.Habitat };
            views.AddRange(app.stopPillars.Stops);
            if (app.stopPillars.TurnBack != null && app.stopPillars.TurnBack.labelRoot != null) views.Add(app.stopPillars.TurnBack);
            float worst = 0f;
            string pair = "none";
            for (int i = 0; i < views.Count; i++)
                for (int j = 0; j < i; j++)
                {
                    Rect a = LabelViewRect(cam, views[i].labelRoot), b = LabelViewRect(cam, views[j].labelRoot);
                    float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                    if (w <= 0f || h <= 0f) continue;
                    float share = w * h / Mathf.Max(1e-9f, Mathf.Min(a.width * a.height, b.width * b.height));
                    if (share > worst) { worst = share; pair = views[i].label.text + " / " + views[j].label.text; }
                }
            Check("Turn back: with the red pillar no label covers another from the user's place (under 5 %)", worst < 0.05f,
                (worst * 100f).ToString("F0") + " % (" + pair + ")");
            cam.transform.SetPositionAndRotation(wasAt, wasLooking);

            // The red label sits one step higher: it must not cover the main board's texts either.
            if (app.stopPillars.TurnBack != null)
            {
                Note("Turn back: the pillar labels against the main board, with the red pillar on the map (tank at 8 %):");
                CheckPillarLabelsAgainstBoard(app, panel);
            }

            Text host = app.stopPillars.TurnBackTagOnPillar;
            app.SetWhatIfTank(100f);
            Check("Turn back: back to a full tank the red pillar goes away and the stop's tag line is its own again",
                !app.TurnBack.found && app.stopPillars.TurnBack == null && app.stopPillars.TurnBackTagOnPillar == null &&
                (host == null || !host.text.StartsWith(StopPillars.TurnBackHereText) && host.color != StopPillars.TurnBackColor), host != null ? host.text : "");
            CheckTurnBackMidLeg(app, panel);
            CheckTurnBackOnVisor(app);
            CheckReachOnStopCard(app, panel);
        }

        /// <summary>
        /// The point of no return in the middle of a leg (never seen with Gale's four stops): one far stop with no
        /// work and a tank at 8 %, on a copy of the site. Then the red pillar with its own label on the real map.
        /// </summary>
        static void CheckTurnBackMidLeg(WalkbackApp app, MissionPanel panel)
        {
            SiteData site = app.terrain.Site;
            float[] home = app.terrain.ReturnMinutes;
            // The walkable point farthest from the habitat (in minutes back), clear of the data's pillars.
            int n = site.GridSize, best = -1;
            for (int k = 0; k < n * n; k += 7)
            {
                if (float.IsInfinity(home[k]) || (best >= 0 && home[k] <= home[best])) continue;
                Vector2 p = new Vector2((k % n) * site.NodeSpacing, site.SideMeters - (k / n) * site.NodeSpacing);
                if (MissionFallback.Walkable(site, app.terrain.Slope, p) && UserStops.Refusal(site, p) == null) best = k;
            }
            if (best < 0) { Check("Turn back mid-leg: there is a far walkable point", false); return; }
            Vector2 far = new Vector2((best % n) * site.NodeSpacing, site.SideMeters - (best / n) * site.NodeSpacing);
            SiteData copy = site.CopyForPlanning();
            copy.Stops = new[] { UserStops.Make(1, far) };
            MissionFallback.Plan plan = MissionFallback.Make(copy, app.terrain.Slope, 8f);
            TurnBackPoint.Result r = TurnBackPoint.Find(copy, home, plan);
            float legMeters = plan.Legs.Count > 0 ? plan.Legs[0].Meters : 0f;
            string note = TurnBackPoint.Note(r, 8f);
            Check("Turn back mid-leg: one far stop with no work at 8 % gives a point in the middle of the way out, before that stop",
                plan.HasRoute && r.found && !r.atStopWithoutWork && r.pathIndex > 0 && r.metersAlong > 0f && r.metersAlong < legMeters &&
                note.Contains(" m along the route, before My stop 1"),
                note + " (" + home[best].ToString("0") + " min back from the stop, leg " + legMeters.ToString("0") + " m)");
            float available = plan.TankLiters - HudEstimate.ReserveLiters;
            float backHere = home[ReturnFallback.NodeOf(copy, r.point)] * HudEstimate.WalkLitersPerMinute;
            float backNext = home[ReturnFallback.NodeOf(copy, plan.Path[r.pathIndex + 1])] * HudEstimate.WalkLitersPerMinute;
            Check("Turn back mid-leg: at the point the oxygen covers the way home, one point on it does not",
                available - UsedAlong(plan, r.pathIndex, false) >= backHere - 0.01f && available - UsedAlong(plan, r.pathIndex + 1, false) < backNext);

            // That point on the real map: a red pillar with its own TURN BACK BY HERE label, clear of the other labels and of the board.
            app.stopPillars.ShowTurnBack(r.point, "with 8 % of a tank · ESTIMATE", "with 8 %");
            PillarView red = app.stopPillars.TurnBack;
            bool own = red != null && red.label != null && red.label.text == StopPillars.TurnBackText && app.stopPillars.TurnBackTagOnPillar == null;
            Check("Turn back mid-leg: away from the pillars the red pillar has its own TURN BACK BY HERE label", own);
            if (own)
            {
                Note("Turn back mid-leg: the pillar labels against the main board, with the red pillar's own label:");
                CheckPillarLabelsAgainstBoard(app, panel);
                var cam = app.desktopCamera;
                Vector3 wasAt = cam.transform.position;
                Quaternion wasLooking = cam.transform.rotation;
                cam.transform.position = MissionPanel.UserEye;
                cam.transform.rotation = Quaternion.LookRotation(app.mapRoot.position - MissionPanel.UserEye, Vector3.up);
                app.stopPillars.FaceCamera(cam);
                Canvas.ForceUpdateCanvases();
                Rect mine = LabelViewRect(cam, red.labelRoot);
                float worst = 0f;
                string with = "none";
                var others = new List<PillarView> { app.stopPillars.Habitat };
                others.AddRange(app.stopPillars.Stops);
                foreach (PillarView o in others)
                {
                    Rect b = LabelViewRect(cam, o.labelRoot);
                    float w = Mathf.Min(mine.xMax, b.xMax) - Mathf.Max(mine.xMin, b.xMin), h = Mathf.Min(mine.yMax, b.yMax) - Mathf.Max(mine.yMin, b.yMin);
                    if (w <= 0f || h <= 0f) continue;
                    float share = w * h / Mathf.Max(1e-9f, Mathf.Min(mine.width * mine.height, b.width * b.height));
                    if (share > worst) { worst = share; with = o.label.text; }
                }
                Check("Turn back mid-leg: the red label covers no other label from the user's place (under 5 %)", worst < 0.05f,
                    (worst * 100f).ToString("F0") + " % (" + with + "), slid " + app.stopPillars.TurnBackSlide.ToString("0.0") + " label widths");
                cam.transform.SetPositionAndRotation(wasAt, wasLooking);
            }
            app.stopPillars.ShowTurnBack(null, null, null);
            Check("Turn back mid-leg: taken away again", app.stopPillars.TurnBack == null);
        }

        /// <summary>v1.1: the stop card's first line: minutes back, or past the point of no return in red.</summary>
        static void CheckReachOnStopCard(WalkbackApp app, MissionPanel panel)
        {
            SiteData site = app.terrain.Site;
            int far = site.Stops.Length - 1;
            app.SetWhatIfTank(100f);
            app.SelectStop(far);
            float back = app.terrain.ReturnMinutes[ReturnFallback.NodeOf(site, new Vector2(site.Stops[far].x, site.Stops[far].y))];
            Check("Stop card: with a full tank it reads the meters and the RETURN layer's minutes back from the stop",
                panel.stopBadge.text.EndsWith(HelmetHud.MinutesText(back) + " back on foot") && panel.stopBadge.color == WalkbackTheme.TextSoft, panel.stopBadge.text);

            app.SetWhatIfTank(8f);
            Canvas.ForceUpdateCanvases();
            bool farLate = panel.stopBadge.text == "Out of reach at 8 % · turn back at the red pillar" && panel.stopBadge.color == WalkbackTheme.Danger;
            bool fitsFar = panel.stopBadge.preferredWidth <= ((RectTransform)panel.stopBadge.transform).rect.width;
            string farText = panel.stopBadge.text;
            int first = -1;
            foreach (MissionFallback.Leg leg in app.Plan.Legs) if (leg.LastPoint == app.TurnBack.pathIndex && leg.ToStop >= 0) first = leg.ToStop;
            bool atWork = true, fitsWork = true;
            string workText = "";
            if (app.TurnBack.atStopWithoutWork && first >= 0)
            {
                app.SelectStop(first);
                Canvas.ForceUpdateCanvases();
                workText = panel.stopBadge.text;
                atWork = workText == "At 8 %: no time for the work here · turn back" && panel.stopBadge.color == WalkbackTheme.Danger;
                fitsWork = panel.stopBadge.preferredWidth <= ((RectTransform)panel.stopBadge.transform).rect.width;
            }
            Check("Stop card: at 8 % a stop past the point of no return says out of reach, and the stop where the work no longer fits says so, in red, on one line",
                farLate && atWork && fitsFar && fitsWork, farText + " | " + workText);
            app.SetWhatIfTank(100f);
            Check("Stop card: back to a full tank the line is the minutes back again", panel.stopBadge.text.EndsWith(" back on foot"), panel.stopBadge.text);
            app.SelectStop(-1);
        }

        /// <summary>v1.1: the visor while walking says how far the point of no return is.</summary>
        static void CheckTurnBackOnVisor(WalkbackApp app)
        {
            SiteData site = app.terrain.Site;
            app.SetWhatIfTank(8f);
            var progress = new MissionProgress(site, app.Plan, app.terrain.ReturnMinutes);
            MissionProgress.Snapshot s = progress.Read(site.Habitat);
            string line = HelmetHud.ReturnLastLine(s);
            float expected = app.TurnBack.found ? progress.TurnBackAt : -1f;
            Check("Visor: at 8 % the minutes-back card says how far the point of no return is (route meters from here)",
                app.TurnBack.found && Mathf.Abs(s.turnBackInMeters - expected) < 0.5f && line == "turn back in " + HelmetHud.MetersText(expected) && !s.turnBack, line);
            app.SetWhatIfTank(100f);
            progress = new MissionProgress(site, app.Plan, app.terrain.ReturnMinutes);
            s = progress.Read(site.Habitat);
            Check("Visor: with a full tank there is no point of no return and the line is the oxygen margin again",
                s.turnBackInMeters < 0f && HelmetHud.ReturnLastLine(s).StartsWith("spare O2"), HelmetHud.ReturnLastLine(s));
        }

        /// <summary>Liters used walking the plan up to a path point (work of the stops passed included; with <paramref name="withWorkHere"/>, the work at that point's stop too).</summary>
        static float UsedAlong(MissionFallback.Plan plan, int index, bool withWorkHere)
        {
            float used = 0f;
            foreach (MissionFallback.Leg leg in plan.Legs)
            {
                float legMeters = Mathf.Max(0.001f, leg.Meters);
                for (int p = leg.FirstPoint + 1; p <= leg.LastPoint; p++)
                {
                    if (p > index) return used;
                    used += leg.Minutes * (Vector2.Distance(plan.Path[p - 1], plan.Path[p]) / legMeters) * HudEstimate.WalkLitersPerMinute;
                }
                if (leg.LastPoint == index && !withWorkHere) return used;
                used += leg.WorkMinutesAtEnd * HudEstimate.DesignLitersPerMinute;
                if (leg.LastPoint == index) return used;
            }
            return used;
        }

        /// <summary>v1.1 screenshot: the map and the board with a WHAT IF at 8 % and the red pillar.</summary>
        static void ShotTurnBack(WalkbackApp app, Camera cam, Vector3 panelEye)
        {
            app.SetWhatIfTank(8f);
            if (!app.TurnBack.found) { app.SetWhatIfTank(100f); return; }
            Vector3 map = app.mapRoot.position;
            Vector3 above = map + new Vector3(0f, 0.75f, -0.75f);
            cam.transform.SetPositionAndRotation(above, Quaternion.LookRotation(map - above, Vector3.up));
            app.stopPillars.SetGrowth(1f);
            app.stopPillars.FaceCamera(cam);
            Canvas.ForceUpdateCanvases();
            Shot(cam, "v11_05_turn_back_on_the_map.png", above, map);
            Shot(cam, "v11_06_turn_back_on_the_board.png", panelEye, SceneBuilder.PanelCenter);
            app.SetWhatIfTank(100f);
        }
    }
}
