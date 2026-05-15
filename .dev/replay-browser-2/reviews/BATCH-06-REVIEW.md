# BATCH-06 Review

**Reviewer:** Dev Lead  
**Date:** 2026-05-15  
**Verdict:** APPROVED

---

## Summary

All 9 tasks (A–I) implemented. The 6 required tests (SR-T28, SR-T29, SR-T32 x4, SR-T33 x3,
SR-T39 x4, FND-T11 x4) pass. Pre-existing failures (Vis2D DebugGizmoLayer x7, DataDrivenGizmoPredicate
D003 x2) are unchanged. BATCH-06 is the final code batch — all Stage 4 UI tasks and Stage 5
registration are complete.

---

## Scope Check

| Task | Expected | Delivered |
|------|----------|-----------|
| A — `ISpatialPickerContext` | New interface in Fdp.Presentation | Done |
| B — `ComponentEditDrawer` bbox picker block | Optional ctor param + `DrawLeafNode` block | Done |
| C — `BoundingBoxFieldDrawer` | Two `DragFloat2` rows | Done |
| D — `BehaviorHashFieldDrawer` | Registry-backed int combo | Done |
| E — `FilteredTypeComboFieldDrawer` | OrdinalIgnoreCase filter + both modes | Done |
| F — `ReplaySearchPanel` full impl | All 6 modes, preset I/O, background search, results grid | Done |
| G — `WireDelegates` update | 4-arg `ReplaySearchPanel` ctor with real services | Done |
| H — RB-5.1 ClusterRunner reference | `ProjectReference` added | Done |
| I — Tests SR-T28, SR-T29, SR-T32, SR-T33, SR-T39, FND-T11 | All pass | Done |

---

## Design Alignment

### ISpatialPickerContext (Task A)

Matches spec exactly — `RequestBoundingBoxPick` and `TryConsumeBoundingBoxPick` signatures are correct.
Correctly placed in `Fdp.Presentation.Editing` namespace.

### ComponentEditDrawer (Task B)

Optional 4th parameter is backward-compatible; the existing 3-arg call in `ComponentEditWindow`
compiles unchanged. The `DrawLeafNode` block correctly guards on both `bboxAttr != null`
and `_spatialPickerCtx != null` before drawing the button or consuming a result.

### ReplaySearchPanel (Task F)

The panel is correctly decoupled from `PlaybackHistoryTracker` and `EntitySelectionHistory`
(SR-T39 reflection check passes). All 6 modes have DTO instances that survive mode switches.
Session is rebuilt on mode change, which avoids StructEdit session state pollution. The `CurrentFilePath`
property pattern is clean.

**Design deviation — preset I/O mechanism (acceptable)**:
The instructions showed `session.ToJson()` / `session.LoadJson()` (StructEdit.Json extensions).
The developer instead used `System.Text.Json.JsonSerializer` for both Save and Load Preset.
Justification: StructEdit.Json's `ApplyDynamicArray` cannot instantiate abstract-type collection
elements (throws `NotSupportedException`), so the StructEdit round-trip path is fundamentally
broken for `CompoundPredicateDto.Conditions: List<SearchPredicateDto>`.
Using STJ directly (which the `[JsonPolymorphic]` hierarchy supports) is the correct solution
and matches what SR-T28 now tests.
**This deviation is recorded as justified and requires no corrective action.**

### SR-T28 (preset round-trip)

The test uses `JsonSerializer.Serialize<SearchPredicateDto>(root)` and
`JsonSerializer.Deserialize<SearchPredicateDto>(json)` — matching the panel's actual behavior.
The assertions are complete: operator, nesting depth, concrete subtype identity, and field values.
The StructEdit session path (ToJson/LoadJson) is not covered, but that path is broken for
polymorphic types and was correctly abandoned. No corrective action needed.

### SR-T29 (rebuild required)

Tests the `IContainerBinding.Resize` → `MarkStructuralChange` → `RebuildDocument` lifecycle.
Assertions on `RebuildState` transitions (Stable → RebuildRequired → Stable) and child count
after rebuild are solid and test the actual StructEdit behavior.

### SR-T32 (ISpatialPickerContext stub)

Verifies the contract: method invocability, TryConsume returns stored box and clears entry
(second call returns false), and RequestedPath recording. All 4 tests are meaningful.
**Edge-case coverage is good** — the one-time consume behavior catches the double-fire bug
mentioned in developer insights.

### SR-T33 (BehaviorHashFieldDrawer)

Partial by necessity (ImGui combo requires a live renderer context). The 3 tests cover:
registry `TryGetId` round-trip, `GetRegisteredNames` contains registered entry, and
`drawer.TargetType == typeof(int)`. Acceptable for a UI drawer.

### FilteredTypeComboFieldDrawer tests (no SR-T ID)

5 tests cover empty/null filter returns all, matching filter returns subset, no-match returns
empty, and `TargetType == typeof(Type)`. Logic coverage is thorough.
**Minor issue**: these tests have no SR-T ID. This is a documentation gap only;
the test code itself is correct. Record as debt.

### SR-T39 (panel decoupling)

The reflection check for forbidden field types (`PlaybackHistoryTracker`, `EntitySelectionHistory`)
is exactly right — it validates the architectural boundary. The seek/select delegate tests
use `InvokeSeekRequested`/`InvokeEntitySelected` test seams correctly.

### FND-T11 (discovery)

4 tests cover: ISubsystem implementation, presence of `INetworkFactory` ctor (reflection-based,
no Hrot.Core dependency), CLI name derivation, and assembly loaded in AppDomain.
The use of `ParameterType.Name == "INetworkFactory"` instead of `typeof(INetworkFactory)` is
pragmatic and avoids a cross-project reference — consistent with the test project's reference graph.

---

## Test Quality Assessment

All required tests exercise logic correctness (values, behavior, type identity), not just
compilation or string existence. The SR-T39 reflection approach is particularly clean.
Partial tests (SR-T33, FilteredTypeCombo) are labeled `[PARTIAL]` in comments and explained.

**Overall test quality: Good.**

---

## Issues Found

### P3 — Thread safety in search result assignment

**Source:** `ReplaySearchPanel.DrawExecuteButton` — Task.Run closure writes to
`_results`, `_lifecycleResults`, and `_statusLine` without any synchronization.
These fields are read on the render thread. On platforms with relaxed memory ordering,
a torn read is possible. The immediate risk is low (reference types in .NET have
atomic pointer assignment), but a proper fix (e.g. `Interlocked.Exchange`, a volatile
wrapper, or a completion queue) would be clean.

**Action:** Record in DEBT-TRACKER.md as P3, target BATCH-07 cleanup.

### P3 — FilteredTypeComboFieldDrawer tests missing SR-T IDs

**Source:** `ReplaySearchPanelTests.cs` — 5 tests in `FilteredTypeComboFieldDrawerTests` class
have no SR-T ID prefix, making them invisible in the test summary grep.

**Action:** Record in DEBT-TRACKER.md as P3. Renaming is cosmetic.

---

## Verification Results

```
Fdp.Presentation.Tests (SR-T32, SR-T33, SR-T39, FilteredTypeCombo + existing):
  Passed: 35, Failed: 7 (all Vis2D DebugGizmoLayer/DebugPrimitiveRenderer2D — pre-existing)

Fdp.Toolkits.Tests (SR-T28, SR-T29 filter):
  Passed: 2/2

Hrot.ClusterRunner.Tests (FND-T11):
  Passed: 241, Failed: 2 (D003 DataDrivenGizmoPredicate — pre-existing)

Hrot.ReplayBrowser.Tests (regression):
  Passed: 8/8
```

All required new tests pass. No regressions introduced.

---

## Task Status Updates

Mark in TASK-TRACKER.md:
- [x] RB-4.8 — StructEdit plumbing for the search panel
- [x] RB-4.9 — Custom IImGuiFieldDrawers (BBox, BehaviorHash, FilteredTypeCombo)
- [x] RB-4.10 — ReplaySearchPanel (all five modes incl. compound)
- [x] RB-4.11 — Stage 4 Final Gate
- [x] RB-5.1 — Add Hrot.ReplayBrowser.csproj to ClusterRunner Solution

---

## Suggested Git Commit Message

**FDP submodule:**
```
feat(search-ui): Custom drawers, ISpatialPickerContext, full ReplaySearchPanel (RB-4.8..4.10)

- ISpatialPickerContext interface for bbox gizmo pick brokering
- ComponentEditDrawer: optional ISpatialPickerContext param, bbox pick block in DrawLeafNode
- BoundingBoxFieldDrawer: two DragFloat2 controls for BoundingBox2D
- BehaviorHashFieldDrawer: registry-backed int combo with filter
- FilteredTypeComboFieldDrawer: OrdinalIgnoreCase-filtered component/event type combo
- ReplaySearchPanel: full implementation (6 modes, STJ preset I/O, background search, results grid)
- Tests: SR-T28, SR-T29, SR-T32 (x4), SR-T33 (x3), FilteredTypeCombo (x5), SR-T39 (x4)
```

**Parent repo:**
```
feat(replay-browser): Wire ReplaySearchPanel into subsystem; register in ClusterRunner (RB-5.1, RB-4.11)

- ReplayBrowserSubsystem.WireDelegates builds ComponentEditService, PredicateCompiler,
  EventScannerCompiler, RecordingSearchService and constructs ReplaySearchPanel
- Hrot.ClusterRunner.csproj: ProjectReference to Hrot.ReplayBrowser added
- ReplayBrowserSubsystemTests: updated to 4-arg ReplaySearchPanel ctor
- FND-T11 (x4): discovery tests in Hrot.ClusterRunner.Tests
```
