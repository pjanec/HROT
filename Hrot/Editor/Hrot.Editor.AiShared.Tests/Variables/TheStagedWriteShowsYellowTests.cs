using System;
using System.Linq;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.Blueprints.Core.Debug;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b><c>W4</c>'s acceptance rail — THE SHARED YELLOW.</b>
/// 📄 <c>DESIGN_Staged_Live_Write.md</c> §4 fork A · §7 · the handoff's stated rail:
/// <i>"an edit stages, both a Details-shaped and a Watch-shaped row report <c>Pending</c> + the same
/// staged bytes; after a simulated drain, both clear."</i>
///
/// <para>🔒 <b>User, <c>2026-08-21</c>:</b> <i>"if we can share the staged state to both views, even
/// better, both yellow, both showing the same staged value, immediately after user edit."</i></para>
///
/// <para>⭐⭐⭐ <b>The REAL <c>DataBreakpointManager</c>, through the REAL <c>IStagedWrites</c>.</b>
/// ⛔ Not <c>FakeStagedWrites</c> — 📌 <c>BP-402</c> ①: a probe that reddened zero rails is what a rail
/// built entirely from doubles buys you. ⚠ The claim here is that <b>the production stager and the
/// production query agree about an address</b>, and only the real pair can say so: the typeId comes from
/// <c>ComponentTypeRegistry</c> on both sides, and a mismatch would be invisible to any double.</para>
///
/// <para>⚠ <b>What this rail deliberately does NOT drive:</b> the Blueprint address RESOLVER
/// *(<c>BlueprintLiveValueWriter.ResolveStagedField</c> → <c>ResolveWorkingStateField</c>)</c>, which
/// needs a compiled blueprint and a debug session. ⭐ That seam is exercised where it lives
/// (<c>TheBlueprintLiveWriteLandsTests</c>); here the resolver is the identity map onto the component
/// this test actually stages into, so the claim under test stays <i>shared-ness</i>, not resolution.</para>
/// </summary>
public sealed class TheStagedWriteShowsYellowTests
{
    /// <summary>⭐ Two <c>int</c> fields: one the designer edits, one that must NOT inherit its yellow.
    /// ⚠ <c>[ComponentId]</c> is mandatory — <c>ComponentTypeRegistry</c> refuses an unattributed type,
    /// deliberately. <c>267</c> is free *(measured across the repo; 261–266 are taken)*.</summary>
    [ComponentId(267)]
    [StructLayout(LayoutKind.Sequential)]
    internal struct WatchedComp
    {
        public int Health;
        public int Ammo;
    }

    private const string Path   = "Health";
    private static readonly Guid Asset = new("11111111-2222-3333-4444-555555555555");

    /// <summary>⭐ The manager needs a live repo, a pre-tick snapshot and a time controller; nothing in
    /// this rail depends on a breakpoint actually being hit.</summary>
    private sealed class NoTimeControl : IEngineDebugTimeController
    {
        public bool IsPausedByDebugger => true;
        public void RequestPause() { }
        public void RequestResume() { }
        public void RequestStepOneTick() { }
    }

    private static (DataBreakpointManager Manager, EntityRepository Live, Entity Entity) Live()
    {
        ComponentTypeRegistry.Clear();
        var live    = new EntityRepository();
        var preTick = new EntityRepository();
        live.RegisterComponent<WatchedComp>();
        preTick.RegisterComponent<WatchedComp>();

        var entity = live.CreateEntity();
        live.AddComponent(entity, new WatchedComp { Health = 10, Ammo = 5 });
        preTick.SyncFrom(live);

        var manager = new DataBreakpointManager(
            live, preTick, new DebugSnapshotProvider(preTick), new NoTimeControl());
        return (manager, live, entity);
    }

    /// <summary>
    /// ⭐ The view over the REAL manager. The resolver answers with the very address
    /// <see cref="DataBreakpointManager.StageFieldMutation"/> was called at — ⛔ the ONE thing this rail
    /// abstracts, and it is stated in the class remarks.
    /// </summary>
    private static StagedWriteView ViewOver(DataBreakpointManager manager, Entity entity)
        => new(() => manager,
               (_, _) => new StagedFieldAddress(
                   ComponentTypeRegistry.GetId(typeof(WatchedComp)),
                   ByteOffset: 0,                       // Health is the first field
                   SizeBytes:  sizeof(int)),
               () => entity);

    /// <summary>
    /// ⭐⭐ A row shaped the way a panel builds one. <paramref name="chameleon"/> gives it the DETAILS
    /// shape — 📌 <c>R-78</c>: a Details row's origin carries <c>entity: default</c>, <i>"whoever is
    /// selected"</i> — while a Watch row carries a concrete entity. ⚠ The two shapes are exactly what
    /// §7 requires to agree, so the rail must build both, not one twice.
    /// </summary>
    private static VariableRow Row(EntityRepository live, Entity entity, bool chameleon)
    {
        var origin = new VariableRowOrigin(
            Asset, chameleon ? default : entity, "Variables", Path, "TestAsset");

        return new VariableRow(
            origin, Path, "int", typeof(int),
            ReadValue: () => BitConverter.GetBytes(live.GetComponent<WatchedComp>(entity).Health),
            // ⭐⭐ A PULSE, and it is load-bearing for the post-drain half of the rail.
            // 📐 Measured: VariableRowSampler holds its sample for ever when a row has no tick source
            //    ("a row with no pulse samples exactly once and then holds") ⇒ without this the rail
            //    would read the pre-drain value back and look like the drain had failed. ⚠ Production
            //    rows carry a pulse; ⛔ a rail that dropped it would be testing a shape no panel has.
            AssetTick: () => (uint)live.SimulationTick);
    }

    private static VariableTableModel Model(VariableRow row, StagedWriteView staged)
        => new(new FixedVariableRowSource(new[] { row }), VariableTableColumns.Details)
        {
            RunState     = VariableRunState.Paused,
            StagedWrites = staged,
        };

    // ══ THE RAIL ═════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>ONE edit ⇒ BOTH panels yellow, BOTH showing the staged bytes; a drain clears BOTH.</b>
    ///
    /// <para>⛔⛔ <b>The negative half is not decoration.</b> Before the edit, both rows must be white
    /// and showing <c>10</c>. ⚠ Without that, an implementation that reported <c>Pending</c>
    /// unconditionally would pass the interesting half — 📌 <c>BP-402</c> ②'s vacuous-rail shape, which
    /// is how a rail goes green while asserting nothing.</para>
    /// </summary>
    [Fact]
    public void AnEdit_YellowsBothPanels_WithTheSameStagedBytes_AndADrainClearsBoth()
    {
        var (manager, live, entity) = Live();
        var staged   = ViewOver(manager, entity);
        var details  = Model(Row(live, entity, chameleon: true),  staged);
        var watch    = Model(Row(live, entity, chameleon: false), staged);

        // ── before the edit: white, and both read the APPLIED value ──────────
        Assert.False(PendingOf(details));
        Assert.False(PendingOf(watch));
        Assert.Equal(10, ValueOf(details));
        Assert.Equal(10, ValueOf(watch));

        // ── the designer edits, and the write STAGES (R-63 / R-126) ──────────
        manager.StageFieldMutation(entity, typeof(WatchedComp), byteOffset: 0, BitConverter.GetBytes(77));

        // ⭐⭐⭐ §7 — IMMEDIATELY, with no tick in between, BOTH surfaces agree.
        Assert.True(PendingOf(details));
        Assert.True(PendingOf(watch));
        Assert.Equal(77, ValueOf(details));
        Assert.Equal(77, ValueOf(watch));

        // ⛔ …and the repository has NOT changed. 📌 R-130: yellow means "not applied yet"; if the
        //    write had landed the colour would be a lie.
        Assert.Equal(10, live.GetComponent<WatchedComp>(entity).Health);

        // ── the tick drains it (W1/W2's job, simulated here through the seam) ─
        ((IStagedWrites)manager).DrainInto(live);
        var ecb = (EntityCommandBuffer)((ISimulationView)live).GetCommandBuffer();
        ecb.Playback(live);
        live.Tick();

        // ⭐⭐ THE AUTO-CLEAR — ⛔ nothing called a "ClearPending": the mutation left the queue.
        Assert.False(manager.HasPending);
        Assert.False(PendingOf(details));
        Assert.False(PendingOf(watch));

        // ⭐ …and both now read the APPLIED value, from the repository rather than from the queue.
        Assert.Equal(77, live.GetComponent<WatchedComp>(entity).Health);
        Assert.Equal(77, ValueOf(details));
        Assert.Equal(77, ValueOf(watch));
    }

    /// <summary>
    /// ⭐⭐ <b>A row nobody edited stays WHITE while another row is pending.</b>
    /// ⛔ The discrimination half: 📐 <c>TryGetPending</c> keys on <c>(entity, typeId, byteOffset)</c>,
    /// so a second FIELD of the same component must not inherit the first one's yellow. ⚠ An
    /// implementation that answered <see cref="IStagedWrites.HasPending"/> per row — the obvious cheap
    /// mistake — would yellow the whole table and pass the rail above.
    /// </summary>
    [Fact]
    public void ASiblingFieldOfTheSameComponent_DoesNotInheritTheYellow()
    {
        var (manager, live, entity) = Live();

        var ammo = new VariableRow(
            new VariableRowOrigin(Asset, entity, "Variables", "Ammo", "TestAsset"),
            "Ammo", "int", typeof(int),
            ReadValue: () => BitConverter.GetBytes(live.GetComponent<WatchedComp>(entity).Ammo));

        // ⭐ The resolver answers with each row's OWN offset — Health at 0, Ammo at 4.
        var staged = new StagedWriteView(
            () => manager,
            (origin, _) => new StagedFieldAddress(
                ComponentTypeRegistry.GetId(typeof(WatchedComp)),
                ByteOffset: origin.VariablePath == "Ammo" ? sizeof(int) : 0,
                SizeBytes:  sizeof(int)),
            () => entity);

        var health = Model(Row(live, entity, chameleon: false), staged);
        var ammoModel = new VariableTableModel(
            new FixedVariableRowSource(new[] { ammo }), VariableTableColumns.Details)
        {
            RunState = VariableRunState.Paused, StagedWrites = staged,
        };

        manager.StageFieldMutation(entity, typeof(WatchedComp), byteOffset: 0, BitConverter.GetBytes(77));

        Assert.True (PendingOf(health));
        Assert.False(PendingOf(ammoModel));
        Assert.Equal(5, ValueOf(ammoModel));       // ⭐ still its own applied value
    }

    /// <summary>
    /// ⭐⭐⭐ <b>A SUPERSEDED edit shows the LAST one.</b>
    /// ⚠ 📐 <c>_pendingMutations</c> is a QUEUE, not a map: editing the same field twice before a drain
    /// leaves TWO entries. ⛔ Returning the first would show the designer a value they had already
    /// replaced — and the drain applies them in order, so the LAST is what will actually land.
    /// ⇒ ⭐ <c>TryGetPending</c> keeps walking and the last match wins; asserted here because "walks the
    /// whole queue" is invisible in a single-edit test.
    /// </summary>
    [Fact]
    public void TwoEditsBeforeADrain_ShowTheSecond_BecauseThatIsWhatWillLand()
    {
        var (manager, live, entity) = Live();
        var model = Model(Row(live, entity, chameleon: false), ViewOver(manager, entity));

        manager.StageFieldMutation(entity, typeof(WatchedComp), 0, BitConverter.GetBytes(77));
        manager.StageFieldMutation(entity, typeof(WatchedComp), 0, BitConverter.GetBytes(99));

        Assert.Equal(99, ValueOf(model));

        ((IStagedWrites)manager).DrainInto(live);
        ((EntityCommandBuffer)((ISimulationView)live).GetCommandBuffer()).Playback(live);
        live.Tick();

        Assert.Equal(99, live.GetComponent<WatchedComp>(entity).Health);
    }

    /// <summary>
    /// ⭐⭐ <b><c>IsRewound</c> is the manager's own pause</b> — 📌 <c>R-63</c>: the drain must SKIP while
    /// a breakpoint holds a rewound view, because the resume path restores the post-tick snapshot and
    /// drains itself. ⛔ Railed because the TIME lane's <c>ResumeAndDrainSystem</c> consumes this bit and
    /// a wrong answer there loses a designer's edit silently.
    /// </summary>
    [Fact]
    public void IsRewound_TracksTheManagersPause()
    {
        var (manager, _, entity) = Live();
        var staged = (IStagedWrites)manager;

        Assert.False(staged.IsRewound);

        var id = manager.Add(new Hrot.Diagnostics.Breakpoints.Breakpoint
        {
            Id = Hrot.Diagnostics.Breakpoints.BreakpointId.Invalid, Enabled = true, OccurrenceThreshold = 1, DisplayName = "w4",
        });
        manager.OnHit(manager.AllBreakpoints.First(b => b.Id == id), entity);

        Assert.True(staged.IsRewound);
        Assert.Equal(manager.IsPaused, staged.IsRewound);
    }

    /// <summary>⭐ Whether the panel would paint this row yellow — asked of the BUILT view, i.e. the
    /// same object the renderer reads.</summary>
    private static bool PendingOf(VariableTableModel model)
    {
        var view = model.Build();
        return view.HighlightOf(view.AllRows[0]).Pending;
    }

    /// <summary>⭐ What the panel's Value column would render, as an int.</summary>
    private static int ValueOf(VariableTableModel model)
    {
        var view = model.Build();
        var bytes = view.AllRows[0].ReadValue().ToArray();
        return BitConverter.ToInt32(bytes, 0);
    }
}
