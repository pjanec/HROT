# BATCH-04: Test Debt Fixes + Phase 3 Muscle ECS Systems (Part 1)

**Batch Number:** BATCH-04  
**Tasks:** Test corrections (BATCH-03 P1 issues + BATCH-02 P1-08 debt), ANC-P3-01, ANC-P3-02, ANC-P3-03, ANC-P3-04, ANC-P3-05  
**Phase:** Phase 3 Part 1 — Muscle ECS systems first five  
**Estimated Effort:** 16-20 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (Phase 0), BATCH-02 (Phase 1), BATCH-03 (Phase 2)

---

## Batch Goal

This batch has two work streams:

**Stream A — Test Debt Fixes (must be done FIRST, unblocks Phase 3 correctness verification):**
- Fix three P1 gaps found in BATCH-03 review:  
  - Missing `AnimationTkbTranslator.Inject` tests  
  - Missing `BakedAnimationCache` hot-reload tests  
  - Missing `AnimationTkbQueries` query-method tests  
- Backfill Phase 1 behavioral tests: replace smoke-only tests with the full set
  specified in DD-Tests §3.2 (PlayMontage slot state, Tick advancement, notify
  firing, footstep cadence).

**Stream B — Phase 3 ECS Systems (after Stream A tests are green):**
Implement the first five Muscle ECS systems:
- `AnimationDispatcherSystem` (ANC-P3-01)
- `LookAtDispatcherSystem` (ANC-P3-02)
- `StanceTransitionSystem` (ANC-P3-03)
- `MontageQueueAdvanceSystem` (ANC-P3-04)
- `AnimationRuntimeBridgeSystem` (ANC-P3-05)

---

## Developer Onboarding

### Required Reading (IN ORDER)
1. **This file** — goals, architecture decisions, exact success criteria.
2. **BATCH-03 Review:** `.dev/anim-ctrl/reviews/BATCH-03-REVIEW.md` — exact list of test gaps to fix.
3. **BATCH-01 + BATCH-02 + BATCH-03 Reports:** `.dev/anim-ctrl/reports/` — understand what is already built.
4. **Mini Design:** `.dev/anim-ctrl/AnimationControl_BrainMuscle_MiniDesign_v0_3.md`
5. **Task Details:** `.dev/anim-ctrl/TASK-DETAIL.md` (Phase 3 section, ANC-P3-01 to ANC-P3-05)
6. **Primary Design Doc:** `.dev/anim-ctrl/DD-1_MuscleCharacterRuntime_v1_2.md` — §6 (Dispatcher), §7 (Queue), §8 (LookAt), §9 (Stance), §10 (Bridge), §12 (component shapes), §17 (phase order)
7. **Test Spec:** `.dev/anim-ctrl/DD-Tests_AnimationControl_v1_1.md` — §3 (Layer-1 required tests) and §4 (Layer-2 system tests)
8. **Debt Tracker:** `.dev/anim-ctrl/DEBT-TRACKER.md` — note D-04 about using `Params`/`State` field names.

### Source Code Locations

**Animation subsystem:**
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/` — main project
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Systems/` — new ECS systems go here
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Translators/AnimationTkbTranslator.cs` — translator (already exists)
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Baking/BakedAnimationCache.cs` — cache (already exists)
- `Hrot/Editor/Hrot.Editor.AiShared/Catalog/AnimationTkbQueries.cs` — query API (already exists)

**Test projects:**
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Tests/` — add to existing `Phase2DescriptorTests.cs` OR create `TranslatorAndCacheTests.cs` for the translator/cache/query tests
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Tests/` — add a new `Phase1BackendBehaviorTests.cs` for the Phase 1 behavioral tests

**Reference implementation patterns:**
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/` — look at how existing Hrot tests construct `EntityRepository` and systems
- `FDP/Engine/Fdp.Core/Abstractions/TkbTemplate.cs` — `TkbTemplate` constructor and `AddDescriptor<T>` pattern for inject tests

### Debt Items to Address
- **D-04:** Use `Params` and `State` field names in dispatcher/executor systems (not `ActionParams`/`ActionState`)
- **D-05:** Footstep notify emission deferred in Phase 1; backend system tick tests must still validate cadence logic

---

## Stream A: Test Debt Fixes

### Fix 1: AnimationTkbTranslator.Inject tests

**File to add to:** `Hrot.MuscleCharacter.Animation.Tests/TranslatorAndCacheTests.cs` (new file)

Create a test class `AnimationTranslatorTests` with these four tests:

```
[Fact] Inject_WithNonAnimatedTemplate_AddsNoComponents
  Arrange:
    - Create real EntityRepository, register: AnimationChannel, StanceIntent, StanceStatus,
      AnimationMontageQueue, AnimationMontageQueueState, CharacterAnimationDefRuntime,
      AnimationExecutorState, LookAtChannel, LookAtExecutorState
    - Create TkbTemplate("test", 1L) WITHOUT adding a CharacterAnimationDefDto descriptor
    - Create entity
    - Create AnimationTkbTranslator(hotReloadEvents: null)
  Act: translator.Inject(repo, entity, template)
  Assert: repo.HasComponent<AnimationChannel>(entity) == false
  Assert: repo.HasComponent<StanceIntent>(entity) == false

[Fact] Inject_WithAnimatedEntity_AddsRequiredComponents
  Arrange:
    - Same repo setup as above (all component types registered)
    - Create TkbTemplate("sniper", 1L), add CharacterAnimationDefDto (from CreateSniperDto(),
      AimConfig == null so LookAt is skipped)
    - Create entity
  Act: translator.Inject(repo, entity, template)
  Assert: repo.HasComponent<AnimationChannel>(entity) == true
  Assert: repo.HasComponent<StanceIntent>(entity) == true
  Assert: repo.HasComponent<StanceStatus>(entity) == true
  Assert: repo.HasComponent<AnimationMontageQueue>(entity) == true
  Assert: repo.HasComponent<AnimationMontageQueueState>(entity) == true
  Assert: repo.HasComponent<CharacterAnimationDefRuntime>(entity) == true
  Assert: repo.HasComponent<AnimationExecutorState>(entity) == true

[Fact] Inject_WithAimCapableEntity_AddsLookAtComponents
  Arrange:
    - Same repo setup (all types registered)
    - DTO with AimConfig != null (e.g., MaxYawDegrees=90f)
  Act: translator.Inject(repo, entity, template)
  Assert: repo.HasComponent<LookAtChannel>(entity) == true
  Assert: repo.HasComponent<LookAtExecutorState>(entity) == true

[Fact] Inject_WithoutAimConfig_DoesNotAddLookAtComponents
  Arrange:
    - Same repo setup
    - DTO with AimConfig == null
  Act: translator.Inject(repo, entity, template)
  Assert: repo.HasComponent<LookAtChannel>(entity) == false
  Assert: repo.HasComponent<LookAtExecutorState>(entity) == false
```

**Note on `repo.HasComponent<T>`:** Look at how the existing engine code checks component
presence after injection — use whatever API is available (`HasComponent`, `TryGetComponent`,
`GetComponent` in a try/catch pattern, etc.).

### Fix 2: BakedAnimationCache hot-reload tests

**File:** `TranslatorAndCacheTests.cs` — add class `BakedAnimationCacheTests`

```
[Fact] BakedAnimationCache_GetOrBake_ReturnsConsistentResult
  Arrange: cache = new BakedAnimationCache(null); dto = CreateSniperDto()
  Act: result1 = cache.GetOrBake(classId=1L, dto); result2 = cache.GetOrBake(classId=1L, dto)
  Assert: result1 != null
  Assert result1 and result2 have the same montage count (proving the same DTO was used both times)

[Fact] BakedAnimationCache_HotReload_InvalidatesEntry
  Arrange:
    - Implement a simple ITkbHotReloadEvents via a local stub/class:
      class FakeHotReloadEvents : ITkbHotReloadEvents
      {
          private Action<long> _handler;
          public void Subscribe(Action<long> handler) => _handler = handler;
          public void Unsubscribe(Action<long> handler) => _handler = null;
          public void FireReload(long classId) => _handler?.Invoke(classId);
      }
    - cache = new BakedAnimationCache(fakeEvents)
    - result1 = cache.GetOrBake(classId=1L, dto)  (populates cache)
    - fakeEvents.FireReload(1L)  (invalidates entry)
    - result2 = cache.GetOrBake(classId=1L, dto)  (re-bakes)
  Assert: result2 != null
  Assert: result2.MontageDict.Count == result1.MontageDict.Count  (same structure, fresh bake)
  (Optionally: verify they are not the same reference if BakeDef always creates new objects)
```

**Important:** Check what interface/signature `ITkbHotReloadEvents` has. The BATCH-03 report
says they created this interface. Read the actual file before writing tests.

### Fix 3: AnimationTkbQueries query-method tests

**File:** `TranslatorAndCacheTests.cs` — add class `AnimationTkbQueriesTests`

The `AnimationTkbQueries` class (in `Hrot.Editor.AiShared/Catalog/`) takes a `TkbDatabase`
or similar. Read the actual constructor to understand how to instantiate it in tests.
If it requires a real `TkbDatabase`, check if there is a `FakeTkbDatabase` or if you can
pass a simple stub.

Required tests:

```
AnimationTkbQueries_GetPlayableMontages_ExcludesStanceTransitionMontages
  Arrange: queries with entity class pointing to a DTO that has:
    - 2 normal montages (IsStanceTransition=false)
    - 1 transition montage (IsStanceTransition=true)
  Act: result = queries.GetPlayableMontages(entityClass)
  Assert: result.Count == 2
  Assert: result.All(m => !m.IsStanceTransition)

AnimationTkbQueries_GetSupportedStances_ReturnsAll
  Arrange: DTO with SupportedStances=[Standing, Crouched]
  Act: stances = queries.GetSupportedStances(entityClass)
  Assert: stances.Count == 2

AnimationTkbQueries_SupportsAim_TrueWhenAimConfigPresent
AnimationTkbQueries_SupportsAim_FalseWhenAimConfigNull

AnimationTkbQueries_GetAvailableMarkers_ReturnsAllMarkers
  Arrange: DTO with NotifyMarkers (2 entries)
  Assert: GetAvailableMarkers returns at least those 2 entries

AnimationTkbQueries_GetMarkerName_ReverseLookup
  Arrange: DTO with marker {Name="MagOut", Hash=0xA1B2C3D4}
  Act: name = queries.GetMarkerName(entityClass, 0xA1B2C3D4)
  Assert: name == "MagOut"

AnimationTkbQueries_ResolveMontageId_MatchesStableIdHasher
  Act: id = queries.ResolveMontageId(entityClass, "Reload_Rifle")
  Assert: id == StableIdHasher.ComputeMontageAssetId("Reload_Rifle")
```

**Note:** If `AnimationTkbQueries` needs a full TKB registry to look up DTOs, you will need to either:
(a) Mock/stub the dependency with a simple in-memory implementation, OR
(b) Create the `AnimationTkbQueries` with a direct DTO injection path (a test constructor or factory).
Read the existing `AnimationTkbQueries.cs` implementation before writing tests.

### Fix 4: Phase 1 behavioral tests (backfill DD-Tests §3.2)

**File:** `Hrot.MuscleCharacter.Animation.Tests/Phase1BackendBehaviorTests.cs` (new file)

These replace/supplement the smoke tests from BATCH-02. Implement the full test set
from DD-Tests §3.2. The tests use a hand-constructed `EntityRepository` with only
`FakeAnimBackendState` and `CharacterAnimationDefRuntime` registered — no systems.

**Key setup:**
- Use `CharacterAnimationDefRuntime` with `StanceCount=2`, `SlotCount=4` (similar to sniper test data).
- For tests involving a montage with a specific duration and a notify at a specific time, you need
  either a static `MontageAssetId` constant or to compute it via `StableIdHasher.ComputeMontageAssetId("Reload_Rifle")`.
- Look at how the existing backend tests in `Phase0ContractsTests.cs` or `Phase2DescriptorTests.cs`
  construct test data — follow the same pattern.

**Required tests** (exact names from DD-Tests §3.2):

PlayMontage tests:
```csharp
[Fact] PlayMontage_SetsSlotActive()
  Act: backend.PlayMontageOnSlot(handle, slot=1, montageId=reloadId, blendIn=0.1f, rate=1.0f, section=0)
  Assert: SlotState at slot 1 is active
  Assert: ActiveMontage == reloadId
  Assert: ElapsedSeconds == 0

[Fact] PlayMontage_OverwritesPreviousMontageInSameSlot()
  Arrange: PlayMontage(slot=1, Reload); Tick(dt=0.5f)
  Act: PlayMontage(slot=1, Vault, blendIn=0.05f)
  Assert: ActiveMontage == vaultId (not reloadId)
  Assert: ElapsedSeconds == 0
  Assert: FiredNotifyMask == 0

[Fact] PlayMontage_UnknownMontage_NoOps()
  Act: PlayMontageOnSlot(handle, slot=1, montageId=0x99999999, ...)
  Assert: slot 1 is NOT active
```

Tick advancement tests:
```csharp
[Fact] Tick_AdvancesElapsedTimeByDeltaTimesPlayRate()
  Arrange: PlayMontage(slot=1, Reload, playRate=2.0f, duration=3.4f)
  Act: backend.Tick(dt=0.5f)
  Assert: Slot[1].ElapsedSeconds == 1.0f (0.5 * 2.0)  -- allow small float tolerance

[Fact] Tick_DeactivatesSlotOnNaturalCompletion()
  Arrange: PlayMontage(slot=1, Reload, duration=1.0f)
  Act: backend.Tick(dt=1.5f)   (past duration)
  Assert: slot 1 is no longer active

[Fact] Tick_DoesNotAdvanceInactiveSlots()
  Arrange: PlayMontage(slot=1, Reload); explicitly stop: backend.StopMontageOnSlot(handle, slot=1, blendOut=0f)
  Act: backend.Tick(dt=0.5f)
  Assert: slot 1 elapsed time has not advanced from before the stop
```

Notify firing tests:
```csharp
[Fact] Tick_FiresNotifyWhenElapsedCrossesTimeSeconds()
  Arrange: PlayMontage with a montage that has a notify at TimeSeconds=0.5f
  Act: backend.Tick(dt=0.6f)
  Assert: DrainNotifies returns >= 1
  Assert: at least one returned notify has the expected MarkerHash

[Fact] Notify_FiresExactlyOncePerPlay()
  Arrange: PlayMontage with notify at 0.5f
  Act: Tick(1.0f)  -- fires notify; drain notifies
       Tick(0.1f)  -- past notify time again (shouldn't re-fire)
  Assert: second drain returns 0

[Fact] PlayMontage_ResetsFiredNotifyMask()
  Arrange: PlayMontage; Tick(1.0f) to fire notify; drain
  Act: PlayMontage same slot, same montage
  Tick(0.6f) again
  Assert: notify fires again (drain returns >= 1)
```

Footstep cadence test:
```csharp
[Fact] Footstep_EmitsAtStrideDistance()
  Arrange: UpdateLocomotionInputs(handle, horizontalVelocity=(2,0), verticalVelocity=0, isGrounded=true)
          (stride distance per DD-Fake is 0.9m; at 2 m/s that is 0.45s per step)
  Act: backend.Tick(dt=0.46f)  (just over one stride)
  Assert: DrainNotifies returns a footstep event
  Assert: returned event Kind == AnimNotifyCategory.Footstep
```

DrainNotifies tests:
```csharp
[Fact] DrainNotifies_ReturnsUpToBufferSize()
  Arrange: cause 3 notifies to fire (e.g., 3 separate Tick calls each crossing a notify threshold)
  Act: Span<RawNotifyEvent> buf = stackalloc RawNotifyEvent[5]; int n = DrainNotifies(handle, buf)
  Assert: n == 3 (or however many were queued, up to buffer size)
  Assert: all 3 slots have valid MarkerHash

[Fact] DrainNotifies_HandlesSmallerDestBuffer()
  Arrange: cause 5 notifies to fire
  Act: Span<RawNotifyEvent> buf = stackalloc RawNotifyEvent[3]; int n = DrainNotifies(handle, buf)
  Assert: n == 3 (only fills buffer)
```

**Note:** Look at the current `FakeAnimationBackend` implementation (BATCH-02 built it) to understand
what `FakeAnimBackendState` fields are available for assertions. The state is a component on the entity
so read `repo.GetComponent<FakeAnimBackendState>(entity)` for assertions.

**Note on BackendHandle:** Check whether `FakeAnimationBackend.RegisterEntity` returns an
`AnimationBackendHandle` and how slot state is accessed via `QuerySlotState`. Use the backend's
own query API rather than direct component field access where possible — that tests the public
surface.

---

## Stream B: Phase 3 ECS Systems

### Architecture Overview (from DD-1)

The systems form a pipeline that runs each frame:

```
PreSimulation:
  AnimationDispatcherSystem   -- reads channel commands, stages executor state
  LookAtDispatcherSystem      -- reads look-at commands, stages executor state
  StanceTransitionSystem      -- drives stance changes via backend

Simulation (mid):
  MontageQueueAdvanceSystem   -- advances queue state when current slot enters blend-out
  AnimationRuntimeBridgeSystem -- applies staged state to backend, calls Tick

PostSimulation (covered in BATCH-05):
  NotifyEventEmitterSystem    -- drains notifies, emits typed events
  AnimationStateReporterSystem -- synthesizes montage start/end events, writes Status
  AnimationBackendCleanupSystem -- unregisters destroyed entities
```

All systems are in `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Systems/`.

**Field names (D-04):** Use `Params` and `State` field names on channel components
(`AnimationChannel.Params`, `AnimationChannel.State` or similar). Do NOT use `ActionParams`
or `ActionState`. Verify the actual component field names from the Phase 0 components
before writing code.

**BakedAnimationCache access:** The dispatcher systems need to validate montage IDs against
the baked data. Inject the `BakedAnimationCache` into the systems' constructors. Read
`CharacterAnimationDefRuntime.BackendHandle` from the entity to determine the class key for
the cache lookup (the BackendHandle stores the class-id used as the cache key, OR the systems
look up the baked data by TKB class key stored separately). Read DD-1 §5–§6 carefully to
understand exactly what the systems read from which component.

---

### ANC-P3-01 — `AnimationDispatcherSystem`
**Refs:** DD-1 §6, §12 + TASK-DETAIL.md ANC-P3-01

- `DispatcherSystemBase<AnimationChannel>` (or equivalent base, check actual base class name)
- Phase: `PreSimulation`
- Capability: `ActorCapabilities.CanPlayAnimations`
- Processes: `ActionIdPlayMontage`, `ActionIdPlayMontageQueue`, `ActionIdStopMontage`
- On recognized command with capability: stage executor state, bump `DispatchedInstanceId`
- On unknown montage ID: immediately set `Status = Failure`
- On no capability: immediately set `Status = Failure`
- On repeated `ActionInstanceId` (same as `DispatchedInstanceId`): no-op

**Reference the exact action ID constants from Phase 0 (ANC-P0-04).**

**Success criterion tests (from DD-Tests §4.2):**
```
PlayMontageCommand_TriggersBackendPlay
PlayMontageCommand_NoCapability_FailsImmediately
PlayMontageCommand_UnknownMontage_FailsImmediately
SameInstanceId_NoActionTaken
```

---

### ANC-P3-02 — `LookAtDispatcherSystem`
**Refs:** DD-1 §8, §12 + TASK-DETAIL.md ANC-P3-02

- `PreSimulation`
- Actions: `ActionIdLookAtPoint`, `ActionIdLookAtEntity`, `ActionIdReleaseLook`
- Requires `CanAim` capability for point/entity (NOT for release)
- For entity-mode: store target entity ID in `LookAtExecutorState` for resolution by bridge
- Release: set blend-out intent in `LookAtExecutorState`

**Success criterion tests:**
```
LookAtPoint_SetsExecutorStateCorrectly
LookAtEntity_StoresTargetEntityId
ReleaseLook_SetsBlendOutIntent
NoCanAim_NonRelease_FailsImmediately
```

---

### ANC-P3-03 — `StanceTransitionSystem`
**Refs:** DD-1 §9, §13 + TASK-DETAIL.md ANC-P3-03

- Not a dispatcher — it is a descriptor-pair driver watching `StanceIntent.Version` vs `StanceStatus.AckVersion`
- `PreSimulation`
- If `Version != AckVersion`: start transition via `backend.RequestStanceChange`
- Same-stance target: immediately acknowledge (ack version, set `Phase = Completed`)
- Missing `CanChangeStance`: silently acknowledge (no backend call, ack version)
- On completion (QueryStanceTransition returns terminal state): update `StanceStatus`

**Success criterion tests:**
```
NewVersion_TriggersTransition
SameStanceTarget_ImmediatelyCompletes
MissingCapability_SilentlyAcks
```

---

### ANC-P3-04 — `MontageQueueAdvanceSystem`
**Refs:** DD-1 §7 + TASK-DETAIL.md ANC-P3-04

- `Simulation` (early)
- Observes `AnimationMontageQueue.QueueVersion` vs `AnimationMontageQueueState.ObservedVersion`
- When current slot enters `InBlendOutWindow == true` (from `backend.QuerySlotState`) AND
  there is a next entry in the queue: crossfade-to-next
- When montage ends naturally and no next entry: clear queue state

**Success criterion tests:**
```
QueueAdvance_CrossfadesToNextWhenInBlendOutWindow
QueueAdvance_NaturalEnd_ClearsQueueState
```

---

### ANC-P3-05 — `AnimationRuntimeBridgeSystem`
**Refs:** DD-1 §10, §17 + TASK-DETAIL.md ANC-P3-05

- `Simulation` (mid, after queue advance)
- First tick: calls `backend.RegisterEntity(entity, in def)` and stores handle in  
  `CharacterAnimationDefRuntime.BackendHandle`
- Per-tick: pump locomotion inputs via `backend.UpdateLocomotionInputs`
- Apply staged executor state: call `backend.PlayMontageOnSlot`, crossfades, stops
- Resolve look-at: if entity-mode, resolve world point from `SimTransform` of target entity  
  (check entity is alive first)
- Call `backend.Tick(dt)` exactly once after all per-entity updates

**Success criterion tests:**
```
StagedPlay_ResultsInBackendPlayMontageWithCorrectArgs
EntityModeLookAt_ResolvesWorldPointFromSimTransform
FirstTick_RegistersEntityWithBackend
```

---

## Test File Locations for Phase 3

Add a new test class **`Phase3SystemTests.cs`** in
`Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Tests/`:

Per-system fixture pattern from DD-Tests §4.1:
- Real `EntityRepository` with only the components the system reads/writes
- `FakeAnimationBackend` initialized with that repo
- Directly call `system.Tick(dt)` (not via simulation kernel)
- Assert component state + backend state after tick

---

## Mandatory Workflow (Test-Driven Task Progression)

For each task:
1. Write the test first — confirm it FAILS (red)
2. Write the minimum implementation to make it pass (green)
3. Ensure all PREVIOUS tests still pass
4. Only then mark the task done and move to the next

---

## Build and Test Verification

After each system is implemented:
```
dotnet build "Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Tests/Hrot.MuscleCharacter.Animation.Tests.csproj" -c Debug 2>&1 | Select-String "error CS|Build succeeded|Build FAILED"
dotnet test "Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Tests/Hrot.MuscleCharacter.Animation.Tests.csproj" --logger "console;verbosity=minimal" 2>&1 | Select-Object -Last 10
```

Full solution build before submitting:
```
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4 2>&1 | Select-String "error CS|Build succeeded|Build FAILED" | Select-Object -Last 5
```

**All tests must pass. Zero build errors. Zero warnings introduced.**

---

## Report Format

Submit your completion report to `.dev/anim-ctrl/reports/BATCH-04-REPORT.md`.

Include:
1. **Task status table** — all tasks with DONE/PARTIAL/BLOCKED
2. **Test results** — total count and pass/fail after batch
3. **Stream A: Test fixes** — for each fix, confirm the exact test names added and that they exercise the actual code path (not trivial stubs)
4. **Stream B: Systems** — for each system, note the key design decisions, field names used, and whether it matches DD-1 section exactly
5. **Developer Insights:**
   - What issues were encountered?
   - What weak points were spotted in the existing codebase?
   - What design decisions were made beyond the spec?
   - Were there any field-name or interface discrepancies between the design doc and actual Phase 0/1/2 code?
6. **Blockers** — anything preventing Phase 3 Part 2 (BATCH-05)

If you need clarification, create `.dev/anim-ctrl/questions/BATCH-04-QUESTIONS.md`.
