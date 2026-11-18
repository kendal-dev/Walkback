// Written with Claude Code (AI), directed and tested by team MARSXR
using System.Globalization;
using UnityEngine;

namespace Walkback
{
    /// <summary>
    /// The helmet HUD (guion §16.4, ficha G; §22.2): what the astronaut's visor shows while standing at
    /// a stop (inside its 360° photo). Few things and big: oxygen left (top left), minutes back to the
    /// habitat (top right), the next stop (bottom left), the 2D map (bottom right), and a red TURN BACK
    /// NOW in the middle only when the way back is no longer covered. Pinned to the view with a slight
    /// lag (§21.6), 1.2 m ahead, everything within about 25° of the center. Numbers come from
    /// HudEstimate and say so until session C's model lands.
    /// </summary>
    public class HelmetHud : MonoBehaviour
    {
        public const float Distance = 1.2f;
        public const float FollowSpeed = 6f;
        /// <summary>
        /// The one line that says what every number on the visor is. Short on purpose (36 characters):
        /// the visor is read at a glance, and the long version is on the verdict card.
        /// </summary>
        public const string EstimateLine = "ESTIMATE · not the walking model yet";
        /// <summary>The estimate line's place inside a 360° photo: above the cards (the photo's own card is below).</summary>
        public static readonly Vector3 EstimateAbove = new Vector3(0f, 0.345f, 0f);
        /// <summary>
        /// Its place in mission mode: below the cards. The next stop's name stands 3.35 m up on its pillar,
        /// so while walking it is always above the horizon; a line above the cards crossed it between
        /// 7 and 5.5 m from the stop (and for most of the walk when looking down at the path).
        /// </summary>
        public static readonly Vector3 EstimateBelow = new Vector3(0f, -0.33f, 0f);

        public WalkbackApp app;
        /// <summary>Everything that shows; inactive while the HUD is off.</summary>
        public Transform content;
        public GlobeLabel oxygenCard;
        public GlobeLabel returnCard;
        public GlobeLabel nextCard;
        public GlobeLabel estimateTag;
        public GlobeLabel warningCard;
        public MiniMap miniMap;

        [Header("Mission mode: north is known")]
        /// <summary>The compass tape between the two top cards (hidden where north is not known).</summary>
        public HudCompass compass;
        /// <summary>The heading over the tape, "047° NE".</summary>
        public GlobeLabel headingCard;
        /// <summary>The plate with the arrow to the next stop, next to the NEXT card.</summary>
        public Transform arrowPlate;
        /// <summary>Turns in the visor's plane: up = the stop is straight ahead, right = it is to the right.</summary>
        public Transform arrow;

        [Header("Oxygen bar")]
        /// <summary>The bar under the oxygen number: as long as the oxygen left, in the number's colour.</summary>
        public Transform oxygenFill;
        public Renderer oxygenFillRenderer;
        public const float OxygenBarMeters = 0.272f;
        public const float OxygenBarHeightMeters = 0.014f;
        /// <summary>Closer than this to the stop the arrow hides: the direction to a point underfoot means nothing.</summary>
        public const float ArrowHideMeters = 1f;

        public bool HeadingKnown { get; private set; }
        /// <summary>Oxygen left as shown on the bar, 0..1.</summary>
        public float OxygenBarFraction { get; private set; }

        MaterialPropertyBlock barBlock;
        int headingShown = -1;

        public static Color OxygenColor(float percentLeft)
        {
            return percentLeft > 50f ? WalkbackTheme.Safe : (percentLeft > 25f ? WalkbackTheme.Watch : WalkbackTheme.Danger);
        }

        public static Color ReturnColor(HudEstimate.ReturnState state)
        {
            return state == HudEstimate.ReturnState.Covered ? WalkbackTheme.Safe : (state == HudEstimate.ReturnState.Tight ? WalkbackTheme.Watch : WalkbackTheme.Danger);
        }

        /// <summary>The third line of the return card: what is left after the way back, or by how much it falls short.</summary>
        /// <summary>
        /// v1.1: the minutes-back card's last line while walking. Ahead of the plan's point of no return it says how
        /// far that is ("turn back in 120 m"); past it TURN BACK NOW is on, and the line says the oxygen margin as before.
        /// </summary>
        public static string ReturnLastLine(MissionProgress.Snapshot s)
        {
            if (!s.turnBack && !s.noWayBack && s.turnBackInMeters >= 0f) return "turn back in " + MetersText(s.turnBackInMeters);
            return MarginText(s.returnState, s.marginMinutes, s.noWayBack);
        }

        public static string MarginText(HudEstimate.ReturnState state, float marginMinutes, bool noWayBack)
        {
            if (noWayBack) return "no way back on foot";
            if (state == HudEstimate.ReturnState.NotCovered) return "short by " + MinutesText(-marginMinutes);
            return "spare O2 " + MinutesText(marginMinutes);
        }

        void SetOxygen(float percentLeft)
        {
            Color color = OxygenColor(percentLeft);
            oxygenCard.SetLines("O2  " + F(percentLeft, "0") + " %", "oxygen left (estimate)");
            oxygenCard.lines[0].color = color;
            OxygenBarFraction = Mathf.Clamp01(percentLeft / 100f);
            if (oxygenFill == null) return;
            // The bar grows from its left end.
            float length = Mathf.Max(OxygenBarFraction * OxygenBarMeters, 0.0005f);
            Vector3 p = oxygenFill.localPosition;
            oxygenFill.localScale = new Vector3(length, OxygenBarHeightMeters, 1f);
            oxygenFill.localPosition = new Vector3(-OxygenBarMeters / 2f + length / 2f, p.y, p.z);
            if (oxygenFillRenderer == null) return;
            if (barBlock == null) barBlock = new MaterialPropertyBlock();
            oxygenFillRenderer.GetPropertyBlock(barBlock);
            barBlock.SetColor("_BaseColor", color);
            oxygenFillRenderer.SetPropertyBlock(barBlock);
        }

        /// <summary>
        /// Every frame in mission mode: which way the astronaut looks (degrees clockwise from north) and
        /// the bearing of the next stop. The tape slides, the arrow turns, the heading number changes
        /// when its whole degree changes. Not known (inside a 360° photo): the three hide.
        /// </summary>
        public void SetHeading(bool known, float headingDegrees, bool hasTarget, float bearingDegrees)
        {
            HeadingKnown = known;
            if (compass != null && compass.gameObject.activeSelf != known) compass.gameObject.SetActive(known);
            if (headingCard != null && headingCard.gameObject.activeSelf != known) headingCard.gameObject.SetActive(known);
            bool showArrow = known && hasTarget;
            if (arrowPlate != null && arrowPlate.gameObject.activeSelf != showArrow) arrowPlate.gameObject.SetActive(showArrow);
            if (!known)
            {
                headingShown = -1;
                return;
            }
            if (compass != null) compass.Show(headingDegrees, hasTarget, bearingDegrees);
            int whole = Mathf.RoundToInt(Mathf.Repeat(headingDegrees, 360f)) % 360;
            if (headingCard != null && whole != headingShown)
            {
                headingShown = whole;
                headingCard.SetLines(HudCompass.BearingText(whole));
            }
            // Seen from the viewer the plane has x to the right and y up: clockwise is a negative turn about z.
            if (arrow != null && showArrow) arrow.localRotation = Quaternion.Euler(0f, 0f, -HudCompass.Offset(bearingDegrees, headingDegrees));
        }

        public bool Visible { get { return content != null && content.gameObject.activeSelf; } }
        public int AtStop { get; private set; } = -1;
        public HudEstimate.State Current { get; private set; }

        /// <summary>Shows the visor as on arrival at stop <paramref name="index"/>.</summary>
        public void ShowAt(int index)
        {
            SiteData site = app != null && app.terrain != null ? app.terrain.Site : null;
            if (site == null || site.Stops.Length == 0) { Hide(); return; }
            // The way back is read from the same return layer the map on the table shows.
            // Time, oxygen and the next leg are the plan's own (the board's numbers, and its WHAT IF tank).
            var s = HudEstimate.AtStop(site, index, app.terrain.ReturnMinutes, app.Plan);
            Current = s;
            AtStop = s.stop;

            SetOxygen(s.oxygenLeftPercent);
            returnCard.SetLines(MinutesText(s.returnMinutes), "back to habitat (estimate)", MarginText(s.returnState, s.marginMinutes, false));
            returnCard.lines[0].color = ReturnColor(s.returnState);
            nextCard.SetLines("NEXT  " + s.nextName,
                MetersText(s.nextMeters) + "  ·  " + MinutesText(s.nextMinutes) + "  ·  O2 -" + F(s.nextOxygenPercent, "0.0") + " %",
                HudCompass.ClimbText(s.nextClimbMeters) + (s.taskMinutes > 0f ? "  ·  tasks here " + F(s.taskMinutes, "0") + " min" : ""));
            // Inside a 360° photo the way the headset looks is not a direction on Mars: no compass, no arrow.
            SetHeading(false, 0f, false, 0f);
            estimateTag.SetLines(site.IsMarsData ? EstimateLine : EstimateLine + "  ·  TEST TERRAIN, not Mars data");
            estimateTag.transform.localPosition = EstimateAbove;
            warningCard.SetLines("TURN BACK NOW");
            SetWarning(s.turnBack);

            content.gameObject.SetActive(true);
            miniMap.Show(app, s.here, s.nextPoint);
            // Inside a 360° photo the way the headset looks is not a direction on Mars: no heading arrow.
            miniMap.SetHeading(false, 0f);
            if (app.wristMap != null) app.wristMap.SetHeading(false, 0f);
            WalkbackLog.Info("HUD at " + s.stopName + ": O2 " + s.oxygenLeftPercent.ToString("F1") + " %, back " + s.returnMinutes.ToString("F1") + " min, next " + s.nextName + " " + s.nextMeters.ToString("F0") + " m");
        }

        /// <summary>
        /// Mission mode: the visor while walking, from the headset's position (MissionProgress). Same cards
        /// as ShowAt; the numbers come from the plan and the map's return layer, so they stay labelled ESTIMATE.
        /// </summary>
        public void ShowLive(MissionProgress.Snapshot s, SiteData site)
        {
            AtStop = -1;
            SetOxygen(s.oxygenLeftPercent);
            // "CUT OFF" fits the card on one line at this size; the line under it says what it means.
            returnCard.SetLines(s.noWayBack ? "CUT OFF" : MinutesText(s.returnMinutes), "back to habitat (estimate)", ReturnLastLine(s));
            returnCard.lines[0].color = ReturnColor(s.returnState);
            string work = s.taskMinutesAtNext > 0f ? "work there " + F(s.taskMinutesAtNext, "0") + " min" : "";
            if (s.arrived)
                nextCard.SetLines("NEXT  " + s.nextName, "You are here", work);
            else
                nextCard.SetLines("NEXT  " + s.nextName,
                    MetersText(s.nextMeters) + "  ·  " + MinutesText(s.nextMinutes) + "  ·  " + HudCompass.BearingText(s.nextBearingDegrees),
                    HudCompass.ClimbText(s.nextClimbMeters) + (work.Length > 0 ? "  ·  " + work : ""));
            estimateTag.SetLines(site != null && !site.IsMarsData ? EstimateLine + "  ·  TEST TERRAIN, not Mars data" : EstimateLine);
            estimateTag.transform.localPosition = EstimateBelow;
            warningCard.SetLines("TURN BACK NOW");
            SetWarning(s.turnBack);
            content.gameObject.SetActive(true);
            miniMap.Show(app, s.here, s.nextPoint);
        }

        public void Hide()
        {
            if (content != null) content.gameObject.SetActive(false);
            AtStop = -1;
            // Hidden is not "safe": the next time the visor shows with the warning on, it sounds again.
            warningOn = false;
        }

        /// <summary>How many times TURN BACK NOW has come on (each one sounds the alert once).</summary>
        public int TurnBackAlerts { get; private set; }

        public const int AlertRate = 44100;
        public const float AlertSeconds = 0.62f;
        public const float AlertVolume = 0.5f;

        bool warningOn;
        AudioSource alertAudio;

        /// <summary>
        /// Shows or hides TURN BACK NOW. The warning used to be only a red card, easy to miss while looking
        /// at the ground: the moment it comes on (not while it stays on) a two-beep alert sounds.
        /// </summary>
        void SetWarning(bool on)
        {
            warningCard.gameObject.SetActive(on);
            if (on && !warningOn)
            {
                TurnBackAlerts++;
                // Sound only while running: the editor test builds no clips and plays nothing.
                if (Application.isPlaying)
                {
                    if (alertAudio == null)
                    {
                        alertAudio = gameObject.AddComponent<AudioSource>();
                        alertAudio.playOnAwake = false;
                        alertAudio.spatialBlend = 0f;
                        alertAudio.volume = AlertVolume;
                        float[] tone = AlertTone(AlertRate);
                        alertAudio.clip = AudioClip.Create("TurnBackAlert", tone.Length, 1, AlertRate, false);
                        alertAudio.clip.SetData(tone, 0);
                    }
                    alertAudio.Play();
                }
                WalkbackLog.Info("HUD: TURN BACK NOW came on");
            }
            warningOn = on;
        }

        /// <summary>
        /// The alert, made in code like the pointer's click and the globe's chime (no audio file to credit):
        /// two 0.22 s beeps at 880 Hz with 0.12 s of silence between them, each with a soft start and end.
        /// </summary>
        public static float[] AlertTone(int rate)
        {
            int n = Mathf.RoundToInt(rate * AlertSeconds);
            var samples = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                // Beeps at 0.00–0.22 s and 0.34–0.56 s.
                float inBeep = t < 0.22f ? t : (t >= 0.34f && t < 0.56f ? t - 0.34f : -1f);
                if (inBeep < 0f) continue;
                float envelope = Mathf.Clamp01(inBeep / 0.01f) * Mathf.Clamp01((0.22f - inBeep) / 0.03f);
                samples[i] = 0.6f * envelope * Mathf.Sin(2f * Mathf.PI * 880f * t);
            }
            return samples;
        }

        /// <summary>Jumps to its place in front of <paramref name="head"/> (on entering a stop).</summary>
        public void SnapTo(Transform head)
        {
            if (head == null) return;
            transform.SetPositionAndRotation(head.position + head.forward * Distance, head.rotation);
        }

        /// <summary>Follows the head with a slight lag, so the visor feels steady.</summary>
        public void Follow(Transform head, float deltaTime)
        {
            if (head == null) return;
            float k = 1f - Mathf.Exp(-FollowSpeed * deltaTime);
            transform.position = Vector3.Lerp(transform.position, head.position + head.forward * Distance, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, head.rotation, k);
        }

        public static string MinutesText(float minutes)
        {
            if (minutes < 1f) return "under 1 min";
            // Rounded to whole minutes first: 119.6 is "2 h 0 min", never "1 h 60 min"; 59.7 is "1 h 0 min", not "60 min".
            int whole = Mathf.FloorToInt(minutes + 0.5f);
            if (whole < 60) return whole.ToString(CultureInfo.InvariantCulture) + " min";
            return (whole / 60) + " h " + (whole % 60) + " min";
        }

        public static string MetersText(float meters)
        {
            return meters >= 1000f ? F(meters / 1000f, "0.0") + " km" : F(meters, "0") + " m";
        }

        /// <summary>Numbers on the visor always with a decimal point (the UI is in English, whatever the device language).</summary>
        static string F(float value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
