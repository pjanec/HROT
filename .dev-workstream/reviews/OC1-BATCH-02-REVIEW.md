# OC1-BATCH-02 Review

**Verdict:** APPROVED WITH CORRECTIONS REQUIRED

## Findings

The developer has successfully completed the targeted feature work for Phase 2 and Phase 3:
- **OC1-S001:** `MissionControlRequestSystem` successfully implements translation of `FollowRoute` network IDs to underlying SimHost entity IDs, resolving the route-assignment bug.
- **OC1-G001 & OC1-G002:** Command handlers for `CMD_SET_SELECTION` and `CMD_SET_VIEW` are implemented correctly with robust test hooks and excellent edge-case handling (such as filtering ghost entities properly via `EntityLifecycle.All`).
- **OC1-G003:** `CMD_DRAW_PERSONAL_ROUTE` correctly leverages the ACK pattern and seamlessly ties into `MapCommandController`.
- **Debt tasks:** Guards in `ActivateAreaAuthoringTool` and logs in area entity creation correctly implemented.

However, the **OC1-CORRECTIVE-01** effort did not fully resolve the underlying bugs as designed, and introduced new insights that were missed in the initial analysis:
1. **Vertical Coordinate Drift on Edit/Authoring:** The translation math using `new Vector3(points[i].X, points[i].Y, 0f)` is flawed. The Raylib canvas uses `Y` for the vertical screen dimension (North in world-space), but the code assigned it to the `Vector3` `Y` dimension (Altitude) and set North to `0f`. This caused the edits to literally affect altitude instead of latitude/longitude.
2. **Missing Picking & Context Menu:** Routes are drawn via `RouteRenderLayer`, but this layer explicitly returns `null` for `PickEntity`. Consequently, the route can never be "picked" or set as the `SelectedEntity`, making it impossible to invoke context menus.
3. **IG Inspector Deletion:** While `DerEntityInspectorPanel` was fixed locally, the Context Menu logic executing the deletion calls `_world.Bus.PublishManaged(new DestroyEntityCommand...)`. This destroys the entity on the local IG node but fails to synchronize the deletion to the owner (SimHost) and IOS via network DDS, which expects a `DeleteEntityRequest` to be sent instead when the local node is not the owner. As `DeleteEntityRequest` is application-layer specific (not in FDP), it must be handles on the application layer.

The feature additions are clean, but these bugs must be resolved immediately in the next batch.

## Suggested Commit Message

```text
feat(orbat-context-menu): implement phase 2 & 3 DDS map commands

Added handlers in IG for CMD_SET_SELECTION, CMD_SET_VIEW, and 
CMD_DRAW_PERSONAL_ROUTE, wired via MapCommandController with robust ACK 
support. Updated SimHost MissionControlRequestSystem to safely translate network 
IDs to entity IDs for FollowRoute assignments using trajectory cache lookups.
Resolved asymmetric authoring tool guards and added missing error logging.

OC1-S001, OC1-G001, OC1-G002, OC1-G003, OC1-DEBT-01, OC1-DEBT-02
```
