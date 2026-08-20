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
    /// ⭐⭐⭐ <b>THE offset agreement — the whole reason <c>WorkingStateLayout</c> exists.</b>
    ///
    /// <para>📌 <c>Q32</c> §2.1: <i>"the read path uses <c>8 + OffsetBytes</c> … whoever computes the
    /// offset must own that <c>+8</c> in exactly one place, not two."</i> ⇒ ⭐ the caller passes the
    /// offset <b>within the working-state block</b>, exactly as the LAYOUT reports it, and the session
    /// adds the header through the one owner. ⛔ A caller that added its own <c>+8</c> would write 8
    /// bytes past the field it was shown — on <c>Blackboard1024</c> that is another subsystem's bytes
    /// (📌 <c>R-65</c>), and it would look like a wrong value rather than corruption.</para>
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(64)]
    public void TheStagedOffsetIsTheLayoutsOffsetPlusTheHeader(int fieldOffset)
    {
        var (session, manager) = Session(paused: true);

        session.TryWriteWorkingStateField(
            default, typeof(Fdp.Toolkit.Behavior.Components.Blackboard1024), fieldOffset, new byte[4]);

        Assert.Equal(
            WorkingStateLayout.ComponentOffsetOf(fieldOffset),
            Assert.Single(manager.Staged).ByteOffset);
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
