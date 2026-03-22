# Batch Instructions — OC1-BATCH-02

**Batch:** OC1-BATCH-02
**Target Effort:** ~11 hours
**Phase:** 0 (Correctives) + Phase 2 + Phase 3

We identified three edge-case bugs in the BATCH-01 verification and some technical debt. This batch starts with a Corrective Task (OC1-CORRECTIVE-01) to squash them, clears the immediate technical debt, and then advances the main Context Menu features.

## Phase 0: Corrective Fixes

**OC1-CORRECTIVE-01: Fix BATCH-01 Verification Bugs**

1. **Bug 1: Entity deleted in IG is not deleted in IOS Entity Inspector**
   - **Context:** The `DerEntityInspectorPanel` (used as the "IOS Entity Inspector") maintains its own `_selectedEntityId` field, disregarding `IosLogic.SelectedEntityId`. When an entity is deleted, it displays "Entity no longer exists" but does not reset the selection.
   - **Fix:** In `DerEntityInspectorPanel.DrawDetails` (`D:\Work\IOS-IG-SimHost-FDP-2\FDP\Toolkits\FDP.Toolkit.ImGui\Panels\DerEntityInspectorPanel.cs`), if `repo.GetEntity(_selectedEntityId)` returns null, reset `_selectedEntityId = NoSelection;` before exiting.

2. **Bug 2: Route plan has zero points on generation**
   - **Context:** `DescriptorMapper.MapToComponents` (`Bagira.Map.Common\Replication\Utils\DescriptorMapper.cs`) currently silently discards `EDescriptorType.dtMapRoute` because it's missing from the `switch` statement. Consequently, SimHost doesn't create the `RoutePlan` component during the spawn phase.
   - **Fix:** Add a `case EDescriptorType.dtMapRoute:` to `MapToComponents`. Create a `Bagira.Map.Common.Components.RoutePlan` component, set `IsLoop`, convert all waypoints using `geoTransform.ToCartesian` (matching the logic in `MapRouteIngressTranslator`), and add it to `result`. Ignore if `geoTransform` is null or `Points` is null.

3. **Bug 3: Tactical drawing edit results in different shape / loses style**
   - **Context:** When the Area Tool edits are committed in `IgApplication.ActivateAreaEditingTool` (`Bagira.IG\IgApplication.cs`), the `UpdateEntityDescriptorRequest` creates a *new* `MapVisualOverlay` but hardcodes `StyleOverrideJson` to null and defaults other fields. This overrides and destroys the overlay's original style and parameters down the network line.
   - **Fix:** Before sending the updated descriptor, retrieve the original `MapVisualOverlay` properties. If unavailable from descriptors directly, you may fetch `MapOverlayStyle` (which contains the JSON style string) from the entity and map it into the new `MapVisualOverlay` payload to prevent the shape string from getting corrupted.
    - ** shape change ** The edit changes the shape - relocates the points to different locations tan originally moved by the edit tool - some coordinate recalculation issue maybe

## Debt Tasks

- **OC1-DEBT-01:** Connect asymmetric guards. `ActivateRouteAuthoringTool` and `ActivateAreaAuthoringTool` have different network/test initialization checks. Unify them in `IgApplication.cs`.
- **OC1-DEBT-02:** Add `FdpLog.Warn` in `MapCommandController.OnAreaEntityCreated` if `_sessionRequestId == Guid.Empty` to log dropped authoring sessions over time.

## Feature Tasks

### Phase 2: SimHost Route-Assignment Fix
- **OC1-S001:** SimHost FollowRoute: translate network ID at ingress boundary. (See `OC1-TASK-DETAIL.md`)

### Phase 3: IG Command Handling Extensions
- **OC1-G001:** IG handles `CMD_SET_SELECTION`. (See `OC1-TASK-DETAIL.md`)
- **OC1-G002:** IG handles `CMD_SET_VIEW` (entity-centric). (See `OC1-TASK-DETAIL.md`)
- **OC1-G003:** IG orchestrates `CMD_DRAW_PERSONAL_ROUTE`. (See `OC1-TASK-DETAIL.md`)

## Acceptance Criteria
- All tests passing.
- Ensure that you execute these tasks by updating the underlying C# codebase for both the bug fixes and the new commands.
- Do not implement Phase 4 tasks yet.
- Record all developer actions and issues you hit in `OC1-BATCH-02-REPORT.md`.
