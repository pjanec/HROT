using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Windows;
using Hrot.ClusterRunner.Integration.Tests;
using Hrot.Editor;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Smoke.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>G-c</c> — the PANEL GRAPH the harness never had.</b>
///
/// <para>📐 <b>Measured (<c>DESIGN_Smoke_Suite.md</c> §1):</b>
/// <c>search_graph / grep: PerspectiveWorkspaceRegistrar in EditorHarness → 0 hits</c>. ⇒
/// <c>EditorHarness</c> mirrors the RUNTIME wiring and builds <b>no window, no registrar, no
/// <c>VariableTableModel</c></b> — ⛔ <b>it cannot see what the user sees.</b> This class is that
/// half.</para>
///
/// <para>⭐⭐⭐ <b>T2 asserts on the ROW TEXT</b> — the string the renderer renders, produced by the
/// production <c>VariableValueFormatter</c>. ⛔ <b>No pixels, no Xvfb, no image drift</b>, and 📌 it is
/// the tier that catches <i>"the panel is wired to the wrong arm"</i>, which is the defect class this
/// programme keeps shipping.</para>
///
/// <para>⚠⚠ <b>WHICH LAYER THIS FAKES</b> *(📌 <c>M-29</c>, and it is the honest limit of this whole
/// item)*. The windows, the registrar, the row sources, the live provider and the formatter are all
/// PRODUCTION types wired through <c>PerspectiveWorkspaceServices.CreateRegistrar</c> — ⭐ the same
/// helper <c>EditorSubsystem</c> calls. ⛔ <b>But the ARGUMENTS are chosen here, not by
/// <c>EditorSubsystem</c></b>, because <c>EditorSubsystem</c> cannot be constructed headless. ⇒ 📌
/// <c>R-67</c> applies in full: <b>this cannot see a composition-root defect</b> — a service the real
/// root holds and forgets to pass is invisible from here, and that is the exact shape that has bitten
/// this programme nine times. ⭐ <c>TheCompositionRootHandsBlueprintALiveWriter</c> and the generic
/// forwarding rails remain the surface that covers it; ⛔ this does not replace them.</para>
/// </summary>
public sealed class EditorPanels : IDisposable
{
    private readonly IconAtlas     _atlas = new(new IntPtr(1), 256f, 256f, 16f);
    private readonly WindowManager _windows;
    private readonly BlueprintDebugSession _session;
    private readonly Guid _assetId;

    /// <summary>⭐ The perspective's own registrar — the object the composition root builds.</summary>
    public PerspectiveWorkspaceRegistrar Registrar { get; }

    /// <summary>⭐ The outline. An outline CLICK is what routes a section into the Details panel.</summary>
    public BlueprintMyBlueprintWindow Outline { get; }

    /// <summary>
    /// ⭐ The Details panel — <b>the SHARED shell</b>, as production registers it.
    /// <para>⚠ <b><c>S1</c> (<c>BP-399</c>, <c>2026-08-22</c>):</b> this used to be Blueprint's own
    /// <c>BlueprintDetailsWindow</c>. 📄 §7.3 ① retired that class; ⭐ the registrar now BUILDS this
    /// panel for every perspective, so the fixture no longer constructs one — ⛔ and could not, because
    /// a second window under <c>ai_details_blueprint</c> would collide.</para>
    /// </summary>
    public Hrot.Editor.AiShared.Windows.DetailsWindow Details => Registrar.Details!;

    /// <summary>⭐ The Watch — a PINNED view, so a row reaches it only by being pinned.</summary>
    public AiWatchWindow Watch => Registrar.Watch!;

    public EditorPanels(EditorHarness harness, BlueprintAsset asset, Entity entity)
    {
        _assetId = asset.AssetId;

        // ⭐⭐ The debug session over the HARNESS'S OWN WORLD — the panels and the sim must read one
        //    world, or T2 would assert against a value nothing produced.
        _session = new BlueprintDebugSession(
            harness.BlueprintRegistry, harness.Repo, new SmokeTimeController());

        var sessions = new DebugSessionRegistry();
        sessions.SetActiveSession(_session);

        // ⭐ The selection the user would have made: this asset is open, this entity is selected.
        //   📌 R-78 — a Details row's origin carries `entity: default` and MEANS "whoever is
        //   selected", so the store is what makes the live read concrete.
        // ⚠ The store holds an IEditableAsset and BlueprintAsset is not one — 📌 the SAME vocabulary
        //   asymmetry BP-355 / 95a name. ⭐ Production bridges it with this adapter; the smoke fixture
        //   uses the production adapter rather than a lookalike.
        var editable = new Hrot.Blueprints.Editor.Variables.BlueprintEditableAssetAdapter(asset);
        var store = new Hrot.Editor.AiShared.Selection.EditorSelectionStore
            { ActiveAsset = editable, SelectedEntity = entity };

        // ⭐⭐ The READ side, built exactly as EditorSubsystem:2270 builds it.
        var liveValues = new BlueprintLiveValueProvider(
            readerFactory: () => sessions.ActiveSession is IBlueprintDebugSession bp
                ? (self, id) => Hrot.Blueprints.Editor.Inspector.BlueprintRuntimeInspectorPane
                                    .ResolveInspectorSnapshot(bp, self, id)
                : null,
            store: store);

        var services = new PerspectiveWorkspaceServices(
            new AssetCatalog(),
            new NoRefactor(),
            sessions,
            // ⭐ A REAL edit service, because the registrar builds the row gestures and the edit dialog
            //   from it. ⛔ Not null: a smoke fixture that quietly dropped the gesture layer would look
            //   identical to one whose gestures are broken — the exact confusion 102b just removed.
            new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
            // ⭐ The sim IS up in a smoke scenario, and it is deterministic ⇒ the Value column reads
            //   the LIVE arm. ⛔ Not "paused": the run state decides which arm the panel shows, and
            //   claiming the wrong one here would make T2 assert on the initial value instead.
            isSimUp:  () => true,
            isFrozen: () => false)
        {
            // ⚠⚠ REQUIRED, and for a reason worth naming: the registrar creates the Watch window ONLY
            //    when a breakpoint manager is present (`if (breakpointManager != null)`). ⇒ ⛔ a smoke
            //    fixture without one has NO Watch, and T2's whole point is comparing the two panels.
            // ⭐ The smoke scenario sets no breakpoints, so this one is inert and says so.
            BreakpointManager = new NoBreakpoints(),
        };

        Registrar = services.CreateRegistrar(
            "Blueprint", store,
            validators:        Array.Empty<IAssetValidator>(),
            liveValueProvider: liveValues);

        _windows = new WindowManager(_atlas);

        Outline = new BlueprintMyBlueprintWindow();
        // ⭐ The outline learns the open document the way the editor tells it: Retarget. ⛔ Not by
        //   reaching into its model.
        Outline.Retarget(
            editableAsset:  editable, blueprintAsset: asset,
            hostServices:   null, commands:       null);

        // ⭐⭐ RegisterExtraWindow is what CONNECTS the outline to the Details panel and installs the
        //    run-state source — 📌 the registrar's own remark: "batches 79/80/81 each lost a surface to
        //    a 'someone must remember to wire it' seam." ⛔ So the smoke fixture must not wire them by
        //    hand either: it registers them, exactly as the composition root does.
        // ⚠ S1: the Details panel is no longer CONSTRUCTED here — the registrar builds one for every
        //   perspective (§7.3 ①). ⭐ It is still registered through the same call, so this fixture keeps
        //   exercising the same path production does; the registrar's own guards make the second pass
        //   idempotent (view sources, properties hosts and gesture attachment are all keyed).
        Registrar.RegisterExtraWindow(_windows, Outline);
        Registrar.RegisterExtraWindow(_windows, Details);
    }

    /// <summary>⭐ True once the registrar joined the outline to the Details panel. ⛔ A rail surface on
    /// the CONSTRUCTED object, not on this file's source.</summary>
    public bool OutlineIsRoutedToDetails => Registrar.OutlineIsRoutedToDetails;

    /// <summary>
    /// ⭐⭐ <b>The user's click:</b> select <paramref name="variable"/> in the outline, which routes a
    /// row list into the Details panel.
    /// </summary>
    public void SelectInOutline(string variable)
    {
        var item = Outline.Model
            .GetItems(BlueprintMyBlueprintModel.SectionVariables)
            .FirstOrDefault(i => string.Equals(i.DisplayName, variable, StringComparison.Ordinal));

        if (item is null)
            throw new InvalidOperationException(
                $"The outline has no variable named '{variable}' to click. Items present: "
              + string.Join(", ", Outline.Model
                    .GetItems(BlueprintMyBlueprintModel.SectionVariables)
                    .Select(i => i.DisplayName)));

        // ⭐⭐ PublishSelection is the ONE call the panel's own ImGui click path makes — 📌 its remark:
        //    "the ONE place that resolves and raises so a panel rebuild cannot double-fire."
        Outline.PublishSelection(item);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>T2 — what the DETAILS table would render for <paramref name="variable"/>.</b>
    /// ⛔ Not the raw value: the string, through the production formatter and the production run-state
    /// arm, which is where a wrong-arm defect actually shows.
    /// </summary>
    public string DetailsRowText(string variable)
    {
        SyncPanels();
        return RowText(Details.Variables.Model, variable);
    }

    /// <summary>⭐⭐ <b>T2 — what the WATCH would render.</b> 📌 Defect #4 was <c>"0"</c> here while the
    /// Details panel showed the truth, which is why both are asserted and compared.</summary>
    public string WatchRowText(string variable)
    {
        SyncPanels();
        return RowText(Watch.Variables!, variable);
    }

    // ── U-obs-4 — T2 THROUGH THE SHARED SNAPSHOT ───────────────────────────────────────────────
    //
    // ⭐⭐⭐ 📄 DESIGN_Smoke_Suite.md G-c/S3, SUPERSEDED 2026-08-22: "T2 now reads the shared
    //    PanelSnapshot singleton — panels register their view-model there and the harness reads it."
    //
    // ⭐⭐ THE DESIGN CALL THE HANDOFF FLAGGED — "call BuildViewModel directly, vs run a headless
    //    frame". 📐 Measured, it is NEITHER:
    //      ⛔ A headless FRAME is impossible: ManagedWindow.Render calls Gui.Begin (:202) before
    //         DrawClientArea (:221), so the draw override is unreachable without a live context.
    //      ⛔ Calling BuildViewModel from the fixture would make the FIXTURE choose the panel's
    //         address, kind and capture gating — i.e. a second implementation of the identity rules,
    //         which is the duplication the observability design removes.
    //      ⭐ So: drive the panel's OWN publish hook (DrawContent / SimulateDrawClientArea), which
    //         every converted panel exposes for exactly this. The fixture then reads what the PANEL
    //         wrote, not a lookalike it assembled.
    //
    // ⚠⚠ WHAT THIS DOES NOT COVER, stated rather than implied: the DETAILS panel publishes its SHELL
    //    model (chosen view, empty state, counts) and its variables VIEW publishes HasContent/Heading
    //    — ⛔ neither carries ROWS. ⇒ T2-via-snapshot is available for the WATCH today and not for the
    //    Details table; the direct-model assertions below stay until that gap closes. Filed as a
    //    finding rather than worked around here.

    /// <summary>
    /// ⭐⭐⭐ <b>T2 via the snapshot: the value the WATCH published for <paramref name="variable"/>,
    /// read back out of <c>PanelSnapshot</c>.</b>
    /// ⛔ Returns <c>null</c> when the panel published no row for it — ⚠ distinguishable from a row
    /// whose value is the empty string, which is a different claim.
    /// </summary>
    public string? WatchValueFromSnapshot(string variable)
    {
        SyncPanels();

        // ⭐ The panel's OWN publish path. ⛔ Not a fixture-built view-model.
        Watch.DrawContent();

        var vm = Fdp.Diagnostics.Contracts.Panels.PanelSnapshot.TryGet(Watch.Id);
        if (vm is null)
            throw new InvalidOperationException(
                "The Watch published nothing to PanelSnapshot. Instrumented panels: "
              + string.Join(", ", Fdp.Diagnostics.Contracts.Panels.PanelSnapshot.RegisteredPanels)
              + " | captured: "
              + string.Join(", ", Fdp.Diagnostics.Contracts.Panels.PanelSnapshot.CapturedPanels));

        var rows = vm.Dump()["rows"]!.AsArray();
        foreach (var row in rows)
        {
            if (row is null) continue;
            if (!string.Equals(row["shortName"]?.GetValue<string>(), variable, StringComparison.Ordinal))
                continue;
            return row["value"] is null ? null : row["value"]!.GetValue<string>();
        }
        return null;
    }

    /// <summary>⭐ The kind the Watch published under — what a cross-host conformance diff groups by.</summary>
    public string? WatchKindFromSnapshot()
        => Fdp.Diagnostics.Contracts.Panels.PanelSnapshot.TryGet(Watch.Id)?.PanelKind;

    /// <summary>
    /// ⭐⭐⭐ <b><c>BP-484</c> — T2 via the snapshot for the DETAILS table too.</b>
    /// ⚠ The address is the hosted view's own sub-address; ⭐ the fixture composes it from the SAME
    /// public constant the view uses, ⛔ never a repeated literal.
    /// </summary>
    /// <summary>⭐ The kind the Details TABLE published under — see the rail that asserts it.</summary>
    public string? DetailsTableKindFromSnapshot()
        => Fdp.Diagnostics.Contracts.Panels.PanelSnapshot.TryGet(DetailsTableAddress())?.PanelKind;

    private string DetailsTableAddress()
        => $"{Details.VariablesIdScope}/"
         + $"{Hrot.Editor.AiShared.Shell.VariablesDetailsViewDescriptor.ViewId}/table";

    public string? DetailsValueFromSnapshot(string variable)
    {
        SyncPanels();
        Details.SimulateDrawClientArea();

        var address = DetailsTableAddress();

        var vm = Fdp.Diagnostics.Contracts.Panels.PanelSnapshot.TryGet(address);
        if (vm is null)
            throw new InvalidOperationException(
                $"The Details table published nothing at '{address}'. Captured: "
              + string.Join(", ", Fdp.Diagnostics.Contracts.Panels.PanelSnapshot.CapturedPanels));

        foreach (var row in vm.Dump()["rows"]!.AsArray())
        {
            if (row is null) continue;
            if (!string.Equals(row["shortName"]?.GetValue<string>(), variable, StringComparison.Ordinal))
                continue;
            return row["value"] is null ? null : row["value"]!.GetValue<string>();
        }
        return null;
    }

    /// <summary>⭐ Pins the Details row into the Watch — the gesture a designer makes. ⛔ The Watch is a
    /// PINNED view; nothing appears in it by itself.</summary>
    public void PinToWatch(string variable)
    {
        var row = Row(Details.Variables.Model, variable);
        Watch.Pinned.Pin(row);
    }

    /// <summary>⭐ The live value the DEBUG SESSION reports — <b>T1</b>. ⛔ This class does not compute a
    /// slot offset of its own; 📌 <c>Q32</c> §2.1, and a smoke test that did would agree with a broken
    /// resolver.</summary>
    public int LiveCount()
    {
        var snapshot = _session.CaptureLiveState(Registrar.SelectionStore.SelectedEntity!.Value, _assetId);
        if (snapshot is null)
            throw new InvalidOperationException(
                "The debug session reports no blueprint state for the smoke entity at all — "
              + "the attach did not take, or the slot is gone.");

        return snapshot.FieldValues.TryGetValue("Count", out var value)
            ? Convert.ToInt32(value)
            : throw new InvalidOperationException(
                "The live snapshot has no 'Count' field. Keys present: "
              + string.Join(", ", snapshot.FieldValues.Keys));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE ONE THING THE DRAW LAYER DOES THAT THIS FIXTURE MUST DO ITSELF</b> *(📌 <c>M-29</c>
    /// — name the faked layer)*.
    ///
    /// <para>📐 Both panels push the run state into their model from inside <c>Draw</c>
    /// *(<c>VariableDetailsSection:157</c>, and <c>AiWatchWindow.SyncRunState</c>, which Batch 100e made
    /// public for exactly this reason)*. ⇒ ⛔ a headless reader that skipped it would see
    /// <c>RunState = Planning</c>, render the INITIAL arm, and report the declaration's
    /// <c>DefaultValueJson</c> — <b><c>"0"</c> for <c>Count4</c></b> — <b>while the sim held 11</b>.</para>
    ///
    /// <para>⚠ <b>That is not hypothetical: it is what this rail printed on its first run</b>, and it
    /// is worth recording — ⭐ <b>a panel that never syncs shows a plausible number, not an error.</b>
    /// ⛔ The fixture drives the same public method the frame does; it does not set
    /// <c>model.RunState</c> behind the panel's back.</para>
    /// </summary>
    private void SyncPanels()
    {
        Details.Variables.SyncRunState();
        Watch.SyncRunState();
    }

    private string RowText(VariableTableModel model, string variable)
        => Registrar.ValueFormatter.Cell(Row(model, variable),
                                         VariableValue.ModeFor(model.RunState));

    private static VariableRow Row(VariableTableModel model, string variable)
    {
        var rows = model.Build().AllRows;
        var row  = rows.FirstOrDefault(r => string.Equals(r.ShortName, variable, StringComparison.Ordinal));
        if (row is null)
            throw new InvalidOperationException(
                $"The panel has no row named '{variable}'. Rows present: "
              + (rows.Count == 0 ? "(none)" : string.Join(", ", rows.Select(r => r.ShortName))));
        return row;
    }

    public void Dispose()
    {
        _atlas.Dispose();
    }

    /// <summary>⭐ The smoke scenario drives time through the HARNESS's controller; the debug session
    /// needs one only to satisfy its constructor and never steps it. ⛔ Deliberately inert, and it says
    /// so rather than pretending to control anything — ⚠ the scenario is FREE-RUNNING, which is what
    /// makes the Value column read the live arm.</summary>
    private sealed class SmokeTimeController : IEngineDebugTimeController
    {
        public bool IsPausedByDebugger => false;
        public void RequestPause() { }
        public void RequestResume() { }
        public void RequestStepOneTick() { }
    }

    /// <summary>
    /// ⛔ <b>An inert breakpoint manager.</b> ⭐ The smoke scenario never pauses and never stages a
    /// mutation — ⚠ it exists because the registrar gates the Watch WINDOW on the manager's presence,
    /// so without one the panel under test would simply not be built.
    /// ⛔ <c>StageFieldMutation</c> THROWS rather than swallowing: a smoke test that silently discarded
    /// a write would be the exact "looks wired, does nothing" shape this suite exists to catch.
    /// </summary>
    private sealed class NoBreakpoints : Hrot.Diagnostics.Breakpoints.IDataBreakpointManager
    {
        public void StageFieldMutation(Entity e, Type t, int off, ReadOnlySpan<byte> b)
            => throw new InvalidOperationException(
                "The smoke scenario staged a live write. It runs free — nothing here can apply it.");
        public void StageMutation(Entity e, Type t, object v)
            => throw new InvalidOperationException("Ruling 14: never the whole-component path.");

        public Hrot.Diagnostics.Breakpoints.BreakpointId Add(
            Hrot.Diagnostics.Breakpoints.Breakpoint breakpoint) => default;
        public Hrot.Diagnostics.Breakpoints.BreakpointId AddBreakpoint(
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto condition, Entity? filter = null,
            int occurrenceThreshold = 1, string displayName = "", Guid? sourceElementId = null) => default;
        public void Remove(Hrot.Diagnostics.Breakpoints.BreakpointId id) { }
        public void SetEnabled(Hrot.Diagnostics.Breakpoints.BreakpointId id, bool enabled) { }
        public void UpdateCondition(Hrot.Diagnostics.Breakpoints.BreakpointId id,
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto? condition) { }
        public void MarkAsWatch(Hrot.Diagnostics.Breakpoints.BreakpointId id, bool isWatch) { }
        public void SaveWatches(string path) { }
        public void LoadWatches(string path) { }
        public void OnHotReloadCompleted() { }
        public void OnHotReloadBegin() { }
        public void OnHit(Hrot.Diagnostics.Breakpoints.Breakpoint bp, Entity entity) { }
        public void RequestStep() { }
        public void RequestContinue() { }
        public void OnExternalHit(string tag, Entity entity) { }
        public event Action<Hrot.Diagnostics.Breakpoints.Breakpoint, Entity>? OnBreakpointHit
            { add { } remove { } }
        public event Action<bool>? OnPauseStateChanged { add { } remove { } }
        public bool IsPaused => false;
        public Fdp.ModuleHost.Abstractions.ISimulationView ActiveView => null!;
        public long PausedTick => 0;
        public int PendingMutationsCount => 0;
        public IReadOnlyList<Hrot.Diagnostics.Breakpoints.Breakpoint> AllBreakpoints
            => Array.Empty<Hrot.Diagnostics.Breakpoints.Breakpoint>();
        public bool HasMountedDelegates => false;
        public bool HasStatefulTrackers => false;
        public void EvaluateStatefulBreakpoints(EntityRepository repo) { }
        public IReadOnlyList<(Hrot.Diagnostics.Breakpoints.Breakpoint Breakpoint,
            Hrot.Diagnostics.Breakpoints.CompiledComponentPredicate Compiled)> MountedComponentPredicates
            => Array.Empty<(Hrot.Diagnostics.Breakpoints.Breakpoint,
                            Hrot.Diagnostics.Breakpoints.CompiledComponentPredicate)>();
        public IReadOnlyList<(Hrot.Diagnostics.Breakpoints.Breakpoint Breakpoint,
            Hrot.Diagnostics.Breakpoints.CompiledEventScanner Scanner)> MountedEventScanners
            => Array.Empty<(Hrot.Diagnostics.Breakpoints.Breakpoint,
                            Hrot.Diagnostics.Breakpoints.CompiledEventScanner)>();
    }

    /// <summary>⛔ No refactor service in a smoke run — nothing renames anything. ⚠ Required non-null by
    /// PerspectiveWorkspaceServices, deliberately: "I forgot" and "there are none" used to look
    /// identical.</summary>
    private sealed class NoRefactor : Hrot.Editor.AiShared.Refactor.IRefactorService
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
