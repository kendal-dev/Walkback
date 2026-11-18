// Written with Claude Code (AI), directed and tested by team MARSXR
namespace Walkback
{
    /// <summary>The pointer and the table controls: their references and per-frame work.</summary>
    public partial class WalkbackApp
    {
        public Pointer pointer;
        public TableControls tableControls;

        partial void StartPointer()
        {
            if (pointer != null) pointer.Begin(this);
            if (tableControls != null) tableControls.Begin(this);
            // The terrain mesh exists now (built in its Awake): let the ray land on the ground.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            if (Pointer.AttachTerrainCollider(terrain) != null)
                WalkbackLog.Info("Terrain collider ready in " + watch.ElapsedMilliseconds + " ms");
        }

        partial void UpdatePointer()
        {
            // The pointer first: the table controls ask it whether a press began on a button.
            if (pointer != null) pointer.Tick();
            if (tableControls != null) tableControls.Tick();
            // v1.1: a short tap on the bare ground adds a stop of the astronaut's own.
            TickMapTap();
            // v1.1: minutes back and slope under the pointer.
            TickGroundProbe();
        }
    }
}
