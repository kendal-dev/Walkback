// Written with Claude Code (AI), directed and tested by team MARSXR
using UnityEditor;
using UnityEngine;

namespace Walkback.EditorTools
{
    /// <summary>The pointer, the hands it reads, the table controls and the RECENTER sign.</summary>
    public static partial class SceneBuilder
    {
        public const string PointerObjectName = "Pointer";
        public const string RecenterButtonName = "RecenterButton";
        /// <summary>The RECENTER sign: near the front-right corner of the map, tilted up toward the user.</summary>
        public static readonly Vector3 RecenterButtonPosition = new Vector3(0.42f, TableHeight + 0.05f, 0.40f);
        public static readonly Vector2 RecenterButtonSize = new Vector2(0.22f, 0.075f);
        /// <summary>v1.1: the WHAT IF sign, at the front-left corner (RECENTER's mirror): hands have no thumbstick to click.</summary>
        public const string WhatIfButtonName = "WhatIfButton";
        public static readonly Vector3 WhatIfButtonPosition = new Vector3(-0.42f, TableHeight + 0.05f, 0.40f);

        static partial void BuildPointer(WalkbackApp app)
        {
            // Pinch needs hand tracking declared next to the controllers (Assets/Oculus/OculusProjectConfig.asset).
            var projectConfig = OVRProjectConfig.CachedProjectConfig;
            if (projectConfig.handTrackingSupport != OVRProjectConfig.HandTrackingSupport.ControllersAndHands)
            {
                projectConfig.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
                OVRProjectConfig.CommitProjectConfig(projectConfig);
            }

            // The cylinder primitive brings a capsule collider that, at this scale, is a ball reaching
            // 0.37 m above the table top: it would catch rays aimed at the map or the panel. A box
            // whose top is the table top replaces it, so the dot lands on the table.
            var tableCollider = app.table.GetComponent<Collider>();
            if (tableCollider != null && !(tableCollider is BoxCollider)) Object.DestroyImmediate(tableCollider);
            if (app.table.GetComponent<BoxCollider>() == null)
            {
                var box = app.table.gameObject.AddComponent<BoxCollider>();
                box.center = Vector3.zero;
                box.size = new Vector3(1f, 2f, 1f);
            }

            // Opaque unlit: a translucent ray would open holes to the room in passthrough (trap 7).
            Material rayMaterial = MakeMaterial("PointerRay", "Universal Render Pipeline/Unlit", WalkbackTheme.Live, 0f);
            Material plateMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/PanelBack.mat");
            Material uiMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/PanelUI.mat");

            var pointerObject = new GameObject(PointerObjectName);
            var pointer = pointerObject.AddComponent<Pointer>();
            pointer.app = app;

            var line = pointerObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = 0.004f;
            line.endWidth = 0.0025f;
            line.numCapVertices = 4;
            line.sharedMaterial = rayMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            pointer.line = line;

            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "PointerDot";
            Object.DestroyImmediate(dot.GetComponent<Collider>());
            dot.transform.SetParent(pointerObject.transform, false);
            dot.transform.localScale = Vector3.one * 0.014f;
            var dotRenderer = dot.GetComponent<MeshRenderer>();
            dotRenderer.sharedMaterial = rayMaterial;
            dotRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dot.SetActive(false);
            pointer.dot = dot.transform;

            // The hands: an OVRHand under each hand anchor gives the pinch and the pointing pose.
            Transform trackingSpace = PointerFindChild(app.cameraRig.transform, "TrackingSpace");
            pointer.rightHand = PointerAddHand(app.cameraRig.transform, "RightHandAnchor", OVRHand.Hand.HandRight, trackingSpace);
            pointer.leftHand = PointerAddHand(app.cameraRig.transform, "LeftHandAnchor", OVRHand.Hand.HandLeft, trackingSpace);

            // The table controls live on the pointer object (not part of the table setup that recenters).
            var controls = pointerObject.AddComponent<TableControls>();
            controls.app = app;
            controls.pointer = pointer;
            controls.baseScale = app.mapRoot.localScale.x;

            var recenter = PointerButton.Create(RecenterButtonName, "RECENTER", RecenterButtonSize, plateMaterial, uiMaterial);
            recenter.transform.position = RecenterButtonPosition;
            recenter.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
            controls.recenterButton = recenter;

            var whatIf = PointerButton.Create(WhatIfButtonName, "WHAT IF", RecenterButtonSize, plateMaterial, uiMaterial);
            whatIf.transform.position = WhatIfButtonPosition;
            whatIf.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
            controls.whatIfButton = whatIf;

            app.pointer = pointer;
            app.tableControls = controls;
        }

        static OVRHand PointerAddHand(Transform rig, string anchorName, OVRHand.Hand type, Transform pointerPoseRoot)
        {
            Transform anchor = PointerFindChild(rig, anchorName);
            if (anchor == null) throw new System.InvalidOperationException("Camera rig has no " + anchorName);
            var handObject = new GameObject(type == OVRHand.Hand.HandRight ? "RightHandPointer" : "LeftHandPointer");
            handObject.transform.SetParent(anchor, false);
            var hand = handObject.AddComponent<OVRHand>();
            // HandType and the pointer pose root are not public: set them as the inspector would.
            var data = new SerializedObject(hand);
            data.FindProperty("HandType").enumValueIndex = PointerHandTypeIndex(data.FindProperty("HandType"), type);
            data.FindProperty("_pointerPoseRoot").objectReferenceValue = pointerPoseRoot;
            data.ApplyModifiedPropertiesWithoutUndo();
            return hand;
        }

        static int PointerHandTypeIndex(SerializedProperty property, OVRHand.Hand type)
        {
            string wanted = type.ToString();
            for (int i = 0; i < property.enumNames.Length; i++)
                if (property.enumNames[i] == wanted) return i;
            throw new System.InvalidOperationException("OVRHand.Hand has no " + wanted);
        }

        static Transform PointerFindChild(Transform parent, string childName)
        {
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
                if (t.name == childName) return t;
            return null;
        }
    }
}
