using System;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;

using NavMode   = Fdp.Toolkit.Navigation.NavigationMode;
using NavResult = Fdp.Toolkit.Navigation.NavigationResult;

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
    [UpdateInPhase(SystemPhase.Simulation)]
    // [UpdateAfter(typeof(CarKinematicsSystem))] -- ordering maintained by array position in GroundKinematicsModule.
    public class NavigationExecutionSystem : IEcsModuleSystem
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

        public void Execute(ISimulationView view, float deltaTime)
        {
            // Prevent frustration accumulation during paused/frozen frames
            if (deltaTime <= 0f) return;

            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(NavigationExecutionSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var query = repo.Query()
                .With<NavigationIntent>()
                .With<NavigationStatus>()
                .With<FrustrationTicks>()
                .With<SimTransform>()
                .With<SimVelocity>()
                .Build();

            foreach (var entity in query)
            {
                var intent = repo.GetComponent<NavigationIntent>(entity);

                // ── Skip inactive intents ──────────────────────────────────────────────────────
                if (intent.Mode == NavMode.None)
                    continue;

                var status      = repo.GetComponent<NavigationStatus>(entity);
                var frustration = repo.GetComponent<FrustrationTicks>(entity);
                var tf          = repo.GetComponent<SimTransform>(entity);
                var vel         = repo.GetComponent<SimVelocity>(entity);

                // ── Mirror ProgressS from NavState (PACK-N002) ───────────────────────────────
                // Cache NavState.ProgressS once per entity tick so Brain-only nodes can read
                // route progress via the NavigationStatus CQRS feedback channel without querying
                // NavState directly (CQRS boundary — DESIGN.md §1.B).
                float progressAtThisTick = 0f;
                if (repo.HasComponent<NavState>(entity))
                    progressAtThisTick = repo.GetComponent<NavState>(entity).ProgressS;
                status.ProgressS = progressAtThisTick;

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
                        IntentId  = intent.IntentId,
                        Result    = NavResult.InProgress,
                        ProgressS = progressAtThisTick,
                    };
                    repo.SetComponent(entity, status);

                    frustration = new FrustrationTicks();
                    repo.SetComponent(entity, frustration);

                    repo.Bus.Publish(new MoveStartedEvent
                    {
                        RouteHandle = intent.RouteHandle,
                    });
                    frustration.MoveStartedFired = 1;
                    repo.SetComponent(entity, frustration);
                }

                // ── Skip entities already at a terminal result ────────────────────────────────
                // Once Arrived or a hard failure is written, do not mutate status further
                // until the Brain issues a new intent (increments IntentId).
                if (status.Result != NavResult.InProgress)
                    continue;

                // ── Arrival check ─────────────────────────────────────────────────────────────────
                // Trust NavState.HasArrived whenever NavState is present (set by CarKinematicsSystem).
                // Fallback to Cartesian check only for entities without NavState.
                bool arrived;
                if (repo.HasComponent<NavState>(entity))
                {
                    var nav = repo.GetComponent<NavState>(entity);
                    arrived = nav.HasArrived != 0;
                }
                else
                {
                    var pos2D = new Vector2(tf.Position.X, tf.Position.Y);
                    // Arrival check is 2D-projected (§0.2): project the 3D destination to XY.
                    var destXY = new Vector2(intent.FinalDestination.X, intent.FinalDestination.Y);
                    float dist = Vector2.Distance(pos2D, destXY);
                    arrived = dist <= intent.ArrivalRadius;
                }

                if (arrived)
                {
                    status.Result = NavResult.Arrived;
                    repo.SetComponent(entity, status);

                    repo.Bus.Publish(new MoveCompletedEvent
                    {
                        Target      = entity,
                        Reason      = NavResult.Arrived,
                        RouteHandle = intent.RouteHandle,
                    });

                    frustration.Ticks = 0;
                    repo.SetComponent(entity, frustration);
                    continue;
                }

                // ── Frustration guard ─────────────────────────────────────────────────────────
                float speed = vel.Linear.Length();

                if (speed < FrustrationSpeedThreshold)
                {
                    frustration.Ticks++;
                    repo.SetComponent(entity, frustration);

                    if (frustration.Ticks > FrustrationTickLimit)
                    {
                        byte effectiveMax = intent.MaxReplans != 0
                            ? intent.MaxReplans
                            : NavigationConstants.DefaultMaxReplans;

                        bool allowReplan = (intent.Flags & (1 << NavigationConstants.FlagBitAllowReplan)) != 0;

                        if (allowReplan && status.ReplanCount < effectiveMax)
                        {
                            // ── Internal Muscle replan ─────────────────────────────────────────
                            status.ReplanCount++;
                            repo.SetComponent(entity, status);

                            repo.Bus.Publish(new PathfindingRequestEvent
                            {
                                RequestId   = (long)entity.Index << 32 | (uint)status.ReplanCount,
                                Start       = tf.Position,
                                End         = intent.FinalDestination, // real destination Z (Sim Z-up, P3D-302)
                                RouteHandle = intent.RouteHandle,
                            });

                            repo.Bus.Publish(new PathReplannedEvent
                            {
                                Target      = entity,
                                RouteHandle = intent.RouteHandle,
                                ReplanCount = (byte)status.ReplanCount,
                            });

                            if ((intent.Flags & (1 << NavigationConstants.FlagBitAutoSendPathOnReplan)) != 0)
                            {
                                repo.Bus.Publish(new NavigationPathDetailsResponseEvent
                                {
                                    Target        = entity,
                                    RouteHandle   = intent.RouteHandle,
                                    ReplanCount   = (byte)status.ReplanCount,
                                    IsAutoRefresh = 1,
                                });
                            }

                            // Throttle MoveBlockedEvent to once per frustration episode.
                            if (frustration.BlockedEventFired == 0)
                            {
                                repo.Bus.Publish(new MoveBlockedEvent
                                {
                                    Target = entity,
                                });
                                frustration.BlockedEventFired = 1;
                            }

                            frustration.Ticks = 0;
                            repo.SetComponent(entity, frustration);

                            status.Result = NavResult.InProgress;
                            repo.SetComponent(entity, status);
                            continue;
                        }
                        else
                        {
                            // ── Hard failure: replan budget exhausted or not allowed ────────────
                            status.Result = NavResult.FailedBlocked;
                            repo.SetComponent(entity, status);

                            repo.Bus.Publish(new MoveCompletedEvent
                            {
                                Target      = entity,
                                Reason      = NavResult.FailedBlocked,
                                RouteHandle = intent.RouteHandle,
                            });

                            frustration.Ticks = 0;
                            repo.SetComponent(entity, frustration);
                            continue;
                        }
                    }
                }
                else
                {
                    // Vehicle is moving — reset frustration counter.
                    if (frustration.Ticks != 0)
                    {
                        frustration.Ticks = 0;
                        repo.SetComponent(entity, frustration);
                    }
                }

                // ── Keep InProgress and persist ProgressS ────────────────────────────────────
                // Unconditional write ensures ProgressS is always persisted on the steady-state
                // InProgress path (no continue above was taken).
                if (status.Result != NavResult.InProgress)
                    status.Result = NavResult.InProgress;
                repo.SetComponent(entity, status);
            }
        }
    }
}
