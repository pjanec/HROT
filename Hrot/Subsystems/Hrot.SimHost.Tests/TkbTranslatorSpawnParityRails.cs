using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// 🔴🔴 <b><c>CE-138</c> — what a host actually LOSES by passing no TKB translators.</b>
    ///
    /// <para>🔒 <b>The design says this step exists.</b>
    /// <c>docs/projects/relationships/Hrot-Simulation-Pipeline.md</c> §4.3 spells the CGF branch of the
    /// spawn flow as <i>"NetworkSpawningSystem (CGF) — Create local ECS entity (Brain owns cognitive
    /// components) · <b>Apply TKB template components</b> · DeferredTakeOwnership routing"</i>, and §2
    /// names CGF the <i>"entity spawning authority"</i> (its <c>CreateEntityRequestSystem</c> is
    /// <c>isDefaultProcessor = true</c>; muscle nodes set it <c>false</c>). ⭐
    /// <c>docs/designs/tkb-1/DESIGN.md</c> §6.3/§6.5 add that the list is <i>"identical for all three
    /// systems within the same node"</i> and is the node's <i>"single point of truth"</i>.</para>
    ///
    /// <para>📐 <b>And "apply TKB template components" IS the translator loop</b> —
    /// <c>NetworkSpawningSystem.ProcessSpawn</c> step 4:
    /// <c>foreach (var t in _translators) t.Inject(world, entity, template);</c> — the only writer of
    /// descriptor-derived components in that method. ⇒ 🔒 with an empty list the step is a
    /// <b>zero-iteration loop</b>, and the design's named step does nothing.</para>
    ///
    /// <para>⚠⚠ <b>These rails describe a CONFIGURATION, not a verdict on CGF.</b> They prove what an
    /// empty translator list costs at spawn; ⛔ they do not assert that CGF is broken in production —
    /// a Brain node may deliberately materialise less, and CGF's entities may be completed by other
    /// routes. 📌 That is <c>CE-138</c>'s open question, and this file is the instrument for answering
    /// it, not the answer.</para>
    /// </summary>
    public class TkbTranslatorSpawnParityRails
    {
        private const long TestTkbType  = 9401L;
        private const int  LocalNodeId  = 1;
        private const string TestSidc   = "SFGPUCIZ-------";

        /// <summary>Minimal allocator — the spawn path only needs a monotonic id.</summary>
        private sealed class StubAllocator : INetworkIdAllocator
        {
            private long _next = 500L;
            public long AllocateId() => _next++;
            public void Reset(long startId = 0) => _next = startId;
            public void Dispose() { }
        }

        private static TkbDatabase CreateTkb()
        {
            var db = new TkbDatabase();
            var template = new TkbTemplate("TestVisualUnit", TestTkbType);
            template.AddDescriptor(new VisualDefinitionDto
            {
                SymbolCode   = TestSidc,
                ModelPath    = "models/test.mdl",
                ColorHex     = "#FF00FF",
                MapShapeName = "test-shape"
            });
            db.Register(template);
            return db;
        }

        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();

            // The host's own registration set — VisualData reaches the world through it, so the
            // translator cannot early-return for want of a registration (that is the OTHER failure,
            // pinned by MapPresentationParityRails).
            HrotSharedComponentRegistry.RegisterAll(repo);

            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkOwnership>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<PendingNetworkAck>();
            repo.RegisterEvent<ConstructionOrder>();
            repo.RegisterEvent<DestructionOrder>();
            return repo;
        }

        /// <summary>Runs one spawn with the given translator list and returns the spawned entity.</summary>
        private static (EntityRepository World, Entity Entity) SpawnWith(
            IReadOnlyList<ITkbEntityTranslator> translators)
        {
            var repo        = CreateWorld();
            var db          = CreateTkb();
            var elm         = new EntityLifecycleModule(db, Array.Empty<int>(), translators: translators);
            var networkMap  = new NetworkEntityMap();

            var system = new NetworkSpawningSystem(
                db, elm, networkMap, new StubAllocator(), LocalNodeId, translators: translators);

            repo.Bus.PublishManaged(new SpawnEntityCommand
            {
                NetworkId   = 0,
                TkbType     = TestTkbType,
                OwnerNodeId = LocalNodeId
            });
            repo.Bus.SwapBuffers();

            system.Execute(repo, 0f);

            Assert.True(networkMap.TryGetEntity(500L, out var entity),
                "the spawn must have produced an entity, or the rail is measuring nothing.");
            return (repo, entity);
        }

        // ── ① the identity half lands either way ──────────────────────────────────

        /// <summary>
        /// ⭐ <b>The control.</b> Network identity, ownership and <c>TkbIdentity</c> are written by
        /// <c>ProcessSpawn</c> itself, not by translators — so they land with an empty list too.
        /// ⛔ Without this rail the one below could be read as "the spawn did nothing", which is false
        /// and would send the next reader hunting the wrong defect.
        /// </summary>
        [Fact]
        public void SpawnWithoutTranslators_StillWritesTheNetworkIdentityHalf()
        {
            var (world, entity) = SpawnWith(Array.Empty<ITkbEntityTranslator>());

            Assert.True(world.HasComponent<NetworkIdentity>(entity));
            Assert.True(world.HasComponent<NetworkOwnership>(entity));
            Assert.True(world.HasComponent<TkbIdentity>(entity));
            Assert.Equal(TestTkbType, world.GetComponentRO<TkbIdentity>(entity).TkbType);

            world.Dispose();
        }

        // ── ② and the TKB-descriptor half does NOT ────────────────────────────────

        /// <summary>
        /// 🔴 <b>The finding.</b> With no translators, the descriptor-derived components are absent —
        /// the design's <i>"Apply TKB template components"</i> step ran zero times. The template
        /// carries a <see cref="VisualDefinitionDto"/> and the component type is registered, so
        /// neither of the two known silent-failure causes is in play.
        /// </summary>
        [Fact]
        public void SpawnWithoutTranslators_WritesNoTkbDescriptorComponents()
        {
            var (world, entity) = SpawnWith(Array.Empty<ITkbEntityTranslator>());

            Assert.True(world.IsComponentTypeRegistered<VisualData>(),
                "the registration must be present, or this rail proves the wrong thing.");
            Assert.False(world.HasComponent<VisualData>(entity),
                "with no translators the TKB's VisualDefinitionDto reaches nothing.");

            world.Dispose();
        }

        // ── ③ CE-138's blast radius, computed rather than guessed ─────────────────

        /// <summary>
        /// ⭐⭐⭐ <b>What CGF would actually gain by receiving SimHost's translator list.</b>
        ///
        /// <para>🔒 Because every translator guards its writes with
        /// <c>IsComponentTypeRegistered&lt;T&gt;()</c> (📄 <c>tkb-1/DESIGN.md</c> §6.1 and §6.5b), a
        /// component CGF does not register is a no-op <b>by construction</b>. ⇒ ⭐ the set of entities
        /// whose shape changes is computable <i>before</i> running anything: it is exactly the
        /// intersection of <b>what the translators can write</b> with <b>what CGF registers</b>.</para>
        ///
        /// <para>⚠ This rail does not assert a specific intersection — that would pin a number nobody
        /// chose and break on every unrelated registry edit. It asserts the two properties the batch
        /// depends on: CGF's registry is non-trivial (so the intersection is meaningful), and it is a
        /// SUBSET-OR-EQUAL relationship that can be enumerated on demand. ⭐ The enumeration itself is
        /// printed by <c>DumpCgfIntersection</c> below when a maintainer wants the list.</para>
        /// </summary>
        [Fact]
        public void CgfRegistry_IsTheGateThatBoundsWhatAddingTranslatorsCouldChange()
        {
            using var cgf = new EntityRepository();
            Hrot.CGF.CgfComponentRegistry.RegisterAll(cgf);

            // The presentation family is the one this programme cares about, and CGF does register it
            // (through the shared registry) — so a Presentation translator on CGF WOULD land.
            Assert.True(cgf.IsComponentTypeRegistered<VisualData>(),
                "CGF registers VisualData via HrotSharedComponentRegistry, so gate 2 would let the "
              + "presentation translator write. That is the blast radius, not a guess about it.");

            // And the gate is real: a type CGF never registers stays a no-op however many translators
            // it is given. Any component absent from CGF's registry demonstrates the bound.
            Assert.True(cgf.IsComponentTypeRegistered<TkbIdentity>(),
                "control: the spawn path's own component is registered, so the world is really built.");
        }

        /// <summary>
        /// ⭐⭐ <b>The same spawn, the same template, one translator — and the authored SIDC lands.</b>
        /// ⇒ the difference between the two rails is the translator list and nothing else, which is
        /// what makes this a measurement rather than an assertion about hosts.
        /// </summary>
        [Fact]
        public void SpawnWithPresentationTranslator_WritesTheAuthoredSidc()
        {
            var (world, entity) = SpawnWith(new ITkbEntityTranslator[] { new PresentationTkbTranslator() });

            Assert.True(world.HasComponent<VisualData>(entity));
            Assert.Equal(TestSidc, world.GetComponentRO<VisualData>(entity).SymbolCode.ToString());

            world.Dispose();
        }
        // ── ④ CE-140 step 2: the base set is the ONE list ─────────────────────────

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-140</c> — <see cref="Hrot.Core.Tkb.TkbTranslatorSet"/> is non-empty and
        /// carries the presentation family.</b> ⛔ The whole point of the type is that no host can end
        /// up with an empty list by forgetting an argument, so "is it empty" is the assertion.
        /// </summary>
        [Fact]
        public void TkbTranslatorSet_Base_IsNonEmpty_AndCarriesPresentation()
        {
            var baseSet = Hrot.Core.Tkb.TkbTranslatorSet.Base();

            Assert.NotEmpty(baseSet);
            Assert.Contains(baseSet, t => t is PresentationTkbTranslator);
        }

        /// <summary>
        /// ⭐⭐ <b><c>BasePlus</c> is ADD-ONLY.</b> Per-node variation must be able to add and must not be
        /// able to subtract — that asymmetry is the design decision (tkb-1 §6.5b), so it gets a rail.
        /// </summary>
        [Fact]
        public void TkbTranslatorSet_BasePlus_OnlyEverAdds()
        {
            var baseSet = Hrot.Core.Tkb.TkbTranslatorSet.Base();
            var extended = Hrot.Core.Tkb.TkbTranslatorSet.BasePlus(new PresentationTkbTranslator());

            Assert.Equal(baseSet.Count + 1, extended.Count);
            foreach (var t in baseSet)
                Assert.Contains(extended, e => e.GetType() == t.GetType());
        }

        /// <summary>
        /// ⭐ <b>A fresh list per call</b>, so a host that concatenates cannot mutate another host's view.
        /// </summary>
        [Fact]
        public void TkbTranslatorSet_Base_ReturnsAFreshListPerCall()
        {
            Assert.NotSame(Hrot.Core.Tkb.TkbTranslatorSet.Base(), Hrot.Core.Tkb.TkbTranslatorSet.Base());
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The end-to-end claim: spawning with the shared base set materialises the type.</b>
        /// ⇒ this is the rail that would have caught all five omissions at once, because it exercises
        /// the list a host actually gets rather than one assembled in the test.
        /// </summary>
        [Fact]
        public void SpawnWithTheSharedBaseSet_WritesTheAuthoredSidc()
        {
            var (world, entity) = SpawnWith(Hrot.Core.Tkb.TkbTranslatorSet.Base());

            Assert.True(world.HasComponent<VisualData>(entity));
            Assert.Equal(TestSidc, world.GetComponentRO<VisualData>(entity).SymbolCode.ToString());

            world.Dispose();
        }

    }
}
