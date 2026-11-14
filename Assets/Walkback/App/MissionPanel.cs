// Written with Claude Code (AI), directed and tested by team MARSXR
using UnityEngine;
using UnityEngine.UI;

namespace Walkback
{
    /// <summary>
    /// The mission board behind the table: 1.50 m x 0.70 m, one canvas pixel = one millimeter.
    /// Top and biggest: the verdict card. Everything else is smaller.
    /// The back plate is an opaque quad and the canvas never writes alpha, so text edges
    /// do not open "holes" to the room in passthrough.
    /// </summary>
    public class MissionPanel : MonoBehaviour
    {
        public const int PixelsWide = 1500;
        public const int PixelsHigh = 700;
        public const float MetersPerPixel = 0.001f;

        public Text title;
        public Text tagline;
        public Text verdictTitle;
        public Text verdictDetail;
        public Text dataLine;
        public Text footer;

        /// <summary>Builds the panel once, at edit time, so it is saved in the scene.</summary>
        public void Build(Material backPlateMaterial, Material uiMaterial)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);

            var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
            back.name = "BackPlate";
            DestroyImmediate(back.GetComponent<Collider>());
            back.transform.SetParent(transform, false);
            back.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            back.transform.localScale = new Vector3(WalkbackTheme.PanelWidthMeters, WalkbackTheme.PanelHeightMeters, 1f);
            back.GetComponent<MeshRenderer>().sharedMaterial = backPlateMaterial;

            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObject.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            var rect = (RectTransform)canvasObject.transform;
            rect.sizeDelta = new Vector2(PixelsWide, PixelsHigh);
            rect.localScale = Vector3.one * MetersPerPixel;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            title = AddText(rect, "Title", "WALKBACK", font, uiMaterial, 72, FontStyle.Bold, WalkbackTheme.Text, 50, 28, 800, 90);
            tagline = AddText(rect, "Tagline", "The safest way to go far.", font, uiMaterial, 36, FontStyle.Normal, WalkbackTheme.TextSoft, 50, 116, 900, 50);

            AddBox(rect, "VerdictCard", uiMaterial, WalkbackTheme.CardBack, 50, 190, 1400, 270);
            verdictTitle = AddText(rect, "VerdictTitle", "NO ROUTE YET", font, uiMaterial, 96, FontStyle.Bold, WalkbackTheme.Watch, 90, 205, 1320, 130);
            verdictDetail = AddText(rect, "VerdictDetail", "Time, oxygen used and margin to return will show here.", font, uiMaterial, 40, FontStyle.Normal, WalkbackTheme.Text, 90, 350, 1320, 80);

            dataLine = AddText(rect, "DataLine", "TEST TERRAIN · synthetic shape for development · not Mars data", font, uiMaterial, 38, FontStyle.Bold, WalkbackTheme.Watch, 50, 490, 1400, 60);
            footer = AddText(rect, "Footer", "Meta Quest 3 stands in for the helmet display.", font, uiMaterial, 32, FontStyle.Normal, WalkbackTheme.TextSoft, 50, 620, 1400, 50);
        }

        static Text AddText(RectTransform parent, string name, string content, Font font, Material material, int size, FontStyle style, Color color, float x, float y, float w, float h)
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

        static void AddBox(RectTransform parent, string name, Material material, Color color, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            Place(parent, (RectTransform)go.transform, x, y, w, h);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (material != null) image.material = material;
        }

        /// <summary>x, y from the top-left corner of the panel, in pixels (millimeters).</summary>
        static void Place(RectTransform parent, RectTransform rect, float x, float y, float w, float h)
        {
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }
    }
}
