// Written with Claude Code (AI), directed and tested by team MARSXR
using System.Globalization;
using UnityEngine;

namespace Walkback
{
    /// <summary>
    /// v1.1: what the map says about the ground under the pointer, as two short lines for a small card that
    /// follows the pointer's dot: the minutes back to the habitat on foot (the RETURN layer's own number,
    /// plan B over the relief), the slope there, and whether a tap may add a stop. Pure: the app and the test call it.
    /// </summary>
    public static class GroundProbe
    {
        public enum Kind { Fine, TooFar, TooSteep, NoWayBack }

        public struct Reading
        {
            public Kind kind;
            public float slopeDegrees;
            public float minutesBack;
            public string first;
            public string second;
        }

        /// <summary>
        /// The card's two lines for a point of the map (local meters). <paramref name="reachMinutes"/> is the
        /// WHAT IF's reach (infinity with a full tank); <paramref name="tankPercent"/> names it.
        /// </summary>
        public static Reading Read(SiteData site, float[] slopeDegrees, float[] returnMinutes, Vector2 meters,
            float reachMinutes, float tankPercent, bool canTap)
        {
            var r = new Reading();
            int node = ReturnFallback.NodeOf(site, meters);
            r.slopeDegrees = slopeDegrees != null && node < slopeDegrees.Length ? slopeDegrees[node] : 0f;
            r.minutesBack = returnMinutes != null && node < returnMinutes.Length ? returnMinutes[node] : float.PositiveInfinity;
            string slope = r.slopeDegrees.ToString("0", CultureInfo.InvariantCulture) + "° slope";
            bool steep = slopeDegrees != null && !MissionFallback.Walkable(site, slopeDegrees, meters);
            if (steep)
            {
                r.kind = Kind.TooSteep;
                r.first = slope + " · too steep to walk";
            }
            else if (float.IsInfinity(r.minutesBack) || float.IsNaN(r.minutesBack))
            {
                r.kind = Kind.NoWayBack;
                r.first = slope + " · no way back on foot";
            }
            else
            {
                r.first = HelmetHud.MinutesText(r.minutesBack) + " back on foot · " + slope;
                if (!float.IsInfinity(reachMinutes) && r.minutesBack > reachMinutes)
                {
                    r.kind = Kind.TooFar;
                    r.first = HelmetHud.MinutesText(r.minutesBack) + " back · too far with " + tankPercent.ToString("0", CultureInfo.InvariantCulture) + " %";
                }
            }
            r.second = canTap ? "ESTIMATE · tap to add a stop here" : "ESTIMATE";
            return r;
        }

        public static Color ColorOf(Kind kind)
        {
            switch (kind)
            {
                case Kind.TooSteep:
                case Kind.NoWayBack:
                case Kind.TooFar: return WalkbackTheme.Danger;
                default: return WalkbackTheme.Text;
            }
        }
    }
}
