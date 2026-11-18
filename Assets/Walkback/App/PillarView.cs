// Written with Claude Code (AI), directed and tested by team MARSXR
using UnityEngine;
using UnityEngine.UI;

namespace Walkback
{
    /// <summary>
    /// One pillar of light on the map: the habitat (green, not pressable) or a stop (amber; pressing it
    /// selects the stop). Built by StopPillars. Its label stays the same size in the room whatever the
    /// map scale, and turns to face whoever looks at it.
    /// </summary>
    public class PillarView : MonoBehaviour, IPointerTarget
    {
        /// <summary>Index into Site.Stops, or -1 for the habitat.</summary>
        public int StopIndex = -1;
        public WalkbackApp app;
        public MeshRenderer beam;
        public Collider hit;
        public RectTransform labelRoot;
        public Text label;
        public Image labelBox;
        public Color baseColor = Color.white;
        public float radiusMeters = 5f;
        public float heightMeters = StopPillars.PillarHeightMeters;
        /// <summary>How many labels of nearby stops sit under this one: it goes up one label height per step, so they never cover each other.</summary>
        public int labelStack;
        /// <summary>
        /// Extra lift, in the same steps, for the eye that looks right now: a label that would cover (or hide
        /// behind) a nearer pillar's label from there goes up until both read. Set by StopPillars every frame.
        /// </summary>
        public float viewLift;
        float labelBase;

        public bool Hovered { get; private set; }
        public bool Selected { get; private set; }
        public float Growth { get; private set; } = 1f;
        public bool IsHabitat { get { return StopIndex < 0; } }

        /// <summary>Label height in the room, in meters per canvas pixel (1 px = 1 mm).</summary>
        public const float LabelMetersPerPixel = 0.001f;

        MaterialPropertyBlock block;

        public void OnPointerEnter()
        {
            Hovered = true;
            Refresh();
            if (!IsHabitat && app != null) app.PreviewStop(StopIndex);
        }

        public void OnPointerExit()
        {
            Hovered = false;
            Refresh();
            if (!IsHabitat && app != null) app.EndPreview(StopIndex);
        }

        void OnDisable()
        {
            // Hidden while the pointer was on it (mission, 360° photo): the preview must not stay on the board.
            if (Hovered && !IsHabitat && app != null) app.EndPreview(StopIndex);
            Hovered = false;
        }

        public void OnPointerClick(Vector3 worldPoint)
        {
            if (IsHabitat || app == null) return;
            app.SelectStop(StopIndex);
        }

        public void SetSelected(bool value)
        {
            Selected = value;
            Refresh();
        }

        /// <summary>0 = not grown yet, 1 = full height. The label shows once the pillar is almost up.</summary>
        public void SetGrowth(float value)
        {
            Growth = Mathf.Clamp01(value);
            Refresh();
        }

        public void Refresh()
        {
            if (beam == null) return;
            float eased = 1f - (1f - Growth) * (1f - Growth);
            float radius = radiusMeters * (Selected ? 1.6f : Hovered ? 1.3f : 1f);
            float height = Mathf.Max(0.01f, heightMeters * eased);
            beam.transform.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);
            beam.transform.localPosition = new Vector3(0f, height / 2f, 0f);

            if (block == null) block = new MaterialPropertyBlock();
            beam.GetPropertyBlock(block);
            Color color = Selected ? Color.white : baseColor * (Hovered ? 1.25f : 1f);
            block.SetColor("_Color", color);
            block.SetFloat("_Pulse", Selected ? 1f : 0f);
            block.SetFloat("_Vertical", 1f);
            block.SetFloat("_Progress", 1f);
            block.SetFloat("_DashCount", 0f);
            block.SetFloat("_Border", 0f);
            beam.SetPropertyBlock(block);

            if (labelRoot != null)
            {
                labelRoot.gameObject.SetActive(Growth > 0.85f);
                labelBase = height + 10f;
                PlaceLabel();
            }
            if (label != null) label.color = Selected ? WalkbackTheme.Watch : WalkbackTheme.Text;
            if (labelBox != null) labelBox.color = Selected ? new Color(0.20f, 0.15f, 0.03f) : WalkbackTheme.PanelBack;
        }

        /// <summary>Label on top of the beam, one label height higher per nearby stop under it (uses the label's current scale).</summary>
        void PlaceLabel()
        {
            float step = labelRoot.sizeDelta.y * Mathf.Abs(labelRoot.localScale.y) * 1.1f;
            labelRoot.localPosition = new Vector3(0f, labelBase + (labelStack + viewLift) * step, 0f);
        }

        /// <summary>Turns the label to the camera and keeps it the same size in the room.</summary>
        public void FaceCamera(Camera cam)
        {
            if (labelRoot == null || cam == null) return;
            Vector3 parentScale = labelRoot.parent != null ? labelRoot.parent.lossyScale : Vector3.one;
            float s = Mathf.Abs(parentScale.x) > 1e-7f ? LabelMetersPerPixel / Mathf.Abs(parentScale.x) : 1f;
            labelRoot.localScale = Vector3.one * s;
            PlaceLabel();
            Vector3 away = labelRoot.position - cam.transform.position;
            if (away.sqrMagnitude > 1e-8f) labelRoot.rotation = Quaternion.LookRotation(away, cam.transform.up);
        }
    }
}
