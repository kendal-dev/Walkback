// Written with Claude Code (AI), directed and tested by team MARSXR
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace Walkback.EditorTools
{
    /// <summary>v1.1 test checks: the optional link to a NASA SUITS telemetry stream server (TSS).</summary>
    public static partial class AutoTest
    {
        // Made-up suit values in the two shapes of TSS's EVA.json (2026: two suits; 2027: one suit). Field names from the TSS repositories.
        const string Tss2026Json = "{\"telemetry\":{\"eva1\":{\"oxy_pri_storage\":84.5,\"oxy_sec_storage\":100,\"heart_rate\":92,\"eva_elapsed_time\":600}," +
                                   "\"eva2\":{\"oxy_pri_storage\":40,\"oxy_sec_storage\":90,\"heart_rate\":80,\"eva_elapsed_time\":600}},\"status\":{\"started\":true}}";
        const string Tss2027Json = "{\"telemetry\":{\"primary_battery_level\":100,\"oxy_pri_storage\":61,\"oxy_sec_storage\":100,\"heart_rate\":0,\"eva_elapsed_time\":0},\"status\":{\"started\":false}}";

        static void CheckSuits(WalkbackApp app, MissionPanel panel)
        {
            byte[] request = SuitsTss.Request(0x01020304u, SuitsTss.EvaCommand);
            Check("SUITS: the request is UNIX time and command 1, big-endian, 8 bytes",
                request.Length == 8 && request[0] == 1 && request[3] == 4 && request[4] == 0 && request[7] == 1);

            var withHeader = new byte[8 + Tss2026Json.Length + 3];
            System.Text.Encoding.UTF8.GetBytes(Tss2026Json).CopyTo(withHeader, 8);
            Check("SUITS: the JSON is found with or without the old 8-byte header, trailing zeros dropped",
                SuitsTss.JsonOf(withHeader) == Tss2026Json && SuitsTss.JsonOf(System.Text.Encoding.UTF8.GetBytes(Tss2026Json)) == Tss2026Json &&
                SuitsTss.JsonOf(new byte[] { 1, 0, 0, 0 }) == null);

            SuitsTss.Reading r1, r2, r3, bad;
            bool ok1 = SuitsTss.TryRead(Tss2026Json, 1, out r1);
            bool ok2 = SuitsTss.TryRead(Tss2026Json, 2, out r2);
            bool ok3 = SuitsTss.TryRead(Tss2027Json, 1, out r3);
            Check("SUITS: EVA.json of 2026 (suit 1 and suit 2) and of 2027 (one suit) give the primary oxygen",
                ok1 && Mathf.Approximately(r1.primaryO2, 84.5f) && r1.started && ok2 && Mathf.Approximately(r2.primaryO2, 40f) &&
                ok3 && Mathf.Approximately(r3.primaryO2, 61f) && !r3.started);
            Check("SUITS: an answer without oxygen, or not JSON, gives no reading",
                !SuitsTss.TryRead("{\"telemetry\":{\"heart_rate\":80}}", 1, out bad) && !SuitsTss.TryRead("not json", 1, out bad) && !SuitsTss.TryRead(null, 1, out bad));

            // The settings file: off without it.
            string folder = Path.Combine(Path.GetTempPath(), "walkback-suits-test");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, SuitsTss.ConfigFile);
            if (File.Exists(file)) File.Delete(file);
            bool offWithout = SuitsTss.LoadConfig(folder) == null;
            File.WriteAllText(file, "{\"host\":\"192.168.1.20\",\"eva\":2}");
            SuitsTss.Config loaded = SuitsTss.LoadConfig(folder);
            File.WriteAllText(file, "{\"host\":\"\"}");
            bool offEmpty = SuitsTss.LoadConfig(folder) == null;
            File.Delete(file);
            Check("SUITS: off without suits.json or with no host; the file sets host, suit and port 14141 by default",
                offWithout && offEmpty && loaded != null && loaded.host == "192.168.1.20" && loaded.eva == 2 && loaded.port == SuitsTss.DefaultPort);

            // A stand-in server on this computer answers like TSS.
            string json = null, why = null;
            int port = 0;
            using (var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                port = ((IPEndPoint)server.Client.LocalEndPoint).Port;
                byte[] asked = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        var from = new IPEndPoint(IPAddress.Any, 0);
                        server.Client.ReceiveTimeout = 3000;
                        asked = server.Receive(ref from);
                        byte[] answer = System.Text.Encoding.UTF8.GetBytes(Tss2027Json);
                        server.Send(answer, answer.Length, from);
                    }
                    catch (Exception) { }
                });
                thread.Start();
                json = SuitsTss.Fetch("127.0.0.1", port, 2000, out why);
                thread.Join(3000);
                Check("SUITS: a request over UDP reaches the server as command 1 and its EVA.json comes back",
                    json == Tss2027Json && asked != null && asked.Length == 8 && asked[7] == 1, why ?? "");
            }
            string silent = SuitsTss.Fetch("127.0.0.1", port, 300, out why);
            Check("SUITS: no server gives no reading and a reason, without hanging", silent == null && !string.IsNullOrEmpty(why), why);

            // The plan follows the suit.
            app.SetWhatIfTank(100f);
            app.UseSuitsConfig(new SuitsTss.Config { host = "127.0.0.1", port = port, eva = 1 });
            bool planned = app.ApplySuitsReading(r1, new DateTime(2026, 11, 18, 12, 3, 0, DateTimeKind.Utc), 1000f);
            Canvas.ForceUpdateCanvases();
            Check("SUITS: the first reading starts the plan with the suit's oxygen, and the card says it comes from SUITS TSS (simulated)",
                planned && app.SuitsLive && Mathf.Approximately(app.WhatIfTankPercent, 84.5f) && app.Plan != null && Mathf.Approximately(app.Plan.TankPercent, 84.5f) &&
                panel.verdictNote.text.StartsWith("SUITS TSS suit 1 · primary O2 85 %") && panel.verdictNote.text.Contains("simulated") && panel.verdictNote.text.Contains("ESTIMATE"),
                panel.verdictNote.text);
            Check("SUITS: that note fits the card on one line",
                panel.verdictNote.preferredWidth <= ((RectTransform)panel.verdictNote.transform).rect.width,
                panel.verdictNote.preferredWidth.ToString("0") + " px in " + ((RectTransform)panel.verdictNote.transform).rect.width.ToString("0"));

            var small = r1; small.primaryO2 = 84f;
            var big = r1; big.primaryO2 = 70f;
            bool quietSmall = !app.ApplySuitsReading(small, DateTime.UtcNow, 1003f);
            bool quietSoon = !app.ApplySuitsReading(big, DateTime.UtcNow, 1005f);
            bool later = app.ApplySuitsReading(big, DateTime.UtcNow, 1011f);
            Check("SUITS: the plan is made again only when the oxygen moved 1 % or more, and not within 10 s",
                quietSmall && quietSoon && later && Mathf.Approximately(app.WhatIfTankPercent, 70f));
            app.NextWhatIf();
            Check("SUITS: while the link is live the WHAT IF button does not change the tank", Mathf.Approximately(app.WhatIfTankPercent, 70f));

            // During the mission the plan stays; the reading is applied back at the table.
            if (app.mission != null)
            {
                var cam = app.desktopCamera;
                Vector3 wasAt = cam.transform.position;
                Quaternion wasLooking = cam.transform.rotation;
                Color background = cam.backgroundColor;
                float farPlane = cam.farClipPlane;
                bool tableWas = app.table.gameObject.activeSelf, mapWas = app.mapRoot.gameObject.activeSelf, panelWas = app.panel.gameObject.activeSelf;
                MapLayer layerBefore = app.terrain.Layer;
                bool entered = app.mission.NextMode(cam.transform) && app.MissionActive;
                MissionFallback.Plan during = app.Plan;
                var low = r1; low.primaryO2 = 40f;
                bool kept = !app.ApplySuitsReading(low, DateTime.UtcNow, 2000f) && app.Plan == during && Mathf.Approximately(app.WhatIfTankPercent, 70f) && app.SuitsReadingWaiting;
                app.mission.Switch(MissionMode.Mode.Table, cam.transform);
                app.TickSuitsDeferred(2001f);
                Check("SUITS: during a mission the plan keeps its tank; back at the table the reading kept is applied",
                    entered && kept && !app.SuitsReadingWaiting && Mathf.Approximately(app.WhatIfTankPercent, 40f), "entered " + entered + ", kept " + kept + ", tank " + app.WhatIfTankPercent);
                app.terrain.ShowLayer(layerBefore);
                app.table.gameObject.SetActive(tableWas);
                app.mapRoot.gameObject.SetActive(mapWas);
                app.panel.gameObject.SetActive(panelWas);
                cam.transform.SetPositionAndRotation(wasAt, wasLooking);
                cam.backgroundColor = background;
                cam.farClipPlane = farPlane;
            }

            app.StopSuits();
            Check("SUITS: stopping the link brings back the full tank and the usual note",
                !app.SuitsLive && Mathf.Approximately(app.WhatIfTankPercent, 100f) && app.Plan != null && Mathf.Approximately(app.Plan.TankPercent, 100f) &&
                !panel.verdictNote.text.StartsWith("SUITS"), panel.verdictNote.text);
        }
    }
}
