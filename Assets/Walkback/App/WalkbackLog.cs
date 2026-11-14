// Written with Claude Code (AI), directed and tested by team MARSXR
using System;
using System.IO;
using UnityEngine;

namespace Walkback
{
    /// <summary>
    /// The app keeps its own log file: the Quest only keeps about a minute of logcat.
    /// Pull it with: adb pull /sdcard/Android/data/[package]/files/walkback.log
    /// </summary>
    public static class WalkbackLog
    {
        static StreamWriter writer;

        public static string FilePath { get; private set; }

        public static void Init()
        {
            if (writer != null) return;
            try
            {
                FilePath = Path.Combine(Application.persistentDataPath, "walkback.log");
                writer = new StreamWriter(FilePath, false) { AutoFlush = true };
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Walkback] Could not open the log file: " + e.Message);
            }
        }

        public static void Info(string message)
        {
            string line = "[Walkback] " + message;
            Debug.Log(line);
            Write(line);
        }

        public static void Warn(string message)
        {
            string line = "[Walkback] WARNING " + message;
            Debug.LogWarning(line);
            Write(line);
        }

        static void Write(string line)
        {
            if (writer == null) return;
            try { writer.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff ") + line); }
            catch (Exception) { /* a full disk must not stop the app */ }
        }
    }
}
