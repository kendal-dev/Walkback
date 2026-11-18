// Written with Claude Code (AI), directed and tested by team MARSXR
using System.Linq;
using UnityEngine;

namespace Walkback.EditorTools
{
    /// <summary>v1.1 test checks: stops the astronaut adds by tapping the map, planned again at once.</summary>
    public static partial class AutoTest
    {
        static void CheckUserStops(WalkbackApp app, MissionPanel panel)
        {
            SiteData site = app.terrain != null ? app.terrain.Site : null;
            if (site == null || site.Stops.Length == 0)
            {
                Check("My stops: the site has stops to plan with", false, "no site");
                return;
            }
            StopInfo[] original = site.Stops;
            int selectedBefore = app.SelectedStop;

            // Pure parts first.
            var habitat = new Vector2(500f, 500f);
            var square = new[]
            {
                new StopInfo { id = "a", name = "A", x = 600f, y = 500f },
                new StopInfo { id = "b", name = "B", x = 600f, y = 600f },
                new StopInfo { id = "c", name = "C", x = 500f, y = 600f },
            };
            Check("My stops: a new stop goes where it adds the least walking (between A and B, not at the end)",
                UserStops.BestSlot(habitat, square, new Vector2(620f, 550f)) == 1 &&
                UserStops.BestSlot(habitat, square, new Vector2(480f, 560f)) == 3 &&
                UserStops.BestSlot(habitat, square, new Vector2(560f, 480f)) == 0);
            StopInfo made = UserStops.Make(2, new Vector2(10f, 20f));
            Check("My stops: a picked stop has no tasks and no photo, and says so (rule 1)",
                UserStops.IsUser(made) && made.name == "My stop 2" && made.tasks.Length == 0 && string.IsNullOrEmpty(made.panorama) &&
                made.tag == UserStops.Tag && !UserStops.IsUser(square[0]));
            Check("My stops: no stop outside the map, on the habitat or on another pillar",
                UserStops.Refusal(site, new Vector2(-5f, 10f)) != null &&
                UserStops.Refusal(site, site.Habitat + new Vector2(3f, 0f)) != null &&
                UserStops.Refusal(site, new Vector2(original[0].x + 2f, original[0].y)) != null);

            // A walkable place, clear of every pillar, near the habitat.
            Vector2 spot;
            if (!FindSpot(site, app.terrain.Slope, true, out spot))
            {
                Check("My stops: there is walkable ground to tap on", false, "none found");
                return;
            }
            app.SetWhatIfTank(100f);
            app.PlanMission(false);
            float before = app.Plan != null ? app.Plan.WalkMeters : 0f;
            int legsBefore = app.Plan != null ? app.Plan.Legs.Count : 0;
            int changed = 0;
            System.Action listen = () => changed++;
            app.UserStopsChanged += listen;

            int index = app.AddUserStop(spot);
            Check("My stops: adding one puts it in the route, selects it and plans again (one leg more, still ESTIMATE)",
                index >= 0 && site.Stops.Length == original.Length + 1 && UserStops.IsUser(site.Stops[index]) && app.SelectedStop == index &&
                app.Plan != null && app.Plan.Legs.Count == legsBefore + 1 && changed == 1 && panel.verdictNote.text.Contains("ESTIMATE"),
                "stop " + index + ", " + before.ToString("0") + " m -> " + (app.Plan != null ? app.Plan.WalkMeters.ToString("0") : "?") + " m");
            Check("My stops: it has a blue pillar and its card says it was picked here, with 0 minutes of work and no 360° PHOTO",
                app.stopPillars.Stops.Count == site.Stops.Length && app.stopPillars.Stops[index].baseColor == StopPillars.UserStopColor &&
                panel.stopName.text == "My stop 1" && !panel.jumpButton.Interactable && panel.stopScience.text == UserStops.Science && panel.stopTasks.text == "Work: 0 min · your estimate",
                panel.stopName.text + " / " + panel.stopTasks.text);
            Check("My stops: the stop buttons stay the data's stops, and CLEAR is on",
                panel.stopButtons.Count == Mathf.Min(original.Length, 4) && panel.clearStopsButton != null && panel.clearStopsButton.Interactable &&
                panel.stopButtons.All(b => !b.Selected));
            Check("My stops: the plan visits it (one leg ends there)",
                app.Plan != null && app.Plan.Legs.Any(l => l.ToStop == index));

            CheckUserStopMinutes(app, panel, index);
            CheckUserStopLabels(app, index);
            CheckPlanOnWorker(app);
            CheckRemoveAndProbe(app, panel);

            // Pressing a data stop's button still selects that stop, wherever it now sits in the order.
            panel.stopButtons[0].OnPointerClick(panel.stopButtons[0].transform.position);
            Check("My stops: a stop button still selects its own stop after the order changed",
                app.SelectedStop >= 0 && site.Stops[app.SelectedStop].id == original[0].id && panel.stopButtons[0].Selected);

            // The tap: a short press on bare ground adds a stop; a press on a pillar does not.
            Pointer pointer = app.pointer;
            Spot second;
            bool tapOk = false;
            string tapDetail = "no pointer";
            if (pointer != null && Pointer.AttachTerrainCollider(app.terrain) != null && FindSecond(app, site, out second))
            {
                Physics.SyncTransforms();
                var ray = new Ray(second.world + Vector3.up * 0.5f, Vector3.down);
                int count = site.Stops.Length;
                pointer.Resolve = Pointer.DefaultResolve;
                pointer.Process(ray, false);
                app.TickMapTap();
                pointer.Process(ray, true);
                app.TickMapTap();
                pointer.Process(ray, false);
                app.TickMapTap();
                tapOk = site.Stops.Length == count + 1 && site.Stops.Any(s => s.name == "My stop 2");
                tapDetail = "hit " + (pointer.HitCollider != null ? pointer.HitCollider.name : "nothing") + ", stops " + count + " -> " + site.Stops.Length;

                // On a pillar: the pillar is the target, no stop is added.
                PillarView pillar = app.stopPillars.Stops[0];
                Physics.SyncTransforms();
                Vector3 top = pillar.transform.TransformPoint(new Vector3(0f, StopPillars.PillarHeightMeters / 2f, 0f));
                var onPillar = new Ray(top + Vector3.up * 0.3f, Vector3.down);
                count = site.Stops.Length;
                pointer.Process(onPillar, false);
                app.TickMapTap();
                pointer.Process(onPillar, true);
                app.TickMapTap();
                pointer.Process(onPillar, false);
                app.TickMapTap();
                tapOk &= site.Stops.Length == count;
                pointer.Process(new Ray(Vector3.zero, Vector3.down), false, false);
            }
            Check("My stops: a tap on bare ground adds a stop there; a tap on a pillar does not", tapOk, tapDetail);

            // Ground too steep to walk: the plan says so instead of making a route up.
            Vector2 steep;
            if (FindSpot(site, app.terrain.Slope, false, out steep) && UserStops.Count(site.Stops) < UserStops.MaxStops)
            {
                app.AddUserStop(steep);
                Check("My stops: a stop on ground steeper than " + TerrainLayers.SlopeLimitDegrees.ToString("0") + "° gives NO SAFE ROUTE",
                    app.Plan != null && !app.Plan.HasRoute && panel.verdictTitle.text == "NO SAFE ROUTE", panel.verdictTitle.text);
            }

            // No more than the limit.
            while (UserStops.Count(site.Stops) < UserStops.MaxStops)
            {
                Vector2 more;
                if (!FindSpot(site, app.terrain.Slope, true, out more)) break;
                if (app.AddUserStop(more) < 0) break;
            }
            Vector2 extra;
            int full = site.Stops.Length;
            bool limited = !FindSpot(site, app.terrain.Slope, true, out extra) || app.AddUserStop(extra) < 0;
            Check("My stops: at most " + UserStops.MaxStops + " picked stops", limited && site.Stops.Length == full && UserStops.Count(site.Stops) <= UserStops.MaxStops);

            // CLEAR: the data's stops and their plan come back.
            panel.clearStopsButton.OnPointerClick(panel.clearStopsButton.transform.position);
            bool asked = panel.ClearArmed && panel.clearStopsButton.label.text == MissionPanel.ClearConfirmText && UserStops.Count(site.Stops) > 0;
            Check("My stops: the first press of CLEAR only asks (SURE?) and takes nothing away", asked,
                panel.clearStopsButton.label.text + ", " + UserStops.Count(site.Stops) + " picked");
            panel.clearStopsButton.OnPointerClick(panel.clearStopsButton.transform.position);
            app.UserStopsChanged -= listen;
            Check("My stops: CLEAR takes them away; the data's stops, their plan and their pillars come back, CLEAR is off",
                site.Stops.Length == original.Length && site.Stops.Select(s => s.id).SequenceEqual(original.Select(s => s.id)) &&
                app.Plan != null && Mathf.Abs(app.Plan.WalkMeters - before) < 0.5f && app.stopPillars.Stops.Count == original.Length &&
                !panel.clearStopsButton.Interactable,
                app.Plan != null ? app.Plan.WalkMeters.ToString("0") + " m" : "no plan");

            site.Stops = original;
            app.ShowRouteStartState(false);
            panel.BuildStopButtons(site.Stops);
            app.PlanMission(false);
            app.SelectStop(selectedBefore);
        }

        /// <summary>The -5 / +5 buttons of a picked stop's card (stop at <paramref name="index"/>, selected, 0 min).</summary>
        static void CheckUserStopMinutes(WalkbackApp app, MissionPanel panel, int index)
        {
            SiteData site = app.terrain.Site;
            var less = panel.lessMinutesButton;
            var more = panel.moreMinutesButton;
            if (less == null || more == null)
            {
                Check("My stop minutes: the card has -5 / +5 buttons", false, "not built");
                return;
            }
            Check("My stop minutes: a picked stop's card shows -5 / +5, with -5 off at 0 minutes",
                less.gameObject.activeInHierarchy && more.gameObject.activeInHierarchy && !less.Interactable && more.Interactable);

            float work = app.Plan.WorkMinutes, total = app.Plan.TotalMinutes, used = app.Plan.UsedLiters;
            for (int i = 0; i < 3; i++) more.OnPointerClick(more.transform.position);
            Check("My stop minutes: three +5 make 15 minutes of work there, and the plan counts them (time and oxygen)",
                Mathf.Approximately(UserStops.Minutes(site.Stops[index]), 15f) && Mathf.Abs(app.Plan.WorkMinutes - work - 15f) < 0.01f &&
                app.Plan.TotalMinutes > total + 14.9f && app.Plan.UsedLiters > used && app.SelectedStop == index &&
                panel.stopTasks.text == "Work: 15 min · your estimate" && less.Interactable,
                panel.stopTasks.text + ", work " + work.ToString("0.0") + " -> " + app.Plan.WorkMinutes.ToString("0.0") + " min");
            less.OnPointerClick(less.transform.position);
            Check("My stop minutes: -5 takes 5 away", Mathf.Approximately(UserStops.Minutes(site.Stops[index]), 10f) && Mathf.Abs(app.Plan.WorkMinutes - work - 10f) < 0.01f);
            Check("My stop minutes: the work line and the buttons fit on the card without covering each other",
                panel.stopTasks.preferredWidth + 54f <= 464f, panel.stopTasks.preferredWidth.ToString("0") + " px");

            var stop = new StopInfo();
            UserStops.SetMinutes(stop, 500f);
            float top = UserStops.Minutes(stop);
            UserStops.SetMinutes(stop, -20f);
            Check("My stop minutes: from 0 to " + UserStops.MaxMinutes.ToString("0") + " min, in steps of 5, and 0 means no task",
                Mathf.Approximately(top, UserStops.MaxMinutes) && stop.tasks.Length == 0);

            int data = -1;
            for (int i = 0; i < site.Stops.Length; i++) if (!UserStops.IsUser(site.Stops[i])) { data = i; break; }
            float dataWork = HudEstimate.TaskMinutes(site.Stops[data]);
            app.SelectStop(data);
            bool hidden = !less.gameObject.activeInHierarchy && !more.gameObject.activeInHierarchy;
            Check("My stop minutes: the data's stops have no -5 / +5 and their tasks cannot change",
                hidden && app.ChangeUserStopMinutes(data, 5f) < 0f && Mathf.Approximately(HudEstimate.TaskMinutes(site.Stops[data]), dataWork));

            app.SelectStop(index);
            app.stopPillars.Stops[data].OnPointerEnter();
            Check("My stop minutes: no -5 / +5 while another stop is previewed", !less.gameObject.activeInHierarchy);
            app.stopPillars.Stops[data].OnPointerExit();
            Check("My stop minutes: back on the picked stop's card they come back", less.gameObject.activeInHierarchy && panel.stopTasks.text.StartsWith("Work: 10 min"));
            app.SelectStop(index);
        }

        /// <summary>v1.1 screenshots: the map with a picked stop, its card with minutes, and a preview of another stop.</summary>
        static void ShotsUserStops(WalkbackApp app, MissionPanel panel, Camera cam, Vector3 panelEye)
        {
            SiteData site = app.terrain.Site;
            StopInfo[] original = site.Stops;
            Vector2 spot;
            if (!FindSpot(site, app.terrain.Slope, true, out spot)) return;
            int index = app.AddUserStop(spot);
            if (index < 0) return;
            app.ChangeUserStopMinutes(index, 15f);
            app.terrain.ShowLayer(MapLayer.Photo);
            app.RefreshPanel();
            Vector3 map = app.mapRoot.position;
            // The labels turn and go up for the camera's place: put the camera there first.
            Vector3 above = map + new Vector3(0f, 0.75f, -0.75f);
            cam.transform.SetPositionAndRotation(above, Quaternion.LookRotation(map - above, Vector3.up));
            app.stopPillars.SetGrowth(1f);
            app.stopPillars.FaceCamera(cam);
            Vector2 probeAt = spot + new Vector2(0f, -120f);
            app.ShowProbeAt(app.terrain.transform.TransformPoint(RouteGeometry.ToMap(site, probeAt, 0f)), above);
            Canvas.ForceUpdateCanvases();
            Shot(cam, "v11_01_my_stop_on_the_map.png", above, map);
            app.ShowProbeAt(null, above);
            if (panel.sideBoard != null)
            {
                Vector3 side = panel.sideBoard.position;
                Shot(cam, "v11_02_my_stop_card_minutes.png", side - panel.sideBoard.forward * 0.85f, side);
                int data = -1;
                for (int i = 0; i < site.Stops.Length; i++) if (!UserStops.IsUser(site.Stops[i])) { data = i; break; }
                app.stopPillars.Stops[data].OnPointerEnter();
                Canvas.ForceUpdateCanvases();
                Shot(cam, "v11_03_preview_of_a_stop.png", side - panel.sideBoard.forward * 0.85f, side);
                app.stopPillars.Stops[data].OnPointerExit();
            }
            Shot(cam, "v11_04_plan_with_my_stop.png", panelEye, SceneBuilder.PanelCenter);
            site.Stops = original;
            app.ShowRouteStartState(false);
            panel.BuildStopButtons(site.Stops);
            app.PlanMission(false);
            app.SelectStop(-1);
        }

        /// <summary>With a picked stop on the map, no pillar label covers another, from the user's place and from above the table.</summary>
        static void CheckUserStopLabels(WalkbackApp app, int index)
        {
            var cam = app.desktopCamera;
            Vector3 wasAt = cam.transform.position;
            Quaternion wasLooking = cam.transform.rotation;
            Vector3 map = app.mapRoot.position;
            var poses = new[] { MissionPanel.UserEye, map + new Vector3(0f, 0.75f, -0.75f) };
            string report = "";
            float worstAll = 0f;
            foreach (Vector3 eye in poses)
            {
                cam.transform.position = eye;
                cam.transform.rotation = Quaternion.LookRotation(map - eye, Vector3.up);
                app.stopPillars.SetGrowth(1f);
                app.stopPillars.FaceCamera(cam);
                Canvas.ForceUpdateCanvases();
                var views = new System.Collections.Generic.List<PillarView> { app.stopPillars.Habitat };
                views.AddRange(app.stopPillars.Stops);
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
                worstAll = Mathf.Max(worstAll, worst);
                report += (report.Length > 0 ? "; " : "") + (worst * 100f).ToString("F0") + " % (" + pair + ")";
            }
            Check("My stops: with a picked stop no pillar label covers another, from the user's place or from above the table (under 5 %)", worstAll < 0.05f, report);
            cam.transform.SetPositionAndRotation(wasAt, wasLooking);
            app.stopPillars.FaceCamera(cam);
        }

        /// <summary>The plan made on a worker thread is the same plan, and the worker's copy of the stops is its own.</summary>
        static void CheckPlanOnWorker(WalkbackApp app)
        {
            SiteData site = app.terrain.Site;
            SiteData copy = site.CopyForPlanning();
            float tank = app.WhatIfTankPercent;
            var task = System.Threading.Tasks.Task.Run(() => MissionFallback.Make(copy, app.terrain.Slope, tank));
            bool done = task.Wait(20000);
            MissionFallback.Plan here = MissionFallback.Make(site, app.terrain.Slope, tank);
            Check("Worker plan: planning on a worker thread gives the same route, time and oxygen",
                done && task.Result.Path.Count == here.Path.Count && Mathf.Abs(task.Result.WalkMeters - here.WalkMeters) < 0.01f &&
                Mathf.Abs(task.Result.TotalMinutes - here.TotalMinutes) < 0.01f && Mathf.Abs(task.Result.UsedLiters - here.UsedLiters) < 0.01f,
                done ? task.Result.WalkMeters.ToString("0") + " m vs " + here.WalkMeters.ToString("0") + " m" : "timed out");
            int user = -1;
            for (int i = 0; i < site.Stops.Length; i++) if (UserStops.IsUser(site.Stops[i])) { user = i; break; }
            float before = user >= 0 ? HudEstimate.TaskMinutes(copy.Stops[user]) : 0f;
            if (user >= 0) UserStops.SetMinutes(site.Stops[user], before + 5f);
            Check("Worker plan: the worker's copy keeps its own stops and minutes while the app changes them",
                user >= 0 && copy.Stops != site.Stops && copy.Heights == site.Heights && Mathf.Approximately(HudEstimate.TaskMinutes(copy.Stops[user]), before) &&
                Mathf.Approximately(HudEstimate.TaskMinutes(site.Stops[user]), before + 5f));
            if (user >= 0) UserStops.SetMinutes(site.Stops[user], before);
            app.PlanMissionSoon(false);
            Check("Worker plan: outside Play it plans at once (nothing left pending)", !app.PlanPending);
        }

        /// <summary>REMOVE on a picked stop's card, and the ground probe's card.</summary>
        static void CheckRemoveAndProbe(WalkbackApp app, MissionPanel panel)
        {
            SiteData site = app.terrain.Site;
            Vector2 spot;
            if (!FindSpot(site, app.terrain.Slope, true, out spot)) return;
            int count = site.Stops.Length;
            int index = app.AddUserStop(spot);
            bool swapped = panel.removeStopButton != null && panel.removeStopButton.gameObject.activeInHierarchy && !panel.jumpButton.gameObject.activeInHierarchy;
            if (panel.removeStopButton != null) panel.removeStopButton.OnPointerClick(panel.removeStopButton.transform.position);
            Check("My stops: REMOVE takes the place of 360° PHOTO on a picked stop's card and takes that one stop away",
                index >= 0 && swapped && site.Stops.Length == count && app.SelectedStop == -1 && panel.jumpButton.gameObject.activeInHierarchy &&
                !panel.removeStopButton.gameObject.activeInHierarchy && app.stopPillars.Stops.Count == count);
            int dataIndex = -1;
            for (int i = 0; i < site.Stops.Length; i++) if (!UserStops.IsUser(site.Stops[i])) { dataIndex = i; break; }
            Check("My stops: REMOVE does nothing to a data stop", !app.RemoveUserStop(dataIndex) && site.Stops.Length == count);

            // The probe card: minutes back and slope where the pointer is.
            Vector3 eye = MissionPanel.UserEye;
            Vector3 world = app.terrain.transform.TransformPoint(RouteGeometry.ToMap(site, spot, 0f));
            app.ShowProbeAt(world, eye);
            Canvas.ForceUpdateCanvases();
            GlobeLabel card = app.ProbeCard;
            int node = ReturnFallback.NodeOf(site, spot);
            string expected = HelmetHud.MinutesText(app.terrain.ReturnMinutes[node]) + " back on foot";
            bool fits = card != null && card.lines.All(t => t.preferredWidth <= ((RectTransform)t.transform).rect.width + 0.5f);
            Check("Probe: on walkable ground the card says the RETURN layer's minutes back, the slope, and that a tap adds a stop",
                app.ProbeShown && card.lines[0].text.StartsWith(expected) && card.lines[0].text.Contains("° slope") &&
                card.lines[1].text == "ESTIMATE · tap to add a stop here" && card.transform.position.y > world.y, card != null ? card.lines[0].text : "no card");
            Check("Probe: both lines fit the card on one line", fits,
                card != null ? string.Join(" / ", card.lines.Select(t => t.preferredWidth.ToString("0") + " in " + ((RectTransform)t.transform).rect.width.ToString("0")).ToArray()) : "");
            Vector2 steep;
            if (FindSpot(site, app.terrain.Slope, false, out steep))
            {
                app.ShowProbeAt(app.terrain.transform.TransformPoint(RouteGeometry.ToMap(site, steep, 0f)), eye);
                Check("Probe: on ground over 20° it says too steep, in red", card.lines[0].text.Contains("too steep") && card.lines[0].color == WalkbackTheme.Danger, card.lines[0].text);
            }
            app.SetWhatIfTank(8f);
            float reach = app.terrain.ReturnReachMinutes;
            Vector2 far = Vector2.zero;
            bool foundFar = false;
            for (int k = 0; k < site.GridSize * site.GridSize && !foundFar; k += 37)
            {
                float m = app.terrain.ReturnMinutes[k];
                if (float.IsInfinity(m) || m <= reach + 1f) continue;
                int row = k / site.GridSize, col = k % site.GridSize;
                far = new Vector2(col * site.NodeSpacing, site.SideMeters - row * site.NodeSpacing);
                foundFar = MissionFallback.Walkable(site, app.terrain.Slope, far);
            }
            if (foundFar)
            {
                app.ShowProbeAt(app.terrain.transform.TransformPoint(RouteGeometry.ToMap(site, far, 0f)), eye);
                Check("Probe: in a WHAT IF at 8 %, ground too far to walk back from says so, in red",
                    card.lines[0].text.Contains("too far with 8 %") && card.lines[0].color == WalkbackTheme.Danger, card.lines[0].text);
            }
            app.SetWhatIfTank(100f);
            app.ShowProbeAt(null, eye);
            Check("Probe: off the ground the card hides", !app.ProbeShown);
        }

        struct Spot { public Vector2 meters; public Vector3 world; }

        /// <summary>A second walkable spot, clear of the pillars, with its point on the terrain in the room.</summary>
        static bool FindSecond(WalkbackApp app, SiteData site, out Spot spot)
        {
            spot = new Spot();
            Vector2 m;
            if (!FindSpot(site, app.terrain.Slope, true, out m)) return false;
            spot.meters = m;
            spot.world = app.terrain.transform.TransformPoint(RouteGeometry.ToMap(site, m, 0f));
            return true;
        }

        /// <summary>
        /// The nearest place to the habitat, on a ring of points, that a tap may use and whose ground is
        /// walkable (or, with walkable = false, steeper than the limit).
        /// </summary>
        static bool FindSpot(SiteData site, float[] slope, bool walkable, out Vector2 spot)
        {
            for (float r = 60f; r < site.SideMeters / 2f; r += 25f)
                for (int k = 0; k < 24; k++)
                {
                    float a = k * Mathf.PI * 2f / 24f;
                    Vector2 p = site.Habitat + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (UserStops.Refusal(site, p) != null) continue;
                    bool ok = MissionFallback.Walkable(site, slope, p);
                    if (walkable && ok && MissionFallback.Walkable(site, slope, p + new Vector2(6f, 0f)) && MissionFallback.Walkable(site, slope, p - new Vector2(6f, 0f)) ||
                        !walkable && !ok)
                    {
                        spot = p;
                        return true;
                    }
                }
            spot = Vector2.zero;
            return false;
        }
    }
}
