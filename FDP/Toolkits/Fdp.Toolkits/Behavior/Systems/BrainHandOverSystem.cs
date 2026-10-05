using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Messages;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-3048</c> (V7) — when this node GAINS a unit's Brain, it runs what the previous owner published. 📄
    /// <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.7.
    /// <para>Triggered by <see cref="DescriptorAuthorityChanged"/> for the brain-intent descriptor (whichever path moved it:
    /// a grant, a debug transfer, a failover reclaim — all go through <c>OwnershipApplier</c>). Reads the unit's
    /// <see cref="ReplicatedBrainIntent"/> and <see cref="Replace">replaces</see> each slot that differs.</para>
    /// <para>⭐ It REPLACES, it does not order: the published intent is what the old owner ran, and the gaining node's own
    /// replica may hold a stale higher-origin order an order through the gate would lose to. A slot that is already the same
    /// keeps running (a first-time gainer usually already runs the template default).</para>
    /// <para>⚠ Network-agnostic: the network implementation names the key (R-165 — the grant is every implementation's
    /// contract).</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class BrainHandOverSystem : IEcsModuleSystem
    {
        private readonly BehaviorRegistry _registry;
        private readonly BehaviorIngressSystem _ingress;
        private readonly long _brainIntentKey;

        /// <summary>Hand-overs applied since construction (a test / diagnostics probe).</summary>
        public int HandOverCount { get; private set; }

        /// <param name="registry">The node's behaviour registry.</param>
        /// <param name="brainIntentKey">The packed key of the descriptor that carries the brain intent.</param>
        public BrainHandOverSystem(BehaviorRegistry registry, long brainIntentKey)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _ingress = new BehaviorIngressSystem(registry);
            _brainIntentKey = brainIntentKey;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.Bus.IsRegistered<DescriptorAuthorityChanged>()) return;
            foreach (var evt in view.ReadEvents<DescriptorAuthorityChanged>())
            {
                if (!evt.IsAuthoritative || evt.PackedKey != _brainIntentKey) continue;
                if (!repo.IsAlive(evt.Entity) || !repo.TryGetTable(typeof(ReplicatedBrainIntent), out _)
                    || !repo.HasManagedComponent<ReplicatedBrainIntent>(evt.Entity)) continue;
                var replica = view.GetManagedComponentRO<ReplicatedBrainIntent>(evt.Entity);
                if (replica == null) continue;
                Replace(repo, evt.Entity, replica.Intent, _registry, _ingress);
                HandOverCount++;
            }
        }

        /// <summary>
        /// Makes <paramref name="entity"/> run <paramref name="intent"/>: the ROE is written as published; a SOP or task slot
        /// that differs (name, params or origin — a running reaction always differs) is ended, then started through the
        /// ingress at its published origin; a slot that is the same keeps running.
        /// </summary>
        public static void Replace(EntityRepository repo, Entity entity, InitialBrainIntent intent, BehaviorRegistry registry,
                                   BehaviorIngressSystem ingress)
        {
            if (intent.Roe is { } roe && repo.IsComponentTypeRegistered<Roe>())
            {
                var value = new Roe { Fire = roe.Fire, Reactions = roe.Reactions, SetBy = roe.SetBy,
                                      ReturnFireWindowSeconds = roe.ReturnFireWindowSeconds };   // CE-2095
                if (repo.HasComponent<Roe>(entity)) repo.GetComponentRW<Roe>(entity) = value;
                else repo.AddComponent(entity, value);
            }

            bool hasTaskSlot = repo.IsComponentTypeRegistered<BehaviorState>() && repo.HasComponent<BehaviorState>(entity);
            bool taskDiffers = hasTaskSlot
                && (repo.GetComponentRO<BehaviorState>(entity).Origin == BehaviorOrigin.Reaction
                    || !BrainIntentReader.Same(BrainIntentReader.TaskOf(repo, entity, registry, BrainIntentScope.Running), intent.Behavior));
            if (taskDiffers)
            {
                BehaviorIngressSystem.DropPausedTask(repo, entity);
                BehaviorIngressSystem.Clear(repo, entity, registry);
            }

            if (!BrainIntentReader.Same(BrainIntentReader.SopOf(repo, entity, BrainIntentScope.Running), intent.Sop))
            {
                BehaviorIngressSystem.EndSop(repo, entity, registry);
                if (intent.Sop is { } sop && !ingress.AssignSopNow(repo, entity, sop.Name, sop.Params, sop.Origin))
                    Fdp.Core.Logging.FdpLog<BrainHandOverSystem>.Warn(
                        "[BrainHandOver] {0}: the published SOP '{1}' was refused here.", entity, sop.Name);
            }

            if (taskDiffers && intent.Behavior is { } task && !ingress.AssignNow(repo, entity, task.Name, task.Params, task.Origin))
                Fdp.Core.Logging.FdpLog<BrainHandOverSystem>.Warn(
                    "[BrainHandOver] {0}: the published task '{1}' was refused here.", entity, task.Name);
        }
    }
}
