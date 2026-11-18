// Written with Claude Code (AI), directed and tested by team MARSXR
using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Walkback
{
    /// <summary>
    /// One pointer for the whole app. In the headset: a ray from the right controller (trigger presses)
    /// or the right hand (index pinch presses); the left side is the fallback. Without a headset: the
    /// mouse through the desktop camera (left click presses). It raycasts colliders and talks to the
    /// first IPointerTarget above the collider: enter, exit, and exactly one click per press.
    /// A press that starts on nothing is free for the table controls; a press that starts on a
    /// target never drags the map.
    /// </summary>
    public class Pointer : MonoBehaviour
    {
        public enum Source { None, RightController, RightHand, LeftController, LeftHand, Mouse, Test }

        /// <summary>
        /// How far the ray reaches. It must be longer than SiteJump.SphereRadius (6 m): inside the 360° photo
        /// the only collider around the user is the photo sphere, and pressing it is the way back to the table.
        /// </summary>
        public const float MaxDistance = 8f;
        const float RayWidth = 0.004f;
        const float RayWidthHover = 0.006f;
        const float DotSize = 0.014f;
        /// <summary>The ray is drawn this long when it hits nothing.</summary>
        const float FreeRayLength = 1.2f;

        public WalkbackApp app;
        public OVRHand rightHand;
        public OVRHand leftHand;
        public LineRenderer line;
        public Transform dot;

        /// <summary>Finds the target behind a collider. The test swaps it to use plain C# targets.</summary>
        public Func<Collider, IPointerTarget> Resolve = DefaultResolve;

        public Source CurrentSource { get; private set; }
        public IPointerTarget Hovered { get; private set; }
        public bool HasHit { get; private set; }
        /// <summary>The collider under the ray this frame (null when it hits nothing).</summary>
        public Collider HitCollider { get; private set; }
        public Vector3 HitPoint { get; private set; }
        public bool IsPressed { get; private set; }
        /// <summary>True from the frame a press starts on a target until it is released.</summary>
        public bool PressBeganOnTarget { get; private set; }
        public int Clicks { get; private set; }

        OVRCameraRig rig;
        MaterialPropertyBlock block;
        AudioSource clickAudio;
        float hapticStopTime = -1f;
        OVRInput.Controller hapticController = OVRInput.Controller.None;

        public static IPointerTarget DefaultResolve(Collider c)
        {
            return c != null ? c.GetComponentInParent<IPointerTarget>() : null;
        }

        /// <summary>
        /// Gives the ray the ground itself: a mesh collider on the terrain's mesh, so the dot lands on
        /// the relief instead of inside it. The mesh is built at runtime, so this runs after it exists.
        /// Returns the collider (null if there is no mesh yet).
        /// </summary>
        public static MeshCollider AttachTerrainCollider(GaleTerrain terrain)
        {
            if (terrain == null) return null;
            var filter = terrain.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return null;
            var meshCollider = terrain.GetComponent<MeshCollider>();
            if (meshCollider == null) meshCollider = terrain.gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = filter.sharedMesh;
            return meshCollider;
        }

        /// <summary>Called by the app once the mode (headset or desktop) is known.</summary>
        public void Begin(WalkbackApp owner)
        {
            app = owner;
            rig = app != null && app.cameraRig != null ? app.cameraRig.GetComponent<OVRCameraRig>() : null;
            clickAudio = gameObject.GetComponent<AudioSource>();
            if (clickAudio == null) clickAudio = gameObject.AddComponent<AudioSource>();
            clickAudio.playOnAwake = false;
            clickAudio.spatialBlend = 0f;
            clickAudio.volume = 0.35f;
            clickAudio.clip = MakeClickSound();
            ShowNothing();
            WalkbackLog.Info("Pointer ready (" + (app != null && app.DesktopMode ? "mouse" : "controller or hand") + ")");
        }

        /// <summary>Once per frame, from the app.</summary>
        public void Tick()
        {
            if (hapticStopTime > 0f && Time.time >= hapticStopTime)
            {
                OVRInput.SetControllerVibration(0f, 0f, hapticController);
                hapticStopTime = -1f;
            }

            Ray ray;
            bool pressed;
            Source source = ReadSource(out ray, out pressed);
            if (source != CurrentSource)
            {
                // Changing hands mid-press must not leave a held press behind.
                IsPressed = false;
                PressBeganOnTarget = false;
                if (source != Source.None) WalkbackLog.Info("Pointer source: " + source);
            }
            CurrentSource = source;
            if (source == Source.None)
            {
                Process(new Ray(Vector3.zero, Vector3.down), false, false);
                ShowNothing();
                return;
            }
            Process(ray, pressed);
            if (source == Source.Mouse) ShowDotOnly(); else ShowRay(ray);
        }

        /// <summary>
        /// The whole hover and press logic, with no XR: raycast, enter/exit, one click per press.
        /// Returns the target under the ray (or null).
        /// </summary>
        public IPointerTarget Process(Ray ray, bool pressed, bool raycast = true)
        {
            IPointerTarget target = null;
            HasHit = false;
            HitCollider = null;
            if (raycast && Physics.Raycast(ray, out RaycastHit hit, MaxDistance, ~0, QueryTriggerInteraction.Collide))
            {
                HasHit = true;
                HitPoint = hit.point;
                HitCollider = hit.collider;
                target = Resolve(hit.collider);
            }

            if (!SameTarget(target, Hovered))
            {
                if (IsAlive(Hovered)) Hovered.OnPointerExit();
                Hovered = target;
                if (IsAlive(target)) target.OnPointerEnter();
            }

            if (pressed && !IsPressed)
            {
                PressBeganOnTarget = IsAlive(target);
                if (PressBeganOnTarget)
                {
                    Clicks++;
                    target.OnPointerClick(HitPoint);
                    Feedback();
                }
            }
            if (!pressed) PressBeganOnTarget = false;
            IsPressed = pressed;
            return target;
        }

        /// <summary>Draws the ray from its origin to the hit (or a short free ray) and the dot at the hit.</summary>
        public void ShowRay(Ray ray)
        {
            if (line == null) return;
            Vector3 end = HasHit ? HitPoint : ray.origin + ray.direction * FreeRayLength;
            line.enabled = true;
            line.positionCount = 2;
            line.SetPosition(0, ray.origin);
            line.SetPosition(1, end);
            bool hover = IsAlive(Hovered);
            line.startWidth = hover ? RayWidthHover : RayWidth;
            line.endWidth = (hover ? RayWidthHover : RayWidth) * 0.6f;
            Color color = IsPressed && hover ? WalkbackTheme.Watch : (hover ? WalkbackTheme.Text : WalkbackTheme.Live);
            Tint(line, color);
            if (dot != null)
            {
                dot.gameObject.SetActive(HasHit);
                dot.position = end;
                dot.localScale = Vector3.one * (hover ? DotSize * 1.4f : DotSize);
                Tint(dot.GetComponent<Renderer>(), color);
            }
        }

        void ShowDotOnly()
        {
            if (line != null) line.enabled = false;
            if (dot == null) return;
            dot.gameObject.SetActive(HasHit);
            dot.position = HitPoint;
            bool hover = IsAlive(Hovered);
            dot.localScale = Vector3.one * (hover ? DotSize * 1.4f : DotSize);
            Tint(dot.GetComponent<Renderer>(), hover ? WalkbackTheme.Text : WalkbackTheme.Live);
        }

        public void ShowNothing()
        {
            if (line != null) line.enabled = false;
            if (dot != null) dot.gameObject.SetActive(false);
        }

        Source ReadSource(out Ray ray, out bool pressed)
        {
            ray = default(Ray);
            pressed = false;
            if (app != null && app.DesktopMode)
            {
                var mouse = Mouse.current;
                if (mouse == null || app.desktopCamera == null) return Source.None;
                ray = app.desktopCamera.ScreenPointToRay(mouse.position.ReadValue());
                pressed = mouse.leftButton.isPressed;
                return Source.Mouse;
            }
            if (rig == null) return Source.None;

            // Right first, left as fallback; a controller wins over a hand on the same side.
            if (OVRInput.IsControllerConnected(OVRInput.Controller.RTouch) && rig.rightControllerAnchor != null)
            {
                ray = new Ray(rig.rightControllerAnchor.position, rig.rightControllerAnchor.forward);
                pressed = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
                hapticController = OVRInput.Controller.RTouch;
                return Source.RightController;
            }
            if (HandReady(rightHand))
            {
                ray = new Ray(rightHand.PointerPose.position, rightHand.PointerPose.forward);
                pressed = rightHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
                return Source.RightHand;
            }
            if (OVRInput.IsControllerConnected(OVRInput.Controller.LTouch) && rig.leftControllerAnchor != null)
            {
                ray = new Ray(rig.leftControllerAnchor.position, rig.leftControllerAnchor.forward);
                pressed = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch);
                hapticController = OVRInput.Controller.LTouch;
                return Source.LeftController;
            }
            if (HandReady(leftHand))
            {
                ray = new Ray(leftHand.PointerPose.position, leftHand.PointerPose.forward);
                pressed = leftHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
                return Source.LeftHand;
            }
            return Source.None;
        }

        static bool HandReady(OVRHand hand)
        {
            return hand != null && hand.isActiveAndEnabled && hand.IsTracked && hand.IsPointerPoseValid && !hand.IsSystemGestureInProgress;
        }

        /// <summary>Every press answers: a short click sound, and a short buzz on a controller.</summary>
        void Feedback()
        {
            if (clickAudio != null && clickAudio.clip != null && Application.isPlaying) clickAudio.Play();
            if (Application.isPlaying && (CurrentSource == Source.RightController || CurrentSource == Source.LeftController))
            {
                OVRInput.SetControllerVibration(0.5f, 0.35f, hapticController);
                hapticStopTime = Time.time + 0.06f;
            }
        }

        void Tint(Renderer r, Color color)
        {
            if (r == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            r.SetPropertyBlock(block);
        }

        static bool SameTarget(IPointerTarget a, IPointerTarget b)
        {
            if (!IsAlive(a) && !IsAlive(b)) return true;
            return ReferenceEquals(a, b);
        }

        /// <summary>A target that is a destroyed Unity object counts as gone.</summary>
        public static bool IsAlive(IPointerTarget t)
        {
            if (t == null) return false;
            var unityObject = t as UnityEngine.Object;
            return !ReferenceEquals(unityObject, null) ? unityObject != null : true;
        }

        /// <summary>A 45 ms click, made in code: no audio file to import.</summary>
        static AudioClip MakeClickSound()
        {
            const int rate = 44100;
            int samples = rate * 45 / 1000;
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                data[i] = Mathf.Sin(2f * Mathf.PI * 1400f * t) * Mathf.Exp(-t * 90f) * 0.8f;
            }
            var clip = AudioClip.Create("PointerClick", samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
