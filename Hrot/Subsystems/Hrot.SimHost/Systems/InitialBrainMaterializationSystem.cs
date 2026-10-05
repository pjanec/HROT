using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-3042</c> (R-192) — starts a unit's SAVED AI the way an order would: each part of an
    /// <see cref="InitialBrainIntent"/> becomes the event an author would have published — <see cref="AssignBehaviorEvent"/>,
    /// <see cref="AssignSopEvent"/>, <see cref="SetRoeEvent"/> — at its SAVED origin, so the one gate decides and a reloaded
    /// unit starts exactly as if it had just been ordered (params parsed, slots provisioned, HSM initialised).
    /// 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.5.
    /// <para>⭐ The template's defaults (published at spawn at origin Sop / SetBy Unmarked) lose to the saved order by RANK, so
    /// the order the two arrive in does not matter. ⚠ Runs only where the events are registered (a brain node); elsewhere the
    /// intent is dropped — the node that runs the brain is the one that loaded it too.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class InitialBrainMaterializationSystem : IEcsModuleSystem
    {
        private readonly List<Entity> _done = new();

        /// <summary>Intents turned into orders since construction (a test / diagnostics probe).</summary>
        public int MaterializedCount { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;   // ⚠ the intent type is registered by GenesisIntentRegistry

            _done.Clear();
            foreach (var entity in repo.Query().WithManaged<InitialBrainIntent>().Build())
            {
                var intent = ((ISimulationView)repo).GetManagedComponentRO<InitialBrainIntent>(entity);
                if (intent.Roe is { } roe && repo.Bus.IsRegistered<SetRoeEvent>())
                    repo.Bus.Publish(new SetRoeEvent { Entity = entity, Fire = roe.Fire, Reactions = roe.Reactions, Origin = roe.SetBy,
                                                       ReturnFireWindowSeconds = roe.ReturnFireWindowSeconds });   // CE-2095
                if (intent.Sop is { } sop && repo.Bus.IsRegisteredManaged<AssignSopEvent>())
                    repo.Bus.PublishManaged(new AssignSopEvent { Entity = entity, BehaviorName = sop.Name, JsonParams = sop.Params, Origin = sop.Origin });
                if (intent.Behavior is { } task && repo.Bus.IsRegisteredManaged<AssignBehaviorEvent>())
                    repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = entity, BehaviorName = task.Name, JsonParams = task.Params, Origin = task.Origin });
                _done.Add(entity);
            }
            foreach (var entity in _done)
            {
                repo.RemoveComponent<InitialBrainIntent>(entity);
                MaterializedCount++;
            }
        }
    }
}
