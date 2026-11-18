// Written with Claude Code (AI), directed and tested by team MARSXR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Walkback
{
    /// <summary>
    /// Mission mode (guion §22.9): the same plan, walked. Three steps on one button:
    ///   Table (the room and the map on the table)
    ///   -> Mars (VR): Mars all around at real scale 1:1 (the terrain's own mesh, not scaled, with the
    ///      HiRISE photo), the route on the ground, a pillar of light at the next stop, the helmet HUD
    ///      and the wrist map;
    ///   -> Room (MR): Mars goes dark but the HUD, the route and the pillar stay on the real floor;
    ///   -> Table again.
    /// Rules it keeps:
    ///   - Nothing moves by itself. The position comes from the headset; one real step is one meter
    ///     of Mars. The only thing that follows the head is the height of the ground: the real floor
    ///     is flat and Mars is not, so Mars slides up or down to keep its ground under the feet
    ///     (followGround). Horizontally Mars never moves between jumps.
    ///   - A jump (MissionProgress.JumpNext) puts the next point of the route under the user's feet,
    ///     with the stop straight ahead of where they look, and counts as walked.
    ///   - The room and Mars never change in one frame: a dark curtain closes, the world changes
    ///     behind it, the curtain opens (about 1 s in all).
    ///   - Every number on the visor comes from MissionProgress (the plan and the return layer).
    /// All methods take the viewer's head, so the automatic test drives them with the desktop camera.
    /// </summary>
    public class MissionMode : MonoBehaviour
    {
        public enum Mode { Table, Mars, Room }

        /// <summary>Seconds for the whole change between the room and Mars (curtain closes, then opens).</summary>
        public const float FadeSeconds = 1.0f;
        /// <summary>Seconds for a jump on Mars (shorter curtain).</summary>
        public const float JumpFadeSeconds = 0.5f;
        /// <summary>The route on the ground at real scale.</summary>
        public const float RouteWidthMeters = 0.4f;
        public const float RouteLiftMeters = 0.05f;
        public const float RouteStepMeters = 1f;
        public const float PillarHeightMeters = 3f;
        public const float PillarWidthMeters = 0.25f;
        /// <summary>The square kilometer must not be cut by the camera's far plane.</summary>
        public const float FarClipMeters = 3000f;
        public const float HudRefreshSeconds = 0.25f;
        /// <summary>Side of the free-space border drawn on the floor on Mars.</summary>
        public const float FreeSpaceMeters = 3f;
        /// <summary>Desktop test only: arrow keys walk and turn the camera (there is no headset to move).</summary>
        public const float DesktopWalkMetersPerSecond = 1.2f;
        public const float DesktopTurnDegreesPerSecond = 70f;
        /// <summary>The sky on Mars is one plain colour: there is no sky photo in the site package.</summary>
        public static readonly Color SkyColor = new Color(0.80f, 0.62f, 0.45f, 1f);

        public WalkbackApp app;
        /// <summary>Everything at real scale, in the terrain mesh's meters (x east, y up, z north, origin at the site's center).</summary>
        public Transform mars;
        public MeshFilter terrainCopy;
        public MeshRenderer terrainRenderer;
        public MeshFilter routeFilter;
        public MeshRenderer routeRenderer;
        public Transform pillar;
        public MeshRenderer pillarRenderer;
        public GlobeLabel pillarLabel;
        /// <summary>The stop's card, shown on arrival.</summary>
        public GlobeLabel stopCard;
        /// <summary>A square on the floor marking the free space (Mars only).</summary>
        public Transform border;
        /// <summary>The dark shell around the head for the fades (may be null: then changes are immediate).</summary>
        public Renderer curtain;
        /// <summary>
        /// Two signs low in front of the user while the mission shows, pressed with the pointer (hands or
        /// controllers): the way on and the way out when the menu button or the grip do not reach the app.
        /// </summary>
        public Transform controls;
        public PointerButton nextStopButton;
        public PointerButton nextViewButton;
        /// <summary>RECENTER under the two signs: what B or Y does, for hands (they have no B or Y).</summary>
        public PointerButton recenterButton;
        public bool showFreeSpaceBorder = true;
        /// <summary>Keep Mars' ground under the feet while walking (the real floor is flat).</summary>
        public bool followGround = true;
        /// <summary>
        /// Back at the table after walking on Mars, the table comes in front of the user (as RECENTER does) if
        /// they ended more than TableComesMeters from where the mission began. Without it the table stays where
        /// it was: after the walk to a stop that is around the user, or behind them.
        /// </summary>
        public bool tableComesToUser = true;
        public const float TableComesMeters = 0.5f;

        public Mode Current { get; private set; }
        public MissionProgress Progress { get; private set; }
        public MissionProgress.Snapshot Last { get; private set; }
        public bool Fading { get; private set; }
        public float CurtainAlpha { get; private set; }
        public bool CardShown { get; private set; }
        /// <summary>Which way the user looks on Mars, in degrees clockwise from north (for the arrows on the maps).</summary>
        public float HeadingDegrees { get; private set; }
        /// <summary>True in the room: the route and the pillar lie on the flat real floor instead of the relief.</summary>
        public bool Flat { get { return Current == Mode.Room; } }

        Mesh routeMesh;
        MaterialPropertyBlock block;
        bool tableWas, mapWas, panelWas, lockWas;
        Camera[] cameras;
        Color[] cameraBackgrounds;
        float[] cameraFarPlanes;
        Vector3 desktopPosition;
        Quaternion desktopRotation;
        bool desktopSaved;
        float refreshIn;
        /// <summary>Where the head was when the mission began (the table was set in front of that place).</summary>
        Vector3 enteredAt;

        /// <summary>Called by the app once the mode (headset or desktop) is known.</summary>
        public void Begin(WalkbackApp owner)
        {
            app = owner;
            HideAll();
            Current = Mode.Table;
        }

        void OnDestroy()
        {
            DestroySafe(routeMesh);
        }

        // ---------------------------------------------------------------- the three modes

        /// <summary>Why the mission cannot start now, or null when it can.</summary>
        public string WhyNot()
        {
            if (app == null || app.terrain == null || app.terrain.Site == null) return "no site";
            // v1.1: a plan still being made on a worker (a stop just added) is finished first.
            app.FinishPendingPlan();
            if (app.mapRoot == null || mars == null || terrainCopy == null || routeFilter == null) return "the mission objects are not in the scene";
            if (app.Plan == null || !app.Plan.HasRoute || app.Plan.Legs.Count == 0) return "no planned route";
            if (app.MapControlsLocked) return "the map is busy (opening, or inside a photo)";
            return null;
        }

        /// <summary>The mode the button leads to from here.</summary>
        public Mode After(Mode mode)
        {
            if (mode == Mode.Table) return app != null && app.missionWithoutMars ? Mode.Room : Mode.Mars;
            return mode == Mode.Mars ? Mode.Room : Mode.Table;
        }

        /// <summary>The button: Table -> Mars -> Room -> Table. False if it cannot change now.</summary>
        public bool NextMode(Transform head)
        {
            if (Fading || head == null) return false;
            if (Current == Mode.Table)
            {
                string why = WhyNot();
                if (why != null)
                {
                    WalkbackLog.Warn("Mission mode not started: " + why);
                    return false;
                }
            }
            Mode next = After(Current);
            // The curtain is for changes that show or hide Mars; between two views of the room nothing needs hiding.
            bool marsChanges = Current == Mode.Mars || next == Mode.Mars;
            if (marsChanges && curtain != null && Application.isPlaying) StartCoroutine(FadeThen(FadeSeconds, head, () => Switch(next, head)));
            else Switch(next, head);
            return true;
        }

        /// <summary>Changes mode at once (the fade calls it behind the closed curtain).</summary>
        public void Switch(Mode next, Transform head)
        {
            if (next == Current) return;
            if (next == Mode.Table)
            {
                ExitToTable(head);
                return;
            }
            bool entering = Current == Mode.Table;
            if (entering) EnterFromTable(head);
            Current = next;
            ApplyLook();
            if (entering)
            {
                Anchor(head);
                PlaceBorder(head);
            }
            else if (head != null) SetGroundHeight(MetersOf(head.position));
            if (head != null) Refresh(head, MetersOf(head.position));
            if (head != null) ShowHeading(head);
            if (app.hud != null && head != null) app.hud.SnapTo(head);
            ShowControls(head);
            WalkbackLog.Info("Mission mode: " + Current + (Current == Mode.Mars ? " (Mars all around, 1:1)" : " (the room, HUD and route on the real floor)"));
        }

        void EnterFromTable(Transform head)
        {
            tableWas = app.table != null && app.table.gameObject.activeSelf;
            mapWas = app.mapRoot.gameObject.activeSelf;
            panelWas = app.panel != null && app.panel.gameObject.activeSelf;
            lockWas = app.MapControlsLocked;
            enteredAt = head != null ? head.position : Vector3.zero;
            if (app.table != null) app.table.gameObject.SetActive(false);
            app.mapRoot.gameObject.SetActive(false);
            if (app.panel != null) app.panel.gameObject.SetActive(false);
            app.MapControlsLocked = true;

            GaleTerrain terrain = app.terrain;
            Progress = new MissionProgress(terrain.Site, app.Plan, terrain.ReturnMinutes);
            CardShown = false;

            // Mars at 1:1 is the terrain's own mesh and its own photo texture: no copy of either.
            terrainCopy.sharedMesh = terrain.GetComponent<MeshFilter>().sharedMesh;
            Texture paint = terrain.LayerTexture(terrain.HasPhoto ? MapLayer.Photo : MapLayer.Height);
            if (block == null) block = new MaterialPropertyBlock();
            terrainRenderer.GetPropertyBlock(block);
            if (paint != null) block.SetTexture("_BaseMap", paint);
            block.SetColor("_BaseColor", terrain.HasPhoto ? terrain.PhotoTint : Color.white);
            terrainRenderer.SetPropertyBlock(block);

            if (app.DesktopMode && app.desktopCamera != null && Application.isPlaying)
            {
                // Desktop test: the camera looks down at the table; level it to stand on Mars.
                Transform cam = app.desktopCamera.transform;
                desktopPosition = cam.position;
                desktopRotation = cam.rotation;
                desktopSaved = true;
                cam.rotation = Quaternion.LookRotation(FlatForward(cam), Vector3.up);
            }

            mars.gameObject.SetActive(true);
        }

        void ExitToTable(Transform head)
        {
            HideAll();
            SetCameras(false);
            if (app.table != null) app.table.gameObject.SetActive(tableWas);
            app.mapRoot.gameObject.SetActive(mapWas);
            if (app.panel != null) app.panel.gameObject.SetActive(panelWas);
            app.MapControlsLocked = lockWas;
            if (desktopSaved && app.desktopCamera != null)
            {
                app.desktopCamera.transform.SetPositionAndRotation(desktopPosition, desktopRotation);
                desktopSaved = false;
            }
            Current = Mode.Table;
            Progress = null;
            WalkbackLog.Info("Mission mode: back to the table");
            // Headset only: the desktop camera goes back to its own place, and the automatic test calls
            // BringTableToUser itself (its other checks need the table where the scene builder put it).
            if (tableComesToUser && Application.isPlaying && !app.DesktopMode) BringTableToUser(head);
        }

        /// <summary>True when two head positions are more than this many meters apart on the floor.</summary>
        public static bool MovedAway(Vector3 from, Vector3 to, float meters)
        {
            return new Vector2(to.x - from.x, to.z - from.z).magnitude > meters;
        }

        /// <summary>
        /// Brings the table setup in front of the user, facing them, if they are no longer where the mission
        /// began. The same move as RECENTER at the table (TableControls.Recenter). False if nothing moved.
        /// </summary>
        public bool BringTableToUser(Transform head)
        {
            if (head == null || app == null || app.tableControls == null || Current != Mode.Table) return false;
            if (!MovedAway(enteredAt, head.position, TableComesMeters)) return false;
            bool moved = app.tableControls.Recenter(head.position, head.forward);
            if (moved) WalkbackLog.Info("Mission mode: the table came to the user (they ended " +
                new Vector2(head.position.x - enteredAt.x, head.position.z - enteredAt.z).magnitude.ToString("F1") + " m from where the mission began)");
            return moved;
        }

        void HideAll()
        {
            if (mars != null) mars.gameObject.SetActive(false);
            if (border != null) border.gameObject.SetActive(false);
            if (stopCard != null) stopCard.gameObject.SetActive(false);
            if (curtain != null) curtain.gameObject.SetActive(false);
            if (controls != null) controls.gameObject.SetActive(false);
            CurtainAlpha = 0f;
            CardShown = false;
            if (app != null && app.hud != null) app.hud.Hide();
            if (app != null && app.wristMap != null)
            {
                // A map left in the air belongs to the mission it was placed in: next time it starts on the wrist.
                app.wristMap.ReturnToWrist();
                app.wristMap.SetVisible(false);
            }
        }

        /// <summary>What shows in the current mode: Mars or the room behind, relief or flat floor.</summary>
        void ApplyLook()
        {
            bool onMars = Current == Mode.Mars;
            terrainRenderer.enabled = onMars;
            SetCameras(onMars);
            if (border != null) border.gameObject.SetActive(onMars && showFreeSpaceBorder);
            BuildRoute();
            PlacePillar();
        }

        /// <summary>On Mars the cameras draw an opaque sky and see the whole kilometer; in the room they go back as they were.</summary>
        void SetCameras(bool onMars)
        {
            if (cameras == null)
            {
                var list = new List<Camera>();
                if (app.cameraRig != null) list.AddRange(app.cameraRig.GetComponentsInChildren<Camera>(true));
                if (app.desktopCamera != null) list.Add(app.desktopCamera);
                cameras = list.ToArray();
                cameraBackgrounds = new Color[cameras.Length];
                cameraFarPlanes = new float[cameras.Length];
                for (int i = 0; i < cameras.Length; i++)
                {
                    cameraBackgrounds[i] = cameras[i].backgroundColor;
                    cameraFarPlanes[i] = cameras[i].farClipPlane;
                }
            }
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] == null) continue;
                cameras[i].backgroundColor = onMars ? SkyColor : cameraBackgrounds[i];
                cameras[i].farClipPlane = onMars ? Mathf.Max(cameraFarPlanes[i], FarClipMeters) : cameraFarPlanes[i];
            }
        }

        // ---------------------------------------------------------------- where Mars stands in the room

        /// <summary>Height of the real floor in the world (the rig's origin is on the floor).</summary>
        public float FloorY { get { return app != null && app.cameraRig != null ? app.cameraRig.transform.position.y : 0f; } }

        /// <summary>
        /// Puts the progress point of the route under the head, with the current stop straight ahead of
        /// where the head looks. Called on entering, on every jump and on recenter: never while walking.
        /// </summary>
        public void Anchor(Transform head)
        {
            if (Progress == null || head == null) return;
            SiteData site = app.terrain.Site;
            Vector2 point = Progress.PointAt(Progress.RouteMeters);
            Vector3 feet = new Vector3(head.position.x, FloorY, head.position.z);
            Vector3 position;
            Quaternion rotation;
            AnchorPose(site.SideMeters, point, Progress.DirectionToTarget(), feet, FlatForward(head), GroundAt(point), out position, out rotation);
            mars.SetPositionAndRotation(position, rotation);
        }

        /// <summary>
        /// The pose of the Mars root that puts <paramref name="pointMeters"/> (local meters, at
        /// <paramref name="groundHeight"/> in the mesh) on <paramref name="feet"/>, with
        /// <paramref name="directionMeters"/> (x east, y north) turned to <paramref name="facing"/>.
        /// </summary>
        public static void AnchorPose(float sideMeters, Vector2 pointMeters, Vector2 directionMeters, Vector3 feet, Vector3 facing, float groundHeight,
            out Vector3 position, out Quaternion rotation)
        {
            float half = sideMeters / 2f;
            float yaw = (Mathf.Atan2(facing.x, facing.z) - Mathf.Atan2(directionMeters.x, directionMeters.y)) * Mathf.Rad2Deg;
            rotation = Quaternion.Euler(0f, yaw, 0f);
            position = feet - rotation * new Vector3(pointMeters.x - half, groundHeight, pointMeters.y - half);
        }

        /// <summary>How many times the mission was recentered (B, Y, N or the RECENTER sign).</summary>
        public int Recenters { get; private set; }

        /// <summary>Recenter: the progress point back under the user, the stop ahead of where they look now.</summary>
        public bool Recenter(Transform head)
        {
            if (Current == Mode.Table || Fading || head == null) return false;
            Recenters++;
            Anchor(head);
            PlaceBorder(head);
            PlaceControls(head);
            Refresh(head, MetersOf(head.position));
            WalkbackLog.Info("Mission recentered");
            return true;
        }

        /// <summary>A world position in the site's local meters (x east, y north).</summary>
        public Vector2 MetersOf(Vector3 world)
        {
            float half = app.terrain.Site.SideMeters / 2f;
            Vector3 local = mars.InverseTransformPoint(world);
            return new Vector2(local.x + half, local.z + half);
        }

        /// <summary>A point of the site (local meters) in the Mars root's space, on the ground plus a lift.</summary>
        public Vector3 LocalOf(Vector2 meters, float lift)
        {
            float half = app.terrain.Site.SideMeters / 2f;
            return new Vector3(meters.x - half, GroundAt(meters) + lift, meters.y - half);
        }

        /// <summary>Ground height in the Mars root's space: the relief on Mars, 0 on the flat real floor.</summary>
        public float GroundAt(Vector2 meters)
        {
            return Flat ? 0f : TriangleHeight(app.terrain.Site, meters.x, meters.y);
        }

        /// <summary>
        /// Height above the site's lowest point exactly on the terrain mesh: each grid cell is two
        /// triangles split along the north-west to south-east diagonal, as GaleTerrain builds them.
        /// </summary>
        public static float TriangleHeight(SiteData site, float x, float y)
        {
            int n = site.GridSize;
            float spacing = site.NodeSpacing;
            float gc = Mathf.Clamp(x / spacing, 0f, n - 1);
            float gr = Mathf.Clamp((site.SideMeters - y) / spacing, 0f, n - 1);
            int c0 = Mathf.Min((int)gc, n - 2), r0 = Mathf.Min((int)gr, n - 2);
            float fc = gc - c0, fr = gr - r0;
            float[] h = site.Heights;
            float a = h[r0 * n + c0], b = h[r0 * n + c0 + 1], d = h[(r0 + 1) * n + c0], e = h[(r0 + 1) * n + c0 + 1];
            float height = fc >= fr ? a + (b - a) * fc + (e - b) * fr : a + (d - a) * fr + (e - d) * fc;
            return height - site.MinElevation;
        }

        static Vector3 FlatForward(Transform head)
        {
            Vector3 f = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
        }

        public const float ControlsAheadMeters = 0.7f;
        public const float ControlsBelowEyeMeters = 0.55f;
        /// <summary>The signs come back in front once the user is this far from them (or has turned away).</summary>
        public const float ControlsFollowMeters = 1f;
        /// <summary>
        /// ...and also once the user has walked up to them: closer than this they are under the chin or just
        /// behind. A jump lands 2.5 m from the stop and the card opens at 1.5 m: that 1 m walk ended with the
        /// signs (0.7 m ahead at the start) 0.3 m behind the user, where the old rule left them.
        /// </summary>
        public const float ControlsTooCloseMeters = 0.35f;
        public const string NextStopText = "NEXT STOP";
        public const string RoomViewText = "ROOM VIEW";
        public const string BackToTableText = "BACK TO TABLE";
        public const string RecenterText = "RECENTER";
        /// <summary>What the NEXT STOP sign says once the route has no next point to jump to (the last leg, to the habitat).</summary>
        public const string EndOfRouteText = "END OF ROUTE";

        /// <summary>Shows the two signs for the current mode, in front of the user, and wires what they do.</summary>
        void ShowControls(Transform head)
        {
            if (controls == null) return;
            if (nextStopButton != null && nextStopButton.Clicked == null)
                nextStopButton.Clicked = () => Jump(app.HeadTransform());
            if (nextViewButton != null)
            {
                if (nextViewButton.Clicked == null) nextViewButton.Clicked = () => NextMode(app.HeadTransform());
                // The sign says where the press leads: Mars -> the room -> the table.
                if (nextViewButton.label != null) nextViewButton.label.text = After(Current) == Mode.Table ? BackToTableText : RoomViewText;
            }
            if (recenterButton != null && recenterButton.Clicked == null)
                recenterButton.Clicked = () => Recenter(app.HeadTransform());
            controls.gameObject.SetActive(Current != Mode.Table);
            SyncNextStopSign();
            PlaceControls(head);
        }

        /// <summary>
        /// The first sign says what a press will do: NEXT STOP while the route has a next point, END OF ROUTE
        /// once it has none (it used to keep saying NEXT STOP and do nothing).
        /// </summary>
        void SyncNextStopSign()
        {
            if (nextStopButton == null || nextStopButton.label == null) return;
            string text = Progress != null && !Progress.CanJump ? EndOfRouteText : NextStopText;
            if (nextStopButton.label.text != text) nextStopButton.label.text = text;
        }

        /// <summary>The signs low in front of where the head looks, tilted up to face the eyes.</summary>
        void PlaceControls(Transform head)
        {
            if (controls == null || head == null) return;
            Vector3 at = head.position + FlatForward(head) * ControlsAheadMeters + Vector3.down * ControlsBelowEyeMeters;
            controls.SetPositionAndRotation(at, Quaternion.LookRotation(at - head.position, Vector3.up));
        }

        /// <summary>While walking: the signs stay where they are until the user leaves them behind.</summary>
        void FollowControls(Transform head)
        {
            if (controls == null || !controls.gameObject.activeSelf) return;
            if (ControlsLeftBehind(controls.position - head.position, FlatForward(head))) PlaceControls(head);
        }

        /// <summary>
        /// True when the signs must come back in front: too far, walked up to (or past), or behind the user.
        /// <paramref name="toSigns"/> goes from the head to the signs; only its horizontal part counts.
        /// </summary>
        public static bool ControlsLeftBehind(Vector3 toSigns, Vector3 flatForward)
        {
            toSigns.y = 0f;
            float meters = toSigns.magnitude;
            if (meters > ControlsFollowMeters || meters < ControlsTooCloseMeters) return true;
            return Vector3.Dot(toSigns / meters, flatForward) < -0.2f;
        }

        void PlaceBorder(Transform head)
        {
            if (border == null || head == null) return;
            border.SetPositionAndRotation(new Vector3(head.position.x, FloorY, head.position.z), Quaternion.LookRotation(FlatForward(head), Vector3.up));
        }

        // ---------------------------------------------------------------- per frame

        /// <summary>Once per frame while the mission is on: the headset's position drives everything.</summary>
        public void Tick(Transform head, float deltaTime)
        {
            if (Current == Mode.Table || Progress == null || head == null) return;
            if (app.DesktopMode && Application.isPlaying && !Fading) DesktopWalk(deltaTime);
            if (curtain != null && curtain.gameObject.activeSelf) curtain.transform.position = head.position;

            Vector2 here = MetersOf(head.position);
            Progress.Walk(here);
            if (followGround) SetGroundHeight(here);
            ShowHeading(head);
            FollowControls(head);

            refreshIn -= deltaTime;
            if (refreshIn <= 0f) Refresh(head, here);
            if (app.hud != null) app.hud.Follow(head, deltaTime);
            // Every frame, so a press of X (keep the wrist map) is never missed.
            if (app.wristMap != null && Application.isPlaying) app.HoldWristMap(head);
            if (pillarLabel != null) pillarLabel.Face(head.position);
        }

        /// <summary>The way the head looks, as a direction on Mars, goes to the "you" arrows of the mini map and the wrist map.</summary>
        void ShowHeading(Transform head)
        {
            Vector3 onMars = mars.InverseTransformDirection(FlatForward(head));
            HeadingDegrees = Mathf.Repeat(Mathf.Atan2(onMars.x, onMars.z) * Mathf.Rad2Deg, 360f);
            if (app.hud != null && app.hud.miniMap != null) app.hud.miniMap.SetHeading(true, HeadingDegrees);
            if (app.wristMap != null) app.wristMap.SetHeading(true, HeadingDegrees);

            // The compass and the arrow to the next stop: Mars is anchored, so the bearing from here is known.
            HasBearing = false;
            if (Progress != null && Progress.Valid)
            {
                Vector2 here = MetersOf(head.position), target = Progress.LegTarget(Progress.Leg);
                HasBearing = Vector2.Distance(here, target) > HelmetHud.ArrowHideMeters;
                if (HasBearing) BearingToNext = HudCompass.Bearing(here, target);
            }
            if (app.hud != null) app.hud.SetHeading(true, HeadingDegrees, HasBearing, BearingToNext);
        }

        /// <summary>Bearing from the user straight to the next stop, degrees clockwise from north (while HasBearing).</summary>
        public float BearingToNext { get; private set; }
        /// <summary>False within a meter of the stop: there the arrow and the tape's bar hide.</summary>
        public bool HasBearing { get; private set; }

        /// <summary>
        /// Only the height follows the user: Mars slides up or down so that its ground at
        /// <paramref name="here"/> lies on the real floor. In the room the route is flat, so nothing moves.
        /// </summary>
        void SetGroundHeight(Vector2 here)
        {
            Vector3 p = mars.position;
            mars.position = new Vector3(p.x, FloorY - GroundAt(here), p.z);
        }

        /// <summary>Reads the visor's numbers and puts them on the HUD, the wrist, the pillar and the card.</summary>
        void Refresh(Transform head, Vector2 here)
        {
            refreshIn = HudRefreshSeconds;
            if (Progress == null) return;
            Last = Progress.Read(here);
            SiteData site = app.terrain.Site;
            if (app.hud != null) app.hud.ShowLive(Last, site);
            if (app.wristMap != null && Application.isPlaying) app.wristMap.Prepare(app, here, "on the route");
            PlacePillar();
            if (pillarLabel != null) pillarLabel.Face(head.position);
            if (Last.arrived && !CardShown) ShowCard(head);
            SyncNextStopSign();
        }

        void DesktopWalk(float deltaTime)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || app.desktopCamera == null) return;
            Transform cam = app.desktopCamera.transform;
            float turn = (keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.leftArrowKey.isPressed ? 1f : 0f);
            float move = (keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.downArrowKey.isPressed ? 1f : 0f);
            if (turn != 0f) cam.Rotate(0f, turn * DesktopTurnDegreesPerSecond * deltaTime, 0f, Space.World);
            if (move != 0f) cam.position += FlatForward(cam) * (move * DesktopWalkMetersPerSecond * deltaTime);
        }

        // ---------------------------------------------------------------- the jump

        /// <summary>
        /// Jumps to the next point of the route. It counts as walked (MissionProgress.JumpNext); the new
        /// point lands under the user, the stop ahead of where they look. False if there is no next point.
        /// </summary>
        public bool Jump(Transform head)
        {
            if (Current == Mode.Table || Fading || Progress == null || head == null) return false;
            if (!Progress.CanJump)
            {
                WalkbackLog.Info("Mission jump: the route has no next point");
                return false;
            }
            // On Mars the world changes around the user: behind the curtain. In the room only the route and the pillar move.
            if (Current == Mode.Mars && curtain != null && Application.isPlaying) StartCoroutine(FadeThen(JumpFadeSeconds, head, () => DoJump(head)));
            else DoJump(head);
            return true;
        }

        void DoJump(Transform head)
        {
            if (Progress == null || !Progress.JumpNext()) return;
            CardShown = false;
            if (stopCard != null) stopCard.gameObject.SetActive(false);
            Anchor(head);
            PlacePillar();
            PlaceControls(head);
            Refresh(head, MetersOf(head.position));
            WalkbackLog.Info("Mission jump " + Progress.Jumps + ": " + Last.nextName + " " + Last.nextMeters.ToString("F1") + " m ahead, walked so far " +
                Last.walkMinutes.ToString("F1") + " min, O2 used " + Last.usedLiters.ToString("F1") + " L");
        }

        // ---------------------------------------------------------------- the fade

        /// <summary>Opacity of the curtain at time t of a fade (0..1): closes in the first half, opens in the second.</summary>
        public static float CurtainAlphaAt(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? t * 2f : (1f - t) * 2f;
        }

        /// <summary>
        /// Runs a change of world behind the curtain: it closes, <paramref name="change"/> runs, it opens.
        /// For other pieces that swap what surrounds the user (the jump into a stop's 360° photo).
        /// Without a curtain, or outside Play (the automatic test), the change runs at once.
        /// False, and nothing happens, while another fade is still running.
        /// </summary>
        public bool BehindCurtain(float seconds, Transform head, System.Action change)
        {
            if (Fading || change == null) return false;
            if (curtain == null || head == null || !Application.isPlaying)
            {
                RunChange(change);
                return true;
            }
            StartCoroutine(FadeThen(seconds, head, change));
            return true;
        }

        /// <summary>How many changes behind the curtain threw an error (each one is in the log).</summary>
        public int FailedChanges { get; private set; }

        /// <summary>
        /// Runs the change of world and keeps going if it throws: an error inside the fade would otherwise
        /// stop it with the curtain closed (a black headset for good, and no later fade could start).
        /// </summary>
        void RunChange(System.Action change)
        {
            try
            {
                change();
            }
            catch (System.Exception e)
            {
                FailedChanges++;
                WalkbackLog.Warn("A change behind the fade curtain failed, the curtain opens anyway: " + e);
            }
        }

        IEnumerator FadeThen(float seconds, Transform head, System.Action change)
        {
            Fading = true;
            curtain.gameObject.SetActive(true);
            bool changed = false;
            float start = Time.unscaledTime;
            while (true)
            {
                float t = seconds > 0f ? (Time.unscaledTime - start) / seconds : 1f;
                if (t >= 0.5f && !changed)
                {
                    // The curtain is closed for this frame: change the world behind it.
                    SetCurtain(1f, head);
                    changed = true;
                    RunChange(change);
                    yield return null;
                    continue;
                }
                if (t >= 1f) break;
                SetCurtain(CurtainAlphaAt(t), head);
                yield return null;
            }
            SetCurtain(0f, head);
            curtain.gameObject.SetActive(false);
            Fading = false;
        }

        void SetCurtain(float alpha, Transform head)
        {
            CurtainAlpha = alpha;
            if (curtain == null) return;
            if (head != null) curtain.transform.position = head.position;
            if (block == null) block = new MaterialPropertyBlock();
            curtain.GetPropertyBlock(block);
            block.SetFloat("_Alpha", alpha);
            curtain.SetPropertyBlock(block);
        }

        // ---------------------------------------------------------------- route, pillar and card at real scale

        /// <summary>The whole route as a ribbon on the ground (on the relief on Mars, flat in the room).</summary>
        void BuildRoute()
        {
            SiteData site = app.terrain.Site;
            List<Vector2> points = RouteGeometry.Densify(app.Plan.Path, RouteStepMeters);
            if (routeMesh == null) routeMesh = new Mesh { name = "MissionRoute", hideFlags = HideFlags.DontSave };
            routeMesh.Clear();
            int count = points.Count;
            if (count < 2)
            {
                routeRenderer.gameObject.SetActive(false);
                return;
            }
            var vertices = new Vector3[count * 2];
            var uv = new Vector2[count * 2];
            var normals = new Vector3[count * 2];
            float total = RouteGeometry.Length(points);
            if (total <= 0f) total = 1f;
            float along = 0f, halfWidth = RouteWidthMeters / 2f;
            for (int i = 0; i < count; i++)
            {
                if (i > 0) along += Vector2.Distance(points[i - 1], points[i]);
                Vector2 t = points[Mathf.Min(i + 2, count - 1)] - points[Mathf.Max(i - 2, 0)];
                if (t.sqrMagnitude < 1e-6f) t = Vector2.right;
                t.Normalize();
                Vector2 left = new Vector2(-t.y, t.x);
                vertices[2 * i] = LocalOf(Inside(site, points[i] + left * halfWidth), RouteLiftMeters);
                vertices[2 * i + 1] = LocalOf(Inside(site, points[i] - left * halfWidth), RouteLiftMeters);
                float u = along / total;
                uv[2 * i] = new Vector2(u, 0f);
                uv[2 * i + 1] = new Vector2(u, 1f);
                normals[2 * i] = normals[2 * i + 1] = Vector3.up;
            }
            var triangles = new int[(count - 1) * 6];
            for (int i = 0; i < count - 1; i++)
            {
                int a = 2 * i, b = a + 1, c = a + 2, e = a + 3;
                triangles[6 * i] = a; triangles[6 * i + 1] = c; triangles[6 * i + 2] = b;
                triangles[6 * i + 3] = b; triangles[6 * i + 4] = c; triangles[6 * i + 5] = e;
            }
            routeMesh.indexFormat = vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            routeMesh.vertices = vertices;
            routeMesh.uv = uv;
            routeMesh.normals = normals;
            routeMesh.triangles = triangles;
            routeMesh.RecalculateBounds();
            routeFilter.sharedMesh = routeMesh;
            routeRenderer.gameObject.SetActive(true);
            Paint(routeRenderer, WalkbackTheme.Watch, 0.22f, 0.25f, 0f);
        }

        static Vector2 Inside(SiteData site, Vector2 meters)
        {
            return new Vector2(Mathf.Clamp(meters.x, 0f, site.SideMeters), Mathf.Clamp(meters.y, 0f, site.SideMeters));
        }

        /// <summary>The pillar of light stands on the stop (or the habitat) the current leg ends at.</summary>
        void PlacePillar()
        {
            if (Progress == null || pillar == null || !Progress.Valid) return;
            Vector2 target = Progress.LegTarget(Progress.Leg);
            pillar.localPosition = LocalOf(target, PillarHeightMeters / 2f);
            pillar.gameObject.SetActive(true);
            if (pillarRenderer != null) Paint(pillarRenderer, WalkbackTheme.Watch, 0f, 0.3f, 1f);
            if (pillarLabel == null) return;
            pillarLabel.transform.localPosition = LocalOf(target, PillarHeightMeters + 0.35f);
            pillarLabel.gameObject.SetActive(true);
            SiteData site = app.terrain.Site;
            int stop = app.Plan.Legs[Progress.Leg].ToStop;
            StopInfo info = stop >= 0 && stop < site.Stops.Length ? site.Stops[stop] : null;
            pillarLabel.SetLines(info != null ? info.name : "Habitat", info != null ? info.tag : "Back home");
        }

        void Paint(MeshRenderer target, Color color, float border, float pulse, float vertical)
        {
            if (block == null) block = new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            block.SetColor("_Color", color);
            block.SetFloat("_Progress", 1f);
            block.SetFloat("_DashCount", 0f);
            block.SetFloat("_Border", border);
            block.SetFloat("_Pulse", pulse);
            block.SetFloat("_Vertical", vertical);
            target.SetPropertyBlock(block);
        }

        /// <summary>
        /// On arrival: the stop's card just in front of the pillar, fixed in the world (not on the head), low and
        /// tilted back to face the eyes, like a plaque on a stand. Arrival is at 1.5 m, so it is first read from
        /// about 1.5 m, 24° to 33° below the horizon: under the visor's lower row (its lowest box ends 16° down).
        /// At 1.3 m above the floor it stood 8° to 18° down, across the NEXT card, the arrow, the mini map and the
        /// ESTIMATE line; the visor is 1.2 m away and the card about as far, so each covered part of the other.
        /// </summary>
        public const float CardBelowEyeMeters = 0.70f;
        /// <summary>The card's center never goes lower than this above the real floor (a short user, or a child).</summary>
        public const float CardMinHeightMeters = 0.60f;

        void ShowCard(Transform head)
        {
            CardShown = true;
            if (stopCard == null || pillar == null) return;
            SiteData site = app.terrain.Site;
            int stop = Last.nextStop;
            StopInfo info = stop >= 0 && stop < site.Stops.Length ? site.Stops[stop] : null;
            if (info != null)
            {
                string first, second;
                SplitInTwo(string.IsNullOrEmpty(info.science) ? "No science note for this stop in the data yet." : info.science, out first, out second);
                stopCard.SetLines(info.name, info.tag, first, second,
                    Last.taskMinutesAtNext > 0f ? "Work here: " + HelmetHud.MinutesText(Last.taskMinutesAtNext) : "");
            }
            else
            {
                stopCard.SetLines("HABITAT", "Back home",
                    "Walked " + HelmetHud.MetersText(Last.routeMeters) + " in " + HelmetHud.MinutesText(Last.walkMinutes),
                    "Work at the stops: " + HelmetHud.MinutesText(Last.workMinutes),
                    MissionFallback.Note);
            }
            Vector3 eye = head.position;
            Vector3 foot = new Vector3(pillar.position.x, FloorY, pillar.position.z);
            Vector3 toUser = new Vector3(eye.x - foot.x, 0f, eye.z - foot.z);
            Vector3 toward = toUser.sqrMagnitude > 1e-4f ? toUser.normalized : -FlatForward(head);
            Vector3 at = foot + toward * 0.2f;
            at.y = Mathf.Max(FloorY + CardMinHeightMeters, eye.y - CardBelowEyeMeters);
            stopCard.gameObject.SetActive(true);
            // Square to the line of sight (GlobeLabel.Face turns only about the vertical): its +z points away from the eyes.
            stopCard.transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - eye, Vector3.up));
            // The user has just stopped here: the signs (NEXT STOP is the way on) in front, under the card.
            PlaceControls(head);
            WalkbackLog.Info("Mission arrival: " + (info != null ? info.name : "habitat"));
        }

        /// <summary>Splits a sentence into two lines of similar length at a space (the card's lines do not wrap).</summary>
        public static void SplitInTwo(string text, out string first, out string second)
        {
            first = text;
            second = "";
            if (string.IsNullOrEmpty(text) || text.Length < 48) return;
            int middle = text.Length / 2, best = -1;
            for (int i = 0; i < text.Length; i++)
                if (text[i] == ' ' && (best < 0 || Mathf.Abs(i - middle) < Mathf.Abs(best - middle))) best = i;
            if (best <= 0) return;
            first = text.Substring(0, best);
            second = text.Substring(best + 1);
        }

        static void DestroySafe(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
