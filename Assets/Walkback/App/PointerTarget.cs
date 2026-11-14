// Written with Claude Code (AI), directed and tested by team MARSXR
using UnityEngine;

namespace Walkback
{
    /// <summary>
    /// Anything the pointer can hover and press: a panel button, the globe, the map.
    /// The pointer (hand ray with pinch, controller ray with trigger, or the mouse in desktop mode)
    /// raycasts colliders and calls the first IPointerTarget it finds with GetComponentInParent.
    /// So a target needs a Collider on itself or on a child.
    /// </summary>
    public interface IPointerTarget
    {
        void OnPointerEnter();
        void OnPointerExit();
        /// <summary>Called once per press (pinch, trigger or mouse click), with the hit point in world space.</summary>
        void OnPointerClick(Vector3 worldPoint);
    }
}
