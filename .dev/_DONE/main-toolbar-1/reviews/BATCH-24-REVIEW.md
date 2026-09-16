# BATCH-24 Review (BUG-2 — populate the main toolbar)
**Status:** ✅ APPROVED   **Date:** 2026-06-11

## Summary
Wired the three toolbar groups into `EditorSubsystem.RegisterWindows`: Time(0) │ Perspective(20) │
AI-debug(40–45), with separators. New `EditorTimeTransportFacade : ITimeTransportFacade` adapts the
editor preview/time controllers for `MainToolbarTimeControlSection`.

## Issues Found
No issues found.

## Verification (done by lead)
- New + existing `EditorSubsystemBlueprintWindowsTests` → **9/9** (8 window + 1 new
  `…_PopulatesMainToolbar` guardrail). `RegisterWindows` no longer throws and now populates the toolbar.
- `Hrot.Blueprints.Tests` (Stability filter) → **exactly the 9 PRE-1 failures** (clean re-run; an earlier
  run showed a 10th = a known env-sensitive alloc/quick-reload flake, gone on re-run). No new
  deterministic failure.
- Wiring read in `EditorSubsystem`: `PerspectiveToolbarSection` (self-registers, sortOrder 20),
  `AiDebugCommands.Register(ShellCommands.Register, debugRegistry)` + 6 `ToolbarCommandAdapter.Register`
  (40–45), `MainToolbarTimeControlSection` + `EditorTimeTransportFacade` RegisterEntry (0), separators
  at 10/30. `EditorTimeTransportFacade` is null-guarded + `HasSingleton<GlobalTime>`-guarded; maps
  IsPaused/TotalTime/TimeScale/Toggle/Step/Stop/SetTimeScale onto the preview + MasterSyncController
  (mirrors the existing status-bar time control). Compiles clean.
- All toolbar wiring is null-safe → the bare-subsystem unit-test path doesn't throw.
- Scope: `EditorSubsystem.cs` + new `EditorTimeTransportFacade.cs` + guardrail test. No deletions/creep.

## Note
Play/pause/step *semantics* of the time-control group are best confirmed in the running editor (the
toolbar's live behavior); the adapter is structurally sound and crash-safe. Icon-cell visual polish for
the AI-debug/perspective icons remains DBT-1 (cosmetic).

## Verdict
APPROVED. **BUG-2 resolved** — the toolbar now renders Time/Perspective/AI-debug groups.

## Commit Message
```
fix(main-toolbar): populate the main toolbar — wire Time/Perspective/AI-Debug groups (BUG-2)

EditorSubsystem.RegisterWindows now registers the toolbar groups (Time(0) | sep | Perspective(20)
| sep | AI-debug(40-45)): PerspectiveToolbarSection (self-registering), AiDebugCommands + per-command
ToolbarCommandAdapter entries, and MainToolbarTimeControlSection backed by a new null-safe
EditorTimeTransportFacade (adapts the editor preview + MasterSyncController). Guardrail test
EditorSubsystem_RegisterWindows_PopulatesMainToolbar added; 8 window tests stay green; Blueprints.Tests
= the 9 pre-existing failures only. All toolbar wiring is null-safe.
```
