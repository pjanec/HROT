using System;
using System.Numerics;
using Fdp.Examples.Common;
using Fdp.Examples.Common.Constants;
using Fdp.Core;
using CommonBehaviorIds = Fdp.Examples.Common.Constants.DemoBehaviorIds;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Vis2D;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Examples.Scenarios.Cognitive
{
    /// <summary>
    /// DEM1-D006 — MissionCommand: prove <see cref="MissionDirectorSystem"/> advances phases
    /// and <see cref="ChannelArbitrationSystem"/> preempts stale locomotion commands.
    ///
    /// <para><b>Topology:</b> <see cref="MissionControlModule"/> systems
    /// (<see cref="BehaviorIngressSystem"/>, <see cref="MissionDirectorSystem"/>)
    /// and <see cref="CognitiveRuntimeModule"/> system
    /// (<see cref="ChannelArbitrationSystem"/>). No physics or executors.</para>
    ///
    /// <para><b>Phase table:</b></para>
    /// <list type="table">
    ///   <item><term>Phase 1 (tick 5)</term><description>Script writes MoveTo command to LocomotionChannel.</description></item>
    ///   <item><term>Phase 2 (tick 10)</term><description>Enemy injected into TargetMemory; MissionDirector detects UnderAttack.</description></item>
    ///   <item><term>Phase 3 (tick 11)</term><description>MissionPlanQueue.CurrentPhase==1, BehaviorState.ActiveBehaviorHash==200 (Combat).</description></item>
    ///   <item><term>Phase 4 (tick 12)</term><description>ChannelArbitration clears stale MoveTo command; LocomotionChannel.ActiveAction==0.</description></item>
    /// </list>
    ///
    /// <para><b>Design note — manual pipeline driving:</b>
    /// The mission pipeline (BehaviorIngress → MissionDirector → BehaviorIngress → ChannelArbitration)
    /// is stepped manually in <see cref="EvaluateTick"/> rather than via a kernel module.
    /// This follows the <c>SensorGridScenario</c> pattern and allows exact-tick assertions
    /// by flushing <see cref="FdpEventBus.SwapBuffers"/> between
    /// <c>MissionDirectorSystem</c> and <c>BehaviorIngressSystem</c> so that
    /// <c>BehaviorState.ActiveBehaviorHash</c> is updated in the same tick as the phase advance.</para>
    /// </summary>
    public sealed class MissionCommandScenario : IScenario
    {
        // ── Behavior IDs (from DemoBehaviorIds.Patrol/Combat) ─────────────────
        private const int PatrolBehaviorId = (int)CommonBehaviorIds.Patrol; // 100
        private const int CombatBehaviorId = (int)CommonBehaviorIds.Combat; // 200

        private const uint InitialBehaviorInstanceId = 1;
        private const long DummyEnemyId = 999L;

        // ── Observable state for test assertions ──────────────────────────────

        /// <summary>MissionPlanQueue.CurrentPhase captured at tick 11 (Phase 3).</summary>
        public int Phase3CurrentPhase { get; private set; }

        /// <summary>BehaviorState.ActiveBehaviorHash captured at tick 11 (Phase 3).</summary>
        public int Phase3BehaviorHash { get; private set; }

        /// <summary>LocomotionChannel.ActiveAction captured at tick 12 (Phase 4).</summary>
        public ushort Phase4LocoAction { get; private set; }

        // ── Phase latch flags ─────────────────────────────────────────────────

        private bool _passedPhase1;
        private bool _passedPhase3;
        private bool _passedPhase4;

        // ── Entity handle ─────────────────────────────────────────────────────

        private Entity _commander;

        // ── Mission pipeline systems (manually driven) ────────────────────────

        private BehaviorIngressSystem? _behaviorIngress;
        private MissionDirectorSystem? _missionDirector;
        private ChannelArbitrationSystem? _channelArbitration;

        // ── IScenario ─────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public string ScenarioName => ScenarioNames.MissionCommand;

        /// <inheritdoc/>
        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            // ── Component registration ─────────────────────────────────────────
            world.RegisterComponent<BehaviorState>();
            world.RegisterComponent<MissionPlanQueue>();
            world.RegisterComponent<LocomotionChannel>();
            world.RegisterComponent<WeaponChannel>();
            world.RegisterComponent<TargetMemory>();

            // ── Event registration ─────────────────────────────────────────────
            world.RegisterEvent<AssignBehaviorHashEvent>();
            world.RegisterEvent<ClearBehaviorEvent>();
            world.RegisterEvent<BehaviorFinishedEvent>();

            // ── Behavior registry (dummy definitions — no BTree/HSM needed) ──
            var registry = new BehaviorRegistry();
            registry.Register(PatrolBehaviorId, "Patrol", new BehaviorDefinition
            {
                Name      = "Patrol",
                BrainTier = 0,
            });
            registry.Register(CombatBehaviorId, "Combat", new BehaviorDefinition
            {
                Name      = "Combat",
                BrainTier = 0,
            });

            // ── Mission pipeline systems ───────────────────────────────────────
            _behaviorIngress   = new BehaviorIngressSystem(registry);
            _missionDirector   = new MissionDirectorSystem();
            _channelArbitration = new ChannelArbitrationSystem();

            // ── Entity spawning ────────────────────────────────────────────────
            _commander = SpawnCommander(world);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The mission pipeline is driven manually each tick with a double bus-swap pattern
        /// (MissionDirector publishes → SwapBuffers → BehaviorIngress applies) so that
        /// behavior hash changes are visible in the same tick as the phase advance.
        /// </remarks>
        public bool EvaluateTick(uint tick, EntityRepository world)
        {
            // ── Tick-specific stimuli injected BEFORE pipeline ─────────────────
            if (tick == 5)
            {
                // Phase 1: script writes MoveTo to LocomotionChannel.
                ref var loco = ref world.GetComponentRW<LocomotionChannel>(_commander);
                loco.ActiveAction       = NavigationConstants.ActionIdMoveTo;
                loco.BehaviorInstanceId = InitialBehaviorInstanceId;
                _passedPhase1 = true;
            }

            if (tick == 10)
            {
                // Phase 2: inject enemy into TargetMemory so MissionDirector fires UnderAttack.
                ref var mem = ref world.GetComponentRW<TargetMemory>(_commander);
                TargetMemory.AddOrUpdateTarget(
                    ref mem,
                    entityId:   DummyEnemyId,
                    posX:       50f,
                    posY:       50f,
                    scoreBoost: 50f,
                    tick:       tick);
            }

            // ── Manual mission pipeline ────────────────────────────────────────
            // 1. Swap so any events published in the previous kernel cycle are readable.
            world.Bus.SwapBuffers();
            // 2. BehaviorIngress: apply any pending AssignBehaviorHashEvent from prev tick.
            _behaviorIngress!.Execute(world, 0.016f);
            // 3. MissionDirector: evaluate phase triggers; publishes AssignBehaviorHashEvent.
            _missionDirector!.Execute(world, 0.016f);
            // 4. Swap so MissionDirector's events are now in the read buffer.
            world.Bus.SwapBuffers();
            // 5. BehaviorIngress again: apply behavior changes from this tick's MissionDirector.
            _behaviorIngress!.Execute(world, 0.016f);
            // 6. ChannelArbitration: clear channels whose BehaviorInstanceId lags behind InstanceId.
            _channelArbitration!.Execute(world, 0.016f);

            // ── Phase 3 assertions (tick 11) ──────────────────────────────────
            if (tick == 11 && !_passedPhase3)
            {
                ref var queue   = ref world.GetComponentRW<MissionPlanQueue>(_commander);
                var     behavior = world.GetComponent<BehaviorState>(_commander);

                Phase3CurrentPhase = queue.CurrentPhase;
                Phase3BehaviorHash = behavior.ActiveBehaviorHash;

                if (queue.CurrentPhase != 1)
                    throw new ScenarioFailureException(3,
                        $"Phase 3 FAILED at tick {tick}: CurrentPhase={queue.CurrentPhase} expected 1");

                if (behavior.ActiveBehaviorHash != CombatBehaviorId)
                    throw new ScenarioFailureException(3,
                        $"Phase 3 FAILED at tick {tick}: ActiveBehaviorHash={behavior.ActiveBehaviorHash} expected {CombatBehaviorId}");

                _passedPhase3 = true;
            }

            // ── Phase 4 assertions (tick 12) ──────────────────────────────────
            if (tick == 12 && !_passedPhase4)
            {
                var loco = world.GetComponent<LocomotionChannel>(_commander);

                Phase4LocoAction = loco.ActiveAction;

                if (loco.ActiveAction != 0)
                    throw new ScenarioFailureException(4,
                        $"Phase 4 FAILED at tick {tick}: LocomotionChannel.ActiveAction={loco.ActiveAction} expected 0 (preempted)");

                if (!_passedPhase1 || !_passedPhase3)
                    throw new ScenarioFailureException(4,
                        $"Phase 4: preconditions not met (phase1={_passedPhase1}, phase3={_passedPhase3})");

                _passedPhase4 = true;
                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }

        // ── Entity factory ────────────────────────────────────────────────────

        private static Entity SpawnCommander(EntityRepository world)
        {
            var e = world.CreateEntity();

            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = PatrolBehaviorId,
                InstanceId         = InitialBehaviorInstanceId,
                BrainTier          = 0,
            });

            // Build the 2-phase mission plan using the Span<MissionPhase> pattern to avoid
            // the C# [InlineArray] defensive-copy mutation trap.
            var queue = new MissionPlanQueue { PhaseCount = 2 };
            Span<MissionPhase> phases = queue.Phases;
            phases[0] = new MissionPhase
            {
                BehaviorId   = PatrolBehaviorId,
                Trigger      = MissionTrigger.UnderAttack,
                TriggerParam = 0f,
            };
            phases[1] = new MissionPhase
            {
                BehaviorId   = CombatBehaviorId,
                Trigger      = MissionTrigger.TimerElapsed,
                TriggerParam = 5.0f,
            };
            world.AddComponent(e, queue);

            world.AddComponent(e, new LocomotionChannel());
            world.AddComponent(e, new WeaponChannel());
            world.AddComponent(e, new TargetMemory());

            return e;
        }
    }
}
