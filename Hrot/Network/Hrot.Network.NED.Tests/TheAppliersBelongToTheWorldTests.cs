using System.Reflection;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Attributes;
using Xunit;

namespace Hrot.Network.NED.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>AX-016</c> — the attribute appliers are ONE PER WORLD and NOT bound to any network.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §15. 🔒 User ruling: *"is instantiating the same
/// interpreter in every network factory the right solution? the interpreter should not be bound to any
/// network."*</para>
///
/// <para>⚠⚠ <b>SUPERSEDES <c>TheBinaryArmIsWiredInProductionTests</c>, and why is worth keeping.</b> Those
/// rails asserted that the request system's <b>constructor</b> produced a non-null interpreter — the right
/// control for the defect of that day *(<c>AX-012</c>: a production caller omitting an argument)*. ⛔ But they
/// pinned the wrong thing: they made a per-network-stack instance the asserted contract. ⭐ The appliers now
/// belong to the WORLD, so these rails assert that — and <c>AX-012</c>'s silent-null arm becomes
/// <b>unrepresentable</b> rather than merely detected.</para>
/// </summary>
public class TheAppliersBelongToTheWorldTests
{
    /// <summary>⚠ Domains kept low and distinct — CycloneDDS ports are `7400 + 250 × domainId`, ceiling ≈ 232.</summary>
    private const int TestDomain = 171;

    private static EntityRepository WorldWithGeo()
    {
        var repo = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);

        var geo = UtmTransform.AtOrigin(52.52, 13.405, 0.0);
        repo.SetSingletonManaged<IGeographicTransform>(geo);
        return repo;
    }

    // ══ the world owns them ═══════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Repeated resolution yields the SAME instance.</b>
    ///
    /// <para>🔴 <c>EntityWriteRouter.For(repo)</c> is called at EVERY gizmo construction. Before
    /// <c>AX-016</c> that built a fresh interpreter each time — one per gizmo, each with its own scratchpad,
    /// and N chances for two to be built from different geographic transforms and convert the same attribute
    /// differently.</para>
    /// </summary>
    [Fact]
    public void TheWorldHasExactlyOneInterpreterHoweverOftenItIsAskedFor()
    {
        using var repo = WorldWithGeo();

        var a = AttributeInterpreterProvider.GetOrCreate(repo);
        var b = AttributeInterpreterProvider.GetOrCreate(repo);

        Assert.Same(a, b);
        Assert.Same(a, AttributeInterpreterProvider.GetOrCreate(repo));
    }

    /// <summary>⭐⭐ …and the JSON arm follows the same rule, so the two cannot drift apart *(<c>AX-014</c>)*.</summary>
    [Fact]
    public void TheJsonCompilerIsWorldScopedToo()
    {
        using var repo = WorldWithGeo();

        Assert.Same(AttributeInterpreterProvider.GetOrCreateJson(repo),
                    AttributeInterpreterProvider.GetOrCreateJson(repo));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>NOT BOUND TO ANY NETWORK: no participant, no factory, no translators — both appliers resolve.</b>
    ///
    /// <para>🔴 This is the property that was asked for, and the one the old design could not have.
    /// 📐 Measured <c>2026-08-26</c>: <c>OfflineNetworkFactory.CreateSimHostAttributeUpdateSystems()</c>
    /// returns an EMPTY array ⇒ before <c>AX-016</c> an offline host had no attribute applier at all.
    /// ⛔ Nothing in this rail touches CycloneDDS.</para>
    /// </summary>
    [Fact]
    public void AWorldWithNoNetworkAtAllStillHasBothAppliers()
    {
        using var repo = WorldWithGeo();

        Assert.NotNull(AttributeInterpreterProvider.GetOrCreate(repo));
        Assert.NotNull(AttributeInterpreterProvider.GetOrCreateJson(repo));
    }

    /// <summary>
    /// ⚠ <b>A world with NO geodetic frame still gets appliers</b> — they simply carry no <c>Geo*</c>
    /// handlers *(<c>AX-010</c>)*. ⛔ It must not throw: <c>GetSingletonManaged</c> does when unset, which is
    /// the trap that reddened the <c>AX-005</c> cluster rail on the IG.
    /// </summary>
    [Fact]
    public void AWorldWithNoGeographicTransformStillGetsAppliers()
    {
        using var repo = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);

        Assert.NotNull(AttributeInterpreterProvider.GetOrCreate(repo));
        Assert.NotNull(AttributeInterpreterProvider.GetOrCreateJson(repo));
    }

    /// <summary>
    /// ⭐⭐ N-6 (CE-4468, U-3) — <b>a THREADED node's presentation world writes on the sim thread.</b> The router handed out for a world that
    /// carries a <c>SimLink</c> posts the write as a sim job; it answers <c>Requested</c>, the simulation world is untouched until the sim
    /// thread drains its queue, and then the write lands on S through S's OWN router — the gizmo commit path the threaded editor's drag uses.
    /// 🔴 Red-proof (run): <c>For</c> without the SimLink branch ⇒ the write lands on A at once (Direct) and S never changes.
    /// </summary>
    [Fact]
    public void AThreadedPresentationWorld_WritesThroughTheSimJobQueue_OntoTheSimulationWorld()
    {
        using var sim = WorldWithGeo();
        using var ui = new EntityRepository();
        var jobs = new Fdp.Core.Threading.SimJobQueue();
        ui.SetSingletonManaged(new Fdp.Core.Threading.SimLink(jobs, sim));
        var e = sim.CreateEntity();
        sim.AddComponent(e, new SimTransform { Position = new System.Numerics.Vector3(1f, 2f, 3f), Rotation = System.Numerics.Quaternion.Identity });
        sim.SetAuthority<SimTransform>(e, true);   // the simulation world OWNS it (the editor's node is the master)

        var route = EntityWriteRouter.For(ui).Write(e, new[]
        {
            EntityAttributeChange.Double(AttributeIds.PositionX, 50.0),
            EntityAttributeChange.Double(AttributeIds.PositionY, 60.0),
        });

        Assert.Equal(EntityWriteRoute.Requested, route);
        Assert.Equal(1f, sim.GetComponentRO<SimTransform>(e).Position.X);   // nothing written before the sim thread drains
        Assert.Equal(1, jobs.Pending);
        jobs.Drain(sim);
        var p = sim.GetComponentRO<SimTransform>(e).Position;
        Assert.Equal(50f, p.X);
        Assert.Equal(60f, p.Y);
        Assert.Equal(3f, p.Z);                                               // Z not sent ⇒ kept
    }

    // ══ ⭐⭐ CE-11060 — the ONE edit entry (Edits.Submit) on a threaded node: the Details form, the Unit form's Strength, every other Submit caller ══

    /// <summary>Two worlds as the threaded node composes them: S (the simulation, owns the man) and A (the UI's copy, a SimLink to S), the man at
    /// the same handle in both, dead (Health 0 / 100). The job queue's replies run on A when <see cref="UiOutbox.RunAll"/> plays the next pull.</summary>
    private static (EntityRepository Sim, EntityRepository Ui, Entity Man, Fdp.Core.Threading.SimJobQueue Jobs, UiOutbox Outbox) DeadManInTwoWorlds()
    {
        var sim = WorldWithGeo(); var ui = WorldWithGeo();
        Entity man = default;
        foreach (var w in new[] { sim, ui })
        {
            man = w.CreateEntity();
            w.AddComponent(man, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });
            w.SetAuthority<Fdp.Toolkit.Combat.Components.Health>(man, true);   // this node owns him (the editor's one node)
        }
        var jobs = new Fdp.Core.Threading.SimJobQueue();
        var outbox = new UiOutbox(ui);
        jobs.Outbox = outbox;
        ui.SetSingletonManaged(new Fdp.Core.Threading.SimLink(jobs, sim));
        return (sim, ui, man, jobs, outbox);
    }

    private sealed class UiOutbox : Fdp.Core.Threading.IPresentationOutbox
    {
        private readonly EntityRepository _ui;
        private readonly System.Collections.Generic.List<System.Action<EntityRepository>> _queued = new();
        public UiOutbox(EntityRepository ui) => _ui = ui;
        public void Post(System.Action<EntityRepository> onPresentation) => _queued.Add(onPresentation);
        public void RunAll() { foreach (var a in _queued) a(_ui); _queued.Clear(); }
    }

    /// <summary>
    /// ⭐⭐ CE-11060 — 📐 found by the user (`2026-11-25`): <i>"i was able to edit the fields, every change was reverted (if not immediately then when unpaused)"</i>
    /// — setting a dead soldier's Health in the Details panel. The form's write (<c>FormView.Commit</c> → <c>Edits.Submit</c> → <c>EditRequests.Write</c>) built its
    /// writer on the world it was given — A, the UI's copy — so it landed there, reported <i>applied</i>, and the next staging pull of a changed chunk put S's 0
    /// back. Now: the ticket answers Requested at once, nothing touches A, the sim thread writes S under the SAME request id, and the outcome reaches A's log
    /// (the form's pending mark turns into applied) and A's journal entry.
    /// 🔴 Red-proof (run): without the threaded branch in <c>Edits.Submit</c> ⇒ the route is Direct, A holds 50 and S keeps 0.
    /// </summary>
    [Fact]
    public void CE11060_TheFormsHealthWrite_OnAThreadedNode_LandsOnTheSimulationWorld_AndItsOutcomeReachesTheUi()
    {
        var (sim, ui, man, jobs, outbox) = DeadManInTwoWorlds();
        using (sim) using (ui)
        {
            var ticket = Fdp.Toolkit.Replication.Edits.Edits.Submit(ui,
                Fdp.Toolkit.Replication.Edits.EditOp.Write(man, EntityAttributeChange.Double(AttributeIds.HealthCurrent, 50), "Set strength"));

            Assert.Equal(EntityWriteRoute.Requested, ticket.Route);
            Assert.Equal(0f, ui.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(man).Current);    // ⛔ never written on the UI's copy
            Assert.Equal(Fdp.Toolkit.Replication.Edits.EditStatus.Pending, Fdp.Toolkit.Replication.Edits.EditJournal.Of(ui).Find(ticket.RequestId)!.Status);

            jobs.Drain(sim);   // the sim thread, between two ticks — paused or not
            Assert.Equal(0, jobs.Faults);
            Assert.Equal(50f, sim.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(man).Current);
            Assert.False(Fdp.Toolkit.Replication.Edits.EditOutcomeLog.Of(ui).TryGet(ticket.RequestId, out _));   // not before the pull

            outbox.RunAll();   // the next pull
            Assert.True(Fdp.Toolkit.Replication.Edits.EditOutcomeLog.Of(ui).TryGet(ticket.RequestId, out var o));
            Assert.Equal(Fdp.Toolkit.Replication.Edits.EditStatus.Applied, o.Status);
            Fdp.Toolkit.Replication.Edits.EditJournal.Of(ui).Refresh(ui);
            Assert.Equal(Fdp.Toolkit.Replication.Edits.EditStatus.Applied, Fdp.Toolkit.Replication.Edits.EditJournal.Of(ui).Find(ticket.RequestId)!.Status);
        }
    }

    /// <summary>
    /// ⭐ CE-11061 — REVIVE clicked on a threaded node's Unit form: routed on the sim thread like any world-writing edit (CE-11060), the man is revived on S —
    /// never on the UI's copy — and the outcome reaches the UI's log.
    /// </summary>
    [Fact]
    public void CE11061_ReviveOnAThreadedNode_RevivesOnTheSimulationWorld()
    {
        var (sim, ui, man, jobs, outbox) = DeadManInTwoWorlds();
        using (sim) using (ui)
        {
            var ticket = Fdp.Toolkit.Replication.Edits.Edits.Submit(ui, Fdp.Toolkit.Replication.Edits.EditOp.Revive(man));
            Assert.Equal(EntityWriteRoute.Requested, ticket.Route);
            jobs.Drain(sim);
            outbox.RunAll();
            Assert.Equal(0, jobs.Faults);
            Assert.Equal(100f, sim.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(man).Current);
            Assert.Equal(0f, ui.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(man).Current);   // the copy follows at the next pull, never written here
            Assert.True(Fdp.Toolkit.Replication.Edits.EditOutcomeLog.Of(ui).TryGet(ticket.RequestId, out var o));
            Assert.Equal(Fdp.Toolkit.Replication.Edits.EditStatus.Applied, o.Status);
        }
    }

    /// <summary>
    /// ⭐ CE-11060 — a write the simulation world REFUSES (the man is not owned here and nobody can be asked) comes back to the UI as refused, under the
    /// ticket's id — the form turns its pending mark into ✗ with the reason (<c>FormView.StateOf</c> reads the log).
    /// </summary>
    [Fact]
    public void CE11060_AThreadedWriteTheSimulationRefuses_IsReportedOnTheUiUnderTheSameTicket()
    {
        var (sim, ui, man, jobs, outbox) = DeadManInTwoWorlds();
        using (sim) using (ui)
        {
            sim.SetAuthority<Fdp.Toolkit.Combat.Components.Health>(man, false);   // owned elsewhere, and this world has no network to ask through
            var ticket = Fdp.Toolkit.Replication.Edits.Edits.Submit(ui,
                Fdp.Toolkit.Replication.Edits.EditOp.Write(man, EntityAttributeChange.Double(AttributeIds.HealthCurrent, 50), "Set strength"));
            jobs.Drain(sim);
            outbox.RunAll();

            Assert.Equal(0f, sim.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(man).Current);
            Assert.True(Fdp.Toolkit.Replication.Edits.EditOutcomeLog.Of(ui).TryGet(ticket.RequestId, out var o));
            Assert.Equal(Fdp.Toolkit.Replication.Edits.EditStatus.Refused, o.Status);
        }
    }

    /// <summary>
    /// ⭐ CE-11060 — a LATER answer (an owner on another node, a judge) is reported on the simulation world and the staging carries the event onto the UI
    /// world's bus; <c>EditOutcomes.MirrorFromBus</c> (once per pull) takes it into the UI's log — once, however often it is carried.
    /// </summary>
    [Fact]
    public void CE11060_ALaterOutcomeCarriedOntoTheUiBus_IsTakenIntoTheUiLog_Once()
    {
        using var ui = WorldWithGeo();
        var id = System.Guid.NewGuid();
        var answer = Fdp.Toolkit.Replication.Edits.EditOutcome.Create(id, 7, Fdp.Toolkit.Replication.Edits.EditStatus.Applied, "applied on node 2", 2, 0d, fromNetwork: true);
        ui.Bus.RegisterManaged<Fdp.Toolkit.Replication.Edits.EditOutcome>();
        ui.Bus.PublishManaged(answer);
        ui.Bus.SwapBuffers();   // the staging pull makes the carried events readable

        Fdp.Toolkit.Replication.Edits.EditOutcomes.MirrorFromBus(ui);
        Fdp.Toolkit.Replication.Edits.EditOutcomes.MirrorFromBus(ui);

        Assert.True(Fdp.Toolkit.Replication.Edits.EditOutcomeLog.Of(ui).TryGet(id, out var o));
        Assert.Equal(Fdp.Toolkit.Replication.Edits.EditStatus.Applied, o.Status);
        Assert.Equal(1, Fdp.Toolkit.Replication.Edits.EditOutcomeLog.Of(ui).Count);
    }

    /// <summary>
    /// ⭐ CE-11060 — the kinds that only SEND a host's event (create, interact, order, commit) stay on the UI thread: their delegate publishes on A's bus, which
    /// the staging uplink carries to S. ⛔ Running it on the sim thread would publish on A's bus from the wrong thread.
    /// </summary>
    [Fact]
    public void CE11060_AnEventSendingEdit_StaysOnTheUiThread()
    {
        var (sim, ui, _, jobs, _) = DeadManInTwoWorlds();
        using (sim) using (ui)
        {
            System.Guid sent = default;
            var ticket = Fdp.Toolkit.Replication.Edits.Edits.Submit(ui, Fdp.Toolkit.Replication.Edits.EditOp.Create(id => sent = id, "Place"));
            Assert.Equal(ticket.RequestId, sent);   // sent at once, here — before any job ran
            Assert.Equal(2, jobs.Pending);           // only its undo bracket (EditSteps forwards begin / end to S, CE-4342) — no route job
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The write router SHARES the world's instance — it does not build its own.</b>
    /// ⭐ Asserted on the CONSTRUCTED OBJECT, which is the control that caught <c>AX-012</c>.
    /// </summary>
    [Fact]
    public void TheWriteRouterSharesTheWorldsInterpreter()
    {
        using var repo = WorldWithGeo();

        var expected = AttributeInterpreterProvider.GetOrCreate(repo);
        Assert.Same(expected, Held(EntityWriteRouter.For(repo)));
    }

    /// <summary>
    /// ⭐⭐ <b>Two routers built independently share ONE interpreter</b> — the per-gizmo duplication railed as
    /// gone rather than described as gone.
    /// </summary>
    [Fact]
    public void TwoRoutersOnTheSameWorldShareOneInterpreter()
    {
        using var repo = WorldWithGeo();

        Assert.Same(Held(EntityWriteRouter.For(repo)), Held(EntityWriteRouter.For(repo)));
    }

    // ══ the DDS system resolves, rather than being handed one ══════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The request system resolves BOTH arms from the world on its first <c>Execute</c>.</b>
    ///
    /// <para>⭐ This replaces *"the constructor produced one"*. ⛔ The interpreter is no longer the network
    /// stack's to provide, so asserting it at construction would pin the very coupling <c>AX-016</c> removes.
    /// ⚠ The property that matters is unchanged: once the system has run, neither arm is null — so
    /// <c>AX-012</c>'s silent drop cannot recur.</para>
    /// </summary>
    [Fact]
    public void TheRequestSystemResolvesBothArmsFromTheWorldOnFirstExecute()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var repo        = WorldWithGeo();

        var system = new Hrot.Map.Common.Systems.UpdateEntityAttributeRequestSystem(
            participant, new NetworkEntityMap());

        system.Execute(repo, 0.016f);

        Assert.Same(AttributeInterpreterProvider.GetOrCreate(repo),     Field(system, "_binaryInterpreter"));
        Assert.Same(AttributeInterpreterProvider.GetOrCreateJson(repo), Field(system, "_jsonCompiler"));
    }

    /// <summary>
    /// ⭐⭐ <b>…and an explicit override still wins</b> — ⛔ the world default must not have removed the seam.
    /// ⚠ Not decoration: <c>SimHostAppTests</c> passes its own JSON compiler.
    /// </summary>
    [Fact]
    public void AnExplicitOverrideStillWins()
    {
        using var participant = new DdsParticipant(TestDomain + 1);
        using var repo        = WorldWithGeo();

        var ownJson   = Fdp.Toolkit.Replication.Attributes.AttributeCompilerFactory.Build();
        var ownBinary = Fdp.Toolkit.Replication.Attributes.AttributeCompilerFactory.BuildBinaryInterpreter();

        var system = new Hrot.Map.Common.Systems.UpdateEntityAttributeRequestSystem(
            participant, new NetworkEntityMap(), ownJson, default, ownBinary);

        system.Execute(repo, 0.016f);

        Assert.Same(ownJson,   Field(system, "_jsonCompiler"));
        Assert.Same(ownBinary, Field(system, "_binaryInterpreter"));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static object? Held(IEntityComponentWriter writer) => Field(writer, "_interpreter");

    private static object? Field(object target, string name)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                 ?.GetValue(target);
}
