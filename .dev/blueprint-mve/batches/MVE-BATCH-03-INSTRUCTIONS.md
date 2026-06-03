# MVE-BATCH-03: toolbar "Run Opened Blueprint on Selected Entity" button
Manual-testing button: attach the currently-opened Instance Blueprint to the currently-selected entity, using the production `BlueprintAttachService` from MVE-02. Run-mode-agnostic (just sets up the entity's components; the running sim ticks it).

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md`; `.dev/blueprint-mve/DESIGN.md`; MVE-BATCH-02 report/review (`BlueprintAttachService`, `BlueprintRuntimeWiring`, the editor `_blueprintRegistry`, `_world`).
2. Reuse: `Hrot.Blueprints.Editor/Runtime/BlueprintAttachService.AttachToEntity(world, registry, asset, entity)` (idempotent, returns a `BlueprintAttachResult` with a status enum). Toolbar API: `Hrot.Blueprints.Editor/IWindowRegistrar.cs` `RegisterToolbarEntry`. Selection: `Hrot.Editor.AiShared/Selection/EditorSelectionStore` (`SelectedEntity`, `ActiveAsset`) — VERIFY the exact members (entity selection vs asset selection) against the code first.
Use codebase-memory MCP; not search_code. GizmoMap.Contracts 0.2.2; no Hrot.IG/DDS.

## Locked design (from the user)
- Runs on the **currently-selected entity** (`EditorSelectionStore.SelectedEntity`) — does NOT spawn a new entity.
- **Run-mode-agnostic**: the button just attaches (component setup); it must NOT require the sim to be running/previewing. The tick happens whenever the sim runs.
- Observable = the demo blueprint's blackboard `Count` increments once the sim ticks (the existing runtime inspector / watch shows it).

## Task 1 — the button + callback
- Register a toolbar entry ("Run Blueprint on Selected Entity", or similar) via the editor's `IWindowRegistrar.RegisterToolbarEntry` from `EditorSubsystem` (it has `_world`, `_blueprintRegistry`, the selection store, and the active document/`AiCanvasContext.AssetRef`).
- Callback logic (extract into a **headless-testable** method — NOT inline in ImGui — so it can be unit-tested without a context):
  1. Resolve the selected `Entity` (`EditorSelectionStore.SelectedEntity`); if none selected → report "Select an entity first" (no-op).
  2. Resolve the active Blueprint `BlueprintAsset` (from the active `AiDocument`/`AiCanvasContext.AssetRef`); if none/not a blueprint → report.
  3. Call `BlueprintAttachService.AttachToEntity(_world, _blueprintRegistry, asset, entity)`.
  4. Surface the `BlueprintAttachResult.Status` to the user (log/toast/status line): Attached / AlreadyAttached / NotRegistered ("compile the blueprint first" — note: on-demand compile is a later MVE step) / NotInstanceKind / NoSlotAvailable.
- Run-mode-agnostic: do not gate on sim state.

## Task 2 — headless test of the callback path
The button UI itself is ImGui (manual), but the **callback logic must be unit-tested headlessly**: given a world + registry with the demo blueprint registered + a created+"selected" entity + the active asset, invoke the extracted callback method and assert it returns `Attached` and the entity now carries the blueprint slot (and AlreadyAttached on a second call; NotRegistered when the asset isn't in the registry; the "no entity selected" path). Reuse `BlueprintAttachService` semantics. Put the test where it can construct the editor selection + a world (likely `Hrot.Blueprints.Tests` or a ClusterRunner integration test using EditorHarness + the wiring helper).

## Success Criteria
- [ ] A toolbar button attaches the opened Instance Blueprint to the selected entity via `BlueprintAttachService`, run-mode-agnostic, with clear status feedback.
- [ ] Callback logic is headless-testable + tested (Attached / AlreadyAttached / NotRegistered / no-entity).
- [ ] Build 0 errors; touched projects no new warnings. GizmoMap.Contracts 0.2.2.
- [ ] Green: new test(s); `EditorSubsystemBoot` filter (no regressions — the button is registered at composition); `Hrot.Blueprints.Tests` (DEBT-006 unchanged); `Hrot.Editor.AiShared.Tests`.
- [ ] Report at `.dev/blueprint-mve/reports/MVE-BATCH-03-REPORT.md`.

## Execution rules — YOU (the sonnet agent) run the full implement→build→test→fix loop yourself
- Verify `IWindowRegistrar.RegisterToolbarEntry` + `EditorSelectionStore.SelectedEntity`/`ActiveAsset` + the active-asset access against the code FIRST (cite file:line). If `SelectedEntity` doesn't exist on `EditorSelectionStore`, find the real entity-selection source the editor uses and use that (report what you found).
- Reuse `BlueprintAttachService`; do NOT duplicate the attach sequence or reimplement the runtime. Gate all ImGui behind a context check; keep the callback logic ImGui-free + testable.
- Build (`dotnet build IOS-IG-SimHost.sln`) and run the suites listed above yourself; fix to green before reporting; never fake a pass.

## Report
Document: the toolbar registration; the selected-entity + active-asset resolution (members used, cited); the status feedback; the testable callback method + its tests/counts; build status; EditorSubsystemBoot unaffected; the precise next step for MVE-04 (Save). Suggested commit message. No comprehension questions.
