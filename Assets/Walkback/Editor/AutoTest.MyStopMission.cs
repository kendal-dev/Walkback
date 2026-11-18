// Written with Claude Code (AI), directed and tested by team MARSXR
using System.Collections.Generic;
using UnityEngine;

namespace Walkback.EditorTools
{
    /// <summary>v1.1 test checks: a mission with a picked stop, end to end, and the picked stop and the point of no return on the visor's and the wrist's maps.</summary>
    public static partial class AutoTest
    {
        static Color MarkColor(Transform mark)
        {
            var r = mark != null ? mark.GetComponent<Renderer>() : null;
            if (r == null || !r.HasPropertyBlock()) return Color.clear;
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            return block.GetColor("_BaseColor");
        }

        static void CheckMissionWithMyStop(WalkbackApp app, MissionPanel panel)
        {
            MissionMode mission = app.mission;
            SiteData site = app.terrain.Site;
            if (mission == null || app.hud == null || app.hud.miniMap == null || app.wristMap == null)
            {
                Note("Mission with my stop: the mission, the visor or the wrist map is not in the scene, not checked");
                return;
            }
            StopInfo[] original = site.Stops;
            var cam = app.desktopCamera;
            Vector3 homePosition = cam.transform.position;
            Quaternion homeRotation = cam.transform.rotation;
            Color background = cam.backgroundColor;
            float farPlane = cam.farClipPlane;
            MapLayer layerBefore = app.terrain.Layer;
            bool tableWas = app.table.gameObject.activeSelf, mapWas = app.mapRoot.gameObject.activeSelf, panelWas = app.panel.gameObject.activeSelf;

            app.SetWhatIfTank(100f);
            Vector2 spot;
            FindSpot(site, app.terrain.Slope, true, out spot);
            int index = app.AddUserStop(spot);
            app.ChangeUserStopMinutes(index, 10f);
            MissionFallback.Plan plan = app.Plan;

            // The whole mission by jumps: every leg, the picked stop among them.
            bool entered = mission.NextMode(cam.transform) && mission.Current == MissionMode.Mode.Mars;
            int jumps = 0;
            var reached = new List<string>();
            bool sawMine = false, blueOnVisor = false;
            mission.Tick(cam.transform, 0.3f);
            while (jumps < 40 && mission.Jump(cam.transform))
            {
                jumps++;
                mission.Tick(cam.transform, 0.3f);
                reached.Add(mission.Last.nextName);
                if (mission.Last.nextStop == index)
                {
                    sawMine = true;
                    Transform dot = index < app.hud.miniMap.StopDots.Count ? app.hud.miniMap.StopDots[index] : null;
                    blueOnVisor = dot != null && MarkColor(dot) == StopPillars.UserStopColor;
                }
            }
            mission.Switch(MissionMode.Mode.Table, cam.transform);
            Check("Mission with my stop: START MISSION walks every leg by jumps, the picked stop among them",
                entered && plan != null && plan.HasRoute && jumps == plan.Legs.Count && sawMine,
                jumps + " jumps for " + (plan != null ? plan.Legs.Count : 0) + " legs: " + string.Join(", ", reached.ToArray()));
            Check("Mission with my stop: on the visor's map the picked stop is blue, the data's stops keep the amber",
                blueOnVisor && MarkColor(app.hud.miniMap.StopDots[index == 0 ? 1 : 0]) == Color.clear);

            app.wristMap.Prepare(app, site.Habitat, "Habitat");
            Check("Mission with my stop: on the wrist map the picked stop's pin is blue too",
                index < app.wristMap.StopPins.Count && MarkColor(app.wristMap.StopPins[index]) == StopPillars.UserStopColor &&
                MarkColor(app.wristMap.StopPins[index == 0 ? 1 : 0]) == Color.clear);

            // The point of no return on both maps (tank at 8 %).
            app.SetWhatIfTank(8f);
            app.hud.miniMap.Show(app, site.Habitat, app.TurnBack.point);
            app.wristMap.Prepare(app, site.Habitat, "Habitat");
            Transform redDot = app.hud.miniMap.TurnBackDot, redPin = app.wristMap.TurnBackPin;
            Check("Maps: at 8 % the point of no return is a red dot on the visor's map and a red pin on the wrist map",
                app.TurnBack.found && redDot != null && redDot.gameObject.activeSelf && MarkColor(redDot) == StopPillars.TurnBackColor &&
                redPin != null && redPin.gameObject.activeSelf && MarkColor(redPin) == StopPillars.TurnBackColor);
            app.SetWhatIfTank(100f);
            app.hud.miniMap.Show(app, site.Habitat, site.Habitat);
            app.wristMap.Prepare(app, site.Habitat, "Habitat");
            Check("Maps: with a full tank no red mark", !app.hud.miniMap.TurnBackDot.gameObject.activeSelf && !app.wristMap.TurnBackPin.gameObject.activeSelf);

            // Back as it was.
            site.Stops = original;
            app.ShowRouteStartState(false);
            panel.BuildStopButtons(site.Stops);
            app.PlanMission(false);
            app.SelectStop(-1);
            app.hud.Hide();
            app.wristMap.SetVisible(false);
            app.terrain.ShowLayer(layerBefore);
            app.table.gameObject.SetActive(tableWas);
            app.mapRoot.gameObject.SetActive(mapWas);
            app.panel.gameObject.SetActive(panelWas);
            cam.transform.SetPositionAndRotation(homePosition, homeRotation);
            cam.backgroundColor = background;
            cam.farClipPlane = farPlane;
        }
    }
}
