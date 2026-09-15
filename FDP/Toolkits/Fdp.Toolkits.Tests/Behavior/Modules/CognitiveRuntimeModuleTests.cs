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

            // Assert — 7 systems: arbitration, CognitiveInterrupt, BTree, HsmHsm128, HsmHsm64,
            //          CognitiveCleanup, and (Batch 94) BehaviorFrame.
            // Order: ChannelArbitrationSystem -> CognitiveInterruptSystem -> BTreeTickSystem
            //        -> HsmTickSystem<BrainHsm128> -> HsmTickSystem<BrainHsm64> -> CognitiveCleanupSystem
            //        -> BehaviorFrameSystem
            // ⭐ Batch 94 (94b): the pulse is LAST so it means "a brain tick HAS RUN". ⚠ This
            //   assertion is what the handoff warned would need updating.
            Assert.Equal(7, module.SimulationSystems.Count);
            Assert.IsType<ChannelArbitrationSystem>(module.SimulationSystems[0]);
            Assert.IsType<CognitiveInterruptSystem>(module.SimulationSystems[1]);
            Assert.IsType<BTreeTickSystem>(module.SimulationSystems[2]);
            Assert.IsType<HsmTickSystem<BrainHsm128>>(module.SimulationSystems[3]);
            Assert.IsType<HsmTickSystem<BrainHsm64>>(module.SimulationSystems[4]);
            Assert.IsType<CognitiveCleanupSystem>(module.SimulationSystems[5]);
            Assert.IsType<BehaviorFrameSystem>(module.SimulationSystems[6]);

            // BHU-010: CognitiveInterruptSystem must appear before BTree and HSM ticks.
            var systemsList = module.SimulationSystems.ToList();
            int interruptIdx = systemsList.FindIndex(s => s is CognitiveInterruptSystem);
            int btreeIdx     = systemsList.FindIndex(s => s is BTreeTickSystem);
            int hsmIdx128    = systemsList.FindIndex(s => s is HsmTickSystem<BrainHsm128>);
            int cleanupIdx   = systemsList.FindIndex(s => s is CognitiveCleanupSystem);
            Assert.True(interruptIdx < btreeIdx,
                "CognitiveInterruptSystem must be registered before BTreeTickSystem.");
            Assert.True(interruptIdx < hsmIdx128,
                "CognitiveInterruptSystem must be registered before HsmTickSystem.");
            Assert.True(cleanupIdx > hsmIdx128,
                "CognitiveCleanupSystem must be registered after all brain tick systems.");

            // ⭐⭐ Batch 94: the pulse comes after every brain tick, so "the counter moved" means
            //    "a tick produced values", not "a tick is about to run".
            int frameIdx = systemsList.FindIndex(s => s is BehaviorFrameSystem);
            Assert.True(frameIdx > cleanupIdx,
                "BehaviorFrameSystem must be registered after CognitiveCleanupSystem.");
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
            repo.RegisterComponent<BrainBlackboard>();
            repo.RegisterComponent<BrainBTreeState>();
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
            repo.AddComponent(e, new BrainBlackboard());
            repo.AddComponent(e, new BrainBTreeState());
            repo.AddComponent(e, new LocomotionChannel());
            repo.AddComponent(e, new ActorCapabilityState());
            if (owned)
            {
                repo.SetAuthority(e, ComponentTypeRegistry.GetId(typeof(BehaviorState)),   true);
                repo.SetAuthority(e, ComponentTypeRegistry.GetId(typeof(BrainBlackboard)), true);
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
                ref var bb = ref repo.GetComponentRW<BrainBlackboard>(e);
                bb.Interrupt_MobilityLost = 1;
            }

            RunPipeline(module, repo);

            Assert.Equal(0, repo.GetComponent<BrainBlackboard>(mine).Interrupt_MobilityLost);
            Assert.Equal(1, repo.GetComponent<BrainBlackboard>(someones).Interrupt_MobilityLost);
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
                ref var bb = ref repo.GetComponentRW<BrainBlackboard>(e);
                bb.Interrupt_MobilityLost = 1;
            }

            RunPipeline(module, repo);

            Assert.Equal(0, repo.GetComponent<BrainBlackboard>(mine).Interrupt_MobilityLost);
            Assert.Equal(0, repo.GetComponent<BrainBlackboard>(someones).Interrupt_MobilityLost);
        }
    }
}
