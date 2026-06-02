# BATCH-10 Report
**Task:** STR-LIVE-1 — Live bring-up: make `editor_stride` actually run and render

## Implementation Summary

### Task 1: Entry Point
`Stride/HrotStrideApp.Windows/HrotStrideAppApp.cs` changed from `new Game()` to `new StrideHrotGame()`. The `StrideHrotGame.Run()` call without arguments uses Stride's own internal SDL game loop — no external RunCallback or SDL pump needed (that complexity is deferred to P5).

### Task 2: Boot subsystem in `StrideHrotGame`
Two Stride lifecycle overrides added to `StrideHrotGame`:

**`BeginRun()` override** — one-time setup hook:
- Gets `SceneSystem.SceneInstance.RootScene` (the loaded MainScene)
- Calls `NeutralizeTemplatePlayer(scene)` to remove the `PlayerCharacter` entity and its children
- Calls `AddFixedCamera(scene)` to add a fixed overview camera + directional light
- Constructs `StrideVisualFactory(this, scene)` and `EditorStrideSubsystem`, calls `Initialize(visualFactory)`
- Calls `EnqueueDemoSpawns()` to queue 6 UrbanCombat entities

**`Update(GameTime)` override** — per-frame driver:
- Calls `base.Update(gameTime)` (Stride's own update chain)
- Calls `_loopDriver.AdvanceFrame(wallDt, dt => _editorSubsystem.Tick(dt))` to drive the FDP simulation at fixed timestep

The existing `Tick(float)` / `AttachBootstrapper` path is preserved for backward compatibility (the external-loop P5 mode).

### Task 3: Camera/scene changes
**What was neutralized:**
- `PlayerCharacter` entity (and its children `CameraTarget` and `Camera`) was removed from the scene via `RemoveEntityAndChildren()` — a recursive helper that removes all children before the root.
- This kills `PlayerController`, `PlayerInput` (crash-prone: needs a live CharacterComponent), and `ThirdPersonCamera` (crash-prone: needs a parent + Bullet simulation) without touching any static arena geometry (walls, floors, grid, lights that were already in the scene).

**What was added:**
- `DemoCamera` entity at Stride position (0, 10, −5) with a 60° FoV perspective `CameraComponent`. Rotation is `RotationX(-45°)` which points it toward Stride (0, 0, 5) — roughly a 45° downward angle along the +Z axis.
- `DemoDirectionalLight` entity with `LightDirectional` at −60° pitch, so spawned models are lit from above-and-forward rather than appearing pitch-black.

**Why those camera values:** The static arena grid spans roughly Stride Z=[0,15], X=[−10,+10], Y=0. The spawn area is at Stride Z=5 and Z=7 (see below). Placing the camera at Z=−5, Y=10 puts all spawns roughly 10–12 Stride units away, comfortably within the 1000-unit far clip, and the downward angle keeps the floor + spawned models both in view.

### Task 4: Demo spawns
6 entities enqueued in `EnqueueDemoSpawns()`:

| # | TkbType | FDP position | → Stride position | Model |
|---|---------|-------------|-------------------|-------|
| 1 | 2002 (InfantrySoldier) | (−3, 5, 0) | (−3, 0, 5) | `Models/mannequinModel` |
| 2 | 2002 (InfantrySoldier) | (−1, 5, 0) | (−1, 0, 5) | `Models/mannequinModel` |
| 3 | 2002 (InfantrySoldier) | ( 1, 5, 0) | ( 1, 0, 5) | `Models/mannequinModel` |
| 4 | 2002 (InfantrySoldier) | ( 3, 5, 0) | ( 3, 0, 5) | `Models/mannequinModel` |
| 5 | 2001 (MilitaryAPC) | (−5, 7, 0) | (−5, 0, 7) | `Models/Box2x1x1` |
| 6 | 2001 (MilitaryAPC) | ( 5, 7, 0) | ( 5, 0, 7) | `Models/Box2x1x1` |

All at FDP Z=0 (ground level). FDP→Stride swizzle: Stride=(fdp.X, fdp.Z, fdp.Y).
Spawn pipeline: `EntityCreationRequest { OwnerAppInstanceId=0, TkbType, InitialComponents=[SimTransform, TkbIdentity] }` enqueued to `ScenarioSource` — identical pattern to the integration tests.

### STR-D10 resolved
`StrideVisualFactory.CreateModelVisual`: the silent `Content.Load` catch-and-placeholder is replaced with a loud failure:
1. Logs the full asset URL + exception to `Debug.WriteLine` and `Console.Error`
2. Rethrows as `InvalidOperationException(message, innerException)`

The silent placeholder entity (`CreatePlaceholderEntity` method) and the dead `return CreatePlaceholderEntity(...)` branch are removed. A missing/miscompiled asset now immediately crashes the boot with a clear message naming the asset URL.

## Design Decisions

**`BeginRun` as the boot hook, not `LoadContent`:**
`BeginRun` is documented "Called after all components are initialized, before the game loop starts." In Stride's `Tick()` flow, `BeginRun` fires after `LoadContent` (which loads the scene and populates the asset database). At that point `Content.Load<T>` works and `SceneSystem.SceneInstance.RootScene` is the live MainScene. Scripts on scene entities have NOT yet had `Start()` called — that happens on the first `Update()` cycle. This is the ideal window: scene is ready, scripts haven't fired, so removing `PlayerCharacter` before scripts start is safe and avoids any `Start()` exceptions.

**Remove `PlayerCharacter` entirely rather than disabling scripts:**
Removing the entity is safer and cleaner than trying to remove individual script components. The entity carries a `CharacterComponent` (Bullet physics) that would otherwise step in the physics simulation even with scripts disabled. Total removal eliminates all side effects and is easy to reason about.

**Simple `RotationX(-45°)` for camera rather than a LookAt:**
Stride 4.2 does not expose a public `Matrix.CreateLookAt`-style helper on `TransformComponent`. The -45° pitch with yaw=0 is correct for a camera at (0,10,-5) looking toward (0,0,5): the pitch angle = atan2(−(10), 10) ≈ −45°. A proper LookAt would produce the same quaternion in this case (yaw=0, no roll).

**`Update(GameTime)` drives subsystem, not `Tick(float)`:**
The batch says "override `Update(GameTime)` — do NOT build the fully-external SDL pump here." The `Update(GameTime)` approach uses Stride's normal `Run()` loop, avoiding the SDL2 manual pump complexity that belongs to P5. The existing `Tick(float)` path (for P5 external loop) is preserved unchanged.

## Deviations

**No deviation from the spec.** All four sub-tasks implemented as described.

One note: `CameraProjectionMode` is in `Stride.Engine.Processors` namespace, requiring an additional `using`. This is a [VERIFY] fact not mentioned in the batch but easily resolved.

## Test Results

All existing tests pass after the changes. Build is clean (0 errors, pre-existing NU1608 NuGet warnings only).

```
Stride Core suite:  215 / 215 passed   (Hrot.Stride.Core.Tests)
Game suite:          33 /  33 passed   (HrotStrideApp.Game.Tests)
Animation suite:      4 /   4 passed   (Hrot.Stride.Animation.Tests)
```

Total: 252 tests, 0 failures, 0 new warnings introduced.

The pre-existing xUnit2013 warnings (Assert.Equal on collections) in `Hrot.Stride.Core.Tests` are unchanged from the BATCH-09 baseline — not introduced by this batch.

## How to Run and What You Should See

### Build and run
```
cd d:\Work\IOS-IG-SimHost-FDP\Stride
dotnet build HrotStrideApp.sln -c Debug
cd HrotStrideApp.Windows
dotnet run -c Debug
```

Or open `HrotStrideApp.sln` in Visual Studio and press F5 with `HrotStrideApp.Windows` as the startup project.

### What you should see
1. **A Stride window opens** (SDL2 + DirectX backend, 1280×720 default).
2. **The static arena renders**: grid floors, walls — the scene geometry from the MainScene template. No player capsule is present (it was removed).
3. **After approximately 3 frames** (the spawn pipeline takes 2–3 ticks to materialize entities — CreateEntityRequestSystem → SpawnEntityCommand → NetworkSpawningSystem materialization), **6 models appear in the arena**:
   - 4 mannequin figures (InfantrySoldier) standing in a row at roughly the center of the arena — they are small upright humanoid meshes.
   - 2 box models (MilitaryAPC) slightly further back, one on each side.
4. **All models are static** — no movement. Physics is still `NoOpPhysicsBodyService` (STR-D11 is open). This is expected.
5. **The camera** looks down at roughly 45° from slightly behind the arena, framing all 6 spawned entities against the floor grid.
6. **The scene should be lit** — a directional light is added programmatically. The static arena lights from the original template may or may not still be present depending on whether they were in the PlayerCharacter hierarchy (they are NOT — they are separate root entities in the MainScene and are kept).

### What an asset-load failure looks like (STR-D10)
If `Models/mannequinModel` or `Models/Box2x1x1` fails to load (asset not compiled):
- The Stride window immediately terminates with an unhandled exception.
- The exception message is: `[StrideVisualFactory] FATAL: Content.Load<Model> failed for asset 'Models/mannequinModel'. Ensure the asset is compiled in the HrotStrideApp asset pipeline. Inner exception: <ExceptionType>: <message>`
- This also appears on `stderr` and in the debugger output window.
- Fix: open the HrotStrideApp solution in Stride Game Studio and trigger an asset compilation ("Build Assets" or rebuild), which recompiles the model assets.

### How to confirm the spawn succeeded
Press the Window's close button to exit gracefully. Check the console/debugger output — no exception means the spawn loop ran. If models did not appear after ~1 second, check:
1. Is the console showing a `[StrideVisualFactory] FATAL` message? → Asset not compiled.
2. Are there any `NullReferenceException` or `InvalidOperationException` messages? → Boot hook timing issue (report to Lead).

## What Was and Could NOT Be Verified

**Verified (by build + headless tests):**
- The solution builds clean (0 errors) against Stride 4.2.1.2487 APIs.
- `BeginRun()` is a valid override point (documented in `Stride.Games.xml`).
- `SceneSystem.SceneInstance.RootScene` is the correct API (documented in `Stride.Engine.xml`).
- `LightComponent.Type = new LightDirectional()` and `LightComponent.Intensity` are valid properties (documented).
- `CameraComponent.Projection`, `VerticalFieldOfView`, `NearClipPlane`, `FarClipPlane` are valid properties (documented).
- `CameraProjectionMode.Perspective` is in `Stride.Engine.Processors` (confirmed from XML).
- All 252 existing tests still pass; no regressions.
- The spawn pipeline (EntityCreationRequest → FDP kernel → StrideVisualBindingSystem.Sync) is proven correct by the existing integration tests in `HrotStrideApp.Game.Tests`.

**Cannot be verified without a GPU/window:**
- Whether the Stride window actually opens and renders on a GPU machine.
- Whether `BeginRun()` fires at the exact lifecycle point claimed (documented, but not runtime-confirmable headlessly).
- Whether the mannequin and box models actually appear in the rendered view (requires Content compilation + GraphicsDevice + the real StrideVisualFactory path).
- Whether the camera position/angle actually frames the spawned models well (requires a running window).
- Whether the `PlayerCharacter` removal fully prevents ThirdPersonCamera/PlayerController exceptions (scripts have not been run in a real game boot by this agent).
- Whether the directional light is sufficient to illuminate the mannequin model materials.

**I am NOT claiming the render works.** I have wired the code correctly against the verified Stride 4.2.1.2487 APIs and proven it compiles. A human must run it on a GPU machine to confirm the visual result.

## Developer Insights

1. **`BeginRun` vs `LoadContent` for scene access:** Stride's `Game.LoadContent()` is also a valid override but it fires slightly before `BeginRun` and the scene is also valid there. `BeginRun` is cleaner because it is clearly "after everything is ready, before the loop." Either would work.

2. **`PlayerCharacter` child hierarchy:** CameraTarget and Camera are child entities of PlayerCharacter but are independently tracked in the `SceneInstance`. Removing just the root entity from `scene.Entities` does NOT automatically remove children from the scene instance — they remain as orphan entities with a broken parent transform. The `RemoveEntityAndChildren` recursive helper handles this correctly.

3. **Camera slot:** The original `Camera` entity had a `CameraComponent` bound to a specific slot GUID (`9aeac611-d1f6-46da-a235-e20cc154e170`). The new `DemoCamera` does not set a slot. This may or may not be required by the `GraphicsCompositor`. If the human sees a black screen, the camera slot not matching the compositor's expected slot is the most likely cause. Fix: either set `camera.Slot = ...` to the same slot, or update the `GraphicsCompositor` asset.

4. **Spawn timing:** The FDP entity pipeline takes 2–3 fixed-dt ticks to materialize an entity through the full spawn pipeline (CreateEntityRequestSystem → SpawnEntityCommand → NetworkSpawningSystem). Since we enqueue in `BeginRun` but the first ticks fire in `Update`, models will appear after the first few frames — not frame 0. This is expected and was already proven by the integration tests.

5. **STR-D13 still open:** Owned entities' visuals are not pose-synced by `SplitSync.PassB` (which only syncs non-owned). With `NoOpPhysicsBodyService`, the reverse-sync writes identity pose, so all visuals stay at their initial spawn position forever. The entities are static. This is the correct P1 behaviour until `BulletPhysicsBodyService` lands (STR-D11).

## Known Issues / Limitations

- **Camera slot mismatch (risk):** The new `DemoCamera` does not configure a `CameraComponent.Slot`. If the `GraphicsCompositor` has a hardcoded slot GUID that references the old camera, a black screen will result. The human should check if the slot matters.
- **Models are static** (by design, NoOp physics — STR-D11 is open).
- **Procedural visuals are invisible** (STR-D9 open — the `CreateProceduralVisual` path still creates a mesh-less entity; not exercised by the demo).
- **STR-D4 partial:** This batch satisfies the "boot the app + wire all pieces" obligation, but the GPU render is still human-verified, not CI-verified.

## STR-D10 Status
**RESOLVED in BATCH-10.** See DEBT-TRACKER.md updated accordingly.

## Suggested Commit Message
```
feat(stride): live bring-up — boot EditorStrideSubsystem + demo spawns, fix asset-load loud (BATCH-10 STR-LIVE-1)
```

## Camera-slot binding (post-review fix)

**Problem (from review):** `AddFixedCamera` created a `DemoCamera` entity with a `CameraComponent` but never bound it to the `GraphicsCompositor`'s camera slot. Because `NeutralizeTemplatePlayer` removes the template `PlayerCharacter` (whose child `Camera` was the entity filling the compositor's slot), the active `GraphicsCompositor` was left with no camera bound to its `SceneCameraRenderer` slot → guaranteed black screen, defeating the purpose of the batch. This was the "Camera slot mismatch (risk)" flagged in *Known Issues* above; it is now resolved.

**Fix:** After constructing the `CameraComponent`, `AddFixedCamera` now calls a new helper `BindCameraToCompositorSlot(CameraComponent)` which binds the camera to the first slot of the active compositor.

**Exact API used (VERIFIED against Stride 4.2.1.2487 via `Stride.Engine.dll` / `Stride.Engine.xml` metadata):**

```csharp
var compositor = SceneSystem.GraphicsCompositor;              // Stride.Engine.SceneSystem.GraphicsCompositor : GraphicsCompositor
if (compositor == null) { /* loud warning, return */ }
if (compositor.Cameras.Count > 0)                            // GraphicsCompositor.Cameras : SceneCameraSlotCollection (IList<SceneCameraSlot>)
    camera.Slot = compositor.Cameras[0].ToSlotId();          // SceneCameraSlot.ToSlotId() : SceneCameraSlotId ; CameraComponent.Slot : SceneCameraSlotId (field)
else { /* loud warning: compositor has zero camera slots */ }
```

Symbol confirmation (from `Stride.Engine.xml`):
- `P:Stride.Rendering.Compositing.GraphicsCompositor.Cameras` — "The list of cameras used in the graphic pipeline" (type `SceneCameraSlotCollection`).
- `T:Stride.Rendering.Compositing.SceneCameraSlot` with `M:...SceneCameraSlot.ToSlotId` — "Generates a `SceneCameraSlotId` corresponding to this slot."
- `F:Stride.Engine.CameraComponent.Slot` — "The camera slot used in the graphics compositor" (field of type `SceneCameraSlotId`).
- `SceneSystem.GraphicsCompositor` is the canonical Stride 4.2 property used to reach the live compositor (compiled clean; it is the same compositor the default `DefaultGraphicsCompositorLevel10` archetype declares with one camera slot — see `Assets/GraphicsCompositor.sdgfxcomp`).

**Loud-warning fallbacks:** If `SceneSystem.GraphicsCompositor` is null, or if `compositor.Cameras.Count == 0`, the helper writes a clearly-prefixed warning to `Console.Error` (matching the existing logging style in `StrideVisualFactory`) explaining that the scene will render black and that the compositor needs a camera slot. No exception is thrown.

**Verification:**
- `dotnet build Stride/HrotStrideApp.sln -c Debug` → **Build succeeded, 0 errors** (24 pre-existing NU1608 package-version warnings only, unrelated to this change). The compile is itself confirmation the four API symbols are correct on 4.2.1.2487.
- Stride test projects (`--no-build`): **Hrot.Stride.Core.Tests 215/215 passed**, **HrotStrideApp.Game.Tests 33/33 passed**, **Hrot.Stride.Animation.Tests 4/4 passed**.
- Rendering itself cannot be verified headlessly (no `GraphicsDevice`); the binding is now correct per the verified API and is the standard way to make a runtime-created camera render through the default compositor.

---

## Free-flight camera + spawn diagnostics (follow-up)

Follow-up to BATCH-10, addressing two human-inspection gaps: the overview camera could not be moved, and the 6 spawns could not be confirmed as actually rendered. Two changes, both in `Stride/HrotStrideApp.Game/StrideHrotGame.cs`.

### Change 1 — Free-flight camera

`AddFixedCamera` now attaches the existing `HrotStrideApp.BasicCameraController` (`SyncScript`, `Stride/HrotStrideApp.Game/BasicCameraController.cs`) to the `DemoCamera` entity via `cameraEntity.Add(new BasicCameraController())`. The camera keeps its initial overview position `Stride (0, 10, -5)` and ~45° downward pitch, so it still starts framing the spawn area — but is now movable.

**Controls:**
- **W / A / S / D** (or arrow keys) — move forward / strafe-left / back / strafe-right (camera-local frame)
- **Q / E** — move down / up (stabilised against world up-vector)
- **Right-mouse-drag** — look (yaw + pitch); the cursor is hidden and locked while the button is held, restored on release
- **Shift** (left or right) — speed boost (multiplies movement by `SpeedFactor`, default 5×)
- Numpad 2/4/6/8 — keyboard look (pitch/yaw), from the stock controller

Default movement speed is 5 units/s (≈25 u/s with Shift). The human can now fly to the unit cluster at Stride Z=5–7 to inspect the models.

### Change 2 — Spawn diagnostics

`Update` now calls a new `LogSpawnDiagnostics()` helper, throttled to once every 60 render frames (~1 s) via a frame counter. Each emission writes to `Console.Out`:

- **FDP world entity count** — `_editorSubsystem.World.EntityCount` (expected 6 after the demo spawns materialize).
- **Visual count** — `_editorSubsystem.VisualBindingSystem?.Visuals.Count` (the `IReadOnlyDictionary<Entity, StrideVisualReference>`).
- **Per-visual Stride position** — for each `StrideVisualReference`, the `VisualHandle` is cast to its concrete type `Stride.Engine.Entity` (the handle produced by `StrideVisualFactory`) and the owning entity's `Transform.Position` (already swizzled into Stride space by the factory) is logged together with the entity's `Name` (e.g. `Visual_Models/mannequinModel`, `Box_..`).

Sample line shape:
```
[StrideHrotGame][diag] FDP entities=6, visuals=6:
[StrideHrotGame][diag]   visual 'Visual_Models/mannequinModel' stridePos=(-3.00, 0.00, 5.00)
...
```

All accesses are null-guarded (subsystem, `World`, `VisualBindingSystem`, handle-type) so the log never throws; if the binding system is null (headless, no factory) it prints `visuals=<none>`. This lets a human confirm whether the 6 models actually instantiated on the real engine and where, so the free-flight camera can be flown to them.

### Build / test result

- `dotnet build Stride/HrotStrideApp.sln -c Debug` → **Build succeeded, 0 errors** (only pre-existing NU1608 / xUnit2013 analyzer warnings).
- `Hrot.Stride.Core.Tests` → **215/215 passed**
- `HrotStrideApp.Game.Tests` → **33/33 passed**
- `Hrot.Stride.Animation.Tests` → **4/4 passed**
- GPU/render path remains unverifiable headlessly (no `GraphicsDevice`); both changes are compile-correct and use only verified Stride 4.2.1.2487 + existing project APIs.
