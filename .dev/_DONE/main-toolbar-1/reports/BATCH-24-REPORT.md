# BATCH-24 Report

## Implementation Summary

Wired three main toolbar groups into `EditorSubsystem.RegisterWindows` to fix BUG-2 (empty toolbar band). All wiring is null-safe so `RegisterWindows` does not throw on a bare `new EditorSubsystem()`.

### A. Perspective group (§8) — sortOrder=20
- `PerspectiveToolbarSection` self-registers `"PerspectiveGroup"` into `MainToolbar` at sortOrder 20 with 64f height.
- Constructed with `SilkIconProvider(windowManager.Atlas)` for icon resolution.
- Field `_perspectiveToolbarSection` pins the instance against GC.

### B. AI-debug group (§9) — sortOrder range 40–45
- `AiDebugCommands.Register(windowManager.ShellCommands.Register, debugRegistry)` registers the 6 shell commands (Continue/StepOver/StepInto/StepOut/Pause/StepBack) once.
- `ToolbarCommandAdapter.Register` creates a toolbar entry per command at ascending sortOrders (40–45).
- Separator `"ToolbarSep_PerspToAiDebug"` at sortOrder=30 divides Perspective from AI-debug.

### C. Time-control group (§7) — sortOrder=0
- Created `EditorTimeTransportFacade : ITimeTransportFacade` (public, in `Hrot.Editor.UI`) adapting the editor's `IPreviewController` + `MasterSyncController` + `EntityRepository` to the `ITimeTransportFacade` contract required by `MainToolbarTimeControlSection`.
- `MainToolbarTimeControlSection(facade)` renders play/pause, step, stop buttons + time readout + rate selector.
- Registered as `"TimeControlGroup"` at sortOrder=0 with 64f declared height.
- Separator `"ToolbarSep_TimeToPersp"` at sortOrder=10 divides Time from Perspective.
- Wired inside the existing `if (_previewController != null && _timeController != null && _world != null)` guard — null-safe.

### Separator plan (sortOrder):
| ID | sortOrder | Between |
|----|-----------|---------|
| `TimeControlGroup` | 0 | (entries) |
| `ToolbarSep_TimeToPersp` | 10 | Time → Perspective |
| `PerspectiveGroup` | 20 | (entries) |
| `ToolbarSep_PerspToAiDebug` | 30 | Perspective → AI-debug |
| AI-debug entries | 40–45 | (entries) |

### Guardrail test
`EditorSubsystem_RegisterWindows_PopulatesMainToolbar`: creates `new EditorSubsystem()` (bare, no `Initialize`), calls `RegisterWindows(wm)`, asserts `wm.MainToolbar.Height > 0`. Verifies the method does not throw and the toolbar is populated on the bare-subsystem path.

## Design Decisions

1. **EditorTimeTransportFacade mirrors EditorTimeTransportAdapter.** The existing internal `EditorTimeTransportAdapter` already does exactly what we need, but is `internal sealed`. Rather than changing its visibility (which would be a separate refactor touching all its callers), a new public `EditorTimeTransportFacade` was created with the same adapter logic.

2. **Placement of Perspective+AI-debug wiring before the `_editorLogic` guard.** The Perspective and AI-debug groups only need `WindowManager` (always available) and `debugRegistry` (a local variable). Placing them before the early-return guard ensures they register even in the bare-subsystem test path.

3. **Time-control group inside the `_previewController`/`_timeController`/`_world` null guard.** These three fields are null in the bare-subsystem path; the time-control toolbar group only wires when all three are non-null (production Initialize path).

## Deviations

None. All three groups (A, B, C) were fully implemented per spec.

## Test Results

All run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`.

### EditorSubsystemBlueprintWindowsTests — 9/9 passed
```
Passed! - Failed: 0, Passed: 9, Skipped: 0, Total: 9
```
Includes the 8 existing tests (perspectives, canvas windows, My Blueprint/Details/Variables) + the new `EditorSubsystem_RegisterWindows_PopulatesMainToolbar`.

### Hrot.Blueprints.Tests (Stability filter) — exactly 9 PRE-1 failures
```
Failed! - Failed: 9, Passed: 1854, Skipped: 8, Total: 1871
```
The 9 failing tests (all pre-existing, none related to BATCH-24):
1. `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource("MoveToAndFire")` — golden source snapshot
2. `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource("HasVisibleTarget")` — golden source snapshot
3. `Stage8Tests.Stage8_PdbContainsEmbeddedSource` — PDB embedding
4. `Stage8Tests.Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb` — Roslyn compiler output
5. `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes` — zero-alloc GC measurement
6. `MoveToAndFireDemoTests.MoveToAndFire_GeneratedSource_Snapshot` — generated source snapshot
7. `CF2_AuthoredIdProbeTests.CF2_EndToEnd_DelayBreakpointPauses` — debug breakpoint end-to-end
8. `CF7rev_EndToEndTests.SetBreakpoint_TriggersAutoInstrument_ThenPauses` — auto-instrument breakpoint
9. `WhenNodePerfTests.WhenNode_ZeroAllocOnHotPath` — hot-path allocation benchmark

### Hrot.Editor.Tests (Stability filter) — 0 failed
```
Passed! - Failed: 0, Passed: 176, Skipped: 0, Total: 176
```

### Fdp.Presentation.Tests (toolbar class filter) — 0 failed
```
Passed! - Failed: 0, Passed: 31, Skipped: 0, Total: 31
```

### Fdp.Toolkits.Tests (Stability filter) — 1 pre-existing flaky failure
```
Failed! - Failed: 1, Passed: 1855, Skipped: 0, Total: 1856
```
Sole failure: `AllReaders_ZeroAlloc_After1MillionCalls` — a pre-existing flaky GC-allocation benchmark (allocated 6776 bytes vs expected 0). Passes in isolation. Completely unrelated to BATCH-24.

### Hrot.SimHost.Tests (Stability filter) — 0 failed
```
Passed! - Failed: 0, Passed: 585, Skipped: 3, Total: 588
```

### Compilation
- `Hrot.Editor` — 0 warnings, 0 errors
- `Hrot.Blueprints.Tests` — 9 pre-existing warnings, 0 errors
- No new warnings introduced by BATCH-24

## Developer Insights

- The existing `EditorTimeTransportAdapter` (internal, same logic) could be retired in favor of the new public facade in a follow-up refactor, reducing duplication.
- `MainToolbarManager` is in a nullable-oblivious assembly (`Fdp.Presentation`), requiring explicit `!= null` guards even though the property is initialized as `new()` and never set to null. The same pattern already exists at line ~2917 in the Perspective wiring.
- The `debugRegistry` local variable (line ~1902) is reused directly for AI-debug command registration — no new registry instance is created, so the toolbar commands share the same session tracking as the rest of the AI editor infrastructure.
- `SilkIconProvider` construction takes `IconAtlas` only (no GPU calls), so it's safe to create in `RegisterWindows` even in headless/test paths.

## Known Issues

- The `EditorTimeTransportFacade` duplicates the logic of the internal `EditorTimeTransportAdapter`. The internal adapter could be retired in a future cleanup batch.
- The `AllReaders_ZeroAlloc_After1MillionCalls` flaky test in Fdp.Toolkits.Tests is not catalogued in TEST-HEALTH.md and should be added as a `Flaky` entry.

## Suggested Commit Message

```
feat(main-toolbar): wire Perspective, AI-debug, and Time-control toolbar groups (BATCH-24)

Co-Authored-By: Claude <noreply@anthropic.com>
```
