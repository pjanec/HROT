# BATCH-06 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-11

## Summary
MTB-P2-T4: `ShellSaveCommands` registrar (Save/SaveAs/SaveAll with Ctrl+S/Ctrl+Shift+S), wired into
`WindowManager.ShellCommands` with a perspective-level `EditorHotkeyDispatcher` pump (text-field
gated), and removal of the focus-gated inline Ctrl+S in EditorSubsystem (§20). Completes Phase 2.

## Issues Found
No issues found.

## Verification (done by lead)
- `dotnet build IOS-IG-SimHost.sln` → **0 errors, 0 new warnings**.
- New tests run by lead: `SaveCommandsTests` → **5 passed, 0 failed** (unfiltered).
- **SimHost "regression" investigated:** worker reported `Hrot.SimHost.Tests` 584/1
  (`EqsModuleTests` "EditablePolyline not registered"). Verified it's a pre-existing nondeterministic
  test-ordering flake: EqsModuleTests passes in isolation (8/0) and the full SimHost re-run is
  **585/0 clean**. BATCH-06 touches only editor assemblies (no EQS/SimHost code), so it cannot cause
  it. Recorded PRE-3.
- Source read: `ShellSaveCommands.Save` routing correct (null→noop; empty `SourceFilePath`→
  `requestSaveAs` with NO write; else per-kind delegate + `MarkClean`). `SaveAll` delegates to the
  existing `SaveAllAiDocumentsCommand.Execute`. `register`-delegate seam makes it unit-testable.
  EditorSubsystem: perspective-level pump `ProcessThisFrame(_wm.ShellCommands)` gated on
  `!io.WantTextInput`; both inline `(isWindowFocused && ctrl && ... && sPressed)` conditions removed
  (Buttons retained); commands registered with production save delegates + DEC-9 seam.
- Scope: `ShellSaveCommands.cs` (new), `SaveCommandsTests.cs` (new), `EditorSubsystem.cs` (modified).
  No legacy deletions, no scope creep.

## Test Quality
Strong. `Save_WithSourcePath_WritesActiveDocument` asserts the delegate got the right asset+path and
the doc was marked clean and saveAs was NOT called. `Save_EmptySourcePath_RoutesToSaveAs` asserts the
seam fired with the doc, NO per-kind write, doc still dirty. Hotkey test drives a fake `IInputSource`
through the real dispatcher and asserts the correct command id fires (Ctrl+S vs Ctrl+Shift+S).

## Verdict
APPROVED. MTB-P2-T4 → `[x]`. **Phase 2 complete.**

## Commit Message
```
feat(main-toolbar): Save/Save-As/Save-All shell commands + Ctrl+S fix (MTB-P2-T4)

ShellSaveCommands registrar: shell.save (Ctrl+S), shell.saveAs, shell.saveAll (Ctrl+Shift+S);
Save routes to requestSaveAs seam on empty SourceFilePath, else per-kind write + MarkClean
(DEC-9: Save-As dialog deferred to Phase 6). Wired into WindowManager.ShellCommands with a
perspective-level EditorHotkeyDispatcher pump (text-field gated) so Ctrl+S fires regardless
of focus; removed the focus-gated inline Ctrl+S/Ctrl+Shift+S in EditorSubsystem (§20).
Tests: SaveCommandsTests (5), all pass. Completes Phase 2.
```
