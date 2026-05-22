# BATCH-06 Review — Extended Demo Scenarios (S15–S33)

**Status:** ✅ APPROVED  
**Reviewer:** Dev Lead  
**Date:** 2026-05-22

---

## Verification Results

| Check | Result |
|---|---|
| All 19 scenario files exist | ✅ S15–S33 confirmed |
| `dotnet build NodeEditor.sln -v quiet` | ✅ Build succeeded. 0 Warning(s), 0 Error(s) |
| `dotnet test NodeEditor.sln --no-build -v quiet` | ✅ 67/67 tests pass (63 Core + 4 UI) |
| S33 Debug.Assert on 500 nodes | ✅ Present |
| FPS badge in status bar | ✅ Confirmed in DrawStatusBar() |
| Save/Compile mock menu items | ✅ Emit EditorNotification toasts correctly |
| Make Dirty button | ✅ Uses nop GraphCommand.Batch to flip undo state |
| Simulate External Modify (S32) | ✅ NotificationActions with Save/Discard/Ignore |
| FakeMyBlueprintModel mutation methods | ✅ AddVariable, AddFunction, AddMacro, AddCustomEvent, AddDispatcher |
| FakeGraphContainer | ✅ Clean, well-documented, ActivateNext/Prev/index |
| S25 multi-tab (3 graphs) | ✅ Tab bar renders, Ctrl+Tab cycles |
| Scenario.Setup virtual | ✅ Called before Build, allows My Blueprint pre-population |

---

## Code Quality Assessment

**Architecture:** The three-virtual extension (`Setup`, `BuildMultiGraph`, `Session`) on the
`Scenario` base class is a clean, non-breaking approach. All new scenarios that don't need
multi-graph leave `BuildMultiGraph` at its default `null` return, which keeps the existing
single-graph path intact.

**FakeMyBlueprintModel extension:** Mutation methods are purely additive. `EnsureSection`
private helper correctly lazily materialises sections only when a mutation is called.
No existing scenario (S01–S13) calls `Setup`, so zero regression risk.

**DemoShell refactor:** Extracting `RebuildPanels()` is good housekeeping and reduces
duplication between initial setup and scenario switching.

**S33 Big Graph:** Correctly uses `System.Random(42)` with positional syntax (seeded), generates
exactly 500 nodes (verified by `Debug.Assert`), and silently skips duplicate-wire attempts.

**S32 Hot-Reload Conflict:** The `Simulate External Modify` button creates a blocking toast
with the three `NotificationAction` instances (Save/Discard/Ignore) - this correctly exercises
the `ChangeNotifier`/`ToastQueue` infrastructure even without a real file system.

**S25 Multi-Tab:** Tab switching reconstructs `FakeHostServices` for the newly active graph.
This ensures command sink, validator, and type system all reference the correct model.

---

## Issues Found

### P2 Issues

| ID | Description | Location |
|----|-------------|----------|
| TD-001 | `events` section has `CanCreate = false`. S17's description tells users to "Click '+' next to Events", but the '+' button is hidden by the `CanCreate=false` flag. Should be `true` to allow interactive custom-event creation in the demo. | `FakeMyBlueprintModel.cs` line ~57 |

### P3 Issues

| ID | Description | Location |
|----|-------------|----------|
| TD-002 | S29 FindInAsset builds only a single graph. The spec calls for "4 graphs each with Multiply nodes" to demonstrate cross-graph search grouping. The FindResultsPanel will only show results from one graph. Low impact — feature is still exercisable, just not the multi-graph grouping aspect. | `S29_FindInAsset.cs` |
| TD-003 | S25 tab switching creates a new `FakeHostServices` on every click. For a demo this is acceptable, but if the scenario count increases, this could add noticeable latency. | `DemoShell.cs` DrawCanvasWindow |

---

## Developer Insights Summary

The coder's Q1 answer (three orthogonal virtuals: Setup, BuildMultiGraph, Session) shows sound
design thinking. The Q2 answer about `IsKeyChordPressed` not existing is a legitimate gap in
`FakeInputSource` — this could be added as a P3 convenience method.

The note about multi-tab viewport state not surviving tab switches (because `FakeHostServices`
is rebuilt) is a real limitation. In production, viewport state would be stored in the
`FakeGraphContainer` per graph.

---

## Suggested Git Commit Message

```
feat(demo): add extended scenarios S15–S33 covering blueprint authoring, multi-tab, find, bookmarks, hot-reload, and big-graph perf
```

---

## Task Status Update

- **TASK-P7-003** → ✅ Completed
