using System;
using System.Linq;
using Fdp.Core;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Diagnostics.Breakpoints.Tests;

/// <summary>A component whose two int fields sit at known offsets 0 and 4 (ID 262).</summary>
[ComponentId(262)]
internal struct OffsetAddressedComp
{
    public int First;    // offset 0
    public int Second;   // offset 4
}

/// <summary>
/// ⭐⭐⭐ <b>Batch 84 item 2 — the OFFSET-ADDRESSED staging entry point.</b>
///
/// <para>📌 <b>Ruling 14</b> <i>(user)</i>: <i>"it can not be full component overwrite only, but
/// <b>chirurgical change</b>."</i> 📌 <b><c>R-64</c>:</b> the <c>Fdp.Core</c> half already ships end to
/// end — <c>SetComponentFieldRaw</c>, the <c>IsFieldWrite</c> branch in the drain, and
/// <c>SurgicalFieldWriteTests</c>.</para>
///
/// <para>⚠⚠ <b>One correction to the handoff's premise, measured before building.</b> It says
/// <c>IDataBreakpointManager</c> exposes <i>"whole-component <c>StageMutation</c> only"</i>. 📐 <b>Not
/// quite:</b> the 4-arg <c>StageMutation(…, baseline)</c> ALREADY sets <c>ByteOffset</c> — it diffs a
/// before/after pair of boxed components and enqueues one field write per changed run *(Batch 66)*.
/// ⇒ ⭐ <b>what is genuinely missing is the OFFSET-ADDRESSED shape</b>, because a variable editor knows
/// the field's offset from the layout and has no boxed <c>Blackboard1024</c> to diff — and
/// manufacturing a 1024-byte baseline to diff back down to four bytes would be a second answer to
/// <i>"which bytes changed?"</i>.</para>
/// </summary>
[Collection("ComponentRegistry")]
public sealed class StagedFieldWriteEntryPointTests
{
    private static (DataBreakpointManager Manager, EntityRepository Live, Entity Target) Paused()
    {
        ComponentTypeRegistry.Clear();
        var live    = new EntityRepository();
        var preTick = new EntityRepository();
        live.RegisterComponent<OffsetAddressedComp>();
        preTick.RegisterComponent<OffsetAddressedComp>();

        var entity = live.CreateEntity();
        live.AddComponent(entity, new OffsetAddressedComp { First = 1, Second = 10 });
        preTick.SyncFrom(live);

        live.Tick();

        var manager = new DataBreakpointManager(
            live, preTick, new DebugSnapshotProvider(preTick), new MockDebugTimeController());
        var bpId = manager.Add(new Breakpoint
        {
            Id = BreakpointId.Invalid, Enabled = true, OccurrenceThreshold = 1, DisplayName = "staged",
        });
        manager.OnHit(manager.AllBreakpoints.First(b => b.Id == bpId), entity);

        return (manager, live, entity);
    }

    // ══ it stages a FIELD write, not a component write ═══════════════════════

    /// <summary>
    /// 🔴 <b>RED before Batch 84</b> — there was no way to stage a write by offset at all.
    /// ⭐ Asserted on <c>IsFieldWrite</c>, because a mutation that merely lands in the queue as a
    /// whole-component write is exactly the failure ruling 14 forbids.
    /// </summary>
    [Fact]
    public void AnOffsetAddressedWrite_IsStagedAsAFieldWrite()
    {
        var (manager, _, target) = Paused();

        manager.StageFieldMutation(target, typeof(OffsetAddressedComp), 4, BitConverter.GetBytes(99));

        var staged = Assert.Single(manager.PendingMutationsQueue);
        Assert.True(staged.IsFieldWrite, "A write staged by offset must not degrade to a whole-component write.");
        Assert.Equal(4, staged.ByteOffset);
        Assert.Equal(4, staged.SizeBytes);
    }

    /// <summary>
    /// ⭐⭐ <b>And it LANDS, through the real command buffer and repository</b> — ⛔ not just "the queue
    /// grew". 📌 <c>R-63</c>: the drain runs AFTER <c>_liveRepo</c> is restored from the post-tick
    /// snapshot, which is exactly why the staged write survives resume.
    /// </summary>
    [Fact]
    public void AStagedFieldWrite_LandsOnResume()
    {
        var (manager, live, target) = Paused();

        manager.StageFieldMutation(target, typeof(OffsetAddressedComp), 4, BitConverter.GetBytes(99));

        // ⭐ The drain writes into the repo's command buffer; playback is the tick boundary.
        //   ⚠ RequestContinue (not Step) on purpose — both resume paths must reach the same drain.
        //   ⭐⭐ W5: the drain is no longer INSIDE RequestContinue — see ResumeThenDrain.
        var ecb = (EntityCommandBuffer)((Fdp.ModuleHost.Abstractions.ISimulationView)live).GetCommandBuffer();
        manager.ContinueAndDrain(live);
        ecb.Playback(live);

        var after = live.GetComponent<OffsetAddressedComp>(target);
        Assert.Equal(99, after.Second);
        Assert.Equal(1,  after.First);   // ⭐ untouched, by construction rather than by luck
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The composition property — N queued writes to one component ALL land, IN ORDER.</b>
    ///
    /// <para>⛔ This is precisely what a whole-component write destroys: two staged components each
    /// carry the OTHER field's stale value, so the second silently undoes the first. ⭐ Asserted with
    /// two different fields AND a same-field overwrite, because the two failures differ — the first
    /// loses a field, the second loses an ordering.</para>
    /// </summary>
    [Fact]
    public void TwoQueuedFieldWrites_BothLand_AndTheLastWriteWins()
    {
        var (manager, live, target) = Paused();

        manager.StageFieldMutation(target, typeof(OffsetAddressedComp), 0, BitConverter.GetBytes(7));
        manager.StageFieldMutation(target, typeof(OffsetAddressedComp), 4, BitConverter.GetBytes(8));
        manager.StageFieldMutation(target, typeof(OffsetAddressedComp), 0, BitConverter.GetBytes(9));

        var ecb = (EntityCommandBuffer)((Fdp.ModuleHost.Abstractions.ISimulationView)live).GetCommandBuffer();
        manager.StepAndDrain(live);              // ⭐ W5: two steps, not one — see ResumeThenDrain
        ecb.Playback(live);

        var after = live.GetComponent<OffsetAddressedComp>(target);
        Assert.Equal(9, after.First);    // ⭐ third write, not the first
        Assert.Equal(8, after.Second);   // ⛔ not reverted by the write that followed it
    }

    // ══ a bad range FAILS LOUDLY ═════════════════════════════════════════════

    /// <summary>
    /// 🔴🔴 <b>📌 <c>Q32</c> §2.1:</b> <i>"an out-of-range offset/size is <b>MEMORY CORRUPTION</b>, not
    /// a wrong value. <b>Bounds-check against the registered component size and fail LOUDLY</b>."</i>
    ///
    /// <para>⭐ <b>Asserted as THROWS, not as "does nothing"</b> — a staging call that silently drops a
    /// bad range is the same defect wearing a working feature's clothes.</para>
    ///
    /// <para>⚠ <b>Why here and not only in the engine.</b> 📐 <c>ComponentTable.SetRawAt</c> DOES
    /// bounds-check — but at PLAYBACK, one step or continue later, on the sim thread, where the row
    /// and the dialog that produced it are long gone. ⭐ This check fires at the designer's OK button.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(-1, 4)]    // ⛔ negative offset
    [InlineData(8,  4)]    // ⛔ starts at the end of an 8-byte component
    [InlineData(5,  4)]    // ⛔ starts inside and runs off the end
    [InlineData(0,  0)]    // ⛔ empty write — nothing to say, and the engine would no-op it silently
    public void ABadRange_ThrowsAtStagingTime(int byteOffset, int length)
    {
        var (manager, _, target) = Paused();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            manager.StageFieldMutation(target, typeof(OffsetAddressedComp), byteOffset, new byte[length]));

        Assert.Empty(manager.PendingMutationsQueue);   // ⭐ and it staged NOTHING
    }

    /// <summary>⛔ A managed component has no byte layout to patch, and says so rather than forwarding.</summary>
    [Fact]
    public void AManagedComponent_IsRefused_NotForwarded()
    {
        var (manager, _, target) = Paused();

        Assert.Throws<ArgumentException>(() =>
            manager.StageFieldMutation(target, typeof(string), 0, new byte[4]));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The interface's DEFAULT must throw, ⛔ never forward to the whole-component write.</b>
    /// 📌 <c>R-65</c>: <c>Blackboard1024</c> is <b>ONE component shared by BTree, HSM and Blueprint at
    /// disjoint offsets</b>, so the fallback would clobber other subsystems' state.
    /// ⚠⚠ <b>Cite the SHARING, never the size</b> — <i>"exceeds <c>MaxComponentSize</c>"</i> is false:
    /// <c>Blackboard1024.ByteSize == 1024</c> and the guard is <c>&gt;</c>, so it fits exactly.
    /// </summary>
    [Fact]
    public void TheInterfaceDefault_Throws_RatherThanClobberingTheWholeComponent()
    {
        IDataBreakpointManager bare = new NoSurgicalWriteManager();

        var ex = Assert.Throws<NotSupportedException>(() =>
            bare.StageFieldMutation(default, typeof(OffsetAddressedComp), 0, new byte[4]));

        Assert.Contains("clobber", ex.Message);
        Assert.Empty(((NoSurgicalWriteManager)bare).WholeComponentCalls);
    }

    /// <summary>⭐ An implementer that takes the default — the shape every test double has.</summary>
    private sealed class NoSurgicalWriteManager : IDataBreakpointManager
    {
        public System.Collections.Generic.List<Type> WholeComponentCalls { get; } = new();

        public void StageMutation(Entity entity, Type componentType, object componentValue)
            => WholeComponentCalls.Add(componentType);

        public BreakpointId Add(Breakpoint breakpoint) => default;
        public BreakpointId AddBreakpoint(Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto condition,
            Entity? filter = null, int occurrenceThreshold = 1, string displayName = "",
            Guid? sourceElementId = null) => default;
        public void Remove(BreakpointId id) { }
        public void SetEnabled(BreakpointId id, bool enabled) { }
        public void UpdateCondition(BreakpointId id, Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto? condition) { }
        public void MarkAsWatch(BreakpointId id, bool isWatch) { }
        public void SaveWatches(string path) { }
        public void LoadWatches(string path) { }
        public void OnHotReloadCompleted() { }
        public void OnHotReloadBegin() { }
        public void OnHit(Breakpoint bp, Entity entity) { }
        public void RequestStep() { }
        public void RequestContinue() { }
        public void OnExternalHit(string tag, Entity entity) { }
        public event Action<Breakpoint, Entity>? OnBreakpointHit { add { } remove { } }
        public event Action<bool>? OnPauseStateChanged { add { } remove { } }
        public bool IsPaused => false;
        public Fdp.ModuleHost.Abstractions.ISimulationView ActiveView => null!;
        public long PausedTick => 0;
        public int PendingMutationsCount => 0;
        public System.Collections.Generic.IReadOnlyList<Breakpoint> AllBreakpoints
            => Array.Empty<Breakpoint>();
        public bool HasMountedDelegates => false;
        public bool HasStatefulTrackers => false;
        public void EvaluateStatefulBreakpoints(EntityRepository repo) { }
        public System.Collections.Generic.IReadOnlyList<(Breakpoint Breakpoint, CompiledComponentPredicate Compiled)>
            MountedComponentPredicates => Array.Empty<(Breakpoint, CompiledComponentPredicate)>();
        public System.Collections.Generic.IReadOnlyList<(Breakpoint Breakpoint, CompiledEventScanner Scanner)>
            MountedEventScanners => Array.Empty<(Breakpoint, CompiledEventScanner)>();
    }
}
