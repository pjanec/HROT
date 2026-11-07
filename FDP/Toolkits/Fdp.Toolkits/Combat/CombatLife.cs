using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-466</c> — "is this unit still in the fight?", as the engine defines it.
    /// </summary>
    /// <remarks>
    /// Since the <c>CE-267</c> revert (<c>2026-09-13</c>) combat death is the STATE <c>Health.Current &lt;= 0</c> and the
    /// body stays in the world (<c>DamageSystem</c>, <c>HealthApplicationSystem</c>). ⛔ So <c>IsAlive</c> — ECS
    /// existence — is NOT a combat-death test: a knocked-out tank still exists. This helper is both halves: the entity
    /// exists AND, if it has a <see cref="Health"/> pool, the pool is above zero. An entity with no <see cref="Health"/>
    /// (an area, a marker) is "alive" while it exists.
    /// </remarks>
    public static class CombatLife
    {
        /// <summary>True while <paramref name="entity"/> exists and, when it has <see cref="Health"/>, has HP above zero.</summary>
        public static bool IsAlive(ISimulationView view, Entity entity)
        {
            if (!view.IsAlive(entity)) return false;
            return !view.HasComponent<Health>(entity) || view.GetComponentRO<Health>(entity).Current > 0f;
        }

        /// <summary>
        /// ⭐ W2 (docs/DESIGN_Visual_Effects.md §7 VE-N, R-269 (ui)) — <b>a wreck or a body:</b> the entity EXISTS, has a <see cref="Health"/>
        /// pool, and the pool is at or below zero. The one "destroyed" test every presentation reads (the near-black 3-D wreck, the
        /// 2-D X, the flames and smoke, the dead figure). An entity with no <see cref="Health"/> is never destroyed.
        /// <para>⚠ Not <c>!IsAlive</c>: a deleted entity is gone, not destroyed. When D1's damage states land
        /// (DESIGN_Operations_Behaviour_Catalogue.md §10.1) only this body changes.</para>
        /// </summary>
        public static bool IsDestroyed(ISimulationView view, Entity entity)
            => view.IsAlive(entity) && view.HasComponent<Health>(entity) && view.GetComponentRO<Health>(entity).Current <= 0f;

        /// <summary>
        /// ⭐ <c>CE-3092</c> — what a hit does to a unit's CAPABILITIES, the ONE rule (both damage systems call it).
        /// Death (<c>Current &lt;= 0</c>) and incapacitation (<see cref="Injury"/>, CE-2155) strip moving and shooting. A NON-lethal hit
        /// strips <c>CanMove</c> only on a unit type
        /// that opted in (<see cref="MobilityKill"/>) and only once health is below its fraction of max; every other unit keeps
        /// moving — so a hurt infantryman can still take cover or fall back. 🔒 User, <c>2026-10-06</c>: "death always stops; a
        /// non-lethal hit stops a unit only when its TKB platform says so". 📄 <c>docs/designs/packs-1/DESIGN.md</c> §2.B.
        /// <para>⚠ Was: every non-lethal hit stripped <c>CanMove</c> on every unit (PACK-M002, written for the UrbanCombat APC) —
        /// measured: one rifle round froze an infantryman for good (CE-3084 U4 red; CE-3094's soldier died where he stood).</para>
        /// </summary>
        public static void ApplyHitCapabilities(EntityRepository repo, Entity entity, in Health health)
        {
            if (!repo.HasComponent<ActorCapabilityState>(entity)) return;
            // ⭐ CE-2155 (R-2011 I6) — incapacitated stops like death does (move and fire), whatever MobilityKill says: down, not dead.
            if (health.Current <= 0f || Injury.StateOf(repo, entity, in health) == InjuryState.Incapacitated)
            {
                ref var dead = ref repo.GetComponentRW<ActorCapabilityState>(entity);
                dead.Capabilities &= ~(ActorCapabilities.CanMove | ActorCapabilities.CanShoot);
                return;
            }
            // ⭐⭐ CE-3211 (hit location §9 S2-8, R-263 D1) — a vehicle's damaged COMPARTMENT: its running gear or engine over the zone's
            //   threshold is the mobility kill, its turret or gun the firepower kill (derived from the replicated PartDamage, applied here
            //   where the capability bits live, like MobilityKill). Destroyed stays Health ≤ 0, above.
            if (HitStopsMove(repo, entity, in health))
                repo.GetComponentRW<ActorCapabilityState>(entity).Capabilities &= ~ActorCapabilities.CanMove;
            if (Fdp.Toolkit.Physics.BodyParts.Wounds.Has(repo, entity, Fdp.Toolkit.Tkb.Domain.WoundEffect.Firepower))
                repo.GetComponentRW<ActorCapabilityState>(entity).Capabilities &= ~ActorCapabilities.CanShoot;
        }

        /// <summary>
        /// The mobility half of <see cref="ApplyHitCapabilities"/> for a unit still in the fight — the ONE test, shared with
        /// <see cref="RestoreHitCapabilities"/> so what strips and what gives back cannot disagree: a Mobility-effect compartment over its
        /// threshold (CE-3211), else the type's <see cref="MobilityKill"/> health fraction (CE-3092).
        /// </summary>
        private static bool HitStopsMove(EntityRepository repo, Entity entity, in Health health)
        {
            if (Fdp.Toolkit.Physics.BodyParts.Wounds.Has(repo, entity, Fdp.Toolkit.Tkb.Domain.WoundEffect.Mobility)) return true;
            if (health.Current >= health.Max) return false;
            if (!repo.IsComponentTypeRegistered<MobilityKill>() || !repo.HasComponent<MobilityKill>(entity)) return false;
            float below = repo.GetComponentRO<MobilityKill>(entity).BelowFraction;
            return below > 0f && health.Current < health.Max * below;
        }

        /// <summary>
        /// ⭐ Initial state S5 (docs/UX/DESIGN_Initial_State.md §3, "Damage") — the inverse of <see cref="ApplyHitCapabilities"/> for an authored
        /// damage that was LOWERED: gives <c>CanMove</c> / <c>CanShoot</c> back when nothing the hit rule knows still takes them away
        /// (alive, not incapacitated, no Mobility / Firepower compartment wounded, under no <see cref="MobilityKill"/> threshold) AND nothing
        /// else does: not aboard anything (<c>IsEmbarkedTag</c>, boarding strips both), not assisting a team carry (<c>IsAssistingTag</c>),
        /// and — for <c>CanShoot</c> only, as <c>Embarkation.Board</c> strips only that — not a person carrying a casualty.
        /// A bit is granted only when the unit's TYPE grants it (<c>BehaviorProfileDto.CanMove</c> / <c>CanShoot</c>; no type known ⇒ granted).
        /// Run it BEFORE <see cref="ApplyHitCapabilities"/>, which stays the one that strips.
        /// </summary>
        public static void RestoreHitCapabilities(EntityRepository repo, Entity entity, in Health health)
        {
            if (!repo.IsComponentTypeRegistered<ActorCapabilityState>() || !repo.HasComponent<ActorCapabilityState>(entity)) return;
            if (health.Current <= 0f || Injury.StateOf(repo, entity, in health) == InjuryState.Incapacitated) return;
            if (repo.IsComponentTypeRegistered<IsEmbarkedTag>() && repo.HasComponent<IsEmbarkedTag>(entity)) return;
            if (repo.IsComponentTypeRegistered<IsAssistingTag>() && repo.HasComponent<IsAssistingTag>(entity)) return;

            var give = ActorCapabilities.None;
            var granted = TypeGrants(repo, entity);
            if (!HitStopsMove(repo, entity, in health)) give |= ActorCapabilities.CanMove & granted;
            if (!Fdp.Toolkit.Physics.BodyParts.Wounds.Has(repo, entity, Fdp.Toolkit.Tkb.Domain.WoundEffect.Firepower) && !CarriesCasualty(repo, entity))
                give |= ActorCapabilities.CanShoot & granted;
            if (give != ActorCapabilities.None) repo.GetComponentRW<ActorCapabilityState>(entity).Capabilities |= give;
        }

        /// <summary>The capabilities the unit's TYPE grants (its <c>BehaviorProfileDto</c>); everything when the type is unknown here.</summary>
        private static ActorCapabilities TypeGrants(EntityRepository repo, Entity entity)
        {
            const ActorCapabilities both = ActorCapabilities.CanMove | ActorCapabilities.CanShoot;
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>() || !repo.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(entity)
                || !repo.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>()
                || repo.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() is not { } db
                || !db.TryGetByType(repo.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(entity).TkbType, out var template)
                || template?.GetDescriptor<Fdp.Toolkit.Tkb.Domain.BehaviorProfileDto>() is not { } dto) return both;
            return (dto.CanMove ? ActorCapabilities.CanMove : 0) | (dto.CanShoot ? ActorCapabilities.CanShoot : 0);
        }

        /// <summary>Is <paramref name="entity"/> a person carrier with his hands full — a casualty or a team load (not a carried weapon)? The test
        /// <c>Embarkation.ReleaseCarrier</c> applies before giving fire back.</summary>
        private static bool CarriesCasualty(EntityRepository repo, Entity entity)
        {
            if (!Fdp.Toolkit.Behavior.Embarkation.IsPersonCarrier(repo, entity) || !repo.IsComponentTypeRegistered<PassengerBuffer>() || !repo.HasComponent<PassengerBuffer>(entity)) return false;
            ref readonly var list = ref repo.GetComponentRO<PassengerBuffer>(entity);
            if (!list.Assistant.IsNull) return true;
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
                if (!list.Passengers[i].IsNull && !Fdp.Toolkit.Behavior.Embarkation.IsWeaponSystem(repo, list.Passengers[i])) return true;
            return false;
        }
    }
}
