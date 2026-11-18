// Written with Claude Code (AI), directed and tested by team MARSXR
using System.IO;
using System.Linq;
using UnityEngine;

namespace Walkback.EditorTools
{
    /// <summary>v1.1 test checks: the picked stops are kept in my_stops.json between sessions.</summary>
    public static partial class AutoTest
    {
        static void CheckUserStopsFile(WalkbackApp app, MissionPanel panel)
        {
            SiteData site = app.terrain.Site;
            StopInfo[] original = site.Stops;
            string folder = Path.Combine(Path.GetTempPath(), "walkback-mystops-test");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            string file = Path.Combine(folder, UserStopsFile.FileName);

            app.UserStopsFolder = null;
            Check("My stops file: outside Play, with no folder set, nothing is written", app.RestoreUserStops() == 0);

            app.UserStopsFolder = folder;
            Vector2 a, b;
            FindSpot(site, app.terrain.Slope, true, out a);
            int first = app.AddUserStop(a);
            FindSpot(site, app.terrain.Slope, true, out b);
            int second = app.AddUserStop(b);
            app.ChangeUserStopMinutes(second, 20f);
            var written = File.Exists(file) ? JsonUtility.FromJson<UserStopsFile.Contents>(File.ReadAllText(file)) : null;
            Check("My stops file: every change writes my_stops.json with the site's name, each stop's place and its minutes",
                first >= 0 && second >= 0 && written != null && written.site == UserStopsFile.SiteKey(site) && written.stops.Length == 2 &&
                written.stops.Any(s => Mathf.Approximately(s.minutes, 20f)) && written.stops.All(s => s.id.StartsWith(UserStops.IdPrefix)),
                written != null ? written.stops.Length + " stops for " + written.site : "no file");

            // As after closing and opening the app: the data's stops, then the file.
            site.Stops = original;
            int back = app.RestoreUserStops();
            var picked = site.Stops.Where(UserStops.IsUser).ToArray();
            Check("My stops file: at start they come back, in the same places, with their minutes",
                back == 2 && picked.Length == 2 &&
                picked.Any(s => Vector2.Distance(new Vector2(s.x, s.y), a) < 0.01f && Mathf.Approximately(UserStops.Minutes(s), 0f)) &&
                picked.Any(s => Vector2.Distance(new Vector2(s.x, s.y), b) < 0.01f && Mathf.Approximately(UserStops.Minutes(s), 20f)));

            // Another site's file, or a stop the rules refuse, is left out.
            site.Stops = original;
            var other = UserStopsFile.From(site);
            other.site = "Somewhere else";
            other.stops = new[] { new UserStopsFile.Saved { id = "user-1", name = "My stop 1", x = a.x, y = a.y } };
            File.WriteAllText(file, JsonUtility.ToJson(other));
            bool otherSite = app.RestoreUserStops() == 0 && site.Stops.Length == original.Length;
            other.site = UserStopsFile.SiteKey(site);
            other.stops = new[]
            {
                new UserStopsFile.Saved { id = "user-1", name = "My stop 1", x = site.Habitat.x + 2f, y = site.Habitat.y },
                new UserStopsFile.Saved { id = "user-2", name = "My stop 2", x = -50f, y = 10f },
                new UserStopsFile.Saved { id = "user-3", name = "My stop 3", x = a.x, y = a.y },
            };
            File.WriteAllText(file, JsonUtility.ToJson(other));
            bool ruled = app.RestoreUserStops() == 1 && site.Stops.Count(UserStops.IsUser) == 1;
            File.WriteAllText(file, "not json");
            site.Stops = original;
            bool broken = app.RestoreUserStops() == 0 && site.Stops.Length == original.Length;
            Check("My stops file: a file of another site or an unreadable one gives none; stops on the habitat or outside the map are left out",
                otherSite && ruled && broken);

            // CLEAR writes an empty list.
            site.Stops = original;
            app.ShowRouteStartState(false);
            panel.BuildStopButtons(site.Stops);
            app.AddUserStop(a);
            app.ClearUserStops();
            written = JsonUtility.FromJson<UserStopsFile.Contents>(File.ReadAllText(file));
            Check("My stops file: CLEAR leaves the file with no stops", written != null && written.stops.Length == 0);

            app.UserStopsFolder = null;
            Directory.Delete(folder, true);
            site.Stops = original;
            app.ShowRouteStartState(false);
            panel.BuildStopButtons(site.Stops);
            app.PlanMission(false);
        }
    }
}
