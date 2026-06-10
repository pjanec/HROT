# BATCH-05: Shell command set + menu/toolbar binding adapters
**Tasks:** MTB-P2-T1, MTB-P2-T2, MTB-P2-T3   **Phase:** 2 — Shell Command Set & Binding Adapters   **Est:** ~12h
**Dependencies:** Phase 1 (`MainToolbarManager`, `IconWidgets` IconHandle overloads, `IIconProvider` keys).

> Do tasks in sequence; do NOT start the next until the current task's impl + tests are done and ALL
> tests pass. MTB-P2-T4 (Save/Save-As/Save-All + Ctrl+S) is a SEPARATE later batch — do NOT implement
> save commands or hotkey wiring here.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/main-toolbar-1/DESIGN.md` §6 (Editor Action System Integration), esp. §6.1 + §6.2.
3. `.dev/main-toolbar-1/TASK-DETAIL.md` → MTB-P2-T1, T2, T3.
4. Existing types you build on (read them):
   - `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Action/IEditorCommands.cs` —
     `IEditorCommands`, `EditorCommandDescriptor(Id, DisplayName, Category, Description, IconKey,
     DefaultKey, IsEnabled, IsChecked)`, `EditorCommandResult`, `KeyBinding`.
   - `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Action/EditorCommandsImpl.cs` —
     `Register(descriptor, Action<EditorCommandContext>)`, `Get`, `All`, `Invoke`,
     `AvailabilityChanged`, `NotifyAvailabilityChanged`.
   - `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/GlobalMenuRegistry.cs` —
     `RegisterItem(path, onClick)`, `RegisterCheckableItem(path, getChecked, onChanged)`,
     `RegisterSeparator(path)`, `MenuItemNode`.
   - `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/MainToolbarManager.cs` (BATCH-03) and
     `IconWidgets` IconHandle overloads (BATCH-03), `IIconProvider`/`IconHandle`.

---

## Task 1 — Shell `EditorCommandsImpl` (MTB-P2-T1) — §6.1
`EditorCommandsImpl` already exists in NodeEditor.Core. This task introduces a **single long-lived
shell-level command set** owned by the editor composition root and exposes it for subsystem
registration (distinct from per-document command sets, which stay unchanged).
- Add a shell command holder accessible to subsystems — e.g. a `ShellCommands` property/holder.
  **Placement:** prefer exposing it where subsystems already reach shared editor services. Use the
  codebase-memory MCP to find the editor composition root (likely `Hrot.Editor`/`EditorSubsystem`
  or a shell services object). If a neutral engine home is cleaner, a thin holder in
  `Fdp.Presentation` is acceptable — document your choice in the report. Do NOT duplicate
  `EditorCommandsImpl`; reuse it.
- The shell set must support: register a command (descriptor + handler), `Get(id)`, `All`,
  `Invoke(id)` (no-op + non-success when the command's `IsEnabled()` is false).

**Tests required (`ShellCommandsTests`, in the test project matching the chosen placement):**
- `RegisteredCommand_IsReturnedByGetAndAll` — after registering, `Get(id)` returns it and `All`
  contains it.
- `Invoke_CallsHandler_WhenEnabled` — handler runs and result.Success is true when `IsEnabled` true.
- `Invoke_NoOp_WhenDisabled` — handler does NOT run (use a recording flag) and result indicates
  not-invoked/failure when `IsEnabled` returns false.

## Task 2 — Menu-binding adapter (MTB-P2-T2) — §6.2
**File (NEW):** a generic `MenuCommandAdapter` in `Fdp.Presentation` (it bridges
`IEditorCommands` → `GlobalMenuRegistry`; both reachable from `Fdp.Presentation`).
Given an `IEditorCommands` + command id + menu path, register a `GlobalMenuRegistry` item:
- non-checkable command → `RegisterItem(path, onClick)` where `onClick` guards on `IsEnabled()`
  and calls `commands.Invoke(id)` only when enabled (disabled → no-op).
- checkable command (`IsChecked != null`) → `RegisterCheckableItem(path, getChecked: IsChecked,
  onChanged: _ => Invoke(id))`.
- Shortcut text from `DefaultKey` (use `KeyBinding.ToString()`); if `MenuItemNode` has no shortcut
  field, add a minimal optional `Shortcut`/`GetEnabled` field to `MenuItemNode` ONLY if needed and
  keep it backward-compatible (document any GlobalMenuRegistry API addition). Do not break existing
  menu rendering/tests.

**Tests required (`MenuCommandAdapterTests`):**
- `RegistersItem_AtPath_OnClickInvokesCommand` — invoking the registered node's `OnClick` calls the
  command (recording fake command set).
- `Checkable_ReflectsIsChecked` — the node's `GetCheckedState()` tracks the descriptor's `IsChecked`.
- `Disabled_ItemNotInvoked_WhenIsEnabledFalse` — with `IsEnabled` false, triggering `OnClick` does
  NOT invoke the command.

## Task 3 — Toolbar-binding adapter (MTB-P2-T3) — §6.2
**File (NEW):** a generic `ToolbarCommandAdapter` in `Fdp.Presentation`.
Given an `IEditorCommands` + command id + `IIconProvider` + a `MainToolbarManager` + sortOrder/
perspective, register a `MainToolbarManager` entry whose render delegate (per §4.2):
- resolves the command's `IconKey` via `IIconProvider.TryGet`,
- draws via `IconWidgets` (`enabled = IsEnabled()`, toggled = `IsChecked() ?? false`),
- tooltip = `DisplayName` (+ `Description`/shortcut when present),
- on click → `commands.Invoke(id)`.
Re-read `IsEnabled`/`IsChecked` every frame (immediate mode; no caching). Keep the render logic
split from raw ImGui so the click→invoke / enabled / toggled wiring is unit-testable headlessly
(extract a small testable "should-invoke / visual-state" computation if needed; mirror how
IconWidgets/MainToolbar tests run under the headless fixture).

**Tests required (`ToolbarCommandAdapterTests`):**
- `Click_InvokesCommand` — simulating a click invokes `Invoke(id)` (recording fake).
- `Enabled_And_Toggled_TrackDescriptor` — the entry's enabled/toggled inputs reflect the
  descriptor's `IsEnabled()`/`IsChecked()` live (change the fake's state → reflected next read).

## Hard constraints
- Do NOT delete/modify legacy/assembly-loading code. Do NOT implement Save/Save-As/Save-All or any
  hotkey/Ctrl+S wiring (that is MTB-P2-T4).
- Keep public APIs of existing types intact except a minimal, backward-compatible `MenuItemNode`
  addition if genuinely required for shortcut/enabled (document it). No scope creep.
- Do NOT weaken/skip/auto-pass tests or add a Stability trait to dodge a failure.

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings).
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New tests pass UNFILTERED. Relevant suites 0-failed
  with `--filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"`:
  `Fdp.Presentation.Tests` (run new tests by class filter to avoid the pre-existing Vis2D suite
  DEADLOCK — see PRE-2; do NOT touch those failures), `NodeEditor.Core.Tests`,
  `Hrot.Editor.AiShared.Tests`, and whichever test project hosts your ShellCommands placement.
- Write `.dev/main-toolbar-1/reports/BATCH-05-REPORT.md`: files changed, where you placed the shell
  command set and why, any GlobalMenuRegistry API addition, each new test + assertions, the headless
  seam for the toolbar adapter, paste actual test-run summaries, and the insight questions.

If something cannot be done as specified, stop and report why rather than stubbing it.
