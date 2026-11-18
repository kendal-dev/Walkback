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

        partial void UpdatePanelPiece()
        {
            if (panel != null) panel.TickClearConfirm();
            // The charts fill in step with the route drawing itself on the map (2.5 s); after that this does nothing.
            if (panel == null || panel.charts == null || !panel.charts.HasPlan || !panel.gameObject.activeInHierarchy) return;
            panel.charts.SetProgress(routeView != null && routeView.HasRoute ? routeView.Progress : 1f);
        }
    }
}
