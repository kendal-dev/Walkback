// Written with Claude Code (AI), directed and tested by team MARSXR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Walkback.EditorTools
{
    /// <summary>Panel piece checks: verdict card, layer buttons, stop card, conditions, side board.</summary>
    public static partial class AutoTest
    {
        const string ViableTestLine = "O2 used 23 % · margin to return 5 h 40 min · reserve untouched";

        static partial void CheckPanelPiece(WalkbackApp app)
        {
            var panel = app.panel;
            panel.Bind(app);
            CheckPanelButtons(app, panel);
            CheckPanelVerdict(panel);
            CheckPlanCharts();
            CheckPanelCharts(app, panel);
            CheckPanelSideBoard(app, panel);
            CheckSourceBoard(app, panel);
            CheckPanelConditions(panel);
            CheckPanelTextFits(panel);
            CheckUserStops(app, panel);
            CheckUserStopsFile(app, panel);
            CheckSuits(app, panel);
            CheckTurnBack(app, panel);
        }

        /// <summary>
        /// v1.1: pointing at a stop's pillar shows that stop's card as a preview; leaving it brings the selected
        /// stop's card back. Called with stop 1 selected.
        /// </summary>
        static void CheckStopPreview(WalkbackApp app, MissionPanel panel, StopInfo[] stops)
        {
            StopPillars pillars = app.stopPillars;
            if (pillars == null || pillars.Stops.Count < 2)
            {
                Check("Preview: the map has at least two stop pillars", false, "no pillars");
                return;
            }
            PillarView other = pillars.Stops[0];
            other.OnPointerEnter();
            Check("Preview: pointing at another stop's pillar shows its card, marked PREVIEW, without selecting it",
                app.PreviewedStop == 0 && app.SelectedStop == 1 && panel.ShowingPreview &&
                panel.stopName.text == stops[0].name && panel.stopScience.text == stops[0].science && panel.stopBadge.text == MissionPanel.PreviewBadge,
                panel.stopName.text + " / " + panel.stopBadge.text);
            Check("Preview: the selected stop's button stays lit and 360° PHOTO is off while previewing",
                panel.stopButtons[1].Selected && !panel.stopButtons[0].Selected && !panel.jumpButton.Interactable);
            Canvas.ForceUpdateCanvases();
            Check("Preview: the PREVIEW line fits its box on one line",
                panel.stopBadge.preferredWidth <= ((RectTransform)panel.stopBadge.transform).rect.width,
                panel.stopBadge.preferredWidth.ToString("F0") + " px in " + ((RectTransform)panel.stopBadge.transform).rect.width.ToString("F0"));

            other.OnPointerExit();
            Check("Preview: leaving the pillar brings back the selected stop's card and its 360° PHOTO button",
                app.PreviewedStop == -1 && !panel.ShowingPreview && panel.stopName.text == stops[1].name && panel.jumpButton.Interactable &&
                panel.stopBadge.text != MissionPanel.PreviewBadge, panel.stopName.text);

            PillarView same = pillars.Stops[1];
            same.OnPointerEnter();
            Check("Preview: pointing at the selected stop's own pillar changes nothing on the card",
                !panel.ShowingPreview && panel.stopName.text == stops[1].name && panel.jumpButton.Interactable);
            same.OnPointerExit();

            other.OnPointerEnter();
            other.OnPointerClick(other.transform.position);
            Check("Preview: pressing the previewed pillar selects it, and the card is no longer a preview",
                app.SelectedStop == 0 && !panel.ShowingPreview && panel.stopName.text == stops[0].name && panel.jumpButton.Interactable);
            other.OnPointerExit();
            Check("Preview: leaving it after pressing keeps the new stop", app.SelectedStop == 0 && panel.stopName.text == stops[0].name && app.PreviewedStop == -1);

            app.SelectStop(-1);
            other.OnPointerEnter();
            bool shownWithNone = panel.ShowingPreview && panel.stopName.text == stops[0].name;
            other.OnPointerExit();
            Check("Preview: with no stop chosen the preview works, and leaving goes back to NO STOP SELECTED",
                shownWithNone && panel.stopName.text == "NO STOP SELECTED" && !panel.jumpButton.Interactable);

            pillars.Habitat.OnPointerEnter();
            Check("Preview: the habitat's pillar shows no preview", app.PreviewedStop == -1 && !panel.ShowingPreview);
            pillars.Habitat.OnPointerExit();
            app.SelectStop(1);
        }

        static void CheckPanelButtons(WalkbackApp app, MissionPanel panel)
        {
            var buttons = panel.layerButtons;
            Check("Panel: four layer buttons (PHOTO first on screen, then HEIGHT, SLOPE, RETURN)", buttons.Length == 4 && Array.TrueForAll(buttons, b => b != null) &&
                buttons[0].label.text == "HEIGHT" && buttons[1].label.text == "SLOPE" && buttons[2].label.text == "RETURN" && buttons[3].label.text == "PHOTO" &&
                buttons[3].transform.position.x < buttons[0].transform.position.x);

            bool collidersMatch = true, targets = true;
            foreach (var b in buttons)
            {
                var box = b.GetComponent<BoxCollider>();
                var r = (RectTransform)b.transform;
                if (box == null || Mathf.Abs(box.size.x - r.sizeDelta.x) > 0.5f || Mathf.Abs(box.size.y - r.sizeDelta.y) > 0.5f ||
                    Mathf.Abs(box.center.x - r.sizeDelta.x / 2f) > 0.5f || Mathf.Abs(box.center.y + r.sizeDelta.y / 2f) > 0.5f) collidersMatch = false;
                if (b.GetComponent<IPointerTarget>() == null) targets = false;
            }
            Check("Panel: each layer button has a collider the size of the button", collidersMatch);
            Check("Panel: each layer button is a pointer target", targets);

            // The pointer will find the button: a ray from the user's eyes to its center hits its collider.
            Physics.SyncTransforms();
            var slope = buttons[1];
            Vector3 center = slope.GetComponent<BoxCollider>().bounds.center;
            RaycastHit hit;
            bool hits = Physics.Raycast(MissionPanel.UserEye, (center - MissionPanel.UserEye).normalized, out hit, 5f);
            Check("Panel: a ray from the user's eyes reaches the SLOPE button", hits && hit.collider.GetComponentInParent<IPointerTarget>() == (IPointerTarget)slope,
                hits ? "hit " + hit.collider.name + " at " + hit.distance.ToString("F2") + " m" : "no hit");

            slope.OnPointerEnter();
            Check("Panel: hover lights the button", slope.Hovered && slope.background.color == PanelButton.HoverBack);
            int before = slope.Presses;
            slope.OnPointerClick(center);
            app.RefreshPanel();
            Check("Panel: pressing SLOPE shows the slope layer and its legend", app.terrain.Layer == MapLayer.Slope && panel.legendLine.text.StartsWith("SLOPE") && slope.Presses == before + 1);
            Check("Panel: the active layer button is amber, the others are not",
                slope.Selected && !buttons[0].Selected && !buttons[2].Selected && slope.background.color == PanelButton.SelectedBack && buttons[0].background.color != PanelButton.SelectedBack);
            slope.OnPointerExit();
            Check("Panel: leaving the button ends the hover", !slope.Hovered);
            buttons[2].OnPointerClick(buttons[2].transform.position);
            Check("Panel: pressing RETURN ON FOOT shows the return layer", app.terrain.Layer == MapLayer.Return && buttons[2].Selected && !slope.Selected);
            buttons[0].OnPointerClick(buttons[0].transform.position);
            app.RefreshPanel();
            Check("Panel: pressing HEIGHT goes back to the height layer", app.terrain.Layer == MapLayer.Height && buttons[0].Selected && panel.legendLine.text.StartsWith("HEIGHT"));
        }

        static void CheckPanelVerdict(MissionPanel panel)
        {
            panel.ShowVerdict(VerdictState.Viable, null, ViableTestLine, null);
            Check("Panel: VIABLE is green, with the ficha A summary line", panel.verdictTitle.text == "VIABLE" && panel.verdictTitle.color == WalkbackTheme.Safe &&
                panel.verdictAccent.color == WalkbackTheme.Safe && panel.verdictDetail.text == ViableTestLine);
            panel.ShowVerdict(VerdictState.NotViable, "NO SAFE ROUTE", "Every route crosses slopes over 20°", "TEST");
            var rect = (RectTransform)panel.verdictTitle.transform;
            Check("Panel: NOT VIABLE / NO SAFE ROUTE is red and fits on one line", panel.verdictTitle.color == WalkbackTheme.Danger && panel.verdictAccent.color == WalkbackTheme.Danger &&
                panel.verdictTitle.preferredWidth <= rect.rect.width, panel.verdictTitle.preferredWidth.ToString("F0") + " of " + rect.rect.width.ToString("F0") + " px");
            Check("Panel: the verdict stays the biggest text", panel.verdictTitle.fontSize > panel.title.fontSize && panel.verdictTitle.fontSize > panel.verdictDetail.fontSize);
            panel.ShowNoRoute();
            Check("Panel: back to NO ROUTE YET, with no number on the card (rule 1)", panel.verdictTitle.text == "NO ROUTE YET" && panel.Verdict == VerdictState.NoRoute &&
                panel.verdictTitle.color == WalkbackTheme.Watch && !HasDigit(panel.verdictDetail.text) && !HasDigit(panel.verdictNote.text));
        }

        /// <summary>The numbers behind the charts, on hand-made plans: flat ground, then a slope rising to the east.</summary>
        static void CheckPlanCharts()
        {
            // Flat: habitat in the south-west corner, one stop 1 km east with 10 minutes of work.
            SiteData site = MissionTestSite();
            var slope = new float[site.GridSize * site.GridSize];
            var plan = MissionFallback.Make(site, slope, 100f);
            var profile = PlanCharts.MakeProfile(site, plan);
            Check("Charts profile: on flat ground the height along the route is 0 all the way, 2 km long, the stop at 1 km",
                profile.Meters.Length == plan.Path.Count && Mathf.Abs(profile.TotalMeters - 2000f) < 1f && Mathf.Abs(profile.Highest) < 0.001f && Mathf.Abs(profile.Lowest) < 0.001f &&
                profile.StopMeters.Length == 1 && Mathf.Abs(profile.StopMeters[0] - 1000f) < 1f && profile.StopNames[0] == "A",
                profile.TotalMeters.ToString("F0") + " m, stop at " + (profile.StopMeters.Length > 0 ? profile.StopMeters[0].ToString("F0") : "?") + " m");

            // The ground rises 5 m every 100 m to the east: the stop is 50 m above the habitat, and nothing is below it.
            SiteData hill = MissionTestSite();
            int n = hill.GridSize;
            for (int r = 0; r < n; r++) for (int c = 0; c < n; c++) hill.Heights[r * n + c] = c * hill.NodeSpacing * 0.05f;
            var hillPlan = MissionFallback.Make(hill, TerrainLayers.SlopeDegrees(hill.Heights, n, hill.NodeSpacing), 100f);
            var hillProfile = PlanCharts.MakeProfile(hill, hillPlan);
            int middle = hillPlan.Legs.Count > 0 ? hillPlan.Legs[0].LastPoint / 2 : 0;
            Check("Charts profile: on a slope the heights are the relief's, relative to the habitat (stop +50 m, half way +25 m, back to 0)",
                hillPlan.HasRoute && Mathf.Abs(hillProfile.Highest - 50f) < 0.05f && Mathf.Abs(hillProfile.Lowest) < 0.05f && Mathf.Abs(hillProfile.StopHeight[0] - 50f) < 0.05f &&
                Mathf.Abs(hillProfile.Height[middle] - hillProfile.Meters[middle] * 0.05f) < 0.1f && Mathf.Abs(hillProfile.Height[hillProfile.Height.Length - 1]) < 0.05f,
                "highest " + MissionCharts.Signed(hillProfile.Highest) + " m, lowest " + MissionCharts.Signed(hillProfile.Lowest) + " m");
            Check("Charts: heights relative to the habitat carry their sign", MissionCharts.Signed(77.4f) == "+77" && MissionCharts.Signed(-3.2f) == "-3" && MissionCharts.Signed(0.2f) == "0");

            // Filling with the route: nothing at the start, the stop's work only once the stop is reached, the plan's totals at the end.
            var start = PlanCharts.At(plan, 0f);
            var beforeStop = PlanCharts.At(plan, 0.49f);
            var afterStop = PlanCharts.At(plan, 0.51f);
            var end = PlanCharts.At(plan, 1f);
            Check("Charts fill: nothing spent at the start; the work counts once its stop is reached; at the end the plan's own totals",
                start.usedLiters < 0.001f && start.TotalMinutes < 0.001f && Mathf.Abs(beforeStop.walkMinutes - 38.6f * 0.49f) < 0.05f && beforeStop.workMinutes < 0.001f &&
                Mathf.Approximately(afterStop.workMinutes, 10f) && Mathf.Abs(afterStop.walkMinutes - 38.6f * 0.51f) < 0.05f &&
                Mathf.Abs(end.walkMinutes - plan.WalkMinutes) < 0.01f && Mathf.Abs(end.workMinutes - plan.WorkMinutes) < 0.01f && Mathf.Abs(end.usedLiters - plan.UsedLiters) < 0.05f,
                "half way " + beforeStop.TotalMinutes.ToString("F1") + " -> " + afterStop.TotalMinutes.ToString("F1") + " min, end " + end.usedLiters.ToString("F1") + " L");

            // The tank: with a full one used + spare + reserve fill the bar; the spare is the plan's.
            var tank = PlanCharts.TankAt(plan, plan.UsedLiters);
            float full = HudEstimate.TankLiters;
            Check("Charts tank: used + spare + reserve fill a full tank, and the spare is the plan's spare",
                Mathf.Abs(tank.used + tank.spare + tank.reserve - 1f) < 0.0001f && tank.missing < 0.0001f && tank.intoReserve < 0.0001f &&
                Mathf.Abs(tank.used * full - plan.UsedLiters) < 0.05f && Mathf.Abs(tank.spare * full - plan.SpareLiters) < 0.05f && Mathf.Abs(tank.reserve * full - HudEstimate.ReserveLiters) < 0.05f,
                (tank.used * 100f).ToString("F1") + " % used, " + (tank.spare * 100f).ToString("F1") + " % spare, " + (tank.reserve * 100f).ToString("F1") + " % reserve");
            // WHAT IF with 16 % of a tank: the plan ends 1.8 L into the reserve. With 8 % it asks for more than the tank holds.
            var after = MissionFallback.Make(site, slope, 16f);
            var tight = PlanCharts.TankAt(after, after.UsedLiters);
            var dry = MissionFallback.Make(site, slope, 8f);
            var empty = PlanCharts.TankAt(dry, dry.UsedLiters);
            Check("Charts tank: a WHAT IF shows what was never in the tank, and red where the plan breathes the reserve",
                Mathf.Abs(tight.missing - 0.84f) < 0.0001f && tight.spare < 0.0001f && Mathf.Abs(tight.intoReserve * full + after.SpareLiters) < 0.05f && tight.shortLiters < 0.001f &&
                Mathf.Abs(tight.used + tight.intoReserve + tight.reserve + tight.missing - 1f) < 0.0001f &&
                Mathf.Abs(empty.missing - 0.92f) < 0.0001f && empty.reserve < 0.0001f && empty.spare < 0.0001f && Mathf.Abs(empty.shortLiters - (dry.UsedLiters - dry.TankLiters)) < 0.05f,
                (tight.intoReserve * full).ToString("F1") + " L of the reserve with 16 %; " + empty.shortLiters.ToString("F1") + " L short of a whole tank with 8 %");

            var spans = PlanCharts.Timeline(plan);
            Check("Charts time: walking, work at the stop, walking back, in that order and filling the whole bar",
                spans.Count == 3 && !spans[0].work && spans[1].work && !spans[2].work && spans[0].start < 0.0001f && Mathf.Abs(spans[2].end - 1f) < 0.0001f &&
                Mathf.Abs(spans[0].end - spans[1].start) < 0.0001f && Mathf.Abs(spans[1].end - spans[2].start) < 0.0001f &&
                Mathf.Abs(spans[1].end - spans[1].start - 10f / plan.TotalMinutes) < 0.0005f && spans[1].name == "A",
                spans.Count + " stretches, work from " + (spans.Count > 1 ? (spans[1].start * 100f).ToString("F0") + " % to " + (spans[1].end * 100f).ToString("F0") + " %" : "?"));
            var none = new MissionFallback.Plan { HasRoute = false };
            Check("Charts: without a plan there is nothing to draw", PlanCharts.Timeline(none).Count == 0 && PlanCharts.MakeProfile(site, none).Meters.Length == 0 && PlanCharts.At(none, 1f).usedLiters < 0.001f);
        }

        /// <summary>The charts on the board: empty without a plan, the plan's numbers with one, filling with the route, the icon by shape.</summary>
        static void CheckPanelCharts(WalkbackApp app, MissionPanel panel)
        {
            MissionCharts charts = panel.charts;
            Check("Charts: the board has the verdict's icon and three charts (oxygen, time, height along the route)",
                charts != null && charts.icon != null && charts.tankBar != null && charts.timeBar != null && charts.profilePlot != null && charts.stopLabels.Length == MissionCharts.MaxStops);
            if (charts == null) return;
            bool uiOnly = true;
            foreach (var chart in panel.GetComponentsInChildren<ChartGraphic>(true))
                if (chart.material == null || chart.material.shader.name != "Walkback/UI") uiOnly = false;
            Check("Charts: drawn with the board's material, which never writes alpha (passthrough)", uiOnly);

            panel.ShowNoRoute();
            bool noNumbers = true;
            foreach (var t in new[] { charts.tankCaption, charts.tankUsed, charts.tankSpare, charts.tankReserve, charts.timeCaption, charts.timeWalk, charts.timeWork, charts.timeTotal, charts.profileCaption, charts.profileNumbers })
                if (HasDigit(t.text)) noNumbers = false;
            Check("Charts: with no route they are empty and show no number (rule 1); the icon is the waiting bar",
                !charts.HasPlan && noNumbers && charts.StopsShown == 0 && charts.IconState == VerdictState.NoRoute && charts.icon.TriangleCount > 0 && charts.tankBar.TriangleCount == 2 && charts.timeBar.TriangleCount == 2);
            int waitingShape = charts.icon.TriangleCount;
            panel.ShowVerdict(VerdictState.Viable, null, ViableTestLine, null);
            int checkShape = charts.icon.TriangleCount;
            panel.ShowVerdict(VerdictState.NotViable, null, null, null);
            int crossShape = charts.icon.TriangleCount;
            Check("Charts: the verdict's icon changes shape, not only colour (a check, a cross, a bar)",
                charts.IconState == VerdictState.NotViable && checkShape != crossShape && crossShape != waitingShape && checkShape != waitingShape,
                checkShape + " / " + crossShape + " / " + waitingShape + " triangles");
            panel.ShowNoRoute();

            MapLayer layerBefore = app.terrain.Layer;
            app.PlanMission(false);
            MissionFallback.Plan plan = app.Plan;
            if (plan == null || !plan.HasRoute)
            {
                Note("Charts: no planned route on this site, the charts with a plan were not checked");
                app.ClearPlannedRoute();
                panel.ShowNoRoute();
                return;
            }
            Canvas.ForceUpdateCanvases();
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var profile = charts.CurrentProfile;
            Check("Charts: with the plan on the board, the oxygen bar's numbers are the plan's (used, spare, reserve)",
                charts.HasPlan && charts.tankUsed.text == "USED " + plan.UsedLiters.ToString("0", culture) + " L" && charts.tankSpare.text == "SPARE " + plan.SpareLiters.ToString("0", culture) + " L" &&
                charts.tankReserve.text == "RESERVE " + HudEstimate.ReserveLiters.ToString("0", culture) + " L" && charts.tankCaption.text.Contains("estimate") && charts.tankBar.TriangleCount >= 8,
                charts.tankUsed.text + " | " + charts.tankSpare.text + " | " + charts.tankReserve.text);
            Check("Charts: the time bar's numbers are the plan's (walk, work, total), one stretch per leg and per stop with work",
                charts.timeWalk.text == "WALK " + HelmetHud.MinutesText(plan.WalkMinutes) && charts.timeWork.text == "WORK " + HelmetHud.MinutesText(plan.WorkMinutes) &&
                charts.timeTotal.text == "TOTAL " + HelmetHud.MinutesText(plan.TotalMinutes) && charts.timeCaption.text.Contains("estimate") &&
                charts.timeBar.TriangleCount == 2 + 2 * PlanCharts.Timeline(plan).Count,
                charts.timeWalk.text + " | " + charts.timeWork.text + " | " + charts.timeTotal.text);
            Check("Charts: the profile is the relief along the planned route, with its length and its highest and lowest point",
                profile != null && Mathf.Abs(profile.TotalMeters - plan.WalkMeters) < 0.5f && charts.profileNumbers.text.StartsWith(HelmetHud.MetersText(plan.WalkMeters)) &&
                charts.profileNumbers.text.Contains("highest " + MissionCharts.Signed(profile.Highest) + " m") && charts.profilePlot.TriangleCount > 200,
                charts.profileNumbers.text);
            int stopCount = Mathf.Min(profile.StopNames.Length, MissionCharts.MaxStops);
            bool labels = charts.StopsShown == profile.StopNames.Length;
            string clash = "";
            for (int i = 0; i < stopCount; i++)
            {
                Text label = charts.stopLabels[i];
                if (!label.gameObject.activeSelf || label.text != profile.StopNames[i]) labels = false;
                var rect = (RectTransform)label.transform;
                float center = rect.anchoredPosition.x + MissionCharts.StopLabelWidth / 2f;
                // Under its own post, or pushed in from the edge of the plot by half its width at most.
                if (Mathf.Abs(center - (MissionCharts.PlotLeft + charts.PlotX(profile.StopMeters[i]))) > MissionCharts.StopLabelWidth / 2f + 8f) labels = false;
                for (int j = 0; j < i; j++)
                {
                    var other = (RectTransform)charts.stopLabels[j].transform;
                    bool sameRow = Mathf.Abs(other.anchoredPosition.y - rect.anchoredPosition.y) < 1f;
                    float gap = Mathf.Abs(other.anchoredPosition.x - rect.anchoredPosition.x) - (label.preferredWidth + charts.stopLabels[j].preferredWidth) / 2f;
                    if (sameRow && gap < 0f) clash += label.text + " over " + charts.stopLabels[j].text + "; ";
                }
            }
            Check("Charts: every stop is marked on the profile with its name under it, and no two names run into each other", labels && clash.Length == 0 && stopCount > 0,
                stopCount + " stops" + (clash.Length > 0 ? ": " + clash : ""));
            if (app.terrain.Site.IsMarsData && app.terrain.Site.StopsAreReal)
                Note("Charts on this site: " + charts.profileNumbers.text + "  |  " + charts.tankUsed.text + ", " + charts.tankSpare.text + "  |  " + charts.timeWalk.text + ", " + charts.timeWork.text);

            // Filling with the route's drawing: at 0 nothing, half way part, at 1 the plan's totals.
            int wholePlot = charts.profilePlot.TriangleCount;
            charts.SetProgress(0f);
            bool emptyAtStart = charts.Shown.usedLiters < 0.001f && charts.StopsShown == 0 && charts.timeBar.TriangleCount == 2 && charts.profilePlot.TriangleCount < 8;
            charts.SetProgress(0.5f);
            var half = charts.Shown;
            bool partHalfWay = half.usedLiters > 0f && half.usedLiters < plan.UsedLiters && charts.profilePlot.TriangleCount < wholePlot && charts.profilePlot.TriangleCount > 8 &&
                charts.StopsShown <= profile.StopNames.Length && Mathf.Abs(half.usedLiters - PlanCharts.At(plan, 0.5f).usedLiters) < 0.001f;
            charts.SetProgress(1f);
            Check("Charts: they fill in step with the route's drawing (empty at the start, part of the plan half way, the whole plan at the end)",
                emptyAtStart && partHalfWay && Mathf.Abs(charts.Shown.usedLiters - plan.UsedLiters) < 0.05f && charts.profilePlot.TriangleCount == wholePlot && charts.StopsShown == profile.StopNames.Length,
                "half way " + half.usedLiters.ToString("F0") + " of " + plan.UsedLiters.ToString("F0") + " L");

            // A WHAT IF redraws them at once: less in the tank, and SHORT in red where the plan does not close.
            app.SetWhatIfTank(8f);
            Check("Charts: a WHAT IF shows on the oxygen chart (tank at 8 %, SHORT in red instead of SPARE)",
                charts.tankCaption.text.Contains("tank at 8 %") && charts.tankSpare.text.StartsWith("SHORT") && charts.tankSpare.color == MissionCharts.IntoReserve && charts.IconState == VerdictState.NotViable,
                charts.tankCaption.text + " | " + charts.tankSpare.text);
            app.SetWhatIfTank(100f);
            Check("Charts: back to a full tank the oxygen chart is the plan's again", charts.tankSpare.text.StartsWith("SPARE") && charts.tankCaption.text == "OXYGEN (estimate)" && charts.IconState == VerdictState.Viable == plan.Viable);

            // The charts' texts: big enough for the board's distance, each on one line.
            string small = "", wide = "";
            foreach (var t in new[] { charts.tankCaption, charts.tankUsed, charts.tankSpare, charts.tankReserve, charts.timeCaption, charts.timeWalk, charts.timeWork, charts.timeTotal, charts.profileCaption, charts.profileNumbers })
            {
                if (t.fontSize < MissionCharts.TextSize) small += t.name + " " + t.fontSize + " px; ";
                float box = ((RectTransform)t.transform).rect.width;
                if (t.preferredWidth > box) wide += t.text + " " + t.preferredWidth.ToString("F0") + "/" + box.ToString("F0") + " px; ";
            }
            Check("Charts: every text is " + MissionCharts.TextSize + " mm or more and fits its place on one line", small.Length == 0 && wide.Length == 0, small + wide);

            // The whole main board against the project's own floor for text: 17 mm at 1.2 m, so 28 mm at the board's 1.95 m.
            Transform mainCanvas = panel.transform.Find("Canvas");
            float boardMeters = (panel.transform.position - MissionPanel.UserEye).magnitude;
            float neededMeters = 0.017f * boardMeters / 1.2f;
            string tooSmall = "";
            int texts = 0;
            if (mainCanvas != null)
                foreach (var t in mainCanvas.GetComponentsInChildren<Text>(true))
                {
                    texts++;
                    float tall = (t.resizeTextForBestFit ? t.resizeTextMinSize : t.fontSize) * Mathf.Abs(t.transform.lossyScale.y);
                    if (tall < neededMeters) tooSmall += t.name + " " + (tall * 1000f).ToString("F0") + " mm; ";
                }
            Check("Panel: no text on the main board is under the floor for its distance (17 mm at 1.2 m)", texts > 20 && tooSmall.Length == 0,
                tooSmall.Length > 0 ? tooSmall : texts + " texts, " + (neededMeters * 1000f).ToString("F1") + " mm needed at " + boardMeters.ToString("F2") + " m");

            // With the plan still on the board: the stops' names under the height chart are its lowest texts.
            CheckPillarLabelsAgainstBoard(app, panel);

            app.ClearPlannedRoute();
            panel.ShowNoRoute();
            app.terrain.ShowLayer(layerBefore);
            app.RefreshPanel();
        }

        /// <summary>
        /// The table's pillar labels stand between the user and the foot of the main board. From the user's start
        /// place none may cover a text of the board: looking at the map (the labels go up to get clear of each
        /// other), then looking at the board (a lifted label stays up until it is well clear). Patch 0039 put the
        /// stops' names 60 mm lower and Altadena's label covered one in the test's screenshot.
        /// </summary>
        static void CheckPillarLabelsAgainstBoard(WalkbackApp app, MissionPanel panel)
        {
            StopPillars pillars = app.stopPillars;
            Transform canvas = panel.transform.Find("Canvas");
            Camera cam = app.desktopCamera;
            if (pillars == null || pillars.Habitat == null || canvas == null || cam == null || app.mapRoot == null)
            {
                Note("Pillar labels against the main board: no pillars or no board on this scene, not checked");
                return;
            }
            Vector3 wasAt = cam.transform.position;
            Quaternion wasLooking = cam.transform.rotation;
            pillars.SetGrowth(1f);
            Canvas.ForceUpdateCanvases();

            // Every text the board shows now, as its box on the board's canvas (pixels = millimeters).
            var boxes = new List<Rect>();
            var boxNames = new List<string>();
            var corners = new Vector3[4];
            float lowestText = float.MaxValue;
            foreach (Text t in canvas.GetComponentsInChildren<Text>(false))
            {
                if (t.text == null || t.text.Trim().Length == 0) continue;
                t.rectTransform.GetWorldCorners(corners);
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                foreach (Vector3 corner in corners)
                {
                    Vector3 local = canvas.InverseTransformPoint(corner);
                    x0 = Mathf.Min(x0, local.x); x1 = Mathf.Max(x1, local.x);
                    y0 = Mathf.Min(y0, local.y); y1 = Mathf.Max(y1, local.y);
                    lowestText = Mathf.Min(lowestText, corner.y);
                }
                boxes.Add(Rect.MinMaxRect(x0, y0, x1, y1));
                boxNames.Add(t.name);
            }

            Vector3[] lookAts = { app.mapRoot.position, panel.transform.position };
            float reach;
            int measured;
            string covered = LabelsOverBoardTexts(pillars, cam, MissionPanel.UserEye, lookAts, canvas, boxes, boxNames, out reach, out measured);
            Check("Pillar labels: from the user's start place none covers a text of the main board (looking at the map, then at the board)",
                boxes.Count > 10 && measured > 0 && covered.Length == 0,
                covered.Length > 0 ? "covered: " + covered : "the labels reach " + reach.ToString("F3") + " m above the floor on the board, its lowest text ends at " +
                    lowestText.ToString("F3") + " m: " + ((lowestText - reach) * 1000f).ToString("F0") + " mm clear (" + boxes.Count + " texts, " + measured + " label views)");

            // Not a pass or fail: the same from 0.10 m lower, for a shorter person (the labels sit higher against the board).
            Vector3 shortEye = MissionPanel.UserEye + new Vector3(0f, -0.10f, 0f);
            float shortReach;
            int shortMeasured;
            string shortCovered = LabelsOverBoardTexts(pillars, cam, shortEye, lookAts, canvas, boxes, boxNames, out shortReach, out shortMeasured);
            Note("Pillar labels against the main board, eyes at " + shortEye.y.ToString("F2") + " m: they reach " + shortReach.ToString("F3") + " m on the board, lowest text at " +
                lowestText.ToString("F3") + " m" + (shortCovered.Length > 0 ? "; covered: " + shortCovered : "; none covered"));

            // Back to where the labels were left for the screenshots (turned to the desktop camera).
            cam.transform.SetPositionAndRotation(wasAt, wasLooking);
            pillars.FaceCamera(cam);
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>
        /// Turns the pillar labels to a camera at <paramref name="eye"/> looking at each point in turn (the lift
        /// rule remembers the look before) and lists the board's texts that a label covers from there: the label's
        /// four corners are projected from the eye onto the board's canvas. <paramref name="reach"/> is the highest
        /// point of the board's plane (meters above the floor) that a label covers, inside the board's width.
        /// </summary>
        static string LabelsOverBoardTexts(StopPillars pillars, Camera cam, Vector3 eye, Vector3[] lookAts, Transform canvas, List<Rect> boxes, List<string> boxNames,
            out float reach, out int measured)
        {
            reach = float.MinValue;
            measured = 0;
            string covered = "";
            var views = new List<PillarView> { pillars.Habitat };
            views.AddRange(pillars.Stops);
            if (pillars.TurnBack != null && pillars.TurnBack.labelRoot != null) views.Add(pillars.TurnBack);
            var plane = new Plane(canvas.forward, canvas.position);
            Rect canvasRect = ((RectTransform)canvas).rect;
            var labelCorners = new Vector3[4];
            foreach (Vector3 lookAt in lookAts)
            {
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(lookAt - eye, Vector3.up));
                pillars.FaceCamera(cam);
                foreach (PillarView view in views)
                {
                    if (view == null || view.labelRoot == null || !view.labelRoot.gameObject.activeInHierarchy) continue;
                    view.labelRoot.GetWorldCorners(labelCorners);
                    float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, top = float.MinValue;
                    bool inFront = true;
                    foreach (Vector3 corner in labelCorners)
                    {
                        // The ray from the eye through the corner, on to the board behind it.
                        var ray = new Ray(eye, corner - eye);
                        float enter;
                        if (!plane.Raycast(ray, out enter) || enter < (corner - eye).magnitude) { inFront = false; break; }
                        Vector3 onBoard = ray.GetPoint(enter);
                        Vector3 local = canvas.InverseTransformPoint(onBoard);
                        x0 = Mathf.Min(x0, local.x); x1 = Mathf.Max(x1, local.x);
                        y0 = Mathf.Min(y0, local.y); y1 = Mathf.Max(y1, local.y);
                        top = Mathf.Max(top, onBoard.y);
                    }
                    if (!inFront) continue;
                    measured++;
                    Rect shadow = Rect.MinMaxRect(x0, y0, x1, y1);
                    if (shadow.xMax > canvasRect.xMin && shadow.xMin < canvasRect.xMax) reach = Mathf.Max(reach, top);
                    for (int i = 0; i < boxes.Count; i++)
                    {
                        if (!shadow.Overlaps(boxes[i])) continue;
                        string entry = view.label.text + " over " + boxNames[i];
                        if (!covered.Contains(entry)) covered += (covered.Length > 0 ? ", " : "") + entry;
                    }
                }
            }
            return covered;
        }

        /// <summary>Relative luminance of a color (WCAG 2), for the contrast between text and its box.</summary>
        static float Luminance(Color c)
        {
            Color l = c.linear;
            return 0.2126f * l.r + 0.7152f * l.g + 0.0722f * l.b;
        }

        static float Contrast(Color a, Color b)
        {
            float la = Luminance(a), lb = Luminance(b);
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }

        static void CheckTextColor()
        {
            Color text = WalkbackTheme.Text;
            float onPanel = Contrast(text, WalkbackTheme.PanelBack), onCard = Contrast(text, WalkbackTheme.CardBack);
            Check("Theme: text is not pure white (it glares on the headset) and still reads on the dark boxes (contrast 7:1 or more)",
                text.r < 0.99f && text.g < 0.99f && text.b < 0.99f && text.r >= 0.85f && onPanel >= 7f && onCard >= 7f,
                "gray " + text.r.ToString("F2") + ", " + onPanel.ToString("F1") + ":1 on the panel, " + onCard.ToString("F1") + ":1 on a card");
        }

        static void CheckPanelSideBoard(WalkbackApp app, MissionPanel panel)
        {
            CheckTextColor();
            CheckControlsCard(panel);
            var side = panel.sideBoard;
            Check("Panel: side board present", side != null);
            if (side == null) return;
            Vector3 toBoard = side.position - MissionPanel.UserEye;
            Vector3 flat = new Vector3(toBoard.x, 0f, toBoard.z).normalized;
            Check("Panel: side board faces the user", Vector3.Dot(side.forward, flat) > 0.99f, "yaw " + side.eulerAngles.y.ToString("F0") + "°");
            Check("Panel: side board is 1.6 to 2.4 m from the user", toBoard.magnitude >= 1.6f && toBoard.magnitude <= 2.4f, toBoard.magnitude.ToString("F2") + " m");
            Vector3 innerEdge = side.position - side.right * (MissionPanel.SidePixelsWide * MissionPanel.MetersPerPixel / 2f);
            Check("Panel: side board does not cover the main board", innerEdge.x > WalkbackTheme.PanelWidthMeters / 2f + 0.02f, "inner edge at x " + innerEdge.x.ToString("F2") + " m");

            bool sizesOk = true;
            string smallest = "";
            foreach (var t in side.GetComponentsInChildren<Text>(true))
            {
                int size = t.resizeTextForBestFit ? t.resizeTextMinSize : t.fontSize;
                int needed = t.resizeTextForBestFit ? 24 : 28;
                if (size < needed) { sizesOk = false; smallest = t.name + " " + size + " px"; }
            }
            Check("Panel: side board text is 28 px or more (stop names 24 or more)", sizesOk, smallest);

            StopInfo[] stops = app.terrain.Site.Stops;
            app.SelectStop(-1);
            Check("Panel: with no stop chosen, the card says how to choose one", panel.stopName.text == "NO STOP SELECTED" && panel.stopBadge.text.Contains("pillar") && !panel.jumpButton.Interactable);
            Check("Panel: one button per stop", panel.stopButtons.Count == Mathf.Min(stops.Length, 4), panel.stopButtons.Count + " buttons, " + stops.Length + " stops");
            if (panel.stopButtons.Count == 0) return;

            panel.stopButtons[0].OnPointerClick(panel.stopButtons[0].transform.position);
            Check("Panel: pressing a stop button selects that stop", app.SelectedStop == 0 && panel.stopButtons[0].Selected);
            Check("Panel: the stop card shows its name, science and tasks with minutes",
                panel.stopName.text == stops[0].name && panel.stopScience.text == stops[0].science && panel.stopTasks.text.Contains("min"), panel.stopTasks.text);
            if (!app.terrain.Site.StopsAreReal)
                Check("Panel: a TEST STOP reads as test data", panel.stopBadge.text.Contains("TEST") && panel.stopBadge.color == WalkbackTheme.Watch, panel.stopBadge.text);
            else
                Check("Panel: a real stop shows its distance to the habitat", panel.stopBadge.text.Contains(" m from the habitat"), panel.stopBadge.text);

            int jumped = -2;
            Action<int> listen = i => jumped = i;
            app.JumpRequested += listen;
            panel.jumpButton.OnPointerClick(panel.jumpButton.transform.position);
            app.JumpRequested -= listen;
            Check("Panel: the 360° PHOTO button asks the app for the chosen stop's photo", jumped == 0, "jump request for stop " + jumped);
            Canvas.ForceUpdateCanvases();
            Text photoLabel = panel.jumpButton.GetComponentInChildren<Text>(true);
            Text startLabel = panel.missionButton != null ? panel.missionButton.GetComponentInChildren<Text>(true) : null;
            Check("Panel: the two buttons say what they are (360° PHOTO on the stop card; START MISSION with \"1:1 terrain, on foot\" under it), each on one line",
                photoLabel != null && photoLabel.text == MissionPanel.PhotoButtonText && photoLabel.preferredWidth <= ((RectTransform)photoLabel.transform).rect.width &&
                startLabel != null && startLabel.text == "START MISSION" && panel.missionCaption != null && panel.missionCaption.text == MissionPanel.MissionCaptionText &&
                panel.missionCaption.preferredWidth <= ((RectTransform)panel.missionCaption.transform).rect.width &&
                -((RectTransform)panel.missionCaption.transform).anchoredPosition.y >= -((RectTransform)panel.missionButton.transform).anchoredPosition.y + ((RectTransform)panel.missionButton.transform).sizeDelta.y,
                photoLabel != null ? photoLabel.text + " " + photoLabel.preferredWidth.ToString("F0") + " px" : "no label");

            if (panel.stopButtons.Count > 1)
            {
                app.SelectStop(1);
                Check("Panel: choosing another stop (from a pillar too) updates the card", panel.stopName.text == stops[1].name && panel.stopButtons[1].Selected && !panel.stopButtons[0].Selected);
                CheckStopPreview(app, panel, stops);
            }
            Check("Panel: tasks line adds up the minutes",
                MissionPanel.TasksLine(new[] { new StopTask { name = "Describe", minutes = 5f }, new StopTask { name = "Collect", minutes = 11f }, new StopTask { name = "Photo", minutes = 2f } })
                    .EndsWith("18 min of work"));

            // Every stop of the site, one after the other: the distance, the science, the tasks and the risk with its
            // photo credit stay inside their boxes, and the last box inside the card. The check "every text fits its
            // box" runs with no stop chosen, so it never saw a real stop's text (Nevado Sajama's ran over the card).
            RectTransform cardRect = null;
            foreach (var image in side.GetComponentsInChildren<Image>(true))
                if (image.name == "StopCard") cardRect = (RectTransform)image.transform;
            var footerRect = (RectTransform)panel.stopFooter.transform;
            bool footerInCard = cardRect != null && -footerRect.anchoredPosition.y + footerRect.sizeDelta.y <= -cardRect.anchoredPosition.y + cardRect.sizeDelta.y;
            string spills = "";
            for (int i = 0; i < panel.stopButtons.Count; i++)
            {
                app.SelectStop(i);
                Canvas.ForceUpdateCanvases();
                foreach (Text t in new[] { panel.stopBadge, panel.stopScience, panel.stopTasks, panel.stopFooter })
                {
                    float box = ((RectTransform)t.transform).rect.height;
                    if (t.preferredHeight > box + 6f) spills += stops[i].name + " " + t.name + " " + t.preferredHeight.ToString("F0") + "/" + box.ToString("F0") + " px; ";
                }
            }
            Check("Panel: every stop's card fits (distance, science, tasks, risk and photo credit inside their boxes, the last one inside the card)",
                footerInCard && spills.Length == 0, spills.Length > 0 ? spills : panel.stopButtons.Count + " stops");
            app.SelectStop(-1);
        }

        /// <summary>
        /// The MAP AND SOURCES board: over the controls card, clear of it and of the main board, facing the user;
        /// and the site line, the legend of each of the four layers (and of a WHAT IF) and the footer inside their boxes.
        /// </summary>
        static void CheckSourceBoard(WalkbackApp app, MissionPanel panel)
        {
            Transform board = panel.sourceBoard;
            GlobeLabel controls = panel.controlsCard;
            Check("Source board: present, with the site line, the layer legend and the footer on it",
                board != null && panel.dataLine != null && panel.legendLine != null && panel.footer != null &&
                panel.dataLine.transform.IsChildOf(board) && panel.legendLine.transform.IsChildOf(board) && panel.footer.transform.IsChildOf(board));
            if (board == null || controls == null || panel.dataLine == null || panel.legendLine == null || panel.footer == null) return;

            float halfWide = MissionPanel.SourcePixelsWide * MissionPanel.MetersPerPixel / 2f, halfHigh = MissionPanel.SourcePixelsHigh * MissionPanel.MetersPerPixel / 2f;
            float gap = (board.position.y - halfHigh) - (controls.transform.position.y + controls.backPlate.lossyScale.y / 2f);
            Vector3 toBoard = board.position - MissionPanel.UserEye;
            float meters = toBoard.magnitude;
            toBoard.y = 0f;
            Vector3 innerEdge = board.position + board.right * halfWide;
            float letters = panel.legendLine.fontSize * Mathf.Abs(panel.legendLine.transform.lossyScale.y);
            Check("Source board: right over the controls card without touching it, clear of the main board, facing the user, letters big enough for its distance",
                gap >= 0.02f && gap <= 0.08f && Mathf.Abs(board.position.x - controls.transform.position.x) < 0.001f && Mathf.Abs(board.position.z - controls.transform.position.z) < 0.001f &&
                Vector3.Dot(board.forward, toBoard.normalized) > 0.99f && innerEdge.x < -(WalkbackTheme.PanelWidthMeters / 2f + 0.02f) && letters >= 0.017f * meters / 1.2f,
                (gap * 1000f).ToString("F0") + " mm over the controls card, inner edge at x " + innerEdge.x.ToString("F2") + " m, " + (letters * 1000f).ToString("F0") + " mm letters at " + meters.ToString("F2") + " m");

            // Every layer's legend, then the return layer's legend in a WHAT IF (its longest).
            MapLayer layerBefore = app.terrain.Layer;
            string spills = "";
            System.Action<string> measure = which =>
            {
                app.RefreshPanel();
                Canvas.ForceUpdateCanvases();
                foreach (Text t in new[] { panel.dataLine, panel.legendLine, panel.footer })
                {
                    float box = ((RectTransform)t.transform).rect.height;
                    if (t.preferredHeight > box + 6f) spills += which + " " + t.name + " " + t.preferredHeight.ToString("F0") + "/" + box.ToString("F0") + " px; ";
                }
            };
            foreach (MapLayer layer in new[] { MapLayer.Photo, MapLayer.Height, MapLayer.Slope, MapLayer.Return })
            {
                app.terrain.ShowLayer(layer);
                measure(layer.ToString());
            }
            app.SetWhatIfTank(15f);
            bool whatIfLegend = app.terrain.Layer == MapLayer.Return;
            measure("WHAT IF");
            whatIfLegend = whatIfLegend && panel.legendLine.text.Contains("red:");
            app.SetWhatIfTank(100f);
            app.ClearPlannedRoute();
            panel.ShowNoRoute();
            app.terrain.ShowLayer(layerBefore);
            app.RefreshPanel();
            Check("Source board: the site line, the legend of each of the four layers and of a WHAT IF, and the footer fit their boxes", spills.Length == 0 && whatIfLegend, spills);
        }

        static void CheckPanelConditions(MissionPanel panel)
        {
            if (!panel.ConditionsHaveData)
                Check("Panel: no conditions.json -> 'waiting for data' and no numbers", panel.conditionsText.text == Conditions.WaitingText && !HasDigit(panel.conditionsText.text));
            else
                Check("Panel: conditions.json from session B is on the card", panel.conditionsText.text.Length > 0, panel.conditionsText.text.Replace("\n", " | "));

            const string sample = "{\"rems\":{\"sol\":4720,\"earthDate\":\"2026-11-10\",\"air\":{\"min\":-78.2,\"max\":-4.6},\"ground\":{\"min\":-85,\"max\":3}," +
                "\"pressure\":822,\"source\":\"NASA/JPL-Caltech/CAB(CSIC-INTA)\"},\"donki\":{\"from\":\"2026-10-15\",\"to\":\"2026-11-14\"," +
                "\"events\":[{\"activityType\":\"CME\"},{\"activityType\":\"FLR\"}],\"checked\":\"2026-11-14\"},\"season\":{\"value\":\"Ls 22°, early southern autumn\",\"source\":\"Hope/EMIRS\"}," +
                "\"daylight\":\"06:02–17:58 local solar time\",\"radiation\":{\"value\":\"0.64 mSv/day\",\"source\":\"Curiosity RAD\"}}";
            var view = Conditions.Describe(sample);
            string t = view.Text;
            Check("Conditions: REMS sol, Earth date, air, ground and pressure", view.HasData && t.Contains("sol 4720") && t.Contains("Earth date 2026-11-10") &&
                t.Contains("Air -78 / -5 °C") && t.Contains("Ground -85 / 3 °C") && t.Contains("Pressure 822 Pa"), t.Replace("\n", " | "));
            Check("Conditions: season, daylight and radiation with their sources", t.Contains("Season: Ls 22°") && t.Contains("Hope/EMIRS") && t.Contains("Daylight: 06:02") && t.Contains("Radiation: 0.64 mSv/day"));
            Check("Conditions: DONKI window, event count and types", t.Contains("2026-10-15 to 2026-11-14") && t.Contains("2 events (CME, FLR)") && t.Contains("checked 2026-11-14"));

            const string other = "{\"REMS\":{\"Sol\":\"4720\",\"terrestrial_date\":\"2026-11-10\",\"min_air_temp_K\":195,\"max_air_temp_K\":268,\"min_ground_temp_K\":188,\"max_ground_temp_K\":276,\"pressure_hPa\":8.2}}";
            string t2 = Conditions.Describe(other).Text;
            Check("Conditions: other field names, kelvin and hPa are understood", t2.Contains("sol 4720") && t2.Contains("Air -78 / -5 °C") && t2.Contains("Ground -85 / 3 °C") && t2.Contains("Pressure 820 Pa"), t2.Replace("\n", " | "));

            var broken = Conditions.Describe("{not json");
            Check("Conditions: a broken file says so and shows no numbers", !broken.HasData && broken.Text.Contains("could not be read") && !HasDigit(broken.Text));
            Check("Conditions: an empty file waits for data", Conditions.Describe("{}").Text == Conditions.WaitingText);

            // Live space weather (R18): the pure parts only. The test never goes to the network.
            var liveAt = new System.DateTime(2026, 11, 15, 18, 0, 0, System.DateTimeKind.Utc);
            string liveUrl = SpaceWeatherLive.Url(liveAt);
            Check("Live space weather: one request to NASA DONKI for the solar particle events of the last 7 days", liveUrl.StartsWith("https://api.nasa.gov/DONKI/SEP?") &&
                liveUrl.Contains("startDate=2026-11-08") && liveUrl.Contains("endDate=2026-11-15") && SpaceWeatherLive.TimeoutSeconds <= 5, liveUrl);
            string liveNone = SpaceWeatherLive.Line("[]", liveAt);
            Check("Live space weather: an empty list reads 'no solar particle events' with the window, the time of the check and (live)", liveNone != null &&
                liveNone.Contains("2026-11-08 to 2026-11-15") && liveNone.Contains("no solar particle events") && liveNone.Contains("checked 2026-11-15 18:00 UTC") &&
                liveNone.EndsWith("(live)") && !liveNone.Contains("stored"), liveNone);
            string liveTwo = SpaceWeatherLive.Line("[{\"a\":1},{\"a\":2}]", liveAt);
            string liveOne = SpaceWeatherLive.Line(" [ {\"a\":1} ] ", liveAt);
            Check("Live space weather: it counts the events NASA lists and says nothing else about them", liveTwo != null && liveTwo.Contains("): 2 solar particle events reported · checked") &&
                liveOne != null && liveOne.Contains("): 1 solar particle event reported · checked"), liveTwo);
            Check("Live space weather: an error object, a web page, an empty answer or a list of something else gives no line (the stored one stays)",
                SpaceWeatherLive.Line("{\"error\":{\"code\":\"OVER_RATE_LIMIT\"}}", liveAt) == null && SpaceWeatherLive.Line("<html><body>Sign in</body></html>", liveAt) == null &&
                SpaceWeatherLive.Line("", liveAt) == null && SpaceWeatherLive.Line(null, liveAt) == null && SpaceWeatherLive.Line("[1,2]", liveAt) == null && SpaceWeatherLive.Line("[{\"a\":1}", liveAt) == null);
            Check("Conditions: the space-weather line from the file says (stored in the app), never (live)", t.Contains("checked 2026-11-14 (stored in the app)") && !t.Contains("(live)"), t.Replace("\n", " | "));
            string swapped = SpaceWeatherLive.Replace(t, liveNone);
            string[] linesBefore = t.Split('\n'), linesAfter = swapped.Split('\n');
            bool onlyThatLine = linesBefore.Length == linesAfter.Length;
            int changedLines = 0;
            for (int li = 0; onlyThatLine && li < linesBefore.Length; li++) if (linesBefore[li] != linesAfter[li]) changedLines++;
            Check("Live space weather: only the space-weather line of the card changes", onlyThatLine && changedLines == 1 && swapped.Contains("(live)") && !swapped.Contains("(stored in the app)") &&
                SpaceWeatherLive.Replace(t, null) == t && SpaceWeatherLive.Replace("Radiation: x", liveNone) == "Radiation: x\n" + liveNone, swapped.Replace("\n", " | "));
            if (panel.ConditionsHaveData && panel.conditionsText != null)
            {
                string cardBefore = panel.conditionsText.text;
                Check("Panel: the card's own space-weather line says (stored in the app)", cardBefore.Contains(SpaceWeatherLive.LinePrefix) && cardBefore.Contains("(stored in the app)") && !cardBefore.Contains("(live)"), cardBefore.Replace("\n", " | "));
                panel.ShowLiveSpaceWeather(SpaceWeatherLive.Line("[{\"a\":1},{\"a\":2},{\"a\":3},{\"a\":4},{\"a\":5},{\"a\":6},{\"a\":7},{\"a\":8},{\"a\":9},{\"a\":10},{\"a\":11},{\"a\":12}]", liveAt));
                Canvas.ForceUpdateCanvases();
                float liveBox = ((RectTransform)panel.conditionsText.transform).rect.height;
                Check("Panel: the card with a live space-weather line still fits its box", panel.conditionsText.text.Contains("12 solar particle events reported") &&
                    panel.conditionsText.preferredHeight <= liveBox + 6f, panel.conditionsText.preferredHeight.ToString("F0") + "/" + liveBox.ToString("F0") + " px");
                panel.conditionsText.text = cardBefore;
            }
        }

        /// <summary>The controls card: there, readable, facing the user, clear of the main board, every control on one line.</summary>
        static void CheckControlsCard(MissionPanel panel)
        {
            GlobeLabel card = panel.controlsCard;
            Check("Controls card: next to the table, a child of the panel (it hides and moves with it)", card != null && card.transform.IsChildOf(panel.transform) && card.gameObject.activeSelf);
            if (card == null) return;
            string all = "";
            foreach (var line in card.lines) all += line.text + "\n";
            Check("Controls card: says how to change layers, WHAT IF, MISSION, JUMP, MAP and RECENTER",
                all.Contains("LAYERS") && all.Contains("WHAT IF") && all.Contains("MISSION") && all.Contains("JUMP") && all.Contains("MAP · X") && all.Contains("RECENTER"));
            Canvas.ForceUpdateCanvases();
            string wide = "";
            float smallest = float.MaxValue;
            bool uiOnly = true;
            foreach (var line in card.lines)
            {
                float box = ((RectTransform)line.transform).rect.width;
                if (line.preferredWidth > box) wide += line.text + " " + line.preferredWidth.ToString("F0") + "/" + box.ToString("F0") + " px; ";
                smallest = Mathf.Min(smallest, line.fontSize * line.transform.lossyScale.y);
                if (line.material == null || line.material.shader.name != "Walkback/UI") uiOnly = false;
            }
            Vector3 toCard = card.transform.position - MissionPanel.UserEye;
            float meters = toCard.magnitude;
            // 17 mm at 1.2 m is the project's floor for text; farther away the letters must be that much taller.
            float needed = 0.017f * meters / 1.2f;
            Check("Controls card: every control on one line, letters big enough for its distance, never writing alpha",
                wide.Length == 0 && smallest >= needed && uiOnly, wide.Length > 0 ? wide : (smallest * 1000f).ToString("F0") + " mm letters at " + meters.ToString("F2") + " m (needs " + (needed * 1000f).ToString("F0") + " mm)");
            toCard.y = 0f;
            Vector3 innerEdge = card.transform.position + card.transform.right * (card.backPlate.lossyScale.x / 2f);
            Check("Controls card: faces the user and does not cover the main board",
                Vector3.Dot(card.transform.forward, toCard.normalized) > 0.99f && innerEdge.x < -(WalkbackTheme.PanelWidthMeters / 2f + 0.02f),
                "inner edge at x " + innerEdge.x.ToString("F2") + " m");
        }

        /// <summary>Every fixed text on both boards fits its box (a text spilling over the next one reads as a bug on the projector).</summary>
        static void CheckPanelTextFits(MissionPanel panel)
        {
            Canvas.ForceUpdateCanvases();
            string overflow = "";
            foreach (var text in panel.GetComponentsInChildren<Text>(true))
            {
                if (text.resizeTextForBestFit) continue;
                var r = ((RectTransform)text.transform).rect;
                if (text.preferredHeight > r.height + 6f) overflow += text.name + " " + text.preferredHeight.ToString("F0") + "/" + r.height.ToString("F0") + " px; ";
            }
            Check("Panel: every text fits its box", overflow.Length == 0, overflow);

            string wrapped = "";
            foreach (var button in panel.GetComponentsInChildren<PanelButton>(true))
            {
                if (button.label.resizeTextForBestFit) continue;
                var r = ((RectTransform)button.label.transform).rect;
                if (button.label.preferredWidth > r.width) wrapped += button.label.text + " " + button.label.preferredWidth.ToString("F0") + "/" + r.width.ToString("F0") + " px; ";
            }
            Check("Panel: every button label fits on one line", wrapped.Length == 0, wrapped);

            // Every button is at least 100 mm tall in the room (2.9° at 2 m: a hand ray hits it), and none
            // shrinks its text to fit: a name too long for its button must fail the check above, not get small.
            string small = "";
            bool shrinks = false;
            float lowest = float.MaxValue;
            foreach (var button in panel.GetComponentsInChildren<PanelButton>(true))
            {
                var rt = (RectTransform)button.transform;
                float tall = rt.rect.height * Mathf.Abs(rt.lossyScale.y);
                lowest = Mathf.Min(lowest, tall);
                if (tall < 0.0999f) small += button.label.text + " " + (tall * 1000f).ToString("F0") + " mm; ";
                if (button.label.resizeTextForBestFit) shrinks = true;
            }
            Check("Panel: every button is at least 100 mm tall and keeps its text size", small.Length == 0 && !shrinks && lowest < float.MaxValue,
                small.Length > 0 ? small : (lowest * 1000f).ToString("F0") + " mm the lowest");
        }

        static partial void ShotsPanelPiece(WalkbackApp app)
        {
            var panel = app.panel;
            var cam = app.desktopCamera;
            Vector3 homePosition = cam.transform.position;
            Quaternion homeRotation = cam.transform.rotation;
            Vector3 panelEye = new Vector3(0f, 1.5f, 0.35f);

            // 1. VIABLE, slope layer on, the pointer over RETURN ON FOOT. Hand-made test values, said on the card.
            app.terrain.ShowLayer(MapLayer.Slope);
            app.RefreshPanel();
            panel.layerButtons[2].OnPointerEnter();
            panel.ShowVerdict(VerdictState.Viable, null, ViableTestLine, "TEST VALUES for this screenshot only: the walking model is not wired yet.");
            Canvas.ForceUpdateCanvases();
            Shot(cam, "panel_01_viable.png", panelEye, SceneBuilder.PanelCenter);
            panel.layerButtons[2].OnPointerExit();

            // 2. NOT VIABLE with the point of no return.
            panel.ShowVerdict(VerdictState.NotViable, null, "O2 used 81 % · the walk back from the far stop needs 96 %",
                "TEST VALUES · point of no return: 640 m along the route, before TEST STOP 2");
            Canvas.ForceUpdateCanvases();
            Shot(cam, "panel_02_not_viable.png", panelEye, SceneBuilder.PanelCenter);

            // 3. The side board with the first stop chosen.
            panel.ShowNoRoute();
            app.terrain.ShowLayer(MapLayer.Height);
            app.RefreshPanel();
            if (panel.stopButtons.Count > 0) app.SelectStop(0);
            if (panel.sideBoard != null)
            {
                panel.jumpButton.OnPointerEnter();
                Canvas.ForceUpdateCanvases();
                Vector3 side = panel.sideBoard.position;
                Shot(cam, "panel_03_stop_card.png", side - panel.sideBoard.forward * 0.85f, side);
                panel.jumpButton.OnPointerExit();
            }

            // 4. Both boards as the user sees them at start (no route yet, one stop chosen).
            Canvas.ForceUpdateCanvases();
            Shot(cam, "panel_04_boards.png", new Vector3(0.35f, 1.55f, -0.25f), new Vector3(0.55f, 1.45f, 1.9f));

            app.SelectStop(-1);
            panel.ShowNoRoute();

            // 5 to 7. The charts with the real plan: whole, half drawn (as while the route draws itself), and a WHAT IF.
            MapLayer layerBefore = app.terrain.Layer;
            app.PlanMission(false);
            if (app.Plan != null && app.Plan.HasRoute && panel.charts != null)
            {
                app.terrain.ShowLayer(MapLayer.Photo);
                app.RefreshPanel();
                Canvas.ForceUpdateCanvases();
                Shot(cam, "panel_05_charts_plan.png", panelEye, SceneBuilder.PanelCenter);
                Shot(cam, "panel_05b_charts_from_the_user.png", MissionPanel.UserEye, SceneBuilder.PanelCenter);
                panel.charts.SetProgress(0.5f);
                Canvas.ForceUpdateCanvases();
                Shot(cam, "panel_06_charts_half_drawn.png", panelEye, SceneBuilder.PanelCenter);
                panel.charts.SetProgress(1f);
                app.SetWhatIfTank(15f);
                app.RefreshPanel();
                Canvas.ForceUpdateCanvases();
                Shot(cam, "panel_07_charts_what_if_15.png", panelEye, SceneBuilder.PanelCenter);
                app.SetWhatIfTank(100f);
            }

            // 8 to 10 (v1.1). A stop picked on the map with 15 minutes of work, its card, and a preview of another stop.
            ShotsUserStops(app, panel, cam, panelEye);
            ShotTurnBack(app, cam, panelEye);

            app.ClearPlannedRoute();
            panel.ShowNoRoute();
            app.terrain.ShowLayer(layerBefore);
            app.RefreshPanel();
            cam.transform.SetPositionAndRotation(homePosition, homeRotation);
        }

        static bool HasDigit(string s)
        {
            foreach (char c in s) if (char.IsDigit(c)) return true;
            return false;
        }
    }
}
