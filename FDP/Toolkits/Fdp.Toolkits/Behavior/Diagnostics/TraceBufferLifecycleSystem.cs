using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    /// <summary>
    /// Reactive provisioning of per-entity trace buffer components based on the
    /// <see cref="DebugState.Behavior"/> bits.
    /// </summary>
    /// <remarks>
    /// Runs in <see cref="SystemPhase.BeforeSync"/> — after the
    /// <c>DebugStatePatchSystem</c> mutates flags in <see cref="SystemPhase.Input"/>
    /// and before the BTree/HSM tick systems read them in
    /// <see cref="SystemPhase.Simulation"/>.
    /// <para>
    /// When the <see cref="BehaviorDebugFlags.EnableTraceBuffer"/> bit flips on,
    /// the appropriate 1KB trace component is added based on the entity's
    /// <see cref="BehaviorState.BrainTier"/>. When the bit flips off, the
    /// component is removed so it stops dirtying the ECS chunk version.
    /// </para>
    /// </remarks>
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public sealed class TraceBufferLifecycleSystem : IEcsModuleSystem
    {
        // Buffer reused across ticks to avoid allocating per-call.
        private readonly List<Entity> _toAddBTree = new();
        private readonly List<Entity> _toRemoveBTree = new();
        private readonly List<Entity> _toAddHsm = new();
        private readonly List<Entity> _toRemoveHsm = new();

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(TraceBufferLifecycleSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            _toAddBTree.Clear();
            _toRemoveBTree.Clear();
            _toAddHsm.Clear();
            _toRemoveHsm.Clear();

            var q = repo.Query()
                .With<DebugState>()
                .With<BehaviorState>()
                .Build();

            foreach (var entity in q)
            {
                ref readonly var dbg = ref repo.GetComponentRO<DebugState>(entity);
                bool enabled = (dbg.Behavior & BehaviorDebugFlags.EnableTraceBuffer) != 0;

                byte tier = repo.GetComponentRO<BehaviorState>(entity).BrainTier;

                if (tier == BehaviorConstants.BrainTierBTree)
                {
                    bool present = repo.HasComponent<BTreeTraceWorkingMemory1024>(entity);
                    if (enabled && !present) _toAddBTree.Add(entity);
                    else if (!enabled && present) _toRemoveBTree.Add(entity);
                }
                else if (tier == BehaviorConstants.BrainTierHsm)
                {
                    bool present = repo.HasComponent<HsmTraceWorkingMemory1024>(entity);
                    if (enabled && !present) _toAddHsm.Add(entity);
                    else if (!enabled && present) _toRemoveHsm.Add(entity);
                }
            }

            // Apply structural changes outside the query.
            foreach (var e in _toAddBTree)
                repo.AddComponent(e, new BTreeTraceWorkingMemory1024());
            foreach (var e in _toRemoveBTree)
                repo.RemoveComponent<BTreeTraceWorkingMemory1024>(e);
            foreach (var e in _toAddHsm)
                repo.AddComponent(e, new HsmTraceWorkingMemory1024());
            foreach (var e in _toRemoveHsm)
                repo.RemoveComponent<HsmTraceWorkingMemory1024>(e);
        }
    }
}
