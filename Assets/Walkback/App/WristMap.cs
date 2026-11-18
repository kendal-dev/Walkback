// Written with Claude Code (AI), directed and tested by team MARSXR
using System.Collections.Generic;
using UnityEngine;

namespace Walkback
{
    /// <summary>
    /// The 3D map on the wrist (guion §22.2, §21.6 "pegado a la mano"): the same square kilometer as the
    /// table (same mesh, same paint as the table's layer), 14 cm wide, relief not exaggerated, held
    /// above the left hand or controller and tilted toward the face. Pins: cyan = you, green = habitat,
    /// amber = stops. It shows while the astronaut looks at the raised left hand. X on the left
    /// controller takes it off the wrist and leaves it in the air where the hand is, 40 cm wide and fixed
    /// in the room (world-locked, not head-locked); X again takes it back to the wrist.
    /// Without a headset it floats low on the left of the view.
    /// </summary>
    public class WristMap : MonoBehaviour
    {
        public const float WidthMeters = 0.14f;
        public const float LookAngleDegrees = 40f;
        public const float TiltDegrees = 45f;

        /// <summary>Scaled holder: its children are in the terrain mesh's space (meters, origin at the map center).</summary>
        public Transform model;
        public MeshFilter terrainCopy;
        public MeshRenderer terrainRenderer;
        public Transform youPin;
        /// <summary>A nose on the you-pin pointing the way the astronaut looks (hidden while that is not known).</summary>
        public Transform youNose;
        public Transform habitatPin;
        public Material stopMaterial;
        public GlobeLabel label;

        public bool Visible { get { return gameObject.activeSelf; } }
        public Vector2 You { get; private set; }

        /// <summary>Side of the map once it is left in the air: read from 1 to 1.5 m, not from the wrist.</summary>
        public const float PlacedWidthMeters = 0.40f;
        /// <summary>The label grows with the map (13 mm letters on the wrist, 26 mm in the air).</summary>
        public const float PlacedLabelScale = 2f;

        /// <summary>
        /// True while the map is left in the air: fixed in the room (not on the head, not on the hand),
        /// until it is taken back to the wrist. X on the left controller switches between the two.
        /// </summary>
        public bool Placed { get; private set; }
        /// <summary>True while the placed map lies flat with its north turned to Mars' north (mission mode).</summary>
        public bool NorthAligned { get; private set; }
        /// <summary>Side of the map now, in meters.</summary>
        public float Width { get { return Placed ? PlacedWidthMeters : WidthMeters; } }

        /// <summary>
        /// Leaves the map in the air at <paramref name="at"/>, bigger, facing the viewer as it was on the
        /// wrist. It stays there (the room does not move) until ReturnToWrist.
        /// </summary>
        public void PlaceAt(Vector3 at, Transform head)
        {
            Placed = true;
            NorthAligned = false;
            ApplyWidth();
            Pose(at, head);
            SetVisible(true);
            WalkbackLog.Info("Wrist map: left in the air at " + at.ToString("F2"));
        }

        /// <summary>Back to the wrist: small again, shown when the user looks at the raised hand.</summary>
        public void ReturnToWrist()
        {
            if (!Placed) return;
            Placed = false;
            NorthAligned = false;
            ApplyWidth();
            WalkbackLog.Info("Wrist map: back on the wrist");
        }

        /// <summary>
        /// Each frame while placed. In mission mode Mars is anchored in the room, so north is known: the
        /// map lies flat with its north on Mars' north (<paramref name="north"/> = the Mars root's rotation),
        /// and a direction on the map is the same direction around the user. Where north is not known
        /// (inside a 360° photo) it keeps the pose it was left in. The label always turns to the viewer.
        /// </summary>
        public void KeepPlaced(Transform head, bool northKnown, Quaternion north)
        {
            if (!Placed) return;
            if (northKnown)
            {
                transform.rotation = Quaternion.Euler(0f, north.eulerAngles.y, 0f);
                NorthAligned = true;
            }
            if (label != null && head != null)
            {
                Vector3 away = label.transform.position - head.position;
                if (away.sqrMagnitude > 1e-6f) label.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
        }

        void ApplyWidth()
        {
            // The model's scale is width / site side; before the first Prepare there is no site yet and Prepare sets it.
            if (model != null && preparedSide > 0f) model.localScale = Vector3.one * (Width / preparedSide);
            if (label != null)
            {
                label.transform.localScale = Vector3.one * (Placed ? PlacedLabelScale : 1f);
                label.transform.localPosition = new Vector3(0f, 0.03f, Width / 2f + 0.015f * (Placed ? PlacedLabelScale : 1f));
            }
        }

        /// <summary>Side of the site of the last Prepare, in meters (0 before the first one).</summary>
        float preparedSide;

        readonly List<Transform> stopPins = new List<Transform>();
        Transform turnBackPin;
        /// <summary>v1.1: the red pin at the plan's point of no return (null until the first Prepare).</summary>
        public Transform TurnBackPin { get { return turnBackPin; } }
        public IList<Transform> StopPins { get { return stopPins; } }

        /// <summary>
        /// Which way the astronaut looks, in degrees clockwise from north: the you-pin turns and shows its
        /// nose. The model's +z is north and +x east, so the heading is a turn about its up axis.
        /// </summary>
        public void SetHeading(bool known, float degreesFromNorth)
        {
            if (youPin != null) youPin.localRotation = Quaternion.Euler(0f, known ? degreesFromNorth : 0f, 0f);
            if (youNose != null) youNose.gameObject.SetActive(known);
        }

        /// <summary>Copies the table's terrain and puts the pins for an astronaut standing at <paramref name="here"/>.</summary>
        public void Prepare(WalkbackApp app, Vector2 here, string hereName)
        {
            GaleTerrain terrain = app.terrain;
            SiteData site = terrain.Site;
            You = here;
            terrainCopy.sharedMesh = terrain.GetComponent<MeshFilter>().sharedMesh;
            terrainRenderer.sharedMaterial = terrain.GetComponent<MeshRenderer>().sharedMaterial;
            var block = new MaterialPropertyBlock();
            Texture paint = terrain.LayerTexture(terrain.Layer);
            if (paint != null) block.SetTexture("_BaseMap", paint);
            block.SetColor("_BaseColor", terrain.Layer == MapLayer.Photo ? terrain.PhotoTint : Color.white);
            terrainRenderer.SetPropertyBlock(block);
            preparedSide = site.SideMeters;
            model.localScale = Vector3.one * (Width / site.SideMeters);

            Pin(youPin, site, here, 26f);
            Pin(habitatPin, site, site.Habitat, 22f);
            var stops = site.Stops;
            while (stopPins.Count < stops.Length) stopPins.Add(MakePin("StopPin" + stopPins.Count));
            for (int i = 0; i < stopPins.Count; i++)
            {
                bool used = i < stops.Length;
                stopPins[i].gameObject.SetActive(used);
                if (used)
                {
                    Pin(stopPins[i], site, new Vector2(stops[i].x, stops[i].y), 16f);
                    UserStops.TintMark(stopPins[i].GetComponent<Renderer>(), stops[i]);
                }
            }
            // v1.1: the plan's point of no return, as a red pin.
            if (turnBackPin == null)
            {
                turnBackPin = MakePin("TurnBackPin");
                UserStops.TintMark(turnBackPin.GetComponent<Renderer>(), StopPillars.TurnBackColor);
            }
            turnBackPin.gameObject.SetActive(app.TurnBack.found);
            if (app.TurnBack.found) Pin(turnBackPin, site, app.TurnBack.point, 16f);
            if (label != null) label.SetLines("YOU  ·  " + hereName);
        }

        void Pin(Transform pin, SiteData site, Vector2 meters, float half)
        {
            pin.localPosition = RouteGeometry.ToMap(site, meters, half);
        }

        Transform MakePin(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            if (Application.isPlaying) Destroy(go.GetComponent<Collider>()); else DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(model, false);
            go.transform.localScale = new Vector3(12f, 16f, 12f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = stopMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        public void SetVisible(bool on)
        {
            if (gameObject.activeSelf != on) gameObject.SetActive(on);
        }

        /// <summary>Above the hand or controller, north away from the viewer, tilted toward the face.</summary>
        public void HoldAt(Transform hand, Transform head)
        {
            Vector3 at = hand.position + Vector3.up * 0.07f;
            Pose(at, head);
        }

        /// <summary>No headset: low on the left of the view, as if the wrist were raised.</summary>
        public void HoldInFrontOf(Transform head)
        {
            Vector3 flat = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, flat);
            Pose(head.position + flat * 0.45f - right * 0.24f + Vector3.down * 0.20f, head);
        }

        void Pose(Vector3 at, Transform head)
        {
            Vector3 away = Vector3.ProjectOnPlane(at - head.position, Vector3.up);
            if (away.sqrMagnitude < 1e-6f) away = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            transform.SetPositionAndRotation(at, Quaternion.LookRotation(away.normalized, Vector3.up) * Quaternion.Euler(-TiltDegrees, 0f, 0f));
            if (label != null) label.transform.rotation = Quaternion.LookRotation(label.transform.position - head.position, Vector3.up);
        }

        /// <summary>The wrist "gesture": the left hand is raised into the view.</summary>
        public static bool LookingAt(Transform hand, Transform head)
        {
            return hand != null && head != null && Vector3.Angle(head.forward, hand.position - head.position) < LookAngleDegrees;
        }
    }
}
