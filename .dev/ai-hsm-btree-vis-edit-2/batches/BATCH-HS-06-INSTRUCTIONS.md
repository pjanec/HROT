# BATCH-HS-06 — Validation surfacing (Diagnostics + node state + region-conflict overlay)  **[VISUAL GATE]**

**Task:** TASK-HS-06. **One task, three cohesive parts:** (a) register `HsmAssetValidator` so HSM diagnostics appear in the Diagnostics window (mirror BT-04); (b) drive `StateNode` node-state/tooltip from per-state diagnostics (mirror BT-05); (c) feed `HsmRegionConflictsRenderer.SetDiagnostics(...)` after each validation run (the renderer is complete but currently never fed).

Design ref: TASK-DETAIL.md §TASK-HS-06; Forward-Plan §5 (EH-03); host doc §12/§15.3.

## Working agreement (MANDATORY — restated)
1. **One task per batch.** Touch ONLY the files listed below. In `EditorSubsystem.cs` make ONLY the single HSM-registrar `validators:` addition described — change nothing else in that file. Do NOT touch the command sink, other renderers, BTree code, or other workstreams.
2. **No cheating to pass.** No suppressing diagnostics, commenting out code, weakening asserts, excluding files. If blocked, STOP + write the blocker.
3. **Finish without asking** — build + test until `Failed: 0`, then report.
4. **Headless only** — you make the LOGIC headless-testable (node-state projection, diagnostics feed). The pixel overlay (yellow conflict line + "!" glyph) is the lead's visual gate.
5. **Tests assert behavior** (NodeState enums, tooltip content, diagnostic codes present, feed received), not strings-in-generated-text. 6. **Litter-free.** 7. **Report = truth.**

## Files
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` — add diagnostic fields to `StateNode`; update `State`/`StatusTooltip`.
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmGraphModel.cs` — run validation in `BuildCaches`, project onto states, expose `LastDiagnostics` + a `DiagnosticsRecomputed` event.
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmRegionConflictsRenderer.cs` — add a tiny test-visible getter for the fed diagnostics.
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmDocumentFactory.cs` — wire the renderer to the graph model's diagnostics.
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — add `HsmAssetValidator` to the `_hsmRegistrar` constructor's `validators:` argument (ONE change).
- Tests: `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/` — new file(s).

## Part (a) — Register HsmAssetValidator  (`EditorSubsystem.cs`, ~line 1959)
The BTree registrar (~line 1946) passes `validators: new IAssetValidator[] { new BTreeAssetValidator(new BTreeValidator()) }`. The HSM registrar (`_hsmRegistrar = new PerspectiveWorkspaceRegistrar("HSM", …)`) currently omits `validators:`. Add it, constructing `new Hrot.Hsm.Editor.Validation.HsmAssetValidator(<schemaExporter>)` — use the SAME `IActionSchemaExporter` instance the BTree registrar two lines above receives (read those lines and reuse that exact variable). Add nothing else.

## Part (b) — StateNode node-state from diagnostics

`StateNode.State` is currently `IsBreakpoint ? Warning : Normal` and `StatusTooltip => null`. Add ephemeral (NOT persisted) fields and make the getters consult them WITHOUT losing the breakpoint behavior:
```csharp
// Editor-only ephemeral (not persisted) — set by HsmGraphModel from validation each rebuild.
public NodeState? DiagnosticState;
public string?    DiagnosticTooltip;

public NodeState State => DiagnosticState ?? (IsBreakpoint ? NodeState.Warning : NodeState.Normal);
public string?   StatusTooltip => DiagnosticTooltip;
```
**Critical:** `DiagnosticState` MUST be nullable and default null. When a state has no diagnostic it must stay null (so the breakpoint fallback still works) — do NOT set it to `NodeState.Normal`.

In `HsmGraphModel.BuildCaches` (mirror `BTreeGraphModel.BuildCaches` lines 224–254 but keyed on `StableId`, and note one diagnostic can carry MULTIPLE `TargetStableIds`):
```csharp
private void BuildCaches()
{
    _linkCache.Clear();

    // Run validation once (include blackboard for region-conflict checks).
    var diagnostics = new HsmValidator().Validate(_asset, _asset as IBlackboardManagedAsset);

    // Map StableId -> worst (Error-wins) severity + message.
    var perState = new Dictionary<Guid, (NodeState State, string Tooltip)>();
    foreach (var d in diagnostics)
    {
        NodeState sev = d.Severity switch
        {
            HsmDiagnosticSeverity.Error   => NodeState.Error,
            HsmDiagnosticSeverity.Warning => NodeState.Warning,
            _                             => NodeState.Normal,
        };
        if (sev == NodeState.Normal) continue;
        foreach (var id in d.TargetStableIds)
        {
            if (!perState.TryGetValue(id, out var ex))
                perState[id] = (sev, d.Message);
            else if (sev == NodeState.Error && ex.State != NodeState.Error)
                perState[id] = (sev, d.Message);
        }
    }

    // Project onto states; RESET to null when no diagnostic (preserves breakpoint state).
    foreach (var s in _asset.AllStates)
    {
        if (perState.TryGetValue(s.StableId, out var diag))
        { s.DiagnosticState = diag.State; s.DiagnosticTooltip = diag.Tooltip; }
        else
        { s.DiagnosticState = null; s.DiagnosticTooltip = null; }
    }

    foreach (var t in _asset.AllTransitions)
        _linkCache[new LinkId(t.VisualId)] = new HsmTransitionLink(t);

    LastDiagnostics = diagnostics;
    DiagnosticsRecomputed?.Invoke(diagnostics);
}
```
Add to `HsmGraphModel`:
```csharp
public IReadOnlyList<HsmDiagnostic> LastDiagnostics { get; private set; } = Array.Empty<HsmDiagnostic>();
public event Action<IReadOnlyList<HsmDiagnostic>>? DiagnosticsRecomputed;
```
(Confirm `HsmValidator` / `HsmDiagnostic` / `HsmDiagnosticSeverity` namespaces; add usings as needed.)

## Part (c) — Feed HsmRegionConflictsRenderer

1. In `HsmRegionConflictsRenderer`, add a test-visible getter (the field `_diagnostics` already exists):
   ```csharp
   internal IReadOnlyList<HsmDiagnostic>? CurrentDiagnostics => _diagnostics;
   ```
2. In `HsmDocumentFactory` where the graph model and the renderer list are built (the `HsmRegionConflictsRenderer` is created in `BuildRenderers`, ~line 175), after both exist, wire them — keep the model decoupled (it only emits diagnostics):
   ```csharp
   // Feed region-conflict diagnostics to the renderer on every validation rebuild.
   var regionConflicts = /* the HsmRegionConflictsRenderer instance in the list */;
   graphModel.DiagnosticsRecomputed += regionConflicts.SetDiagnostics;
   regionConflicts.SetDiagnostics(graphModel.LastDiagnostics); // initial push
   ```
   Find the actual construction order; if the renderer is created inside a helper that returns the list, capture the instance (e.g., hold a local when adding it to the list) and do the wiring where both `graphModel` and that instance are in scope. Do NOT make `HsmGraphModel` reference the renderer type.

## Tests (`Hrot.Hsm.Editor.Tests`, new file(s))
Use the direct-asset-construction pattern. Assert VALUES:
1. **Node-state Error:** build a composite state with children but NO child marked `IsInitial` (triggers `CompositeWithoutInitialChild`, Error, targeting that state's StableId). Construct `HsmGraphModel(asset)`; assert that composite's `State == NodeState.Error` and `StatusTooltip` is non-null/contains the message.
2. **Clean state Normal:** a valid simple state → `State == NodeState.Normal`, `StatusTooltip == null`.
3. **Breakpoint preserved:** a state with `IsBreakpoint=true` and NO diagnostic → `State == NodeState.Warning` (DiagnosticState stayed null).
4. **LastDiagnostics + event:** `graphModel.LastDiagnostics` is non-empty for the broken machine; subscribing to `DiagnosticsRecomputed` and triggering a rebuild (mutate via the asset's `MarkDirty`/a sink command) fires with the diagnostics.
5. **Region-conflict reaches renderer:** build a parallel state whose regions write the same output lane (triggers `OutputLaneConflict`) — OR if that setup is impractical, assert the wiring directly: `renderer.SetDiagnostics(list); renderer.CurrentDiagnostics.Should().BeSameAs(list)`, AND that `graphModel.LastDiagnostics` contains a diagnostic with `Code == OutputLaneConflict` for the conflict asset. (Pixel rendering of the "!" glyph = visual gate.)
6. **(a) registration smoke (if feasible headlessly):** `new HsmAssetValidator(null).SupportedKind == AssetKind.Hsm` and `Validate(brokenHsmAsset)` returns ≥1 `AssetDiagnostic`. (The registrar wiring itself is composition-root glue verified by build.)

## Verification (no regenerate env var)
```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj   # covers EditorSubsystem.cs
dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj
dotnet test  Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests
```
Must end `Failed: 0`, 0 build errors. Baseline before this batch: 423 passed. List pre-existing failures; confirm 0 new. If building `Hrot.Editor` surfaces unrelated pre-existing errors, note them but ensure YOUR change compiles.

## Report → `.dev/ai-hsm-btree-vis-edit-2/reports/BATCH-HS-06-REPORT.md`
The registrar change (exact line + exporter var); the StateNode diagnostic fields + getters; the BuildCaches projection; the renderer feed wiring (where, decoupled); test names + assertions; before/after counts; any pre-existing Hrot.Editor build issues; anything not done. Do not commit.
