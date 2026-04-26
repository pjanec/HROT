using System;
using Fdp.Examples.UrbanCombat;
using Fdp.Examples.UrbanCombat.Brains;
using Fdp.Examples.UrbanCombat.Setup;
using Fdp.Examples.UrbanCombat.Systems;
using Fdp.Interfaces;
using Fdp.Core;
using Fbt;
using Fbt.Runtime;
using Fbt.Serialization;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Tkb;
using Xunit;

namespace Fdp.Examples.UrbanCombat.Tests
{
    /// <summary>
    /// BATCH-15 blueprint and brain tests.
    ///
    /// <list type="table">
    ///   <listheader><term>Task</term><description>Tests</description></listheader>
    ///   <item><term>T0 (4)</term><description>TKB template registration via DemoTkbSetup.</description></item>
    ///   <item><term>T4 (3)</term><description>TrafficBrainSystem channel writes for Tier-1 civilians.</description></item>
    ///   <item><term>T5 (2)</term><description>InsurgentNodes BTree execution via Ambush.json.</description></item>
    ///   <item><term>T6 (3)</term><description>APC ConvoyEscort_HSM build, initial state, and transition.</description></item>
    /// </list>
    /// </summary>
    [Collection("SerialTests")]
    public unsafe class BlueprintTests : IDisposable
    {
        // ── Shared fixture ────────────────────────────────────────────────────────
        // xUnit creates a fresh BlueprintTests instance per test method, so _app is
        // never shared between tests.

        private readonly HeadlessDemoApp _app;

        public BlueprintTests()
        {
            _app = new HeadlessDemoApp();
            _app.Initialize();
        }

        public void Dispose() => _app.Dispose();

        // ── Ambush JSON (inline — avoids path-resolution issues in CI) ────────────

        private const string AmbushJson = """
            {
                "TreeName": "Ambush_BT",
                "Version": 1,
                "Root": {
                    "Type": "Selector",
                    "Children": [
                        {
                            "Type": "Sequence",
                            "Children": [
                                { "Type": "Condition", "Action": "Condition_HasTarget"  },
                                { "Type": "Action",    "Action": "Action_AimAndFire"    }
                            ]
                        },
                        { "Type": "Action", "Action": "Action_HoldPosition" }
                    ]
                }
            }
            """;

        // ── Helper: build the Ambush interpreter ──────────────────────────────────

        private static Interpreter<BrainBlackboard, BTreeContext> BuildAmbushInterpreter()
        {
            var registry = new ActionRegistry<BrainBlackboard, BTreeContext>();
            registry.Register("Condition_HasTarget",  InsurgentNodes.Condition_HasTarget);
            registry.Register("Action_AimAndFire",    InsurgentNodes.Action_AimAndFire);
            registry.Register("Action_HoldPosition",  InsurgentNodes.Action_HoldPosition);

            var blob = TreeCompiler.CompileFromJson(AmbushJson);
            return new Interpreter<BrainBlackboard, BTreeContext>(blob, registry);
        }

        // ════════════════════════════════════════════════════════════════════════════
        // T0 — TKB template registration (BATCH-15 Task 0 / BCS-P7-T2)
        // ════════════════════════════════════════════════════════════════════════════

        /// <summary>DemoTkbSetup.RegisterAll must register exactly five templates.</summary>
        [Fact]
        public void TkbSetup_RegistersAllFiveTemplates()
        {
            // HeadlessDemoApp.Initialize() already calls DemoTkbSetup.RegisterAll(_tkb).
            ITkbDatabase tkb = _app.Tkb;

            Assert.NotNull(tkb.GetByType(1001)); // CivilianPedestrian
            Assert.NotNull(tkb.GetByType(1002)); // CivilianCar
            Assert.NotNull(tkb.GetByType(2001)); // MilitaryAPC
            Assert.NotNull(tkb.GetByType(2002)); // InfantrySoldier
            Assert.NotNull(tkb.GetByType(2003)); // Insurgent
        }

        /// <summary>The MilitaryAPC template must stamp PassengerBuffer and FactionBlue.</summary>
        [Fact]
        public void APC_Template_HasPassengerBuffer()
        {
            var template = _app.Tkb.GetByType(2001)!;
            var e = _app.World.CreateEntity();
            template.ApplyTo(_app.World, e);

            Assert.True(_app.World.HasComponent<PassengerBuffer>(e));
            Assert.True(_app.World.HasComponent<EntityInfo>(e));
            var info = _app.World.GetComponent<EntityInfo>(e);
            Assert.Equal(ForceId.Friend, info.ForceId);   // FactionBlue = Friend
        }

        /// <summary>The MilitaryAPC template must stamp DoctrineState with BrainTierHsm (=1), not BrainTierBTree (=2).</summary>
        [Fact]
        public void APC_Template_HasHsmBrainTier()
        {
            var template = _app.Tkb.GetByType(2001)!;
            var e = _app.World.CreateEntity();
            template.ApplyTo(_app.World, e);
            var ds = _app.World.GetComponent<DoctrineState>(e);
            Assert.Equal(BehaviorConstants.BrainTierHsm, ds.BrainTier);  // must be 1, not 2
        }

        /// <summary>The InfantrySoldier template must stamp WeaponState with 30 rounds.</summary>
        [Fact]
        public void Soldier_Template_HasWeaponState()
        {
            var template = _app.Tkb.GetByType(2002)!;
            var e = _app.World.CreateEntity();
            template.ApplyTo(_app.World, e);

            Assert.True(_app.World.HasComponent<WeaponState>(e));
            var ws = _app.World.GetComponent<WeaponState>(e);
            Assert.Equal(UrbanCombatConstants.RifleAmmo, ws.Ammo);   // Rifle: 30 rounds
        }

        /// <summary>The Insurgent template must stamp WeaponState with 1 round (RPG) and FactionRed.</summary>
        [Fact]
        public void Insurgent_Template_HasWeaponState_WithExpectedAmmo()
        {
            var template = _app.Tkb.GetByType(2003)!;
            var e = _app.World.CreateEntity();
            template.ApplyTo(_app.World, e);

            Assert.True(_app.World.HasComponent<WeaponState>(e));
            var ws = _app.World.GetComponent<WeaponState>(e);
            Assert.Equal(UrbanCombatConstants.RpgAmmo, ws.Ammo);   // RPG: single rocket

            Assert.True(_app.World.HasComponent<EntityInfo>(e));
            var info = _app.World.GetComponent<EntityInfo>(e);
            Assert.Equal(ForceId.Hostile, info.ForceId);   // FactionRed = Hostile
        }

        // ════════════════════════════════════════════════════════════════════════════
        // T4 — TrafficBrainSystem (BCS-P7-T4)
        // ════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Tier-1 entity with TargetMemory.Count &gt; 0 → ActionIdFlee (2) written to channel.
        /// </summary>
        [Fact]
        public void TrafficBrain_SetsFlee_WhenThreatDetected()
        {
            var e = _app.World.CreateEntity();
            _app.World.AddComponent(e, new SimTier              { Value = 1 });
            _app.World.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });
            _app.World.AddComponent(e, new LocomotionChannel());
            _app.World.AddComponent(e, new TargetMemory         { Count = 1 }); // one threat

            var sys = new TrafficBrainSystem();
            sys.Execute(_app.World, 0f);

            var channel = _app.World.GetComponent<LocomotionChannel>(e);
            Assert.Equal(NavigationConstants.ActionIdFlee, channel.ActiveAction);   // 2
        }

        /// <summary>
        /// Tier-1 entity with TargetMemory.Count == 0 → ActionIdMoveTo (1) written to channel.
        /// </summary>
        [Fact]
        public void TrafficBrain_SetsMoveTo_WhenIdle()
        {
            var e = _app.World.CreateEntity();
            _app.World.AddComponent(e, new SimTier              { Value = 1 });
            _app.World.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });
            _app.World.AddComponent(e, new LocomotionChannel());
            _app.World.AddComponent(e, new TargetMemory         { Count = 0 }); // no threats

            var sys = new TrafficBrainSystem();
            sys.Execute(_app.World, 0f);

            var channel = _app.World.GetComponent<LocomotionChannel>(e);
            Assert.Equal(NavigationConstants.ActionIdMoveTo, channel.ActiveAction);  // 1
        }

        /// <summary>
        /// Tier-2 entity is skipped — LocomotionChannel.ActiveAction remains 0 (default).
        /// </summary>
        [Fact]
        public void TrafficBrain_IgnoresTier2Entities()
        {
            var e = _app.World.CreateEntity();
            _app.World.AddComponent(e, new SimTier              { Value = 2 }); // tactical tier
            _app.World.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });
            _app.World.AddComponent(e, new LocomotionChannel());                // ActiveAction starts at 0

            var sys = new TrafficBrainSystem();
            sys.Execute(_app.World, 0f);

            var channel = _app.World.GetComponent<LocomotionChannel>(e);
            Assert.Equal((ushort)0, channel.ActiveAction);   // untouched
        }

        // ════════════════════════════════════════════════════════════════════════════
        // T5 — InsurgentNodes / Ambush_BT (BCS-P7-T5)
        // ════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// No target → Selector falls through to Action_HoldPosition → WeaponChannel untouched.
        /// </summary>
        [Fact]
        public void Ambush_BT_HoldPosition_WhenNoTarget()
        {
            var interpreter = BuildAmbushInterpreter();

            var e = _app.World.CreateEntity();
            _app.World.AddComponent(e, new WeaponChannel());
            _app.World.AddComponent(e, new TargetMemory { Count = 0 }); // no target

            var blackboard = new BrainBlackboard();
            var state      = new BehaviorTreeState();
            var ctx        = new BTreeContext { Self = e, World = _app.World };

            var result = interpreter.Tick(ref blackboard, ref state, ref ctx);

            // HoldPosition returns Running and writes nothing to WeaponChannel.
            Assert.Equal(NodeStatus.Running, result);
            var channel = _app.World.GetComponent<WeaponChannel>(e);
            Assert.Equal((ushort)0, channel.ActiveAction);
        }

        /// <summary>
        /// Target present → Sequence succeeds → Action_AimAndFire writes ActionIdAimAndFire (1).
        /// </summary>
        [Fact]
        public void Ambush_BT_AimsAtTarget_WhenTargetPresent()
        {
            var interpreter = BuildAmbushInterpreter();

            var e = _app.World.CreateEntity();
            _app.World.AddComponent(e, new WeaponChannel());
            _app.World.AddComponent(e, new TargetMemory { Count = 1 }); // target acquired

            var blackboard = new BrainBlackboard();
            var state      = new BehaviorTreeState();
            var ctx        = new BTreeContext { Self = e, World = _app.World };

            interpreter.Tick(ref blackboard, ref state, ref ctx);

            var channel = _app.World.GetComponent<WeaponChannel>(e);
            Assert.Equal(CombatConstants.ActionIdAimAndFire, channel.ActiveAction);  // 1
        }

        // ════════════════════════════════════════════════════════════════════════════
        // T6 — APC ConvoyEscort_HSM (BCS-P7-T6)
        // ════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ApcHsmSetup.Build() must complete without exception and return a 3-state blob
        /// (synthetic root + Cruising + Disabled).
        /// </summary>
        [Fact]
        public void ApcHsm_Builds_WithoutException()
        {
            var blob = ApcHsmSetup.Build();

            // 3 states: root(0) + Cruising(1) + Disabled(2)
            Assert.Equal(3, blob.Header.StateCount);
        }

        /// <summary>
        /// An APC entity initialised at CruisingStateIndex (1) with no event injected
        /// must remain in Cruising after one HsmTickSystem pass.
        /// </summary>
        [Fact]
        public void ApcHsm_InitialState_IsCruising()
        {
            using var world = BuildHsmWorld();
            var blob = ApcHsmSetup.Build();

            const int docId = 9901;
            var registry = BuildHsmRegistry(blob, docId);

            var sys = new HsmTickSystem<BrainHsm128>(registry);

            var e = CreateApcEntity(world, docId);

            var brain = new BrainHsm128();
            brain.State.Header.MachineId = blob.Header.StructureHash;
            brain.State.Header.Phase     = InstancePhase.RTC;
            brain.State.ActiveLeafIds[0] = ApcHsmSetup.CruisingStateIndex;
            // No event — Reserved1 stays 0 (default)
            world.AddComponent(e, brain);

            sys.Execute(world, 0.016f);

            var result = world.GetComponent<BrainHsm128>(e);
            Assert.Equal(ApcHsmSetup.CruisingStateIndex, result.State.ActiveLeafIds[0]);
        }

        /// <summary>
        /// Injecting EventId_MobilityLost (1) via the Reserved1 scratch field while in
        /// Cruising must cause a transition to DisabledStateIndex (2).
        /// </summary>
        [Fact]
        public void ApcHsm_TransitionsToDisabled_OnMobilityLostEvent()
        {
            using var world = BuildHsmWorld();
            var blob = ApcHsmSetup.Build();

            const int docId = 9902;
            var registry = BuildHsmRegistry(blob, docId);

            var sys = new HsmTickSystem<BrainHsm128>(registry);

            var e = CreateApcEntity(world, docId);

            var brain = new BrainHsm128();
            brain.State.Header.MachineId = blob.Header.StructureHash;
            brain.State.Header.Phase     = InstancePhase.RTC;
            brain.State.ActiveLeafIds[0] = ApcHsmSetup.CruisingStateIndex;
            brain.State.Reserved1        = BehaviorConstants.EventId_MobilityLost; // inject
            world.AddComponent(e, brain);

            sys.Execute(world, 0.016f);

            var result = world.GetComponent<BrainHsm128>(e);
            Assert.Equal(ApcHsmSetup.DisabledStateIndex, result.State.ActiveLeafIds[0]);
        }

        // ── T6 helpers ────────────────────────────────────────────────────────────

        /// <summary>Minimal ECS world for HSM tests (only the three components needed).</summary>
        private static EntityRepository BuildHsmWorld()
        {
            var world = new EntityRepository();
            world.RegisterComponent<DoctrineState>();
            world.RegisterComponent<BrainHsm128>();
            world.RegisterComponent<BrainBlackboard>();
            return world;
        }

        private static DoctrineRegistry BuildHsmRegistry(HsmDefinitionBlob blob, int docId)
        {
            var registry = new DoctrineRegistry();
            registry.Register(docId, "ConvoyEscort_HSM", new DoctrineDefinition
            {
                Name          = "ConvoyEscort_HSM",
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = blob,
            });
            return registry;
        }

        private static Entity CreateApcEntity(EntityRepository world, int docId)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState
            {
                ActiveDoctrineHash = docId,
                BrainTier          = BehaviorConstants.BrainTierHsm,
            });
            world.AddComponent(e, new BrainBlackboard());
            return e;
        }
    }
}
