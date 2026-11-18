// Written with Claude Code (AI), directed and tested by team MARSXR
using System.IO;
using System.Linq;
using UnityEngine;

namespace Walkback.EditorTools
{
    /// <summary>v1.1 test checks: EARLIER / LATER on a picked stop's card, and the order kept in my_stops.json.</summary>
    public static partial class AutoTest
    {
        static void CheckStopOrder(WalkbackApp app, MissionPanel panel)
        {
            SiteData site = app.terrain.Site;
            StopInfo[] original = site.Stops;
            string folder = Path.Combine(Path.GetTempPath(), "walkback-order-test");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            app.UserStopsFolder = folder;
            app.SetWhatIfTank(100f);

            Vector2 spot;
            FindSpot(site, app.terrain.Slope, true, out spot);
            int index = app.AddUserStop(spot);
            PanelButton earlier = panel.earlierButton, later = panel.laterButton;
            if (index < 0 || earlier == null || later == null || panel.orderText == null)
            {
                Check("Stop order: a picked stop's card has EARLIER / LATER", false, index < 0 ? "no stop" : "not built");
                return;
            }
            Check("Stop order: a picked stop's card shows EARLIER / LATER and its place in the visiting order, where the risk line was",
                earlier.gameObject.activeInHierarchy && later.gameObject.activeInHierarchy && panel.orderText.gameObject.activeInHierarchy &&
                panel.orderText.text == "visit " + (index + 1) + " of " + site.Stops.Length && panel.stopFooter.text == "",
                panel.orderText.text);

            string id = site.Stops[index].id;
            later.OnPointerClick(later.transform.position);
            int moved = UserStops.IndexOf(site.Stops, id);
            bool legFollows = app.Plan != null && app.Plan.Legs.Count > moved && app.Plan.Legs[moved].ToStop == moved && app.Plan.Legs[moved].ToName == site.Stops[moved].name;
            Check("Stop order: LATER visits it one stop later; the plan's legs follow the new order and the stop stays selected",
                moved == index + 1 && legFollows && app.SelectedStop == moved && panel.orderText.text == "visit " + (moved + 1) + " of " + site.Stops.Length,
                "from " + (index + 1) + " to " + (moved + 1));
            earlier.OnPointerClick(earlier.transform.position);
            Check("Stop order: EARLIER takes it back", UserStops.IndexOf(site.Stops, id) == index);

            // To the very start: EARLIER is off there; to the end: LATER is off.
            while (app.MoveUserStop(UserStops.IndexOf(site.Stops, id), -1) >= 0) { }
            bool firstOff = UserStops.IndexOf(site.Stops, id) == 0 && !earlier.Interactable && later.Interactable;
            while (app.MoveUserStop(UserStops.IndexOf(site.Stops, id), 1) >= 0) { }
            int last = site.Stops.Length - 1;
            bool lastOff = UserStops.IndexOf(site.Stops, id) == last && earlier.Interactable && !later.Interactable;
            Check("Stop order: first in the order EARLIER is off, last LATER is off", firstOff && lastOff);
            Check("Stop order: a data stop does not move", app.MoveUserStop(0, 1) < 0 && site.Stops[0].id == original[0].id);

            // The order survives closing the app: last in the order, same id and name.
            string name = site.Stops[last].name;
            site.Stops = original;
            int back = app.RestoreUserStops();
            Check("Stop order: after closing and opening, the picked stop comes back in the order it was left, with its name",
                back == 1 && site.Stops.Length == original.Length + 1 && site.Stops[site.Stops.Length - 1].id == id && site.Stops[site.Stops.Length - 1].name == name,
                string.Join(", ", site.Stops.Select(s => s.name).ToArray()));

            Check("Stop order: a saved order that names other stops is not used",
                UserStopsFile.InOrder(original, new[] { "altadena", "nobody", "x", "y" }) == original &&
                UserStopsFile.InOrder(original, new string[0]) == original);

            app.UserStopsFolder = null;
            Directory.Delete(folder, true);
            site.Stops = original;
            app.ShowRouteStartState(false);
            panel.BuildStopButtons(site.Stops);
            app.PlanMission(false);
            app.SelectStop(-1);
        }
    }
}
