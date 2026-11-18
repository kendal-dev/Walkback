// Written with Claude Code (AI), directed and tested by team MARSXR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Walkback
{
    /// <summary>
    /// The 2D map in the corner of the helmet HUD (guion §22.2): the HiRISE photo of the square
    /// kilometer (Site/ortho.jpg, session B), north up, a window around the astronaut wide enough to
    /// show the next target and the habitat. Cyan dot = you, green square = habitat, amber dots = stops,
    /// a white line to the next target, a white bar of 100 m. Lives in the HUD's plane (+z away from the
    /// viewer, quads face the viewer).
    /// </summary>
    public class MiniMap : MonoBehaviour
    {
        public const float SizeMeters = 0.24f;
        /// <summary>Widest photo the mini map would load for itself. It now shares the terrain's photo, so it loads none.</summary>
        public const int PhotoWidth = 1024;
        public const float MinWindow = 120f;

        public Renderer photoQuad;
        public Transform you;
        /// <summary>A tick on the "you" dot pointing the way the astronaut looks (hidden while that is not known).</summary>
        public Transform youHeading;
        public Transform habitat;
        public Transform nextRing;
        public Transform line;
        public Transform scaleBar;
        public Material stopMaterial;
        public GlobeLabel caption;

        public float WindowMeters { get; private set; }
        public Vector2 WindowCenter { get; private set; }
        public Texture Photo { get { return photo; } }
        public bool PhotoIsOrtho { get; private set; }

        readonly List<Transform> stopDots = new List<Transform>();
        Texture photo;
        float side = 1000f;

        /// <summary>
        /// Which way the astronaut looks, in degrees clockwise from north (the map is north up), so the
        /// "you" dot is an arrow while walking and turning. Not known (standing inside a 360° photo,
        /// where the headset's direction is not a direction on Mars): the tick hides and the dot is a dot.
        /// </summary>
        public void SetHeading(bool known, float degreesFromNorth)
        {
            HeadingKnown = known;
            HeadingDegrees = known ? Mathf.Repeat(degreesFromNorth, 360f) : 0f;
            // The map is seen with x to the right and y up: clockwise on it is a negative turn about z.
            if (you != null) you.localRotation = Quaternion.Euler(0f, 0f, -HeadingDegrees);
            if (youHeading != null) youHeading.gameObject.SetActive(known);
        }

        public bool HeadingKnown { get; private set; }
        public float HeadingDegrees { get; private set; }

        void OnDestroy()
        {
            if (photo is RenderTexture) { ((RenderTexture)photo).Release(); DestroySafe(photo); }
        }

        /// <summary>Local position in the map's plane of a point in local site meters (lift = toward the viewer).</summary>
        public Vector3 LocalOf(Vector2 meters, float lift)
        {
            return new Vector3((meters.x - WindowCenter.x) / WindowMeters * SizeMeters,
                               (meters.y - WindowCenter.y) / WindowMeters * SizeMeters, -lift);
        }

        public bool InWindow(Vector2 meters)
        {
            return Mathf.Abs(meters.x - WindowCenter.x) <= WindowMeters / 2f && Mathf.Abs(meters.y - WindowCenter.y) <= WindowMeters / 2f;
        }

        /// <summary>Draws the map around <paramref name="here"/> with the next target and every stop.</summary>
        public void Show(WalkbackApp app, Vector2 here, Vector2 next)
        {
            SiteData site = app.terrain.Site;
            side = site.SideMeters;
            EnsurePhoto(app);

            float reach = Mathf.Max(Vector2.Distance(here, next), Vector2.Distance(here, site.Habitat));
            WindowMeters = Mathf.Clamp(reach * 2.3f, MinWindow, side);
            float half = WindowMeters / 2f;
            WindowCenter = new Vector2(Mathf.Clamp(here.x, half, side - half), Mathf.Clamp(here.y, half, side - half));

            var block = new MaterialPropertyBlock();
            if (photo != null) block.SetTexture("_BaseMap", photo);
            block.SetColor("_BaseColor", PhotoIsOrtho ? app.terrain.PhotoTint : Color.white);
            block.SetVector("_BaseMap_ST", new Vector4(WindowMeters / side, WindowMeters / side, (WindowCenter.x - half) / side, (WindowCenter.y - half) / side));
            photoQuad.SetPropertyBlock(block);

            Place(habitat, site.Habitat, 0.003f);
            var stops = site.Stops;
            while (stopDots.Count < stops.Length) stopDots.Add(MakeDot("Stop" + stopDots.Count, stopMaterial, 0.009f));
            for (int i = 0; i < stopDots.Count; i++)
            {
                if (i < stops.Length)
                {
                    Place(stopDots[i], new Vector2(stops[i].x, stops[i].y), 0.004f);
                    UserStops.TintMark(stopDots[i].GetComponent<Renderer>(), stops[i]);
                }
                else stopDots[i].gameObject.SetActive(false);
            }
            // v1.1: the plan's point of no return, as a red dot.
            if (turnBackDot == null)
            {
                turnBackDot = MakeDot("TurnBack", stopMaterial, 0.011f);
                UserStops.TintMark(turnBackDot.GetComponent<Renderer>(), StopPillars.TurnBackColor);
            }
            if (app.TurnBack.found) Place(turnBackDot, app.TurnBack.point, 0.0045f);
            else turnBackDot.gameObject.SetActive(false);
            Place(nextRing, next, 0.0025f);
            Place(you, here, 0.005f);

            // The line from you to the next target, cut at the edge of the window.
            Vector2 end = next;
            if (!InWindow(next))
            {
                Vector2 d = next - here;
                float t = 1f;
                if (Mathf.Abs(d.x) > 1e-3f) t = Mathf.Min(t, ((d.x > 0 ? WindowCenter.x + half : WindowCenter.x - half) - here.x) / d.x);
                if (Mathf.Abs(d.y) > 1e-3f) t = Mathf.Min(t, ((d.y > 0 ? WindowCenter.y + half : WindowCenter.y - half) - here.y) / d.y);
                end = here + d * Mathf.Clamp01(t);
            }
            Vector3 a = LocalOf(here, 0.002f), b = LocalOf(end, 0.002f);
            float length = Vector3.Distance(a, b);
            line.gameObject.SetActive(length > 0.001f);
            line.localPosition = (a + b) / 2f;
            line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
            line.localScale = new Vector3(Mathf.Max(length, 0.001f), 0.0025f, 1f);

            float barMeters = WindowMeters >= 600f ? 200f : 100f;
            float barLength = barMeters / WindowMeters * SizeMeters;
            scaleBar.localScale = new Vector3(barLength, 0.003f, 1f);
            scaleBar.localPosition = new Vector3(-SizeMeters / 2f + 0.012f + barLength / 2f, -SizeMeters / 2f + 0.012f, -0.003f);
            if (caption != null)
                caption.SetLines("North up  ·  bar " + barMeters.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " m" + (PhotoIsOrtho ? "  ·  HiRISE" : ""));
            gameObject.SetActive(true);
        }

        void Place(Transform t, Vector2 meters, float lift)
        {
            bool inside = InWindow(meters);
            t.gameObject.SetActive(inside);
            if (inside) t.localPosition = LocalOf(meters, lift);
        }

        Transform turnBackDot;
        /// <summary>v1.1: the red dot at the plan's point of no return (null until the first Show).</summary>
        public Transform TurnBackDot { get { return turnBackDot; } }
        public IList<Transform> StopDots { get { return stopDots; } }

        Transform MakeDot(string name, Material material, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            DestroySafe(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(size, size, 1f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>The photo once, scaled down to 1024 px (memory on the Quest); the height paint if there is no photo.</summary>
        /// <summary>
        /// The photo under the mini map. The terrain already holds the HiRISE photo (GaleTerrain.PhotoSize px,
        /// with mipmaps): the mini map shares that texture instead of reading and decoding ortho.jpg again
        /// (4096 x 4096, about 48 MB while decoding, on the main thread at the moment of the jump).
        /// The mini map never owns it: the terrain destroys it. Without a photo: the height paint.
        /// </summary>
        void EnsurePhoto(WalkbackApp app)
        {
            if (photo != null) return;
            GaleTerrain terrain = app.terrain;
            if (terrain.HasPhoto)
            {
                Texture2D shared = terrain.LayerTexture(MapLayer.Photo);
                // LayerTexture falls back to the height paint if the photo cannot be read: check which one came.
                if (shared != null && shared.name == "Paint_Photo")
                {
                    photo = shared;
                    PhotoIsOrtho = true;
                    WalkbackLog.Info("Mini map: shares the terrain's HiRISE photo, " + photo.width + " x " + photo.height);
                    return;
                }
            }
            photo = terrain.LayerTexture(MapLayer.Height);
            PhotoIsOrtho = false;
        }

        static void DestroySafe(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
