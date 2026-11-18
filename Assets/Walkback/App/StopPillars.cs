// Written with Claude Code (AI), directed and tested by team MARSXR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Walkback
{
    /// <summary>
    /// Pillars of light on the map: the habitat (green) and every stop of the mission (amber), from
    /// site.json and stops.json. They grow when the map appears ("brotan"), carry a name label that
    /// reads at 1.5 m, and a stop pillar selects its stop when pressed (app.SelectStop). Lives under
    /// MapRoot with an identity transform, in map meters.
    /// </summary>
    public class StopPillars : MonoBehaviour
    {
        public Material lightMaterial;
        public Material labelMaterial;

        // Map meters (1 m of map = 1 mm on the table).
        /// <summary>
        /// 85 mm on the table (it was 110). From the user's start place the labels on top stand in front of the
        /// foot of the main board, and one that goes up three steps to get clear of the others (Altadena's, behind
        /// the habitat's) reached the stops' names under the height chart for eyes at 1.55 m or lower.
        /// </summary>
        public const float PillarHeightMeters = 85f;
        public const float StopRadiusMeters = 5f;
        public const float HabitatRadiusMeters = 8f;
        /// <summary>Stops closer than this (map meters) get their labels stacked one label height apart.</summary>
        public const float LabelSpacingMeters = 60f;
        public const float HitRadiusMeters = 18f;
        public const int LabelFontSize = 30;
        /// <summary>The line under a stop name ("Curiosity drilled here · Nov 2025").</summary>
        public const int TagFontSize = 20;
        public const float GrowSeconds = 0.7f;
        public const float GrowStagger = 0.18f;

        public static readonly Color StopColor = WalkbackTheme.Watch;
        public static readonly Color HabitatColor = WalkbackTheme.Safe;
        public static readonly Color UserStopColor = WalkbackTheme.Live;
        /// <summary>v1.1: the point of no return of a plan that does not fit its tank.</summary>
        public static readonly Color TurnBackColor = WalkbackTheme.Danger;
        public const string TurnBackText = "TURN BACK BY HERE";
        /// <summary>The red pillar at the point of no return, or null when the plan fits.</summary>
        public PillarView TurnBack { get; private set; }
        /// <summary>
        /// When the point of no return is at a pillar (a stop whose work no longer fits), the red pillar has no
        /// label of its own: that pillar's tag line says TURN BACK HERE in red instead (a second label there
        /// pushed the labels up over the main board's texts). This is that tag line, or null.
        /// </summary>
        public Text TurnBackTagOnPillar { get; private set; }
        string hostTagWas;
        Color hostTagColorWas;
        public const string TurnBackHereText = "TURN BACK HERE";

        readonly List<PillarView> stops = new List<PillarView>();
        public PillarView Habitat { get; private set; }
        public IList<PillarView> Stops { get { return stops; } }

        WalkbackApp app;
        float growStarted = -1f;

        /// <summary>
        /// (Re)builds the pillars from app.terrain.Site. With <paramref name="grow"/> (Play only),
        /// they grow one after the other; otherwise they appear whole.
        /// </summary>
        public void Build(WalkbackApp owner, bool grow)
        {
            Unsubscribe();
            app = owner;
            for (int i = transform.childCount - 1; i >= 0; i--) DestroySafe(transform.GetChild(i).gameObject);
            stops.Clear();
            Habitat = null;
            TurnBack = null;
            TurnBackTagOnPillar = null;
            if (app == null || app.terrain == null || app.terrain.Site == null) return;

            SiteData site = app.terrain.Site;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Habitat = MakePillar(site, site.Habitat, "HABITAT", -1, HabitatColor, HabitatRadiusMeters, font);
            for (int i = 0; i < site.Stops.Length; i++)
            {
                StopInfo stop = site.Stops[i];
                string name = string.IsNullOrEmpty(stop.name) ? "Stop " + (i + 1) : stop.name;
                // A stop the astronaut picked on the map is blue, the data's stops are amber.
                var pillar = MakePillar(site, new Vector2(stop.x, stop.y), name, i, UserStops.IsUser(stop) ? UserStopColor : StopColor, StopRadiusMeters, font);
                // Stops a few meters apart (Valle de la Luna and Nevado Sajama are 10 m apart): stack the labels.
                int near = 0;
                for (int j = 0; j < i; j++)
                    if (Vector2.Distance(new Vector2(site.Stops[j].x, site.Stops[j].y), new Vector2(stop.x, stop.y)) < LabelSpacingMeters) near++;
                pillar.labelStack = near;
                pillar.Refresh();
                stops.Add(pillar);
            }
            // The habitat label rides one step above the highest stack, so stacked stop labels never hide it.
            int highest = -1;
            foreach (var p in stops) highest = Mathf.Max(highest, p.labelStack);
            if (Habitat != null && highest > 0)
            {
                Habitat.labelStack = highest + 1;
                Habitat.Refresh();
            }

            app.StopSelected += OnStopSelected;
            OnStopSelected(app.SelectedStop);

            bool animate = grow && Application.isPlaying;
            growStarted = animate ? Time.time : -1f;
            SetGrowth(animate ? 0f : 1f);
            WalkbackLog.Info("Pillars built: habitat + " + stops.Count + " stops" + (site.StopsAreReal ? "" : " (TEST STOPs)"));
        }

        /// <summary>Every pillar at the same growth, 0..1 (the test and the end of the animation).</summary>
        public void SetGrowth(float value)
        {
            if (Habitat != null) Habitat.SetGrowth(value);
            foreach (var p in stops) p.SetGrowth(value);
            if (TurnBack != null) TurnBack.SetGrowth(value);
        }

        /// <summary>
        /// Puts the red TURN BACK BY HERE pillar at a point of the map (local meters), with a line under the
        /// name, or takes it away (null). Not pressable. Rebuilt with every plan.
        /// </summary>
        public void ShowTurnBack(Vector2? meters, string tag, string tankText)
        {
            if (TurnBack != null) { DestroySafe(TurnBack.gameObject); TurnBack = null; }
            if (TurnBackTagOnPillar != null)
            {
                TurnBackTagOnPillar.text = hostTagWas;
                TurnBackTagOnPillar.color = hostTagColorWas;
                TurnBackTagOnPillar = null;
            }
            if (meters == null || app == null || app.terrain == null || app.terrain.Site == null) return;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // A little wider than a stop's beam: when the point of no return is a stop (its work no longer fits),
            // the red shows around the amber.
            TurnBack = MakePillar(app.terrain.Site, meters.Value, TurnBackText, -2, TurnBackColor, StopRadiusMeters * 1.6f, font, tag);
            // Its label goes one step above any label of a pillar near it, so the two never cover each other.
            int highest = -1;
            PillarView host = null;
            float hostMeters = float.MaxValue;
            var near = new List<PillarView>(stops);
            if (Habitat != null) near.Add(Habitat);
            foreach (var p in near)
            {
                float d = Vector3.Distance(p.transform.localPosition, TurnBack.transform.localPosition);
                if (d >= LabelSpacingMeters) continue;
                highest = Mathf.Max(highest, p.labelStack);
                Transform tagLine = p.labelRoot != null ? p.labelRoot.Find("Tag") : null;
                if (tagLine != null && d < hostMeters) { host = p; hostMeters = d; }
            }
            if (host != null)
            {
                // At a pillar with a tag line: say it there, in red, and give the red pillar no label.
                DestroySafe(TurnBack.labelRoot.gameObject);
                TurnBack.labelRoot = null;
                TurnBack.label = null;
                TurnBack.labelBox = null;
                TurnBackTagOnPillar = host.labelRoot.Find("Tag").GetComponent<Text>();
                hostTagWas = TurnBackTagOnPillar.text;
                hostTagColorWas = TurnBackTagOnPillar.color;
                TurnBackTagOnPillar.text = TurnBackHereText + " · " + tankText;
                TurnBackTagOnPillar.color = TurnBackColor;
            }
            else TurnBack.labelStack = highest + 1;
            TurnBack.SetGrowth(1f);
        }

        /// <summary>A label goes up this many steps at most to get clear of the labels in front of it.</summary>
        public const int MaxViewStack = 6;
        /// <summary>A lifted label comes back down only when it would be clear by this fraction of its size (no flicker at the edge).</summary>
        public const float ViewStackMargin = 0.25f;
        /// <summary>Looking almost straight down, lifting a label does not move it on screen: no lifting under this.</summary>
        public const float MinRiseAcrossView = 0.35f;

        readonly List<PillarView> all = new List<PillarView>();
        int[] viewStacks = new int[0];

        /// <summary>
        /// Labels turn to this camera, and a label that another pillar's label covers from where the camera
        /// is goes up until both read (from the front of the table the habitat's label covered Altadena's,
        /// 200 m behind it). Called every frame in Play; the test calls it before a screenshot.
        /// </summary>
        public void FaceCamera(Camera cam)
        {
            all.Clear();
            if (Habitat != null) all.Add(Habitat);
            all.AddRange(stops);
            if (TurnBack != null) all.Add(TurnBack);
            int count = all.Count;
            if (viewStacks.Length != count) viewStacks = new int[count];
            foreach (var p in all)
            {
                p.viewLift = 0f;
                p.FaceCamera(cam);
            }
            if (cam == null || count < 2) return;

            // Every label as the camera sees it, at its own place; then who has to go up.
            var bottom = new Vector3[count];
            var size = new Vector2[count];
            var rise = new Vector3[count];
            var perStep = new float[count];
            Transform eye = cam.transform;
            for (int i = 0; i < count; i++)
            {
                RectTransform root = all[i].labelRoot;
                if (root == null || !root.gameObject.activeInHierarchy) { bottom[i] = new Vector3(0f, 0f, -1f); continue; }
                bottom[i] = eye.InverseTransformPoint(root.position);
                size[i] = new Vector2(root.sizeDelta.x * Mathf.Abs(root.lossyScale.x), root.sizeDelta.y * Mathf.Abs(root.lossyScale.y));
                // One step up the pillar, as the camera sees it. Looking down at the table a step moves the
                // label less on screen, so the step is longer by as much.
                Vector3 up = eye.InverseTransformDirection(all[i].transform.up);
                Vector3 toLabel = bottom[i].normalized;
                float across = (up - Vector3.Dot(up, toLabel) * toLabel).magnitude;
                if (across < MinRiseAcrossView) { rise[i] = Vector3.zero; continue; }
                perStep[i] = 1f / Mathf.Max(0.6f, across);
                rise[i] = up * (size[i].y * 1.1f * perStep[i]);
            }
            viewStacks = ViewStacks(bottom, size, rise, viewStacks);
            for (int i = 0; i < count; i++)
            {
                if (viewStacks[i] == 0) continue;
                all[i].viewLift = viewStacks[i] * perStep[i];
                all[i].FaceCamera(cam);
            }
        }

        /// <summary>
        /// How many steps each label goes up so that none covers another, seen from the camera.
        /// <paramref name="bottom"/> is the label's bottom center in camera space (z forward; z of 0 or
        /// less = not seen), <paramref name="size"/> its width and height in meters, <paramref name="rise"/>
        /// one step up in camera space (zero = this label cannot be lifted). The nearest label keeps its
        /// place; each one behind goes up until it is clear of those already placed. A label that was
        /// lifted the frame before (<paramref name="previous"/>) only comes down once it is clear by a margin.
        /// </summary>
        public static int[] ViewStacks(Vector3[] bottom, Vector2[] size, Vector3[] rise, int[] previous)
        {
            int count = bottom.Length;
            var stacks = new int[count];
            var order = new List<int>();
            for (int i = 0; i < count; i++)
                if (bottom[i].z > 0.01f) order.Add(i);
            order.Sort((a, b) => bottom[a].z != bottom[b].z ? bottom[a].z.CompareTo(bottom[b].z) : a.CompareTo(b));

            var placed = new List<Rect>();
            foreach (int i in order)
            {
                bool canRise = rise[i].sqrMagnitude > 1e-12f;
                int was = previous != null && i < previous.Length ? previous[i] : 0;
                int level = 0;
                Rect box = ViewBox(bottom[i], size[i], 0f);
                for (; ; level++)
                {
                    Vector3 at = bottom[i] + rise[i] * level;
                    if (at.z <= 0.01f) break;
                    box = ViewBox(at, size[i], 0f);
                    // Below where it was a moment ago it needs room to spare, or it would flicker up and down.
                    Rect test = level < was ? ViewBox(at, size[i], ViewStackMargin) : box;
                    bool covered = false;
                    foreach (Rect other in placed)
                        if (test.Overlaps(other)) { covered = true; break; }
                    if (!covered || !canRise || level >= MaxViewStack) break;
                }
                stacks[i] = level;
                placed.Add(box);
            }
            return stacks;
        }

        /// <summary>A label's box on the view plane (1 m in front of the camera), grown by a fraction of its size on every side.</summary>
        static Rect ViewBox(Vector3 bottom, Vector2 size, float margin)
        {
            float z = bottom.z;
            float w = size.x * (1f + 2f * margin), h = size.y * (1f + 2f * margin);
            return new Rect((bottom.x - w / 2f) / z, (bottom.y - size.y * margin) / z, w / z, h / z);
        }

        void Update()
        {
            if (growStarted < 0f) return;
            float t = Time.time - growStarted;
            if (Habitat != null) Habitat.SetGrowth(t / GrowSeconds);
            for (int i = 0; i < stops.Count; i++) stops[i].SetGrowth((t - GrowStagger * (i + 1)) / GrowSeconds);
            if (t > GrowSeconds + GrowStagger * (stops.Count + 1)) growStarted = -1f;
        }

        void LateUpdate()
        {
            FaceCamera(ViewerCamera());
        }

        void OnDestroy()
        {
            Unsubscribe();
        }

        void OnStopSelected(int index)
        {
            for (int i = 0; i < stops.Count; i++) stops[i].SetSelected(i == index);
        }

        void Unsubscribe()
        {
            if (app != null) app.StopSelected -= OnStopSelected;
        }

        /// <summary>The camera the user sees through: the enabled one drawn last (desktop camera or the headset eye).</summary>
        public static Camera ViewerCamera()
        {
            Camera best = null;
            foreach (var cam in Camera.allCameras)
                if (cam.isActiveAndEnabled && (best == null || cam.depth > best.depth)) best = cam;
            return best;
        }

        PillarView MakePillar(SiteData site, Vector2 meters, string name, int index, Color color, float radius, Font font, string tagOverride = null)
        {
            var root = new GameObject(index == -2 ? "Pillar_TurnBack" : index < 0 ? "Pillar_Habitat" : "Pillar_" + index);
            root.transform.SetParent(transform, false);
            Vector3 basePoint = RouteGeometry.ToMap(site, meters, 0f);
            root.transform.localPosition = basePoint;
            var pillar = root.AddComponent<PillarView>();
            pillar.StopIndex = index;
            pillar.app = app;
            pillar.baseColor = color;
            pillar.radiusMeters = radius;
            pillar.heightMeters = PillarHeightMeters;

            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "Beam";
            DestroySafe(beam.GetComponent<Collider>());
            beam.transform.SetParent(root.transform, false);
            var beamRenderer = beam.GetComponent<MeshRenderer>();
            beamRenderer.sharedMaterial = lightMaterial;
            beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beamRenderer.receiveShadows = false;
            pillar.beam = beamRenderer;

            if (index >= 0)
            {
                // A fat, invisible capsule: easy to hit with a ray from 1.5 m.
                var hitObject = new GameObject("Hit");
                hitObject.transform.SetParent(root.transform, false);
                var capsule = hitObject.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                capsule.radius = HitRadiusMeters;
                capsule.height = PillarHeightMeters + 2f * HitRadiusMeters;
                capsule.center = new Vector3(0f, PillarHeightMeters / 2f, 0f);
                pillar.hit = capsule;
            }

            MakeLabel(pillar, name, tagOverride ?? (index >= 0 && index < site.Stops.Length ? site.Stops[index].tag : null), font);
            pillar.Refresh();
            return pillar;
        }

        void MakeLabel(PillarView pillar, string text, string tag, Font font)
        {
            var canvasObject = new GameObject("Label", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var rect = (RectTransform)canvasObject.transform;
            rect.SetParent(pillar.transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObject.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;

            var boxObject = new GameObject("Box", typeof(RectTransform), typeof(Image));
            var boxRect = (RectTransform)boxObject.transform;
            boxRect.SetParent(rect, false);
            var box = boxObject.GetComponent<Image>();
            box.color = WalkbackTheme.PanelBack;
            box.raycastTarget = false;
            if (labelMaterial != null) box.material = labelMaterial;

            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var textRect = (RectTransform)textObject.transform;
            textRect.SetParent(rect, false);
            var label = textObject.GetComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = LabelFontSize;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            label.color = WalkbackTheme.Text;
            if (labelMaterial != null) label.material = labelMaterial;

            float width = Mathf.Max(label.preferredWidth, text.Length * LabelFontSize * 0.55f) + 28f;
            float height = LabelFontSize + 18f;
            Text tagText = null;
            if (!string.IsNullOrEmpty(tag))
            {
                var tagObject = new GameObject("Tag", typeof(RectTransform), typeof(Text));
                tagObject.transform.SetParent(rect, false);
                tagText = tagObject.GetComponent<Text>();
                tagText.font = font;
                tagText.text = tag;
                tagText.fontSize = TagFontSize;
                tagText.alignment = TextAnchor.MiddleCenter;
                tagText.horizontalOverflow = HorizontalWrapMode.Overflow;
                tagText.verticalOverflow = VerticalWrapMode.Overflow;
                tagText.raycastTarget = false;
                tagText.color = WalkbackTheme.Watch;
                if (labelMaterial != null) tagText.material = labelMaterial;
                width = Mathf.Max(width, Mathf.Max(tagText.preferredWidth, tag.Length * TagFontSize * 0.55f) + 28f);
                height += TagFontSize + 6f;
            }
            rect.sizeDelta = new Vector2(width, height);
            // Pivot at the bottom center: the label sits on top of the pillar.
            rect.pivot = new Vector2(0.5f, 0f);
            Stretch(boxRect);
            Stretch(textRect);
            if (tagText != null)
            {
                // Name in the top part, the tag in the bottom strip.
                float split = (TagFontSize + 10f) / height;
                textRect.anchorMin = new Vector2(0f, split);
                var tagRect = (RectTransform)tagText.transform;
                Stretch(tagRect);
                tagRect.anchorMax = new Vector2(1f, split + 0.04f);
            }

            pillar.labelRoot = rect;
            pillar.label = label;
            pillar.labelBox = box;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        static void DestroySafe(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
