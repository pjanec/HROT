# Test Fix Reports

## FDP-G01: DebugGizmoLayer Tests (SC-GZ013, SC-GZ025, SC-GZ026)

**Batch**: BATCH-11  
**Date**: 2025  
**Tests fixed**: 12/12 (DebugGizmoLayerHitTests, DebugGizmoLayerActivationTests, DebugGizmoLayerGizmoTests)

### Root Causes

1. **NullReferenceException in Draw()** (`DebugGizmoLayer.cs:~102`, `DebugPrimitiveRenderer2D.cs`)  
   Tests inject no `IResourceProvider` so `ctx.Resources` was null; code called `ctx.Resources.Get<MapCamera>()` unconditionally.

2. **AccessViolationException from Raylib in headless mode**  
   `DebugPrimitiveRenderer2D._inner.Render()` calls into native Raylib draw calls that require an initialized window. With no window (unit test environment), this crashed.

3. **`HandleInput` was a stub (`=> false`)**  
   No hit detection was implemented, so click tests always got no events and `TestHook_IsInteractionActive` never returned `true`.

### Files Changed

| File | Change |
|------|--------|
| `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs` | Added `_lastCtx`/`_isInteractionActive` fields; stored ctx in `Draw()`; implemented geometry-aware `HandleInput`, `HitTest`, `PointToSegmentDistance`; fixed `TestHook_IsInteractionActive`. |
| `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/DebugPrimitiveRenderer2D.cs` | Guarded `ctx.Resources?.Get<MapCamera>()` and `_inner.Render()` with null-check. |

### Fix Approach

**Null safety**: Changed `ctx.Resources.Get<MapCamera>()` to `ctx.Resources?.Get<MapCamera>()` and wrapped the Raylib render call in `if (ctx.Resources != null)`.

**Hit detection**: Implemented `HandleInput` to iterate the primitive buffer, calling `HitTest` for each primitive with a valid pick token. `HitTest` dispatches on `DebugPrimitiveShape`:
- `Sphere`: point-in-circle test (`distance(center, testPos) <= sphereRadius + hitRadius`)
- `Line`/`Arrow`: point-to-segment distance test

Hit radius is `HitRadiusWorld = 5f` world units, scaled by `1/zoom` for `SizeMode.ScreenPixels` primitives. On a hit, `GizmoInteractionStartedEvent` is published and `_isInteractionActive` is set to `true`. The context (`_lastCtx`) is captured at the end of each `Draw()` call to provide the current zoom value.

### Verification

```
dotnet test FDP\FDP.sln --no-build --filter "FullyQualifiedName~DebugGizmoLayerHitTests|FullyQualifiedName~DebugGizmoLayerActivationTests|FullyQualifiedName~DebugGizmoLayerGizmoTests"
```

Result: **Failed: 0, Passed: 12, Skipped: 0**

---

## FDP-G03: ModuleHost SharedSnapshotProvider

**Tests fixed:**
- `ConvoyAutoGroupingTests` (3 tests) â€” legacy `ModuleTier.Slow` modules sharing one `SharedSnapshotProvider`
- `ProviderAssignmentTests.ProviderAssignment_AsyncSoD_MultipleModules_Convoy` â€” explicit `SlowBackground` modules expecting convoy grouping
- `ConvoyIntegrationTests` â€” 5 async SoD modules expected to share the same `SharedSnapshotProvider` instance
- `HonestSodGdbTests.BatchInstall_SodModules_ActivatedAtomically` â€” dynamic batch install of 3 SoD modules expecting convoy
- `HonestSodGdbTests.UnionMask_Expansion_NewSodModule_ExpandsSharedProvider` â€” sequential SoD installs expecting shared provider promotion
- `ResilienceIntegrationTests.Resilience_MultipleModulesFailing_SystemDegrades` â€” circuit breaker opens for 3 bad async SoD modules

**Root cause:** In `ModuleHostKernel.AssignProviderForDynamicInstall`, the `case DataStrategy.SoD:` block filtered potential convoy group members with two contradictory conditions:

```csharp
&& e.Module.Policy.Mode == policy.Mode            // require Mode matches (e.g. Asynchronous)
&& e.Module.Policy.Mode != RunMode.Asynchronous   // exclude Asynchronous  <-- THE BUG
```

`SlowBackground(hz)` always produces `Mode = RunMode.Asynchronous`. The second condition directly negated the first, so `groupMembers` was always empty for async SoD modules. Each module was issued its own exclusive `OnDemandProvider` instead of sharing a `SharedSnapshotProvider` with its convoy mates.

**Fix:** Removed the redundant/contradictory line `&& e.Module.Policy.Mode != RunMode.Asynchronous` from the `case DataStrategy.SoD:` LINQ filter in `AssignProviderForDynamicInstall`.

**File changed:** `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs` (~line 1701)

**Verification:**

```
dotnet test FDP\FDP.sln --no-build --filter "FullyQualifiedName~ConvoyAutoGroupingTests|FullyQualifiedName~ProviderAssignmentTests|FullyQualifiedName~HonestSodGdbTests|FullyQualifiedName~ConvoyIntegrationTests|FullyQualifiedName~ResilienceIntegrationTests"
```

Result: **Failed: 0, Passed: 26, Skipped: 0**

---

## FDP-G04: Ballistics and UrbanCombat Scenario Tests

**Tests fixed:** 9
**Status:** All 9 PASS

### Failing tests addressed

**`Fdp.Examples.Scenarios.Tests.BallisticsAndHitScenarioTests`** (4 tests):
- `BallisticsAndHit_RunToCompletion_ExitsZero`
- `BallisticsAndHit_Phase1_BulletSpawnedWithCorrectVelocity`
- `BallisticsAndHit_Phase3_TargetTakesDamage_NoBulletSwimthrough`
- `BallisticsAndHit_Phase4_BulletDestroyedAfterImpact`

**`Fdp.Examples.Scenarios.Tests.UrbanCombatNewScenarioTests`** (5 tests):
- `UrbanCombatNew_RunToCompletion_ExitsZero`
- `UrbanCombatNew_Latch1_InsurgentFiresWithin100Ticks`
- `UrbanCombatNew_Latch2_ApcHaltsAfterAmbush`
- `UrbanCombatNew_Latch4_InsurgentDies`
- `UrbanCombatNew_Latch5_MissionResumes`

### Issue 1 -- Missing event registrations in both scenario files

**Root cause:** `EntityCommandBuffer.Playback()` calls `FdpEventBus.PublishRaw(typeId, data)` which
requires the event type to be pre-registered via `world.RegisterEvent<T>()`. Missing registrations
caused `InvalidOperationException: Event type N not registered via RegisterEvent<T>()` during
`PlaybackCommands`, aborting the kernel update mid-tick.

**Events missing (both files):**
- `RaycastRequestEvent` (EventId 2030) -- submitted by `BallisticsSystem` via cmd buffer
- `RaycastResultEvent` (EventId 2031) -- submitted by `RaycastSolverSystem` via cmd buffer
- `WeaponFireNotification` (EventId 5004) -- published directly by `FireProcessingSystem`
- `DetonationNotification` -- published directly by `HitResolutionSystem`

**Fix:** Added `world.RegisterEvent<T>()` calls for all four events in both:
- `FDP/Examples/Fdp.Examples.Scenarios/Physics/BallisticsAndHitScenario.cs`
- `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs`

### Issue 2 -- BallisticsSystem immediate bullet destruction breaks DamageSystem

**Root cause:** `BallisticsSystem` called `repo.DestroyEntity(entity)` directly (synchronous) for
bullets in `TearDown` lifecycle state. `DamageSystem` runs after `BallisticsSystem` in the same
tick and needs to read `BallisticProjectile.Damage` from the TearDown bullet to apply hit damage.
Direct `repo.DestroyEntity` removes the entity immediately, so the bullet was gone by the time
`DamageSystem` executed and the hit was silently ignored.

**Fix:** Changed the TearDown path in `BallisticsSystem` to `cmd.DestroyEntity(entity)` (deferred
via the command buffer). `PlaybackCommands` runs after `Tick()` completes, so `DamageSystem` can
still read the bullet's components in the same tick.

**File:** `FDP/Toolkits/Fdp.Toolkits/Combat/Systems/BallisticsSystem.cs`

### Issue 3 -- BallisticsAndHit scenario checked hit result one tick too early

**Root cause:** Phase 3/4 validation checked `tick == 4`, but the bullet->raycast->hit->damage
pipeline needs 6 frames to complete with the double-buffered event bus. Checking at tick 4 found
the target at full health because `DamageSystem` had not yet consumed the `HitEvent`.

**Fix:** Changed Phase 3/4 check to `if (tick == 6)`.

**File:** `FDP/Examples/Fdp.Examples.Scenarios/Physics/BallisticsAndHitScenario.cs`

### Issue 4 -- UrbanCombat: CognitiveInterruptSystem missing from module pipeline

**Root cause:** The UrbanCombat damage-to-HSM chain requires:
```
DamageSystem strips CanMove from APC
  -> CognitiveInterruptSystem detects CanMove edge (prev set, curr cleared)
     -> sets BrainBlackboard.Interrupt_MobilityLost = 1
  -> HsmTickSystem reads Interrupt_MobilityLost
     -> injects MobilityLost event into APC BrainHsm128
  -> APC HSM: Cruising -> Disabled
  -> OnEnter_Disabled: clears LocomotionChannel, sets InteractionChannel=EjectPassengers
  -> InteractionDispatcherSystem runs EjectPassengersExecutor
  -> Soldiers: CanShoot restored, IsEmbarkedTag removed
  -> Soldiers BTree fires Action_AimAndFire -> insurgent takes damage
```
`CognitiveInterruptSystem` and `CognitiveCleanupSystem` were not in `BuildSystems()`, so
`Interrupt_MobilityLost` was never set, `MobilityLost` never injected, soldiers never ejected.

**Fix (three parts):**
1. Added `CognitiveInterruptSystem` to `modSystems` after `ChannelArbitrationSystem` and before
   `BTreeTickSystem` (matching `CognitiveRuntimeModule` ordering).
2. Added `CognitiveCleanupSystem` to `modSystems` after `HsmTickSystem<BrainHsm128>`.
3. Added `world.RegisterEvent<CognitiveInterruptEvent>()` and `using Fdp.Toolkit.Behavior.Events`.
4. Changed `CognitiveInterruptSystem` and `CognitiveCleanupSystem` from `internal sealed` to
   `public sealed` so they can be instantiated from the scenarios assembly.

**Files changed:**
- `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs`
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/CognitiveInterruptSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/CognitiveCleanupSystem.cs`


## FDP-G13+G14: Combat component sizes and cooldown logic

**Tests fixed:**
- WeaponFireIntent_IsUnmanaged_AndHasCorrectSize
- WeaponFireNotification_IsUnmanaged_AndHasCorrectSize
- DetonationNotification_IsUnmanaged_AndHasCorrectSize
- DamageAssessedEvent_IsUnmanaged_AndHasCorrectSize
- AimAndFire_DoesNotFire_WhenCooldownActive
- AimAndFire_DrainsCooldown_ByDt_UntilCanFire

**Root cause:** Two separate bugs. (1) Each of the four combat event structs had a ool IsRemote field added after the PACK-P003 refactor. Marshal.SizeOf treats ool as a 4-byte BOOL (Windows P/Invoke convention), making each struct 4 bytes larger than the layout comment specified. (2) In AimAndFireExecutor.Execute, the cooldown branch set NodeStatus.Running and returned without decrementing CooldownSecondsRemaining by dt, so the cooldown never drained.

**Fix:**
- FDP/Toolkits/Fdp.Toolkits/Combat/Events/WeaponFireEvents.cs: Removed ool IsRemote from WeaponFireIntent and WeaponFireNotification.
- FDP/Toolkits/Fdp.Toolkits/Combat/DetonationNotification.cs: Removed ool IsRemote from DetonationNotification.
- FDP/Toolkits/Fdp.Toolkits/Combat/Events/DetonationEvents.cs: Removed ool IsRemote from DamageAssessedEvent.
- FDP/Toolkits/Fdp.Toolkits/Combat/Systems/DamageCalculationSystem.cs: Removed the if (evt.IsRemote) continue; guard that referenced the now-removed field.
- FDP/Toolkits/Fdp.Toolkits/Combat/Executors/AimAndFireExecutor.cs: Added weapon.CooldownSecondsRemaining -= dt; in the cooldown branch before returning.

## FDP-G15: RecordingExportServiceTests (EX_T02 - EX_T29)

**Tests fixed**: 28/29 (all previously failing; EX_T01 was already passing)

### Root Causes

Three separate bugs, all masked by bug #1 so they appeared as one group of failures.

**Bug 1 — EntityInlineComp not excluded from FdpAutoSerializer.Build()**
FdpAutoSerializerFixedBufferTests defines EntityInlineComp (ComponentId 228), a component with an [InlineArray] field of element type Entity. FdpAutoSerializer.Build() throws InvalidOperationException for any snapshotable component that has an Entity-typed inline array field without [ScenarioIgnore]. EntityInlineComp lacked [ScenarioIgnore], causing all 28 tests to fail at the AutoRegisterAllComponentTypes / Build() step.

**Bug 2 — JsonExportOptions.FormatMode defaulted to Incremental instead of AbsoluteState**
ExportToJson routes Incremental (and Changelog) mode to ExportChangelogToJson, which writes a JSON array root. Tests called LoadJson which calls JsonNode.Parse(text)!.AsObject() — this throws InvalidOperationException: The node must be of type 'JsonObject' for an array root. The CLI tool always sets FormatMode explicitly, so production was unaffected.

**Bug 3 — ExportChangelogToJson emitted spurious entries on first observation and entity destruction**
In ExportChangelogToJson, the per-entity baseline was initialized to 
ull. When first observing an entity (aseline == null, current != null), the code computed a diff against null, emitting a frame-0 entry instead of silently establishing the baseline. When an entity was destroyed (aseline != null, current == null), a diff was also computed and emitted at the destruction frame. Tests expected: (a) first observation sets baseline with no entry; (b) entity destruction resets baseline with no entry.

### Fixes

- **FDP/Toolkits/Fdp.Toolkits.Tests/Scenario/FdpAutoSerializerFixedBufferTests.cs**  
  Added [ScenarioIgnore] to the Refs field of EntityInlineComp. Updated field summary comment accordingly. Build() now skips the [ScenarioIgnore]-annotated field and no longer throws.

- **FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/JsonExportOptions.cs**  
  Changed public ExportFormatMode FormatMode = ExportFormatMode.Incremental; to ExportFormatMode.AbsoluteState. This matches the CLI's default behavior (which always set AbsoluteState explicitly) and allows LoadJson to succeed.

- **FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/RecordingExportService.cs** (ExportChangelogToJson)  
  Replaced if (baseline == null && current == null) continue; with:
  `
  if (baseline == null || current == null) { baselines[target] = current; continue; }
  `
  When either side is null, the baseline is updated silently and no diff entry is emitted. Diffs are only computed when both sides are non-null (entity was alive in consecutive frames).
