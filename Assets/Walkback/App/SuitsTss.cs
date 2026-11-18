// Written with Claude Code (AI), directed and tested by team MARSXR
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace Walkback
{
    /// <summary>
    /// v1.1, off unless set up: reads the suit's primary oxygen from a NASA SUITS telemetry stream server
    /// (TSS, github.com/SUITS-Techteam). TSS answers UDP requests on port 14141: 4 bytes of UNIX time and
    /// 4 bytes of command number, big-endian; command 1 returns EVA.json. The 2026 server nests the suit
    /// under telemetry.eva1 / telemetry.eva2; the 2027 server has one suit directly under telemetry. Both
    /// are read. TSS is a test server of the SUITS challenge: its numbers are a simulation, and the app says
    /// so on the card. Pure functions plus one blocking Fetch (the app runs it off the main thread).
    /// </summary>
    public static class SuitsTss
    {
        public const int DefaultPort = 14141;
        public const uint EvaCommand = 1;
        public const int TimeoutMs = 1500;
        /// <summary>Optional file in the app's data folder: {"host":"192.168.1.20","port":14141,"eva":1}. Without it the link is off.</summary>
        public const string ConfigFile = "suits.json";

        [Serializable]
        public class Config
        {
            public string host = "";
            public int port = DefaultPort;
            public int eva = 1;
            public bool IsSet { get { return !string.IsNullOrEmpty(host) && port > 0 && port < 65536 && (eva == 1 || eva == 2); } }
        }

        public struct Reading
        {
            /// <summary>Primary oxygen tank, percent of full (TSS oxy_pri_storage).</summary>
            public float primaryO2;
            /// <summary>Secondary oxygen tank, percent of full (TSS oxy_sec_storage).</summary>
            public float secondaryO2;
            public float heartRate;
            public float elapsedSeconds;
            public bool started;
        }

        [Serializable] class Suit
        {
            public float oxy_pri_storage = -1f;
            public float oxy_sec_storage = -1f;
            public float heart_rate = -1f;
            public float eva_elapsed_time = -1f;
        }
        [Serializable] class Telemetry : Suit
        {
            public Suit eva1;
            public Suit eva2;
        }
        [Serializable] class Status { public bool started; }
        [Serializable] class Root
        {
            public Telemetry telemetry;
            public Status status;
        }

        /// <summary>The 8-byte request: UNIX time and command number, both big-endian.</summary>
        public static byte[] Request(uint unixTime, uint command)
        {
            var b = new byte[8];
            b[0] = (byte)(unixTime >> 24); b[1] = (byte)(unixTime >> 16); b[2] = (byte)(unixTime >> 8); b[3] = (byte)unixTime;
            b[4] = (byte)(command >> 24); b[5] = (byte)(command >> 16); b[6] = (byte)(command >> 8); b[7] = (byte)command;
            return b;
        }

        /// <summary>The JSON text of a reply: from its first '{' (older servers put 8 bytes of time and command before it), or null.</summary>
        public static string JsonOf(byte[] reply)
        {
            if (reply == null) return null;
            int start = Array.IndexOf(reply, (byte)'{');
            if (start < 0) return null;
            int end = reply.Length;
            while (end > start && reply[end - 1] == 0) end--;
            return System.Text.Encoding.UTF8.GetString(reply, start, end - start);
        }

        /// <summary>Reads suit 1 or 2 from EVA.json (either server shape). False when the oxygen is not there.</summary>
        public static bool TryRead(string json, int eva, out Reading reading)
        {
            reading = new Reading();
            if (string.IsNullOrEmpty(json)) return false;
            Root root;
            try { root = JsonUtility.FromJson<Root>(json); }
            catch (Exception) { return false; }
            if (root == null || root.telemetry == null) return false;
            Suit nested = eva == 2 ? root.telemetry.eva2 : root.telemetry.eva1;
            Suit suit = nested != null && nested.oxy_pri_storage >= 0f ? nested : root.telemetry;
            if (suit.oxy_pri_storage < 0f || suit.oxy_pri_storage > 100.5f) return false;
            reading.primaryO2 = Mathf.Clamp(suit.oxy_pri_storage, 0f, 100f);
            reading.secondaryO2 = suit.oxy_sec_storage;
            reading.heartRate = suit.heart_rate;
            reading.elapsedSeconds = suit.eva_elapsed_time;
            reading.started = root.status != null && root.status.started;
            return true;
        }

        /// <summary>The config from the app's data folder, or null (the link stays off).</summary>
        public static Config LoadConfig(string folder)
        {
            try
            {
                string path = Path.Combine(folder, ConfigFile);
                if (!File.Exists(path)) return null;
                var config = JsonUtility.FromJson<Config>(File.ReadAllText(path));
                return config != null && config.IsSet ? config : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// One request to TSS for EVA.json, waiting at most TimeoutMs. Blocking: the app calls it on a worker
        /// thread. Returns the JSON text, or null with the reason in <paramref name="why"/>.
        /// </summary>
        public static string Fetch(string host, int port, int timeoutMs, out string why)
        {
            why = null;
            try
            {
                using (var client = new UdpClient())
                {
                    client.Client.ReceiveTimeout = timeoutMs;
                    client.Connect(host, port);
                    byte[] request = Request((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), EvaCommand);
                    client.Send(request, request.Length);
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] reply = client.Receive(ref from);
                    string json = JsonOf(reply);
                    if (json == null) why = "the answer has no JSON (" + reply.Length + " bytes)";
                    return json;
                }
            }
            catch (SocketException e)
            {
                why = e.SocketErrorCode == SocketError.TimedOut ? "no answer in " + timeoutMs + " ms" : e.SocketErrorCode.ToString();
                return null;
            }
            catch (Exception e)
            {
                why = e.GetType().Name;
                return null;
            }
        }

        /// <summary>The verdict card's note while the tank comes from TSS.</summary>
        public static string Note(int eva, float primaryO2, DateTime readUtc)
        {
            return "SUITS TSS suit " + eva + " · primary O2 " + primaryO2.ToString("0", CultureInfo.InvariantCulture) +
                   " % at " + readUtc.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC (simulated) · ESTIMATE";
        }
    }
}
