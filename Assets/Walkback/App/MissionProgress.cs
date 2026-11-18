// Written with Claude Code (AI), directed and tested by team MARSXR
using UnityEngine;

namespace Walkback
{
    /// <summary>
    /// Where the astronaut is along the planned mission, and what the visor shows there
    /// (mission mode, guion §22.9). It only reads the plan already made by MissionFallback and the
    /// map's return layer: it makes up no number.
    ///   - The position comes from outside (the headset): Walk(here) moves the progress forward to
    ///     the point of the current leg's route nearest to the astronaut. It never goes back:
    ///     oxygen already breathed is not given back by stepping backwards.
    ///   - JumpNext() goes to the next point of the route: ApproachMeters before the next stop.
    ///     The jump counts as walked: the minutes and the oxygen of the stretch jumped over are
    ///     charged, taken from the plan's own legs. Leaving a stop also charges its work.
    ///   - Read(here) gives the visor's numbers: oxygen left, way back from the node the astronaut
    ///     stands on (the map's return layer), the next stop, and TURN BACK when the oxygen left
    ///     above the reserve no longer covers that way back (the same rule as HudEstimate.TurnBack,
    ///     with the tank the plan starts with, so a WHAT IF carries into the visor).
    /// Pure class: no scene needed, the automatic test calls it directly.
    /// </summary>
    public class MissionProgress
    {
        /// <summary>
        /// A jump lands this far before the stop: the last stretch is walked for real. 2.5 m (it was 5 m) so the
        /// walk to the arrival card (1 m) fits inside the 3 m free space marked on the floor.
        /// </summary>
        public const float ApproachMeters = 2.5f;
        /// <summary>Closer than this to the stop counts as arrived: its card shows.</summary>
        public const float ArriveMeters = 1.5f;
        /// <summary>How far ahead of the progress the route is searched for the astronaut (a real room is smaller).</summary>
        const float LookAheadMeters = 15f;
        const float LookBackMeters = 1f;

        /// <summary>What the visor shows at one moment.</summary>
        public struct Snapshot
        {
            public Vector2 here;
            public int leg;
            public string nextName;
            public Vector2 nextPoint;
            /// <summary>Index into site.Stops of the next stop, or -1 when the next target is the habitat.</summary>
            public int nextStop;
            public float nextMeters;
            public float nextMinutes;
            public float routeMeters;
            public float walkMinutes;
            public float workMinutes;
            public float elapsedMinutes;
            public float usedLiters;
            public float oxygenLeftPercent;
            public float returnMinutes;
            public float returnLiters;
            /// <summary>True when the map has no way back on foot from where the astronaut stands.</summary>
            public bool noWayBack;
            public bool turnBack;
            public bool arrived;
            public float taskMinutesAtNext;
            public bool canJump;
            /// <summary>Bearing from here straight to the next stop, degrees clockwise from north.</summary>
            public float nextBearingDegrees;
            /// <summary>Straight-line meters from here to the next stop (nextMeters is along the route).</summary>
            public float nextStraightMeters;
            /// <summary>Height of the next stop above where the astronaut stands, from the relief (negative = downhill).</summary>
            public float nextClimbMeters;
            /// <summary>Walking minutes the oxygen above the reserve covers after the way back (negative = short).</summary>
            public float marginMinutes;
            /// <summary>Green, amber or red for the minutes back (HudEstimate.StateOf).</summary>
            public HudEstimate.ReturnState returnState;
            /// <summary>v1.1: route meters from here to the plan's point of no return (TurnBackPoint), or -1 when the plan has none.</summary>
            public float turnBackInMeters;
        }

        readonly SiteData site;
        readonly MissionFallback.Plan plan;
        readonly float[] returnMinutes;
        /// <summary>Route meters from the habitat at every point of plan.Path.</summary>
        readonly float[] along;
        readonly bool[] workCharged;
        /// <summary>v1.1: route meters of the plan's point of no return, or -1.</summary>
        readonly float turnBackAt = -1f;
        public float TurnBackAt { get { return turnBackAt; } }

        /// <summary>The leg being walked (index into plan.Legs).</summary>
        public int Leg { get; private set; }
        /// <summary>Route meters accounted for so far: jumped over or walked.</summary>
        public float RouteMeters { get; private set; }
        /// <summary>Minutes of work charged so far (stops already left).</summary>
        public float WorkMinutes { get; private set; }
        public int Jumps { get; private set; }
        /// <summary>True from reaching the current leg's target until the next jump.</summary>
        public bool Arrived { get; private set; }

        public MissionProgress(SiteData site, MissionFallback.Plan plan, float[] returnMinutes)
        {
            this.site = site;
            this.plan = plan;
            this.returnMinutes = returnMinutes;
            int count = plan != null ? plan.Path.Count : 0;
            along = new float[Mathf.Max(count, 1)];
            for (int i = 1; i < count; i++) along[i] = along[i - 1] + Vector2.Distance(plan.Path[i - 1], plan.Path[i]);
            workCharged = new bool[plan != null ? plan.Legs.Count : 0];
            if (site != null && returnMinutes != null && returnMinutes.Length == site.GridSize * site.GridSize)
            {
                TurnBackPoint.Result r = TurnBackPoint.Find(site, returnMinutes, plan);
                if (r.found && r.pathIndex < along.Length) turnBackAt = along[r.pathIndex];
            }
        }

        /// <summary>False when there is no plan to walk (no route, or no legs).</summary>
        public bool Valid { get { return plan != null && plan.HasRoute && plan.Legs.Count > 0 && plan.Path.Count >= 2; } }
        public float TotalMeters { get { return along[along.Length - 1]; } }
        public float LegStart(int leg) { return along[plan.Legs[leg].FirstPoint]; }
        public float LegEnd(int leg) { return along[plan.Legs[leg].LastPoint]; }
        /// <summary>
        /// Route meters of the point a jump lands on for this leg: the last point of its route that is
        /// ApproachMeters from the stop IN A STRAIGHT LINE (so the user really walks that far to the pillar),
        /// or the leg's start if the whole leg is closer. On a grid the route zigzags near its end:
        /// 5 m of route back can be under 2 m from the stop, which is why it is not counted along the route.
        /// </summary>
        public float ApproachOf(int leg)
        {
            MissionFallback.Leg l = plan.Legs[leg];
            Vector2 target = plan.Path[l.LastPoint];
            float r = ApproachMeters;
            for (int i = l.LastPoint - 1; i >= l.FirstPoint; i--)
            {
                Vector2 a = plan.Path[i], b = plan.Path[i + 1];
                if (Vector2.Distance(a, target) < r) continue;
                // a is at least r from the stop and b is closer: find t on a -> b with |a + t(b - a) - target| = r.
                Vector2 d = b - a, f = a - target;
                float qa = Vector2.Dot(d, d), qb = 2f * Vector2.Dot(f, d), qc = Vector2.Dot(f, f) - r * r;
                float t = 0f;
                if (qa > 1e-8f)
                {
                    float disc = Mathf.Max(0f, qb * qb - 4f * qa * qc);
                    // Walking from a toward b, the first crossing of the circle is the smaller root.
                    t = Mathf.Clamp01((-qb - Mathf.Sqrt(disc)) / (2f * qa));
                }
                return along[i] + t * (along[i + 1] - along[i]);
            }
            return LegStart(leg);
        }
        /// <summary>The stop (or the habitat) the leg ends at, in local meters.</summary>
        public Vector2 LegTarget(int leg) { return plan.Path[plan.Legs[leg].LastPoint]; }

        /// <summary>The point of the route at this many route meters from the habitat, in local meters.</summary>
        public Vector2 PointAt(float meters)
        {
            if (plan == null || plan.Path.Count == 0) return Vector2.zero;
            meters = Mathf.Clamp(meters, 0f, TotalMeters);
            for (int i = 1; i < along.Length; i++)
            {
                if (meters > along[i]) continue;
                float span = along[i] - along[i - 1];
                return span > 1e-6f ? Vector2.Lerp(plan.Path[i - 1], plan.Path[i], (meters - along[i - 1]) / span) : plan.Path[i];
            }
            return plan.Path[plan.Path.Count - 1];
        }

        /// <summary>From the progress point straight to the current leg's target (unit vector, x east, y north).</summary>
        public Vector2 DirectionToTarget()
        {
            if (!Valid) return Vector2.up;
            Vector2 d = LegTarget(Leg) - PointAt(RouteMeters);
            if (d.sqrMagnitude < 1e-4f)
            {
                // Standing on the target: look the way the route arrived.
                int last = plan.Legs[Leg].LastPoint;
                for (int i = last; i > plan.Legs[Leg].FirstPoint; i--)
                {
                    d = plan.Path[last] - plan.Path[i - 1];
                    if (d.sqrMagnitude >= 1e-4f) break;
                }
            }
            return d.sqrMagnitude >= 1e-4f ? d.normalized : Vector2.up;
        }

        /// <summary>True while there is a next point of the route to jump to.</summary>
        public bool CanJump
        {
            get { return Valid && (RouteMeters < ApproachOf(Leg) - 0.01f || Leg + 1 < plan.Legs.Count); }
        }

        /// <summary>
        /// Goes to the next point of the route: the approach of the current leg's stop, or, when already
        /// there or past it, the approach of the next stop (the work at the stop left behind is charged).
        /// Everything jumped over counts as walked. False when the route has no next point.
        /// </summary>
        public bool JumpNext()
        {
            if (!CanJump) return false;
            if (RouteMeters >= ApproachOf(Leg) - 0.01f)
            {
                ChargeWork(Leg);
                Leg++;
            }
            RouteMeters = Mathf.Max(RouteMeters, ApproachOf(Leg));
            Arrived = false;
            Jumps++;
            return true;
        }

        void ChargeWork(int leg)
        {
            if (workCharged[leg]) return;
            workCharged[leg] = true;
            WorkMinutes += plan.Legs[leg].WorkMinutesAtEnd;
        }

        /// <summary>
        /// The astronaut is at <paramref name="here"/> (local meters, from the headset): the progress
        /// moves forward to the nearest point of the current leg's route, never back.
        /// </summary>
        public void Walk(Vector2 here)
        {
            if (!Valid) return;
            MissionFallback.Leg leg = plan.Legs[Leg];
            float best = -1f, bestDistance = float.MaxValue;
            for (int i = leg.FirstPoint; i < leg.LastPoint; i++)
            {
                if (along[i + 1] < RouteMeters - LookBackMeters || along[i] > RouteMeters + LookAheadMeters) continue;
                Vector2 a = plan.Path[i], ab = plan.Path[i + 1] - a;
                float length2 = ab.sqrMagnitude;
                float t = length2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(here - a, ab) / length2) : 0f;
                float distance = Vector2.Distance(here, a + ab * t);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = along[i] + (along[i + 1] - along[i]) * t;
                }
            }
            if (best >= 0f) RouteMeters = Mathf.Clamp(Mathf.Max(RouteMeters, best), LegStart(Leg), LegEnd(Leg));
            if (Vector2.Distance(here, LegTarget(Leg)) <= ArriveMeters) Arrived = true;
        }

        /// <summary>The visor's numbers with the astronaut at <paramref name="here"/> (local meters).</summary>
        public Snapshot Read(Vector2 here)
        {
            var s = new Snapshot { here = here, leg = Leg, nextName = "", nextStop = -1 };
            if (!Valid) return s;
            MissionFallback.Leg leg = plan.Legs[Leg];
            float start = LegStart(Leg), end = LegEnd(Leg);
            float length = end - start;
            float done = length > 1e-4f ? Mathf.Clamp01((RouteMeters - start) / length) : 1f;

            float walk = 0f;
            for (int i = 0; i < Leg; i++) walk += plan.Legs[i].Minutes;
            walk += leg.Minutes * done;

            s.routeMeters = RouteMeters;
            s.walkMinutes = walk;
            s.workMinutes = WorkMinutes;
            s.elapsedMinutes = walk + WorkMinutes;
            s.usedLiters = walk * HudEstimate.WalkLitersPerMinute + WorkMinutes * HudEstimate.DesignLitersPerMinute;
            s.oxygenLeftPercent = Mathf.Max(0f, (plan.TankLiters - s.usedLiters) / HudEstimate.TankLiters * 100f);

            s.nextName = leg.ToName;
            s.nextStop = leg.ToStop;
            s.nextPoint = LegTarget(Leg);
            s.nextMeters = Mathf.Max(0f, end - RouteMeters);
            s.nextMinutes = leg.Minutes * (1f - done);
            s.taskMinutesAtNext = leg.WorkMinutesAtEnd;
            s.arrived = Arrived;
            s.canJump = CanJump;

            float back = returnMinutes != null && returnMinutes.Length == site.GridSize * site.GridSize
                ? returnMinutes[ReturnFallback.NodeOf(site, here)]
                : float.PositiveInfinity;
            s.noWayBack = float.IsInfinity(back) || float.IsNaN(back);
            s.returnMinutes = s.noWayBack ? 0f : back;
            s.returnLiters = s.returnMinutes * HudEstimate.WalkLitersPerMinute;
            // The rule of HudEstimate.TurnBack, with the tank this plan starts with (a WHAT IF starts with less).
            s.turnBack = s.noWayBack || plan.TankLiters - s.usedLiters - HudEstimate.ReserveLiters < s.returnLiters;
            float aboveReserve = plan.TankLiters - s.usedLiters - HudEstimate.ReserveLiters;
            s.marginMinutes = s.noWayBack ? 0f : HudEstimate.MarginMinutes(aboveReserve, s.returnLiters);
            s.returnState = s.noWayBack ? HudEstimate.ReturnState.NotCovered : HudEstimate.StateOf(aboveReserve, s.returnLiters);
            s.turnBackInMeters = turnBackAt >= 0f ? Mathf.Max(0f, turnBackAt - RouteMeters) : -1f;

            // Where the next stop is from here: bearing and straight distance on the map, height from the relief.
            s.nextBearingDegrees = HudCompass.Bearing(here, s.nextPoint);
            s.nextStraightMeters = Vector2.Distance(here, s.nextPoint);
            s.nextClimbMeters = RouteGeometry.HeightAt(site, s.nextPoint.x, s.nextPoint.y) - RouteGeometry.HeightAt(site, here.x, here.y);
            return s;
        }
    }
}
