// Written with Claude Code (AI), directed and tested by team MARSXR
using UnityEngine;

namespace Walkback
{
    /// <summary>
    /// One color code for the whole app (table, panel, HUD):
    /// green = safe, amber = the route and what to watch, red = do not step / not enough,
    /// cyan = you and live data, white = text. Always on a dark box: projectors wash out darks.
    /// </summary>
    public static class WalkbackTheme
    {
        public static readonly Color Safe = new Color(0.25f, 0.85f, 0.40f);
        public static readonly Color Watch = new Color(1.00f, 0.72f, 0.05f);
        public static readonly Color Danger = new Color(0.95f, 0.22f, 0.16f);
        public static readonly Color Live = new Color(0.35f, 0.82f, 1.00f);
        public static readonly Color Text = Color.white;
        public static readonly Color TextSoft = new Color(0.80f, 0.82f, 0.85f);

        public static readonly Color PanelBack = new Color(0.045f, 0.05f, 0.06f);
        public static readonly Color CardBack = new Color(0.12f, 0.135f, 0.16f);
        public static readonly Color Table = new Color(0.10f, 0.105f, 0.115f);

        /// <summary>Panel size that reads at 1.6 m (a 1 m panel did not).</summary>
        public const float PanelWidthMeters = 1.50f;
        public const float PanelHeightMeters = 0.70f;

        /// <summary>Titles must be at least this tall to read at 1.5 m and on the projector.</summary>
        public const float MinTitleHeightMeters = 0.05f;
    }
}
