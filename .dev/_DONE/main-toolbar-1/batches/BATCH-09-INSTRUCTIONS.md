# BATCH-09: AI Debug toolbar group (polymorphic)
**Tasks:** MTB-P3-T5   **Phase:** 3 — Toolbar Groups   **Est:** ~8h
**Dependencies:** BATCH-05 (shell command set), BATCH-03 (debug/* icon keys, MainToolbarManager).
Completes Phase 3.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/_DONE/main-toolbar-1/DESIGN.md` §9 (AI Debug Group, polymorphic).
3. `.dev/_DONE/main-toolbar-1/TASK-DETAIL.md` → MTB-P3-T5.
4. Existing types (read):
   - `Hrot/Editor/Hrot.Editor.AiShared/Debug/IAiDebugSession.cs` — `IsAttached`, `IsPaused`,
     `Continue()`, `StepOver()`, `StepInto()`, `StepOut()`, `Pause()`, `OnSessionStateChanged`.
   - `Hrot/Editor/Hrot.Editor.AiShared/Debug/IDebugSessionRegistry.cs` — `ActiveSession`
     (`IAiDebugSession?`), `Changed`.
   - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs` —
     `int CurrentNodePointer` (L190), `void StepBack()` (L206) (NOT on `IAiDebugSession`).
   - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/DebugStepControls.cs` —
     `static string FormatNodePosition(IBlueprintDebugSession)` (L97).
   - `NodeEditor.Core.Action.IEditorCommands`/`EditorCommandDescriptor` (BATCH-05).
   - `ShellSaveCommands.cs` (BATCH-06) — mirror its `register`-delegate pattern for testability.

## Placement (DEV-LEAD guidance — confirm with codebase-memory)
The registrar needs `IAiDebugSession`+`IDebugSessionRegistry` (AiShared), `IEditorCommands`
(NodeEditor.Core), AND `IBlueprintDebugSession`+`DebugStepControls` (Blueprints.Core/Editor).
`Hrot.Editor.AiShared` must NOT reference the Blueprints assemblies (layering). So place the
registrar in an assembly that already sees all of these — **`Hrot.Blueprints.Editor`** is the
expected home (it references AiShared + Blueprints.Core and hosts `DebugStepControls`). Verify there
is no circular reference; document the choice. Tests go in the matching test project
(`Hrot.Blueprints.Editor.Tests` if it exists, else the closest one that references these types).

## Scope — do ONLY this (MTB-P3-T5) — §9
Create a testable `AiDebugCommands` registrar (mirror `ShellSaveCommands`'s `register`-delegate +
pure decision seams) that registers the AI Debug group into the shell command set, keyed off
`IDebugSessionRegistry.ActiveSession`:

### Common commands (always registered; work for ANY `IAiDebugSession` — Blueprint/BTree/HSM)
| Id | DisplayName | IconKey | IsEnabled |
|----|-------------|---------|-----------|
| `debug.continue` | Continue | `debug/continue` | `ActiveSession is { IsPaused: true }` |
| `debug.stepOver` | Step Over | `debug/step_over` | `ActiveSession is { IsPaused: true }` |
| `debug.stepInto` | Step Into | `debug/step_into` | `ActiveSession is { IsPaused: true }` |
| `debug.stepOut`  | Step Out  | `debug/step_out`  | `ActiveSession is { IsPaused: true }` |
| `debug.pause`    | Pause     | `debug/continue`* | `ActiveSession is { IsAttached: true, IsPaused: false }` (attached & running) |

(*no `debug/pause` key in §5.1; reuse an available debug key or render text — your choice, document it.)
Handlers invoke the matching `ActiveSession` method (`Continue/StepOver/StepInto/StepOut/Pause`),
no-op when `ActiveSession` is null.

### Blueprint-only extras (present ONLY when `ActiveSession is IBlueprintDebugSession bp`)
- `debug.stepBack` (Step Back, `IconKey = debug/step_back`): `IsEnabled = bp.CurrentNodePointer > 0`;
  handler calls `bp.StepBack()`. Must NOT be registered/active for a non-blueprint session.
- **Node-position indicator**: expose the text via `DebugStepControls.FormatNodePosition(bp)` for the
  toolbar group to render; empty/absent for non-blueprint sessions.

### Toolbar group
- Group label **"AI Debug"** (not "Blueprint Debug"); commands carry `debug/*` `IconKey`s and live in
  the shell set. The existing `DebugStepControls` text row stays for the debug panel — the toolbar
  group is the icon surface over the same session.
- Provide a headless seam: e.g. `BuildGroupModel(registry)` →
  `(id, displayName, isEnabled, present)[]` reflecting common-always + StepBack-only-when-blueprint,
  and a `NodePositionText(registry)` → string (empty for non-blueprint). Render path wires these +
  the §6.2 ToolbarCommandAdapter (or directly) into the `MainToolbarManager`.

## Tests required (`AiDebugCommandsTests`, fake `IDebugSessionRegistry` + fake sessions)
Provide a fake `IAiDebugSession` (records Continue/Step*/Pause calls; settable `IsPaused`/`IsAttached`)
and a fake `IBlueprintDebugSession` (adds `CurrentNodePointer`/`StepBack`).
- `Continue_Enabled_WhenActiveSessionPaused_Else_Disabled` — IsEnabled true when paused, false when
  not paused / when ActiveSession null.
- `Continue_Invoke_CallsActiveSessionContinue` — and analogous `StepOver/StepInto/StepOut/Pause`
  invoke the matching session method (one assertion each, via recording fake).
- `Pause_Enabled_WhenAttachedAndRunning` — Pause IsEnabled true when attached & not paused; false when
  paused or detached.
- `StepBack_PresentOnly_WhenActiveSessionIsBlueprint` — with a blueprint session, StepBack is present
  (and `IsEnabled == CurrentNodePointer > 0`); with a non-blueprint `IAiDebugSession`, StepBack is
  absent (not present in the group model / not invocable).
- `Group_Works_ForNonBlueprintSession` — with a non-blueprint fake (paused), the common commands are
  present & enabled and invoke correctly (proves polymorphism beyond Blueprint).
- `NodePosition_EmptyForNonBlueprintSession` — `NodePositionText` empty for non-blueprint; non-empty
  format for a paused blueprint session.

## Hard constraints
- Do NOT delete/modify legacy/assembly-loading code. Do NOT change `IAiDebugSession`/
  `IBlueprintDebugSession`/`IDebugSessionRegistry` public APIs (consume them as-is). Additive only.
- No scope creep beyond the registrar + its tests (+ minimal wiring if you surface the group through
  an existing composition point — keep that minimal and documented).
- Do NOT weaken/skip/auto-pass tests; zero new warnings (TreatWarningsAsErrors).

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings).
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New tests pass UNFILTERED. 0-failed with the Stability
  filter for the registrar's test project + the hot suites `Fdp.Toolkits.Tests` + `Hrot.SimHost.Tests`
  (PRE-3 EQS flake → re-run if it appears; for `Hrot.Blueprints.Tests` note PRE-1 pre-existing
  failures — run by class filter for your new tests, and do NOT touch those pre-existing failures).
- Write `.dev/_DONE/main-toolbar-1/reports/BATCH-09-REPORT.md`: chosen assembly + why (layering), the
  headless seams, the `debug/pause` icon decision, each new test + assertions, paste actual test-run
  summaries, and the insight questions.

If something cannot be done as specified, stop and report why rather than stubbing it.
