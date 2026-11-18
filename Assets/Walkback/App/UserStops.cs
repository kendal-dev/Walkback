// Written with Claude Code (AI), directed and tested by team MARSXR
using System.Collections.Generic;
using UnityEngine;

namespace Walkback
{
    /// <summary>
    /// v1.1: stops the astronaut picks by tapping the map. Each one is a StopInfo like the data's stops, with
    /// an id that starts with "user-", no tasks (0 minutes of work: Walkback does not invent what is done
    /// there) and no photo. It goes into the route where it adds the least straight-line distance, so the
    /// plan stays a loop habitat -> stops -> habitat. Pure functions: the app and the test call them.
    /// </summary>
    public static class UserStops
    {
        public const string IdPrefix = "user-";
        /// <summary>At most this many picked stops on the map at once.</summary>
        public const int MaxStops = 4;
        /// <summary>A tap closer than this to a stop or the habitat (map meters) adds nothing: it was meant for that pillar.</summary>
        public const float MinSpacingMeters = 20f;
        /// <summary>A tap is a press released within this time...</summary>
        public const float TapSeconds = 0.45f;
        /// <summary>...whose point on the ground moved less than this in the room (meters): anything longer is a drag.</summary>
        public const float TapMoveMeters = 0.02f;

        public const string Tag = "Your pick · no task time";
        public const string Science = "Picked on the map by the astronaut. No science note in the data for this place.";

        public static bool IsUser(StopInfo stop)
        {
            return stop != null && stop.id != null && stop.id.StartsWith(IdPrefix);
        }

        public static int Count(StopInfo[] stops)
        {
            int n = 0;
            if (stops != null) foreach (var s in stops) if (IsUser(s)) n++;
            return n;
        }

        /// <summary>The next free number for "My stop N" (1 more than the highest on the map).</summary>
        public static int NextNumber(StopInfo[] stops)
        {
            int highest = 0;
            if (stops != null)
                foreach (var s in stops)
                {
                    if (!IsUser(s)) continue;
                    int n;
                    if (int.TryParse(s.id.Substring(IdPrefix.Length), out n)) highest = Mathf.Max(highest, n);
                }
            return highest + 1;
        }

        public static StopInfo Make(int number, Vector2 meters)
        {
            return new StopInfo
            {
                id = IdPrefix + number,
                name = "My stop " + number,
                x = meters.x,
                y = meters.y,
                science = Science,
                tasks = new StopTask[0],
                risk = "",
                photoCredit = "",
                panorama = "",
                tag = Tag,
            };
        }

        /// <summary>Why a tap at this point adds no stop, or null if it may. Inside the map, not on a pillar, under the limit.</summary>
        public static string Refusal(SiteData site, Vector2 meters)
        {
            if (site == null) return "no site";
            if (meters.x < 0f || meters.y < 0f || meters.x > site.SideMeters || meters.y > site.SideMeters) return "outside the map";
            if (Count(site.Stops) >= MaxStops) return "already " + MaxStops + " picked stops";
            if (Vector2.Distance(meters, site.Habitat) < MinSpacingMeters) return "on the habitat";
            foreach (var s in site.Stops)
                if (s != null && Vector2.Distance(meters, new Vector2(s.x, s.y)) < MinSpacingMeters) return "on " + s.name;
            return null;
        }

        /// <summary>
        /// Where the new stop goes in the visiting order (0 = first after the habitat, stops.Length = last before
        /// going home): the place that adds the least straight-line distance to the loop.
        /// </summary>
        public static int BestSlot(Vector2 habitat, StopInfo[] stops, Vector2 p)
        {
            int n = stops != null ? stops.Length : 0;
            int best = n;
            float bestCost = float.PositiveInfinity;
            for (int slot = 0; slot <= n; slot++)
            {
                Vector2 a = slot == 0 ? habitat : new Vector2(stops[slot - 1].x, stops[slot - 1].y);
                Vector2 b = slot == n ? habitat : new Vector2(stops[slot].x, stops[slot].y);
                float cost = Vector2.Distance(a, p) + Vector2.Distance(p, b) - Vector2.Distance(a, b);
                if (cost < bestCost - 1e-4f) { bestCost = cost; best = slot; }
            }
            return best;
        }

        public static StopInfo[] Insert(StopInfo[] stops, int slot, StopInfo stop)
        {
            var list = new List<StopInfo>(stops ?? new StopInfo[0]);
            list.Insert(Mathf.Clamp(slot, 0, list.Count), stop);
            return list.ToArray();
        }

        /// <summary>The data's stops alone, in their order.</summary>
        public static StopInfo[] Without(StopInfo[] stops)
        {
            var list = new List<StopInfo>();
            if (stops != null) foreach (var s in stops) if (!IsUser(s)) list.Add(s);
            return list.ToArray();
        }

        /// <summary>Index in the array of the stop with this id, or -1.</summary>
        public static int IndexOf(StopInfo[] stops, string id)
        {
            if (stops == null || id == null) return -1;
            for (int i = 0; i < stops.Length; i++) if (stops[i] != null && stops[i].id == id) return i;
            return -1;
        }
    }
}
