# BATCH-38 REPORT — BUG-A2: scenario Save via icon + BUG-A3: save feedback

**Date:** 2026-06-12  
**Branch:** blueprint-integ-1  
**Commit:** (working tree)  
**Model:** pro (prescribed)

---

## Summary

Fixed two bugs in the save command system:

**BUG-A2** — Scenario Save via the toolbar icon was broken in the Editor/"Scenario" perspective:
- `shell.save` / `shell.saveAs` IsEnabled gated on `hasLoadedScenario()`, which is false after `NewScenario()` → icon greyed out.
- `describeActiveTarget` fell through to the stale document branch when the scenario was unnamed.
- `shell.save` always called `saveScenarioAction` regardless of whether the scenario was named.

**BUG-A3** — No save gave any status/log feedback:
- Document saves (Blueprint, BTree, HSM) emitted no `report` message.
- Scenario save via `saveScenarioAction` did not set `_saveAllStatus`.

---

## Changes

Three files touched (as prescribed):

### 1. `Hrot/Editor/Hrot.Editor.AiShared/Documents/ShellSaveCommands.cs`

| Change | Detail |
|--------|--------|
| `shell.save` IsEnabled | Always `true` in scenario context (was: `hasLoadedScenario()`) |
| `shell.saveAs` IsEnabled | Same — always `true` in scenario context |
| `shell.save` handler (scenario branch) | Routes by `hasLoadedScenario()`: named → `saveScenarioAction`, unnamed → `requestScenarioSaveAs` (mirrors document empty-path→Save-As rule) |
| Per-kind save feedback | After each `saveBlueprint`/`saveBTree`/`saveHsm` + `doc.MarkClean()`: calls `report?.Invoke($"[OK] Saved {doc.Kind}: '{doc.Asset.Name}'.")` |

### 2. `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

| Change | Detail |
|--------|--------|
| `saveScenarioAction` | Now sets `_saveAllStatus = $"[OK] Saved scenario '{_editorLogic?.LoadedScenarioName}'."` after saving |
| `describeActiveTarget` | In scenario context always describes the scenario: named → `"Save [scenario: {n}]"`, unnamed → `"Save Scenario"`. No longer falls through to the document branch. |
| `report` seam | Already wired as `msg => _saveAllStatus = msg` — unchanged |

### 3. `Hrot/Editor/Hrot.Editor.AiShared.Tests/Documents/SaveCommandsTests.cs`

| Test | Change |
|------|--------|
| `Save_IsEnabled_ReflectsActiveTarget` | Updated: scenario context without loaded scenario is now **enabled** (was disabled) |
| `Hotkey_CtrlS_InvokesSave_RegardlessOfFocusedWindow` | Updated: `reportCalled` assertions now 1 (after shell.save) and 2 (after shell.saveAll), reflecting new save-feedback |
| `Save_InScenarioContext_Named_RoutesToSaveScenario` | **NEW** — scenario ctx + `hasLoadedScenario=true` → `saveScenarioAction` fires, not `requestScenarioSaveAs` |
| `Save_InScenarioContext_Unnamed_RoutesToSaveAs` | **NEW** — scenario ctx + `hasLoadedScenario=false` → `requestScenarioSaveAs` fires, not `saveScenarioAction` |
| `Save_Document_ReportsSavedMessage` | **NEW** — document ctx, active Blueprint with SourceFilePath + report spy → `shell.save` calls `report` with message containing asset name |

All existing tests pass unchanged (including `NullSeams_PreserveLegacySaveBehavior` — null seams keep legacy behavior).

---

## Build result

```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
Build succeeded. 0 Warning(s) 0 Error(s)
```

---

## Test results (BLUEPRINT_REGENERATE_SNAPSHOTS not set)

```
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj --filter "FullyQualifiedName~SaveCommands"
Passed!  - Failed:     0, Passed:    15, Skipped:     0, Total:    15, Duration: 37 ms
```

```
dotnet test Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj
Passed!  - Failed:     0, Passed:   186, Skipped:     0, Total:   186, Duration: 728 ms
```

Both suites: **Failed: 0**.

---

## Deviations

None. All changes match the prescribed instructions exactly.

---

## Known issues

None.
