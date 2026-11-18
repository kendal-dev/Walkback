// Written with Claude Code (AI), directed and tested by team MARSXR
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace Walkback
{
    /// <summary>Panel piece: wires the boards' buttons and the stop and layer events at start.</summary>
    public partial class WalkbackApp
    {
        partial void StartPanelPiece()
        {
            if (panel == null) return;
            panel.Bind(this);
            // Until session C's model is wired, the verdict card says there is no route: no numbers are made up.
            panel.ShowNoRoute();
            // PLAN B: if the route piece already planned the mission (MissionFallback), show its verdict,
            // labelled as an estimate on the card.
            ShowPlanOnPanel();
            WalkbackLog.Info("Panel bound: " + panel.layerButtons.Length + " layer buttons, " + panel.stopButtons.Count + " stop buttons, conditions " + (panel.ConditionsHaveData ? "loaded" : "waiting for data"));
            if (Application.isPlaying && isActiveAndEnabled) StartCoroutine(LiveSpaceWeather());
        }

        /// <summary>
        /// One request to NASA DONKI at start, in the background: nothing waits for it. Only an answer that is a
        /// list of events changes the card; any failure leaves the stored line and is written to the log.
        /// </summary>
        IEnumerator LiveSpaceWeather()
        {
            DateTime asked = DateTime.UtcNow;
            float t0 = Time.realtimeSinceStartup;
            string line = null, why = null;
            using (var request = UnityWebRequest.Get(SpaceWeatherLive.Url(asked)))
            {
                request.timeout = SpaceWeatherLive.TimeoutSeconds;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) why = request.result + ", HTTP " + request.responseCode;
                else
                {
                    line = SpaceWeatherLive.Line(request.downloadHandler.text, asked);
                    if (line == null) why = "the answer is not a list of events";
                }
            }
            string took = (Time.realtimeSinceStartup - t0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s";
            if (line == null || panel == null)
            {
                WalkbackLog.Info("Space weather: stored in the app (NASA DONKI not read: " + (why ?? "no panel") + ", " + took + ")");
                yield break;
            }
            panel.ShowLiveSpaceWeather(line);
            WalkbackLog.Info("Space weather: live from NASA DONKI in " + took + " -> " + line);
        }

        // ---- v1.1: the time at Gale (MarsClock), for the daylight line and the verdict's night / sunset note.

        /// <summary>The clock the Mars time is read from (the test sets a fixed time).</summary>
        public Func<DateTime> ClockUtc = () => DateTime.UtcNow;
        /// <summary>Seconds between two updates of the daylight line and the night note.</summary>
        public const float MarsClockSeconds = 30f;
        float marsClockAt = -100f;
        string lastDarkNote;

        bool AtGale { get { return terrain != null && terrain.Site != null && terrain.Site.IsMarsData && terrain.Site.Info != null; } }

        /// <summary>The verdict's note if the plan started now would not fit today's daylight at the site, or null.</summary>
        public string DarkNoteNow()
        {
            if (!AtGale || Plan == null || !Plan.HasRoute) return null;
            return MarsClock.DarkNote(ClockUtc(), terrain.Site.Info.centerLon, terrain.Site.Info.centerLat, Plan.TotalMinutes);
        }

        /// <summary>Writes today's daylight line on the conditions card and, if the night / sunset note changed, the verdict card again.</summary>
        public void RefreshMarsClock()
        {
            if (!AtGale || panel == null) return;
            panel.ShowLiveDaylight(MarsClock.DaylightLine(ClockUtc(), terrain.Site.Info.centerLon, terrain.Site.Info.centerLat));
            string dark = DarkNoteNow();
            if (dark != lastDarkNote) ShowPlanOnPanel();
            lastDarkNote = dark;
        }

        partial void UpdatePanelPiece()
        {
            if (panel != null) panel.TickClearConfirm();
            if (Time.unscaledTime - marsClockAt >= MarsClockSeconds)
            {
                marsClockAt = Time.unscaledTime;
                RefreshMarsClock();
            }
            // The charts fill in step with the route drawing itself on the map (2.5 s); after that this does nothing.
            if (panel == null || panel.charts == null || !panel.charts.HasPlan || !panel.gameObject.activeInHierarchy) return;
            panel.charts.SetProgress(routeView != null && routeView.HasRoute ? routeView.Progress : 1f);
        }
    }
}
