using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common.EntityCreation;
using Hrot.Core.Network;
using Hrot.Core.Tkb;
using Hrot.Map.Common;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <b><c>CE-140</c> step 3 — the <c>EntityCreationPack</c>.</b>
    ///
    /// <para>🔒 <b>The ruling these enforce</b> (user, <c>2026-08-31</c>): <i>"the shared code for entity
    /// creation support should not restrict any ECS enabled node from creating own networked entities …
    /// no exceptions, not removing capabilities by design, and only concrete authoring code picks the way
    /// it needs."</i> ⇒ ⛔ <b>the pack has no opt-out</b>, and these rails are what keep it that way.</para>
    ///
    /// <para>📄 Acceptance ②③⑤⑨ of <c>docs/DESIGN_Entity_Creation_Unification.md</c> §6.</para>
    /// </summary>
    public class EntityCreationPackRails
    {
        private static EntityCreationContext MinimalContext(
            out EntityRepository world,
            IReadOnlyList<ITkbEntityTranslator>? extras = null,
            bool arbiter = false,
            IReadOnlyList<TranslatorPlacement>? placements = null,
            Hrot.Core.Network.IEntityCreationRequestEgress? egress = null)
        {
            world = new EntityRepository();
            var tkb = HrotEnvironment.CreateTkb();
            return new EntityCreationContext
            {
                World                = world,
                EntityMap            = new NetworkEntityMap(),
                TkbDb                = tkb,
                IdAllocator          = new SequentialIdAllocator(),
                Elm                  = new EntityLifecycleModule(tkb, Array.Empty<int>()),
                NodeId               = 7,
                IsBroadcastArbiter   = arbiter,
                ExtraTranslators     = extras,
                TranslatorPlacements = placements,
                RequestEgress        = egress,
            };
        }

        private sealed class RecordingEgress : Hrot.Core.Network.IEntityCreationRequestEgress
        {
            public List<Hrot.Core.Network.EntityCreationRequest> Sent { get; } = new();
            public void Send(Hrot.Core.Network.EntityCreationRequest request) => Sent.Add(request);
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>D1</c> — a host that SUPPLIES an egress actually gets a forwarder, wired in the
        /// right place.</b>
        ///
        /// <para>📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §3.4b. ⭐ Asserted through the
        /// PRODUCTION pack and the PRODUCTION request system, end to end: a request addressed elsewhere
        /// must leave via the egress and must NOT be materialised here.</para>
        ///
        /// <para>⚠ This is the control for the optional dependency. <c>RequestEgress</c> may legitimately
        /// be null — that states "this host does not forward", true of every host that materialises
        /// entities itself. ⛔ What must never happen is a host that HAS one and silently does not use it,
        /// which is the silent-default shape this codebase keeps producing.</para>
        /// </summary>
        [Fact]
        public void Build_WhenAnEgressIsSupplied_ARequestForAnotherNodeIsForwarded_NotMaterialised()
        {
            var egress   = new RecordingEgress();
            var ctx      = MinimalContext(out var world, egress: egress);
            var creation = EntityCreationPack.Build(ctx);

            creation.LocalRequests.Enqueue(new Hrot.Core.Network.EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = 99,              // ⭐ NOT this node (NodeId = 7)
                TkbType            = ctx.TkbDb.GetAll().First().TkbType,
            });

            creation.RequestSystem.Execute(world, 0f);
            world.Bus.SwapBuffers();

            var sent = Assert.Single(egress.Sent);
            Assert.Equal(99, sent.OwnerAppInstanceId);
            Assert.Empty(((Fdp.ModuleHost.Abstractions.ISimulationView)world)
                .ReadManagedEvents<Fdp.Toolkit.NetworkSpawning.Events.SpawnEntityCommand>());
        }

        /// <summary>
        /// ⭐⭐ <b>And with NO egress, the pack composes exactly as before.</b>
        /// ⛔ Non-vacuity for the rail above: it must be the EGRESS that changes the outcome, not the
        /// request's owner value on its own.
        /// </summary>
        [Fact]
        public void Build_WithNoEgress_ARequestForAnotherNodeIsSilentlyIgnored_AsBefore()
        {
            var ctx      = MinimalContext(out var world);
            var creation = EntityCreationPack.Build(ctx);

            creation.LocalRequests.Enqueue(new Hrot.Core.Network.EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = 99,
                TkbType            = ctx.TkbDb.GetAll().First().TkbType,
            });

            creation.RequestSystem.Execute(world, 0f);
            world.Bus.SwapBuffers();

            Assert.Empty(((Fdp.ModuleHost.Abstractions.ISimulationView)world)
                .ReadManagedEvents<Fdp.Toolkit.NetworkSpawning.Events.SpawnEntityCommand>());
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Acceptance ⑨ — no node can be denied the genesis pipeline.</b> Whatever the inputs, a
        /// built pack yields BOTH the request system and the spawn system. ⛔ If someone adds a flag that
        /// omits one, this reddens — which is the whole point, because the omission is exactly what made
        /// "every node can create entities" false before.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Build_AlwaysYieldsBothTheRequestAndSpawnSystems(bool arbiter)
        {
            var ctx = MinimalContext(out _, arbiter: arbiter);

            var creation = EntityCreationPack.Build(ctx);

            Assert.NotNull(creation.RequestSystem);
            Assert.NotNull(creation.SpawnSystem);
            Assert.NotNull(creation.FinalizationSystem);
            Assert.NotNull(creation.LocalRequests);
        }

        /// <summary>
        /// ⭐⭐ <b>Acceptance ② — the translator list can never be empty.</b> 📌 Five production defects
        /// were exactly this: an optional <c>translators:</c> defaulting to <c>Array.Empty</c>, so the TKB
        /// projection loop ran zero times and entities were born with no components.
        /// </summary>
        [Fact]
        public void Build_TranslatorListIsNeverEmpty()
        {
            var creation = EntityCreationPack.Build(MinimalContext(out _));

            Assert.NotEmpty(creation.Translators);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Acceptance ③ — ONE list instance reaches every consumer.</b>
        /// <c>tkb-1/DESIGN.md</c> §6.3 requires the list be <i>"identical for all three systems within the
        /// same node"</i>; handing the same INSTANCE is what makes that true by construction instead of by
        /// convention. ⛔ Reference equality, not sequence equality — two equal lists would still let the
        /// two drift apart later.
        /// </summary>
        [Fact]
        public void Build_HandsTheSameListInstanceToTheElm()
        {
            var ctx = MinimalContext(out _);

            var creation = EntityCreationPack.Build(ctx);

            // ⚠ REFLECTION, deliberately: EntityLifecycleModule keeps `_translators` private and exposes
            //   no accessor, so the §6.3 "one instance" invariant is not observable through its public
            //   API. ⭐ The better long-term fix is a read-only accessor on the ELM — that is a change to
            //   Fdp.Toolkits and out of this slice's scope, so the rail reads the field instead of going
            //   unwritten. ⛔ If this throws, the field was renamed: fix the rail, not the invariant.
            var field = typeof(EntityLifecycleModule).GetField(
                "_translators",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(field);

            var onElm = field!.GetValue(ctx.Elm);

            Assert.Same(creation.Translators, onElm);
        }

        /// <summary>
        /// ⭐⭐ <b>Acceptance ② — <c>ExtraTranslators</c> only ever ADDS.</b> There is no way to pass a
        /// narrower list than <c>TkbTranslatorSet.Base()</c>. ⛔ Per-component narrowing is gate ②
        /// (<c>IsComponentTypeRegistered</c>), never the list — <c>tkb-1/DESIGN.md</c> §6.5b.
        /// </summary>
        [Fact]
        public void ExtraTranslators_OnlyEverAdds()
        {
            var bare  = EntityCreationPack.Build(MinimalContext(out _));
            var extra = new CountingTranslator();
            var wider = EntityCreationPack.Build(
                MinimalContext(out _, extras: new ITkbEntityTranslator[] { extra }));

            Assert.Equal(bare.Translators.Count + 1, wider.Translators.Count);
            Assert.Contains(extra, wider.Translators);
            // and every base translator survived
            foreach (var t in bare.Translators)
                Assert.Contains(wider.Translators.Select(x => x.GetType()), ty => ty == t.GetType());
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE-146</c> — <b>the ORDER-SENSITIVE addition lands where it says, not at the end.</b>
        ///
        /// <para>📌 The Stride editor's <c>InfantryVehicleStateStripTkbTranslator</c> must run
        /// <i>"immediately after <c>VehicleKinematicsTkbTranslator</c>"</i>; <c>CE-145</c> recorded that
        /// <c>BasePlus</c> APPENDS and so violated that contract. ⛔ This rail is the one that would have
        /// caught it: appending is off by four positions, and the assertion is on the ADJACENCY, not on a
        /// literal index — an index would go green again for the wrong reason the day <c>Base()</c>
        /// changes.</para>
        /// </summary>
        [Fact]
        public void TranslatorPlacements_PutTheAdditionImmediatelyAfterItsAnchor()
        {
            var strip = new CountingTranslator();
            var ctx = MinimalContext(out _, placements: new[]
            {
                TranslatorPlacement.After<CarKinem.Tkb.VehicleKinematicsTkbTranslator>(strip),
            });

            var list = EntityCreationPack.Build(ctx).Translators;

            int anchor = list.ToList()
                .FindIndex(t => t is CarKinem.Tkb.VehicleKinematicsTkbTranslator);
            Assert.True(anchor >= 0, "the anchor translator must be in Base()");
            Assert.Same(strip, list[anchor + 1]);

            // ⛔ and it is NOT merely appended — that is exactly the CE-145 defect.
            Assert.NotSame(strip, list[list.Count - 1]);
        }

        /// <summary>
        /// ⛔⛔ <c>CE-146</c> — <b>a placement whose anchor is absent THROWS.</b> Appending instead would
        /// be the SILENT-DEFAULT shape this whole family exists to kill: the caller stated an ordering
        /// contract and would receive a list that quietly does not honour it.
        /// </summary>
        [Fact]
        public void TranslatorPlacements_ThrowWhenTheAnchorIsNotInTheList()
        {
            var ctx = MinimalContext(out _, placements: new[]
            {
                TranslatorPlacement.After<CountingTranslator>(new CountingTranslator()),
            });

            var ex = Assert.Throws<InvalidOperationException>(() => EntityCreationPack.Build(ctx));
            Assert.Contains("CountingTranslator", ex.Message);
        }

        /// <summary>
        /// ⛔ <c>CE-146</c> — <b>the two addition forms are alternatives, not a merge.</b> Two ways to say
        /// one thing is the duplicate-mechanism trap, so setting both is refused at construction rather
        /// than silently concatenated in some order nobody chose.
        /// </summary>
        [Fact]
        public void ExtraTranslatorsAndPlacements_CannotBothBeSet()
        {
            var ctx = MinimalContext(
                out _,
                extras:     new ITkbEntityTranslator[] { new CountingTranslator() },
                placements: new[] { TranslatorPlacement.Append(new CountingTranslator()) });

            Assert.Throws<ArgumentException>(() => EntityCreationPack.Build(ctx));
        }

        /// <summary>
        /// ⭐⭐ <b>Acceptance ⑤ — a skipped piece is REPORTED.</b> Every one of the five defects behind
        /// this design was a silent omission; <c>Unserviceable</c> is the mechanism that makes the next
        /// one loud, and it must name the specific piece rather than just failing a count.
        /// </summary>
        [Fact]
        public void Unserviceable_NamesEachPieceTheHostDidNotSchedule()
        {
            var creation = EntityCreationPack.Build(MinimalContext(out _));

            Assert.Equal(string.Empty, creation.Unserviceable(new object[]
            {
                creation.RequestSystem, creation.SpawnSystem, creation.FinalizationSystem,
            }));

            var missingRequest = creation.Unserviceable(new object[]
            {
                creation.SpawnSystem, creation.FinalizationSystem,
            });
            Assert.Contains("RequestSystem", missingRequest);

            var missingAll = creation.Unserviceable(Array.Empty<object>());
            Assert.Contains("RequestSystem", missingAll);
            Assert.Contains("SpawnSystem", missingAll);
            Assert.Contains("FinalizationSystem", missingAll);
        }

        /// <summary>
        /// ⛔⛔ <b>The context must carry NO kernel — pack constructs, host schedules.</b> This is the
        /// <c>MapInteractionContext</c> precedent (<c>UXI-23 S2b</c>): structural enforcement, not a
        /// convention. ⚠ If a <c>ModuleHostKernel</c> ever appears here, the pack can start scheduling
        /// and the host loses the single place that knows what was registered.
        /// </summary>
        [Fact]
        public void Context_CarriesNoKernel()
        {
            // ⭐ Matched by type NAME rather than by `typeof(ModuleHostKernel)`, so the rail needs no
            //   reference to the kernel's assembly — and it also catches a kernel smuggled in behind an
            //   interface or a wrapper whose name still says Kernel.
            var offending = typeof(EntityCreationContext)
                .GetProperties()
                .Where(p => p.PropertyType.Name.Contains("Kernel", StringComparison.OrdinalIgnoreCase))
                .Select(p => $"{p.Name} : {p.PropertyType.Name}")
                .ToList();

            Assert.Empty(offending);
        }

        /// <summary>
        /// ⭐ <b>The pack has no opt-out switch.</b> Reflection over the context: no boolean may exist
        /// whose name suggests suppressing a piece. ⚠ Deliberately name-based and therefore weak — it is a
        /// tripwire for the next person reaching for `SkipSpawnSystem`, not a proof.
        /// </summary>
        [Fact]
        public void Context_HasNoSuppressionFlag()
        {
            var suspicious = typeof(EntityCreationContext)
                .GetProperties()
                .Where(p => p.PropertyType == typeof(bool))
                .Select(p => p.Name)
                .Where(n => n.Contains("Skip",    StringComparison.OrdinalIgnoreCase)
                         || n.Contains("Omit",    StringComparison.OrdinalIgnoreCase)
                         || n.Contains("Disable", StringComparison.OrdinalIgnoreCase)
                         || n.Contains("Without", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Empty(suspicious);
        }

        private sealed class CountingTranslator : ITkbEntityTranslator
        {
            public IEnumerable<Type> GetConsumedDescriptors() => Array.Empty<Type>();
            public void Inject(EntityRepository repo, Entity entity, TkbTemplate template) { }
        }
    }
}
