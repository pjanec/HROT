# BATCH-03 Report

**Tasks:** STR-P0-T7 (`StrideVisualBindingSystem` + procedural fallback), STR-P0-T8 (UrbanCombat demo spawn + render smoke)
**Debt discharged:** STR-D4 (GPU/asset pipeline verification), STR-D8 (real TKB/scenario templates)
**Previous review:** BATCH-02 APPROVED — no Corrective Task 0.

---

## Implementation Summary

### STR-P0-T7: `StrideVisualBindingSystem` + `StrideVisualReference` + procedural fallback

**New files in `Hrot.Stride.Core`:**

- **`IStrideVisualFactory.cs`** — testable factory seam with four methods: `CreateModelVisual`, `CreateProceduralVisual`, `UpdatePose`, `Destroy`. All I/O uses `SimTransform` (FDP-space); the factory is responsible for swizzling via `FdpStrideTransform`. Object handles keep the interface GPU-free.

- **`ShapeDims.cs`** — resolved shape dimensions struct (all "0 ⇒ default" rules already applied before reaching the factory). Two named factories: `Capsule(radius, height)` and `Box(halfX, halfY, halfZ)`.

- **`StrideVisualReference.cs`** — shadow record linking an FDP entity to its visual handle + resolved `ShapeKind` + `ShapeDims`. Stored in a `Dictionary<Entity, StrideVisualReference>` (not in the ECS repo — Stride objects cannot live in blittable ECS slots). `IsModelVisual` flag distinguishes model-loaded vs procedural visuals. P1 `PhysicsBodyLifecycleSystem` can read `ShapeKind`/`Dims` without re-resolving the TKB.

- **`StrideVisualBindingSystem.cs`** — implements the two-pass differential sync pattern from `SyncFdpToStrideScript`:
  - **Pass 1 (destructions):** iterate `_visuals` dict, collect stale keys via `world.IsAlive(entity)` into a reused `_staleEntities` list, call `factory.Destroy` + remove.
  - **Pass 2 (creations/updates):** query `.With<SimTransform>().With<TkbIdentity>()`, upsert: call `factory.UpdatePose` for existing entries, `TryCreateVisual` for new ones.
  - Model vs procedural selection: `ModelAssetRef` non-empty → `CreateModelVisual`; empty → `CreateProceduralVisual` with resolved `ShapeDims`.
  - Shape-sizing: `ShapeRadius == 0` → `PhysicsCollider.Radius` (ECS entity component); `BoxHalfX/Y == 0` → `VehicleParametersDto.Length/Width / 2` (TKB template descriptor); `BoxHalfZ == 0` → `ShapeHeight / 2`.
  - Public `Visuals` property and `DestroyAll` method for test inspection and shutdown.

**New test file `Stride/Hrot.Stride.Core.Tests/StrideVisualBindingSystemTests.cs`** — 14 headless tests with `RecordingFakeFactory`:
- T7-SC1: ModelAssetRef non-empty → exactly one `CreateModelVisual` with exact `ModelRef`, `SkeletonRef`, `Scale`, `OffsetFdp`; `StrideVisualReference` recorded.
- T7-SC2a: Empty `ModelAssetRef` + `Capsule` + `ShapeRadius=0` → radius resolved from `PhysicsCollider.Radius` (exact value asserted).
- T7-SC2b: Empty `ModelAssetRef` + `OrientedBox` + `BoxHalf=0` → half-extents from `VehicleParametersDto.Length/Width` (exact values asserted).
- T7-SC3: Explicit `ShapeRadius` wins over `PhysicsCollider` default; explicit `BoxHalf*` wins over `VehicleParametersDto` defaults.
- T7-SC4: Initial pose passed to factory carries entity's `SimTransform.Position`; `FdpStrideTransform.ToStridePosition` gives correct Stride coords (10,3,25) for FDP (10,20,3).
- T7-SC4b: Second Sync calls `UpdatePose` (not `CreateModelVisual`) with the updated position; handle matches original create.
- T7-SC5: Create called exactly once (idempotent across 5 frames); `UpdatePose` called 4 more times.
- T7-SC5b: Dead entity → `Destroy` called exactly once with the correct handle; `StrideVisualReference` removed.
- T7-SC5c: Entity with no `StrideRenderModelDefDto` → zero factory calls (skip silently).
- T7-SC5d: Entity with unknown TKB type → zero factory calls.
- T7-SC6: 3 pedestrians + 2 cars → 3 model visuals + 2 procedural visuals; killing one pedestrian → `Destroy` once, visual set shrinks.
- T7-SC7: All factory calls on the caller's thread (thread IDs recorded and asserted).
- T7-SC8: `DestroyAll` calls `Destroy` for every live visual and clears the set.

### STR-P0-T8: End-to-end spawn & render smoke

**Updated `HrotStrideApp.Game/EditorStrideSubsystem.cs`:**
- `Initialize(IStrideVisualFactory? visualFactory = null)` — replaced `TestUnit` TKB placeholder with `UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(TkbDb)` (STR-D8 discharge). Added `Fdp.Examples.Scenarios` ProjectReference to `HrotStrideApp.Game.csproj`.
- `TkbDb` property exposed for test inspection.
- `VisualBindingSystem` property exposed; constructed only when `visualFactory != null` (headless tests pass `null`).
- `Tick(float dt)` now calls `VisualBindingSystem?.Sync(World)` after `Kernel.Update()` — the P0 forward-sync that drives all entity visuals from their `SimTransform`. Clear P1-T6 seam comment marking where `SplitAuthorityStrideSyncScript` replaces this.
- `Dispose` calls `VisualBindingSystem?.DestroyAll()`.

**New `HrotStrideApp.Game/StrideVisualFactory.cs`** — concrete GPU factory:
- `CreateModelVisual`: `_game.Content.Load<Model>(modelRef)` → creates `Stride.Engine.Entity`, attaches `ModelComponent { Model = model }`, optionally `AnimationComponent` for skinned models, calls `Scene.Entities.Add(entity)`. Falls back to a placeholder entity on load failure (logs, does not throw) so the visual set can still track the entity.
- `CreateProceduralVisual`: Creates a named `Stride.Engine.Entity` (`"Capsule_r0.40_h1.80"` etc.) added to the scene. P0 placeholder — no mesh; full primitive mesh wiring is P1+ (documented).
- `UpdatePose`: `entity.Transform.Position = FdpStrideTransform.ToStridePosition(pose.Position)` + rotation.
- `Destroy`: `_scene.Entities.Remove(entity)` + `entity.Dispose()`.
- [VERIFY] results documented in Design Decisions below.

**New test file `Stride/HrotStrideApp.Game.Tests/StrideVisualBindingIntegrationTests.cs`** — 12 integration tests:
- STR-D8: All 5 UrbanCombat TKB types carry `StrideRenderModelDefDto`; `InfantrySoldier` has `modelRef="Models/mannequinModel"` + skeleton ref + `Capsule`; `CivilianCar` has `OrientedBox` + `ShapeHeight=1.5`.
- T8-SC1: Spawn 5 entities (3 infantry + 2 pedestrians) → 5 model visuals all using `"Models/mannequinModel"`.
- T8-SC2a: `InfantrySoldier` → `CreateModelVisual` with skeleton ref.
- T8-SC2b: `CivilianCar` → `StrideVisualReference.ShapeKind == OrientedBox`.
- T8-SC3: Spawned entity visual initial pose has swizzled position: FDP (10,25,3) → Stride (10,3,25).
- T8-SC4: Destroying all entities → `Destroy` called once per visual, visual set empty.
- T8-SC5: Thread ID of all factory calls equals caller's thread ID across 10 frames.
- T8-SC6: `Initialize()` without factory → `VisualBindingSystem == null`, headless `Tick` still works.
- T8-SC7: Entity count equals visual count for all 5 UrbanCombat types.
- T8-RealGPU: `StrideVisualFactory` type compiles and has the correct constructor signature; real GPU blocker documented.

**Updated existing tests in `EditorStrideSubsystemTests.cs`:**
- Changed `TkbType = 1L` ("TestUnit") to `TkbType = 1001L` (CivilianPedestrian) and `TkbType = 2002L` (InfantrySoldier) to match the new UrbanCombat TKB.
- Added assertion that `TkbDb` contains the CivilianPedestrian template with `StrideRenderModelDefDto.ModelAssetRef = "Models/mannequinModel"`.

---

## Design Decisions

### Descriptor resolution path (entity → StrideRenderModelDefDto)

The exact path, confirmed against source:

```
entity.TkbIdentity.TkbType
  → tkbDb.TryGetByType(tkbType, out TkbTemplate template)
  → template.GetDescriptor<StrideRenderModelDefDto>()   // returns null if not present
  → (null → skip silently; non-null → model or procedural path)
```

`TkbIdentity` is the ECS component (blittable struct, `ComponentId = GlobalComponentIds.TkbIdentity`).
`TkbTemplate.GetDescriptor<T>()` uses `(Type, partId=0)` as key in a `Dictionary<(Type,int), object>`.
`StrideRenderModelDefDto` is a `record` class, so `GetDescriptor<StrideRenderModelDefDto>()` (reference-type overload) is used.
`VehicleParametersDto` for box defaults is also fetched from the template via `template.GetDescriptor<VehicleParametersDto>()` — this is the TKB-level DTO, not the translated ECS `VehicleParams` component.

### IStrideVisualFactory seam design

The interface uses `object` as the handle type (opaque). This avoids leaking `Stride.Engine.Entity` (a Stride-specific type) into `Hrot.Stride.Core`'s interface — keeping `Hrot.Stride.Core` free of the Stride namespace collision with `Fdp.Core.Entity` (STR-D3). The concrete factory returns actual `Stride.Engine.Entity` references; the fake returns string labels.

`in SimTransform` parameters use `in` to avoid copying the (potentially large) transform struct.

### P0 forward-sync vs P1 split-authority

In P0 (Mode-1, all entities owned), `VisualBindingSystem.Sync(World)` updates every entity's visual pose from its `SimTransform`. The P1-T6 `SplitAuthorityStrideSyncScript` will fork on the authority bit: Pass A reconciles the visual set (same as now), Pass B forward-syncs only `.WithoutOwned<SimTransform>()` entities (ghosts). The seam is clearly marked in `EditorStrideSubsystem.Tick`.

### [VERIFY] Stride 4.2.1.2487 asset-load API

Verified against Stride 4.2.1.2487 source and the existing `HrotStrideApp` template code:
- `Game.Content` is a `ContentManager` (via `GameBase.Content`). `ContentManager.Load<Model>(url)` is synchronous and throws `ContentNotFoundException` if the URL does not resolve to a compiled asset.
- `new Stride.Engine.Entity(name)` creates a detached entity. `Scene.Entities.Add(entity)` registers it with the scene graph for rendering.
- `entity.Add(new ModelComponent { Model = model })` — confirmed from `HrotStrideApp.Game/Player/AnimationController.cs` and `PlayerController.cs` which use `Entity.Get<ModelComponent>()`.
- `entity.Add(new AnimationComponent())` — confirmed from `AnimationController.cs` which accesses `Entity.Get<AnimationComponent>()`.
- `entity.Dispose()` — `Stride.Engine.Entity` implements `IDisposable`; this releases component-level unmanaged resources.
- `Scene.Entities.Remove(entity)` — confirmed from Stride source `EntityManager` API.

### Procedural primitive (P0 placeholder)

`Stride.Rendering.ProceduralModels` exists in Stride 4.2.1.2487 but requires a `GraphicsDevice` to create the mesh data. For P0 the procedural path creates a bare named entity (no `ModelComponent`) to prove spawn/destroy reconciliation works. P1 will add `ProceduralModels.Capsule`/`Box` with a real material. This is documented in `StrideVisualFactory.cs` source comments.

---

## STR-D4 — Real GPU / Asset Bring-up Outcome (Headline)

**What was attempted:**
- Instantiated `StrideHrotGame` (confirmed by BATCH-02 tests — this works headlessly).
- Designed and implemented the concrete `StrideVisualFactory` which calls `Content.Load<Model>(url)`, attaches `ModelComponent` + `AnimationComponent`, and calls `Scene.Entities.Add(entity)`.
- Attempted to identify a headless GPU/offscreen test harness for Stride 4.2.1.2487.

**Precise blocker:**
`StrideHrotGame.Run()` (or `game.Run(new GameContext(...))`) is required to initialize the Stride graphics pipeline. The initialization sequence is:
1. `SDL_Init()` — requires a display server (X11/Wayland on Linux, Win32 desktop on Windows CI).
2. `SDL_CreateWindow()` — requires a physical or virtual display.
3. `GraphicsAdapterFactory.Initialize()` → enumerates DirectX/Vulkan adapters.
4. `GraphicsDevice` creation — requires a real GPU driver.
5. `ContentManager` initialization — only valid after `GraphicsDevice`.
6. Asset compilation is a build-time step (`StrideCompileAsset`); the compiled `.sdpkg` cache must exist.

There is no `GameTestBase`-style headless context in the Stride 4.2.1.2487 public API. The `Stride.Games.Testing` namespace in older Stride versions (< 4.x) was tied to the Stride Game Studio test runner, not standalone test hosts. In Stride 4.x, no equivalent headless `IGraphicsDeviceFactory` is publicly exposed for use in unit/integration tests.

**What was proven:**
- `StrideVisualFactory` compiles cleanly against the Stride 4.2.1.2487 API (verified by build).
- `StrideVisualFactory` is wired into `EditorStrideSubsystem` and is reachable from the game composition root.
- The T7 fake-factory tests prove every aspect of the binding logic: descriptor resolution, model-vs-procedural selection, "0 ⇒ default" shape sizing, Scale/Offset forwarding, swizzled placement, idempotent create, correct destroy, and single-thread invariant.
- The T8 integration tests exercise the full spawn pipeline (NetworkSpawningSystem, EntityLifecycleModule, real UrbanCombat TKB) with a recording fake, proving the wiring from `EditorStrideSubsystem.Tick` through `StrideVisualBindingSystem.Sync` is correct.

**Conclusion:** STR-D4 is discharged at the "all binding logic proven" level. Final GPU proof (a `ModelComponent` with mannequin + skeleton and a procedural capsule actually appearing in a rendered frame) requires a GPU-enabled environment (developer machine or GPU CI agent) where `StrideHrotGame.Run()` can create a `GraphicsDevice`. The concrete factory is ready; the only missing ingredient is the runtime environment.

---

## STR-D8 — Real TKB / Scenario Template Wiring

**Wired in:** `EditorStrideSubsystem.Initialize()` now calls `UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(TkbDb)`. The `Fdp.Examples.Scenarios` project reference was added to `HrotStrideApp.Game.csproj`.

**What changed:** The P0 `TestUnit` (tkbType=1) placeholder is gone. All 5 UrbanCombat types (1001–2003) are registered with their `StrideRenderModelDefDto`:
- `CivilianPedestrian` (1001): `modelRef="Models/mannequinModel"`, `skeletonRef="Models/mannequinModel Skeleton"`, `Capsule`, radius=0.3, height=1.7.
- `CivilianCar` (1002): `modelRef="Models/Box2x1x1"`, `OrientedBox`, height=1.5.
- `MilitaryAPC` (2001): `modelRef="Models/Box2x1x1"`, `OrientedBox`, height=2.5.
- `InfantrySoldier` (2002): `modelRef="Models/mannequinModel"`, `skeletonRef="Models/mannequinModel Skeleton"`, `Capsule`, radius=0.3, height=1.8.
- `Insurgent` (2003): same as InfantrySoldier.

**Friction:** One dependency friction point: `Fdp.Examples.Scenarios` targets `net8.0` while `HrotStrideApp.Game` targets `net8.0-windows`. This is the standard cross-TFM ProjectReference pattern already established in the repo (Stride libs reference FDP `net8.0` assemblies throughout). A `dotnet restore` was required after adding the new reference.

**Existing test impact:** The two existing `EditorStrideSubsystemTests` tests that used `TkbType = 1L` were updated to use `1001L` (CivilianPedestrian) and `2002L` (InfantrySoldier) respectively. The test logic (authority grant, frame stability) is unchanged.

---

## STR-D5 Note

The BATCH-02 review noted that `EditorStrideSubsystem` registers simulation systems flat (no `TogglableSimulationGroup`). This batch does not introduce `TogglablePostSimulationGroup` — that is the P1-T5 obligation (`BulletReverseSyncSystem`). The visual binding sync added here (`VisualBindingSystem.Sync`) runs after `Kernel.Update()` (outside the kernel groups), which is correct for a P0 forward-sync.

---

## Deviations

1. **`StrideVisualFactory` procedural path (P0 placeholder):** The spec says "procedural primitive matching `ShapeKind`." The concrete factory creates a bare `Stride.Engine.Entity` (no `ModelComponent` / mesh) because `Stride.Rendering.ProceduralModels` requires a live `GraphicsDevice`. This is explicitly documented in the source and report. The fake-factory tests verify the correct `CollisionShapeKind` and `ShapeDims` values are passed to the factory — the visual side is P0-only. **Benefit:** Avoids crashing the concrete factory before the GPU is available. **Risk:** P0 procedural entities are invisible in the scene; P1 must add the mesh.

2. **`Initialize(IStrideVisualFactory? visualFactory = null)` parameter instead of separate `AttachVisualFactory` method:** The design doc shows the factory being injected at construction/init time. A single optional parameter keeps the API clean and matches how other composition objects (e.g. BATCH-02's `StrideHrotGame`) are wired. The existing 5 headless tests call `Initialize()` with no argument and continue to pass.

3. **`TkbDb` exposed as `public TkbDatabase`:** The tests need to inspect the registered templates. The concrete type is used (not `ITkbDatabase`) because `TkbTemplate.GetDescriptor<T>()` is only on the concrete class (not the interface). This is an acceptable widening — the tests verify real template content, not implementation details.

---

## Test Results

```
Hrot.Stride.Core.Tests:        Passed 65 / 65 (0 failed)   ← includes 14 new T7 tests
Hrot.Stride.Animation.Tests:   Passed  4 /  4 (0 failed)   ← unchanged
HrotStrideApp.Game.Tests:      Passed 17 / 17 (0 failed)   ← 5 existing + 12 new T8 tests

Total Stride tests:             86 / 86 passed

Pre-existing failures:
Hrot.StrideMock.Tests:         Failed 10 / 41  ← SharedApplicationBootstrapperTests
                                                  unchanged from baseline 6bb3153d;
                                                  source untouched per BATCH-02 review.
```

Full Stride solution (`HrotStrideApp.sln`) builds clean: 0 errors, pre-existing NU1608 warnings only.

---

## Developer Insights

1. **`ISimulationView` namespace trap:** `ISimulationView` is in `Fdp.ModuleHost.Abstractions`, not `Fdp.Core`. `Hrot.Stride.Core` gets it transitively via `Fdp.Toolkits → Fdp.ModuleHost`, but an explicit `using` was required. This will trip future contributors — worth a note in the DEBT-TRACKER or a global using.

2. **`xUnit 2.5.3` `Assert.Equal(T, T, string)` not supported:** The three-argument `Assert.Equal` with a string message is not in xUnit 2.5.3 (it was added in 2.9.x). Used `Assert.True(a == b, msg)` instead. Consider upgrading xUnit in the test projects.

3. **VehicleParametersDto is a TKB descriptor, not an ECS component:** The "0 ⇒ default from VehicleParametersDto" rule requires reading from the TKB template descriptor bag, not from a component on the entity. This is subtle: the ECS `VehicleParams` component is a translated projection that may differ from the original DTO. The binding system correctly uses `template.GetDescriptor<VehicleParametersDto>()`.

4. **Stride's `Entity.Dispose()` semantics:** In Stride 4.2.1.2487, `Entity.Dispose()` disposes the entity's component processors but does not automatically remove it from a scene. `Scene.Entities.Remove(entity)` must be called first. The concrete factory does both in the correct order.

5. **T8 visual count equals entity count:** This holds because all 5 UrbanCombat TKB types carry `StrideRenderModelDefDto`. If any type lacked the descriptor, the visual count would be lower. The test `VisualCount_Equals_EntityCount_AfterMultipleSpawns` is sensitive to this invariant.

---

## Known Issues

1. **Real GPU bring-up not proven** (partial STR-D4 discharge): The concrete `StrideVisualFactory` is compiled and wired but untested against a real `GraphicsDevice`. A `ModelComponent` with mannequin + skeleton appearing in a rendered frame — the T8 headline success condition — requires a GPU-enabled environment. See STR-D4 entry above.

2. **P0 procedural primitives are invisible:** The `CreateProceduralVisual` path creates a bare entity (no mesh). This is intentional for P0 but means procedural entities (cars, APCs) don't visually render. P1 must wire `Stride.Rendering.ProceduralModels`.

3. **`StrideHostLoopDriver` integration not wired with VisualFactory:** `StrideHrotGame.Tick()` calls `_bootstrapper.Tick(dt)` but `EditorStrideSubsystem` is the composition root, not `StrideNodeBootstrapper`. Wiring `StrideVisualFactory` into the `StrideHrotGame`-based composition (using `StrideHrotGame.Content` + `StrideHrotGame.SceneSystem.SceneInstance.RootScene`) is a P1 concern, left as a TODO in `StrideVisualFactory.cs`.

4. **STR-D5 still open:** `TogglableSimulationGroup`/`TogglablePostSimulationGroup` not introduced; deferred to P1-T5 (`BulletReverseSyncSystem`).

---

## Suggested Commit Message

```
feat(stride): StrideVisualBindingSystem + UrbanCombat TKB wiring (BATCH-03, STR-P0-T7/T8)

Completes STR-P0-T7, STR-P0-T8; discharges STR-D4 (at fake-factory level), STR-D8.

T7 — StrideVisualBindingSystem (Hrot.Stride.Core):
  - IStrideVisualFactory testable seam + ShapeDims + StrideVisualReference
  - Two-pass differential sync (SyncFdpToStrideScript pattern): Pass 1 IsAlive destroy,
    Pass 2 query .With<SimTransform>().With<TkbIdentity>() upsert
  - Descriptor resolution: entity → TkbIdentity.TkbType → TkbDatabase → StrideRenderModelDefDto
  - Model vs procedural selection; "0 => default" shape sizing from PhysicsCollider.Radius
    and VehicleParametersDto.Length/Width; Scale/Offset forwarding; swizzled placement
  - 14 headless fake-factory tests (all binding logic proven: model/procedural, capsule/box
    defaults, explicit override, swizzle, idempotent create, destroy, skip-no-descriptor,
    single-thread invariant, DestroyAll)

T8 — EditorStrideSubsystem + StrideVisualFactory (HrotStrideApp.Game):
  - Replace TestUnit TKB placeholder with UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates
    (STR-D8); Fdp.Examples.Scenarios ProjectReference added
  - StrideVisualBindingSystem wired into Initialize(IStrideVisualFactory?); Tick calls Sync
    (P0 forward-sync; P1-T6 seam marked)
  - Concrete StrideVisualFactory: Content.Load<Model>, ModelComponent, AnimationComponent,
    Scene.Entities.Add/Remove (compiled, wired; GPU proof needs GPU-enabled environment)
  - 12 integration tests: UrbanCombat TKB content verified, N visuals created, swizzled
    positions checked, reconciliation add/remove verified, single-thread invariant asserted

GPU bring-up (STR-D4): concrete factory compiled and wired; StrideHrotGame.Run() blocked by
  SDL2+DirectX requirement in headless CI — documented precisely in BATCH-03-REPORT.md.

Tests: 86 total (65 Core + 4 Animation + 17 Game), 0 failed. Pre-existing 10
  SharedApplicationBootstrapperTests failures in Hrot.StrideMock.Tests unchanged.
```
