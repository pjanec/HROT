using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-3219</c> (civilians B-1, CV-6 ⓐ, R-3013; docs/DESIGN_Civilians_And_Crowd_Control.md §7.3) — what a hit DOES besides its
    /// damage. Rides the one damage pipeline (<c>DamageAssessedEvent</c> → <c>EntityHitDamage</c> → the Health owner).
    /// </summary>
    public enum HitEffect : byte
    {
        /// <summary>Damage only (a bullet, a fragment, a blast) — every hit before CE-3219.</summary>
        Kinetic = 0,
        /// <summary>A baton round, a baton, a stone: knocks the man down.</summary>
        Blunt = 1,
        /// <summary>Tear gas: irritated (eyes, breath) for a while.</summary>
        Irritant = 2,
        /// <summary>A stun grenade: stunned for a while.</summary>
        Stun = 3,
        /// <summary>A shove (a shield line, later a water cannon): knocked down, briefly.</summary>
        Push = 4,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3219</c> (CV-6 ⓑ) — a person's STORED non-lethal effects: until when he is stunned, irritated, knocked down (sim seconds).
    /// ⛔ Never derived from Health — a stunned man is at full health; that is the case Injury_States' rule I1 names. Written only by
    /// <c>HealthApplicationSystem</c> on the Health owner, Brain group beside Health, replicated as <c>dtEffectState</c> (142).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.EffectState)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct EffectState
    {
        public double StunnedUntil;
        public double IrritatedUntil;
        public double KnockedDownUntil;
    }

    /// <summary>⭐ <c>CE-3219</c> — the one reader and the one writer rule of <see cref="EffectState"/>.</summary>
    public static class Effects
    {
        /// <summary>Extends the effect <paramref name="kind"/> names to at least <paramref name="now"/> + <paramref name="seconds"/>
        /// (a second hit never shortens a running effect). Returns false when the kind has no stored effect (Kinetic) or no time.</summary>
        public static bool Apply(ref EffectState state, HitEffect kind, float seconds, double now)
        {
            if (seconds <= 0f) return false;
            double until = now + seconds;
            switch (kind)
            {
                case HitEffect.Blunt:
                case HitEffect.Push:     state.KnockedDownUntil = Math.Max(state.KnockedDownUntil, until); return true;
                case HitEffect.Irritant: state.IrritatedUntil   = Math.Max(state.IrritatedUntil, until);   return true;
                case HitEffect.Stun:     state.StunnedUntil     = Math.Max(state.StunnedUntil, until);     return true;
                default: return false;
            }
        }

        public static bool IsStunned(EntityRepository repo, Entity e)     => Read(repo, e, out var s) && s.StunnedUntil > HitModel.Now(repo);
        public static bool IsIrritated(EntityRepository repo, Entity e)   => Read(repo, e, out var s) && s.IrritatedUntil > HitModel.Now(repo);
        public static bool IsKnockedDown(EntityRepository repo, Entity e) => Read(repo, e, out var s) && s.KnockedDownUntil > HitModel.Now(repo);

        private static bool Read(EntityRepository repo, Entity e, out EffectState state)
        {
            state = default;
            if (!repo.IsAlive(e) || !repo.IsComponentTypeRegistered<EffectState>() || !repo.HasComponent<EffectState>(e)) return false;
            state = repo.GetComponentRO<EffectState>(e);
            return true;
        }
    }
}
