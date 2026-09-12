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

            // ⭐ FOUR pieces since P2 — PromotionSystem joined when ghost promotion's registrar moved out
            //   of NedReplicationModule (DESIGN_Role_Affinity_Ownership.md §3.7). ⭐⭐ This rail REDDENED on
            //   that change and was right to: it is the control that every built piece is accounted for,
            //   so a new piece must be added here deliberately rather than the assertion relaxed.
            Assert.Equal(string.Empty, creation.Unserviceable(new object[]
            {
                creation.RequestSystem, creation.SpawnSystem, creation.FinalizationSystem,
                creation.PromotionSystem,
            }));

            var missingRequest = creation.Unserviceable(new object[]
            {
                creation.SpawnSystem, creation.FinalizationSystem, creation.PromotionSystem,
            });
            Assert.Contains("RequestSystem", missingRequest);

            var missingAll = creation.Unserviceable(Array.Empty<object>());
            Assert.Contains("RequestSystem", missingAll);
            Assert.Contains("SpawnSystem", missingAll);
            Assert.Contains("FinalizationSystem", missingAll);
            Assert.Contains("PromotionSystem", missingAll);
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

        // ── P2 — GHOST PROMOTION MOVED INTO THE PACK ──────────────────────────────────────────────
        //
        // 📄 docs/DESIGN_Role_Affinity_Ownership.md §3.7 · §6 step 0a, whose gate is: "a node built from
        //    the pack registers promotion EXACTLY ONCE; and a BDC-composed node promotes its ghosts".
        //
        // ⭐⭐ The rails below split that gate into the four things that can actually go wrong in a MOVE:
        //    the add is missing · the remove is missing · the ORDER flips · a host forgets to schedule it.

        /// <summary>
        /// ⭐⭐⭐ <b>The add half — the pack builds promotion, and it shares the node's ONE translator
        /// list.</b>
        ///
        /// <para>⭐ The list identity is the load-bearing assertion, not the non-null: <c>tkb-1/DESIGN.md</c>
        /// §6.3 requires the projection list to be *"identical for all three systems within the same
        /// node"*, and before <c>P2</c> that was true of TWO of them — promotion was built by a different
        /// registrar, and on the factory path *(CGF)* it got no list at all and fell back to the ELM's.
        /// ⇒ asserting the SAME INSTANCE reaches the ELM, the spawn system and promotion is what makes
        /// §6.3 true by construction.</para>
        /// </summary>
        [Fact]
        public void Build_ProducesGhostPromotion_SharingTheNodesOneTranslatorList()
        {
            var creation = EntityCreationPack.Build(MinimalContext(out _));

            Assert.NotNull(creation.PromotionSystem);

            // ⭐ The ELM holds the one list; promotion resolves through it (explicitly passed, and its own
            //   fallback is the same instance) — so the ELM's list IS what promotion projects with.
            Assert.Same(creation.Translators, creation.Elm.Translators);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The remove half, and the reason the design demands ONE commit.</b> The failure mode of a
        /// relocation is landing the add without the remove, which would promote twice per frame.
        ///
        /// <para>⛔⛔ This is asserted STRUCTURALLY rather than by counting registrations:
        /// <c>GhostPromotionSystem</c> carries <c>[SingleInstance]</c>, so a second registration throws in
        /// <c>SystemScheduler</c> *(<c>CE-165</c>, and it recurses into groups)*. ⇒ the rail asserts the
        /// attribute is present — the mechanism — plus that the NED module no longer registers it.</para>
        ///
        /// <para>⚠ The source check is deliberately about <c>RegisterSystem</c>, not about the type name:
        /// that file still MENTIONS the system in a long explanatory comment, and it should.</para>
        /// </summary>
        [Fact]
        public void GhostPromotion_IsRegisteredExactlyOnce_StructurallyAndInTheNedModule()
        {
            var t = typeof(Fdp.Toolkit.Replication.Systems.GhostPromotionSystem);

            Assert.True(
                Attribute.IsDefined(t, typeof(Fdp.ModuleHost.Abstractions.SingleInstanceAttribute)),
                "GhostPromotionSystem must carry [SingleInstance]. P2 moved its registrar from " +
                "NedReplicationModule into EntityCreationPack; without the attribute a host that lands " +
                "the add without the remove promotes every arrived ghost TWICE per frame, and nothing " +
                "fails — which is exactly the one-commit hazard the design names (CE-165).");

            var ned = CompositionRootSource.StripComments(CompositionRootSource.ReadRepoSource(
                "Hrot/Network/Hrot.Network.NED/Replication/NedReplicationModule.cs"));

            Assert.DoesNotContain("new GhostPromotionSystem", ned);

            // ⛔ Anti-vacuity: the file must still register ghost CREATION. If this stopped being true the
            //   rail above would pass over a module that had lost the network half too.
            Assert.Contains("RegisterSystem(GhostCreationSystem)", ned);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The ORDER — and this is the one the relocation genuinely put at risk.</b>
        ///
        /// <para>📐 Measured: with no declared edge, <c>SystemScheduler</c> orders a phase by REGISTRATION
        /// ORDER *(Kahn over nodes added in insertion order)*. ⛔ While one module registered both systems
        /// that was free; across two registrars it depends on which the host wires first, and promotion
        /// running before creation costs a frame of latency SILENTLY. ⇒ the system declares
        /// <c>[UpdateAfter(GhostCreationSystem)]</c> and the ordering is true by construction.</para>
        ///
        /// <para>⚠ Both must also be in the SAME phase, because <c>SystemScheduler</c> adds the edge only
        /// when the target is in the phase being sorted — an edge across phases is silently dropped. ⇒ the
        /// rail asserts the phase equality too, which is the half that would rot invisibly.</para>
        /// </summary>
        [Fact]
        public void GhostPromotion_DeclaresItRunsAfterGhostCreation_InTheSamePhase()
        {
            var promotion = typeof(Fdp.Toolkit.Replication.Systems.GhostPromotionSystem);
            var creation  = typeof(Fdp.Toolkit.Replication.Systems.GhostCreationSystem);

            var after = Attribute.GetCustomAttributes(promotion, typeof(UpdateAfterAttribute), inherit: true)
                                 .Cast<UpdateAfterAttribute>()
                                 .Select(a => a.Target)
                                 .ToList();

            Assert.Contains(creation, after);

            static Fdp.ModuleHost.Abstractions.SystemPhase PhaseOf(Type t) =>
                ((Fdp.ModuleHost.Abstractions.UpdateInPhaseAttribute)Attribute.GetCustomAttribute(
                    t, typeof(Fdp.ModuleHost.Abstractions.UpdateInPhaseAttribute))!).Phase;

            Assert.Equal(PhaseOf(creation), PhaseOf(promotion));
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The fourth failure mode, and the one <c>P2</c> could regress SILENTLY: a host that
        /// adopts the pack and forgets to schedule promotion LOSES a capability it already had.</b>
        ///
        /// <para>🔒 The design states the hazard: *"a host that has not adopted the pack would LOSE ghost
        /// promotion the moment the NED module stops registering it."* ⇒ <c>Unserviceable</c> must name it,
        /// and every production root must pass it.</para>
        /// </summary>
        [Fact]
        public void Unserviceable_NamesGhostPromotion_WhenAHostForgetsToScheduleIt()
        {
            var creation = EntityCreationPack.Build(MinimalContext(out _));

            var missing = creation.Unserviceable(new object[]
                { creation.RequestSystem, creation.SpawnSystem, creation.FinalizationSystem });

            Assert.Contains("PromotionSystem", missing);
            Assert.Contains("EntityLifecycle.Ghost", missing);

            // ⭐ And it is silent once the host schedules all four.
            Assert.Equal(string.Empty, creation.Unserviceable(new object[]
            {
                creation.RequestSystem, creation.SpawnSystem, creation.FinalizationSystem,
                creation.PromotionSystem,
            }));
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Every production root that builds the pack SCHEDULES promotion.</b> This is the rail
        /// that would have caught the regression, and it is the <c>CE-162</c> family shape: a caller that
        /// HAS the dependency must pass it.
        ///
        /// <para>⚠ Source-based on purpose: these are composition roots, so what matters is what the host
        /// WIRES, which is not observable by constructing anything.</para>
        /// </summary>
        public static TheoryData<string> RootsThatBuildThePack() => new()
        {
            "Hrot/Subsystems/Hrot.IG/IgNodeBootstrapper.cs",
            "Hrot/Subsystems/Hrot.NodeComposition/StrideNodeBootstrapper.cs",
            "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs",
            "Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs",
            "Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs",
        };

        [Theory]
        [MemberData(nameof(RootsThatBuildThePack))]
        public void EveryRootThatBuildsThePack_SchedulesGhostPromotion(string rootPath)
        {
            var src = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(rootPath));

            // ⛔ Anti-vacuity: if this root stops building the pack, the row asserts nothing.
            Assert.True(src.Contains("EntityCreationPack.Build"),
                $"{rootPath} no longer builds the pack, so this row cannot assert anything. Either it " +
                "genuinely stopped (drop the row and say why) or the rail is aimed at the wrong file.");

            Assert.True(src.Contains("creation.PromotionSystem"),
                $"{rootPath} builds EntityCreationPack but never schedules creation.PromotionSystem. " +
                "P2 moved ghost promotion's registrar out of NedReplicationModule, so this host no " +
                "longer gets it for free: arrived ghosts will never receive their TKB projection and " +
                "will stay in EntityLifecycle.Ghost forever. That is a capability this host ALREADY HAD " +
                "being lost silently — the exact regression the design's one-commit rule exists to stop.");
        }

        private sealed class CountingTranslator : ITkbEntityTranslator
        {
            public IEnumerable<Type> GetConsumedDescriptors() => Array.Empty<Type>();
            public void Inject(EntityRepository repo, Entity entity, TkbTemplate template) { }
        }

        // ═══ THE AUTHORING AFFORDANCE ═══════════════════════════════════════════════════════════════
        //  📄 docs/DESIGN_Entity_Authoring_Surface.md — acceptance ①, ①b, ④, ⑤.
        //  ⭐ These live HERE, in the pack's own suite, rather than in a new class: the affordance is a
        //    member of what the pack produces, and every one of these asserts through a PRODUCTION-built
        //    pack (acceptance ④'s own wording) rather than a hand-made EntityCreation.

        /// <summary>⭐ Drains whatever the affordance enqueued. ⚠ Asserting on the QUEUE rather than on a
        /// returned object is deliberate: it is the same channel a translator uses, so the rail cannot
        /// pass while the real enqueue path is broken.</summary>
        private static Hrot.Core.Network.EntityCreationRequest DrainOne(EntityCreation creation)
        {
            var drained = new List<Hrot.Core.Network.EntityCreationRequest>();
            creation.LocalRequests.ProcessRequests(drained.Add);
            return Assert.Single(drained);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Acceptance ④ — <c>owner</c> has THREE legal values, and all three are expressible.</b>
        ///
        /// <para>📄 <c>DESIGN_Entity_Authoring_Surface.md</c> §4b. ⛔ This is the rail that pins WHY the
        /// design superseded §3.4's two-method shape (<c>RequestFromDefaultProcessor</c> /
        /// <c>CreateLocallyOwned</c>): two verbs can express rows 1 and 2 and have <b>no way at all</b>
        /// to say row 3 — <i>"owned by some other node"</i> — which the wire ingress already receives.
        /// ⇒ if someone ever collapses this back to a boolean, the third case reddens here.</para>
        ///
        /// <para>⭐ Row 1 also pins acceptance ①b: the default is the NAMED constant, so an omitted
        /// argument reads as a decision rather than as a forgotten zero.</para>
        /// </summary>
        [Fact]
        public void RequestEntityCreation_ExpressesAllThreeOwnerValues_IncludingAThirdNode()
        {
            var ctx      = MinimalContext(out _);                       // NodeId = 7
            var creation = EntityCreationPack.Build(ctx);
            long tkbType = ctx.TkbDb.GetAll().First().TkbType;

            // ① omitted ⇒ "the designated default processor owns it"
            creation.RequestEntityCreation(tkbType);
            Assert.Equal(Hrot.Core.Network.EntityCreationRouting.DefaultEntityCreationRequestProcessor,
                         DrainOne(creation).OwnerAppInstanceId);
            Assert.Equal(0, Hrot.Core.Network.EntityCreationRouting.DefaultEntityCreationRequestProcessor);

            // ② "mine" — and the author gets the id from the pack, not from the host
            creation.RequestEntityCreation(tkbType, owner: creation.NodeId);
            Assert.Equal(7, DrainOne(creation).OwnerAppInstanceId);

            // ③ ⭐ a THIRD node by id — the case the two-method shape could not express at all
            creation.RequestEntityCreation(tkbType, owner: 42);
            Assert.Equal(42, DrainOne(creation).OwnerAppInstanceId);
        }

        /// <summary>
        /// ⭐⭐ <b>Acceptance ⑤ — the returned <c>Guid</c> is the one the request actually carries.</b>
        ///
        /// <para>📐 An author that must be told the outcome correlates the two-phase ACK on this value
        /// (<c>MapCommandController._pendingEntityRequests</c>), so a returned id that did not match the
        /// enqueued one would hang that session forever while looking perfectly healthy.</para>
        /// </summary>
        [Fact]
        public void RequestEntityCreation_ReturnsTheIdTheRequestCarries_SuppliedOrMinted()
        {
            var ctx      = MinimalContext(out _);
            var creation = EntityCreationPack.Build(ctx);
            long tkbType = ctx.TkbDb.GetAll().First().TkbType;

            var mine     = Guid.NewGuid();
            var echoed   = creation.RequestEntityCreation(tkbType, requestId: mine);
            Assert.Equal(mine, echoed);
            Assert.Equal(mine, DrainOne(creation).RequestId);

            var minted = creation.RequestEntityCreation(tkbType);
            Assert.NotEqual(Guid.Empty, minted);
            Assert.Equal(minted, DrainOne(creation).RequestId);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Acceptance ① end to end — every argument survives the PRODUCTION request system and
        /// lands on the order.</b>
        ///
        /// <para>⭐ The one that matters most is <c>transform</c>: the affordance has no transform FIELD to
        /// carry it, it folds it into <c>InitialComponents</c> and <c>CreateEntityRequestSystem</c> pulls
        /// the <see cref="SimTransform"/> back out. ⛔ A rail that only inspected the DTO would pass while
        /// that hand-off was broken.</para>
        ///
        /// <para>⚠ <c>DisType</c> is asserted for a measured reason: nothing downstream derives it from
        /// <c>TkbType</c> — the request system copies the request's value verbatim — so an affordance
        /// without that parameter would silently strip the DIS type off every authored entity the moment
        /// a hand-rolled producer was converted to a thin caller (<c>R-137</c>).</para>
        /// </summary>
        [Fact]
        public void RequestEntityCreation_EveryArgumentReachesTheSpawnOrder_TransformIncluded()
        {
            var ctx      = MinimalContext(out var world);               // NodeId = 7
            var creation = EntityCreationPack.Build(ctx);
            long tkbType = ctx.TkbDb.GetAll().First().TkbType;

            creation.RequestEntityCreation(
                tkbType,
                transform:   new SimTransform { Position = new System.Numerics.Vector3(10f, 20f, 30f) },
                owner:       creation.NodeId,                           // self-targeted ⇒ serviced here
                initType:    Fdp.Toolkit.Replication.ReliableInitType.None,
                isTransient: true,
                disType:     0xABCDUL);

            creation.RequestSystem.Execute(world, 0f);
            world.Bus.SwapBuffers();

            var order = Assert.Single(((Fdp.ModuleHost.Abstractions.ISimulationView)world)
                .ReadManagedEvents<Fdp.Toolkit.NetworkSpawning.Events.SpawnEntityCommand>().ToArray());

            Assert.Equal(tkbType, order.TkbType);
            Assert.Equal(7,       order.OwnerNodeId);
            Assert.Equal(Fdp.Toolkit.Replication.ReliableInitType.None, order.InitType);
            Assert.True(order.IsTransient);
            Assert.Equal(0xABCDUL, order.DisType);

            Assert.True(order.InitialTransform.HasValue,
                "the affordance's transform never reached the order — it is folded into " +
                "InitialComponents and CreateEntityRequestSystem separates it back out; one of those " +
                "two halves is broken.");
            Assert.Equal(new System.Numerics.Vector3(10f, 20f, 30f), order.InitialTransform!.Value.Position);
        }

        /// <summary>
        /// ⭐⭐ <b>The pack SURFACES the node id, so an author never asks the host for a number the pack
        /// already holds.</b>
        ///
        /// <para>⚠ <c>DESIGN_Entity_Authoring_Surface.md</c> §4 asserted this was <i>already</i> on
        /// <c>EntityCreation</c> and §6's class diagram drew it as an existing member. 📐 It was not —
        /// the value was a composition input that stopped at the two systems. The design carries the
        /// correction; this row stops it regressing to a host lookup.</para>
        /// </summary>
        [Fact]
        public void EntityCreation_SurfacesTheNodeId()
        {
            var ctx = MinimalContext(out _);
            Assert.Equal(ctx.NodeId, EntityCreationPack.Build(ctx).NodeId);
        }
    }
}
