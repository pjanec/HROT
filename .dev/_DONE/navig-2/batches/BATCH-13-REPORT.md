# BATCH-13 REPORT

## Summary

Both tasks completed. Build: 0 errors, 9 pre-existing warnings (unchanged). Navigation tests: 259 passed, 0 failed (255 baseline + 4 new).

---

## Task NAV-P7-T2 -- JSON snapshot export button

### Files Created
- `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationSnapshotBuilder.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationSnapshotTests.cs`

### Files Modified
- `Hrot/Subsystems/Hrot.SimHost/Windows/FakeNavigationInspectorWindow.cs` -- `DrawPathsTab` extended with snapshot button; two new helper methods added

### Design Decisions

- **NavigationSnapshotBuilder** is a static class in `Fdp.Toolkit.Navigation` rather than an inner method on the window. This lets the unit tests call it without instantiating the ImGui window (which has ImGuiNET dependency).
- `System.Text.Json.Nodes.JsonObject` is used to build the JSON tree. This avoids the anonymous-type-as-object polymorphism issue that arises when storing anonymous types in `object?` fields and then serializing with `JsonSerializer.Serialize`.
- **`HasSingletonManaged<T>()` guard** is used before every `GetSingletonManaged<T>()` call. `FDP_PARANOID_MODE` is always defined in `Fdp.Core.csproj`, which makes `GetSingletonManaged` throw when the singleton is not set. `NavigationFakesModule` registers only `INavmeshProvider` (not `IPathRegistry`, `IDtCrowdProvider`, or `IVolumetricPathProvider`) as an ECS singleton.
- **`loaded_map`** is `"fake-navmesh"` for `FakeNavmeshProvider` (NavTestMap has no `Name` property), `"engine-backed"` for `EngineBackedNavmeshProvider`, and `"none"` when no navmesh singleton is registered.
- **`crowd` and `volumetric`** are always `null`: `IDtCrowdProvider` and `IVolumetricPathProvider` are not registered as ECS singletons by either module, so they cannot be retrieved from the repo.
- **`captured_at_tick`** is hardcoded to `0`. `EntityRepository` has no `TickCount` property.
- **`path_registry`**: uses `HasSingletonManaged<IPathRegistry>()`. If the registry is a `SharedPathRegistry`, includes `handle_count` from its `Muscle.Snapshot()`; otherwise records just the type name; `null` when absent.
- **`DrawPathsTab` early return removed**: the old code had `return;` after the null-registry message, which would have hidden the button and corridor table. Replaced with an if/else block so all sections are always rendered.

### Tests (4 / 4 passing)

| Test | Description |
|------|-------------|
| `Build_FakeMode_ProducesValidJson` | Fake module + map -> parseable JSON with all 6 required keys |
| `Build_FakeMode_LoadedMapIsFakeNavmesh` | loaded_map value is "fake-navmesh" |
| `Build_FakeMode_NavmeshTypePresentCrowdNull` | navmesh has type field, crowd and volumetric are JSON null |
| `Build_NoProviders_LoadedMapIsNone` | No module registered -> loaded_map is "none" |

---

## Task NAV-P7-T3 -- Path corridor preview visualization (Option C)

### Files Modified
- `Hrot/Subsystems/Hrot.SimHost/Windows/FakeNavigationInspectorWindow.cs` -- `DrawCorridorPreviewTable` and `DrawPreviewWaypoint` static methods added; called from `DrawPathsTab`

### Design Decision: Option C chosen (ImGui table in Paths tab)

The instructions listed three options for NAV-P7-T3 visibility. Option A (IStatelessGizmo) and Option B (EntityStatefulGizmo) both require a selection-event infrastructure (`EntitySelectedEvent`, `EntityDeselectedEvent`, or a selection manager) to set/clear `FlagBitStreamCorridorPreview` on the correct entity. No such infrastructure exists in the codebase: `grep EntitySelectedEvent` and `grep EntityDeselectedEvent` found zero matches; no selection manager type was found.

Option C was chosen: add a corridor preview waypoint table directly to the existing Paths tab in `FakeNavigationInspectorWindow`.

**StreamCorridorPreview flag management is deferred.** The table displays any entity that currently has `NavigationCorridorPreview` (i.e., where `FlagBitStreamCorridorPreview` was already set by some other code path). The footer message directs the user to set the flag manually. When selection infrastructure is added, a proper gizmo can be introduced on top of the already-functioning CorridorPreviewSystem without changing this window.

### Implementation notes
- `DrawCorridorPreviewTable` queries `repo.Query().With<NavigationCorridorPreview>().Build()` (same pattern as `CorridorPreviewSystem`).
- Each entity renders as a collapsible `ImGui.TreeNode` showing version, segment start, and waypoint count.
- `DrawPreviewWaypoint` renders each of the up to 8 inline `W0..W7` fields with `in` parameter to avoid copying the 16-byte struct.
- `TraversalKind` and `SurfaceType` are displayed via `.ToString()` interpolation.

### Tests
No new unit tests for Option C: the ImGui tree output is purely visual and has no testable return value. The underlying `CorridorPreviewSystem` (which writes the component) is covered by 6 existing tests in `CorridorPreviewSystemTests.cs`.
