using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Examples.Common;
using Fdp.Core;
using Fbt;
using Fbt.Runtime;
using Fbt.Serialization;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Vis2D;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Examples.Scenarios.Cognitive
{
    /// <summary>
    /// DEM1-D004 — BehaviorValidation: prove the BTree executor shifts decision nodes
    /// strictly through <c>BrainBlackboard</c> state writes, without any physics.
    ///
    /// <para>A single Commander agent runs a synthetic <em>MockCombat_BT</em> behavior.
    /// The scenario script acts as the perception layer, directly writing
    /// <c>ThreatVisible</c> and <c>AmmoCount</c> into the agent's inline blackboard memory.
    /// Only <see cref="CognitiveRuntimeModule"/> systems are active — no physics, no kinematics,
    /// no combat executors.</para>
    ///
    /// <para><b>BTree structure:</b></para>
    /// <code>
    /// Selector
    ///   └─ Sequence
    ///        ├─ Condition_ThreatVisible   ← Success when Memory[0] != 0
    ///        ├─ Condition_HasAmmo         ← Success when Memory[4..7] as int &gt; 0
    ///        └─ Action_AimAndFire         ← writes WeaponChannel=AimAndFire, Loco=0; returns Running
    ///   └─ Action_Flee                    ← writes LocoChannel=ActionIdFlee, Weapon=0; returns Running
    /// </code>
    ///
    /// <para><b>Phase table:</b></para>
    /// <list type="table">
    ///   <item><term>Phase 1 (tick 10)</term><description>No threat → agent flees. Then ThreatVisible set to true.</description></item>
    ///   <item><term>Phase 2 (tick 20)</term><description>Threat visible, ammo available → agent engages. Then AmmoCount set to 0.</description></item>
    ///   <item><term>Phase 3 (tick 30)</term><description>Ammo depleted → agent flees again → scenario succeeds.</description></item>
    /// </list>
    ///
    /// <para><b>Design note — reactive BTree:</b> Because the FastBTree Selector's resume
    /// optimisation skips previously-failed subtrees, the BTree state is reset to
    /// <c>default</c> each tick in <see cref="EvaluateTick"/> so conditions are re-evaluated
    /// fresh from the root. This gives the reactive, stateless behaviour required here.
    /// See BATCH-04 report §Q3.</para>
    /// </summary>
    public sealed class BehaviorValidationScenario : IScenario
    {
        // ── Blackboard memory layout ──────────────────────────────────────────
        // BrainBlackboard.BehaviorParameters is fixed byte[100]. We reserve:
        //   [0]    ThreatVisible: bool (byte) — 0=false, 1=true
        //   [4..7] AmmoCount: int (little-endian)

        private const int MemThreatVisible = 0;
        private const int MemAmmoCount     = 4;

        private const int InitialAmmo = 10;

        // ── Inline BTree JSON (MockCombat_BT) ─────────────────────────────────

        private const string CombatBTreeJson = """
            {
                "TreeName": "MockCombat_BT",
                "Version": 1,
                "Root": {
                    "Type": "Selector",
                    "Children": [
                        {
                            "Type": "Sequence",
                            "Children": [
                                { "Type": "Condition", "Action": "Condition_ThreatVisible" },
                                { "Type": "Condition", "Action": "Condition_HasAmmo" },
                                { "Type": "Action",    "Action": "Action_AimAndFire" }
                            ]
                        },
                        { "Type": "Action", "Action": "Action_Flee" }
                    ]
                }
            }
            """;

        // ── Observable state for test assertions ──────────────────────────────

        /// <summary>LocomotionChannel.ActiveAction captured at tick 10 (Phase 1).</summary>
        public ushort LocoActionAtTick10 { get; private set; }

        /// <summary>WeaponChannel.ActiveAction captured at tick 10 (Phase 1).</summary>
        public ushort WeaponActionAtTick10 { get; private set; }

        /// <summary>WeaponChannel.ActiveAction captured at tick 20 (Phase 2).</summary>
        public ushort WeaponActionAtTick20 { get; private set; }

        /// <summary>LocomotionChannel.ActiveAction captured at tick 30 (Phase 3).</summary>
        public ushort LocoActionAtTick30 { get; private set; }

        /// <summary>WeaponChannel.ActiveAction captured at tick 30 (Phase 3).</summary>
        public ushort WeaponActionAtTick30 { get; private set; }

        // ── Phase latch flags ─────────────────────────────────────────────────

        private bool _phase1Checked;
        private bool _phase2Checked;

        // ── Entity handle ─────────────────────────────────────────────────────

        private Entity _agent;

        // ── IScenario ─────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public string ScenarioName => "behaviorvalidation";

        /// <inheritdoc/>
        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            // ── Component registration ─────────────────────────────────────────
            world.RegisterComponent<BehaviorState>();
            world.RegisterComponent<LocomotionChannel>();
            world.RegisterComponent<WeaponChannel>();
            world.RegisterComponent<ActorCapabilityState>();

            // ── Behavior registry and BTree setup ─────────────────────────────
            var registry = new BehaviorRegistry();

            var actionReg = new ActionRegistry<byte, BTreeContext>();
            actionReg.Register("Condition_ThreatVisible", Condition_ThreatVisible);
            actionReg.Register("Condition_HasAmmo",       Condition_HasAmmo);
            actionReg.Register("Action_AimAndFire",       Action_AimAndFire);
            actionReg.Register("Action_Flee",             Action_Flee);

            var blob        = TreeCompiler.CompileFromJson(CombatBTreeJson);
            var interpreter = new Interpreter<byte, BTreeContext>(blob, actionReg);

            registry.Register(BehaviorValidationBehaviorIds.Combat, "MockCombat",
                new BehaviorDefinition
                {
                    Name             = "MockCombat",
                    BrainTier        = BehaviorConstants.BrainTierBTree,
                    BTreeInterpreter = interpreter,
                });

            // ── Systems (CognitiveRuntimeModule — no physics, no combat executors) ──
            var systems = new IEcsModuleSystem[]
            {
                new ChannelArbitrationSystem(),
                new BTreeTickSystem(registry),
                new HsmTickSystem<BrainHsm128>(registry),
            };

            kernel.RegisterModule(new DirectSystemsModule("CognitiveModule", systems));

            // ── Entity spawning ────────────────────────────────────────────────
            _agent = SpawnAgent(world);
        }

        /// <inheritdoc/>
        public unsafe bool EvaluateTick(uint tick, EntityRepository world)
        {
            // Reset BTreeState to default every tick so conditions are re-evaluated
            // fresh from the root (reactive/stateless BTree semantics).
            // Without this reset, the FastBTree Selector's resume optimisation would
            // skip previously-failed subtrees even after blackboard state changes.
            // ⭐ O7c-②: the cursor lives in the entity's root state slot now.
            Fdp.Toolkit.Behavior.RootStateAccess.ResetState(world, _agent);

            // ── Phase 1 (tick 10): no threat → agent flees ────────────────────
            if (tick == 10 && !_phase1Checked)
            {
                _phase1Checked = true;

                var loco   = world.GetComponent<LocomotionChannel>(_agent);
                var weapon = world.GetComponent<WeaponChannel>(_agent);
                LocoActionAtTick10   = loco.ActiveAction;
                WeaponActionAtTick10 = weapon.ActiveAction;

                if (loco.ActiveAction != NavigationConstants.ActionIdFlee)
                    throw new ScenarioFailureException(1,
                        $"Phase 1 FAILED: LocomotionChannel.ActiveAction={loco.ActiveAction} " +
                        $"expected ActionIdFlee={NavigationConstants.ActionIdFlee} at tick {tick}");

                if (weapon.ActiveAction != 0)
                    throw new ScenarioFailureException(1,
                        $"Phase 1 FAILED: WeaponChannel.ActiveAction={weapon.ActiveAction} " +
                        $"expected 0 at tick {tick}");

                // Inject threat — BTree will pick it up from kernel.Update(tick 10) onwards.
                ref byte bb = ref global::Fdp.Toolkit.Behavior.RootParamsAccess.RootRef(world, _agent);
                global::System.Runtime.CompilerServices.Unsafe.AddByteOffset(ref bb, (nint)MemThreatVisible) = 1;
            }

            // ── Phase 2 (tick 20): threat + ammo → agent engages ──────────────
            if (tick == 20 && !_phase2Checked)
            {
                _phase2Checked = true;

                var weapon = world.GetComponent<WeaponChannel>(_agent);
                WeaponActionAtTick20 = weapon.ActiveAction;

                if (weapon.ActiveAction != CombatConstants.ActionIdAimAndFire)
                    throw new ScenarioFailureException(2,
                        $"Phase 2 FAILED: WeaponChannel.ActiveAction={weapon.ActiveAction} " +
                        $"expected ActionIdAimAndFire={CombatConstants.ActionIdAimAndFire} at tick {tick}");

                // Deplete ammo — BTree will detect Condition_HasAmmo fails next tick.
                ref byte bb = ref global::Fdp.Toolkit.Behavior.RootParamsAccess.RootRef(world, _agent);
                fixed (byte* mem = &bb)
                    *(int*)(mem + MemAmmoCount) = 0;
            }

            // ── Phase 3 (tick 30): ammo gone → agent flees again ─────────────
            if (tick == 30)
            {
                var loco   = world.GetComponent<LocomotionChannel>(_agent);
                var weapon = world.GetComponent<WeaponChannel>(_agent);
                LocoActionAtTick30   = loco.ActiveAction;
                WeaponActionAtTick30 = weapon.ActiveAction;

                if (loco.ActiveAction != NavigationConstants.ActionIdFlee)
                    throw new ScenarioFailureException(3,
                        $"Phase 3 FAILED: LocomotionChannel.ActiveAction={loco.ActiveAction} " +
                        $"expected ActionIdFlee={NavigationConstants.ActionIdFlee} at tick {tick}");

                if (weapon.ActiveAction != 0)
                    throw new ScenarioFailureException(3,
                        $"Phase 3 FAILED: WeaponChannel.ActiveAction={weapon.ActiveAction} " +
                        $"expected 0 at tick {tick}");

                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }

        // ── Entity factory ────────────────────────────────────────────────────

        private unsafe Entity SpawnAgent(EntityRepository world)
        {
            var e = world.CreateEntity();

            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = BehaviorValidationBehaviorIds.Combat,
                InstanceId         = 1,
                BrainTier          = BehaviorConstants.BrainTierBTree,
            });

            RootStateAccess.EnsureRootState(world, e);   // ⛔ O7c-②: BrainBTreeState retired — the root cursor is an occurrence slot (§31).

            // Initialise blackboard: ThreatVisible=false, AmmoCount=InitialAmmo.
            // ⭐ P4-②: a params region this example owns — the base is a `ref byte` now.
            var bbBuf = new byte[128];
            ref byte bb = ref bbBuf[0];
            // Memory[0] = 0 (ThreatVisible = false) — already zero from default struct.
            // Write InitialAmmo as a little-endian int at offset MemAmmoCount=4.
            unsafe
            {
                int val = InitialAmmo;
                global::System.Runtime.CompilerServices.Unsafe.AddByteOffset(ref bb, (nint)MemAmmoCount)     = (byte)val;
                global::System.Runtime.CompilerServices.Unsafe.AddByteOffset(ref bb, (nint)(MemAmmoCount + 1)) = (byte)(val >> 8);
                global::System.Runtime.CompilerServices.Unsafe.AddByteOffset(ref bb, (nint)(MemAmmoCount + 2)) = (byte)(val >> 16);
                global::System.Runtime.CompilerServices.Unsafe.AddByteOffset(ref bb, (nint)(MemAmmoCount + 3)) = (byte)(val >> 24);
            }
            world.AddComponent(e, bb);

            world.AddComponent(e, new LocomotionChannel());
            world.AddComponent(e, new WeaponChannel());
            world.AddComponent(e, new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot,
            });

            return e;
        }

        // ── BTree action delegates ────────────────────────────────────────────

        /// <summary>Returns Success when <c>BrainBlackboard.BehaviorParameters[0]</c> is non-zero.</summary>
        private static unsafe NodeStatus Condition_ThreatVisible(
            ref byte bb,
            ref BehaviorTreeState _,
            ref BTreeContext ctx,
            int payloadIndex)
        {
            return global::System.Runtime.CompilerServices.Unsafe.AddByteOffset(ref bb, (nint)MemThreatVisible) != 0
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }

        /// <summary>Returns Success when <c>Memory[4..7]</c> as int is greater than zero.</summary>
        private static unsafe NodeStatus Condition_HasAmmo(
            ref byte bb,
            ref BehaviorTreeState _,
            ref BTreeContext ctx,
            int payloadIndex)
        {
            fixed (byte* mem = &bb)
            {
                int ammo = *(int*)(mem + MemAmmoCount);
                return ammo > 0 ? NodeStatus.Success : NodeStatus.Failure;
            }
        }

        /// <summary>
        /// Writes <see cref="CombatConstants.ActionIdAimAndFire"/> to <see cref="WeaponChannel"/>
        /// and clears <see cref="LocomotionChannel"/>. Returns <see cref="NodeStatus.Running"/>.
        /// </summary>
        private static NodeStatus Action_AimAndFire(
            ref byte _bb,
            ref BehaviorTreeState _,
            ref BTreeContext ctx,
            int payloadIndex)
        {
            var behavior = ctx.World.GetComponent<BehaviorState>(ctx.Self);

            ref var wpn  = ref ctx.World.GetComponentRW<WeaponChannel>(ctx.Self);
            wpn.ActiveAction       = CombatConstants.ActionIdAimAndFire;
            wpn.BehaviorInstanceId = behavior.InstanceId;

            ref var loco = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);
            loco.ActiveAction       = 0;
            loco.BehaviorInstanceId = behavior.InstanceId;

            return NodeStatus.Running;
        }

        /// <summary>
        /// Writes <see cref="NavigationConstants.ActionIdFlee"/> to <see cref="LocomotionChannel"/>
        /// and clears <see cref="WeaponChannel"/>. Returns <see cref="NodeStatus.Running"/>.
        /// </summary>
        private static NodeStatus Action_Flee(
            ref byte _bb,
            ref BehaviorTreeState _,
            ref BTreeContext ctx,
            int payloadIndex)
        {
            var behavior = ctx.World.GetComponent<BehaviorState>(ctx.Self);

            ref var loco = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);
            loco.ActiveAction       = NavigationConstants.ActionIdFlee;
            loco.BehaviorInstanceId = behavior.InstanceId;

            ref var wpn = ref ctx.World.GetComponentRW<WeaponChannel>(ctx.Self);
            wpn.ActiveAction       = 0;
            wpn.BehaviorInstanceId = behavior.InstanceId;

            return NodeStatus.Running;
        }

        // ── Inner module ──────────────────────────────────────────────────────

        private sealed class DirectSystemsModule : IEcsModule
        {
            private readonly IEcsModuleSystem[] _systems;

            public string Name { get; }
            public ExecutionPolicy Policy              => ExecutionPolicy.Synchronous();
            public IReadOnlyList<Type>? WatchComponents => null;
            public IReadOnlyList<Type>? WatchEvents     => null;

            public DirectSystemsModule(string name, IEcsModuleSystem[] systems)
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
    }
}
