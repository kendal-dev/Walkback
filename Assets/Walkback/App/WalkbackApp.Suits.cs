// Written with Claude Code (AI), directed and tested by team MARSXR
using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace Walkback
{
    /// <summary>
    /// v1.1: the optional link to a NASA SUITS telemetry stream server. Off unless suits.json is in the app's
    /// data folder (or -suits host[:port] is on the command line). While a server answers, the plan starts
    /// with the suit's primary oxygen instead of a full tank, and the verdict card says where it came from.
    /// </summary>
    public partial class WalkbackApp
    {
        /// <summary>Seconds between two requests to TSS.</summary>
        public const float SuitsPollSeconds = 2f;
        /// <summary>The plan is made again only when the oxygen moved this much (percent of a tank)...</summary>
        public const float SuitsReplanPercent = 1f;
        /// <summary>...and not more often than this (a plan costs ~200 ms on the Quest).</summary>
        public const float SuitsReplanSeconds = 10f;

        /// <summary>The link's settings, or null when it is off.</summary>
        public SuitsTss.Config SuitsConfig { get; private set; }
        /// <summary>True once a TSS answer set the tank; the WHAT IF button is off then.</summary>
        public bool SuitsLive { get; private set; }
        public SuitsTss.Reading SuitsLast { get; private set; }

        /// <summary>The verdict card's note while the tank comes from TSS (null otherwise).</summary>
        string tankNote;
        float suitsPlannedAt = -100f;

        partial void StartSuits()
        {
            SuitsConfig = SuitsTss.LoadConfig(Application.persistentDataPath) ?? ConfigFromCommandLine();
            if (SuitsConfig == null)
            {
                WalkbackLog.Info("SUITS TSS link: off (no " + SuitsTss.ConfigFile + ")");
                return;
            }
            WalkbackLog.Info("SUITS TSS link: asking " + SuitsConfig.host + ":" + SuitsConfig.port + " for suit " + SuitsConfig.eva + " every " + SuitsPollSeconds + " s");
            if (Application.isPlaying && isActiveAndEnabled) StartCoroutine(PollSuits());
        }

        static SuitsTss.Config ConfigFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] != "-suits") continue;
                string[] parts = args[i + 1].Split(':');
                var config = new SuitsTss.Config { host = parts[0] };
                int port;
                if (parts.Length > 1 && int.TryParse(parts[1], out port)) config.port = port;
                return config.IsSet ? config : null;
            }
            return null;
        }

        IEnumerator PollSuits()
        {
            string lastWhy = null;
            var wait = new WaitForSecondsRealtime(SuitsPollSeconds);
            while (SuitsConfig != null)
            {
                string why = null;
                SuitsTss.Config config = SuitsConfig;
                Task<string> task = Task.Run(() => SuitsTss.Fetch(config.host, config.port, SuitsTss.TimeoutMs, out why));
                while (!task.IsCompleted) yield return null;
                SuitsTss.Reading reading;
                if (task.Status == TaskStatus.RanToCompletion && SuitsTss.TryRead(task.Result, config.eva, out reading))
                {
                    if (lastWhy != null || !SuitsLive) WalkbackLog.Info("SUITS TSS: suit " + config.eva + " primary O2 " + reading.primaryO2.ToString("0.0") + " %");
                    lastWhy = null;
                    ApplySuitsReading(reading, DateTime.UtcNow, Time.unscaledTime);
                }
                else
                {
                    string now = why ?? "the answer is not EVA.json";
                    if (now != lastWhy) WalkbackLog.Info("SUITS TSS: no reading (" + now + ")" + (SuitsLive ? ", the tank stays at the last one" : ""));
                    lastWhy = now;
                }
                yield return wait;
            }
        }

        /// <summary>
        /// A reading from TSS: the plan starts with that much oxygen. Plans again only if it moved by
        /// SuitsReplanPercent or more and SuitsReplanSeconds have passed (the first reading always plans).
        /// The test calls it directly. Returns true if it planned again.
        /// </summary>
        public bool ApplySuitsReading(SuitsTss.Reading reading, DateTime readUtc, float now)
        {
            SuitsLast = reading;
            bool first = !SuitsLive;
            bool moved = Mathf.Abs(reading.primaryO2 - WhatIfTankPercent) >= SuitsReplanPercent;
            if (!first && (!moved || now - suitsPlannedAt < SuitsReplanSeconds)) return false;
            SuitsLive = true;
            suitsPlannedAt = now;
            tankNote = SuitsTss.Note(SuitsConfig != null ? SuitsConfig.eva : 1, reading.primaryO2, readUtc);
            SetWhatIfTank(reading.primaryO2);
            return true;
        }

        /// <summary>Ends the link (the test, or a future button): the full tank and the WHAT IF come back.</summary>
        public void StopSuits()
        {
            SuitsConfig = null;
            if (!SuitsLive) return;
            SuitsLive = false;
            tankNote = null;
            SetWhatIfTank(100f);
        }

        /// <summary>The test sets the link's settings without a file.</summary>
        public void UseSuitsConfig(SuitsTss.Config config)
        {
            SuitsConfig = config;
        }
    }
}
