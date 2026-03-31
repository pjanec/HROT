# Batch Instructions — OC1-BATCH-03

**Batch:** OC1-BATCH-03
**Target Effort:** ~10 hours
**Phase:** 0 (Correctives) + Phase 4

We identified three additional bugs that survived BATCH-02. The previous batch correctly implemented the Phase 2 & 3 architectural commands, but we have some residual map-layer and transform issues preventing full interaction with the new Routes. This batch will clear the last hurdle (OC1-CORRECTIVE-02) and implement the bulk of the Phase 4 UI.

## Phase 0: Corrective Fixes

**OC1-CORRECTIVE-02: Fix BATCH-02 Bug List**

1. **Bug 1: Canvas Y-to-Z Math in Authoring/Editing Tools**
   - **Context:** In `IgApplication.cs`, the methods `ActivateRouteAuthoringTool` and `ActivateAreaAuthoringTool` take `points[i]` (`Vector2`) and assign `X` to East and `Y` to Altitude: `(lat, lon, alt) = _geoTransform.ToGeodetic(new Vector3(points[i].X, points[i].Y, 0f));`. The IG canvas operates in an `XZ` fashion (where Canvas `Y` maps to World `Z` = North). By passing `0f` to `Z` and `Y` to Altitude, authors were essentially drawing vertical walls at `North=0`! Similarly, `ActivateAreaEditingTool` assigned `simTr.Position.Z` onto `Vector3.Z` instead of preserving the local edited component.
   - **Fix:** Update all three locations in `IgApplication.cs`. Map `points[i].Y` to `Vector3.Z`, and set `Vector3.Y` (Altitude) to `0f`. In `ActivateAreaEditingTool`, set `Vector3.Y` to `simTr.Position.Y` and `Vector3.Z` to `absCartPoints[i].Y`. 

2. **Bug 2: Missing `PickEntity` for Routes**
   - **Context:** Routes are drawn via `RouteRenderLayer`, but `RouteRenderLayer.PickEntity` is hardcoded to return `null`. This means the user cannot click on the route to select it or invoke the Context Menu.
   - **Fix:** In `RouteRenderLayer`, implement `PickEntity(Vector2 worldPos)` by calculating the distance from the point to every segment of the `RoutePlan`. If `distance < PickRadius`, return the entity. `PickRadius` can be set to around `5.0f` to `10.0f` world units. For looping routes, remember to include the segment connecting the last vertex back to the first. Note: coordinate convention here is `Vector2(pos.X, pos.Z)`.

3. **Bug 3: IG Router Deletion uses Local `DestroyEntityCommand`**
   - **Context:** The "Delete entity" context menu item in `IgApplication.cs` publishes a `DestroyEntityCommand`, which only deletes the entity from the IG simulator world. To actually delete the entity correctly and synchronize it, the IG must request deletion from the SimHost (the owner) using `DeleteEntityRequest` over DDS.
   - **Fix:** In `IgApplication.InitializeNetwork`, initialize a `private DdsWriter<Hrot.NED.Messages.DeleteEntityRequest> _deleteEntityDdsWriter`. When "Delete entity" is clicked in the IG context menu handler, if `_networkEnabled` is true, write a `DeleteEntityRequest { RequestId = Guid.NewGuid(), EntityId = (int)netId.Value }`. Only fall back to `DestroyEntityCommand` if `_networkEnabled` is false.

## Feature Tasks

### Phase 4: IOS — ORBAT Context Menu
We are now building out the actual ImGUI context menus on the IOS panel logic.

- **OC1-I001:** OrbatPanel context menu infrastructure + `IsSimulatedEntity` helper. (See `OC1-TASK-DETAIL.md`)
- **OC1-I002:** Select Entity action. (See `OC1-TASK-DETAIL.md`)
- **OC1-I003:** Center on Entity action. (See `OC1-TASK-DETAIL.md`)
- **OC1-I004:** Delete action. (See `OC1-TASK-DETAIL.md`)
- **OC1-I005:** Edit Route action (physical entities only). (See `OC1-TASK-DETAIL.md`)
- **OC1-I006:** Abort Mission action (physical entities only). (See `OC1-TASK-DETAIL.md`)

## Acceptance Criteria
- All tests passing.
- Focus exclusively on writing clean, readable `ImGui` code in `IosLogic` components for Phase 4.
- Make sure to carefully review `OC1-TASK-DETAIL.md` for specific nuances.
- Record all developer actions and issues you hit in `OC1-BATCH-03-REPORT.md`.
