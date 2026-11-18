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
                panel.stopName.text == "My stop 1" && panel.stopScience.text == UserStops.Science && panel.stopTasks.text.Contains("none"),
                panel.stopName.text + " / " + panel.stopTasks.text);
            Check("My stops: the stop buttons stay the data's stops, and CLEAR is on",
                panel.stopButtons.Count == Mathf.Min(original.Length, 4) && panel.clearStopsButton != null && panel.clearStopsButton.Interactable &&
                panel.stopButtons.All(b => !b.Selected));
            Check("My stops: the plan visits it (one leg ends there)",
                app.Plan != null && app.Plan.Legs.Any(l => l.ToStop == index));

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
