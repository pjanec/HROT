using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Examples.NetworkDemo.Events;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Core.Logging;
using Fdp.Interfaces;

namespace Fdp.Examples.NetworkDemo.Systems
{
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class CombatFeedbackSystem : IEcsModuleSystem, IDisposable
    {
        private readonly int _localNodeId;
        private readonly IEventBus _bus;

        public CombatFeedbackSystem(int localNodeId, IEventBus bus)
        {
            _localNodeId = localNodeId;
            _bus = bus;
        }

        public void Execute(ISimulationView view, float dt)
        {
            var ecb = view.GetCommandBuffer();

            // 1. Process ECS Events (from EntityRepository)
            var ecsEvents = view.ConsumeEvents<FireInteractionEvent>();
            foreach (ref readonly var evt in ecsEvents)
            {
                ProcessEvent(evt, view, ecb);
            }

            // 2. Process Bus Events (from FdpEventBus/Translators)
            if (_bus is FdpEventBus fdpBus)
            {
                var busEvents = fdpBus.Consume<FireInteractionEvent>();
                foreach (ref readonly var evt in busEvents)
                {
                    ProcessEvent(evt, view, ecb);
                }
            }
        }

        private void ProcessEvent(FireInteractionEvent evt, ISimulationView view, IEntityCommandBuffer cmd )
        {
            FdpLog<CombatFeedbackSystem>.Info(
                $"[Combat] Fire event: Attacker={evt.AttackerRoot.Index} " +
                $"Target={evt.TargetRoot.Index} " +
                $"Weapon={evt.WeaponInstanceId} " +
                $"Damage={evt.Damage}");

            if (view.HasComponent<NetworkOwnership>(evt.TargetRoot))
            {
                ref readonly var own = ref view.GetComponentRO<NetworkOwnership>(evt.TargetRoot);

                if (own.PrimaryOwnerId == _localNodeId)
                {
                    if (view.HasComponent<Health>(evt.TargetRoot))
                    {
                        ref readonly var originalHealth = ref view.GetComponentRO<Health>(evt.TargetRoot);
                        var health = originalHealth;

                        health.Value -= evt.Damage;
                        if (health.Value < 0) health.Value = 0;

                        cmd.SetComponent(evt.TargetRoot, health);

                        FdpLog<CombatFeedbackSystem>.Info(
                            $"[Damage] Applied {evt.Damage} damage. " +
                            $"Health: {health.Value}/{health.MaxValue}");
                        
                        if (health.Value <= 0)
                        {
                            FdpLog<CombatFeedbackSystem>.Warn("[Destroyed] Tank destroyed!");
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            // No resources to dispose currently
        }
    }
}
