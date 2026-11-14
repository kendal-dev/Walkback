// Written with Claude Code (AI), directed and tested by team MARSXR
using System.Collections;
using UnityEngine;
using UnityEngine.XR;

namespace Walkback
{
    /// <summary>
    /// Entry point. In the headset: Meta camera rig with passthrough.
    /// Without a headset (Play in the editor): a desktop camera looks at the table.
    /// </summary>
    public class WalkbackApp : MonoBehaviour
    {
        public GameObject cameraRig;
        public Camera desktopCamera;
        public Transform table;
        public Transform mapRoot;
        public MissionPanel panel;

        public bool DesktopMode { get; private set; }

        float fpsWindowStart;
        int fpsFrames;
        float fpsWorst = float.MaxValue;

        void Awake()
        {
            WalkbackLog.Init();
            WalkbackLog.Info("Walkback " + Application.version + " on " + SystemInfo.deviceModel + " (" + SystemInfo.operatingSystem + "), Unity " + Application.unityVersion);
            WalkbackLog.Info("Log file: " + WalkbackLog.FilePath);
        }

        IEnumerator Start()
        {
            // The XR device needs a couple of frames to report itself.
            yield return null;
            yield return null;
            DesktopMode = !XRSettings.isDeviceActive;
            if (cameraRig != null) cameraRig.SetActive(!DesktopMode);
            if (desktopCamera != null) desktopCamera.enabled = DesktopMode;
            WalkbackLog.Info(DesktopMode
                ? "Mode: desktop (no headset found)"
                : "Mode: headset " + XRSettings.loadedDeviceName);
            fpsWindowStart = Time.realtimeSinceStartup;
        }

        void Update()
        {
            fpsFrames++;
            if (Time.unscaledDeltaTime > 0f)
                fpsWorst = Mathf.Min(fpsWorst, 1f / Time.unscaledDeltaTime);
            float elapsed = Time.realtimeSinceStartup - fpsWindowStart;
            if (elapsed >= 10f)
            {
                WalkbackLog.Info("fps avg " + (fpsFrames / elapsed).ToString("F1") + " worst " + fpsWorst.ToString("F1"));
                fpsWindowStart = Time.realtimeSinceStartup;
                fpsFrames = 0;
                fpsWorst = float.MaxValue;
            }
        }
    }
}
