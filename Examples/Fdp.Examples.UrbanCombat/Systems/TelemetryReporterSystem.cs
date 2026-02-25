using System.Collections.Generic;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Contracts;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Navigation;

namespace Fdp.Examples.UrbanCombat.Systems
{
    /// <summary>
    /// Export-phase telemetry reporter for the Urban Ambush scenario.
    /// Emits structured log lines to <see cref="System.Console.Out"/> whenever
    /// significant simulation events occur, enabling integration tests to assert on
    /// milestone strings in the captured output.
    ///
    /// <para>
    /// <b>Events reported:</b>
    /// <list type="table">
    ///   <item><term>DOCTRINE ASSIGNED</term><description><see cref="DoctrineState.InstanceId"/> changes.</description></item>
    ///   <item><term>GUNFIRE</term><description><see cref="FDP.Toolkit.Combat.Events.FireRequestEvent"/> on bus.</description></item>
    ///   <item><term>HIT</term><description><see cref="FDP.Toolkit.Combat.Contracts.HitEvent"/> on bus.</description></item>
    ///   <item><term>CAPABILITY LOST</term><description><c>CanMove</c> cleared (compare vs prev frame).</description></item>
    ///   <item><term>HSM TRANSITION</term><description><see cref="BrainHsm128"/> state index changes.</description></item>
    ///   <item><term>INTERACTION: EjectPassengers</term><description><see cref="InteractionChannel.ActiveAction"/> == 3.</description></item>
    ///   <item><term>FLEE</term><description><see cref="LocomotionChannel.ActiveAction"/> == <see cref="NavigationConstants.ActionIdFlee"/>.</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Shadow tracking:</b> Per-entity dictionaries record the previous-frame value of
    /// <see cref="DoctrineState.InstanceId"/>, <see cref="BrainHsm128"/> state index,
    /// and <see cref="ActorCapabilityState.Capabilities"/>. Only this system owns these
    /// dictionaries — no ECS components are added. This is acceptable for a
    /// telemetry/debug-only system.
    /// </para>
    ///
    /// <para>
    /// <b>Console writes:</b> Uses <see cref="System.Console.Out"/><c>.WriteLine()</c>
    /// (not <c>Console.WriteLine()</c>) so that <c>StringWriter</c> redirects in integration
    /// tests capture all output.
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(ExportSystemGroup))]
    public class TelemetryReporterSystem : ComponentSystem
    {
        // ── Shadow state for change detection ────────────────────────────────────

        /// <summary>Maps entity index → last-known DoctrineState.InstanceId.</summary>
        private readonly Dictionary<int, uint> _prevDoctrineInstanceId = new Dictionary<int, uint>();

        /// <summary>Maps entity index → last-known BrainHsm128 active leaf state index.</summary>
        private readonly Dictionary<int, ushort> _prevHsmState = new Dictionary<int, ushort>();

        /// <summary>Maps entity index → last-known ActorCapabilityState.Capabilities bitmask.</summary>
        private readonly Dictionary<int, ActorCapabilities> _prevCapabilities = new Dictionary<int, ActorCapabilities>();

        /// <summary>Current frame number (incremented each OnUpdate).</summary>
        private int _frame;

        // ── Action IDs (hardcoded per EjectPassengersExecutor doc comment: kind = 3) ──

        private const ushort EjectPassengersActionId = 3;

        // ─────────────────────────────────────────────────────────────────────────

        protected override unsafe void OnUpdate()
        {
            _frame++;
            string frameTag = $"[FRAME {_frame:D4}]";

            // ── Bus events (GUNFIRE, HIT) ────────────────────────────────────────

            var fireEvents = World.Bus.Consume<FireRequestEvent>();
            for (int i = 0; i < fireEvents.Length; i++)
            {
                ref readonly var evt = ref fireEvents[i];
                System.Console.Out.WriteLine($"{frameTag} GUNFIRE: entity {evt.Shooter.Index}");
            }

            var hitEvents = World.Bus.Consume<HitEvent>();
            for (int i = 0; i < hitEvents.Length; i++)
            {
                ref readonly var evt = ref hitEvents[i];
                // Resolve damage from the bullet entity if still alive.
                float dmg = 0f;
                var bullet = World.GetEntityByIndex(evt.BulletIndex);
                if (World.IsAlive(bullet) && World.HasComponent<FDP.Toolkit.Combat.Components.BallisticProjectile>(bullet))
                    dmg = World.GetComponent<FDP.Toolkit.Combat.Components.BallisticProjectile>(bullet).Damage;
                System.Console.Out.WriteLine($"{frameTag} HIT: target {evt.HitEntity.Index}, damage {dmg}");
            }

            // ── ECS component change detection ───────────────────────────────────

            // DOCTRINE ASSIGNED — DoctrineState.InstanceId changed
            var qDoctrine = World.Query()
                .With<DoctrineState>()
                .Build();

            foreach (var entity in qDoctrine)
            {
                var doctrine = World.GetComponent<DoctrineState>(entity);
                int key = entity.Index;

                if (!_prevDoctrineInstanceId.TryGetValue(key, out uint prevId)
                    || prevId != doctrine.InstanceId)
                {
                    // Report on first encounter (new entity seen) AND on every subsequent
                    // InstanceId bump (doctrine reassignment via DoctrineIngressSystem).
                    if (doctrine.ActiveDoctrineHash != 0)
                    {
                        string name = doctrine.ActiveDoctrineHash.ToString();
                        System.Console.Out.WriteLine($"{frameTag} DOCTRINE ASSIGNED: entity {key} → {name}");
                    }
                    _prevDoctrineInstanceId[key] = doctrine.InstanceId;
                }
            }

            // CAPABILITY LOST — CanMove cleared
            var qCaps = World.Query()
                .With<ActorCapabilityState>()
                .Build();

            foreach (var entity in qCaps)
            {
                var caps = World.GetComponent<ActorCapabilityState>(entity);
                int key = entity.Index;

                if (_prevCapabilities.TryGetValue(key, out var prev))
                {
                    bool wasAbleToMove = (prev & ActorCapabilities.CanMove) != 0;
                    bool canMoveNow    = (caps.Capabilities & ActorCapabilities.CanMove) != 0;

                    if (wasAbleToMove && !canMoveNow)
                    {
                        System.Console.Out.WriteLine($"{frameTag} CAPABILITY LOST: entity {key} CanMove");
                    }
                }

                _prevCapabilities[key] = caps.Capabilities;
            }

            // HSM TRANSITION — BrainHsm128 active leaf index changed
            var qHsm = World.Query()
                .With<BrainHsm128>()
                .Build();

            foreach (var entity in qHsm)
            {
                var brain = World.GetComponent<BrainHsm128>(entity);
                int key = entity.Index;
                ushort curState = brain.State.ActiveLeafIds[0];

                if (_prevHsmState.TryGetValue(key, out ushort prevState)
                    && prevState != curState)
                {
                    System.Console.Out.WriteLine($"{frameTag} HSM TRANSITION: entity {key} → state {curState}");
                }

                _prevHsmState[key] = curState;
            }

            // INTERACTION: EjectPassengers — InteractionChannel.ActiveAction == 3
            var qInteract = World.Query()
                .With<InteractionChannel>()
                .Build();

            foreach (var entity in qInteract)
            {
                var channel = World.GetComponent<InteractionChannel>(entity);
                if (channel.ActiveAction == EjectPassengersActionId)
                {
                    System.Console.Out.WriteLine($"{frameTag} INTERACTION: EjectPassengers on entity {entity.Index}");
                }
            }

            // FLEE — LocomotionChannel.ActiveAction == ActionIdFlee
            var qLoco = World.Query()
                .With<LocomotionChannel>()
                .Build();

            foreach (var entity in qLoco)
            {
                var channel = World.GetComponent<LocomotionChannel>(entity);
                if (channel.ActiveAction == NavigationConstants.ActionIdFlee)
                {
                    System.Console.Out.WriteLine($"{frameTag} FLEE: entity {entity.Index}");
                }
            }
        }
    }
}
