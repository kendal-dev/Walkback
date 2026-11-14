// Written with Claude Code (AI), directed and tested by team MARSXR
using UnityEngine;

namespace Walkback
{
    /// <summary>
    /// TODO(H2): replace with session C's ReturnMap (Assets/Walkback/Model/) when it lands on main.
    /// Until then: straight-line distance to the habitat at the flat-ground pace of the walking
    /// model's own test (1 km on flat ground = 19.3 min, ficha A). It ignores slope and sand, so
    /// the legend labels it as a placeholder: it is not the walking model.
    /// </summary>
    public static class ReturnStandIn
    {
        public const float FlatMinutesPerKilometer = 19.3f;
        public const string Label = "PLACEHOLDER: straight line at flat-ground pace, not the walking model yet";

        /// <summary>Minutes back to the habitat from every node, row 0 = north.</summary>
        public static float[] Minutes(SiteData site)
        {
            int n = site.GridSize;
            var minutes = new float[n * n];
            Vector2 habitat = site.Habitat;
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    minutes[r * n + c] = Vector2.Distance(site.NodeToMeters(c, r), habitat) / 1000f * FlatMinutesPerKilometer;
            return minutes;
        }
    }
}
