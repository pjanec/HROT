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
    /// <summary>
    /// ⭐⭐ <c>MIN</c> — the harness now sets up the THREE-WAY. ⚠ <c>sessionPaused</c> is still driven,
    /// deliberately: ⛔ it is no longer the gate, and a rail that stopped setting it could not tell the
    /// difference between <i>"the gate moved"</i> and <i>"the flag happens to agree"</i>.
    /// </summary>
    private static (BlueprintDebugSession Session, RecordingManager Manager) Session(
        bool clockHalted, bool breakpointHolding, bool sessionPaused)
    {
        var repo    = new EntityRepository();
        var session = new BlueprintDebugSession(
            new BlueprintRegistry(), repo, new MockTimeController());

        var manager = new RecordingManager
        {
            ClockHalted       = clockHalted,
            BreakpointHolding = breakpointHolding,
        };
        session.SetDataBreakpointManager(manager);
        if (sessionPaused) session.Pause();

        return (session, manager);
    }

    /// <summary>⭐ The pre-<c>MIN</c> shape: a breakpoint holding a rewound tick.</summary>
    private static (BlueprintDebugSession Session, RecordingManager Manager) UnderABreakpoint()
        => Session(clockHalted: true, breakpointHolding: true, sessionPaused: true);

    /// <summary>
    /// ⛔⛔ <b>A RUNNING simulation REFUSES, and stages NOTHING.</b> 📌 Ruling 15 — <i>"at that time
    /// nothing else changes the blackboard"</i> is only true while stopped. ⭐ <c>false</c> rather than
    /// a throw: the UI turns this answer into a sentence (📌 the visual guide's <c>F3</c>).
    ///
    /// <para>⚠⚠ <b><c>MIN</c> RE-EXPRESSED THIS RAIL, and the re-expression is the finding.</b> It used
    /// to read <c>Session(paused: false)</c> — i.e. it asserted that <b>the SESSION's own pause flag</b>
    /// was the gate. 📐 That flag is set only by this session's <c>Pause()</c> or a breakpoint hit, so
    /// a designer who paused time from the TOOLBAR was refused while demonstrably stopped
    /// *(<c>AS-3</c>)*. ⇒ ⭐ the rail now drives the CLOCK, which is what <c>R-126</c> makes the single
    /// source of "paused". ⛔ <b>Not a weakening</b>: a running simulation is still refused, and
    /// <see cref="TheSessionsOwnPauseFlagIsNoLongerTheGate"/> pins the half that changed.</para>
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <b><c>W3</c> INVERTED THIS RAIL.</b> It asserted <i>"while the clock advances the write is
    /// REFUSED and nothing is staged"</i>. 📌 <c>R-126</c>, the user: <i>"running is not a reason to
    /// refuse, it is a reason to STAGE"</i> ⇒ the refusal is deleted and the bytes queue for the
    /// kernel's drain. ⭐ <b>The half worth keeping is asserted harder</b>: the payload must be the
    /// designer's, at their offset — a "stages something" rail would pass for an implementation that
    /// queued the wrong bytes.
    /// </remarks>
    [Fact]
    public void WhileTheClockAdvances_TheWriteIsAcceptedAndStaged()
    {
        var (session, manager) = Session(
            clockHalted: false, breakpointHolding: false, sessionPaused: false);

        Assert.True(session.TryWriteWorkingStateField(
            default, typeof(Fdp.Toolkit.Behavior.Components.Blackboard1024), 4, BitConverter.GetBytes(7)));

        var staged = Assert.Single(manager.Staged);
        Assert.Equal(typeof(Fdp.Toolkit.Behavior.Components.Blackboard1024), staged.ComponentType);
        Assert.Equal(4, staged.ByteOffset);
        Assert.Equal(7, BitConverter.ToInt32(staged.Bytes));
    }

    /// <summary>
    /// ⛔⛔ <b>And a running simulation is refused EVEN IF the session thinks it is paused.</b>
    /// ⚠ The mirror of <see cref="TheSessionsOwnPauseFlagIsNoLongerTheGate"/>: <c>MIN</c> did not swap
    /// one flag for another, it moved the question to the clock — so a stale session flag can no longer
    /// admit a write into an advancing simulation either.
    /// </summary>
    /// <remarks>
    /// ⭐⭐ <b><c>W3</c> kept this rail's POINT and reversed its sign.</b> It asserted that a stale
    /// session flag could not ADMIT a write; now nothing is refused, so the checkable property is the
    /// stronger one: <b>the session's flag changes NOTHING in either direction</b> — the same bytes
    /// stage whether it claims paused or not. ⛔ A gate re-introduced on that flag reddens here.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheSessionsOwnPauseFlagChangesNothing(bool sessionPaused)
    {
        var (session, manager) = Session(
            clockHalted: false, breakpointHolding: false, sessionPaused: sessionPaused);

        Assert.True(session.TryWriteWorkingStateField(
            default, typeof(Fdp.Toolkit.Behavior.Components.Blackboard1024), 4, BitConverter.GetBytes(7)));

        var staged = Assert.Single(manager.Staged);
        Assert.Equal(4, staged.ByteOffset);
        Assert.Equal(7, BitConverter.ToInt32(staged.Bytes));
    }

    /// <summary>
    /// 🔴 <b>RED before Batch 84</b> — the interface had no write at all.
    /// ⭐ Under a BREAKPOINT the write is accepted and STAGED, ⛔ never applied to <c>ActiveView</c>
    /// (📌 <c>R-63</c>: resume restores the live repo from the POST-tick snapshot and drains after it,
    /// so a direct write there would be lost).
    /// </summary>
    [Fact]
    public void UnderABreakpoint_TheWriteIsStaged()
    {
        var (session, manager) = UnderABreakpoint();

        Assert.True(session.TryWriteWorkingStateField(
            default, typeof(Fdp.Toolkit.Behavior.Components.Blackboard1024), 4, BitConverter.GetBytes(7)));

        var staged = Assert.Single(manager.Staged);
        Assert.Equal(typeof(Fdp.Toolkit.Behavior.Components.Blackboard1024), staged.ComponentType);
        Assert.Equal(7, BitConverter.ToInt32(staged.Bytes));

        // ⛔ And it did NOT take the immediate arm — R-63's restore would have eaten it.
        Assert.Empty(manager.WroteNow);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>MIN</c> — THE TOOLBAR ARM. The write LANDS, and it is NOT staged.</b>
    ///
    /// <para>🔒 The user's ruling <c>R-126</c>: <i>"time is paused OR debugger hit a breakpoint — in
    /// both cases the simulation is stopped and we can write new values."</i> 📌 The live failure this
    /// closes: <i>edit a working-state variable while paused from the toolbar → the value does not
    /// change</i>.</para>
    ///
    /// <para>⚠⚠ <b><c>W3</c> REVERSED THIS, and the reversal is the batch's user-visible change.</b>
    /// 📌 <c>MIN</c>'s rail said <i>"it landed, and it did not queue"</i>, because <c>AS-5</c> had
    /// measured that <b>nothing drained under a toolbar pause</b> — so staging would have been a write
    /// that never happens. ⭐ The kernel's <c>PreFrame</c> drain now exists *(design §8)*, so staging is
    /// the honest path and <c>R-130</c>'s yellow becomes true. ⛔ <c>WriteFieldNow</c> is gone; there is
    /// no "lands now" arm to assert.</para>
    /// </summary>
    [Fact]
    public void UnderAToolbarPause_TheWriteIsStagedRatherThanApplied()
    {
        var (session, manager) = Session(
            clockHalted: true, breakpointHolding: false, sessionPaused: false);

        Assert.True(session.TryWriteWorkingStateField(
            default, typeof(Fdp.Toolkit.Behavior.Components.Blackboard1024), 4, BitConverter.GetBytes(7)));

        var staged = Assert.Single(manager.Staged);
        Assert.Equal(typeof(Fdp.Toolkit.Behavior.Components.Blackboard1024), staged.ComponentType);
        Assert.Equal(4, staged.ByteOffset);
        Assert.Equal(7, BitConverter.ToInt32(staged.Bytes));
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>AS-3</c>, pinned from the other side: the SESSION's own pause flag is NOT the gate.</b>
    ///
    /// <para>⚠ Identical to <see cref="UnderAToolbarPause_TheWriteLandsNow_AndIsNotStaged"/> in
    /// mechanism, and it exists anyway — because that rail could keep passing if someone re-introduced
    /// <c>if (!_isPaused) return false;</c> AND a future harness happened to call <c>Pause()</c>. ⭐ This
    /// one states the property that actually regressed: <b>the session is NOT paused, and the write
    /// still lands.</b></para>
    /// </summary>
    [Fact]
    public void TheSessionsOwnPauseFlagIsNoLongerTheGate()
    {
        var (session, manager) = Session(
            clockHalted: true, breakpointHolding: false, sessionPaused: false);

        Assert.False(session.IsPaused);

        Assert.True(session.TryWriteWorkingStateField(
            default, typeof(Fdp.Toolkit.Behavior.Components.Blackboard1024), 0, new byte[4]));
        Assert.Single(manager.Staged);   // ⭐ W3: staged, not written-now — WriteFieldNow is gone
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
        var (session, manager) = UnderABreakpoint();

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
        var (session, _) = UnderABreakpoint();

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

        /// <summary>⭐ <c>MIN</c> — what the session wrote through the TOOLBAR arm, kept apart from
        /// <see cref="Staged"/> so a rail can tell the two arms apart rather than counting writes.</summary>
        public List<(Type ComponentType, int ByteOffset, byte[] Bytes)> WroteNow { get; } = new();

        /// <summary>⭐ <c>MIN</c> — drives the three-way. Defaults reproduce the pre-<c>MIN</c> world
        /// *(halted, breakpoint holding)*, so every rail written before this keeps its meaning.</summary>
        public bool ClockHalted { get; set; } = true;

        /// <summary>⭐ <c>MIN</c> — is a BREAKPOINT holding a rewound tick? ⛔ Not "is time stopped".</summary>
        public bool BreakpointHolding { get; set; } = true;

        public void StageFieldMutation(Entity entity, Type componentType, int byteOffset, ReadOnlySpan<byte> bytes)
            => Staged.Add((componentType, byteOffset, bytes.ToArray()));

        public bool IsClockHalted() => ClockHalted;

        // ⛔ W3 — `WriteFieldNow` is gone from IDataBreakpointManager, so this double no longer
        //    implements it. ⭐ `WroteNow` is kept and stays EMPTY by construction: a rail asserting
        //    "nothing was written immediately" is now guaranteed by the compiler, not by the fake.

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
        public bool IsPaused => BreakpointHolding;
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
