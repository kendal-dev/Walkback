// Written with Claude Code (AI), directed and tested by team MARSXR
using System;
using UnityEngine;

namespace Walkback.EditorTools
{
    /// <summary>v1.1 test checks: the time at Gale (MarsClock, the Mars24 algorithm) against Curiosity's REMS records.</summary>
    public static partial class AutoTest
    {
        struct RemsDay
        {
            public int sol; public int year, month, day; public double ls; public string rise, set;
            public RemsDay(int sol, int year, int month, int day, double ls, string rise, string set)
            { this.sol = sol; this.year = year; this.month = month; this.day = day; this.ls = ls; this.rise = rise; this.set = set; }
        }

        // From the REMS weather feed (NASA/JPL-Caltech/CAB) that session B downloaded on 14 Nov 2026 (data-pipeline/cache/rems/msl_weather.json):
        // sol, Earth date, Ls, sunrise, sunset as the feed gives them.
        static readonly RemsDay[] RemsDays =
        {
            new RemsDay(100, 2012, 11, 16, 207, "05:19", "17:26"),
            new RemsDay(1000, 2015, 5, 30, 350, "06:44", "18:47"),
            new RemsDay(3000, 2021, 1, 13, 347, "06:45", "18:49"),
            new RemsDay(4404, 2024, 12, 26, 20, "06:29", "18:23"),
            new RemsDay(4995, 2026, 8, 25, 341, "06:46", "18:51"),
        };

        static double ClockHours(string hhmm)
        {
            string[] p = hhmm.Split(':');
            return int.Parse(p[0]) + int.Parse(p[1]) / 60.0;
        }

        static void CheckMarsClock(WalkbackApp app, MissionPanel panel)
        {
            const double lon = 137.381, lat = -4.807;   // site.json's center of the Gale square
            string worst = "";
            bool solsOk = true, lsOk = true, sunOk = true;
            double worstMinutes = 0;
            foreach (RemsDay r in RemsDays)
            {
                var noon = new DateTime(r.year, r.month, r.day, 12, 0, 0, DateTimeKind.Utc);
                MarsClock.Sample s = MarsClock.At(noon, lon);
                double rise, set;
                bool both = MarsClock.Daylight(noon, lon, lat, out rise, out set);
                double dRise = Math.Abs(rise - ClockHours(r.rise)) * 60.0, dSet = Math.Abs(set - ClockHours(r.set)) * 60.0;
                double dLs = Math.Abs(Mathf.DeltaAngle((float)s.ls, (float)r.ls));
                solsOk &= s.curiositySol == r.sol;
                lsOk &= dLs <= 1.5;
                sunOk &= both && dRise <= 4.0 && dSet <= 4.0;
                if (Math.Max(dRise, dSet) > worstMinutes) worstMinutes = Math.Max(dRise, dSet);
                worst += (worst.Length > 0 ? "; " : "") + "sol " + s.curiositySol + "/" + r.sol + " Ls " + s.ls.ToString("0.0") + "/" + r.ls +
                         " " + MarsClock.Clock(rise) + "-" + MarsClock.Clock(set) + " vs " + r.rise + "-" + r.set;
            }
            Check("Mars clock: at noon UTC of five REMS days (2012 to 2026) it gives Curiosity's sol of that day", solsOk, worst);
            Check("Mars clock: the season (Ls) is REMS's within 1.5°", lsOk);
            Check("Mars clock: sunrise and sunset at Gale are REMS's within 4 min (LMST), worst " + worstMinutes.ToString("0.0") + " min", sunOk);

            // The card and the verdict at fixed times.
            Func<DateTime> clockWas = app.ClockUtc;
            string cardWas = panel.conditionsText != null ? panel.conditionsText.text : null;
            if (!app.terrain.Site.IsMarsData || panel.conditionsText == null || !panel.ConditionsHaveData)
            {
                Note("Mars clock: not on Gale's data, the card and the verdict are not checked");
                return;
            }
            DateTime day = new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc);
            DateTime morning = FindLocal(day, lon, 8.0), afternoon = FindLocal(day, lon, 17.0), night = FindLocal(day, lon, 2.0);

            app.SetWhatIfTank(100f);
            app.ClockUtc = () => morning;
            app.RefreshMarsClock();
            Canvas.ForceUpdateCanvases();
            string text = panel.conditionsText.text;
            float box = ((RectTransform)panel.conditionsText.transform).rect.height;
            Check("Mars clock: the conditions card's daylight line is today's at Gale, with the time now, and the card still fits",
                text.Contains(MarsClock.DaylightPrefix) && text.Contains("LMST (Mars24)") && !text.Contains("REMS sol 4404") &&
                panel.conditionsText.preferredHeight <= box + 6f,
                text.Split('\n')[Array.FindIndex(text.Split('\n'), l => l.StartsWith(MarsClock.DaylightPrefix))] + " (" + panel.conditionsText.preferredHeight.ToString("0") + "/" + box.ToString("0") + " px)");
            string seasonLine = Array.Find(text.Split('\n'), l => l.StartsWith(MarsClock.SeasonPrefix));
            double lsNow = MarsClock.At(morning, lon).ls;
            Check("Mars clock: the season line is today's (Ls and season from Mars24), not the REMS record of a Mars year ago",
                seasonLine != null && seasonLine.Contains("(Mars24)") && seasonLine.Contains("Ls " + lsNow.ToString("0") + "°") && !text.Contains("a Mars year ago"),
                seasonLine);
            Check("Mars clock: the seasons by Ls (southern hemisphere; dusty Ls 180-360)",
                MarsClock.SeasonOf(20).StartsWith("southern autumn, the low-dust") && MarsClock.SeasonOf(120).StartsWith("southern winter") &&
                MarsClock.SeasonOf(200).StartsWith("southern spring, the dusty") && MarsClock.SeasonOf(341).StartsWith("southern summer, the dusty"));
            Check("Mars clock: leaving at 08:00 LMST the 2 h 55 min plan fits the daylight: the verdict's note is the usual one",
                app.DarkNoteNow() == null && panel.verdictNote.text.StartsWith("ESTIMATE"), panel.verdictNote.text);

            app.ClockUtc = () => afternoon;
            app.RefreshMarsClock();
            Canvas.ForceUpdateCanvases();
            Check("Mars clock: leaving at 17:00 LMST the note says the walk ends after sunset, on one line",
                panel.verdictNote.text.StartsWith("Leaving now you are back at") && panel.verdictNote.text.Contains("after sunset") &&
                panel.verdictNote.preferredWidth <= ((RectTransform)panel.verdictNote.transform).rect.width, panel.verdictNote.text);

            app.ClockUtc = () => night;
            app.RefreshMarsClock();
            Canvas.ForceUpdateCanvases();
            Check("Mars clock: at 02:00 LMST the note says it is night at Gale, with today's daylight, and the verdict stays the oxygen's",
                panel.verdictNote.text.StartsWith("Night at Gale now (02:00 LMST)") && panel.Verdict == VerdictState.Viable &&
                panel.verdictNote.preferredWidth <= ((RectTransform)panel.verdictNote.transform).rect.width, panel.verdictNote.text);

            app.SetWhatIfTank(8f);
            Check("Mars clock: a point of no return comes first on the note", panel.verdictNote.text.StartsWith("WHAT IF 8 %"), panel.verdictNote.text);
            app.SetWhatIfTank(100f);

            app.ClockUtc = clockWas;
            app.RefreshMarsClock();
            panel.conditionsText.text = cardWas;
        }

        /// <summary>The first UTC time after <paramref name="from"/> when the LMST at that longitude is <paramref name="hours"/> (within a minute).</summary>
        static DateTime FindLocal(DateTime from, double lon, double hours)
        {
            DateTime t = from;
            for (int i = 0; i < 3000; i++)
            {
                double lmst = MarsClock.At(t, lon).lmst;
                double diff = hours - lmst;
                if (diff < 0) diff += 24.0;
                if (diff < 1.0 / 60.0) return t;
                t = t.AddSeconds(Math.Max(20.0, diff / 24.0 * MarsClock.SolSeconds * 0.9));
            }
            return t;
        }
    }
}
