// Written with Claude Code (AI), directed and tested by team MARSXR
using UnityEngine;
using UnityEngine.UI;

namespace Walkback.EditorTools
{
    /// <summary>v1.1 test checks: the WHAT IF sign by the table, for hands (no thumbstick to click).</summary>
    public static partial class AutoTest
    {
        static void CheckWhatIfSign(WalkbackApp app, MissionPanel panel)
        {
            TableControls controls = app.tableControls;
            PointerButton sign = controls != null ? controls.whatIfButton : null;
            Check("WHAT IF sign: built by the table, at the front-left corner (RECENTER's mirror)",
                sign != null && sign.name == SceneBuilder.WhatIfButtonName && Vector3.Distance(sign.transform.position, SceneBuilder.WhatIfButtonPosition) < 0.001f &&
                sign.label != null && sign.label.text == "WHAT IF");
            if (sign == null) return;
            controls.Begin(app);

            // A ray from the user's eyes reaches it (nothing in front of it).
            Physics.SyncTransforms();
            Vector3 eye = MissionPanel.UserEye;
            RaycastHit hit;
            bool reaches = Physics.Raycast(eye, (sign.transform.position - eye).normalized, out hit, 3f) && hit.collider.GetComponentInParent<PointerButton>() == sign;
            Check("WHAT IF sign: a ray from the user's eyes reaches it", reaches, reaches ? "" : hit.collider != null ? "hit " + hit.collider.name : "hit nothing");

            app.SetWhatIfTank(100f);
            sign.OnPointerClick(sign.transform.position);
            float first = app.WhatIfTankPercent;
            sign.OnPointerClick(sign.transform.position);
            float second = app.WhatIfTankPercent;
            sign.OnPointerClick(sign.transform.position);
            float third = app.WhatIfTankPercent;
            Check("WHAT IF sign: each press is one step of the stick's WHAT IF (100 -> 15 -> 8 -> 100 %)",
                Mathf.Approximately(first, 15f) && Mathf.Approximately(second, 8f) && Mathf.Approximately(third, 100f),
                first + " -> " + second + " -> " + third);

            // It hides with the table, like RECENTER.
            bool tableWas = app.table.gameObject.activeSelf;
            app.table.gameObject.SetActive(false);
            controls.SyncRecenterSign();
            bool hidden = !sign.gameObject.activeSelf;
            app.table.gameObject.SetActive(tableWas);
            controls.SyncRecenterSign();
            Check("WHAT IF sign: hidden with the table (360° photo, mission), back with it", hidden && sign.gameObject.activeSelf == tableWas);

            // The controls card names it.
            bool named = false;
            foreach (string line in MissionPanel.ControlsText) if (line.StartsWith("WHAT IF") && line.Contains("sign")) named = true;
            Check("WHAT IF sign: the controls card names it", named);
        }
    }
}
