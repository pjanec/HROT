using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b><c>94g</c> — THE ACCEPTANCE CRITERION: a concrete watch pin survives a scenario reload.</b>
/// 📄 <c>DESIGN_Variable_Watch_Pinning.md</c> §5 *(restart survival BY TRANSLATION)</b> · §8 ① · §8a.
///
/// <para>⛔⛔ <b>What used to happen.</b> <c>StagingEntityExtractor</c> Pass 1 allocates a FRESH runtime
/// network id for every authored entity on every load, and the pin stored that runtime id ⇒ after a
/// reload it pointed at nothing, or — worse — at whichever entity now held that number.</para>
///
/// <para>⭐⭐ <b>The scenario every rail here re-creates:</b> authored entity <c>S=100</c> is loaded once as
/// runtime <c>7000</c>, pinned, then the scenario is reloaded and the SAME authored entity comes back as
/// runtime <c>9000</c> on a different <c>Entity</c> handle.</para>
/// </summary>
public sealed class AConcretePinSurvivesAReloadTests
{
    private const long AuthoredId = 100;
    private static readonly Guid AssetA = Guid.Parse("aaaaaaaa-3333-3333-3333-333333333333");

    private static Entity Ent(int index) => new(index, 1);

    private static VariableRow Row(Entity entity, string name = "Health")
        => new(Origin:    new VariableRowOrigin(AssetA, entity, "s", name, "Alpha"),
               ShortName: name,
               TypeText:  "int",
               ClrType:   typeof(int),
               ReadValue: () => BitConverter.GetBytes(1));

    private sealed class NoTimeControl : Hrot.Blueprints.Core.Debug.IEngineDebugTimeController
    {
        public bool IsPausedByDebugger => false;
        public void RequestPause() { }
        public void RequestResume() { }
        public void RequestStepOneTick() { }
    }

    /// <summary>⭐ A fake WORLD: runtime network id ⇄ Entity, the two halves the host supplies.</summary>
    private sealed class FakeWorld
    {
        private readonly Dictionary<long, Entity> _byId = new();
        private readonly Dictionary<Entity, long> _byEntity = new();

        public void Spawn(long runtimeId, Entity e) { _byId[runtimeId] = e; _byEntity[e] = runtimeId; }
        public void Clear() { _byId.Clear(); _byEntity.Clear(); }

        public Entity EntityByRuntimeId(long id) => _byId.TryGetValue(id, out var e) ? e : default;
        public long   RuntimeIdOf(Entity e)      => _byEntity.TryGetValue(e, out var id) ? id : 0;
    }

    private static (AiWatchWindow Watch, StagingRemapView Remap, FakeWorld World) Production()
    {
        var remap = new StagingRemapView();
        var world = new FakeWorld();

        var services = new PerspectiveWorkspaceServices(
            new AssetCatalog(),
            new Windows.TheDefaultLayoutIsNotStaleTests.NoRefactor(),
            new DebugSessionRegistry(),
            new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
            isSimUp: () => false, isFrozen: () => false)
        {
            BreakpointManager = new Hrot.Diagnostics.Breakpoints.DataBreakpointManager(
                new EntityRepository(), new EntityRepository(),
                new Hrot.Diagnostics.Breakpoints.DebugSnapshotProvider(new EntityRepository()),
                new NoTimeControl()),
            EntityIdentity = new WatchEntityIdentity(remap, world.EntityByRuntimeId, world.RuntimeIdOf),
        };

        var reg = services.CreateRegistrar("BTree", new EditorSelectionStore(),
                                           Array.Empty<IAssetValidator>());
        return (reg.Watch!, remap, world);
    }

    // ══ the FORWARDING (R-67) ═══════════════════════════════════════════════════

    /// <summary>⭐⭐ A composition root that HAS the identity bridge gets a Watch that HAS it — asserted on
    /// the CONSTRUCTED window, ⛔ never on the registrar's source line.</summary>
    [Fact]
    public void TheRegistrarHandsTheIdentityBridgeToTheWatchItBuilds()
    {
        Assert.True(Production().Watch.HasEntityIdentity);
    }

    // ══ THE HEADLINE ════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL <c>94g</c> EXISTS FOR:</b> pin a variable on a concrete entity, reload the
    /// scenario, and the pin re-binds to the NEW runtime entity for the SAME authored one.
    ///
    /// <para>⚠ <b>The new handle is deliberately a different <c>Entity</c> AND a different runtime id</b>
    /// — 📌 both change across a reload, and a rail that varied only one would pass on a fix that
    /// happened to track the other.</para>
    /// </summary>
    [Fact]
    public void APinnedConcreteVariableRebindsToTheNewEntityAfterAReload()
    {
        var (watch, remap, world) = Production();

        // ── load 1: authored 100 arrives as runtime 7000 on handle #1 ──
        remap.Publish(new Dictionary<long, long> { [AuthoredId] = 7000 });
        world.Spawn(7000, Ent(1));

        // ⭐ The designer pins through the ORDINARY gesture path: BindingFor fills in the AUTHORED id.
        var binding = watch.BindingFor(Ent(1));
        Assert.Equal(EntityBindingKind.Concrete, binding.Kind);
        Assert.Equal(AuthoredId, binding.StagingNetworkId);      // ⛔ NOT 7000
        Assert.True(binding.IsPersistable);
        watch.Pinned.Pin(Row(Ent(1)), binding);

        // ── the reload: same authored entity, NEW runtime id, NEW handle ──
        world.Clear();
        remap.Publish(new Dictionary<long, long> { [AuthoredId] = 9000 });
        world.Spawn(9000, Ent(42));

        Assert.Equal(1, watch.RebindConcretePins());

        var (row, rebound) = Assert.Single(watch.Pinned.PinnedWithBindings());
        Assert.Equal(AuthoredId, rebound.StagingNetworkId);      // ⭐ the durable key never moved
        Assert.Equal(Ent(42),    rebound.Captured);              // ⭐ the in-session handle did
        Assert.Equal(Ent(42),    row.Origin.Entity);             // ⭐ …and the row followed it
        Assert.False(row.IsStale);
    }

    /// <summary>
    /// ⚠⚠ <b>An authored entity the new scenario does NOT contain leaves its pin STALE</b> — ⛔ not
    /// dropped *(which reads as data loss)* and ⛔ not left on the dead handle *(which would show the
    /// value of whatever entity now occupies that slot — the wrong-entity failure this mechanism removes)*.
    /// </summary>
    [Fact]
    public void APinWhoseAuthoredEntityIsGoneGoesStaleRatherThanWrong()
    {
        var (watch, remap, world) = Production();

        remap.Publish(new Dictionary<long, long> { [AuthoredId] = 7000 });
        world.Spawn(7000, Ent(1));
        watch.Pinned.Pin(Row(Ent(1)), watch.BindingFor(Ent(1)));

        // ── a DIFFERENT scenario: authored 100 is not in it, and something else holds 7000 now ──
        world.Clear();
        remap.Publish(new Dictionary<long, long> { [777] = 7000 });
        world.Spawn(7000, Ent(9));

        Assert.Equal(0, watch.RebindConcretePins());

        var (row, binding) = Assert.Single(watch.Pinned.PinnedWithBindings());
        Assert.True(row.IsStale);
        Assert.NotEqual(Ent(9), binding.Captured);   // ⛔ never re-pointed at the id's new owner
    }

    /// <summary>⛔ A CHAMELEON is untouched by a reload — it carries no id and follows the selection.</summary>
    [Fact]
    public void AChameleonPinIsNotTouchedByARebind()
    {
        var (watch, remap, _) = Production();

        watch.Pinned.Pin(Row(default), EntityBinding.Chameleon);
        remap.Publish(new Dictionary<long, long> { [AuthoredId] = 9000 });

        Assert.Equal(0, watch.RebindConcretePins());
        var (row, binding) = Assert.Single(watch.Pinned.PinnedWithBindings());
        Assert.Equal(EntityBindingKind.Chameleon, binding.Kind);
        Assert.False(row.IsStale);
    }

    /// <summary>
    /// ⚠ A pin on a RUNTIME-SPAWNED entity has no authored ancestor ⇒ <c>StagingNetworkId == 0</c> ⇒
    /// ⭐ within-session, ⛔ and a rebind neither re-binds nor stales it: it was never durable, and marking
    /// it stale would claim something was lost that never persisted.
    /// </summary>
    [Fact]
    public void APinOnARuntimeSpawnedEntityIsWithinSessionAndSurvivesTheRebindUntouched()
    {
        var (watch, remap, world) = Production();

        remap.Publish(new Dictionary<long, long> { [AuthoredId] = 7000 });
        world.Spawn(5555, Ent(3));                 // ⚠ a live entity with NO authored ancestor

        var binding = watch.BindingFor(Ent(3));
        Assert.Equal(0, binding.StagingNetworkId);
        Assert.False(binding.IsPersistable);
        watch.Pinned.Pin(Row(Ent(3)), binding);

        Assert.Equal(0, watch.RebindConcretePins());
        var (row, after) = Assert.Single(watch.Pinned.PinnedWithBindings());
        Assert.False(row.IsStale);
        Assert.Equal(Ent(3), after.Captured);
    }

    // ══ the TRANSLATION TABLE itself ════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>Both directions, and the refusal that matters:</b> an unknown id answers <c>0</c>, ⛔ it does
    /// NOT pass the input through. 📌 Staging and runtime ids come from one numeric space, so a
    /// pass-through would look like a successful translation and resolve to the wrong entity.
    /// </summary>
    [Fact]
    public void TheRemapTranslatesBothWaysAndRefusesToPassUnknownIdsThrough()
    {
        var remap = new StagingRemapView();
        Assert.False(remap.HasMap);
        Assert.Equal(0, remap.ToRuntime(100));
        Assert.Equal(0, remap.ToStaging(100));

        remap.Publish(new Dictionary<long, long> { [100] = 7000, [101] = 7001 });

        Assert.True(remap.HasMap);
        Assert.Equal(7000, remap.ToRuntime(100));
        Assert.Equal(100,  remap.ToStaging(7000));
        Assert.Equal(0,    remap.ToRuntime(999));
        Assert.Equal(0,    remap.ToStaging(999));
    }

    /// <summary>
    /// ⚠⚠ <b>A new load REPLACES the table, it does not merge into it.</b> ⛔ Merging would leave a
    /// previous world's staging→runtime pair alive and resolvable — the exact stale-mapping failure this
    /// mechanism removes. ⭐ <c>Generation</c> is what a host watches to know a reload happened.
    /// </summary>
    [Fact]
    public void EachLoadReplacesThePreviousTableAndBumpsTheGeneration()
    {
        var remap = new StagingRemapView();

        remap.Publish(new Dictionary<long, long> { [100] = 7000 });
        Assert.Equal(1, remap.Generation);

        remap.Publish(new Dictionary<long, long> { [101] = 8000 });
        Assert.Equal(2, remap.Generation);
        Assert.Equal(0, remap.ToRuntime(100));       // ⛔ the previous load's pair is GONE
        Assert.Equal(0, remap.ToStaging(7000));
        Assert.Equal(8000, remap.ToRuntime(101));
    }

    /// <summary>⚠ An EMPTY table is a legitimate answer *(a scenario with no networked entities)*, and a
    /// <c>null</c> payload must not throw — ⭐ both leave the view with no translations.</summary>
    [Fact]
    public void AnEmptyOrAbsentTableIsAnAnswerNotAFault()
    {
        var remap = new StagingRemapView();

        remap.Publish(new Dictionary<long, long>());
        Assert.False(remap.HasMap);

        remap.Publish(null);
        Assert.False(remap.HasMap);
        Assert.Equal(2, remap.Generation);
    }

    // ══ ① the TRANSPORT — the map survives the control-plane bus ════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Item ① end to end at the transport level:</b> the extractor's table is published on the
    /// control-plane bus as a MANAGED event and arrives at a reader <b>whole</b>.
    ///
    /// <para>📐 The design's measured claim, re-asserted here rather than trusted:
    /// <c>PublishManaged&lt;T&gt;</c> carries no <c>unmanaged</c> constraint, so a
    /// <c>Dictionary&lt;long,long&gt;</c> travels as-is — ⛔ no flattening to parallel arrays.</para>
    ///
    /// <para>⚠ <c>SwapBuffers</c> before reading: the bus is double-buffered, and this is the frame
    /// boundary the editor's own drain sits on.</para>
    /// </summary>
    [Fact]
    public void TheRemapTableTravelsTheControlPlaneBusWhole()
    {
        var bus = new FdpEventBus();
        OrchestrationEventRegistry.RegisterAll(bus);

        bus.PublishManaged(new StagingRemapPublishedEvent
        {
            StagingToRuntime = new Dictionary<long, long> { [100] = 7000, [101] = 7001 },
            SourceNodeId     = 7,
        });
        bus.SwapBuffers();

        var received = Assert.Single(bus.ReadManaged<StagingRemapPublishedEvent>());
        Assert.Equal(7, received.SourceNodeId);
        Assert.Equal(2, received.StagingToRuntime.Count);

        // ⭐ …and it feeds the view the Watch actually reads, which is the point of publishing it.
        var remap = new StagingRemapView();
        remap.Publish(received.StagingToRuntime);
        Assert.Equal(7001, remap.ToRuntime(101));
        Assert.Equal(101,  remap.ToStaging(7001));
    }
}
