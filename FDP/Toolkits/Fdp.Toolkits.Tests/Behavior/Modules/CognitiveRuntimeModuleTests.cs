using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Modules;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Behavior.Components;
using System.Linq;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests.Modules
{
    /// <summary>
    /// Verifies that <see cref="CognitiveRuntimeModule"/> registers exactly the expected
    /// systems into a <see cref="SystemGroup"/> (BHU-010 success condition).
    /// </summary>
    public class CognitiveRuntimeModuleTests
    {
        [Fact]
        public void CognitiveRuntimeModule_RegistersAllTickSystems()
        {
            // Arrange
            var registry = new BehaviorRegistry();
            var module   = new CognitiveRuntimeModule(registry);

            // Assert — 5 systems: arbitration, CognitiveInterrupt, the ONE brain tick,
            //          CognitiveCleanup, and (Batch 94) BehaviorFrame.
            // Order: ChannelArbitrationSystem -> CognitiveInterruptSystem -> BrainTickSystem
            //        -> CognitiveCleanupSystem -> BehaviorFrameSystem
            // ⛔ O7c-④b (2026-09-23): was SIX. BTreeTickSystem and HsmTickSystem<BrainHsm128> are ONE
            //   system now — the generic one could not survive retiring its component, and two
            //   near-identical non-generic systems is the duplication B3 already paid to remove.
            // ⛔ O7c-① (2026-09-22): was SEVEN before that. HsmTickSystem<BrainHsm64> went with its
            //   component, which nothing in production ever attached ⇒ an always-empty query.
            // ⭐ Batch 94 (94b): the pulse is LAST so it means "a brain tick HAS RUN".
            Assert.Equal(5, module.SimulationSystems.Count);
            Assert.IsType<ChannelArbitrationSystem>(module.SimulationSystems[0]);
            Assert.IsType<CognitiveInterruptSystem>(module.SimulationSystems[1]);
            Assert.IsType<BrainTickSystem>(module.SimulationSystems[2]);
            Assert.IsType<CognitiveCleanupSystem>(module.SimulationSystems[3]);
            Assert.IsType<BehaviorFrameSystem>(module.SimulationSystems[4]);

            // BHU-010: CognitiveInterruptSystem must appear before the brain tick.
            // ⭐ ONE index now covers both paradigms — which is the point: the two used to be kept in
            //   step by hand, and a rail asserting each separately could pass while they disagreed.
            var systemsList = module.SimulationSystems.ToList();
            int interruptIdx = systemsList.FindIndex(s => s is CognitiveInterruptSystem);
            int brainIdx     = systemsList.FindIndex(s => s is BrainTickSystem);
            int cleanupIdx   = systemsList.FindIndex(s => s is CognitiveCleanupSystem);
            Assert.True(interruptIdx < brainIdx,
                "CognitiveInterruptSystem must be registered before BrainTickSystem.");
            Assert.True(cleanupIdx > brainIdx,
                "CognitiveCleanupSystem must be registered after the brain tick.");

            // ⭐⭐ Batch 94: the pulse comes after every brain tick, so "the counter moved" means
            //    "a tick produced values", not "a tick is about to run".
            int frameIdx = systemsList.FindIndex(s => s is BehaviorFrameSystem);
            Assert.True(frameIdx > cleanupIdx,
                "BehaviorFrameSystem must be registered after CognitiveCleanupSystem.");
        }

        // ═══ O7c step 1 — A TICK SYSTEM FOR A COMPONENT NOTHING CAN ATTACH ══════════════════════

        /// <summary>
        /// ⭐⭐⭐ <b><c>O7c</c>-① rail — EVERY HSM tick system must be registered for a component the
        /// PRODUCTION path can actually attach.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §31.9 ④.
        ///
        /// <para>⛔⛔ <b>This is the <c>CE-315</c> defect class, one level up.</b> There, dead storage was
        /// used as a QUERY PREDICATE and the failure was silent NON-EXECUTION. Here the whole
        /// <i>query</i> is dead: <c>HsmTickSystem&lt;BrainHsm64&gt;</c> was registered, scheduled and
        /// ticked every frame against a component that <b>no production path has ever attached</b> —
        /// 📐 measured <c>2026-09-22</c>: <c>BehaviorTkbTranslator.Inject</c> is the ONE production
        /// attach site and it writes <c>BrainHsm128</c> unconditionally, so every
        /// <c>new BrainHsm64()</c> in the tree is in a test.</para>
        ///
        /// <para>⚠ <b>An empty query costs no correctness, which is exactly why nothing found it.</b>
        /// It cost a registration, a role-set bit, an ingress reset branch, two debug-session branches
        /// and a hot-reload sweep — all maintained for a component with no instances. ⇒ the rail asserts
        /// the AGREEMENT rather than the absence, so it keeps meaning something after the deletion:
        /// adding a tick system without an attach path reddens it.</para>
        ///
        /// <para>⛔⛔⛔ <b>SUPERSEDED BY <c>O7c</c>-④b (<c>2026-09-23</c>) — THE DEFECT CLASS IS NOW
        /// ELIMINATED BY CONSTRUCTION, WHICH IS WHY THE RAIL CHANGED SHAPE.</b> 📄 §31.16.4.</para>
        ///
        /// <para>📐 The original rail compared <c>HsmTickSystem&lt;T&gt;</c>'s generic arguments against
        /// what <c>BehaviorTkbTranslator</c> can attach. ⇒ <b>it cannot even be written any more</b>:
        /// there is no generic tick system, because the brain tick discovers by the OCCURRENCE-STORE
        /// TIER WALK rather than by a component type. A walk cannot name a component nothing
        /// attaches.</para>
        ///
        /// <para>⚠ <b>The claim EXPIRED; it was not dropped.</b> A silently deleted rail and a silently
        /// weakened one look identical in a diff, so the successor is asserted instead: <b>no system in
        /// the cognitive pipeline is generic over a brain component at all.</b> ⭐ That is the
        /// structural property the original was a proxy for — <i>"discovery must not be keyed to a
        /// component whose attach path may not exist"</i> — and it reddens the moment someone
        /// reintroduces the shape, which is the only way the defect can come back.</para>
        /// </summary>
        [Fact]
        public void NoBrainTickSystemIsGenericOverAComponent_O7c4b()
        {
            var module = new CognitiveRuntimeModule(new BehaviorRegistry());

            var genericSystems = module.SimulationSystems
                .Select(s => s.GetType())
                .Where(t => t.IsGenericType)
                .ToList();

            Assert.True(module.SimulationSystems.Count > 0);   // guard: a vacuous pass is not a pass

            Assert.True(genericSystems.Count == 0,
                "A cognitive system is generic over a type, which is how O7c-①'s always-empty query " +
                "arose: HsmTickSystem<BrainHsm64> was registered, scheduled and ticked every frame " +
                "against a component no production path ever attached. Discovery must come from the " +
                "occurrence-store tier walk, never from a component type. Generic systems: " +
                string.Join(", ", genericSystems.Select(t => t.Name)) + ".");
        }

        // ═══ P3 step 3b — THE EXECUTION GATE ════════════════════════════════════════════════════════
        //  📄 docs/DESIGN_Role_Affinity_Ownership.md §3.5, §6 step 3b.
        //  ⭐ Asserted at MODULE level rather than per system: the gate's whole claim is that the WHOLE
        //    cognitive pipeline leaves an unowned brain alone, and six separate rails would each be green
        //    while the pipeline as a whole still wrote to it through the one system nobody gated.

        private static EntityRepository GateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<BehaviorState>();
            repo.RegisterComponent<BrainInterrupts>();
            repo.RegisterComponent<LocomotionChannel>();
            repo.RegisterComponent<ActorCapabilityState>();
            repo.RegisterComponent<PreviousCapabilities>();
            return repo;
        }

        /// <summary>⭐ An entity carrying a full brain. <paramref name="owned"/> decides whether THIS node
        /// holds authority over its cognitive state — i.e. "my brain" versus "someone else's, replicated
        /// here".</summary>
        private static Entity BrainEntity(EntityRepository repo, bool owned)
        {
            var e = repo.CreateEntity();
            repo.AddComponent(e, new BehaviorState());
            repo.AddComponent(e, new BrainInterrupts());
            RootStateAccess.EnsureRootState(repo, e);   // ⛔ O7c-②: BrainBTreeState retired — the root cursor is an occurrence slot (§31).
            repo.AddComponent(e, new LocomotionChannel());
            repo.AddComponent(e, new ActorCapabilityState());
            if (owned)
            {
                repo.SetAuthority(e, ComponentTypeRegistry.GetId(typeof(BehaviorState)),   true);
                // 🔴🔴 O2 (2026-09-20) — SPLITTING A COMPONENT SPLITS ITS AUTHORITY, and this rail is
                //   what caught it. CognitiveCleanupSystem / CognitiveInterruptSystem now gate on
                //   BrainInterrupts (the interrupts moved there), so authority must be granted for the
                //   NEW component or the gate stops discriminating and an unowned brain IS touched.
                //   ⚠ In production `gateOnAuthority` is false on every host today, so this is not yet
                //   a live defect — but BrainInterrupts must join BrainBlackboard's ownership set
                //   before the gate is ever turned on. Recorded in design §6's O2 AS-BUILT block.
                repo.SetAuthority(e, ComponentTypeRegistry.GetId(typeof(BrainInterrupts)),  true);
            }
            return e;
        }

        private static void RunPipeline(CognitiveRuntimeModule module, EntityRepository repo)
        {
            foreach (var sys in module.SimulationSystems)
                sys.Execute(repo, 0.016f);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Step 3b's gate — a node holding brain components it does NOT own touches them zero
        /// times.</b>
        ///
        /// <para>⛔⛔ This is the rail that decides whether <c>P3</c> is more than cosmetic. Authority gates
        /// REPLICATION — every egress translator checks it — but a QUERY does not. ⇒ without the gate a
        /// node can decline the brain components, publish nothing, and still RUN the tree, so two nodes
        /// tick one brain and the ownership work buys nothing.</para>
        ///
        /// <para>📐 <c>CognitiveCleanupSystem</c> is the probe: it unconditionally zeroes
        /// <c>Interrupt_MobilityLost</c> on every blackboard it matches, so a non-zero value surviving the
        /// whole pipeline proves the entity was never visited.</para>
        /// </summary>
        [Fact]
        public void WithTheGateOn_AnUnownedBrainIsNeverTouched()
        {
            var repo   = GateWorld();
            var module = new CognitiveRuntimeModule(new BehaviorRegistry(), gateOnAuthority: true);

            var mine      = BrainEntity(repo, owned: true);
            var someones  = BrainEntity(repo, owned: false);

            foreach (var e in new[] { mine, someones })
            {
                ref var bb = ref repo.GetComponentRW<BrainInterrupts>(e);
                bb.Interrupt_MobilityLost = 1;
            }

            RunPipeline(module, repo);

            Assert.Equal(0, repo.GetComponent<BrainInterrupts>(mine).Interrupt_MobilityLost);
            Assert.Equal(1, repo.GetComponent<BrainInterrupts>(someones).Interrupt_MobilityLost);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>And with the gate OFF — the default — BOTH are processed, exactly as today.</b>
        ///
        /// <para>⛔⛔ The opt-in guarantee, and it is not ceremony. A promoted ghost owns NOTHING until a
        /// role policy or an explicit grant says otherwise, so an unconditional gate would stop a node
        /// processing every entity it did not create. ⚠ That is <c>CE-256</c>'s own failure —
        /// <i>"owns nothing, so nothing it is responsible for ever moves"</i> — reproduced by its fix.
        /// ⇒ the gate follows the POLICY (step 4), and until then this rail is what says so.</para>
        /// </summary>
        [Fact]
        public void WithTheGateOff_BothBrainsAreProcessed_UnchangedFromToday()
        {
            var repo   = GateWorld();
            var module = new CognitiveRuntimeModule(new BehaviorRegistry());   // default: gate OFF

            var mine     = BrainEntity(repo, owned: true);
            var someones = BrainEntity(repo, owned: false);

            foreach (var e in new[] { mine, someones })
            {
                ref var bb = ref repo.GetComponentRW<BrainInterrupts>(e);
                bb.Interrupt_MobilityLost = 1;
            }

            RunPipeline(module, repo);

            Assert.Equal(0, repo.GetComponent<BrainInterrupts>(mine).Interrupt_MobilityLost);
            Assert.Equal(0, repo.GetComponent<BrainInterrupts>(someones).Interrupt_MobilityLost);
        }
    }
}
