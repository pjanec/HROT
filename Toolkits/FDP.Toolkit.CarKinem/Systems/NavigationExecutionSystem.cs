using System.Numerics;
using CarKinem.Core;
using Fdp.Kernel;
using FDP.Toolkit.Navigation;

using NavMode   = FDP.Toolkit.Navigation.NavigationMode;
using NavResult = FDP.Toolkit.Navigation.NavigationResult;

namespace CarKinem.Systems
{
    /// <summary>
    /// Muscle-layer authority for navigation completion.
    /// Writes <see cref="NavigationStatus"/> each kinematics tick based on the current
    /// entity position relative to the active <see cref="NavigationIntent"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This system is the CQRS <em>status writer</em> for the navigation contract
    /// (MOD1-P1T4).  It has no knowledge of what the entity <em>is</em> — it only
    /// knows position, velocity, and the current navigation intent.
    /// </para>
    /// <para>
    /// <b>Algorithm per entity per tick:</b>
    /// <list type="number">
    ///   <item>If <c>intent.Mode == NavigationMode.None</c> → skip (no active command).</item>
    ///   <item>If <c>status.IntentId != intent.IntentId</c> → new command: reset status to
    ///     InProgress and clear <see cref="FrustrationTicks"/> to zero.</item>
    ///   <item>Check Cartesian distance from current XY position to
    ///     <c>intent.FinalDestination</c>. If within <c>ArrivalRadius</c> → write Arrived.</item>
    ///   <item>Else if <c>SimVelocity.Linear.Length() &lt; FrustrationSpeedThreshold</c> for
    ///     more than <see cref="FrustrationTickLimit"/> consecutive ticks → write FailedBlocked.</item>
    ///   <item>Else keep InProgress.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Memory note:</b> the frustration counter is stored in the <see cref="FrustrationTicks"/>
    /// ECS component on each entity.  This avoids the dictionary-based memory leak from the
    /// previous implementation — the counter is automatically reclaimed when the entity is
    /// destroyed (MOD1-BATCH-02 CT-MOD1-A).
    /// </para>
    /// <para>
    /// <b>No geo conversion:</b> all distance checks are Cartesian — the same coordinate
    /// space as <see cref="NavigationIntent.FinalDestination"/>.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CarKinematicsSystem))]
    public class NavigationExecutionSystem : ComponentSystem
    {
        // ── Constants ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Speed threshold (m/s) below which a vehicle is considered stuck.
        /// Compared against <c>SimVelocity.Linear.Length()</c>.
        /// </summary>
        public const float FrustrationSpeedThreshold = 0.2f;

        /// <summary>
        /// Number of consecutive ticks below <see cref="FrustrationSpeedThreshold"/> before
        /// the result is set to <see cref="NavigationResult.FailedBlocked"/>.
        /// At 60 Hz: 120 ticks ≈ 2 seconds.
        /// </summary>
        public const int FrustrationTickLimit = 120;

        // ── OnUpdate ───────────────────────────────────────────────────────────────────────────

        protected override void OnUpdate()
        {
            var query = World.Query()
                .With<NavigationIntent>()
                .With<NavigationStatus>()
                .With<FrustrationTicks>()
                .With<SimTransform>()
                .With<SimVelocity>()
                .Build();

            foreach (var entity in query)
            {
                var intent = World.GetComponent<NavigationIntent>(entity);

                // ── Skip inactive intents ──────────────────────────────────────────────────────
                if (intent.Mode == NavMode.None)
                    continue;

                var status      = World.GetComponent<NavigationStatus>(entity);
                var frustration = World.GetComponent<FrustrationTicks>(entity);
                var tf          = World.GetComponent<SimTransform>(entity);
                var vel         = World.GetComponent<SimVelocity>(entity);

                // ── New command detection: reset status and frustration counter ────────────────
                // Round-trip latency note (TD-12): FollowRouteExecutor increments IntentId on
                // the Brain side to signal a loop reset.  This system (Muscle side) detects the
                // mismatch here on the NEXT tick and resets NavigationStatus to InProgress.
                // FollowRouteExecutor therefore always observes at least one tick of InProgress
                // before the new lap can produce an Arrived result. This is intentional and
                // ensures the executor never mistakes the previous lap's Arrived for a new one.
                if (status.IntentId != intent.IntentId)
                {
                    status = new NavigationStatus
                    {
                        IntentId = intent.IntentId,
                        Result   = NavResult.InProgress,
                    };
                    World.SetComponent(entity, status);
                    frustration.Ticks = 0;
                    World.SetComponent(entity, frustration);
                }

                // ── Arrival check ─────────────────────────────────────────────────────────────────
                // DirectPoint: use Cartesian distance from intent.FinalDestination.
                // RoadGraph and FollowRoute: delegate to NavState.HasArrived (set by CarKinematicsSystem).
                bool arrived;
                if ((intent.Mode == NavMode.RoadGraph || intent.Mode == NavMode.FollowRoute)
                    && World.HasComponent<NavState>(entity))
                {
                    var nav = World.GetComponent<NavState>(entity);
                    arrived = nav.HasArrived != 0;
                }
                else
                {
                    var pos2D = new Vector2(tf.Position.X, tf.Position.Y);
                    float dist = Vector2.Distance(pos2D, intent.FinalDestination);
                    arrived = dist <= intent.ArrivalRadius;
                }

                if (arrived)
                {
                    status.Result = NavResult.Arrived;
                    World.SetComponent(entity, status);
                    frustration.Ticks = 0;
                    World.SetComponent(entity, frustration);
                    continue;
                }

                // ── Frustration guard ─────────────────────────────────────────────────────────
                float speed = vel.Linear.Length();

                if (speed < FrustrationSpeedThreshold)
                {
                    frustration.Ticks++;
                    World.SetComponent(entity, frustration);

                    if (frustration.Ticks > FrustrationTickLimit)
                    {
                        status.Result = NavResult.FailedBlocked;
                        World.SetComponent(entity, status);
                        continue;
                    }
                }
                else
                {
                    // Vehicle is moving — reset frustration counter.
                    if (frustration.Ticks != 0)
                    {
                        frustration.Ticks = 0;
                        World.SetComponent(entity, frustration);
                    }
                }

                // ── Keep InProgress ───────────────────────────────────────────────────────────
                if (status.Result != NavResult.InProgress)
                {
                    status.Result = NavResult.InProgress;
                    World.SetComponent(entity, status);
                }
            }
        }
    }
}