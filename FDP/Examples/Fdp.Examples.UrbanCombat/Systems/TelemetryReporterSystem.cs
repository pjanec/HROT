using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fhsm.Kernel;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Navigation;

namespace Fdp.Examples.UrbanCombat.Systems
{
    /// <summary>
    /// Export-phase telemetry reporter for the Urban Ambush scenario.
    /// Emits structured log lines to <see cref="System.Console.Out"/> whenever
    /// significant simulation events occur, enabling integration tests to assert on
    /// milestone strings in the captured output.
    /// </summary>
    [UpdateInPhase(SystemPhase.Export)]
    public class TelemetryReporterSystem : IEcsModuleSystem
    {
        // Shadow state for change detection.
        private readonly Dictionary<int, uint>              _prevBehaviorInstanceId = new Dictionary<int, uint>();
        private readonly Dictionary<int, ActorCapabilities> _prevCapabilities       = new Dictionary<int, ActorCapabilities>();
        private readonly Dictionary<int, ushort>            _prevHsmState           = new Dictionary<int, ushort>();

        private int _frame;

        // Cached queries (lazy-initialised on first Execute).
        private EntityQuery? _qBehavior;
        private EntityQuery? _qCaps;
        private EntityQuery? _qHsm;
        private EntityQuery? _qInteract;
        private EntityQuery? _qLoco;

        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            var repo = (EntityRepository)view;
            _frame++;
            string frameTag = $"[FRAME {_frame:D4}]";

            // -- Bus events (GUNFIRE, HIT) --

            var fireIntents = view.ReadEvents<WeaponFireIntent>();
            for (int i = 0; i < fireIntents.Length; i++)
            {
                ref readonly var evt = ref fireIntents[i];
                System.Console.Out.WriteLine($"{frameTag} GUNFIRE: shooter #{evt.Shooter.Index}");
            }

            var hitEvents = view.ReadEvents<HitEvent>();
            for (int i = 0; i < hitEvents.Length; i++)
            {
                ref readonly var evt = ref hitEvents[i];
                System.Console.Out.WriteLine($"{frameTag} HIT: target {evt.HitEntity.Index}");
            }

            // -- BEHAVIOR ASSIGNED: BehaviorState.InstanceId changed --

            _qBehavior ??= repo.Query().With<BehaviorState>().Build();
            foreach (var entity in _qBehavior)
            {
                ref readonly var behavior = ref view.GetComponentRO<BehaviorState>(entity);
                int key = entity.Index;

                if (!_prevBehaviorInstanceId.TryGetValue(key, out uint prevId)
                    || prevId != behavior.InstanceId)
                {
                    if (behavior.ActiveBehaviorHash != 0)
                        System.Console.Out.WriteLine($"{frameTag} BEHAVIOR ASSIGNED: entity {key}");
                    _prevBehaviorInstanceId[key] = behavior.InstanceId;
                }
            }

            // -- CAPABILITY LOST: CanMove cleared --

            _qCaps ??= repo.Query().With<ActorCapabilityState>().Build();
            foreach (var entity in _qCaps)
            {
                ref readonly var caps = ref view.GetComponentRO<ActorCapabilityState>(entity);
                int key = entity.Index;

                if (_prevCapabilities.TryGetValue(key, out var prev))
                {
                    bool wasAbleToMove = (prev & ActorCapabilities.CanMove) != 0;
                    bool canMoveNow    = (caps.Capabilities & ActorCapabilities.CanMove) != 0;

                    if (wasAbleToMove && !canMoveNow)
                        System.Console.Out.WriteLine($"{frameTag} CAPABILITY LOST: entity {key} CanMove");
                }

                _prevCapabilities[key] = caps.Capabilities;
            }

            // -- HSM TRANSITION: the root machine's region-0 active leaf changed --
            //
            // ⭐⭐ O7c-④d (2026-09-23): the instance is an OCCURRENCE SLOT, not a BrainHsm128
            //   component, so there is no brain component left to query on. The query becomes
            //   "entities with a behaviour" and the slot lookup is the filter — an entity whose
            //   brain is a BTree, or which was never assigned an HSM behaviour, simply has no
            //   instance and is skipped. ⛔ Same set of reported entities, reached differently.
            //   📄 DESIGN_Occurrence_Scoped_Storage.md §31.19.

            _qHsm ??= repo.Query().With<BehaviorState>().Build();
            foreach (var entity in _qHsm)
            {
                if (!RootHsmAccess.TryGetInstance(repo, entity, out byte* instance, out int instanceSize))
                    continue;

                // ⭐ The region count is a function of the tier, so the KERNEL owns it (§31.16.1).
                ushort* leaves = HsmKernel.GetActiveLeafIds(instance, instanceSize, out int regionCount);
                if (leaves == null || regionCount <= 0) continue;

                int key = entity.Index;
                ushort curState = leaves[0];

                if (_prevHsmState.TryGetValue(key, out ushort prevState) && prevState != curState)
                    System.Console.Out.WriteLine($"{frameTag} HSM TRANSITION: entity {key} -> state {curState}");

                _prevHsmState[key] = curState;
            }

            // -- INTERACTION: EjectPassengers --

            _qInteract ??= repo.Query().With<InteractionChannel>().Build();
            foreach (var entity in _qInteract)
            {
                ref readonly var channel = ref view.GetComponentRO<InteractionChannel>(entity);
                if (channel.ActiveAction == Fdp.Toolkit.Behavior.BehaviorConstants.ActionIdEjectPassengers)
                    System.Console.Out.WriteLine($"{frameTag} INTERACTION: EjectPassengers on entity {entity.Index}");
            }

            // -- FLEE: LocomotionChannel.ActiveAction == ActionIdFlee --

            _qLoco ??= repo.Query().With<LocomotionChannel>().Build();
            foreach (var entity in _qLoco)
            {
                ref readonly var channel = ref view.GetComponentRO<LocomotionChannel>(entity);
                if (channel.ActiveAction == NavigationConstants.ActionIdFlee)
                    System.Console.Out.WriteLine($"{frameTag} FLEE: entity {entity.Index}");
            }
        }
    }
}