// Written with Claude Code (AI), directed and tested by team MARSXR
using System.Globalization;
using UnityEngine;

namespace Walkback
{
    /// <summary>
    /// v1.1: the point of no return of a plan that does not fit its tank. Walking the planned route from the
    /// habitat, it is the last point where the oxygen left above the reserve still covers the way home from
    /// there (the RETURN layer's minutes at the walking rate), counting the walk so far and the work done at
    /// each stop reached. The same rules as the verdict (MissionFallback), point by point along the route;
    /// the walking minutes of a leg are spread over its points by distance. Plan B, labelled ESTIMATE.
    /// </summary>
    public static class TurnBackPoint
    {
        public struct Result
        {
            public bool found;
            /// <summary>Index into plan.Path of the last point with a way back.</summary>
            public int pathIndex;
            public Vector2 point;
            /// <summary>Meters along the route from the habitat to that point.</summary>
            public float metersAlong;
            /// <summary>The stop the route was heading to (or where the work no longer fits).</summary>
            public string beforeName;
            /// <summary>True when the walk to the stop fits but its work does not: turn back on arrival, without the work.</summary>
            public bool atStopWithoutWork;
        }

        public static Result Find(SiteData site, float[] homeMinutes, MissionFallback.Plan plan)
        {
            var none = new Result { pathIndex = -1 };
            if (site == null || plan == null || !plan.HasRoute || homeMinutes == null || plan.Path.Count < 2) return none;
            float available = plan.TankLiters - HudEstimate.ReserveLiters;
            float used = 0f, along = 0f;
            int lastSafe = 0;
            float lastSafeAlong = 0f;
            if (Fails(site, homeMinutes, plan.Path[0], available, used)) return Make(plan, 0, 0f, plan.Legs.Count > 0 ? plan.Legs[0].ToName : "", false);
            foreach (MissionFallback.Leg leg in plan.Legs)
            {
                // On the way home every step brings the habitat nearer: nothing to turn back from.
                if (leg.ToStop < 0) break;
                float legMeters = Mathf.Max(0.001f, leg.Meters);
                for (int p = leg.FirstPoint + 1; p <= leg.LastPoint && p < plan.Path.Count; p++)
                {
                    float step = Vector2.Distance(plan.Path[p - 1], plan.Path[p]);
                    along += step;
                    used += leg.Minutes * (step / legMeters) * HudEstimate.WalkLitersPerMinute;
                    if (Fails(site, homeMinutes, plan.Path[p], available, used)) return Make(plan, lastSafe, lastSafeAlong, leg.ToName, false);
                    lastSafe = p;
                    lastSafeAlong = along;
                }
                used += leg.WorkMinutesAtEnd * HudEstimate.DesignLitersPerMinute;
                if (Fails(site, homeMinutes, plan.Path[Mathf.Min(leg.LastPoint, plan.Path.Count - 1)], available, used))
                    return Make(plan, lastSafe, lastSafeAlong, leg.ToName, true);
            }
            return none;
        }

        static bool Fails(SiteData site, float[] homeMinutes, Vector2 at, float available, float used)
        {
            float back = homeMinutes[ReturnFallback.NodeOf(site, at)];
            if (float.IsInfinity(back) || float.IsNaN(back)) return false;   // the planner never routes over such ground
            return available - used < back * HudEstimate.WalkLitersPerMinute;
        }

        static Result Make(MissionFallback.Plan plan, int index, float along, string before, bool withoutWork)
        {
            return new Result { found = true, pathIndex = index, point = plan.Path[index], metersAlong = along, beforeName = before, atStopWithoutWork = withoutWork };
        }

        /// <summary>The verdict card's note when there is a point of no return.</summary>
        public static string Note(Result r, float tankPercent)
        {
            string tank = tankPercent < 100f ? "WHAT IF " + tankPercent.ToString("0", CultureInfo.InvariantCulture) + " %: " : "";
            if (r.pathIndex == 0) return tank + "not enough oxygen to leave the habitat · ESTIMATE";
            if (r.atStopWithoutWork) return tank + "turn back at " + r.beforeName + ", no time for its work (red pillar) · ESTIMATE";
            return tank + "turn back " + r.metersAlong.ToString("0", CultureInfo.InvariantCulture) + " m along the route, before " + r.beforeName + " (red pillar) · ESTIMATE";
        }
    }
}
