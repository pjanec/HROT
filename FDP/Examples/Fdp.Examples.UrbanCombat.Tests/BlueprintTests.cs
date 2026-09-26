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
using Fhsm.Kernel;
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

        private static Interpreter<byte, BTreeContext> BuildAmbushInterpreter()
        {
            var registry = new ActionRegistry<byte, BTreeContext>();
            registry.Register("Condition_HasTarget",  InsurgentNodes.Condition_HasTarget);
            registry.Register("Action_AimAndFire",    InsurgentNodes.Action_AimAndFire);
            registry.Register("Action_HoldPosition",  InsurgentNodes.Action_HoldPosition);

            var blob = TreeCompiler.CompileFromJson(AmbushJson);
            return new Interpreter<byte, BTreeContext>(blob, registry);
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

            byte blackboard = 0;   // P4: the node takes `ref byte` — the root params slot base
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

            byte blackboard = 0;   // P4: the node takes `ref byte` — the root params slot base
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
        /// must remain in Cruising after one BrainTickSystem pass.
        /// </summary>
        [Fact]
        public void ApcHsm_InitialState_IsCruising()
        {
            using var world = BuildHsmWorld();
            var blob = ApcHsmSetup.Build();

            const int docId = 9901;
            var registry = BuildHsmRegistry(blob, docId);

            var sys = new BrainTickSystem(registry);

            var e = CreateApcEntity(world, docId);

            // ⭐ O7c-④b: seed the machine IN CRUISING through the slot. Phase Idle with an empty
            //   queue is a no-op tick, which is exactly the claim.
            SeedApcAt(world, e, docId, blob, ApcHsmSetup.CruisingStateIndex);

            sys.Execute(world, 0.016f);

            Assert.Equal(ApcHsmSetup.CruisingStateIndex, ActiveLeaf0(world, e));
        }

        /// <summary>
        /// Injecting EventId_MobilityLost (1) while in Cruising must cause a transition to
        /// DisabledStateIndex (2).
        ///
        /// <para>⭐⭐ <c>O7c</c>-④b: the event now goes through <c>HsmEventQueue.TryEnqueue</c> — the
        /// PUBLIC size-driven API — instead of poking <c>HsmInstance128.Reserved1</c>, the kernel's
        /// per-tier <c>CurrentEventId</c> scratch. ⛔ That offset was only ever right for a 128-byte
        /// instance, and the width now follows the MACHINE. ⚠ It is also the path production uses, so
        /// the rail got closer to the real thing rather than further from it — at the cost of needing
        /// the full Idle→Entry→RTC phase cycle rather than one pass.</para>
        /// </summary>
        [Fact]
        public void ApcHsm_TransitionsToDisabled_OnMobilityLostEvent()
        {
            using var world = BuildHsmWorld();
            var blob = ApcHsmSetup.Build();

            const int docId = 9902;
            var registry = BuildHsmRegistry(blob, docId);

            var sys = new BrainTickSystem(registry);

            var e = CreateApcEntity(world, docId);

            SeedApcAt(world, e, docId, blob, ApcHsmSetup.CruisingStateIndex);

            Assert.True(RootHsmAccess.TryGetInstance(world, e, out byte* inst, out int size));
            Assert.True(HsmEventQueue.TryEnqueue(
                inst, size, new HsmEvent { EventId = BehaviorConstants.EventId_MobilityLost }));

            for (int t = 0; t < 10; t++)
                sys.Execute(world, 0.016f);

            Assert.Equal(ApcHsmSetup.DisabledStateIndex, ActiveLeaf0(world, e));
        }

        // ── T6 helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ O7c-④b — provision the slot-resident instance and park it in <paramref name="leafId"/>
        /// with an empty queue, which replaces the hand-built <c>BrainHsm128</c> these rails used.
        /// </summary>
        private static unsafe void SeedApcAt(
            EntityRepository world, Entity e, int docId, HsmDefinitionBlob blob, ushort leafId)
        {
            Assert.True(RootHsmAccess.EnsureRootInstance(world, e, docId, blob));
            Assert.True(RootHsmAccess.TryGetInstance(world, e, out byte* ptr, out int size));

            ((InstanceHeader*)ptr)->Phase = InstancePhase.Idle;

            ushort* leaves = HsmKernel.GetActiveLeafIds(ptr, size, out int count);
            Assert.True(leaves != null && count > 0);
            leaves[0] = leafId;
        }

        /// <summary>⭐ The first active leaf, read size-driven through the kernel's own accessor.</summary>
        private static unsafe ushort ActiveLeaf0(EntityRepository world, Entity e)
        {
            Assert.True(RootHsmAccess.TryGetInstance(world, e, out byte* ptr, out int size));
            ushort* leaves = HsmKernel.GetActiveLeafIds(ptr, size, out int count);
            Assert.True(leaves != null && count > 0);
            return leaves[0];
        }

        /// <summary>Minimal ECS world for HSM tests (only the three components needed).</summary>
        private static EntityRepository BuildHsmWorld()
        {
            var world = new EntityRepository();
            world.RegisterComponent<BehaviorState>();
            // ⭐⭐ O7c-④b: the instance lives in an OCCURRENCE SLOT; the tier ladder replaces the
            //   brain component. ⛔ Omitting it does not throw — the walk enumerates nothing and the
            //   brain silently never ticks, which is why it is registered explicitly.
            Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(world);
            // ⭐⭐⭐ CE-334 (2026-09-26): Activity_Cruise writes LocomotionChannel, and an active
            //   state's activity now runs EVERY TICK rather than once ⇒ the component the real APC
            //   always carries must be registered here too. ⛔ Registering it is not optional
            //   plumbing: AddComponent throws without it, and — trap ⑨ — a MISSING registration is
            //   the failure mode this programme has paid for repeatedly.
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.LocomotionChannel>();
            return world;
        }

        private static BehaviorRegistry BuildHsmRegistry(HsmDefinitionBlob blob, int docId)
        {
            var registry = new BehaviorRegistry();
            registry.Register(docId, "ConvoyEscort_HSM", new BehaviorDefinition
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
            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = docId,
                BrainTier          = BehaviorConstants.BrainTierHsm,
            });

            // ⭐⭐⭐ CE-334 (2026-09-26) — LocomotionChannel is REQUIRED now, and the reason is the
            //    point of the change: Activity_Cruise writes it, and an active state's activity runs
            //    EVERY TICK instead of once. 🔴 Before, `Idle` with an empty queue was a no-op, so
            //    these rails ticked an APC parked in Cruising and the activity never fired — the
            //    fixture could omit the component the real APC always has.
            // ⛔ That omission was not laziness; it was the one-shot showing through the fixture.
            //    ⚠ A production APC carries LocomotionChannel, so this makes the rail MORE like the
            //    real thing, not less. 📄 DESIGN_Occurrence_Scoped_Storage.md §32.14.
            world.AddComponent(e, new Fdp.Toolkit.Behavior.Components.LocomotionChannel());
            return e;
        }
    }
}
