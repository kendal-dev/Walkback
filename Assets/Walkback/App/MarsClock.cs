// Written with Claude Code (AI), directed and tested by team MARSXR
using System;
using System.Globalization;
using UnityEngine;

namespace Walkback
{
    /// <summary>
    /// v1.1: the time at Gale. The Mars24 algorithm of NASA GISS (Allison and McEwen 2000, Planet. Space Sci. 48,
    /// 215-235; giss.nasa.gov/tools/mars24/help/algorithm.html): from a UTC time, the Mars Sol Date, the
    /// local mean solar time (LMST, the clock MSL plans with), the true solar time, the season (Ls) and the
    /// Sun's declination; from those, sunrise and sunset at a latitude. Checked by the automatic test against
    /// Curiosity's REMS records (sol, Ls, sunrise and sunset in LMST) from 2012 to 2026.
    /// </summary>
    public static class MarsClock
    {
        /// <summary>Seconds of a Mars solar day (sol).</summary>
        public const double SolSeconds = 88775.244;
        /// <summary>Earth days per sol (Mars24).</summary>
        public const double SolDays = 1.0274912517;
        /// <summary>TT - UTC since 2017 (32.184 s + 37 leap seconds).</summary>
        public const double TtMinusUtcSeconds = 69.184;
        /// <summary>Curiosity's sol 0 began at local midnight of this Mars Sol Date at Gale (landing 2012-08-06 05:17:57 UTC at 15:03 LMST).</summary>
        public const double CuriositySolZeroMsd = 49269.0;

        public struct Sample
        {
            public double msd;
            /// <summary>Local mean solar time, hours 0..24.</summary>
            public double lmst;
            /// <summary>Local true solar time, hours 0..24.</summary>
            public double ltst;
            /// <summary>Areocentric solar longitude, degrees 0..360.</summary>
            public double ls;
            /// <summary>Sun's declination, degrees.</summary>
            public double declination;
            /// <summary>Curiosity's mission sol at this local time (counted at Gale's longitude).</summary>
            public int curiositySol;
        }

        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        static readonly double[] A = { 0.0071, 0.0057, 0.0039, 0.0037, 0.0021, 0.0020, 0.0018 };
        static readonly double[] Tau = { 2.2353, 2.7543, 1.1177, 15.7866, 2.1354, 2.4694, 32.8493 };
        static readonly double[] Phi = { 49.409, 168.173, 191.837, 21.736, 15.704, 95.528, 49.095 };

        public static Sample At(DateTime utc, double eastLongitude)
        {
            double jdUt = 2440587.5 + (utc.ToUniversalTime() - Epoch).TotalMilliseconds / 8.64e7;
            double jdTt = jdUt + TtMinusUtcSeconds / 86400.0;
            double d = jdTt - 2451545.0;
            double m = Rad(19.3871 + 0.52402073 * d);
            double alphaFms = 270.3871 + 0.524038496 * d;
            double pbs = 0.0;
            for (int i = 0; i < A.Length; i++) pbs += A[i] * Math.Cos(Rad(0.985626 * d / Tau[i] + Phi[i]));
            double equationOfCenter = (10.691 + 3.0e-7 * d) * Math.Sin(m) + 0.623 * Math.Sin(2 * m) + 0.050 * Math.Sin(3 * m) +
                                      0.005 * Math.Sin(4 * m) + 0.0005 * Math.Sin(5 * m) + pbs;
            double ls = Mod(alphaFms + equationOfCenter, 360.0);
            double l = Rad(ls);
            double eot = 2.861 * Math.Sin(2 * l) - 0.071 * Math.Sin(4 * l) + 0.002 * Math.Sin(6 * l) - equationOfCenter;
            double msd = (jdTt - 2451549.5) / SolDays + 44796.0 - 0.0009626;
            double mst = Mod(24.0 * msd, 24.0);
            double lmst = Mod(mst + eastLongitude / 15.0, 24.0);
            return new Sample
            {
                msd = msd,
                lmst = lmst,
                ltst = Mod(lmst + eot / 15.0, 24.0),
                ls = ls,
                declination = Deg(Math.Asin(0.42565 * Math.Sin(l))) + 0.25 * Math.Sin(l),
                curiositySol = (int)Math.Floor(msd + eastLongitude / 360.0 - CuriositySolZeroMsd),
            };
        }

        /// <summary>The Sun's elevation above the horizon (degrees, its center, no refraction) at a place and time.</summary>
        public static double SunElevation(DateTime utc, double eastLongitude, double latitude)
        {
            Sample s = At(utc, eastLongitude);
            double h = Rad((s.ltst - 12.0) * 15.0), phi = Rad(latitude), dec = Rad(s.declination);
            return Deg(Math.Asin(Math.Sin(phi) * Math.Sin(dec) + Math.Cos(phi) * Math.Cos(dec) * Math.Cos(h)));
        }

        /// <summary>
        /// Sunrise and sunset, in LMST hours, of the local sol that contains <paramref name="utc"/>. False when the
        /// Sun does not cross the horizon that sol (never at Gale, near the equator).
        /// </summary>
        public static bool Daylight(DateTime utc, double eastLongitude, double latitude, out double riseLmst, out double setLmst)
        {
            riseLmst = setLmst = -1.0;
            Sample now = At(utc, eastLongitude);
            DateTime midnight = utc.AddSeconds(-now.lmst / 24.0 * SolSeconds);
            const int steps = 360;
            double step = SolSeconds / steps;
            double before = SunElevation(midnight, eastLongitude, latitude);
            for (int i = 1; i <= steps; i++)
            {
                DateTime t = midnight.AddSeconds(i * step);
                double e = SunElevation(t, eastLongitude, latitude);
                if ((before < 0.0) != (e < 0.0))
                {
                    DateTime lo = t.AddSeconds(-step), hi = t;
                    for (int k = 0; k < 20; k++)
                    {
                        DateTime mid = lo.AddSeconds((hi - lo).TotalSeconds / 2.0);
                        if ((SunElevation(mid, eastLongitude, latitude) < 0.0) == (before < 0.0)) lo = mid; else hi = mid;
                    }
                    double at = At(hi, eastLongitude).lmst;
                    if (e > before) riseLmst = at; else setLmst = at;
                }
                before = e;
            }
            return riseLmst >= 0.0 && setLmst >= 0.0;
        }

        /// <summary>Mars-clock hours a walk of this many Earth minutes takes (a Mars hour is 1/24 of a sol).</summary>
        public static double MarsHours(double earthMinutes)
        {
            return earthMinutes / 60.0 / SolDays;
        }

        /// <summary>"14:32" for an hour of the day (0..24).</summary>
        public static string Clock(double hours)
        {
            int minutes = (int)Math.Round(Mod(hours, 24.0) * 60.0) % 1440;
            return (minutes / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (minutes % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>The conditions card's daylight line, computed for now: "Daylight: 06:47 to 18:52 · now 14:32 LMST (Mars24)".</summary>
        public static string DaylightLine(DateTime utc, double eastLongitude, double latitude)
        {
            double rise, set;
            if (!Daylight(utc, eastLongitude, latitude, out rise, out set)) return null;
            return DaylightPrefix + Clock(rise) + " to " + Clock(set) + " · now " + Clock(At(utc, eastLongitude).lmst) + " LMST (Mars24)";
        }

        public const string DaylightPrefix = "Daylight: ";
        public const string SeasonPrefix = "Season: ";

        /// <summary>
        /// The season at Gale (southern hemisphere) for a solar longitude: Ls 0-90 southern autumn, 90-180 winter,
        /// 180-270 spring, 270-360 summer; Ls 180-360, around perihelion, is the dusty season, Ls 0-180 the low-dust one.
        /// </summary>
        public static string SeasonOf(double ls)
        {
            double l = Mod(ls, 360.0);
            string season = l < 90.0 ? "southern autumn" : l < 180.0 ? "southern winter" : l < 270.0 ? "southern spring" : "southern summer";
            return season + (l < 180.0 ? ", the low-dust season" : ", the dusty season");
        }

        /// <summary>The conditions card's season line, computed for now: "Season: Ls 37°, southern autumn, the low-dust season (Mars24)".</summary>
        public static string SeasonLine(DateTime utc, double eastLongitude)
        {
            double ls = At(utc, eastLongitude).ls;
            return SeasonPrefix + "Ls " + ls.ToString("0", CultureInfo.InvariantCulture) + "°, " + SeasonOf(ls) + " (Mars24)";
        }

        /// <summary>
        /// The verdict card's note when a plan started now would not fit in today's daylight: it is night at
        /// Gale, or the walk ends after sunset. Null when it fits.
        /// </summary>
        public static string DarkNote(DateTime utc, double eastLongitude, double latitude, float planMinutes)
        {
            double rise, set;
            if (!Daylight(utc, eastLongitude, latitude, out rise, out set)) return null;
            double now = At(utc, eastLongitude).lmst;
            if (now < rise || now >= set)
                return "Night at Gale now (" + Clock(now) + " LMST), daylight " + Clock(rise) + " to " + Clock(set) + " · ESTIMATE";
            double back = now + MarsHours(planMinutes);
            if (back > set)
                return "Leaving now you are back at " + Clock(back) + " LMST, after sunset (" + Clock(set) + ") · ESTIMATE";
            return null;
        }

        static double Rad(double deg) { return deg * Math.PI / 180.0; }
        static double Deg(double rad) { return rad * 180.0 / Math.PI; }
        static double Mod(double x, double m) { double r = x % m; return r < 0 ? r + m : r; }
    }
}
