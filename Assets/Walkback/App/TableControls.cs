// Written with Claude Code (AI), directed and tested by team MARSXR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Walkback
{
    /// <summary>
    /// Turn, zoom and slide the map on the table, and bring the whole table in front of the user.
    ///   Controllers: right thumbstick left/right turns, up/down zooms · right grip held + move drags ·
    ///                left thumbstick slides · right thumbstick click resets the map · B or Y recenters.
    ///   Hands:       both hands pinching (not on a button) turn, zoom and slide together.
    ///   Desktop:     mouse wheel zooms · right button drag turns · R resets.
    ///   The RECENTER sign next to the table recenters too.
    /// Nothing moves while app.MapControlsLocked (the globe opening animates the map: trap 17), and a
    /// press that started on a button never drags the map.
    /// </summary>
    public class TableControls : MonoBehaviour
    {
        public const float MinZoom = 0.5f;
        public const float MaxZoom = 4f;
        public const float TurnDegreesPerSecond = 90f;
        public const float SlideMetersPerSecond = 0.4f;
        public const float TableDistance = 1.0f;
        const float Deadzone = 0.2f;

        public WalkbackApp app;
        public Pointer pointer;
        public PointerButton recenterButton;
        /// <summary>mapRoot scale with the map 1 m wide on the table (set when the scene is built).</summary>
        public float baseScale = 0.001f;

        /// <summary>Which way the table setup faces (0 = the panel is to the north of the table, along +z).</summary>
        public float StageYaw { get; private set; }

        bool gripDragging;
        Vector3 gripLast;
        bool twoHandActive;
        Vector3 handLeftLast, handRightLast;
        Vector2 mouseLast;
        OVRCameraRig rig;

        public void Begin(WalkbackApp owner)
        {
            app = owner;
            rig = app != null && app.cameraRig != null ? app.cameraRig.GetComponent<OVRCameraRig>() : null;
            if (recenterButton != null) recenterButton.Clicked = RecenterFromHead;
        }

        /// <summary>
        /// The RECENTER sign is not a child of the table: it shows only while the table does (not inside a
        /// 360° photo, not in mission mode). In the headset at 18:30 it stayed floating inside the photo and
        /// was pressed about 30 times for nothing.
        /// </summary>
        public void SyncRecenterSign()
        {
            if (recenterButton == null || app == null || app.table == null) return;
            bool show = app.table.gameObject.activeSelf;
            if (recenterButton.gameObject.activeSelf != show) recenterButton.gameObject.SetActive(show);
        }

        /// <summary>True while the map is being moved by the grip or by both hands (a tap on the map is not a tap then).</summary>
        public bool Dragging { get { return gripDragging || twoHandActive; } }

        public float Zoom { get { return app != null && app.mapRoot != null ? app.mapRoot.localScale.x / baseScale : 1f; } }

        bool Ready { get { return app != null && app.mapRoot != null && app.table != null && !app.MapControlsLocked; } }

        /// <summary>The map may be dragged only if the pointer is not holding a press that began on a button.</summary>
        public static bool CanDrag(Pointer p)
        {
            return p == null || !p.PressBeganOnTarget;
        }

        public void Tick()
        {
            SyncRecenterSign();
            if (!Ready) { gripDragging = false; twoHandActive = false; return; }
            float dt = Time.deltaTime;
            if (app.DesktopMode) { TickDesktop(); return; }

            // Right thumbstick: turn and zoom
            Vector2 right = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
            if (Mathf.Abs(right.x) > Deadzone) RotateMap(right.x * TurnDegreesPerSecond * dt);
            if (Mathf.Abs(right.y) > Deadzone) ZoomMap(Mathf.Exp(right.y * dt));

            // Left thumbstick: slide, relative to where the user looks
            Vector2 left = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
            if (left.magnitude > Deadzone)
            {
                Vector3 fwd = HeadForward();
                Vector3 side = Vector3.Cross(Vector3.up, fwd);
                MoveMap((fwd * left.y + side * left.x) * SlideMetersPerSecond * dt);
            }

            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.RTouch)) ResetMap();
            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch) || OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch))
                RecenterFromHead();

            // Right grip held: drag the map with the controller
            bool grip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch) && rig != null && rig.rightControllerAnchor != null;
            if (grip && CanDrag(pointer))
            {
                Vector3 p = rig.rightControllerAnchor.position;
                if (gripDragging) MoveMap(Flat(p - gripLast));
                gripLast = p;
                gripDragging = true;
            }
            else gripDragging = false;

            TickHands();
        }

        void TickHands()
        {
            OVRHand l = pointer != null ? pointer.leftHand : null;
            OVRHand r = pointer != null ? pointer.rightHand : null;
            bool both = l != null && r != null && l.IsTracked && r.IsTracked &&
                        l.GetFingerIsPinching(OVRHand.HandFinger.Index) && r.GetFingerIsPinching(OVRHand.HandFinger.Index) && CanDrag(pointer);
            if (!both || rig == null || rig.leftHandAnchor == null || rig.rightHandAnchor == null) { twoHandActive = false; return; }
            Vector3 lp = rig.leftHandAnchor.position, rp = rig.rightHandAnchor.position;
            if (twoHandActive)
            {
                TwoHandDelta(handLeftLast, handRightLast, lp, rp, out float yaw, out float ratio, out Vector3 mid);
                RotateMap(yaw);
                ZoomMap(ratio);
                MoveMap(mid);
            }
            handLeftLast = lp;
            handRightLast = rp;
            twoHandActive = true;
        }

        void TickDesktop()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse != null)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f) ZoomMap(Mathf.Pow(1.1f, Mathf.Sign(wheel)));
                Vector2 pos = mouse.position.ReadValue();
                if (mouse.rightButton.isPressed && mouse.rightButton.wasPressedThisFrame == false)
                    RotateMap((pos.x - mouseLast.x) * 0.3f);
                mouseLast = pos;
            }
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame) ResetMap();
        }

        // ---------------------------------------------------------------- map operations

        /// <summary>Turns the map about its own center. False if locked.</summary>
        public bool RotateMap(float degrees)
        {
            if (!Ready) return false;
            app.mapRoot.rotation = Quaternion.Euler(0f, degrees, 0f) * app.mapRoot.rotation;
            return true;
        }

        /// <summary>Zooms the map about its center, kept between MinZoom and MaxZoom. False if locked.</summary>
        public bool ZoomMap(float factor)
        {
            if (!Ready) return false;
            float zoom = ClampZoom(Zoom, factor);
            app.mapRoot.localScale = Vector3.one * (zoom * baseScale);
            MoveMap(Vector3.zero);
            return true;
        }

        /// <summary>Slides the map on the table plane; its center stays within reach of the table. False if locked.</summary>
        public bool MoveMap(Vector3 delta)
        {
            if (!Ready) return false;
            Vector3 center = TableTop();
            Vector3 offset = Flat(app.mapRoot.position + Flat(delta) - center);
            offset = ClampOffset(offset, Zoom);
            app.mapRoot.position = new Vector3(center.x + offset.x, app.mapRoot.position.y, center.z + offset.z);
            return true;
        }

        /// <summary>Map back to 1 m wide, centered on the table, north away from the user's side of the setup.</summary>
        public bool ResetMap()
        {
            if (!Ready) return false;
            Vector3 center = TableTop();
            app.mapRoot.localScale = Vector3.one * baseScale;
            app.mapRoot.rotation = Quaternion.Euler(0f, StageYaw, 0f);
            app.mapRoot.position = new Vector3(center.x, app.mapRoot.position.y, center.z);
            WalkbackLog.Info("Map reset");
            return true;
        }

        void RecenterFromHead()
        {
            Transform head = rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor : null;
            if (app != null && app.DesktopMode) { ResetMap(); return; }
            if (head == null) return;
            Recenter(head.position, head.forward);
        }

        /// <summary>
        /// Brings the table setup (table, map, panel and the other signs around it) in front of a head
        /// pose, turned to face it, and resets the map. False if locked.
        /// </summary>
        public bool Recenter(Vector3 headPosition, Vector3 headForward)
        {
            if (!Ready) return false;
            Vector3 tableCenter = app.table.position;
            RecenterDelta(headPosition, headForward, tableCenter, StageYaw, TableDistance, out float deltaYaw, out Vector3 newCenter);
            Quaternion turn = Quaternion.Euler(0f, deltaYaw, 0f);
            foreach (Transform t in StageObjects())
            {
                Vector3 local = t.position - tableCenter;
                t.position = newCenter + turn * local;
                t.rotation = turn * t.rotation;
            }
            StageYaw = Mathf.Repeat(StageYaw + deltaYaw, 360f);
            ResetMap();
            WalkbackLog.Info("Table recentered in front of the user, facing " + StageYaw.ToString("F0") + "°");
            return true;
        }

        /// <summary>
        /// Everything that belongs to the table setup: the scene roots except the camera rig, the cameras,
        /// the app, lights, the pointer, and anything that surrounds the user instead of the table
        /// (names with Panorama, Jump, Sky or HUD).
        /// </summary>
        public List<Transform> StageObjects()
        {
            var list = new List<Transform>();
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                if (app.cameraRig != null && root == app.cameraRig) continue;
                if (root == app.gameObject) continue;
                if (root.GetComponent<Camera>() != null || root.GetComponent<Light>() != null) continue;
                if (root.GetComponentInChildren<Pointer>(true) != null) continue;
                string n = root.name.ToLowerInvariant();
                if (n.Contains("panorama") || n.Contains("jump") || n.Contains("sky") || n.Contains("hud")) continue;
                list.Add(root.transform);
            }
            return list;
        }

        Vector3 TableTop()
        {
            return new Vector3(app.table.position.x, app.mapRoot.position.y, app.table.position.z);
        }

        Vector3 HeadForward()
        {
            Vector3 f = rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor.forward : Vector3.forward;
            f = Flat(f);
            return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
        }

        // ---------------------------------------------------------------- pure math (tested without XR)

        public static float ClampZoom(float zoom, float factor)
        {
            return Mathf.Clamp(zoom * factor, MinZoom, MaxZoom);
        }

        /// <summary>How far the map center may slide from the table center: more when zoomed in.</summary>
        public static float MaxOffset(float zoom)
        {
            return Mathf.Max(0.4f, 0.5f * zoom);
        }

        public static Vector3 ClampOffset(Vector3 offset, float zoom)
        {
            offset.y = 0f;
            return Vector3.ClampMagnitude(offset, MaxOffset(zoom));
        }

        public static Vector3 Flat(Vector3 v)
        {
            return new Vector3(v.x, 0f, v.z);
        }

        /// <summary>Where the table goes and how much the setup turns so it stands in front of the head.</summary>
        public static void RecenterDelta(Vector3 head, Vector3 headForward, Vector3 tableCenter, float stageYaw, float distance, out float deltaYaw, out Vector3 newTableCenter)
        {
            Vector3 f = Flat(headForward);
            f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            float targetYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            deltaYaw = Mathf.DeltaAngle(stageYaw, targetYaw);
            newTableCenter = new Vector3(head.x + f.x * distance, tableCenter.y, head.z + f.z * distance);
        }

        /// <summary>Two pinching hands from one frame to the next: turn (degrees), zoom ratio and slide.</summary>
        public static void TwoHandDelta(Vector3 lastA, Vector3 lastB, Vector3 a, Vector3 b, out float yawDegrees, out float ratio, out Vector3 slide)
        {
            Vector3 before = Flat(lastB - lastA), after = Flat(b - a);
            yawDegrees = before.sqrMagnitude > 1e-6f && after.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(before, after, Vector3.up) : 0f;
            ratio = before.magnitude > 0.02f ? Mathf.Clamp(after.magnitude / before.magnitude, 0.5f, 2f) : 1f;
            slide = Flat((a + b) * 0.5f - (lastA + lastB) * 0.5f);
        }
    }
}
