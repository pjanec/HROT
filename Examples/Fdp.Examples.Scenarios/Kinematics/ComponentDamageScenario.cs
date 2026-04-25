using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Examples.Common;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Vis2D;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Examples.Scenarios.Kinematics
{
    /// <summary>
    /// DEM1-D002 — ComponentDamage: Partial entity kill pipeline.
    ///
    /// <para>A single MilitaryAPC entity starts at full health with locomotion active.
    /// At tick 20 a <see cref="DetonationNotification"/> is injected, causing
    /// <see cref="DamageCalculationSystem"/> to emit a <see cref="DamageAssessedEvent"/>
    /// which <see cref="HealthApplicationSystem"/> applies. The scenario verifies that
    /// the mobility-kill pipeline strips <see cref="ActorCapabilities.CanMove"/>,
    /// clears the locomotion channel, and keeps the weapon channel active (firepower retained).</para>
    ///
    /// <para>Phase table:</para>
    /// <list type="table">
    ///   <item><term>Phase 1 (tick 15)</term><description>Health == max, CanMove == true</description></item>
    ///   <item><term>Phase 2 (tick 21)</term><description>Health &lt; max (hit landed)</description></item>
    ///   <item><term>Phase 3 (tick 22)</term><description>CanMove == false (mobility killed)</description></item>
    ///   <item><term>Phase 4 (tick 25)</term><description>LocomotionChannel.ActiveAction == 0</description></item>
    ///   <item><term>Phase 5 (tick 45)</term><description>WeaponChannel.ActiveAction == AimAndFire</description></item>
    /// </list>
    /// </summary>
    public sealed class ComponentDamageScenario : IScenario
    {
        // ── Observable state for test assertions ──────────────────────────────

        /// <summary>APC health captured immediately before the hit (tick 15).</summary>
        public float HealthAtBaseline { get; private set; }

        /// <summary>APC health captured after the hit (tick 21).</summary>
        public float HealthAfterHit { get; private set; }

        /// <summary>CanMove flag captured at tick 22.</summary>
        public bool CanMoveAtTick22 { get; private set; } = true;

        /// <summary>LocomotionChannel.ActiveAction captured at tick 25.</summary>
        public ushort LocoActionAtTick25 { get; private set; } = 1;

        /// <summary>WeaponChannel.ActiveAction captured at tick 45.</summary>
        public ushort WeaponActionAtTick45 { get; private set; }

        // ── Entity handle ─────────────────────────────────────────────────────

        private Entity _apc;

        // ── Scenario constants ────────────────────────────────────────────────

        private const float MaxHealth     = 100f;
        private const float HitDamage     = 30f;   // non-lethal: 100 - 30 = 70 remaining
        private const ushort LocoAction   = 1;      // any non-zero action = "moving"

        // ── Phase latches ─────────────────────────────────────────────────────

        private bool _hitInjected;
        private bool _phase1Checked;
        private bool _phase2Checked;
        private bool _phase3Checked;
        private bool _phase4Checked;
        private bool _phase5Checked;

        // ── IScenario ─────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public string ScenarioName => "componentdamage";

        /// <inheritdoc/>
        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            // ── Component registration ────────────────────────────────────────
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<Health>();
            world.RegisterComponent<ActorCapabilityState>();
            world.RegisterComponent<PreviousCapabilities>();
            world.RegisterComponent<LocomotionChannel>();
            world.RegisterComponent<WeaponChannel>();
            world.RegisterComponent<BrainHsm128>();

            // ── Event registration ────────────────────────────────────────────
            world.RegisterEvent<DetonationNotification>();
            world.RegisterEvent<DamageAssessedEvent>();

            // ── Systems (constructed and created against the live world) ──────
            // DamageCalculationSystem: DetonationNotification -> DamageAssessedEvent.
            // HealthApplicationSystem: DamageAssessedEvent -> mutates Health + ActorCapabilities.
            // MobilityKillSystem is no longer needed: HealthApplicationSystem strips CanMove.
            var damageCalc       = new DamageCalculationSystem();
            var healthApply      = new HealthApplicationSystem();
            var hsmBridge        = new HsmDamageBridgeSystem();
            var locoKillOnDamage = new LocomotionClearOnMobilityKillSystem();

            kernel.RegisterModule(new DirectSystemsModule(
                "ComponentDamageModule",
                damageCalc, healthApply, hsmBridge, locoKillOnDamage));

            // ── Entity spawning ───────────────────────────────────────────────
            _apc = SpawnApc(world);
        }

        /// <inheritdoc/>
        public bool EvaluateTick(uint tick, EntityRepository world)
        {
            // ── Phase 1 baseline (tick 15) ────────────────────────────────────
            if (tick == 15 && !_phase1Checked)
            {
                _phase1Checked = true;
                var health = world.GetComponent<Health>(_apc);
                var caps   = world.GetComponent<ActorCapabilityState>(_apc);
                HealthAtBaseline = health.Current;

                if (health.Current != MaxHealth)
                    throw new ScenarioFailureException(1,
                        $"Phase 1 FAILED: health={health.Current} expected {MaxHealth} at tick 15");
                if (!caps.Capabilities.HasFlag(ActorCapabilities.CanMove))
                    throw new ScenarioFailureException(1,
                        "Phase 1 FAILED: CanMove expected true at tick 15 (baseline)");
            }

            // ── Inject DetonationNotification at tick 20 ─────────────────────
            // Simulates HitResolutionSystem emitting the event after raycast resolution.
            // DamageCalculationSystem will consume it and emit DamageAssessedEvent;
            // HealthApplicationSystem applies the damage in the same frame.
            if (tick == 20 && !_hitInjected)
            {
                _hitInjected = true;

                world.Bus.Publish(new DetonationNotification
                {
                    Shooter = Entity.Null,
                    Target  = _apc,
                    HitX    = 0f,
                    HitY    = 0f,
                    HitZ    = 0f,
                });
            }

            // ── Phase 2 (tick 21): health reduced ─────────────────────────────
            if (tick == 21 && !_phase2Checked)
            {
                _phase2Checked = true;
                var health = world.GetComponent<Health>(_apc);
                HealthAfterHit = health.Current;

                if (HealthAfterHit >= MaxHealth)
                    throw new ScenarioFailureException(2,
                        $"Phase 2 FAILED: health={HealthAfterHit} still at max={MaxHealth} " +
                        $"after hit at tick 20");
            }

            // ── Phase 3 (tick 22): CanMove stripped ───────────────────────────
            if (tick == 22 && !_phase3Checked)
            {
                _phase3Checked = true;
                var caps = world.GetComponent<ActorCapabilityState>(_apc);
                CanMoveAtTick22 = caps.Capabilities.HasFlag(ActorCapabilities.CanMove);

                if (CanMoveAtTick22)
                    throw new ScenarioFailureException(3,
                        "Phase 3 FAILED: CanMove still true at tick 22; expected mobility kill");
            }

            // ── Phase 4 (tick 25): locomotion channel cleared by HSM ──────────
            if (tick == 25 && !_phase4Checked)
            {
                _phase4Checked = true;
                var loco = world.GetComponent<LocomotionChannel>(_apc);
                LocoActionAtTick25 = loco.ActiveAction;

                if (LocoActionAtTick25 != 0)
                    throw new ScenarioFailureException(4,
                        $"Phase 4 FAILED: LocomotionChannel.ActiveAction={LocoActionAtTick25} " +
                        $"expected 0 (cleared after mobility kill)");
            }

            // ── Phase 5 (tick 45): weapon still fires ─────────────────────────
            if (tick == 45 && !_phase5Checked)
            {
                _phase5Checked = true;
                var wpn = world.GetComponent<WeaponChannel>(_apc);
                WeaponActionAtTick45 = wpn.ActiveAction;

                if (WeaponActionAtTick45 != CombatConstants.ActionIdAimAndFire)
                    throw new ScenarioFailureException(5,
                        $"Phase 5 FAILED: WeaponChannel.ActiveAction={WeaponActionAtTick45} " +
                        $"expected {CombatConstants.ActionIdAimAndFire} (AimAndFire still active)");

                // All 5 phases passed — scenario succeeds.
                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }

        // ── Entity factory ────────────────────────────────────────────────────

        private static Entity SpawnApc(EntityRepository world)
        {
            var e = world.CreateEntity();

            world.AddComponent(e, new SimTransform
            {
                Position = new Vector3(0f, 0f, 0f),
                Rotation = Quaternion.Identity
            });

            world.AddComponent(e, new Health  { Current = MaxHealth, Max = MaxHealth });

            world.AddComponent(e, new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot
            });
            // Shadow component (HsmDamageBridgeSystem reads previous vs. current capabilities).
            world.AddComponent(e, new PreviousCapabilities
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot
            });

            // Locomotion channel: action 1 = "moving forward" (any non-zero ID serves the test).
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction     = LocoAction,
                ActionInstanceId = 1,
                Status           = NodeStatus.Running
            });

            // Weapon channel: AimAndFire already commanded (must survive mobility kill).
            world.AddComponent(e, new WeaponChannel
            {
                ActiveAction     = CombatConstants.ActionIdAimAndFire,
                ActionInstanceId = 1,
                Status           = NodeStatus.Running
            });

            // BrainHsm128: required for HsmDamageBridgeSystem to inject MobilityLost event.
            world.AddComponent(e, new BrainHsm128());

            return e;
        }

        // ── Inner module helper (shared pattern) ──────────────────────────────

        private sealed class DirectSystemsModule : IEcsModule
        {
            private readonly IEcsModuleSystem[] _systems;

            public string Name { get; }
            public ExecutionPolicy Policy              => ExecutionPolicy.Synchronous();
            public IReadOnlyList<Type>? WatchComponents => null;
            public IReadOnlyList<Type>? WatchEvents     => null;

            public DirectSystemsModule(string name, params IEcsModuleSystem[] systems)
            {
                Name     = name;
                _systems = systems;
            }

            public void RegisterSystems(ISystemRegistry registry) { }

            public void Tick(ISimulationView view, float deltaTime)
            {
                foreach (var sys in _systems)
                    sys.Execute(view, deltaTime);
            }

            public IReadOnlyList<Type>? GetRequiredComponents() => null;
        }

        // ── LocomotionClearOnMobilityKillSystem ───────────────────────────────

        /// <summary>
        /// Clears <see cref="LocomotionChannel.ActiveAction"/> when an entity no longer
        /// has <see cref="ActorCapabilities.CanMove"/>. Emulates the HSM doctrine response
        /// to a MobilityLost event without requiring a full HSM state-machine definition.
        /// Must run after <see cref="HsmDamageBridgeSystem"/>.
        /// </summary>
        private sealed class LocomotionClearOnMobilityKillSystem : IEcsModuleSystem
        {
            public void Execute(ISimulationView view, float deltaTime)
            {
                if (view is not EntityRepository repo)
                    throw new InvalidOperationException(
                        $"{nameof(LocomotionClearOnMobilityKillSystem)} requires direct EntityRepository access.");

                var q = repo.Query()
                    .With<ActorCapabilityState>()
                    .With<LocomotionChannel>()
                    .Build();

                foreach (var entity in q)
                {
                    var caps = repo.GetComponent<ActorCapabilityState>(entity);
                    if ((caps.Capabilities & ActorCapabilities.CanMove) != 0) continue;

                    ref var loco = ref repo.GetComponentRW<LocomotionChannel>(entity);
                    loco.ActiveAction = 0;
                    loco.Status       = NodeStatus.Failure;
                }
            }
        }
    }
}
