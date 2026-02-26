# IG-BATCH-04-REPORT: Advanced Base Rendering & Culling

**Batch:** IG-BATCH-04  
**Tasks Completed:** IG.2.3, IG.2.4, IG.2.5  
**Test Results:** 92 / 92 passing (includes all prior-batch tests)  
**Status:** ✅ COMPLETE

---

## Summary of Changes

### Task IG.2.3 — SstVisualizerAdapter

New files:

- **`Bagira.IG/Adapters/SstVisualizerAdapterConstants.cs`** — Single source of truth for every magic number the adapter uses: asset base path (`assets/symbols/`), fallback circle radius (`10 px`), label offset/size, selection radius, damage bar dimensions, LOD scale multipliers, and the `HitRadiusWorldUnits` constant (`FallbackCircleRadiusPx / IgCameraConstants.InitialZoom = 20f`).

- **`Bagira.IG/Adapters/SstVisualizerAdapter.cs`** — `IVisualizerAdapter` implementation. `GetPosition` reads `SimTransform` and `CullingState`; returns `null` (skip draw) when either component is absent or `IsVisible == false`. `Render` resolves the full `ResolvedStyle` pipeline: affiliation → tint, texture name → lazy Raylib `Texture2D` load (cached in `Dictionary<string, Texture2D>`), fallback to a tinted circle when the texture file is absent. LOD is read from `CullingState.LodLevel`: `LodIconOnly` halves the draw scale. Damage bar overlaid as a coloured `DrawRectangle` strip below the entity icon when `DamageLevel > 0`. `GetHoverLabel` returns `ResolvedStyle.GetLabelText()` or `null` when the label is empty.

Modified:

- **`Bagira.IG/IgApplication.cs`** — Swapped `StubVisualizerAdapter` → `SstVisualizerAdapter`. Added `_userConfig` and `_cameraViewport` fields; `InitializeEcs` registers `ResolvedStyle` and `CullingState` component tables; `InitializeNetwork` registers `StyleResolutionModule` and `MapCullingModule`; the `Run()` loop updates `_cameraViewport` bounds each frame by projecting the four screen corners through `_camera.ScreenToWorld`.

Test file:

- **`Bagira.IG.Tests/SstVisualizerAdapterTests.cs`** — 13 tests covering: no-`SimTransform` → null, no-`CullingState` → null, `IsVisible=false` → null, `IsVisible=true` → correct XY coords, Z component not included, LOD-simplified path still returns position, `GetHitRadius` returns the expected world-unit constant, `GetHoverLabel` returns null when no `ResolvedStyle`, null when label is empty string, correct label text when set, and two entities returning distinct hover labels independently.

---

### Task IG.2.4 — MapCullingSystem

New files:

- **`Bagira.IG/Components/CullingState.cs`** — `[StructLayout(Sequential, Pack=1)]` unmanaged struct with two fields: `bool IsVisible` (1 byte) and `byte LodLevel` (1 byte). Total 2 bytes. Zero allocation; written into the unmanaged component table.

- **`Bagira.IG/Components/CullingStateConstants.cs`** — Named LOD level constants (`LodFull=0`, `LodSimplified=1`, `LodIconOnly=2`) and zoom thresholds (`LodIconOnlyZoomThreshold=0.1f`, `LodSimplifiedZoomThreshold=0.5f`).

- **`Bagira.IG/Systems/MapCameraViewport.cs`** — Application-owned plain C# class (not an ECS component) that holds the current camera's world-space bounding box (`WorldMinX/MaxX/MinY/MaxY`) and `Zoom` level. Exposes `Contains(float x, float y) → bool` for the hot-path AABB test. Defaults `Zoom` to `IgCameraConstants.InitialZoom`.

- **`Bagira.IG/Systems/MapCullingSystem.cs`** — `[UpdateInPhase(SystemPhase.PostSimulation)]`, implements `IModuleSystem`. Constructor receives `MapCameraViewport`. Each `Execute` call: (1) copies five viewport scalars into locals (`minX/maxX/minY/maxY/zoom`) to avoid repeated property fetches inside the loop; (2) derives `byte lod` once per frame from the zoom level; (3) iterates `With<SimTransform>` query; (4) calls `cmd.AddComponent` (upsert path) with a new `CullingState` built from `x >= minX && x <= maxX && y >= minY && y <= maxY`. No heap allocations anywhere in the hot path.

- **`Bagira.IG/Modules/StyleResolutionModule.cs`** — `IModule` wrapper for `StyleResolutionSystem`. Name = `"StyleResolution"`, policy = `ExecutionPolicy.Synchronous()`.

- **`Bagira.IG/Modules/MapCullingModule.cs`** — `IModule` wrapper for `MapCullingSystem`. Name = `"MapCulling"`, policy = `ExecutionPolicy.Synchronous()`.

Test file:

- **`Bagira.IG.Tests/MapCullingSystemTests.cs`** — 14 tests: entity inside bounds → visible, entity left/right/above/below → not visible, entity exactly on each boundary edge → visible, entity 1 unit outside right boundary → not visible, `LodIconOnly`/`LodSimplified`/`LodFull`/exact-threshold zoom → correct LOD assigned, entity leaving viewport on second tick → `IsVisible` updated to false, entity entering on second tick → `IsVisible` updated to true, multiple entities tagged independently, entity with no prior `CullingState` → component added correctly.

---

### Task IG.2.5 — Integration Test: 100 Entities

New file:

- **`Bagira.IG.Tests/LayerRenderingIntegrationTests.cs`** — 4 integration tests wiring `StyleResolutionSystem` → `MapCullingSystem` end-to-end:
  1. **`PipelineTick_100Entities_Exactly50MarkedVisible`** — 100 entities spaced 100 world units apart along X; viewport covers X ∈ [0, 5000], so exactly 50 entities (indices 0–49) fall inside. Asserts `visible == 50`.
  2. **`StyleResolutionSystem_100Entities_AllReceiveResolvedStyle`** — 100 entities all receive `IgSymbolOverride`. After one `StyleResolutionSystem` tick all 100 have a `ResolvedStyle` component.
  3. **`PipelineTick_AffiliationTintsResolvedCorrectly`** — One Friend entity and one Hostile entity run through the full two-system pipeline. Asserts correct per-channel RGBA values and `ForceId` enum values for each; asserts both are `IsVisible=true` (both in-viewport).
  4. **`PipelineTick_CameraPan_ShiftsVisibleSet`** — First culling pass confirms left 50 entities visible; viewport shifted right by 5000 units; second pass confirms right 50 now visible and left 50 invisible.

---

## Developer Insights

### Q1: Test hurdles with Raylib bounds checking in the visualiser

The primary challenge was that `SstVisualizerAdapter.Render` calls `Raylib.DrawCircle`, `Raylib.DrawTexturePro`, and `Raylib.DrawRectangle` — none of which can be invoked in a headless xUnit runner because Raylib requires an active OpenGL context.

The solution was to draw the test boundary at `GetPosition` and `GetHoverLabel`, which read only from ECS component data and perform no Raylib calls. `GetPosition` is the gate the `MapCanvas` calls first; if it returns `null` the render path is never reached. All test-observable behaviour — visibility, coordinate extraction, label text, hit radius — is fully exercised without touching the render methods.

This meant the `Render` method itself is not directly unit-tested; it is integration-tested at the application level during manual visual checks. The design intentionally keeps the testable query logic (`GetPosition`, `GetHoverLabel`, `GetHitRadius`) separate from the drawing logic (`Render`) to maintain this clean seam.

One secondary hurdle: `SstVisualizerAdapter` stores a `Dictionary<string, Texture2D> _textureCache`. `Texture2D` is a Raylib value type, so constructing one outside a GL context is safe — it is just a struct of integers. The lazy-load path (`Raylib.LoadTexture(path)`) is only entered inside `Render`, which is never called in tests, so no GL call escapes into the test suite.

---

### Q2: Culling loop timing for 100 entities; refactoring targets for 10k

The `PipelineTick_100Entities_Exactly50MarkedVisible` integration test completes in approximately **1 ms** wall time (observed in the xUnit runner output: `[1 ms]`). This includes one full `StyleResolutionSystem` tick and one `MapCullingSystem` tick over 100 entities.

Extrapolating linearly, 10 000 entities would take ~100 ms per pipeline tick — unacceptable for a 16 ms frame budget.

The most impactful refactoring targets are:

1. **`view.HasManagedComponent` + `view.GetManagedComponentRO` per entity in `StyleResolutionSystem`:** Two dictionary lookups per entity per frame for `IgVisualDef` and `IgSymbolOverride`. At 10k entities this is ~20k dictionary reads. A `TryGetManagedComponent<T>` combining the two calls into one, or a presence-bitset on entities with any managed override, would halve or eliminate most of this cost.

2. **`cmd.AddComponent` in `MapCullingSystem`:** Currently issues a command for every entity every tick, even when `IsVisible` has not changed. A read-modify-write guard (`if (!repo.HasComponent || current != next) cmd.AddComponent(...)`) would reduce command buffer pressure by ~90 % in steady state when entities are not crossing the viewport boundary.

3. **Archetype chunking:** The FDP query iterates entities individually. Grouping entities by archetype chunk (all entities with `SimTransform` in contiguous memory blocks) would enable auto-vectorisation of the AABB test via SIMD, which is the standard approach for culling loops at scale.

---

### Q3: Shapes vs textures in SstVisualizerAdapter

The implementation **uses shapes as the primary render path** with textures as an optional upgrade:

- If `ResolvedStyle.GetTextureName()` returns a non-empty string, `SstVisualizerAdapter.Render` attempts to load `assets/symbols/<textureName>.png` via `Raylib.LoadTexture` and caches the result. If the file exists and the texture ID is nonzero, it is drawn with `DrawTexturePro` tinted by the affiliation RGBA values from `ResolvedStyle`.

- If the file does not exist (the common case during development before TKB symbol assets are imported), the fallback is a tinted `DrawCircle` at `SstVisualizerAdapterConstants.FallbackCircleRadiusPx = 10` pixels. The circle uses exactly the same RGBA tint as the texture path, so affiliation colours (blue/red/green/white) are visually correct in both cases.

This approach means the adapter is fully functional — with correct affiliation colour semantics — before any texture assets exist, and upgrades transparently to textured symbols as soon as `.png` files are placed in the asset directory.
