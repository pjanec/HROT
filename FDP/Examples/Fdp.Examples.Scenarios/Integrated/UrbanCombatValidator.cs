using Fdp.Examples.Common;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Examples.Scenarios.Integrated
{
    /// <summary>
    /// Stateful validator for the UrbanCombat scenario narrative.
    ///
    /// <para>Each call to <see cref="EvaluateTick"/> dynamically resolves the APC and
    /// Insurgent actors via their persistent <see cref="TkbIdentity"/> components, making
    /// the validator robust against serialisation/deserialisation round-trips that would
    /// otherwise invalidate cached <c>Entity</c> handles.</para>
    ///
    /// <para>Four sequential latches must fire in order:</para>
    /// <list type="number">
    ///   <item><term>AmbushFired</term>
    ///     <description>Insurgent's <see cref="WeaponChannel.ActiveAction"/> equals
    ///     <see cref="CombatConstants.ActionIdAimAndFire"/>.</description></item>
    ///   <item><term>ApcHalted</term>
    ///     <description>APC's <see cref="LocomotionChannel.ActiveAction"/> is 0 (MobilityLost
    ///     processed by HSM).</description></item>
    ///   <item><term>InsurgentHit</term>
    ///     <description>Insurgent health below maximum, or insurgent already dead.</description></item>
    ///   <item><term>InsurgentKilled</term>
    ///     <description>Insurgent entity no longer alive; returns <c>true</c>.</description></item>
    /// </list>
    ///
    /// <para>Throws <see cref="ScenarioFailureException"/> if <paramref name="tick"/> exceeds
    /// 600 before all latches fire.</para>
    /// </summary>
    public class UrbanCombatValidator
    {
        // ── TKB type constants (DEM1-D010 §9.2) ──────────────────────────────
        private const int TkbMilitaryApc = 2001;
        private const int TkbInsurgent   = 2003;

        // ── Sequential latch flags ────────────────────────────────────────────
        private bool _latchAmbushFired     = false;
        private bool _latchApcHalted       = false;
        private bool _latchInsurgentHit    = false;
        private bool _latchInsurgentKilled = false;

        // ── Cached entity references (survive entity destruction) ─────────────
        private Entity _cachedApc;
        private Entity _cachedInsurgent;
        private bool   _apcEverFound;
        private bool   _insurgentEverFound;

        /// <summary>True once the Insurgent's WeaponChannel.ActiveAction == AimAndFire.</summary>
        public bool LatchAmbushFired     => _latchAmbushFired;
        /// <summary>True once the APC's LocomotionChannel.ActiveAction == 0 (MobilityLost processed).</summary>
        public bool LatchApcHalted       => _latchApcHalted;
        /// <summary>True once the Insurgent's health dropped below maximum.</summary>
        public bool LatchInsurgentHit    => _latchInsurgentHit;
        /// <summary>True once the Insurgent entity is no longer alive.</summary>
        public bool LatchInsurgentKilled => _latchInsurgentKilled;

        /// <summary>
        /// Evaluates one simulation tick against the UrbanCombat narrative latches.
        /// </summary>
        /// <param name="tick">Current simulation tick number.</param>
        /// <param name="world">ECS repository to query — must have <see cref="TkbIdentity"/>
        ///   registered and set on APC and Insurgent entities.</param>
        /// <returns><c>true</c> once the Insurgent is killed (latch 4 fires).</returns>
        /// <exception cref="ScenarioFailureException">
        ///   Thrown if <paramref name="tick"/> exceeds 600 and not all latches have fired.
        /// </exception>
        public bool EvaluateTick(uint tick, EntityRepository world)
        {
            // ── Resolve actor entities via TkbIdentity each call ──────────────
            Entity apc       = default;
            Entity insurgent = default;
            bool   apcFound  = false;
            bool   insFound  = false;

            foreach (var entity in world.Query().With<TkbIdentity>().Build())
            {
                var id = world.GetComponent<TkbIdentity>(entity);
                if (id.TkbType == TkbMilitaryApc)
                {
                    apc           = entity;
                    apcFound      = true;
                    _cachedApc    = entity;
                    _apcEverFound = true;
                }
                else if (id.TkbType == TkbInsurgent)
                {
                    insurgent           = entity;
                    insFound            = true;
                    _cachedInsurgent    = entity;
                    _insurgentEverFound = true;
                }

                if (apcFound && insFound) break;
            }

            // Fall back to cached references for entities that have been destroyed
            // (destroyed entities leave queries but may still need latch evaluation).
            if (!apcFound && _apcEverFound)
            {
                apc      = _cachedApc;
                apcFound = true;
            }
            if (!insFound && _insurgentEverFound)
            {
                insurgent = _cachedInsurgent;
                insFound  = true;
            }

            // ── Latch 1: Insurgent fires (WeaponChannel.ActiveAction == AimAndFire) ──
            if (!_latchAmbushFired && insFound)
            {
                if (world.IsAlive(insurgent) &&
                    world.GetComponent<WeaponChannel>(insurgent).ActiveAction == CombatConstants.ActionIdAimAndFire)
                {
                    _latchAmbushFired = true;
                }
            }

            // ── Latch 2: APC halted (LocomotionChannel.ActiveAction == 0 after MobilityLost) ──
            if (!_latchApcHalted && _latchAmbushFired && apcFound)
            {
                bool halted = !world.IsAlive(apc)
                              || world.GetComponent<LocomotionChannel>(apc).ActiveAction == 0;
                if (halted)
                    _latchApcHalted = true;
            }

            // ── Latch 3: Insurgent hit (health < max, or already dead) ─────────
            if (!_latchInsurgentHit && _latchApcHalted && insFound)
            {
                if (!world.IsAlive(insurgent))
                {
                    // Insurgent died — set latches 3 and 4 simultaneously.
                    _latchInsurgentHit    = true;
                    _latchInsurgentKilled = true;
                }
                else if (world.HasComponent<Health>(insurgent))
                {
                    var hp = world.GetComponent<Health>(insurgent);
                    if (hp.Current < hp.Max)
                        _latchInsurgentHit = true;
                }
            }

            // ── Latch 4: Insurgent killed ─────────────────────────────────────
            if (!_latchInsurgentKilled && _latchInsurgentHit && insFound)
            {
                if (!world.IsAlive(insurgent))
                    _latchInsurgentKilled = true;
            }

            // ── Latch 5 / Success: narrative complete ─────────────────────────
            if (_latchInsurgentKilled)
            {
                FdpLog<UrbanCombatValidator>.Info(
                    $"[urbancombat] Phase 5 PASSED tick={tick} Mission Resumed " +
                    $"apc_alive={apcFound && world.IsAlive(apc)} " +
                    $"insurgent_alive={insFound && world.IsAlive(insurgent)}");
                return true;
            }

            // ── Timeout guard ─────────────────────────────────────────────────
            if (tick > 600)
            {
                throw new ScenarioFailureException(5,
                    $"Grand demo timed out. Latches: " +
                    $"ambush={_latchAmbushFired}, " +
                    $"halt={_latchApcHalted}, " +
                    $"hit={_latchInsurgentHit}, " +
                    $"killed={_latchInsurgentKilled}");
            }

            return false;
        }
    }
}
