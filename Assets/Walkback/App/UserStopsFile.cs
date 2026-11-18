// Written with Claude Code (AI), directed and tested by team MARSXR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Walkback
{
    /// <summary>
    /// v1.1: the astronaut's picked stops survive closing the app. They are written to my_stops.json in the
    /// app's data folder after every change, with the site's name, and read back at start for the same site
    /// only. Each one read goes through the same rules as a tap (inside the map, not on a pillar, at most four).
    /// </summary>
    public static class UserStopsFile
    {
        public const string FileName = "my_stops.json";
        public const int Version = 1;

        [Serializable] public class Saved
        {
            public string id;
            public string name;
            public float x;
            public float y;
            public float minutes;
        }

        [Serializable] public class Contents
        {
            public int version = Version;
            public string site = "";
            public Saved[] stops = new Saved[0];
        }

        public static string SiteKey(SiteData site)
        {
            return site != null && site.Info != null && !string.IsNullOrEmpty(site.Info.name) ? site.Info.name : "";
        }

        /// <summary>The picked stops of the site as the file's contents (data stops are not written).</summary>
        public static Contents From(SiteData site)
        {
            var list = new List<Saved>();
            if (site != null && site.Stops != null)
                foreach (StopInfo s in site.Stops)
                    if (UserStops.IsUser(s)) list.Add(new Saved { id = s.id, name = s.name, x = s.x, y = s.y, minutes = UserStops.Minutes(s) });
            return new Contents { site = SiteKey(site), stops = list.ToArray() };
        }

        /// <summary>Writes the file (an empty list when there are none). Returns false and the reason if it could not.</summary>
        public static bool Save(string folder, SiteData site, out string why)
        {
            why = null;
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, FileName), JsonUtility.ToJson(From(site), true));
                return true;
            }
            catch (Exception e)
            {
                why = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        /// <summary>The file's stops for this site, or none (no file, another site, another version, or unreadable).</summary>
        public static Saved[] Load(string folder, SiteData site, out string why)
        {
            why = null;
            try
            {
                string path = Path.Combine(folder, FileName);
                if (!File.Exists(path)) { why = "no file"; return new Saved[0]; }
                var contents = JsonUtility.FromJson<Contents>(File.ReadAllText(path));
                if (contents == null || contents.stops == null) { why = "unreadable"; return new Saved[0]; }
                if (contents.version != Version) { why = "version " + contents.version; return new Saved[0]; }
                if (contents.site != SiteKey(site)) { why = "made for another site (" + contents.site + ")"; return new Saved[0]; }
                return contents.stops;
            }
            catch (Exception e)
            {
                why = e.GetType().Name;
                return new Saved[0];
            }
        }
    }
}
