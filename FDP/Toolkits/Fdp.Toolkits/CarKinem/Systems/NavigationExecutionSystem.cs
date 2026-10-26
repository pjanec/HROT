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
    [TickMode(TickPolicy.WhenTimeAdvances)]
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

        /// <summary>
        /// ⭐ <c>CE-2304</c> — the path progress (metres) a walking move must gain within <see cref="NoProgressLimitSeconds"/>.
        /// A man crawling prone still gains it in about a second.
        /// </summary>
        public const float NoProgressMinGainM = 0.25f;

        /// <summary>
        /// ⭐ <c>CE-2304</c> — seconds without <see cref="NoProgressMinGainM"/> of progress before a move that is MOVING but getting
        /// nowhere counts as a frustration episode (replan, or <see cref="NavigationResult.FailedBlocked"/>).
        /// </summary>
        public const float NoProgressLimitSeconds = 4f;

        // ── OnUpdate ───────────────────────────────────────────────────────────────────────────

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // ⭐ CE-3329 P2 — a replay: the recording owns the world (DESIGN_Replay_Time_Axis_And_Review §12l)
            // No frustration accumulates on a paused/frozen frame: the system is not called on one.
            // ⭐ CE-3293 S5 (R-3036 rule 8) — no hand pause gate: [TickMode(WhenTimeAdvances)] keeps this system off a frame that does not advance

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
                // ⭐ CE-2304 — a RE-SENT move (a new IntentId, the same point, the path still being walked) is the SAME move: it takes
                //   the new id and keeps its stuck counts. 📌 Live in sq-defend-house the brain re-sent each post move ~2×/s; every
                //   re-send restarted the count, so no watchdog ever reached its limit and the move stayed InProgress for good.
                if (status.IntentId != intent.IntentId && status.Result == NavResult.InProgress
                    && repo.HasComponent<NavState>(entity) && IsResentMove(repo, entity, in intent, in repo.GetComponentRO<NavState>(entity)))
                {
                    status.IntentId = intent.IntentId;
                    repo.SetComponent(entity, status);
                }

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
                // ⭐ Buildings 5d-3 — a DELIBERATE hold (NavState.IsBlocked: waiting at a door it is opening) is not being
                //   stuck: it neither counts toward a replan nor raises MoveBlockedEvent. The count restarts when it ends.
                // ⭐ CE-3286 (R-3025) — nor is a hull BACKING OUT of a wall (HullRecovery): the recovery is this watchdog's remedy, not stuck.
                if (repo.HasComponent<NavState>(entity) && repo.GetComponent<NavState>(entity).IsBlocked != 0 || frustration.RecoverDir != 0)
                {
                    if (frustration.Ticks != 0 || frustration.NoProgressSeconds != 0f)
                    {
                        frustration.Ticks = 0;
                        frustration.NoProgressSeconds = 0f;
                        frustration.BestProgressS = progressAtThisTick;
                        repo.SetComponent(entity, frustration);
                    }
                    repo.SetComponent(entity, status);   // ProgressS persisted, as on the steady-state path
                    continue;
                }

                float speed = vel.Linear.Length();
                bool stalled = Stalled(repo, entity, ref frustration, progressAtThisTick, deltaTime);   // ⭐ CE-2304

                if (speed < FrustrationSpeedThreshold || stalled)
                {
                    frustration.Ticks++;
                    frustration.ElapsedSinceFirstReplan += deltaTime;
                    repo.SetComponent(entity, frustration);

                    if (frustration.Ticks > FrustrationTickLimit || stalled)
                    {
                        // ⭐ CE-2304 — the episode is spent either way: the no-progress window starts again from here
                        frustration.NoProgressSeconds = 0f;
                        frustration.BestProgressS = progressAtThisTick;

                        // §3.4 — allowed by the intent, within the replan and time budgets (one test, shared with the door passage system)
                        if (CanReplan(in intent, in status, frustration.ElapsedSinceFirstReplan))
                        {
                            // ── Internal Muscle replan ─────────────────────────────────────────
                            RequestReplan(repo, entity, in intent, ref status, tf.Position);

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
                            continue;
                        }
                        else
                        {
                            // ── Hard failure: replan budget exhausted or not allowed ────────────
                            FailBlocked(repo, entity, in intent, ref status);

                            frustration.Ticks = 0;
                            repo.SetComponent(entity, frustration);
                            continue;
                        }
                    }
                }
                else
                {
                    // Vehicle is moving — reset frustration counter. ⭐ CE-2304 — written every tick: the no-progress window moved
                    frustration.Ticks = 0;
                    repo.SetComponent(entity, frustration);
                }

                // ── Keep InProgress and persist ProgressS ────────────────────────────────────
                // Unconditional write ensures ProgressS is always persisted on the steady-state
                // InProgress path (no continue above was taken).
                if (status.Result != NavResult.InProgress)
                    status.Result = NavResult.InProgress;
                repo.SetComponent(entity, status);
            }
        }
        /// <summary>⭐ <c>CE-2304</c> — how close (metres, horizontally) a re-sent move's point must be to the walked path's end to be the same move.</summary>
        public const float ResentMoveToleranceM = 0.25f;

        /// <summary>
        /// ⭐ <c>CE-2304</c> — is <paramref name="intent"/> a RE-SEND of the move <paramref name="nav"/> is already walking: a path-to-point
        /// to the same point (<see cref="ResentMoveToleranceM"/> across, within a storey) while the path is followed and not yet arrived?
        /// The one test the intent bridge (keep the path, ask no new one) and this watchdog (keep the counts) share.
        /// ⭐ <c>Q94 C</c> — compared at the RESOLVED point (<see cref="NavigationDestination.Of"/>), the one the path was planned to:
        /// a door move names the door's centre but walks to its near side, and an on-surface move's Z is resolved too.
        /// </summary>
        public static bool IsResentMove(ISimulationView view, Entity mover, in NavigationIntent intent, in NavState nav)
        {
            if (intent.Mode != NavMode.PathToPoint || nav.Mode != KinematicsMode.CustomTrajectory || nav.HasArrived != 0 || nav.TargetSpeed <= 0f)
                return false;
            var to = NavigationDestination.Of(view, mover, intent);
            return Vector2.Distance(new Vector2(to.X, to.Y), new Vector2(nav.FinalDestination.X, nav.FinalDestination.Y)) <= ResentMoveToleranceM
                   && MathF.Abs(to.Z - nav.FinalDestination.Z) <= 1f;
        }

        /// <summary>
        /// ⭐ <c>CE-2304</c> — is this move MOVING BUT GETTING NOWHERE? A body on a path (<see cref="KinematicsMode.CustomTrajectory"/>,
        /// asked to move) that has not gained <see cref="NoProgressMinGainM"/> of path progress in <see cref="NoProgressLimitSeconds"/>.
        /// The speed watchdog cannot see it: 📌 seven men pressed in two small rooms kept walking at ~0.4 m/s with their progress flat,
        /// <c>InProgress</c> for good. A new path (progress back below the best) restarts the window.
        /// </summary>
        private static bool Stalled(EntityRepository repo, Entity entity, ref FrustrationTicks frustration, float progress, float deltaTime)
        {
            if (!repo.HasComponent<NavState>(entity)) return false;
            ref readonly var nav = ref repo.GetComponentRO<NavState>(entity);
            if (nav.Mode != KinematicsMode.CustomTrajectory || nav.TargetSpeed <= 0f || nav.HasArrived != 0) return false;

            if (progress >= frustration.BestProgressS + NoProgressMinGainM || progress < frustration.BestProgressS)
            {
                frustration.BestProgressS = progress;
                frustration.NoProgressSeconds = 0f;
                return false;
            }
            frustration.NoProgressSeconds += deltaTime;
            return frustration.NoProgressSeconds >= NoProgressLimitSeconds;
        }

        /// <summary>
        /// ⭐ May <paramref name="entity"/>'s move be replanned now? Its intent allows replans, the replan budget is not spent and
        /// the time budget not exceeded (§3.4). The frustration watchdog and the door passage system (5d-4) ask the same question.
        /// </summary>
        public static bool CanReplan(in NavigationIntent intent, in NavigationStatus status, float elapsedSinceFirstReplan)
        {
            byte effectiveMax = intent.MaxReplans != 0 ? intent.MaxReplans : NavigationConstants.DefaultMaxReplans;
            bool allowReplan = (intent.Flags & (1 << NavigationConstants.FlagBitAllowReplan)) != 0;
            bool timeBudgetExceeded = intent.ReplanTimeBudget > 0f && elapsedSinceFirstReplan >= intent.ReplanTimeBudget;
            return allowReplan && status.ReplanCount < effectiveMax && !timeBudgetExceeded;
        }

        /// <summary>
        /// ⭐ THE Muscle-side replan: a new path request from <paramref name="from"/> to the intent's destination, the
        /// <see cref="PathReplannedEvent"/> (and the auto path-details refresh when the intent asks for it). One implementation for the
        /// frustration watchdog and the door passage system (5d-4: a door ahead was locked).
        /// </summary>
        public static void RequestReplan(EntityRepository repo, Entity entity, in NavigationIntent intent, ref NavigationStatus status, Vector3 from)
        {
            status.ReplanCount++;
            status.Result = NavResult.InProgress;
            repo.SetComponent(entity, status);

            // ⭐ CE-3128 — the same request the first plan made (PathRequests.FromIntent): the order's backend, layer and road
            //   use. ⛔ Before, this was built by hand and dropped BackendForce and the intent's layer mask (it passed 0 — the
            //   entity's own layer, CE-3025 — which FromIntent still yields when the intent asks for none).
            repo.Bus.Publish(Fdp.Toolkit.Navigation.PathRequests.FromIntent(repo, entity, in intent, from,
                (long)entity.Index << 32 | (uint)status.ReplanCount));

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
        }

        /// <summary>⭐ The move cannot go on: <see cref="NavResult.FailedBlocked"/> and its <see cref="MoveCompletedEvent"/>.</summary>
        public static void FailBlocked(EntityRepository repo, Entity entity, in NavigationIntent intent, ref NavigationStatus status)
        {
            status.Result = NavResult.FailedBlocked;
            repo.SetComponent(entity, status);
            repo.Bus.Publish(new MoveCompletedEvent
            {
                Target      = entity,
                Reason      = NavResult.FailedBlocked,
                RouteHandle = intent.RouteHandle,
            });
        }
    }
}
