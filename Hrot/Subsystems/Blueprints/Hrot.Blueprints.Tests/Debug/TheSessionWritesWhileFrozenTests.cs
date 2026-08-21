using System;
using System.Collections.Generic;
using Fdp.Core;
using Hrot.Blueprints.Core.Debug;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Tests.Editor;
using Hrot.Diagnostics.Breakpoints;
using BpBreakpoint = Hrot.Diagnostics.Breakpoints.Breakpoint;
using BpBreakpointId = Hrot.Diagnostics.Breakpoints.BreakpointId;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// ⭐⭐⭐ <b>Batch 84 item 3 — the session half: a live write, STAGED, and only while frozen.</b>
///
/// <para>📌 <b>Ruling 15</b> <i>(user)</i>: <i>"the change of runtime var makes sense <b>ONLY if sim is
/// paused on breakpoint or deterministic time step</b>."</i> · 📌 <b><c>R-63</c></b>, measured
/// <c>2026-08-18</c>: while paused <c>ActiveView</c> IS the pre-tick snapshot and resume restores the
/// live repo from the POST-tick one, ⇒ ⛔ <b>a direct write to the view is silently lost</b>; ⭐ the
/// staged write drains AFTER that restore, which is exactly why it survives.</para>
///
/// <para>⭐ <b>This rail covers session → staging.</b> Staging → the world is
/// <c>StagedFieldWriteEntryPointTests</c>, over the REAL <c>DataBreakpointManager</c>,
/// <c>EntityCommandBuffer</c> and <c>EntityRepository</c>. ⚠ Stated rather than implied: neither half
/// alone is the chain, and this file does not pretend to be both.</para>
/// </summary>
public sealed class TheSessionWritesWhileFrozenTests
{
    private static (BlueprintDebugSession Session, RecordingManager Manager) Session(bool paused)
    {
        var repo    = new EntityRepository();
        var session = new BlueprintDebugSession(
            new BlueprintRegistry(), repo, new MockTimeController());

        var manager = new RecordingManager();
        session.SetDataBreakpointManager(manager);
        if (paused) session.Pause();

        return (session, manager);
    }

    /// <summary>
    /// ⛔⛔ <b>Free-running REFUSES, and stages NOTHING.</b> 📌 Ruling 15 — <i>"at that time nothing
    /// else changes the blackboard"</i> is only true while frozen. ⭐ <c>false</c> rather than a throw:
    /// the UI greys a control on this answer (📌 the visual-check guide's <c>F3</c>).
    /// </summary>
    [Fact]
    public void WhileFreeRunning_TheWriteIsRefused_AndNothingIsStaged()
    {
        var (session, manager) = Session(paused: false);

        Assert.False(session.TryWriteWorkingStateField(
            default, typeof(Fdp.Toolkit.Behavior.Components.Blackboard1024), 4, new byte[4]));
        Assert.Empty(manager.Staged);
    }

    /// <summary>
    /// 🔴 <b>RED before Batch 84</b> — the interface had no write at all.
    /// ⭐ While frozen the write is accepted and STAGED, ⛔ never applied to <c>ActiveView</c>
    /// (📌 <c>R-63</c>).
    /// </summary>
    [Fact]
    public void WhileFrozen_TheWriteIsStaged()
    {
        var (session, manager) = Session(paused: true);

        Assert.True(session.TryWriteWorkingStateField(
            default, typeof(Fdp.Toolkit.Behavior.Components.Blackboard1024), 4, BitConverter.GetBytes(7)));

        var staged = Assert.Single(manager.Staged);
        Assert.Equal(typeof(Fdp.Toolkit.Behavior.Components.Blackboard1024), staged.ComponentType);
        Assert.Equal(7, BitConverter.ToInt32(staged.Bytes));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE offset agreement — and Batch 102 (<c>102a</c>) MOVED WHO OWNS IT.</b>
    ///
    /// <para>📌 <c>Q32</c> §2.1: <i>"the read path uses <c>8 + OffsetBytes</c> … whoever computes the
    /// offset must own that <c>+8</c> in exactly one place, not two."</i> ⭐ <b>That ruling is
    /// unchanged.</b> ⚠ <b>What changed is WHERE the one place is.</b></para>
    ///
    /// <para>🔴🔴 <b>This rail asserted that <c>TryWriteWorkingStateField</c> adds the header</b>, and
    /// it did — <b>unconditionally</b>. 📐 That is correct for <c>AiPrimitive</c>'s flat block and
    /// ⛔ <b>WRONG for an <c>Instance</c> slot</b>, whose payload the partition allocator places and
    /// whose block opens with a 16-byte <c>BlueprintLatentCursor</c>, not an 8-byte working-state
    /// header. ⇒ every Instance write would have landed <b>8 bytes past the field</b> — 📌 on a
    /// partitioned blackboard, in the NEIGHBOURING blueprint's bytes.</para>
    ///
    /// <para>⭐⭐ <b>So the transform moved into the RESOLVER's per-kind arms</b>, where the layout is
    /// known, and the writer now stages the offset <b>exactly as given</b>. ⇒ ⭐ this rail asserts the
    /// SAME property from the other side: <b>the writer adds nothing of its own.</b> ⚠ The "applied
    /// exactly once" end-to-end claim is pinned by
    /// <c>TheBlueprintLiveWriteLandsTests.TheHeaderIsAppliedExactlyOnce</c>, over both field tables,
    /// and the Instance half by <c>TheInstanceWriteLandsInTheSlotTests</c>.</para>
    ///
    /// <para>⛔ <b>This is a re-expression, not a weakening</b> — a writer that quietly re-applied a
    /// header would still redden it, and that is the corruption the original rail was written against.
    /// ⚠ It is stated here rather than silently edited because ⭐ <b>a rail whose claim about the code
    /// became false is a finding.</b></para>
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(64)]
    public void TheWriterStagesTheOffsetItWasGiven(int componentOffset)
    {
        var (session, manager) = Session(paused: true);

        session.TryWriteWorkingStateField(
            default, typeof(Fdp.Toolkit.Behavior.Components.Blackboard1024), componentOffset, new byte[4]);

        Assert.Equal(componentOffset, Assert.Single(manager.Staged).ByteOffset);

        // ⛔ And explicitly NOT the old behaviour: a re-applied header is the corruption case.
        //    ⚠ Skipped at 0, where ComponentOffsetOf(0) and 0 could only differ by the header itself —
        //    which the first assertion already covers.
        if (componentOffset != 0)
            Assert.NotEqual(
                WorkingStateLayout.ComponentOffsetOf(componentOffset),
                manager.Staged[0].ByteOffset);
    }

    /// <summary>
    /// ⛔ <b>A negative field offset THROWS</b>, even while frozen — 📌 <c>Q32</c> §2.1: <i>"an
    /// out-of-range offset/size is MEMORY CORRUPTION, not a wrong value."</i> ⭐ Distinct from the
    /// run-state refusal above, which returns <c>false</c>: one is an expected answer, the other is a
    /// broken caller.
    /// </summary>
    [Fact]
    public void ANegativeFieldOffset_Throws_EvenWhileFrozen()
    {
        var (session, _) = Session(paused: true);

        Assert.Throws<ArgumentOutOfRangeException>(() => session.TryWriteWorkingStateField(
            default, typeof(Fdp.Toolkit.Behavior.Components.Blackboard1024), -1, new byte[4]));
    }

    /// <summary>
    /// ⚠ <b>No breakpoint manager ⇒ refuse, do not throw.</b> ⭐ A headless or partly-wired host is a
    /// real configuration; ⛔ but it must not look like a successful write.
    /// </summary>
    [Fact]
    public void WithNoBreakpointManager_TheWriteIsRefused()
    {
        var repo    = new EntityRepository();
        var session = new BlueprintDebugSession(
            new BlueprintRegistry(), repo, new MockTimeController());
        session.Pause();

        Assert.False(session.TryWriteWorkingStateField(
            default, typeof(Fdp.Toolkit.Behavior.Components.Blackboard1024), 0, new byte[4]));
    }

    /// <summary>
    /// ⭐⭐ <b>The interface's DEFAULT is "I cannot write"</b>, so every existing session double keeps
    /// compiling and none of them silently claims to have written. ⛔ Not a throw: a host asks this to
    /// decide whether to grey a control.
    /// </summary>
    [Fact]
    public void TheInterfaceDefault_RefusesRatherThanPretending()
    {
        Hrot.Blueprints.Core.Debug.IBlueprintDebugSession bare = new MockDebugSession();

        Assert.False(bare.TryWriteWorkingStateField(default, typeof(int), 0, new byte[4]));
    }

    /// <summary>⭐ Records what the session staged — the collaborator, not a second implementation.</summary>
    /// <summary>
    /// ⭐ <c>internal</c> since Batch 97 (<c>97c</c>) so <c>TheBlueprintLiveWriteLandsTests</c> can
    /// stage through the SAME recorder — ⛔ a second one would be two implementations of one concept
    /// (ruling 9), and the two would drift on exactly the assertion that matters: the byte offset.
    /// </summary>
    internal sealed class RecordingManager : IDataBreakpointManager
    {
        public List<(Type ComponentType, int ByteOffset, byte[] Bytes)> Staged { get; } = new();

        public void StageFieldMutation(Entity entity, Type componentType, int byteOffset, ReadOnlySpan<byte> bytes)
            => Staged.Add((componentType, byteOffset, bytes.ToArray()));

        public void StageMutation(Entity entity, Type componentType, object componentValue)
            => throw new InvalidOperationException(
                "Ruling 14: a variable edit must never take the whole-component path (R-65).");

        public BpBreakpointId Add(BpBreakpoint breakpoint) => default;
        public BpBreakpointId AddBreakpoint(Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto condition,
            Entity? filter = null, int occurrenceThreshold = 1, string displayName = "",
            Guid? sourceElementId = null) => default;
        public void Remove(BpBreakpointId id) { }
        public void SetEnabled(BpBreakpointId id, bool enabled) { }
        public void UpdateCondition(BpBreakpointId id, Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto? condition) { }
        public void MarkAsWatch(BpBreakpointId id, bool isWatch) { }
        public void SaveWatches(string path) { }
        public void LoadWatches(string path) { }
        public void OnHotReloadCompleted() { }
        public void OnHotReloadBegin() { }
        public void OnHit(BpBreakpoint bp, Entity entity) { }
        public void RequestStep() { }
        public void RequestContinue() { }
        public void OnExternalHit(string tag, Entity entity) { }
        public event Action<BpBreakpoint, Entity>? OnBreakpointHit { add { } remove { } }
        public event Action<bool>? OnPauseStateChanged { add { } remove { } }
        public bool IsPaused => true;
        public Fdp.ModuleHost.Abstractions.ISimulationView ActiveView => null!;
        public long PausedTick => 0;
        public int PendingMutationsCount => Staged.Count;
        public IReadOnlyList<BpBreakpoint> AllBreakpoints => Array.Empty<BpBreakpoint>();
        public bool HasMountedDelegates => false;
        public bool HasStatefulTrackers => false;
        public void EvaluateStatefulBreakpoints(EntityRepository repo) { }
        public IReadOnlyList<(BpBreakpoint Breakpoint, CompiledComponentPredicate Compiled)>
            MountedComponentPredicates => Array.Empty<(BpBreakpoint, CompiledComponentPredicate)>();
        public IReadOnlyList<(BpBreakpoint Breakpoint, CompiledEventScanner Scanner)>
            MountedEventScanners => Array.Empty<(BpBreakpoint, CompiledEventScanner)>();
    }
}
