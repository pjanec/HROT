using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Behavior.Translators
{
    /// <summary>
    /// Translates <see cref="BehaviorProfileDto"/> into AI / behavior ECS components.
    /// Selects brain memory components based on <see cref="BehaviorProfileDto.BrainTier"/>:
    ///   <see cref="BehaviorConstants.BrainTierHsm"/> = FastHSM (<see cref="BrainHsm128"/>),
    ///   <see cref="BehaviorConstants.BrainTierBTree"/> = FastBTree (<see cref="BrainBTreeState"/>).
    /// </summary>
    public sealed class BehaviorTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors()
        {
            yield return typeof(BehaviorProfileDto);
        }

        /// <summary>
        /// ⚠ Declared UNCONDITIONALLY, including the branch-selected pair
        /// (<see cref="BrainBTreeState"/> / <see cref="BrainHsm128"/>, chosen by
        /// <c>BrainTier</c>) — see the interface's contract for why over-declaring here is free and
        /// omitting is not. ⭐ <see cref="EntityInfo"/> is the one entry carrying
        /// <c>[PerInstanceValue]</c>: its <c>ForceId</c> is a TEMPLATE default that a per-spawn faction
        /// must beat, which is exactly what the promotion gate protects.
        /// </summary>
        public IEnumerable<Type> GetProducedComponents()
        {
            yield return typeof(SimTier);
            yield return typeof(EntityInfo);
            yield return typeof(ActorCapabilityState);
            yield return typeof(PreviousCapabilities);
            yield return typeof(BehaviorState);
            yield return typeof(LocomotionChannel);
            yield return typeof(WeaponChannel);
            yield return typeof(InteractionChannel);
            yield return typeof(MissionPlanQueue);
            yield return typeof(PassengerBuffer);
            yield return typeof(BrainBTreeState);
            yield return typeof(BrainHsm128);
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            var dto = template.GetDescriptor<BehaviorProfileDto>();
            if (dto == null) return;

            // ── Sim tier ──────────────────────────────────────────────────────────
            if (repo.IsComponentTypeRegistered<SimTier>() && !repo.HasComponent<SimTier>(entity))
                repo.AddComponent(entity, new SimTier { Value = dto.SimTier });

            // ── Force affiliation ─────────────────────────────────────────────────
            if (repo.IsComponentTypeRegistered<EntityInfo>() && !repo.HasComponent<EntityInfo>(entity))
                repo.AddComponent(entity, new EntityInfo { ForceId = dto.Faction });

            // ── Actor capabilities ────────────────────────────────────────────────
            var caps = ActorCapabilities.None;
            if (dto.CanMove)     caps |= ActorCapabilities.CanMove;
            if (dto.CanShoot)    caps |= ActorCapabilities.CanShoot;
            if (dto.CanInteract) caps |= ActorCapabilities.CanInteract;

            if (repo.IsComponentTypeRegistered<ActorCapabilityState>() && !repo.HasComponent<ActorCapabilityState>(entity))
                repo.AddComponent(entity, new ActorCapabilityState { Capabilities = caps });

            if (repo.IsComponentTypeRegistered<PreviousCapabilities>() && !repo.HasComponent<PreviousCapabilities>(entity))
                repo.AddComponent(entity, new PreviousCapabilities { Capabilities = caps });

            // ── Behavior state ────────────────────────────────────────────────────
            // Always stamped when a BehaviorProfileDto is present so that SpawnEntity
            // can unconditionally read/write BehaviorState regardless of brain tier.
            if (repo.IsComponentTypeRegistered<BehaviorState>() && !repo.HasComponent<BehaviorState>(entity))
                repo.AddComponent(entity, new BehaviorState
                {
                    ActiveBehaviorHash = dto.DefaultBehaviorHash,
                    BrainTier          = dto.BrainTier,
                    InstanceId         = 1
                });

            // ── LocomotionChannel: all moveable entities (including tier-0 civilians
            //    driven by TrafficBrainSystem) need a locomotion channel so the system
            //    can write ActiveAction = Flee / MoveTo each frame.
            if (dto.CanMove && repo.IsComponentTypeRegistered<LocomotionChannel>() && !repo.HasComponent<LocomotionChannel>(entity))
                repo.AddComponent(entity, new LocomotionChannel());

            // Only stamp high-fidelity tactical components when a brain tier is set.
            if (dto.BrainTier == 0) return;

            // ── Action channels (tactical only) ───────────────────────────────────
            // LocomotionChannel already added above for any CanMove entity;
            // add it again only when it was skipped (CanMove == false but BrainTier != 0).
            if (!dto.CanMove && repo.IsComponentTypeRegistered<LocomotionChannel>() && !repo.HasComponent<LocomotionChannel>(entity))
                repo.AddComponent(entity, new LocomotionChannel());

            if (repo.IsComponentTypeRegistered<WeaponChannel>() && !repo.HasComponent<WeaponChannel>(entity))
                repo.AddComponent(entity, new WeaponChannel());

            if (repo.IsComponentTypeRegistered<InteractionChannel>() && !repo.HasComponent<InteractionChannel>(entity))
                repo.AddComponent(entity, new InteractionChannel());

            // ── Mission and passenger buffers ─────────────────────────────────────
            if (repo.IsComponentTypeRegistered<MissionPlanQueue>() && !repo.HasComponent<MissionPlanQueue>(entity))
                repo.AddComponent(entity, new MissionPlanQueue());

            if (repo.IsComponentTypeRegistered<PassengerBuffer>() && !repo.HasComponent<PassengerBuffer>(entity))
                repo.AddComponent(entity, new PassengerBuffer());

            // ── Brain memory ──────────────────────────────────────────────────────
            if (dto.BrainTier == BehaviorConstants.BrainTierBTree)
            {
                if (repo.IsComponentTypeRegistered<BrainBTreeState>() && !repo.HasComponent<BrainBTreeState>(entity))
                    repo.AddComponent(entity, new BrainBTreeState());
            }
            else if (dto.BrainTier == BehaviorConstants.BrainTierHsm)
            {
                if (repo.IsComponentTypeRegistered<BrainHsm128>() && !repo.HasComponent<BrainHsm128>(entity))
                    repo.AddComponent(entity, new BrainHsm128());
            }

            // ⛔⛔ P4 §2 ② (2026-09-22) — THE BrainBlackboard ATTACH IS GONE WITH THE COMPONENT.
            //   🔴 This was the ONLY site that attached it, and it attached an EMPTY one: nothing had
            //   filled BehaviorParameters since P3-C moved params to the root occurrence slot, so four
            //   debug surfaces rendered a permanently-zero region and StructEdit bound editable fields
            //   to it (CE-312). ⚠ Its absence is also what un-gates BTreeTickSystem's query (CE-315).

            // ⭐⭐ O2 (2026-09-20) — the entity-fact tail rides the fact that USED to carry the
            //   blackboard: "this template has a brain". ⛔ Hooked here rather than at a consumer —
            //   B1 learned that hooking a path instead of the fact leaves the component missing
            //   wherever a second path exists. This is the one production site that gives an entity a
            //   brain. ⚠ It now stands alone; the line it used to mirror is deleted above.
            if (repo.IsComponentTypeRegistered<BrainInterrupts>() && !repo.HasComponent<BrainInterrupts>(entity))
                repo.AddComponent(entity, new BrainInterrupts());
        }
    }
}
