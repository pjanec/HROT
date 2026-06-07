# BATCH-13 Instructions — NAV-P7-T2 + NAV-P7-T3

## Onboarding

Read these design documents before writing any code:
- `d:\Work\IOS-IG-SimHost-FDP-2\.dev\navig-2\DD-Fake-Nav.md` §9 (JSON snapshot schema) and §10 (AAR recording)
- `d:\Work\IOS-IG-SimHost-FDP-2\.dev\navig-2\TASK-DETAILS.md` sections NAV-P7-T2 and NAV-P7-T3

Reference files (read before implementing):
1. `Hrot\Subsystems\Hrot.SimHost\Windows\FakeNavigationInspectorWindow.cs` (the window to extend)
2. `FDP\Toolkits\Fdp.Toolkits\Navigation\NavigationComponents.cs` lines 380-460 (PreviewWaypoint, NavigationCorridorPreview structs)
3. `FDP\Toolkits\Fdp.Toolkits\Navigation\NavigationComponents.cs` lines 1-100 (to find TraversalKind enum)
4. `FDP\Toolkits\Fdp.Toolkits\Navigation\Fake\FakeNavmeshProvider.cs` (to find what test API exposes for snapshot)
5. `FDP\Toolkits\Fdp.Toolkits\Navigation\Fake\FakeDtCrowdProvider.cs` (crowd agent state for snapshot)
6. `FDP\Toolkits\Fdp.Toolkits\Navigation\Fake\FakeVolumetricPathProvider.cs` (volumetric state for snapshot)
7. `FDP\Toolkits\Fdp.Toolkits\Navigation\Fake\MusclePathRegistry.cs` (path registry for snapshot)
8. `Hrot\Subsystems\Hrot.SimHost\SimHostSubsystem.cs` (to find where gizmos are registered)
9. `Hrot\Subsystems\Hrot.SimHost\SimHostApp.cs` (to find visualization setup)
10. `FDP\Toolkits\Fdp.Toolkits\Diagnostics\Gizmos\Systems\GlobalGizmoManager.cs` (gizmo registration API)
11. Search for `IStatelessGizmo` to find its interface definition
12. Search for `IBehaviorGizmoFactory` to find behavior gizmo registration pattern
13. `Hrot\Subsystems\Hrot.Editor\Gizmos\LocationPickerGizmo.cs` (IEntityStatefulGizmo precedent)
14. Search for any existing per-entity visualization gizmo that reads ECS data per-tick

## Workspace root
`d:\Work\IOS-IG-SimHost-FDP-2`

## AGENTS.md constraints (must follow)
- Do NOT use Unicode characters in comments or string literals — use ASCII equivalents
- Preserve existing comments exactly
- Minimize textual diffs — only change lines required for the task
- Ensure `dotnet build IOS-IG-SimHost.sln` passes with 0 errors before finishing

---

## Task 1 — NAV-P7-T2: JSON snapshot export button

### What to build

Add a "Snapshot JSON" button inside `FakeNavigationInspectorWindow.DrawPathsTab()` (or as a separate overlay).
When clicked via ImGui, the button:
1. Builds a JSON string matching DD-Fake-Nav §9 schema (see below)
2. Copies it to clipboard via `ImGui.SetClipboardText(json)`

### Where to put it

Extend `FakeNavigationInspectorWindow.cs` only. No new files needed for this task.

### JSON schema (from DD-Fake-Nav §9)

The JSON must have these top-level keys:
- `"captured_at_tick"`: integer tick count — use `repo.TickCount` or a similar field from the repository
- `"loaded_map"`: string — the loaded map name. If using `FakeNavmeshProvider`, call its test API to get the map name; for `EngineBackedNavmeshProvider`, use `"engine-backed"`
- `"navmesh"`: object with `"layers"` array — each layer has `"layer"` (name), `"version"` (int), `"blocked_polygons"` (int array). If not in fake mode, omit or set to null.
- `"crowd"`: object with `"tick_count"` and `"agents"` array. Each agent: `"entity"` (entity id as string), `"pos"` (float[3]), `"vel"` (float[3]), `"target"` (float[3]). If not in fake mode, omit or set to null.
- `"volumetric"`: object with `"no_fly_zones"` array. If not in fake mode, omit or set to null.
- `"path_registry"`: object. If `IPathRegistry` singleton is `MusclePathRegistry`, expose basic stats (handle count); otherwise just the type name.

### Implementation approach

Use `System.Text.Json.JsonSerializer` (it's already available in .NET 8). Build an anonymous object or use `System.Text.Json.Nodes.JsonObject` for the JSON structure and serialize with `JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true })`.

**CRITICAL — only query `INavmeshProvider` and `IPathRegistry` singletons** (the only ones registered by `EngineBackedNavigationModule.RegisterProviders`). For `FakeDtCrowdProvider` and `FakeVolumetricPathProvider`, try `repo.GetSingletonManaged<IDtCrowdProvider>()` — they MAY be registered in fake mode (NavigationFakesModule registers them). Cast to the fake types if available.

Read the fake provider files first to understand what properties are publicly accessible. Do NOT access internal/private fields. Use the test API interfaces if they expose snapshot data.

### Simple fallback approach

If the fake providers don't expose rich snapshot data publicly, it's fine to output a minimal but valid JSON with the fields set to sensible defaults (empty arrays, zero tick count). The important thing is:
1. The JSON is valid (parseable)
2. The top-level keys match the schema
3. The button works without crashing

### Test for NAV-P7-T2

Create `FDP\Toolkits\Fdp.Toolkits.Tests\Navigation\NavigationSnapshotTests.cs`.

**Test class**: `NavigationSnapshotTests`

Write one test: `SnapshotJson_FakeMode_ProducesValidSchema`
- Build a navigation fake module (use `NavigationFakesModule` which you read from its source)
- Create an `EntityRepository` with the module registered
- Instantiate `FakeNavigationInspectorWindow` with a getter that returns the repo
- Call an internal/public method that returns the JSON string (you may need to add a `BuildSnapshotJson(EntityRepository repo)` internal method or just test that the structure is correct)
- Parse the JSON with `JsonDocument.Parse(json)`
- Assert that `captured_at_tick` key exists
- Assert that `loaded_map` key exists
- Assert that the JSON is valid (no exception on parse)

If testing the window directly is too complex (it uses ImGui), instead test a `NavigationSnapshotBuilder` static helper class you create in `FDP\Toolkits\Fdp.Toolkits\Navigation\` that the window calls.

---

## Task 2 — NAV-P7-T3: Planned-path corridor gizmo

### What to build

A visualization component that:
1. When an entity is **selected**, sets `NavigationIntent.Flags |= MoveToParams.FlagBitStreamCorridorPreview` on that entity (enabling the 8-waypoint preview stream)
2. Every render tick, reads `NavigationCorridorPreview` from the selected entity and draws a polyline via debug primitives
3. When the entity is **deselected**, clears the `StreamCorridorPreview` flag (set `Flags &= ~FlagBitStreamCorridorPreview`)

### Architecture decision — IMPORTANT

After reading the existing gizmo code (`GlobalGizmoManager`, `IEntityStatefulGizmo`, and the behavior gizmo system), choose the simplest approach that fits the existing patterns.

**Option A (preferred if available)**: `IStatelessGizmo` per-entity gizmo registered via `IBehaviorGizmoFactory` — automatically called each frame for entities with `NavigationCorridorPreview` component. No interaction needed.

**Option B**: A selection-watching system (`IEcsModuleSystem`) that:
- Subscribes to `EntitySelectedEvent` / `EntityDeselectedEvent` (or reads the selection singleton)
- On selection: adds/updates `NavigationCorridorPreview` flag
- On deselection: clears the flag
- Draws the corridor via debug draw API in `Execute`

**Option C (fallback)**: Just add corridor drawing to the `FakeNavigationInspectorWindow` — show the preview waypoints as a table in the Paths tab for the "selected entity" (no 3D gizmo, just ImGui text). Headless-guarded automatically.

Read the existing code to determine which option is practical. Use whichever fits the codebase best.

### Minimum viable implementation

Whatever approach you choose, the minimum viable output is:
1. An entity with `NavigationCorridorPreview` has its waypoints visible somewhere (3D polyline via debug draw, or ImGui table in the window)
2. When an entity is selected, the `StreamCorridorPreview` bit is set in `NavigationIntent.Flags` (if the entity has a `NavigationIntent` component)
3. When deselected, the bit is cleared
4. The implementation is headless-guarded

### Finding the right interfaces

Search for:
- `IStatelessGizmo` interface definition
- `IBehaviorGizmoFactory` registration
- `EntitySelectedEvent` or `EntitySelectionChanged` events  
- `IDebugDrawBuilder` usage for polylines (look at existing gizmos that draw lines)
- `ISelectionManager` or similar selection tracking singleton

If you cannot find entity selection events, use Option C (ImGui table in window) as the fallback.

### Test for NAV-P7-T3

If you implement Option A or B, add a unit test in `FDP\Toolkits\Fdp.Toolkits.Tests\Navigation\NavigationCorridorGizmoTests.cs`:
- Test that the `StreamCorridorPreview` flag is set correctly when triggered
- Test that it is cleared on deselection

If you implement Option C (ImGui-only), no unit test is needed — just ensure the build passes and the window shows the preview waypoints.

---

## Test-Driven Task Progression (MANDATORY WORKFLOW)

For each sub-task:
1. Read the referenced source files FIRST before writing any code
2. Write the minimal implementation
3. Build: `cd d:\Work\IOS-IG-SimHost-FDP-2 ; dotnet build IOS-IG-SimHost.sln 2>&1 | Select-Object -Last 20`
4. Fix all errors
5. Run navigation tests: `cd FDP\Toolkits ; dotnet test Fdp.Toolkits.Tests --filter "FullyQualifiedName~Navigation" 2>&1 | Select-Object -Last 10`
6. Verify test count >= 255 (existing tests must not break)
7. Proceed to next sub-task only after current one passes

---

## Build verification commands

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln 2>&1 | Select-Object -Last 20

cd FDP\Toolkits
dotnet test Fdp.Toolkits.Tests --filter "FullyQualifiedName~Navigation" 2>&1 | Select-Object -Last 10
```

---

## Report to write

Write your report to `d:\Work\IOS-IG-SimHost-FDP-2\.dev\navig-2\reports\BATCH-13-REPORT.md`.

Include:
1. Implementation approach chosen for each task (especially which gizmo option for T3)
2. Interfaces/types discovered (tick count API, snapshot data APIs available on fake providers)
3. Test count (must be >= 255)
4. Build status (0 errors)
5. Files created/modified
6. Developer insights: what was unclear in the design, what design decisions were made beyond the spec, any weak points spotted

---

## Success criteria

- `dotnet build IOS-IG-SimHost.sln` = 0 errors
- Navigation tests >= 255 passing
- "Snapshot JSON" button present in `FakeNavigationInspectorWindow` — clicking it does not crash
- Snapshot JSON has correct top-level keys matching DD-Fake-Nav §9 schema
- Some form of `NavigationCorridorPreview` visualization is present (gizmo or ImGui table)
- `StreamCorridorPreview` flag is set on entity selection and cleared on deselection (or documented as deferred if selection infra is insufficient)
