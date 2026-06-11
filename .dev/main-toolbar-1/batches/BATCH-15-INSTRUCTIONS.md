# BATCH-15: Modal picker host + docked window host
**Tasks:** MTB-P5-T3, MTB-P5-T4   **Phase:** 5   **Est:** ~8h
**Dependencies:** BATCH-11/12 (`AssetBrowserPanel` with `AssetActivated`/`Selection`/options).

> Do T3 then T4 in sequence; do NOT advance until the current task's impl + tests pass. Both hosts
> are GENERIC (no per-kind logic) and perform NO side effects — they only invoke the supplied callback.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/main-toolbar-1/DESIGN.md` §10.3 (hosts: modal picker + docked window).
3. `.dev/main-toolbar-1/TASK-DETAIL.md` → MTB-P5-T3, MTB-P5-T4.
4. Existing code (read):
   - `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetBrowserPanel.cs` — `AssetBrowserPanelOptions`,
     `AssetActivated` event, `Selection`, `DrawContent()`.
   - `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/ManagedWindow.cs` — base class (`Id`,
     `OwningPerspective`, `Scope`, `IsOpen`, `IconKey`); abstract `DrawClientArea()`.
   - `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs` — `RegisterWindow`,
     `WindowScope`.
   - `Hrot.Editor.AiShared.csproj` already references `Fdp.Presentation` → both hosts can live in
     `Hrot.Editor.AiShared/Browser`.

## Task 1 — Modal picker host (MTB-P5-T3) — §10.3
**File (NEW):** `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetPickerModal.cs`
A modal that hosts an `AssetBrowserPanel`, opened with `AssetBrowserPanelOptions` + an
`Action<IEditableAsset?>` callback:
- Construct/own an `AssetBrowserPanel(catalog, icons, options)`. Subscribe to its `AssetActivated`.
- **On `AssetActivated(asset)`:** close the modal AND invoke `callback(asset)`.
- **On Esc / cancel:** close the modal AND invoke `callback(null)`.
- **Never executes** — it does not open documents, load scenarios, or call any `AiDocumentManager`/
  load API. It ONLY invokes the supplied callback.
- Keep the open/close/activate/cancel logic separated from the ImGui draw so it is testable headlessly
  (e.g. `Open(options, callback)`, `bool IsOpen`, internal `HandleActivated(asset)` and
  `HandleCancel()` methods that the draw wires to double-click and Esc). `DrawModal()` does the ImGui
  popup + `panel.DrawContent()`.
- A modal must service exactly one callback per open; guard against double-invoking the callback.

**Tests required (`AssetPickerModalTests`, fake catalog/icons + recording callback + a recording
fake document-manager/load to prove no side effects):**
- `Activate_ClosesAndInvokesCallback_WithAsset` — open, then `HandleActivated(asset)` → `IsOpen ==
  false` and callback received exactly that asset (once).
- `Escape_InvokesCallback_WithNull` — open, then `HandleCancel()` → `IsOpen == false` and callback
  received `null` (once).
- `NeverCalls_DocumentManager_Or_Load` — through both activate and cancel paths, the recording
  document-manager/load fake's methods are NEVER called (the modal has no such dependency / never
  invokes it).

## Task 2 — Docked window host (MTB-P5-T4) — §10.3
**File (NEW):** `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetBrowserDockedWindow.cs`
A `ManagedWindow` subclass hosting the same `AssetBrowserPanel`, registered in the window registry:
- Ctor takes id/title/perspective/scope + `AssetBrowserPanelOptions` + `Action<IEditableAsset>`
  callback (supplied by whoever registers it). Owns an `AssetBrowserPanel`.
- **On `AssetActivated(asset)`:** invoke the callback; the **window STAYS OPEN** (does not close).
- `DrawClientArea()` draws `panel.DrawContent()`.
- Pick a stable, documented `Id` (e.g. `"AssetBrowser"`) and a sensible `WindowScope` (Global unless
  a reason otherwise — document). Registered via `WindowManager.RegisterWindow`.
- Still no side effects beyond invoking the registrant's callback.

**Tests required (`AssetBrowserDockedWindowTests`, headless):**
- `Registered_WithExpectedId_AndScope` — after `WindowManager.RegisterWindow(window)`,
  `TryGetWindow(id)` returns it; assert its `Id` and `Scope` are the documented values.
- `Activate_InvokesCallback_WindowStaysOpen` — show the window (IsOpen true), simulate
  `AssetActivated(asset)` → callback received the asset AND `IsOpen` remains true.

## Hard constraints
- Both hosts GENERIC and side-effect-free (only invoke the callback). Do NOT wire callers to
  `AiDocumentManager.Open`/`LoadScenarioByName` here — that is MTB-P5-T6. Do NOT implement scenario
  nested-name (MTB-P5-T5).
- Do NOT delete/modify legacy/assembly-loading code. Keep existing public APIs intact (additive).
- Do NOT weaken/skip/auto-pass tests; zero new warnings (TreatWarningsAsErrors).

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings).
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New tests pass UNFILTERED. 0-failed with the Stability
  filter for `Hrot.Editor.AiShared.Tests` + the hot suites `Fdp.Toolkits.Tests` + `Hrot.SimHost.Tests`
  (PRE-3 EQS flake → re-run if it appears).
- Write `.dev/main-toolbar-1/reports/BATCH-15-REPORT.md`: files changed, the open/activate/cancel
  seams, the docked window Id/Scope choice, each new test + assertions, paste actual test-run
  summaries, insights.

If something cannot be done as specified, stop and report why rather than stubbing it.
