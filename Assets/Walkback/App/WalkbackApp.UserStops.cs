// Written with Claude Code (AI), directed and tested by team MARSXR
using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Walkback
{
    /// <summary>
    /// v1.1: the astronaut taps the map to add a stop of their own; the route, the verdict and the charts are
    /// planned again at once (plan B over the relief, labelled ESTIMATE). CLEAR on the side board, or C on
    /// the keyboard, takes the picked stops away and the data's plan comes back.
    /// </summary>
    public partial class WalkbackApp
    {
        /// <summary>Raised after a stop was added or the picked stops were cleared (the plan is already new).</summary>
        public event Action UserStopsChanged;

        public int UserStopCount { get { return terrain != null && terrain.Site != null ? UserStops.Count(terrain.Site.Stops) : 0; } }

        /// <summary>Stops can change only at the table, with the map still and not inside a 360° photo.</summary>
        public bool CanEditStops
        {
            get { return terrain != null && terrain.Site != null && !MapControlsLocked && !MissionActive && (siteJump == null || !siteJump.Inside); }
        }

        /// <summary>A point on the terrain (room space) in the map's local meters (x east, y north).</summary>
        public Vector2 WorldToMapMeters(Vector3 world)
        {
            Vector3 local = terrain.transform.InverseTransformPoint(world);
            float half = terrain.Site.SideMeters / 2f;
            return new Vector2(local.x + half, local.z + half);
        }

        /// <summary>
        /// Adds a picked stop at that place, where it adds the least distance to the loop, plans the mission
        /// again and selects the new stop. Returns its index, or -1 (the log says why).
        /// </summary>
        public int AddUserStop(Vector2 meters)
        {
            if (!CanEditStops) { WalkbackLog.Info("My stop not added: the map is busy"); return -1; }
            SiteData site = terrain.Site;
            string refusal = UserStops.Refusal(site, meters);
            if (refusal != null) { WalkbackLog.Info("My stop not added: " + refusal); return -1; }
            StopInfo stop = UserStops.Make(UserStops.NextNumber(site.Stops), meters);
            int slot = UserStops.BestSlot(site.Habitat, site.Stops, meters);
            site.Stops = UserStops.Insert(site.Stops, slot, stop);
            ApplyStops();
            WalkbackLog.Info(stop.name + " added at " + meters.x.ToString("0") + ", " + meters.y.ToString("0") + " m, stop " + (slot + 1) + " of " + site.Stops.Length);
            SelectStop(slot);
            return slot;
        }

        /// <summary>Takes every picked stop away; the data's stops and their plan come back.</summary>
        public void ClearUserStops()
        {
            if (!CanEditStops || UserStopCount == 0) return;
            string selectedId = SelectedStop >= 0 && SelectedStop < terrain.Site.Stops.Length ? terrain.Site.Stops[SelectedStop].id : null;
            terrain.Site.Stops = UserStops.Without(terrain.Site.Stops);
            ApplyStops();
            WalkbackLog.Info("My stops cleared");
            SelectStop(UserStops.IndexOf(terrain.Site.Stops, selectedId));
        }

        /// <summary>
        /// The -5 / +5 buttons of a picked stop's card: changes its minutes of work and plans again (time, oxygen,
        /// verdict). Data stops keep their tasks. Returns the minutes now set, or -1 if nothing changed.
        /// </summary>
        public float ChangeUserStopMinutes(int index, float delta)
        {
            if (!CanEditStops || index < 0 || index >= terrain.Site.Stops.Length) return -1f;
            StopInfo stop = terrain.Site.Stops[index];
            if (!UserStops.IsUser(stop)) return -1f;
            float was = UserStops.Minutes(stop);
            UserStops.SetMinutes(stop, was + delta);
            float now = UserStops.Minutes(stop);
            if (Mathf.Approximately(now, was)) return -1f;
            PlanMission(false);
            WalkbackLog.Info(stop.name + ": work " + was.ToString("0") + " -> " + now.ToString("0") + " min (your estimate)");
            SelectStop(index);
            return now;
        }

        void ApplyStops()
        {
            if (PreviewedStop >= 0) EndPreview(PreviewedStop);
            ShowRouteStartState(false);
            if (panel != null) panel.BuildStopButtons(terrain.Site.Stops);
            PlanMission(true);
            if (UserStopsChanged != null) UserStopsChanged();
        }

        // ---------------------------------------------------------------- the tap on the map

        bool tapWasPressed, tapArmed;
        float tapStartedAt;
        Vector3 tapPoint;

        /// <summary>The pointer is on the bare ground of the map (not on a pillar, a button or the globe).</summary>
        bool PointerOnGround()
        {
            return pointer != null && pointer.HasHit && pointer.Hovered == null && pointer.HitCollider != null &&
                   terrain != null && pointer.HitCollider.gameObject == terrain.gameObject;
        }

        /// <summary>
        /// A short press on the ground that does not move is a tap: it adds a stop there. A longer press, a
        /// press that moves, or one while the map is dragged (grip, two hands) is not.
        /// </summary>
        public void TickMapTap()
        {
            if (pointer == null) return;
            bool pressed = pointer.IsPressed;
            bool dragging = tableControls != null && tableControls.Dragging;
            if (pressed && !tapWasPressed)
            {
                tapArmed = PointerOnGround() && CanEditStops && !dragging;
                tapStartedAt = Time.unscaledTime;
                tapPoint = pointer.HitPoint;
            }
            else if (pressed && tapArmed)
            {
                if (!PointerOnGround() || dragging || Vector3.Distance(pointer.HitPoint, tapPoint) > UserStops.TapMoveMeters) tapArmed = false;
            }
            else if (!pressed && tapWasPressed && tapArmed)
            {
                tapArmed = false;
                if (Time.unscaledTime - tapStartedAt <= UserStops.TapSeconds) AddUserStop(WorldToMapMeters(tapPoint));
            }
            tapWasPressed = pressed;

            var keyboard = Keyboard.current;
            if (DesktopMode && keyboard != null && keyboard.cKey.wasPressedThisFrame) ClearUserStops();
        }
    }
}
