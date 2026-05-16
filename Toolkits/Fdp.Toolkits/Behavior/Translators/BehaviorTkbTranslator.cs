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

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            var dto = template.GetDescriptor<BehaviorProfileDto>();
            if (dto == null) return;

            // ── Sim tier ──────────────────────────────────────────────────────────
            if (repo.IsComponentTypeRegistered<SimTier>())
                repo.AddComponent(entity, new SimTier { Value = dto.SimTier });

            // ── Actor capabilities ────────────────────────────────────────────────
            var caps = ActorCapabilities.None;
            if (dto.CanMove)     caps |= ActorCapabilities.CanMove;
            if (dto.CanShoot)    caps |= ActorCapabilities.CanShoot;
            if (dto.CanInteract) caps |= ActorCapabilities.CanInteract;

            if (repo.IsComponentTypeRegistered<ActorCapabilityState>())
                repo.AddComponent(entity, new ActorCapabilityState { Capabilities = caps });

            if (repo.IsComponentTypeRegistered<PreviousCapabilities>())
                repo.AddComponent(entity, new PreviousCapabilities { Capabilities = caps });

            // Only stamp high-fidelity tactical components when a brain tier is set.
            if (dto.BrainTier == 0) return;

            // ── Behavior state ────────────────────────────────────────────────────
            if (repo.IsComponentTypeRegistered<BehaviorState>())
                repo.AddComponent(entity, new BehaviorState
                {
                    ActiveBehaviorHash = dto.DefaultBehaviorHash,
                    BrainTier          = dto.BrainTier,
                    InstanceId         = 1
                });

            // ── Action channels ───────────────────────────────────────────────────
            if (repo.IsComponentTypeRegistered<LocomotionChannel>())
                repo.AddComponent(entity, new LocomotionChannel());

            if (repo.IsComponentTypeRegistered<WeaponChannel>())
                repo.AddComponent(entity, new WeaponChannel());

            if (repo.IsComponentTypeRegistered<InteractionChannel>())
                repo.AddComponent(entity, new InteractionChannel());

            // ── Mission and passenger buffers ─────────────────────────────────────
            if (repo.IsComponentTypeRegistered<MissionPlanQueue>())
                repo.AddComponent(entity, new MissionPlanQueue());

            if (repo.IsComponentTypeRegistered<PassengerBuffer>())
                repo.AddComponent(entity, new PassengerBuffer());

            // ── Brain memory ──────────────────────────────────────────────────────
            if (dto.BrainTier == BehaviorConstants.BrainTierBTree)
            {
                if (repo.IsComponentTypeRegistered<BrainBTreeState>())
                    repo.AddComponent(entity, new BrainBTreeState());
            }
            else if (dto.BrainTier == BehaviorConstants.BrainTierHsm)
            {
                if (repo.IsComponentTypeRegistered<BrainHsm128>())
                    repo.AddComponent(entity, new BrainHsm128());
            }

            if (repo.IsComponentTypeRegistered<BrainBlackboard>())
                repo.AddComponent(entity, new BrainBlackboard());
        }
    }
}
