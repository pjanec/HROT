using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Behavior.Translators
{
    /// <summary>
    /// Translates <see cref="BehaviorProfileDto"/> into AI / behavior ECS components.
    ///
    /// <para>⛔ <b><c>O7c</c>-④d (2026-09-23): there are no brain MEMORY COMPONENTS to select any
    /// more.</b> <c>BrainTier</c> still branches here, but only the BTree arm provisions anything —
    /// a root occurrence slot via <see cref="RootStateAccess.EnsureRootState"/>. 📄 §31.19.</para>
    /// </summary>
    public sealed class BehaviorTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors()
        {
            yield return typeof(BehaviorProfileDto);
        }

        /// <summary>
        /// ⚠ The branch-selected brain pair is no longer here at all — both roots are occurrence
        /// slots (<c>O7c</c>). ⭐ <see cref="EntityInfo"/> is the one entry carrying
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
            // ⛔ O7c-④d: BrainHsm128 is gone. There is no brain COMPONENT left to declare — both
            //   roots are occurrence slots, and the store's tier component is declared by the
            //   blueprint side, not here.
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
                // ⭐⭐⭐ O7c-② / CE-319 — PROVISION THE ROOT STATE SLOT AT SPAWN.
                //   🔴🔴 THIS IS THE LOAD-BEARING HALF OF THE MOVE, and it is NOT where the design put it.
                //   📐 Measured: this translator stamps BehaviorState.ActiveBehaviorHash from the
                //   template's DEFAULT behaviour and NO AssignBehaviorEvent is published at spawn — the
                //   assign path runs only from mission/intent (MissionDirectorSystem,
                //   TacticalIntentResolutionSystem, the maneuver mappers). ⇒ an entity that spawns with
                //   a default behaviour would reach the tick with NO root state slot, and
                //   RootStateAccess.RequireStateRef would throw where the component silently ticked
                //   from the root.
                //
                //   ⚠ THE ASYMMETRY WITH ROOT PARAMS IS WHY THIS BITES HERE AND NOT THERE: the params
                //   path is entered only when RootParamsBytes(def) > 0, so a params-less behaviour never
                //   touches it. EVERY BTree behaviour has a cursor. ⇒ the state slot must exist for a
                //   strictly larger set of entities than the params slot does.
                //
                //   🔒 "Before moving ANY state into an occurrence slot, name what will PROVISION the
                //   slot and what will WRITE its contents." The writer is BTreeTickSystem; the
                //   provisioner is THIS site at spawn and BehaviorIngressSystem on every assign after.
                RootStateAccess.EnsureRootState(repo, entity, dto.DefaultBehaviorHash);
            }
            // ⛔⛔ O7c-④d (2026-09-23) — THE HSM ARM IS GONE, AND IT HAS NO SLOT-BASED REPLACEMENT.
            //   📄 §31.15.1 measured why, and the answer is that the arm PROVISIONED NOTHING USABLE:
            //   it attached a ZEROED BrainHsm128, whose Header.MachineId is 0, and
            //   HsmKernelCore.ValidateInstance rejects exactly that by `continue`. ⇒ an entity that
            //   spawned with a default HSM behaviour and never received an assign carried a component
            //   the kernel refused to step — the attach bought reachability, not a running machine.
            //   ⛔ It cannot be ported: sizing the slot needs SelectTier(blob), the blob comes from
            //   the BehaviorRegistry, and TkbTranslatorSet.Base() holds no registry. ⭐ Provisioning
            //   an HSM instance is therefore BehaviorIngressSystem's alone — a faithful port of what
            //   this site actually achieved, not a narrowing.
            //   ⚠ The BTree arm above is NOT symmetric with this and must stay: every BTree behaviour
            //   has a cursor, and sizeof(BehaviorTreeState) is a compile-time constant.

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
