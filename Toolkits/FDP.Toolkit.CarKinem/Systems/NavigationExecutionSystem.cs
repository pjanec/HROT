using System.Collections.Generic;
using System.Numerics;
using CarKinem.Core;
using Fdp.Kernel;
using FDP.Toolkit.Navigation;

// Disambiguate from CarKinem.Core.NavigationMode which exists for the legacy NavState.
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
    ///     InProgress and clear the frustration counter.</item>
    ///   <item>Check Cartesian distance from current XY position to
    ///     <c>intent.FinalDestination</c>. If within <c>ArrivalRadius</c> → write Arrived.</item>
    ///   <item>Else if <c>SimVelocity.Linear.Length() &lt; FrustrationSpeedThreshold</c> for
    ///     more than <see cref="FrustrationTickLimit"/> consecutive ticks → write FailedBlocked.</item>
    ///   <item>Else keep InProgress.</item>
    /// </list>
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

        // ── Per-entity frustration counter ─────────────────────────────────────────────────────
        // Keyed by entity Index (not Entity handle) for O(1) access.
        // The dictionary is allocated once at system creation and reused.
        private readonly Dictionary<int, int> _frustrationTicks = new();

        // ── OnUpdate ───────────────────────────────────────────────────────────────────────────

        protected override void OnUpdate()
        {
            var query = World.Query()
                .With<NavigationIntent>()
                .With<NavigationStatus>()
                .With<SimTransform>()
                .With<SimVelocity>()
                .Build();

            foreach (var entity in query)
            {
                var intent = World.GetComponent<NavigationIntent>(entity);

                // ── Skip inactive intents ──────────────────────────────────────────────────────
                if (intent.Mode == NavMode.None)
                    continue;

                var status = World.GetComponent<NavigationStatus>(entity);
                var tf     = World.GetComponent<SimTransform>(entity);
                var vel    = World.GetComponent<SimVelocity>(entity);

                // ── New command detection: reset status and frustration counter ────────────────
                if (status.IntentId != intent.IntentId)
                {
                    status = new NavigationStatus
                    {
                        IntentId = intent.IntentId,
                        Result   = NavResult.InProgress,
                    };
                    World.SetComponent(entity, status);
                    _frustrationTicks[entity.Index] = 0;
                }

                // ── Arrival check (Cartesian XY only — no geo conversion) ─────────────────────
                var pos2D    = new Vector2(tf.Position.X, tf.Position.Y);
                float dist   = Vector2.Distance(pos2D, intent.FinalDestination);

                if (dist <= intent.ArrivalRadius)
                {
                    status.Result = NavResult.Arrived;
                    World.SetComponent(entity, status);
                    _frustrationTicks.Remove(entity.Index);
                    continue;
                }

                // ── Frustration guard ─────────────────────────────────────────────────────────
                float speed = vel.Linear.Length();

                if (speed < FrustrationSpeedThreshold)
                {
                    _frustrationTicks.TryGetValue(entity.Index, out int ticks);
                    ticks++;
                    _frustrationTicks[entity.Index] = ticks;

                    if (ticks > FrustrationTickLimit)
                    {
                        status.Result = NavResult.FailedBlocked;
                        World.SetComponent(entity, status);
                        continue;
                    }
                }
                else
                {
                    // Vehicle is moving — reset frustration counter.
                    _frustrationTicks[entity.Index] = 0;
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
