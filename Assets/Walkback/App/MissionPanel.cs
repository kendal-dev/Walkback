// Written with Claude Code (AI), directed and tested by team MARSXR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Walkback
{
    public enum VerdictState { NoRoute, Viable, NotViable }

    /// <summary>
    /// The mission board behind the table: 1.50 m x 1.00 m, one canvas pixel = one millimeter.
    /// It says only what the plan comes to, with 30 mm of air between its cards: the verdict card with
    /// its icon on top, then the charts (MissionCharts) in three cards: the oxygen tank, the mission's
    /// time, the height along the route.
    /// A side board to the right, turned to the user, holds the stop card and the current conditions.
    /// A source board to the left, over the controls card, holds the lines a chart cannot replace: the site
    /// and its source, the map layer's legend, and what the map does not show (rule 1: still on screen).
    /// Back plates are opaque quads and every text and box uses the Walkback/UI material, which never
    /// writes alpha, so text edges do not open "holes" to the room in passthrough.
    /// </summary>
    public class MissionPanel : MonoBehaviour
    {
        public const int PixelsWide = 1500;
        public const int PixelsHigh = 1000;
        public const float MetersPerPixel = 0.001f;

        public const int SidePixelsWide = 760;
        public const int SidePixelsHigh = 1250;
        /// <summary>
        /// Every button on the boards is at least this tall (1 px = 1 mm): 100 mm at about 2 m is 2.9°,
        /// inside the 2.5° to 3° that a hand ray needs to hit a target comfortably.
        /// </summary>
        public const float ButtonHeight = 100f;
        /// <summary>Left and right margin of the main board, and the top of the verdict card (pixels = mm).</summary>
        public const float Margin = 50f;
        public const float VerdictTop = 184f;
        /// <summary>
        /// Side board center relative to the panel center (the panel is not rotated). The side pieces stand
        /// 0.40 m nearer the user than the main board, about 1.95 m from the eyes like it, so their 28-30 mm
        /// letters stay over the floor for that distance (at 0.22 m nearer they were 2.1-2.2 m away).
        /// </summary>
        public static readonly Vector3 SideBoardOffset = new Vector3(1.22f, -0.17f, -0.40f);
        /// <summary>Where the user's eyes are when the scene starts (floor origin, standing).</summary>
        public static readonly Vector3 UserEye = new Vector3(0f, 1.6f, 0f);

        public static readonly string[] LayerLabels = { "HEIGHT", "SLOPE", "RETURN", "PHOTO" };

        /// <summary>
        /// The controls card to the left of the main board, turned to face the user like the side board
        /// on the right: what each control does, for someone who puts the headset on for the first time.
        /// </summary>
        public static readonly Vector3 ControlsCardOffset = new Vector3(-1.16f, -0.35f, -0.40f);
        /// <summary>The source board: left of the main board, right over the controls card, turned to the user.</summary>
        public static readonly Vector3 SourceBoardOffset = new Vector3(-1.16f, 0.145f, -0.40f);
        public const int SourcePixelsWide = 760;
        public const int SourcePixelsHigh = 560;
        public static readonly string[] ControlsText =
        {
            "CONTROLS",
            "LAYERS · point at a button and press",
            "WHAT IF · click the left stick",
            "MISSION · START MISSION, or left menu",
            "JUMP · NEXT STOP sign, or left grip",
            "MAP · X: in the air / on the wrist",
            "RECENTER · B or Y",
        };
        /// <summary>Built by the scene builder as a child of the panel, so it hides and moves with it.</summary>
        public GlobeLabel controlsCard;

        [Header("Main board")]
        public Text title;
        public Text tagline;
        public Text verdictTitle;
        public Text verdictDetail;
        public Text verdictNote;
        public Image verdictAccent;
        /// <summary>The verdict's icon and the three charts under the verdict card.</summary>
        public MissionCharts charts;
        public Text dataLine;
        public Text legendLine;
        public Text footer;
        public PanelButton[] layerButtons = new PanelButton[0];
        /// <summary>
        /// START MISSION on the verdict card: the same as the left menu button, but pressed with the pointer,
        /// so it works with hands too. In the headset at 18:30 the mission was never entered from the menu button.
        /// </summary>
        public PanelButton missionButton;
        /// <summary>One line under START MISSION that says what it is.</summary>
        public Text missionCaption;
        public const string MissionCaptionText = "1:1 terrain, on foot";
        /// <summary>
        /// The stop card's button opens Curiosity's real 360° photo of that stop. It was JUMP TO SITE: the
        /// mission also has a "jump" (NEXT STOP), and neither name told the photo from the walk.
        /// </summary>
        public const string PhotoButtonText = "360° PHOTO";

        [Header("Side board")]
        public Transform sideBoard;
        /// <summary>The board to the left with the source line, the legend and the footer.</summary>
        public Transform sourceBoard;
        public RectTransform stopButtonRow;
        /// <summary>v1.1: takes away the stops the astronaut picked on the map. Off while there are none.</summary>
        public PanelButton clearStopsButton;
        /// <summary>v1.1: on a picked stop's card, take 5 minutes of work away / add 5 (hidden for the data's stops).</summary>
        public PanelButton lessMinutesButton;
        public PanelButton moreMinutesButton;
        /// <summary>v1.1: on a picked stop's card, in the place of 360° PHOTO (a picked stop has no photo): takes that stop away.</summary>
        public PanelButton removeStopButton;
        public const string RemoveStopText = "REMOVE";
        public const string ClearStopsText = "CLEAR";
        /// <summary>The id of the stop behind each stop button (only the data's stops get a button).</summary>
        readonly List<string> stopButtonIds = new List<string>();
        public Text stopsLabel;
        public Text stopName;
        public Text stopBadge;
        public Text stopScience;
        public Text stopTasks;
        public Text stopFooter;
        public PanelButton jumpButton;
        public Text conditionsTitle;
        public Text conditionsText;

        [Header("Build")]
        public Material backPlateMaterial;
        public Material uiMaterial;
        public Font font;

        public VerdictState Verdict { get; private set; } = VerdictState.NoRoute;
        public bool ConditionsHaveData { get; private set; }
        public readonly List<PanelButton> stopButtons = new List<PanelButton>();

        WalkbackApp boundApp;

        /// <summary>The line under the verdict: the site and its source, or the not-Mars-data warning.</summary>
        public void ShowSite(string sourceLine, bool isMarsData)
        {
            if (dataLine == null) return;   // source board not built
            dataLine.text = sourceLine;
            dataLine.color = isMarsData ? WalkbackTheme.Text : WalkbackTheme.Watch;
        }

        /// <summary>What the colors on the map mean (rich text).</summary>
        public void ShowLegend(string legend)
        {
            if (legendLine != null) legendLine.text = legend;
        }

        // ---------------------------------------------------------------- verdict

        /// <summary>
        /// The verdict card. headline: "VIABLE", "NOT VIABLE", "NO SAFE ROUTE"... (null = the state's default).
        /// summaryLine: ficha A, e.g. "O2 used 23 % · margin to return 5 h 40 min · reserve 30 min untouched".
        /// detail: one more line, e.g. where the point of no return is. Empty = the state's default sentence.
        /// Numbers come only from session C's model: this panel never makes one up.
        /// </summary>
        public void ShowVerdict(VerdictState state, string headline, string summaryLine, string detail)
        {
            Verdict = state;
            Color color = state == VerdictState.Viable ? WalkbackTheme.Safe : state == VerdictState.NotViable ? WalkbackTheme.Danger : WalkbackTheme.Watch;
            verdictTitle.text = !string.IsNullOrEmpty(headline) ? headline : DefaultHeadline(state);
            verdictTitle.color = color;
            if (verdictAccent != null) verdictAccent.color = color;
            verdictDetail.text = !string.IsNullOrEmpty(summaryLine) ? summaryLine : "Time  ·  oxygen  ·  margin to return";
            if (verdictNote != null) verdictNote.text = !string.IsNullOrEmpty(detail) ? detail : DefaultDetail(state);
            if (charts != null) charts.ShowIcon(state);
        }

        /// <summary>The runtime default until the walking model gives a route: no verdict, and empty charts.</summary>
        public void ShowNoRoute()
        {
            ShowVerdict(VerdictState.NoRoute, null, null, null);
            if (charts != null) charts.ShowNoPlan();
            if (missionButton != null) missionButton.SetInteractable(false);
        }

        /// <summary>The charts for a plan (oxygen, time, height along the route), or empty ones for no plan.</summary>
        public void ShowCharts(SiteData site, MissionFallback.Plan plan)
        {
            // A route to walk is enough to start the mission, viable or not (a WHAT IF can be walked too).
            if (missionButton != null) missionButton.SetInteractable(site != null && plan != null && plan.HasRoute && plan.Legs.Count > 0);
            if (charts == null) return;
            if (site == null || plan == null) charts.ShowNoPlan();
            else charts.ShowPlan(site, plan);
        }

        static string DefaultHeadline(VerdictState state)
        {
            return state == VerdictState.Viable ? "VIABLE" : state == VerdictState.NotViable ? "NOT VIABLE" : "NO ROUTE YET";
        }

        static string DefaultDetail(VerdictState state)
        {
            if (state == VerdictState.Viable) return "Oxygen to walk back from every point, reserve untouched.";
            if (state == VerdictState.NotViable) return "The oxygen left does not cover the walk back.";
            return "No route has been planned for this map.";
        }

        // ---------------------------------------------------------------- binding (runtime and test)

        /// <summary>
        /// Wires buttons and events to the app. Called at runtime (StartPanelPiece) and by the test.
        /// Delegates are not saved in the scene, so this runs after every load.
        /// </summary>
        public void Bind(WalkbackApp app)
        {
            Unbind();
            boundApp = app;
            for (int i = 0; i < layerButtons.Length; i++)
            {
                var layer = (MapLayer)i;
                layerButtons[i].onClick = () => { if (boundApp != null && boundApp.terrain != null) boundApp.terrain.ShowLayer(layer); };
            }
            if (app.terrain != null)
            {
                app.terrain.LayerChanged += OnLayerChanged;
                SetActiveLayer(app.terrain.Layer);
            }
            app.StopSelected += OnStopSelected;
            app.StopPreviewed += OnStopPreviewed;
            app.UserStopsChanged += OnUserStopsChanged;
            if (clearStopsButton != null)
                clearStopsButton.onClick = () => { if (boundApp != null) boundApp.ClearUserStops(); };
            if (lessMinutesButton != null)
                lessMinutesButton.onClick = () => { if (boundApp != null) boundApp.ChangeUserStopMinutes(boundApp.SelectedStop, -UserStops.StepMinutes); };
            if (removeStopButton != null)
                removeStopButton.onClick = () => { if (boundApp != null) boundApp.RemoveUserStop(boundApp.SelectedStop); };
            if (moreMinutesButton != null)
                moreMinutesButton.onClick = () => { if (boundApp != null) boundApp.ChangeUserStopMinutes(boundApp.SelectedStop, UserStops.StepMinutes); };
            OnUserStopsChanged();
            BuildStopButtons(app.terrain != null && app.terrain.Site != null ? app.terrain.Site.Stops : new StopInfo[0]);
            if (jumpButton != null)
                jumpButton.onClick = () => { if (boundApp != null && boundApp.SelectedStop >= 0) boundApp.RequestJump(boundApp.SelectedStop); };
            if (missionButton != null)
                missionButton.onClick = () => { if (boundApp != null) boundApp.StartMissionFromBoard(); };
            ShowConditions(Conditions.Load());
            ShowStop(app.SelectedStop);
        }

        public void Unbind()
        {
            if (boundApp != null)
            {
                if (boundApp.terrain != null) boundApp.terrain.LayerChanged -= OnLayerChanged;
                boundApp.StopSelected -= OnStopSelected;
                boundApp.StopPreviewed -= OnStopPreviewed;
                boundApp.UserStopsChanged -= OnUserStopsChanged;
            }
            boundApp = null;
        }

        void OnDestroy()
        {
            Unbind();
        }

        void OnLayerChanged(MapLayer layer)
        {
            SetActiveLayer(layer);
        }

        void OnStopSelected(int index)
        {
            ShowStop(index);
        }

        void OnUserStopsChanged()
        {
            if (clearStopsButton != null) clearStopsButton.SetInteractable(boundApp != null && boundApp.UserStopCount > 0);
        }

        /// <summary>Lights the button of the stop at this index (none if that stop has no button).</summary>
        void LightStopButton(int index)
        {
            StopInfo[] stops = boundApp != null && boundApp.terrain != null && boundApp.terrain.Site != null ? boundApp.terrain.Site.Stops : null;
            string id = stops != null && index >= 0 && index < stops.Length && stops[index] != null ? stops[index].id : null;
            for (int i = 0; i < stopButtons.Count; i++) stopButtons[i].SetSelected(id != null && i < stopButtonIds.Count && stopButtonIds[i] == id);
        }

        /// <summary>First line of the stop card while the pointer is on a pillar that is not the selected stop.</summary>
        public const string PreviewBadge = "PREVIEW · press the pillar to select it";

        /// <summary>True while the stop card shows a pillar under the pointer instead of the selected stop.</summary>
        public bool ShowingPreview { get; private set; }

        /// <summary>
        /// The pointer went onto a stop's pillar (index) or left it (-1). The card shows that stop as a preview;
        /// the selected stop, its button and the 360° PHOTO button do not change, and the card comes back on leaving.
        /// </summary>
        void OnStopPreviewed(int index)
        {
            int selected = boundApp != null ? boundApp.SelectedStop : -1;
            if (index < 0 || index == selected)
            {
                if (ShowingPreview) ShowStop(selected);
                return;
            }
            ShowPreview(index);
        }

        /// <summary>The stop card for one stop as a preview (the test calls it too).</summary>
        public void ShowPreview(int index)
        {
            ShowStop(index);
            if (stopName == null) return;
            SiteData site = boundApp != null && boundApp.terrain != null ? boundApp.terrain.Site : null;
            if (site == null || index < 0 || index >= site.Stops.Length) return;
            LightStopButton(boundApp.SelectedStop);
            stopBadge.text = PreviewBadge;
            stopBadge.color = WalkbackTheme.Live;
            // The 360° PHOTO button jumps to the selected stop, not to this one: off while previewing.
            if (jumpButton != null) jumpButton.SetInteractable(false);
            ShowMinuteButtons(false, null);
            ShowingPreview = true;
        }

        public void SetActiveLayer(MapLayer layer)
        {
            for (int i = 0; i < layerButtons.Length; i++)
                if (layerButtons[i] != null) layerButtons[i].SetSelected(i == (int)layer);
        }

        // ---------------------------------------------------------------- stop card

        /// <summary>One button per stop on the side board (stops are known only after the site loads).</summary>
        public void BuildStopButtons(StopInfo[] stops)
        {
            foreach (var b in stopButtons) if (b != null) DestroySafe(b.gameObject);
            stopButtons.Clear();
            stopButtonIds.Clear();
            if (stopButtonRow == null || stops == null) return;
            // v1.1: the stops picked on the map have a pillar but no button; the buttons keep the data's stops.
            stops = UserStops.Without(stops);
            int n = Mathf.Min(stops.Length, 4);
            if (n == 0) return;
            // Up to two buttons side by side; three or four go in two rows of two. A button is then never
            // narrower than 294 px, so a name like "Valle de la Luna" stays on one line at full size
            // (four in a row left 141 px each and broke the names in two).
            const float rowWidth = 600f, gap = 12f;
            int columns = Mathf.Min(n, 2);
            float w = (rowWidth - gap * (columns - 1)) / columns;
            for (int i = 0; i < n; i++)
            {
                int index = i;
                string label = string.IsNullOrEmpty(stops[i].name) ? "Stop " + (i + 1) : stops[i].name;
                var button = PanelButton.Create(stopButtonRow, "Stop" + i, label, font, uiMaterial, n <= 2 ? 30 : 28,
                    (i % columns) * (w + gap), (i / columns) * (ButtonHeight + gap), w, ButtonHeight);
                string id = stops[i].id;
                button.onClick = () =>
                {
                    if (boundApp == null) return;
                    int now = boundApp.terrain != null && boundApp.terrain.Site != null ? UserStops.IndexOf(boundApp.terrain.Site.Stops, id) : -1;
                    boundApp.SelectStop(id != null && now >= 0 ? now : index);
                };
                stopButtons.Add(button);
                stopButtonIds.Add(id);
            }
        }

        /// <summary>Fills the stop card for one stop of the site (index into Site.Stops), or the hint for none.</summary>
        public void ShowStop(int index)
        {
            if (stopName == null) return;   // side board not built
            ShowingPreview = false;
            ShowMinuteButtons(false, null);
            SiteData site = boundApp != null && boundApp.terrain != null ? boundApp.terrain.Site : null;
            StopInfo[] stops = site != null ? site.Stops : new StopInfo[0];
            LightStopButton(index);

            if (index < 0 || index >= stops.Length || stops[index] == null)
            {
                stopName.text = "NO STOP SELECTED";
                stopBadge.text = "Press a pillar or a stop button.";
                stopBadge.color = WalkbackTheme.Live;
                stopScience.text = "Science, tasks and minutes of each stop.";
                stopTasks.text = stops.Length > 0 ? stops.Length + " stops on this map" : "No stops on this map yet";
                stopFooter.text = "Task minutes count in time and oxygen.";
                if (jumpButton != null) jumpButton.SetInteractable(false);
                return;
            }

            StopInfo stop = stops[index];
            stopName.text = string.IsNullOrEmpty(stop.name) ? "Stop " + (index + 1) : stop.name;
            float meters = Vector2.Distance(new Vector2(stop.x, stop.y), site.Habitat);
            if (!site.StopsAreReal)
            {
                stopBadge.text = "TEST DATA · not a real place";
                stopBadge.color = WalkbackTheme.Watch;
            }
            else
            {
                stopBadge.text = meters.ToString("0") + " m from the habitat in a straight line";
                stopBadge.color = WalkbackTheme.TextSoft;
            }
            stopScience.text = string.IsNullOrEmpty(stop.science) ? "No science note for this stop in the data yet." : stop.science;
            stopTasks.text = TasksLine(stop.tasks);
            if (UserStops.IsUser(stop))
            {
                stopTasks.text = UserStops.WorkLine(stop);
                ShowMinuteButtons(true, stop);
            }

            var footerParts = new List<string>();
            if (!string.IsNullOrEmpty(stop.risk)) footerParts.Add("Risk: " + stop.risk);
            if (!string.IsNullOrEmpty(stop.photoCredit)) footerParts.Add("Photo: " + stop.photoCredit);
            if (string.IsNullOrEmpty(stop.panorama)) footerParts.Add("No panorama for this stop yet");
            stopFooter.text = footerParts.Count > 0 ? string.Join("  ·  ", footerParts.ToArray()) : "No risk note or photo credit in the data yet.";
            // A picked stop has no photo: its 360° PHOTO button stays off.
            if (jumpButton != null) jumpButton.SetInteractable(!UserStops.IsUser(stop));
        }

        /// <summary>The -5 / +5 buttons: shown only on a picked stop's card, each off at its end of the range.</summary>
        void ShowMinuteButtons(bool show, StopInfo stop)
        {
            if (lessMinutesButton == null || moreMinutesButton == null) return;
            lessMinutesButton.gameObject.SetActive(show);
            moreMinutesButton.gameObject.SetActive(show);
            // REMOVE takes the place of 360° PHOTO on a picked stop's card.
            if (removeStopButton != null) removeStopButton.gameObject.SetActive(show);
            if (jumpButton != null) jumpButton.gameObject.SetActive(!show);
            if (!show || stop == null) return;
            float m = UserStops.Minutes(stop);
            lessMinutesButton.SetInteractable(m > 0f);
            moreMinutesButton.SetInteractable(m < UserStops.MaxMinutes);
        }

        public static string TasksLine(StopTask[] tasks)
        {
            if (tasks == null || tasks.Length == 0) return "Tasks: none listed in the data yet.";
            var parts = new List<string>();
            float total = 0f;
            foreach (var t in tasks)
            {
                if (t == null) continue;
                parts.Add((string.IsNullOrEmpty(t.name) ? "Task" : t.name) + " " + Minutes(t.minutes));
                total += t.minutes;
            }
            return "Tasks: " + string.Join(" · ", parts.ToArray()) + "  =  " + Minutes(total) + " of work";
        }

        static string Minutes(float m)
        {
            // Always a decimal point: the UI is in English whatever the headset's language ("7.5 min", never "7,5 min").
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            return (Mathf.Approximately(m, Mathf.Round(m)) ? m.ToString("0", culture) : m.ToString("0.#", culture)) + " min";
        }

        // ---------------------------------------------------------------- conditions

        public void ShowConditions(Conditions.View view)
        {
            ConditionsHaveData = view.HasData;
            if (conditionsText == null) return;   // side board not built
            conditionsText.text = view.Text;
            conditionsText.color = view.HasData ? WalkbackTheme.Text : WalkbackTheme.TextSoft;
        }

        /// <summary>The space-weather line just read from NASA DONKI takes the place of the stored one. Without stored conditions the card stays as it is.</summary>
        public void ShowLiveSpaceWeather(string liveLine)
        {
            if (conditionsText == null || !ConditionsHaveData || string.IsNullOrEmpty(liveLine)) return;
            conditionsText.text = SpaceWeatherLive.Replace(conditionsText.text, liveLine);
        }

        // ---------------------------------------------------------------- build (edit time)

        /// <summary>Builds the main board once, at edit time, so it is saved in the scene.</summary>
        public void Build(Material backPlateMaterial, Material uiMaterial)
        {
            this.backPlateMaterial = backPlateMaterial;
            this.uiMaterial = uiMaterial;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);
            sideBoard = null;
            sourceBoard = null;

            var rect = MakeBoard(transform, "BackPlate", "Canvas", PixelsWide, PixelsHigh, WalkbackTheme.PanelWidthMeters, WalkbackTheme.PanelHeightMeters);

            title = AddText(rect, "Title", "WALKBACK", font, uiMaterial, 72, FontStyle.Bold, WalkbackTheme.Text, Margin, 28, 640, 90);
            tagline = AddText(rect, "Tagline", "The safest way to go far.", font, uiMaterial, 36, FontStyle.Normal, WalkbackTheme.TextSoft, Margin, 116, 640, 50);

            // Map layer buttons, top right, the photo first: the active one is amber. layerButtons[i] is MapLayer i.
            var order = new[] { MapLayer.Photo, MapLayer.Height, MapLayer.Slope, MapLayer.Return };
            const float buttonWidth = 165f, buttonGap = 12f;
            float bx = PixelsWide - Margin - 4f * buttonWidth - 3f * buttonGap;
            float layersLeft = bx;
            layerButtons = new PanelButton[order.Length];
            foreach (var layer in order)
            {
                int i = (int)layer;
                layerButtons[i] = PanelButton.Create(rect, "Layer" + LayerLabels[i], LayerLabels[i], font, uiMaterial, 32, bx, 52f, buttonWidth, ButtonHeight);
                bx += buttonWidth + buttonGap;
            }
            AddText(rect, "LayerCaption", "MAP LAYER", font, uiMaterial, 28, FontStyle.Bold, WalkbackTheme.TextSoft, layersLeft, 12, 400, 38);
            layerButtons[(int)MapLayer.Photo].SetSelected(true);

            // The verdict: its icon on the left (drawn by the charts), the word, one line of numbers, one line of what they are.
            const float inner = PixelsWide - 2f * Margin;
            AddBox(rect, "VerdictCard", uiMaterial, WalkbackTheme.CardBack, Margin, VerdictTop, inner, 220);
            verdictAccent = AddBox(rect, "VerdictAccent", uiMaterial, WalkbackTheme.Watch, Margin, VerdictTop, 16, 220);
            verdictTitle = AddText(rect, "VerdictTitle", "NO ROUTE YET", font, uiMaterial, 96, FontStyle.Bold, WalkbackTheme.Watch, 262, VerdictTop + 10, 860, 116);
            missionButton = PanelButton.Create(rect, "StartMission", "START MISSION", font, uiMaterial, 30, PixelsWide - Margin - 24 - 270, VerdictTop + 22, 270, ButtonHeight);
            missionButton.SetInteractable(false);
            // What the button does, right under it: the terrain at full size, walked with the headset (not a photo).
            missionCaption = AddText(rect, "StartMissionCaption", MissionCaptionText, font, uiMaterial, 28, FontStyle.Normal, WalkbackTheme.TextSoft, PixelsWide - Margin - 24 - 270, VerdictTop + 124, 270, 34);
            missionCaption.alignment = TextAnchor.MiddleCenter;
            verdictDetail = AddText(rect, "VerdictDetail", "Time  ·  oxygen  ·  margin to return", font, uiMaterial, 34, FontStyle.Normal, WalkbackTheme.Text, 264, VerdictTop + 124, 1160, 44);
            verdictNote = AddText(rect, "VerdictNote", DefaultDetail(VerdictState.NoRoute), font, uiMaterial, MissionCharts.TextSize, FontStyle.Normal, WalkbackTheme.TextSoft, 264, VerdictTop + 170, 1160, 38);

            // The charts: the verdict's icon, the oxygen tank, the mission's time, the height along the route.
            charts = gameObject.GetComponent<MissionCharts>();
            if (charts == null) charts = gameObject.AddComponent<MissionCharts>();
            charts.Build(rect, font, uiMaterial);

            // The site's source, the layer legend and the footer are on the source board (BuildSideBoard).
            ShowNoRoute();
        }

        /// <summary>
        /// The side board to the right of the main board, turned to face the user: the stop card
        /// on top, the current conditions below. Built at edit time, after Build.
        /// </summary>
        public void BuildSideBoard()
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (sideBoard != null) DestroyImmediate(sideBoard.gameObject);
            var existing = transform.Find("SideBoard");
            if (existing != null) DestroyImmediate(existing.gameObject);

            var board = new GameObject("SideBoard");
            board.transform.SetParent(transform, false);
            board.transform.localPosition = SideBoardOffset;
            Vector3 toBoard = transform.position + SideBoardOffset - UserEye;
            toBoard.y = 0f;
            board.transform.localRotation = Quaternion.LookRotation(toBoard.normalized, Vector3.up);
            sideBoard = board.transform;

            float wM = SidePixelsWide * MetersPerPixel, hM = SidePixelsHigh * MetersPerPixel;
            var rect = MakeBoard(board.transform, "SideBackPlate", "SideCanvas", SidePixelsWide, SidePixelsHigh, wM, hM);

            // Stop chooser: one button per stop, filled in by Bind once the site is loaded.
            stopsLabel = AddText(rect, "StopsLabel", "STOPS", font, uiMaterial, 30, FontStyle.Bold, WalkbackTheme.TextSoft, 30, 24, 110, ButtonHeight);
            var row = new GameObject("StopButtons", typeof(RectTransform));
            stopButtonRow = (RectTransform)row.transform;
            Place(rect, stopButtonRow, 140, 24, 600, 2f * ButtonHeight + 12f);
            // v1.1: under the STOPS label, takes away the stops picked on the map.
            clearStopsButton = PanelButton.Create(rect, "ClearMyStops", ClearStopsText, font, uiMaterial, 28, 24, 24 + ButtonHeight + 12f, 114, ButtonHeight);
            clearStopsButton.SetInteractable(false);

            // Stop card. Its last text (risk, photo credit) takes four lines for a stop with a photo credit
            // (Nevado Sajama): at three lines' height it ran 23 mm over the card's edge, down to the conditions.
            AddBox(rect, "StopCard", uiMaterial, WalkbackTheme.CardBack, 30, 254, 700, 550);
            stopName = AddText(rect, "StopName", "NO STOP SELECTED", font, uiMaterial, 46, FontStyle.Bold, WalkbackTheme.Text, 54, 262, 400, 66);
            // Real stop names vary in length: shrink to fit one line, never under 32 px.
            stopName.resizeTextForBestFit = true;
            stopName.resizeTextMinSize = 32;
            stopName.resizeTextMaxSize = 46;
            jumpButton = PanelButton.Create(rect, "JumpToSite", PhotoButtonText, font, uiMaterial, 30, 464, 266, 250, ButtonHeight);
            stopBadge = AddText(rect, "StopBadge", "Press a pillar or a stop button.", font, uiMaterial, 28, FontStyle.Bold, WalkbackTheme.Live, 54, 374, 660, 40);
            stopScience = AddText(rect, "StopScience", "Science, tasks and minutes of each stop.", font, uiMaterial, 30, FontStyle.Normal, WalkbackTheme.Text, 54, 424, 660, 110);
            stopScience.alignment = TextAnchor.UpperLeft;
            stopTasks = AddText(rect, "StopTasks", "No stops on this map yet.", font, uiMaterial, 30, FontStyle.Normal, WalkbackTheme.Text, 54, 544, 660, 110);
            stopTasks.alignment = TextAnchor.UpperLeft;
            // v1.1: a picked stop's minutes of work, right of its short work line (hidden for the data's stops).
            lessMinutesButton = PanelButton.Create(rect, "LessMinutes", "-5 MIN", font, uiMaterial, 28, 464, 544, 120, ButtonHeight);
            moreMinutesButton = PanelButton.Create(rect, "MoreMinutes", "+5 MIN", font, uiMaterial, 28, 594, 544, 120, ButtonHeight);
            lessMinutesButton.gameObject.SetActive(false);
            moreMinutesButton.gameObject.SetActive(false);
            removeStopButton = PanelButton.Create(rect, "RemoveMyStop", RemoveStopText, font, uiMaterial, 30, 464, 266, 250, ButtonHeight);
            removeStopButton.gameObject.SetActive(false);
            stopFooter = AddText(rect, "StopFooter", "Task minutes count in time and oxygen.", font, uiMaterial, 28, FontStyle.Normal, WalkbackTheme.TextSoft, 54, 664, 660, 134);
            stopFooter.alignment = TextAnchor.UpperLeft;
            jumpButton.SetInteractable(false);

            // Current conditions (ficha C)
            conditionsTitle = AddText(rect, "ConditionsTitle", "CURRENT CONDITIONS", font, uiMaterial, 30, FontStyle.Bold, WalkbackTheme.TextSoft, 30, 834, 700, 40);
            conditionsText = AddText(rect, "ConditionsText", Conditions.WaitingText, font, uiMaterial, 28, FontStyle.Normal, WalkbackTheme.TextSoft, 30, 880, 700, 356);
            conditionsText.alignment = TextAnchor.UpperLeft;

            BuildSourceBoard();
        }

        /// <summary>
        /// The source board to the left of the main board, over the controls card: what a chart cannot say.
        /// The site and its source, the map layer's legend, and what the map does not show.
        /// </summary>
        void BuildSourceBoard()
        {
            if (sourceBoard != null) DestroyImmediate(sourceBoard.gameObject);
            var existing = transform.Find("SourceBoard");
            if (existing != null) DestroyImmediate(existing.gameObject);

            var board = new GameObject("SourceBoard");
            board.transform.SetParent(transform, false);
            board.transform.localPosition = SourceBoardOffset;
            Vector3 toBoard = transform.position + SourceBoardOffset - UserEye;
            toBoard.y = 0f;
            board.transform.localRotation = Quaternion.LookRotation(toBoard.normalized, Vector3.up);
            sourceBoard = board.transform;

            var rect = MakeBoard(board.transform, "SourceBackPlate", "SourceCanvas", SourcePixelsWide, SourcePixelsHigh,
                SourcePixelsWide * MetersPerPixel, SourcePixelsHigh * MetersPerPixel);
            AddText(rect, "SourceTitle", "MAP AND SOURCES", font, uiMaterial, 30, FontStyle.Bold, WalkbackTheme.TextSoft, 30, 20, 700, 40);
            dataLine = AddText(rect, "DataLine", "TEST TERRAIN · synthetic shape for development · not Mars data", font, uiMaterial, 30, FontStyle.Bold, WalkbackTheme.Watch, 30, 70, 700, 110);
            dataLine.alignment = TextAnchor.UpperLeft;
            legendLine = AddText(rect, "LegendLine", "Map layer legend", font, uiMaterial, 30, FontStyle.Normal, WalkbackTheme.Text, 30, 190, 700, 215);
            legendLine.alignment = TextAnchor.UpperLeft;
            footer = AddText(rect, "Footer", "Meta Quest 3 stands in for the helmet display  ·  Rocks under about 4 m are not on this map", font, uiMaterial, 30, FontStyle.Normal, WalkbackTheme.TextSoft, 30, 415, 700, 120);
            footer.alignment = TextAnchor.UpperLeft;
        }

        /// <summary>An opaque back plate and a world-space canvas on it, 1 pixel = 1 mm.</summary>
        RectTransform MakeBoard(Transform parent, string plateName, string canvasName, int pixelsWide, int pixelsHigh, float widthMeters, float heightMeters)
        {
            var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
            back.name = plateName;
            DestroyImmediate(back.GetComponent<Collider>());
            back.transform.SetParent(parent, false);
            back.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            back.transform.localScale = new Vector3(widthMeters, heightMeters, 1f);
            back.GetComponent<MeshRenderer>().sharedMaterial = backPlateMaterial;

            var canvasObject = new GameObject(canvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(parent, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObject.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            var rect = (RectTransform)canvasObject.transform;
            rect.sizeDelta = new Vector2(pixelsWide, pixelsHigh);
            rect.localScale = Vector3.one * MetersPerPixel;
            return rect;
        }

        public static Text AddText(RectTransform parent, string name, string content, Font font, Material material, int size, FontStyle style, Color color, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            Place(parent, (RectTransform)go.transform, x, y, w, h);
            var text = go.GetComponent<Text>();
            text.font = font;
            text.text = content;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            if (material != null) text.material = material;
            return text;
        }

        public static Image AddBox(RectTransform parent, string name, Material material, Color color, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            Place(parent, (RectTransform)go.transform, x, y, w, h);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (material != null) image.material = material;
            return image;
        }

        /// <summary>x, y from the top-left corner of the board, in pixels (millimeters).</summary>
        public static void Place(RectTransform parent, RectTransform rect, float x, float y, float w, float h)
        {
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }

        static void DestroySafe(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
