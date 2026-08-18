using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Row 58 — ONE Value column whose MEANING switches by run state.</b>
///
/// <para>📌 <b><c>Q32</c> ruling 3, verbatim:</b> <i>"ONE Value column, meaning switched by run state —
/// <b>initial</b> when not running, <b>current</b> when running or paused, across live / replay /
/// preview"</i> · ⛔ <i>"the coordinator argued two columns and is <b>overruled</b>"</i>.</para>
///
/// <para>📌 <b>ruling 4:</b> <i>"Value is READ-ONLY in the cell. Tooltip shows it full size and
/// pretty-printed (structs)."</i></para>
///
/// <para>🔴 <b>What was missing, measured:</b> <c>VariableRunState</c>, the formatter, the elision and
/// the <c>(pending)</c>/<c>&lt;unreadable&gt;</c> distinction <b>all already shipped</b> — but
/// <c>VariableTableModel.RunState</c> was <b>set by nothing in production</b> *(only tests)*, and the
/// INITIAL arm had no source at all, so every planning cell read <c>(pending)</c>. ⇒ ⭐ the column
/// existed and had exactly one meaning.</para>
/// </summary>
public sealed class ValueColumnModeTests
{
    // ══ the mode switch — ONE place answers "initial or current?" ════════════

    /// <summary>
    /// ⭐⭐ ⛔ <b>Not a bool per call site.</b> The cell, the tooltip and *(row 59)* the write target all
    /// key off this one function.
    /// </summary>
    [Theory]
    [InlineData(VariableRunState.Planning, VariableValueMode.Initial)]
    [InlineData(VariableRunState.Running,  VariableValueMode.Current)]
    [InlineData(VariableRunState.Paused,   VariableValueMode.Current)]
    public void TheModeFollowsRunState(VariableRunState run, VariableValueMode expected)
        => Assert.Equal(expected, VariableValue.ModeFor(run));

    /// <summary>
    /// ⚠ <b><c>Replay</c> is CURRENT, not initial.</b> Ruling 3's current arm spans <i>"live / replay /
    /// preview"</i> — ⛔ showing the declared default over recorded data would mislabel it as a plan.
    /// </summary>
    [Fact]
    public void ReplayReadsTheCurrentArm()
        => Assert.Equal(VariableValueMode.Current, VariableValue.ModeFor(VariableRunState.Replay));

    // ══ the run state itself — derived, not invented ═════════════════════════

    /// <summary>
    /// ⭐⭐ <b>Sim down ⇒ Planning.</b> ⛔ A surface with no way to observe the sim must not claim the
    /// sim is up.
    ///
    /// <para>⚠⚠ <b>Batch 84 / <c>R-66</c> — this assertion's SUBJECT changed, deliberately.</b> Batch 83
    /// asked <c>IDebugSessionRegistry</c>, on the premise <i>"a live session is what running means to
    /// this editor."</i> 📐 Measured FALSE: <c>ActiveSession</c> is set from the ACTIVE DOCUMENT's kind,
    /// so it says <i>"a blueprint is open."</i> ⭐ The rail now asks a CLOCK, because that is the
    /// question.</para>
    /// </summary>
    [Fact]
    public void WithTheSimDown_TheRunStateIsPlanning()
    {
        Assert.Equal(VariableRunState.Planning, RunStateSource.Resolve(() => false));
        Assert.Equal(VariableRunState.Planning, RunStateSource.Resolve(null));
    }

    /// <summary>⭐ Sim up ⇒ Running; the debugger holding time ⇒ Paused.</summary>
    [Theory]
    [InlineData(false, VariableRunState.Running)]
    [InlineData(true,  VariableRunState.Paused)]
    public void WithTheSimUp_TheRunStateFollowsTheFreezeFlag(bool frozen, VariableRunState expected)
        => Assert.Equal(expected, RunStateSource.Resolve(() => true, () => frozen));

    /// <summary>
    /// ⭐⭐⭐ <b>THE trap <c>R-66</c> leaves behind, pinned.</b> 📐 The editor BOOTS in
    /// <c>TimeMode.Deterministic</c> and stays there until preview starts, so
    /// <c>IsPausedByDebugger</c> is TRUE while nothing is running at all.
    /// ⛔ Deriving the run state from the freeze flag alone would report <c>Paused</c> on a dead editor
    /// — and <c>Paused</c> is the state row <c>59c</c> lets a designer WRITE THE LIVE WORLD in.
    /// ⭐ Frozen is only meaningful once the sim is up, which is the order <c>Resolve</c> evaluates in.
    /// </summary>
    [Fact]
    public void FrozenWithTheSimDown_IsStillPlanning_NotPaused()
        => Assert.Equal(VariableRunState.Planning, RunStateSource.Resolve(() => false, () => true));

    /// <summary>
    /// ⭐⭐ <b>A registrar with no clock reads <c>Planning</c>, and says so.</b> ⛔ Not a bug: a host that
    /// cannot observe the sim must not claim it is up. ⚠ 📌 <c>R-66</c> is precisely what happens when
    /// the safe default is attached to a signal that is always present.
    /// </summary>
    [Fact]
    public void TheRegistrarInstallsTheRunStateSource_OnTheDefaultPath()
    {
        var reg = Registrar();
        Assert.True(reg.Variables.HasRunStateSource);

        reg.Variables.SyncRunState();
        Assert.Equal(VariableRunState.Planning, reg.Variables.RunState);
    }

    /// <summary>
    /// 🔴 <b>RED before row 58.</b> Batch 79 shipped a settable <c>RunState</c> that nothing in
    /// production set, so the table was permanently in <c>Planning</c>. ⭐ Now it follows the sim —
    /// ⚠ and after Batch 84 it follows the SIM, not the open document.
    /// </summary>
    [Fact]
    public void TheTablesRunState_FollowsTheSim()
    {
        var simUp = false;
        var reg   = Registrar(isSimUp: () => simUp);

        reg.Variables.SyncRunState();
        Assert.Equal(VariableRunState.Planning, reg.Variables.RunState);

        simUp = true;
        reg.Variables.SyncRunState();
        Assert.Equal(VariableRunState.Running, reg.Variables.RunState);
    }

    /// <summary>⭐ And the model's view carries ONE resolved mode per frame, not one per cell.</summary>
    [Fact]
    public void TheViewCarriesOneResolvedModePerBuild()
    {
        var model = new VariableTableModel(
            new FixedVariableRowSource(new[] { Row("Health", typeof(int)) }),
            VariableTableColumns.Details);

        Assert.Equal(VariableValueMode.Initial, model.Build().ValueMode);
        model.RunState = VariableRunState.Running;
        Assert.Equal(VariableValueMode.Current, model.Build().ValueMode);
    }

    // ══ the INITIAL arm ══════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>A declared default renders in planning</b> — 🔴 before row 58 this cell said
    /// <c>(pending)</c>, which is the RUNNING arm's answer and is wrong when nothing is running.
    /// </summary>
    [Fact]
    public void InPlanning_ADeclaredDefaultIsShown()
    {
        var row = Row("Health", typeof(int), initialJson: "42");
        Assert.Equal("42", Formatter().Cell(row, VariableValueMode.Initial));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>No default ⇒ ZERO-INITIALISED, rendered as that zero.</b> 📌 <c>BP-247</c>'s uniform
    /// rule — <i>"`0` means leave it zero-initialised, for EVERY type"</i> — ⛔ so this needs no fourth
    /// vocabulary word, and the cell tells the truth about what the variable will start as.
    /// </summary>
    [Theory]
    [InlineData(typeof(int),   "0")]
    [InlineData(typeof(float), "0")]
    [InlineData(typeof(bool),  "false")]
    public void InPlanning_NoDefaultMeansZeroInitialised(Type clr, string expected)
        => Assert.Equal(expected, Formatter().Cell(Row("X", clr), VariableValueMode.Initial));

    /// <summary>
    /// ⛔ <b>The type cannot be resolved ⇒ <c>&lt;unreadable&gt;</c></b>, which is that word's
    /// documented meaning. ⭐ Not <c>(pending)</c>: nothing is pending, the declaration is broken.
    /// </summary>
    [Fact]
    public void InPlanning_AnUnresolvableTypeIsUnreadable()
        => Assert.Equal(VariableValueFormatter.Unreadable,
                        Formatter().Cell(Row("X", clr: null), VariableValueMode.Initial));

    /// <summary>
    /// ⭐⭐⭐ <b>THE HONESTY RULE Batch 82 established, preserved.</b> The three cells mean three
    /// different things and must not collapse into each other:
    /// <c>(pending)</c> = the run has not written it · <c>&lt;unreadable&gt;</c> = the bytes did not
    /// decode · a planning value = what it will START as.
    /// </summary>
    [Fact]
    public void PendingUnreadableAndInitialAreThreeDistinctCells()
    {
        var f = Formatter();

        // no bytes yet at runtime ⇒ (pending)
        var neverWritten = Row("A", typeof(int), hasEverBeenWritten: false);
        Assert.Equal(VariableValueFormatter.PendingFirstWrite,
                     f.Cell(neverWritten, VariableValueMode.Current));

        // bytes that will not decode ⇒ <unreadable>
        var undecodable = Row("B", typeof(int), bytes: new byte[] { 1, 2, 3 }, decodes: false);
        Assert.Equal(VariableValueFormatter.Unreadable,
                     f.Cell(undecodable, VariableValueMode.Current));

        // the SAME row read through the initial arm ⇒ its declared start, not either of the above
        var declared = Row("C", typeof(int), initialJson: "7", hasEverBeenWritten: false);
        Assert.Equal("7", f.Cell(declared, VariableValueMode.Initial));
    }

    /// <summary>
    /// ⭐ <b>The current arm is untouched by row 58.</b> ⛔ A regression here would mean the switch
    /// changed what "current" means rather than adding the other arm.
    /// </summary>
    [Fact]
    public void TheCurrentArmStillDecodesBytes()
    {
        var row = Row("Health", typeof(int), bytes: BitConverter.GetBytes(99));
        Assert.Equal("99", Formatter().Cell(row, VariableValueMode.Current));
    }

    /// <summary>⭐ ruling 4 — the tooltip is the same value, pretty-printed, in BOTH arms.</summary>
    [Fact]
    public void TheTooltipNamesTheArmItIsShowing()
    {
        var row = Row("Health", typeof(int), initialJson: "42");
        Assert.Contains("Initial value", Formatter().Tooltip(row, VariableValueMode.Initial),
                        StringComparison.Ordinal);
    }

    /// <summary>
    /// ⚠ <b>Malformed stored JSON is shown VERBATIM, not called unreadable.</b> ⛔ The compiler's
    /// <c>BP1674</c> is the authority on whether a default converts; the cell must not pre-empt that
    /// verdict — and a designer needs to SEE the text they have to fix.
    /// </summary>
    [Fact]
    public void MalformedDefaultJsonIsShownAsStored()
        => Assert.Equal("{oops", Formatter().Cell(Row("X", typeof(int), initialJson: "{oops"),
                                                  VariableValueMode.Initial));

    // ── helpers ─────────────────────────────────────────────────────────────

    private static VariableValueFormatter Formatter()
        => new((bytes, type) => bytes.Length == 4 && type == typeof(int)
                                    ? BitConverter.ToInt32(bytes)
                                    : (object?)null);

    private static VariableRow Row(
        string name, Type? clr, byte[]? bytes = null, string? initialJson = null,
        bool hasEverBeenWritten = true, bool decodes = true)
        => new(
            Origin:    new VariableRowOrigin(Guid.NewGuid(), default, "sec", name, "Asset"),
            ShortName: name,
            TypeText:  clr?.Name ?? "?",
            ClrType:   clr,
            ReadValue: () => bytes ?? Array.Empty<byte>(),
            HasEverBeenWritten: hasEverBeenWritten,
            ReadInitialJson: initialJson is null ? null : () => initialJson);

    private static Hrot.Editor.AiShared.Windows.PerspectiveWorkspaceRegistrar Registrar(
        DebugSessionRegistry? registry = null,
        Func<bool>?           isSimUp  = null,
        Func<bool>?           isFrozen = null)
        => new(
            perspectiveName: "BTree",
            selectionStore:  new Hrot.Editor.AiShared.Selection.EditorSelectionStore(),
            catalog:         new Hrot.Editor.AiShared.Catalog.AssetCatalog(),
            refactorService: new StubRefactor(),
            debugRegistry:   registry ?? new DebugSessionRegistry(),
            isSimUp:         isSimUp,
            isFrozen:        isFrozen);

    /// <summary>⭐ The proven shape from <c>DebugSessionRegistryTests</c> — a session that is simply
    /// attached. ⛔ Nothing here needs breakpoints; only ActiveSession's presence is under test.</summary>
    private sealed class StubSession : IAiDebugSession
    {
        public bool IsAttached { get; private set; } = true;
        public bool IsPaused => false;
        public Breakpoint? PausedAt => null;
        public Entity? PausedOnEntity => null;
        public bool IsAnyBreakpointActive => false;
        private Action? _onSessionStateChanged;
        event Action? IAiDebugSession.OnSessionStateChanged
        {
            add => _onSessionStateChanged += value;
            remove => _onSessionStateChanged -= value;
        }
        public void Detach() { IsAttached = false; }
        public BreakpointId SetBreakpoint(Guid assetId, Guid elementId) => default;
        public void ClearBreakpoint(BreakpointId id) { }
        public void ClearAllBreakpoints() { }
        public IReadOnlyList<Breakpoint> GetBreakpoints()
            => Array.Empty<Breakpoint>();
        public void Continue() { }
        public void Pause() { }
        public void StepOver() { }
        public void StepInto() { }
        public void StepOut() { }
        public void BeginObservingAsset(Guid assetId, TraceLevel level) { }
        public void EndObservingAsset(Guid assetId) { }
        public IReadOnlyList<Entity> GetActiveEntities(Guid assetId) => Array.Empty<Entity>();
    }

    private sealed class StubRefactor : Hrot.Editor.AiShared.Refactor.IRefactorService
    {
        public IReadOnlyList<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo> FindReferences(string k)
            => Array.Empty<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo>();
        public IReadOnlyList<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo> FindReferencesInAsset(Guid id)
            => Array.Empty<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo>();
        public Hrot.Editor.AiShared.Refactor.RefactorPreview PreviewRename(
            string f, string t, Hrot.Editor.AiShared.Refactor.RefactorOptions o)
            => new(f, t, Array.Empty<Hrot.Editor.AiShared.Refactor.RefactorFileEdit>(),
                   Array.Empty<Hrot.Editor.AiShared.Refactor.RefactorIssue>());
        public Hrot.Editor.AiShared.Refactor.RefactorResult ApplyRename(
            Hrot.Editor.AiShared.Refactor.RefactorPreview p) => new(true, Array.Empty<string>(), null);
        public Hrot.Editor.AiShared.Refactor.DeletePreview PreviewDelete(
            Guid id, Hrot.Editor.AiShared.Refactor.DeleteOptions o)
            => new(id, Array.Empty<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo>(),
                   Array.Empty<Hrot.Editor.AiShared.Refactor.RefactorIssue>());
        public Hrot.Editor.AiShared.Refactor.RefactorResult ApplyDelete(
            Hrot.Editor.AiShared.Refactor.DeletePreview p) => new(true, Array.Empty<string>(), null);
        public System.Threading.Tasks.Task<Hrot.Editor.AiShared.Refactor.RefactorPreview> PreviewRenameAsync(
            string f, string t, Hrot.Editor.AiShared.Refactor.RefactorOptions o,
            System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(PreviewRename(f, t, o));
        public System.Threading.Tasks.Task<Hrot.Editor.AiShared.Refactor.RefactorResult> ApplyRenameAsync(
            Hrot.Editor.AiShared.Refactor.RefactorPreview p, System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(ApplyRename(p));
    }
}
