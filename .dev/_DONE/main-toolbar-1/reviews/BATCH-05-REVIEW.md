# BATCH-05 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-11

## Summary
Shell command set (`WindowManager.ShellCommands` wrapping `EditorCommandsImpl`) + generic
`MenuCommandAdapter` and `ToolbarCommandAdapter` (§6.1/§6.2). MTB-P2-T1/T2/T3 done.

## Issues Found
No issues found.

## Verification (done by lead)
- `dotnet build IOS-IG-SimHost.sln` → **0 errors, 0 new warnings**.
- New tests run by lead: 18 (ShellCommands 5, MenuCommandAdapter 6, ToolbarCommandAdapter 7) +
  `GlobalMenuRegistryTests` 10 (backward-compat) → **28 passed, 0 failed**.
- Disabled semantics verified at source: `EditorCommandsImpl.Invoke` already guards `IsEnabled()`
  (non-success + no handler run); `ShellEditorCommands` delegates → correct no-op-when-disabled.
  Both adapters also guard `IsEnabled()` before `Invoke` (defense-in-depth).
- `MenuCommandAdapter`: checkable→`RegisterCheckableItem`, plain→`RegisterItem`, applies Shortcut +
  `GetEnabled` to the leaf; `ToolbarCommandAdapter`: pure `GetState()` seam (OnClick null when
  disabled), live re-read of IsEnabled/IsChecked per frame, icon-with-text-fallback, tooltip
  (DisplayName+Description+shortcut), invoke only on `clicked && enabled`. Matches §6.2.
- `MenuItemNode` additions (`Shortcut`, `GetEnabled`) are nullable/backward-compatible — the 10
  existing GlobalMenuRegistry tests pass unchanged.
- Scope: all new code in `Fdp.Presentation`; no Save/Ctrl+S work (correctly deferred to T4); no
  legacy deletions; no scope creep.

## Test Quality
Good. Tests use recording fake command sets to assert real invoke/no-op behavior, checkable state
tracking live, and disabled→not-invoked. `ToolbarCommandAdapterTests` exercise the `GetState()` seam
(OnClick null when disabled; enabled/toggled track the descriptor). No tautological/skipped tests.

## Verdict
APPROVED. MTB-P2-T1, MTB-P2-T2, MTB-P2-T3 → `[x]`. Phase 2 still needs MTB-P2-T4 (next batch).

## Commit Message
```
feat(main-toolbar): shell command set + menu/toolbar binding adapters (MTB-P2-T1, T2, T3)

WindowManager.ShellCommands (long-lived shell EditorCommandsImpl). Generic MenuCommandAdapter
(IEditorCommands→GlobalMenuRegistry: checkable/plain, shortcut, enabled) and ToolbarCommandAdapter
(→MainToolbarManager via IIconProvider+IconWidgets, immediate-mode state, GetState headless seam).
Backward-compatible MenuItemNode.Shortcut/GetEnabled additions. Tests: 18 new, all pass.
```
