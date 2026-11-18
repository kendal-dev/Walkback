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
            Check("My stops: it has a blue pillar and its card says it was picked here, with 0 minutes of work",
                app.stopPillars.Stops.Count == site.Stops.Length && app.stopPillars.Stops[index].baseColor == StopPillars.UserStopColor &&
                panel.stopName.text == "My stop 1" && panel.stopScience.text == UserStops.Science && panel.stopTasks.text == "Work: 0 min · your estimate",
                panel.stopName.text + " / " + panel.stopTasks.text);
            Check("My stops: the stop buttons stay the data's stops, and CLEAR is on",
                panel.stopButtons.Count == Mathf.Min(original.Length, 4) && panel.clearStopsButton != null && panel.clearStopsButton.Interactable &&
                panel.stopButtons.All(b => !b.Selected));
            Check("My stops: the plan visits it (one leg ends there)",
                app.Plan != null && app.Plan.Legs.Any(l => l.ToStop == index));

            CheckUserStopMinutes(app, panel, index);

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
