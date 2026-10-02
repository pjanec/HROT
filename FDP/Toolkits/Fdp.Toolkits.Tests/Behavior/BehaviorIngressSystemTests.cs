using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    public unsafe class BehaviorIngressSystemTests
    {
        // ── Nested types used by individual tests ─────────────────────────────────

        /// <summary>
        /// Minimal blackboard used by the FleeToSafety behavior test.
        /// Must be the first field so it aligns with offset 0 of BrainBlackboard.BehaviorParameters.
        /// </summary>
        private struct FleeBlackboard { public float SafeDistance; }

        // ── Helper ───────────────────────────────────────────────────────────────

        private static (EntityRepository world, BehaviorIngressSystem sys, BehaviorRegistry registry)
            CreateFixture()
        {
            var world    = TestWorldFactory.Create();

            // 🔴 P3-C: a behaviour's params live in an OCCURRENCE SLOT now, so a world that never
            //   registered the tier components has nowhere to put them — and the ingress says so
            //   loudly rather than dropping the parse. ⭐ Production registers these Hrot-wide
            //   (HrotSharedComponentRegistry:174); a bare test world has to ask.
            Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(world);

            var registry = new BehaviorRegistry();
            var sys      = new BehaviorIngressSystem(registry);
            return (world, sys, registry);
        }

        // ── Test 1 ───────────────────────────────────────────────────────────────

        [Fact]
        public void BehaviorIngress_ParsesFleeBlackboard_FromJson()
        {
            var (world, sys, registry) = CreateFixture();

            // Register "FleeToSafety" behavior with a parse delegate that writes a float.
            const string behaviorName = "FleeToSafety";
            registry.Register(BehaviorIds.PanicFlee, behaviorName, new BehaviorDefinition
            {
                Name      = behaviorName,
                BrainTier = BehaviorConstants.BrainTierBTree,
                // ⭐ CE-328 (2026-09-23): a behaviour that declares a ParseParams MUST state its params
                //   width, and BehaviorRegistry.Register now refuses it otherwise. This rail used to
                //   declare neither a layout type nor a manifest and leaned on a 100-byte fallback.
                //   ⇒ it says FleeBlackboard, which is what it always parsed into.
                BlackboardLayoutType = typeof(FleeBlackboard),
                ParseParams = static (string json, byte* mem, int capacity, EntityRepository world, Entity self) =>
                {
                    *(float*)mem = float.Parse(json,
                        System.Globalization.CultureInfo.InvariantCulture);
                },
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());

            // Publish event then swap buffers so ConsumeManaged returns it.
            world.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity       = e,
                BehaviorName = behaviorName,
                JsonParams   = "50.0",
            });
            world.Bus.SwapBuffers();

            sys.Execute(world, 0.016f);

            // 🔴 P3-C (2026-09-21): verify the ROOT PARAMS OCCURRENCE SLOT, not BrainBlackboard.
            //   This rail is what caught the cut's one real gap — this behaviour declared a
            //   ParseParams and NEITHER a manifest NOR a BlackboardLayoutType, so RootParamsBytes
            //   returned 0 and the parse went nowhere.
            // ⭐ CE-328 (2026-09-23) closed that hole at the other end: the shape is REFUSED at
            //   Register rather than papered over with a 100-byte guess, and this rail now declares
            //   its width above. ⇒ what it proves is unchanged — the parse lands in the slot.
            Assert.True(RootParamsAccess.TryGetRoot<FleeBlackboard>(world, e, out var fb));
            Assert.Equal(50.0f, fb->SafeDistance);

            world.Dispose();
        }

        // ── Test 2 ───────────────────────────────────────────────────────────────

        [Fact]
        public void BehaviorIngress_IncrementsInstanceId_MonotonicallyAcrossMultipleAssignments()
        {
            var (world, sys, registry) = CreateFixture();

            const string behaviorName = "Patrol";
            const int PatrolId = 3001;
            registry.Register(PatrolId, behaviorName, new BehaviorDefinition
            {
                Name      = behaviorName,
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { InstanceId = 0 });

            // --- Assignment 1 ---
            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = behaviorName, JsonParams = "" });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
            uint instanceId1 = world.GetComponent<BehaviorState>(e).InstanceId;

            // --- Assignment 2 ---
            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = behaviorName, JsonParams = "" });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
            uint instanceId2 = world.GetComponent<BehaviorState>(e).InstanceId;

            Assert.True(instanceId1 > 0);             // was incremented from 0
            Assert.True(instanceId2 > instanceId1);   // strictly increasing each assignment

            world.Dispose();
        }

        // ── Test 3 ───────────────────────────────────────────────────────────────

        [Fact]
        public void BehaviorIngress_ResetsBTreeState_OnNewBehavior()
        {
            var (world, sys, registry) = CreateFixture();

            const string behaviorName = "Assault";
            const int AssaultId = 3002;
            registry.Register(AssaultId, behaviorName, new BehaviorDefinition
            {
                Name      = behaviorName,
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());
            // ⛔⛔ O7c-② — THE CLAIM IS RE-HOMED, NOT WEAKENED. It was "seed a mid-execution cursor,
            //    assign, see it reset". 📐 A cursor can no longer pre-exist the FIRST assign: its slot key
            //    is computed from ActiveBehaviorHash, which is 0 until a behaviour is assigned. ⇒ the
            //    same claim is made across the SECOND assign, which is where "a new behaviour starts at
            //    the root" actually has to hold.
            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = behaviorName, JsonParams = "" });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            // Now drive it mid-execution, and re-assign.
            RootStateAccess.SetState(world, e, new Fbt.BehaviorTreeState { RunningNodeIndex = 5 });
            Assert.Equal(5, RootStateAccess.GetStateOrDefault(world, e).RunningNodeIndex);   // guard: the seed took

            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = behaviorName, JsonParams = "" });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            var btState = RootStateAccess.GetStateOrDefault(world, e);
            Assert.Equal(0, btState.RunningNodeIndex); // reset to start

            world.Dispose();
        }

        // ── Test 4 (integration chain) ────────────────────────────────────────────

        [Fact]
        public void BehaviorIngress_StaleSetsNewInstanceId_ArbitrationClearsOldAction()
        {
            // Full preemption chain: ingress bumps InstanceId, then arbitration
            // clears the now-stale channel. Each system is called explicitly.
            var world    = TestWorldFactory.Create();
            var registry = new BehaviorRegistry();

            const string behaviorName = "Flank";
            const int FlankId = 3003;
            registry.Register(FlankId, behaviorName, new BehaviorDefinition
            {
                Name      = behaviorName,
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            var ingressSys     = new BehaviorIngressSystem(registry);
            var arbitrationSys = new ChannelArbitrationSystem();

            var e = world.CreateEntity();
            // Setup: channel and behavior both at version 1 (valid/matching).
            world.AddComponent(e, new BehaviorState { InstanceId = 1 });
            world.AddComponent(e, new LocomotionChannel
            {
                ActiveAction       = 1,
                BehaviorInstanceId = 1, // matches BehaviorState.InstanceId — not yet stale
            });

            // Step 1: assign new behavior → InstanceId becomes 2.
            world.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity       = e,
                BehaviorName = behaviorName,
                JsonParams   = "",
            });
            world.Bus.SwapBuffers();
            ingressSys.Execute(world, 0.016f);

            uint newInstanceId = world.GetComponent<BehaviorState>(e).InstanceId;
            Assert.True(newInstanceId > 1); // ingress incremented it

            // Step 2: arbitration sees BehaviorInstanceId(1) != InstanceId(2) → clears channel.
            arbitrationSys.Execute(world, 0.016f);

            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(0, channel.ActiveAction);          // preemption chain complete
            Assert.Equal(1u, channel.BehaviorInstanceId);   // selective-clear preserves BehaviorInstanceId (only ActiveAction zeroed)

            world.Dispose();
        }

        // ── Test 5 (DEBT-008 / DEBT-035) ────────────────────────────────────────
        /// <summary>
        /// DEBT-035 fix verification: when <see cref="BehaviorDefinition.ParseParams"/> throws,
        /// <see cref="BehaviorIngressSystem"/> must not propagate the exception AND must leave
        /// the entity entirely on its previous behavior (no partial transition).
        /// </summary>
        [Fact]
        public void BehaviorIngress_DoesNotThrow_WhenParseParamsFails()
        {
            var (world, sys, registry) = CreateFixture();

            const string behaviorName = "BrokenBehavior";
            const int BrokenId = 7001;
            registry.Register(BrokenId, behaviorName, new BehaviorDefinition
            {
                Name      = behaviorName,
                BrainTier = BehaviorConstants.BrainTierBTree,
                // ⭐ CE-328: a ParseParams must come with a declared width — BehaviorRegistry.Register
                //   refuses the shape otherwise. The width is incidental to what this rail proves.
                BlackboardLayoutType = typeof(int),
                // ParseParams delegate that always throws.
                ParseParams = static (string json, byte* mem, int capacity, EntityRepository world, Entity self) =>
                    throw new InvalidOperationException("Simulated parse failure"),
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState { InstanceId = 5 });

            world.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity       = e,
                BehaviorName = behaviorName,
                JsonParams   = "bad_json",
            });
            world.Bus.SwapBuffers();

            // Must not throw.
            var exception = Record.Exception(() => sys.Execute(world, 0.016f));
            Assert.Null(exception);

            // DEBT-035 fix: ParseParams now runs BEFORE BehaviorState is written.
            // A parse failure aborts the transition — InstanceId must remain at 5.
            var state = world.GetComponent<BehaviorState>(e);
            Assert.Equal(5u, state.InstanceId);

            world.Dispose();
        }

        // ── Test 6 (DEBT-035 required test) ──────────────────────────────────────
        /// <summary>
        /// Required by BATCH-14 Corrective-0: verifies that both
        /// <see cref="BehaviorState.ActiveBehaviorHash"/> AND
        /// <see cref="BehaviorState.InstanceId"/> are unchanged when
        /// <see cref="BehaviorDefinition.ParseParams"/> fails.
        /// </summary>
        [Fact]
        public void BehaviorIngress_BehaviorStateUnchanged_WhenParseParamsFails()
        {
            var (world, sys, registry) = CreateFixture();

            const int OldId = 9000;
            const int NewId = 9001;
            const string oldBehaviorName = "OldBehavior";
            const string newBehaviorName = "NewBehavior";

            registry.Register(OldId, oldBehaviorName, new BehaviorDefinition
            {
                Name      = oldBehaviorName,
                BrainTier = BehaviorConstants.BrainTierBTree,
            });
            registry.Register(NewId, newBehaviorName, new BehaviorDefinition
            {
                Name      = newBehaviorName,
                BrainTier = BehaviorConstants.BrainTierBTree,
                // ⭐ CE-328: a ParseParams must come with a declared width — BehaviorRegistry.Register
                //   refuses the shape otherwise. The width is incidental to what this rail proves.
                BlackboardLayoutType = typeof(int),
                // ParseParams delegate that always throws.
                ParseParams = static (string json, byte* mem, int capacity, EntityRepository world, Entity self) =>
                    throw new InvalidOperationException("Test-induced parse failure"),
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = OldId,
                InstanceId         = 0
            });

            // Attempt to switch to NewBehavior — ParseParams will throw.
            world.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity       = e,
                BehaviorName = newBehaviorName,
                JsonParams   = "{}",
            });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            var state = world.GetComponent<BehaviorState>(e);
            // ActiveBehaviorHash must still point to OldId — NOT switched to NewId.
            Assert.Equal(OldId, state.ActiveBehaviorHash);
            // InstanceId must NOT have been bumped.
            Assert.Equal(0u, state.InstanceId);

            world.Dispose();
        }

        // ── Task-2 Tests: ClearBehaviorEvent ─────────────────────────────────────

        [Fact]
        public void ClearBehaviorEvent_SetsBehaviorToNone()
        {
            var (world, sys, _) = CreateFixture();

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState
            {
                ActiveBehaviorHash = 2001,
                InstanceId         = 5,
                BrainTier          = BehaviorConstants.BrainTierBTree,
            });
            // ⭐ O7c-②: the cursor is an occurrence slot; this entity already carries a hash, so the
            //   slot can be provisioned and seeded directly.
            RootStateAccess.EnsureRootState(world, e);
            RootStateAccess.SetState(world, e, new Fbt.BehaviorTreeState { RunningNodeIndex = 3 });

            world.Bus.Publish(new ClearBehaviorEvent { Entity = e });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            var behavior = world.GetComponent<BehaviorState>(e);
            Assert.Equal(BehaviorIds.None, behavior.ActiveBehaviorHash);  // cleared
            Assert.Equal(6u,              behavior.InstanceId);           // incremented
            Assert.Equal(0,               behavior.BrainTier);            // reset to none

            var btState = RootStateAccess.GetStateOrDefault(world, e);
            Assert.Equal(0, btState.RunningNodeIndex);                    // execution pointer reset

            world.Dispose();
        }

        [Fact]
        public void ClearBehaviorEvent_NoBehaviorState_IsIgnored()
        {
            var (world, sys, _) = CreateFixture();

            // Entity without BehaviorState — event must be silently skipped.
            var e = world.CreateEntity();
            // Intentionally no BehaviorState component added.

            world.Bus.Publish(new ClearBehaviorEvent { Entity = e });
            world.Bus.SwapBuffers();

            var exception = Record.Exception(() => sys.Execute(world, 0.016f));
            Assert.Null(exception);

            world.Dispose();
        }

        [Fact]
        public void ClearBehaviorEvent_DoesNotAffectOtherEntities()
        {
            var (world, sys, _) = CreateFixture();

            var entityA = world.CreateEntity();
            world.AddComponent(entityA, new BehaviorState { ActiveBehaviorHash = 1001, InstanceId = 1 });

            var entityB = world.CreateEntity();
            world.AddComponent(entityB, new BehaviorState { ActiveBehaviorHash = 1001, InstanceId = 1 });

            // Only clear entity A.
            world.Bus.Publish(new ClearBehaviorEvent { Entity = entityA });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            var docA = world.GetComponent<BehaviorState>(entityA);
            var docB = world.GetComponent<BehaviorState>(entityB);

            Assert.Equal(BehaviorIds.None, docA.ActiveBehaviorHash); // cleared
            Assert.Equal(1001,            docB.ActiveBehaviorHash);  // untouched

            world.Dispose();
        }

        [Fact]
        public void ClearVsAssign_AreIndependent()
        {
            // In the same frame: AssignBehaviorEvent for A and ClearBehaviorEvent for B.
            // After one Run, A has the assigned behavior; B has BehaviorIds.None.
            var (world, sys, registry) = CreateFixture();

            const string behaviorName = "Patrol";
            const int PatrolId = 5001;
            registry.Register(PatrolId, behaviorName, new BehaviorDefinition
            {
                Name      = behaviorName,
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            var entityA = world.CreateEntity();
            world.AddComponent(entityA, new BehaviorState { ActiveBehaviorHash = 0, InstanceId = 0 });

            var entityB = world.CreateEntity();
            world.AddComponent(entityB, new BehaviorState { ActiveBehaviorHash = PatrolId, InstanceId = 1 });

            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = entityA, BehaviorName = behaviorName, JsonParams = "" });
            world.Bus.Publish(new ClearBehaviorEvent  { Entity = entityB });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            var docA = world.GetComponent<BehaviorState>(entityA);
            var docB = world.GetComponent<BehaviorState>(entityB);

            Assert.Equal(PatrolId,        docA.ActiveBehaviorHash); // assigned
            Assert.Equal(BehaviorIds.None, docB.ActiveBehaviorHash); // cleared

            world.Dispose();
        }

        // ══ CE-307 — PARAMS WIDER THAN 100 BYTES ═══════════════════════════════════════════
        //  🔴🔴 Every one of these was IMPOSSIBLE before CE-307, and each was blocked by a
        //     DIFFERENT site, which is why they are asserted separately:
        //       · BehaviorRegistry.Register  threw on a >100-byte BlackboardLayoutType;
        //       · BehaviorIngressSystem      parsed into a `stackalloc byte[100]` shadow, so a wider
        //                                    ParseParams wrote PAST THE END OF THE STACK BUFFER;
        //       · the carry-over seed        clamped to the constant 100, not to the region's width.
        //  ⭐⭐ These are POSITIVE rails — they write known values and assert they READ BACK. ⛔ The
        //     CE-312 lesson: a rail that asserts only REFUSALS is satisfied by a zero-filled region.

        /// <summary>256 bytes — comfortably over the retired 100-byte cap, under the 16096 ceiling.</summary>
        private const int WideParamsBytes = 256;

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential, Size = WideParamsBytes)]
        private struct WideParams { public byte First; }

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential, Size = 16)]
        private struct NarrowParams { public byte First; }

        /// <summary>The pattern a wide parser writes — distinct per offset, so a truncation SHOWS.</summary>
        private static byte Pattern(int i) => unchecked((byte)(i ^ 0x5A));

        private static void RegisterWide(BehaviorRegistry registry, int id, string name) =>
            registry.Register(id, name, new BehaviorDefinition
            {
                Name                 = name,
                BrainTier            = BehaviorConstants.BrainTierBTree,
                BlackboardLayoutType = typeof(WideParams),
                // ⚠ Writes the FULL width. Before CE-307 this ran against a 100-byte stackalloc.
                ParseParams = static (string json, byte* mem, int capacity, EntityRepository world, Entity self) =>
                {
                    for (int i = 0; i < WideParamsBytes; i++) mem[i] = unchecked((byte)(i ^ 0x5A));
                },
            });

        /// <summary>
        /// ⭐⭐⭐ <b>A behaviour with 256 bytes of params parses IN FULL and lands in its slot.</b>
        /// ⛔ The assertion that matters is the tail: bytes at offsets ≥ 100 are exactly the region the
        /// retired cap made unreachable, and a partial fix would leave them zero.
        /// </summary>
        [Fact]
        public void BehaviorIngress_ParamsWiderThanTheRetiredCap_RoundTripInFull()
        {
            var (world, sys, registry) = CreateFixture();

            const string behaviorName = "WideParamsBehavior";
            RegisterWide(registry, 0x0C_E3_07_01, behaviorName);

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());

            world.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity = e, BehaviorName = behaviorName, JsonParams = "",
            });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            Assert.True(RootParamsAccess.TryGetRootBytes(world, e, out byte* root, out int len),
                "the wide behaviour got no root params slot");

            // ⭐ The slot is sized to the behaviour — promoted off the 256 tier (176-byte payload)
            //   onto the 1024 tier automatically, which is the whole point of the ladder.
            Assert.Equal(WideParamsBytes, len);

            for (int i = 0; i < WideParamsBytes; i++)
                Assert.Equal(Pattern(i), root[i]);

            world.Dispose();
        }

        /// <summary>
        /// ⭐⭐ <b>Registration no longer refuses a wide layout type.</b> 🔴 <c>BehaviorRegistry.Register</c>
        /// threw <i>"exceeds the maximum allowed parameter size of 100 bytes … would corrupt the
        /// SoftAdvice and Interrupt registers in BrainBlackboard"</i> — a rationale that was already
        /// false when <c>O2</c> moved those registers out.
        /// </summary>
        [Fact]
        public void BehaviorRegistry_AcceptsALayoutTypeWiderThanTheRetiredCap()
        {
            var registry = new BehaviorRegistry();

            Assert.Null(Record.Exception(
                () => RegisterWide(registry, 0x0C_E3_07_02, "WideRegistrationBehavior")));
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Switching WIDE → NARROW carries NOTHING over</b> — <c>CE-421</c>'s ruling, built by
        /// <c>CE-426</c>. ⛔⛔ <b>HISTORY:</b> this rail pinned the carry-over it now forbids — it was
        /// <c>…ClampsTheCarryOverToTheNewWidth</c>, and the paragraphs below describe that old contract.
        /// ⚠ The width half still matters: the new region is exactly 16 bytes.
        ///
        /// <para>⛔ <b>SUPERSEDED — the original rationale:</b></para>
        ///
        /// <para>⛔⛔ This is the line the cap was hiding. The seed copies the PREVIOUS behaviour's slot
        /// into the parse shadow so a partial parse behaves as it always has; the clamp used to be the
        /// constant <c>100</c>. ⇒ with a 256-byte previous region and a 16-byte new one, clamping to
        /// 100 would have written <b>84 bytes past the end of the shadow</b>. ⭐ The clamp is now the
        /// shadow's own width, which IS the new region's width.</para>
        ///
        /// <para>⚠ Asserts the NEW region is exactly 16 bytes and holds the narrow parser's marker —
        /// ⛔ not that it is zero, which a broken implementation could also produce.</para>
        /// </summary>
        [Fact]
        public void BehaviorIngress_SwitchingFromWideToNarrow_CarriesNothingOver()
        {
            var (world, sys, registry) = CreateFixture();

            const string wideName   = "WideThenNarrow_Wide";
            const string narrowName = "WideThenNarrow_Narrow";
            RegisterWide(registry, 0x0C_E3_07_03, wideName);
            registry.Register(0x0C_E3_07_04, narrowName, new BehaviorDefinition
            {
                Name                 = narrowName,
                BrainTier            = BehaviorConstants.BrainTierBTree,
                BlackboardLayoutType = typeof(NarrowParams),
                // ⚠ Writes ONLY its first byte — a deliberately PARTIAL parse, which is exactly the
                //   case the carry-over seed exists for.
                ParseParams = static (string json, byte* mem, int capacity, EntityRepository world, Entity self) =>
                {
                    mem[0] = 0xC7;
                },
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());

            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = wideName, JsonParams = "" });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = narrowName, JsonParams = "" });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            Assert.True(RootParamsAccess.TryGetRootBytes(world, e, out byte* root, out int len),
                "the narrow behaviour got no root params slot");
            Assert.Equal(16, len);

            Assert.Equal(0xC7, root[0]);
            // ⭐⭐ CE-421 (user ruling, 2026-09-28) — NOTHING carries over. The shadow starts EMPTY, so
            //   the bytes the narrow parser did not write are its own zero defaults, never the wide
            //   behaviour's head. ⛔ This loop asserted the OPPOSITE — "bytes 1..15 carried over,
            //   the partial-parse contract" — until CE-426 deleted the carry-over.
            for (int i = 1; i < 16; i++)
                Assert.Equal(0, root[i]);

            world.Dispose();
        }
    
        // ══ CE-437 + CE-429 — THE ROOT REGION IS THE BEHAVIOUR'S WHOLE BLOCK ════════════════
        //  ⭐ A generated behaviour's layout is {Asset}_Block = [Inputs][State]; the manifest states
        //     only the Input extent. These rails pin the three runtime consequences with a hand-built
        //     definition of that exact shape — 8 Input bytes, 16 State bytes, a 24-byte block.

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct BlockState16 { public long A; public long B; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        private struct Block24
        {
            [System.Runtime.InteropServices.FieldOffset(0)] public long In;
            [System.Runtime.InteropServices.FieldOffset(8)] public BlockState16 St;
        }

        private static void RegisterBlock(BehaviorRegistry registry, int id, string name) =>
            registry.Register(id, name, new BehaviorDefinition
            {
                Name                       = name,
                BrainTier                  = BehaviorConstants.BrainTierBTree,
                ManagedBlackboardVariables = new ManagedBlackboardVariable[] { new("In", typeof(long), 0) },
                BlackboardLayoutType       = typeof(Block24),
                // ⚠ A PARTIAL parse — writes byte 0 only — so every other byte shows what CARRIED.
                ParseParams = static (string json, byte* mem, int capacity, EntityRepository world, Entity self) =>
                {
                    mem[0] = 0xB1;
                },
            });

        private static void Assign(EntityRepository world, BehaviorIngressSystem sys, Entity e, string name)
        {
            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = name, JsonParams = "" });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE-429</c> — <b>the root slot is sized to the BLOCK, not to the manifest extent.</b>
        /// ⛔ Before it, this definition got 8 bytes and its State half did not exist.
        /// </summary>
        [Fact]
        public void BehaviorIngress_SizesTheRootSlotToTheWholeBlock()
        {
            var (world, sys, registry) = CreateFixture();
            RegisterBlock(registry, 0x0C_E4_37_01, "BlockSized");

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());
            Assign(world, sys, e, "BlockSized");

            Assert.True(RootParamsAccess.TryGetRootBytes(world, e, out _, out int len));
            Assert.Equal(24, len);
            world.Dispose();
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE-426</c> + <c>CE-421</c> — <b>EVERY assign starts the whole block from empty</b>, a
        /// re-assign of the SAME behaviour included. 🔒 User, <c>2026-09-28</c>: <i>"why would
        /// re-assigning the same behaviour deserve special handling"</i>.
        ///
        /// <para>⛔⛔ <b>CORRECTED, and the correction is mine.</b> <c>CE-437</c> first shipped this rail
        /// as <c>…TheStateHalfIsKeptOnReassignAndZeroedOnChange</c> — it kept the State on a
        /// same-behaviour re-assign, which is exactly the gate <c>CE-421</c>'s ruling had already
        /// rejected. The row was not read before building.</para>
        ///
        /// <para>⚠ Inverse-edit red-proof: restore a seed from the previous root slot in
        /// <c>BehaviorIngressSystem</c> and both halves fail — the State survives, the wide pattern
        /// lands in the Inputs.</para>
        /// </summary>
        [Fact]
        public void BehaviorIngress_EveryAssignStartsTheWholeBlockFromEmpty()
        {
            var (world, sys, registry) = CreateFixture();
            RegisterBlock(registry, 0x0C_E4_37_02, "BlockKeep");
            RegisterWide(registry, 0x0C_E4_37_03, "BlockKeep_Wide");

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());

            // ① same behaviour, re-assigned: the State half is NOT kept.
            Assign(world, sys, e, "BlockKeep");
            Assert.True(RootParamsAccess.TryGetRootBytes(world, e, out byte* root, out _));
            ((Block24*)root)->St.A = 0x1122334455667788;
            Assign(world, sys, e, "BlockKeep");
            Assert.True(RootParamsAccess.TryGetRootBytes(world, e, out root, out _));
            Assert.Equal(0, ((Block24*)root)->St.A);

            // ② a different behaviour first: nothing of it carries in either.
            Assign(world, sys, e, "BlockKeep_Wide");
            Assign(world, sys, e, "BlockKeep");
            Assert.True(RootParamsAccess.TryGetRootBytes(world, e, out root, out int len));
            Assert.Equal(24, len);
            Assert.Equal(0xB1, root[0]);
            for (int i = 1; i < 24; i++) Assert.Equal(0, root[i]);

            world.Dispose();
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE-426</c> — <b>a FAILED parse leaves the previous block untouched, State half
        /// included.</b> 📄 <c>Q76</c> §12.7 row 6 / <c>Q43</c> §4. ⭐ The shadow is the whole block
        /// (<c>CE-437</c>), so a parse that writes the State half and then throws changes nothing.
        /// </summary>
        [Fact]
        public void BehaviorIngress_AFailedParseLeavesThePreviousBlockUntouched()
        {
            var (world, sys, registry) = CreateFixture();
            registry.Register(0x0C_E4_26_01, "BlockFails", new BehaviorDefinition
            {
                Name                       = "BlockFails",
                BrainTier                  = BehaviorConstants.BrainTierBTree,
                ManagedBlackboardVariables = new ManagedBlackboardVariable[] { new("In", typeof(long), 0) },
                BlackboardLayoutType       = typeof(Block24),
                ParseParams = static (string json, byte* mem, int capacity, EntityRepository world, Entity self) =>
                {
                    ((Block24*)mem)->In   = 7;
                    ((Block24*)mem)->St.A = 7;
                    if (json == "fail") throw new InvalidOperationException("parse failed half-way");
                },
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());
            Assign(world, sys, e, "BlockFails");
            Assert.True(RootParamsAccess.TryGetRootBytes(world, e, out byte* root, out _));
            ((Block24*)root)->St.A = 1234;

            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = "BlockFails", JsonParams = "fail" });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);

            Assert.True(RootParamsAccess.TryGetRootBytes(world, e, out root, out _));
            Assert.Equal(7,    ((Block24*)root)->In);
            Assert.Equal(1234, ((Block24*)root)->St.A);
            world.Dispose();
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE-437</c> — <b>a thunk finds its block by its OWN behaviour, never the active one.</b>
        /// A hosted subtree runs under its HOST's active hash; keyed on the child's own hash it finds no
        /// slot and fails, instead of writing its State into the host's bytes. ⚠ And a slot narrower
        /// than the requested type is refused rather than overread.
        /// </summary>
        [Fact]
        public void RootParamsAccess_TryGetBlockFor_IsKeyedByTheBehavioursOwnIdentity()
        {
            var (world, sys, registry) = CreateFixture();
            const int id = 0x0C_E4_37_04;
            RegisterBlock(registry, id, "BlockOwn");

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());
            Assign(world, sys, e, "BlockOwn");

            Assert.True(RootParamsAccess.TryGetBlockFor<Block24>(world, e, id, out Block24* blk));
            Assert.True(RootParamsAccess.TryGetRootBytes(world, e, out byte* root, out _));
            Assert.True(root == (byte*)blk, "the block IS the root region");

            Assert.False(RootParamsAccess.TryGetBlockFor<Block24>(world, e, id + 1, out _),
                "another behaviour's identity (a hosted child under this host) must find nothing");
            Assert.False(RootParamsAccess.TryGetBlockFor<WideParams>(world, e, id, out _),
                "a type wider than the slot must be refused, not overread");

            world.Dispose();
        }

        /// <summary>
        /// ⭐ <c>CE-429</c> — the two numbers the region used to conflate. ⭐ An EMPTY manifest is still an
        /// authority: a behaviour with no <c>Role=Input</c> variable has 0 Input bytes but a real block.
        /// </summary>
        [Fact]
        public void RootParamsAccess_SeparatesTheBlockFromItsInputBytes()
        {
            var withInputs = new BehaviorDefinition
            {
                Name                       = "WithInputs",
                ManagedBlackboardVariables = new ManagedBlackboardVariable[] { new("In", typeof(long), 0) },
                BlackboardLayoutType       = typeof(Block24),
            };
            Assert.Equal(24, RootParamsAccess.RootParamsBytes(withInputs));
            Assert.Equal(8,  RootParamsAccess.InputBytes(withInputs));

            var stateOnly = new BehaviorDefinition
            {
                Name                       = "StateOnly",
                ManagedBlackboardVariables = System.Array.Empty<ManagedBlackboardVariable>(),
                BlackboardLayoutType       = typeof(BlockState16),
            };
            Assert.Equal(16, RootParamsAccess.RootParamsBytes(stateOnly));
            Assert.Equal(0,  RootParamsAccess.InputBytes(stateOnly));

            // ⚠ A curated behaviour declares no manifest: its whole layout is its parameters.
            var curated = new BehaviorDefinition { Name = "Curated", BlackboardLayoutType = typeof(NarrowParams) };
            Assert.Equal(16, RootParamsAccess.RootParamsBytes(curated));
            Assert.Equal(16, RootParamsAccess.InputBytes(curated));
        }
}
}
