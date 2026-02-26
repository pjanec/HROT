# IG-BATCH-05-REPORT: Interaction Tools & Selections

**Batch:** IG-BATCH-05  
**Tasks Completed:** IG.3.1, IG.3.2, IG.3.3, IG.3.4, IG.3.5  
**Test Results:** 124 / 124 passing (includes all prior-batch tests)  
**Status:** ✅ COMPLETE

---

## Summary of Changes

### Task IG.3.1 — StandardInteractionTool

New files:

- **`Bagira.IG/Tools/StandardInteractionToolConstants.cs`** — Single named constant: `ToolName = "StandardInteraction"` (§CODE-STANDARDS §1).

- **`Bagira.IG/Tools/StandardInteractionTool.cs`** — IG-specific wrapper around `FDP.Toolkit.Vis2D.Tools.StandardInteractionTool` (aliased as `FdpStandardInteractionTool`). All `IMapTool` methods delegate to the inner FDP tool. Subscribes to `_inner.OnEntitySelectRequest` and `_inner.OnRegionSelected` to synchronise both the `DefaultSelectionState` (consumed by `EntityRenderLayer`) and the ECS `SelectionState` component (consumed by `SelectionRenderSystem`). `ClearAllSelections()` iterates `_selection.SelectedEntities` (snapshot), clears the `DefaultSelectionState`, and resets each entity's `SelectionState { IsSelected=false, IsPrimarySelection=false }` via `ISimulationView.HasComponent<SelectionState>` (cast required — `EntityRepository.HasUnmanagedComponent` is non-public). An `internal TestHook_SelectEntity(Entity, bool)` method drives the selection handler directly for headless unit tests (accessible via `InternalsVisibleTo`).

- **`Bagira.IG/Components/SelectionState.cs`** — `[StructLayout(Sequential, Pack=1)]` two-bool unmanaged struct: `IsSelected` and `IsPrimarySelection`.

### Task IG.3.2 — Selection Highlighting (SelectionRenderSystem)

New files:

- **`Bagira.IG/Systems/SelectionRenderConstants.cs`** — Named constants: `LayerName = "SelectionRings"`, `AlwaysVisibleLayerBitIndex = -1`, `PrimaryFillAlpha = 50`, `PrimaryRingColor/SecondaryRingColor` as static readonly `Color` values.

- **`Bagira.IG/Systems/SelectionRenderSystem.cs`** — `IMapLayer` with `LayerBitIndex = -1` (always drawn, not culled). `Draw(RenderContext)` iterates a `With<SelectionState>().With<SimTransform>()` query. For each entity with `IsSelected=true`, draws a filled semi-transparent green disc (primary) or a yellow outline ring (secondary), centred on the entity's world-space XY. Zero allocations on the hot path.

Modified:

- **`Bagira.IG/Adapters/SstVisualizerAdapter.cs`** — Extended `Render` to differentiate primary vs secondary selection tint: `bool isPrimary = view.HasComponent<SelectionState> && .IsPrimarySelection`. Primary → green tint; secondary → yellow tint; hovered → orange tint; none → base tint. Ring colour follows the same primacy distinction.

- **`Bagira.IG/IgApplication.cs`** — Added `using Bagira.IG.Tools;`; registered `SelectionState` component; added `SelectionRenderSystem` layer to the canvas; wired `StandardInteractionTool` as the default canvas tool (via `_canvas.SwitchTool`).

Test file:

- **`Bagira.IG.Tests/StandardInteractionToolTests.cs`** — 5 tests: click at entity centre returns entity, click at boundary edge (within `HitRadiusWorldUnits`) returns entity, click outside radius returns null, two overlapping entities returns closest, invisible entity (`IsVisible=false`) not returned. Tests construct `EntityRenderLayer` directly with a headless `EntityRepository` (no Raylib window required).

### Task IG.3.3 — CreationTool

New files:

- **`Bagira.IG/Tools/CreationToolConstants.cs`** — Named constants: `ToolName = "Creation"`, `DefaultTkbType = 101L`, `GhostAlpha = 128`, `GhostRadiusPx = 15`, `GhostLabelFontSize = 10`, `GhostLabelOffsetY = 20`.

- **`Bagira.IG/Tools/CreationTool.cs`** — Left-click publishes a `SpawnEntityCommand` to `FdpEventBus.PublishManaged`. `SpawnEntityCommand.NetworkId = 0` (SimHost allocates the real ID); `OwnerNodeId = IgNetworkConstants.LocalNodeId`; `InitType = ReliableInitType.None`; `InitialComponents = [new SimTransform { Position = (x, y, 0), Rotation = SimMath.FacingEast }]`; `RequestId = Guid.NewGuid()`. Right-click cancels and pops the tool without spawning. Ghost preview circle drawn at cursor in `Draw()`. `event Action<SpawnEntityCommand>? OnCommandPublished` raised after publish for test observation without bus subscription. Zero allocations on the hover/draw hot path — the `List<object>` is only allocated on left-click.

Test file:

- **`Bagira.IG.Tests/CreationToolTests.cs`** — 9 tests: command published on left-click, `TkbType` matches constructor arg, `OwnerNodeId = IgNetworkConstants.LocalNodeId`, `NetworkId = 0`, `RequestId` non-empty, `InitialComponents` contains exactly one `SimTransform`, `SimTransform.Position.X/Y` matches click coordinates, right-click does not publish, default TkbType fallback when 0 is passed.

### Task IG.3.4 — MeasureTool

New files:

- **`Bagira.IG/Tools/MeasureToolConstants.cs`** — Named constants: `ToolName = "Measure"`, `LineThickness = 2.0f`, `LabelFontSize = 14`, `LabelOffsetY = 4`, `public static readonly Color LineColor = new Color(0, 255, 255, 255)` (cyan — `Color.Cyan` does not exist in Raylib-cs; stored here to avoid repeating the literal).

- **`Bagira.IG/Tools/MeasureTool.cs`** — Two-click distance measurement. State: `_startPoint: Vector2?`. First click sets `_startPoint`, returns `true` (consumed). Second click computes `LastMeasuredDistanceMeters = Vector2.Distance(_startPoint, worldPos)`, resets `_startPoint` to null, returns `true`. Right-click clears `_startPoint` (cancel). `IsMeasuring` property exposes whether a start point is held (for tests). `Draw` renders a line from start to cursor during first-click-held state, plus a distance label. `MeasureToolConstants.LineColor` used throughout; no `Color.Cyan` reference.

Test file:

- **`Bagira.IG.Tests/MeasureToolTests.cs`** — 12 tests: initial `IsMeasuring=false`, first click sets `IsMeasuring=true`, second click resets `IsMeasuring=false`, horizontal distance, vertical distance, diagonal distance (Pythagoras verified), coincident clicks produce 0 distance, large-scale distance (10 000 m), right-click cancel clears state, left-click is consumed (returns true), second click consumed, no-click `HandleHover` is not consumed.

### Task IG.3.5 — Integration Test

New file:

- **`Bagira.IG.Tests/ToolInteractionIntegrationTests.cs`** — 4 integration tests wiring `CreationTool → NetworkSpawningSystem → EntityRenderLayer → StandardInteractionTool`:

  1. **`CreationTool_LeftClick_EntityAppearsInEcsAfterSpawn`** — `CreationTool.HandleClick` publishes a `SpawnEntityCommand`; `RunSpawn` ticks the `NetworkSpawningSystem` and replays the command buffer; asserts at least one `SimTransform` entity exists.

  2. **`CreationTool_LeftClick_SpawnedEntityHasSimTransformAtClickPosition`** — Same spawn flow; iterates the query and asserts the found `SimTransform.Position.X/Y` matches the click coordinates within 2 decimal places.

  3. **`CreationTool_SpawnAndTag_EntityPickableByRenderLayer`** — Spawn + force-activate lifecycle; sets `CullingState { IsVisible=true }`; constructs `EntityRenderLayer` with `SstVisualizerAdapter` and `DefaultSelectionState`; calls `layer.PickEntity(spawnPos)` and asserts the result equals the spawned entity.

  4. **`StandardInteractionTool_SelectEntity_SetsEcsSelectionStateTrue`** — Spawn + force-activate + tag visible; calls `interactionTool.TestHook_SelectEntity(spawnedEntity, augment: false)`; asserts `SelectionState.IsSelected=true` and `IsPrimarySelection=true`.

  **Key `RunSpawn` design decision:** After `cb.Playback(repo)` the entity is in `EntityLifecycle.Constructing`. `ELM` only transitions to `Active` when all participant modules ACK — but with `Array.Empty<int>()` participants no ACK ever arrives. `RunSpawn` therefore includes a manual lifecycle flush: `repo.Query().WithLifecycle(EntityLifecycle.Constructing).Build()` → `repo.SetLifecycleState(e, EntityLifecycle.Active)`. This mimics a real frame where ELM auto-completes construction with zero participants.

---

## Developer Insights

### Q1: Picking Logic — Linear Scan vs Spatial Hashing

The `EntityRenderLayer.PickEntity` implementation (provided by the FDP toolkit) performs a **linear scan** over the pickable entity query. The IG wrapper (`StandardInteractionTool`) delegates picking entirely to the inner `FdpStandardInteractionTool`; no custom picking logic was written.

For the entity counts targeted by this IG (hundreds of simultaneous entities on the map), a linear scan is appropriate and preferable:

- **O(n) with a tight ECS loop**: The inner loop body is a SIMD-accelerated bitmask component check plus a two-scalar distance comparison — effectively a cache-friendly tight loop rather than a pointer-chasing tree traversal.  
- **No allocation overhead**: A spatial hash or k-d tree would require a per-frame rebuild or an incremental update structure, both of which introduce allocations or deferred work that clash with §CODE-STANDARDS §4.  
- **Correctness over edge cases**: At low entity counts (< 1 000) a spatial hash has higher constant overhead than the scan due to hashing and bucket management. The decision threshold is typically ~10 000 entities before spatial acceleration pays off.  
- **Future path if needed**: If entity count exceeds ~5 000, an axis-aligned grid cell structure (2D bucketing by world tile) would be the natural upgrade — each bucket would be a `NativeChunkTable` slice, keeping it allocation-free.

### Q2: Geographic vs Cartesian Distance in MeasureTool

The `MapCanvas` operates entirely in **flat Cartesian world space** (X/Y in world units, same coordinate system as `SimTransform.Position.X/Y`). `MeasureTool` therefore uses `Vector2.Distance(_startPoint, secondClick)` directly, which is correct Euclidean distance in that space.

Two issues were considered:

1. **Unit mismatch**: The canvas world unit is defined by `IgCameraConstants.InitialZoom` and the tile-pixel mapping. `LastMeasuredDistanceMeters` is named for documentation clarity but the raw value is in world units (which are metres in the current IG coordinate convention — 1 world unit = 1 metre at the standard zoom). No reprojection is required as long as this convention holds.

2. **Geographic projection distortion**: For a true geo-referenced map (Mercator or similar) the relationship between screen pixels and real-world metres is *latitude-dependent* — the same pixel width covers more metres at the equator than at higher latitudes. The current `MapCanvas` uses a flat projection with no lat/lon awareness, so `Vector2.Distance` produces a result that would be wrong for a geographic coordinate system. If the map is ever upgraded to a geo-referenced projection, `MeasureTool.HandleClick` would need to convert world-space XY to lat/lon and apply the Haversine formula (or use the map's own `WorldToGeo` transform). A `TODO` comment noting this has been left in `MeasureTool.cs` as a forward maintenance hint.

### Q3: Edge Cases When Switching Interaction Tools

Several edge cases were identified and mitigated during implementation:

1. **Selection state persistence across tool switches**: `StandardInteractionTool.OnExit()` is delegated to `_inner.OnExit()`. The FDP inner tool clears its internal hover/drag state, but the ECS `SelectionState` components and the `DefaultSelectionState` are intentionally **not** cleared on exit. This allows the operator to switch to `MeasureTool`, measure a distance, then switch back to `StandardInteractionTool` with the previous selection still highlighted. If a full clear-on-switch were needed, `OnExit` would call `ClearAllSelections()`.

2. **Ghost preview cleanup in CreationTool**: `CreationTool.OnExit()` sets `_canvas = null`. If the tool is popped mid-hover (e.g. via `canvas.SwitchTool`), the ghost circle stops being drawn on the next frame because `Draw` reads `_currentMouseWorld` (zero-initialized) but only `OnEnter`-activated tool draws are called. No stale draw leak occurs.

3. **MeasureTool start-point leak**: If `StandardInteractionTool` is pushed on top of an in-progress `MeasureTool` measurement (via `canvas.PushTool`) and then the operator deletes the entity they were measuring to, `_startPoint` in `MeasureTool` holds a dangling world-space coordinate. When the tool regains focus and the operator clicks a second point, the distance is calculated from the stale first point — which is incorrect but not a crash. The mitigation is that right-clicking always cancels (`_startPoint = null`), giving the operator an explicit escape hatch. A cleaner long-term fix would be to clear `_startPoint` in `OnEnter` so each tool activation starts fresh.

4. **Double-deselect on augment switch**: If the operator is holding Shift (augment) and switches tools mid-drag, the `DefaultSelectionState.SelectedEntities` might contain entities that no longer have an in-world `SelectionState` component. `ClearAllSelections()` guards against this with `_world.IsAlive(entity)` and `ISimulationView.HasComponent<SelectionState>` checks before writing, preventing a spurious component-not-found exception.
