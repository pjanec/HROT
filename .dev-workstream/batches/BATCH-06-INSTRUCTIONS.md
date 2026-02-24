# BATCH-06: Architectural Correctives (DEBT-009–011) + Test Gap Fixes + Phase 3 Start (BCS-P3-T1)

**Batch Number:** BATCH-06  
**Tasks:** CORRECTIVE-P1 (SpatialHashGrid Entity refactor + GetEntity internal + Local Rebuild), CORRECTIVE-P2 (test gaps), BCS-P3-T1  
**Phase:** Corrective + Phase 3 start — FDP.Toolkit.Navigation  
**Estimated Effort:** 8–11 hours  
**Priority:** CRITICAL — three P1 items must be resolved before any new code depends on raw entity indices  
**Dependencies:** BATCH-05 ✅

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch is **corrective-heavy**. The P1 items (DEBT-009, DEBT-010, DEBT-011) are breaking API changes that touch the kernel, CarKinem, and Perception in one coordinated sweep. They must all land together — partial completion leaves the build broken.

After the P1 sweep, fix the P2/P3 test gaps, then implement BCS-P3-T1 (Navigation action IDs and parameter structs). Do not begin BCS-P3-T2 or later until BATCH-06 is reviewed.

### Required Reading (IN ORDER)

1. **BATCH-05 Review:** `.dev-workstream/reviews/BATCH-05-REVIEW.md` — understand every issue before touching code
2. **DEBT-TRACKER.md:** `.dev-workstream/DEBT-TRACKER.md` — DEBT-009, 010, 011 full descriptions + coupling note
3. **Architect pattern (local rebuild):** Read the full code samples in the user discussion preceding BATCH-05 (supplied in the review context). The architect provided exact class skeletons for `LocalGridBuilderSystem`, updated `SpatialHashGrid`, and updated `VisionBroadphaseSystem`.
4. **CODE-STANDARDS.md:** `.dev-workstream/guides/CODE-STANDARDS.md` — §0 test quality + all rules
5. **Task details BCS-P3-T1:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — lines 636–662

### Source Locations

| Area | Path |
|---|---|
| **SpatialHashGrid** (kernel-level refactor) | `FDP/Toolkits/FDP.Toolkit.CarKinem/SpatialHashGrid.cs` |
| **SpatialHashSystem** (main-thread builder) | `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/SpatialHashSystem.cs` |
| **EntityRepository.GetEntity** | `FDP/Kernel/Fdp.Kernel/EntityRepository.cs` |
| **AudioPerceptionSystem** | `FDP/Toolkits/FDP.Toolkit.Perception/Systems/AudioPerceptionSystem.cs` |
| **VisionBroadphaseSystem** | `FDP/Toolkits/FDP.Toolkit.Perception/Systems/VisionBroadphaseSystem.cs` |
| **PerceptionModule** | `FDP/Toolkits/FDP.Toolkit.Perception/PerceptionModule.cs` |
| **New: LocalGridBuilderSystem** | `FDP/Toolkits/FDP.Toolkit.Perception/Systems/LocalGridBuilderSystem.cs` ← **create** |
| **CarKinem tests** | `FDP/Toolkits/FDP.Toolkit.CarKinem.Tests/` |
| **Perception tests** | `FDP/Toolkits/FDP.Toolkit.Perception.Tests/` |
| **New: BCS-P3-T1** | `FDP/Toolkits/FDP.Toolkit.Navigation/NavigationActions.cs` ← **create** |
| **New: Navigation test project** | `FDP/Toolkits/FDP.Toolkit.Navigation.Tests/` ← **create** |

### Build & Test Commands

```powershell
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln                                       # must be 0 errors
dotnet test FDP.sln                                        # all green
dotnet test Toolkits/FDP.Toolkit.CarKinem.Tests/           # SpatialHash integration test
dotnet test Toolkits/FDP.Toolkit.Perception.Tests/        # all 15+ tests green
dotnet test Toolkits/FDP.Toolkit.Navigation.Tests/        # new P3-T1 tests green
```

### Report Submission

`.dev-workstream/reports/BATCH-06-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW

1. **P1 Corrective (DEBT-009, 010, 011):** Entity grid refactor → GetEntity internal → local rebuild → full solution compiles ✅
2. **DEBT-001:** Add SpatialHashSystem integration test for Perception entities ✅
3. **P2/P3 test gaps (DEBT-012, 013, 014):** Fix raw literals, add ThreatEval tests, fix source index ✅
4. **BCS-P3-T1:** Navigation action IDs + parameter structs + size tests ✅

Do not proceed past step 1 until `dotnet build FDP.sln` is 0 errors.

---

## 🎯 Batch Objectives

- `SpatialHashGrid.GridValues` stores `Entity` structs; `QueryNeighbors` returns `Span<(Entity, Vector2)>`.
- `EntityRepository.GetEntity(int)` is `internal`; no toolkit code calls it directly.
- `PerceptionModule` owns a private `SpatialHashGrid`; `LocalGridBuilderSystem` rebuilds it from snapshot; `VisionBroadphaseSystem` queries it without brute force.
- All existing tests still pass; 5+ new tests added.
- `NavigationActions.cs` defines all action IDs and parameter structs; all fit in the channel payload (≤ 32 bytes each).

---

## ✅ Tasks

### Task 0a (P1 Corrective): Refactor `SpatialHashGrid` to store `Entity` structs (DEBT-009)

**File:** `FDP/Toolkits/FDP.Toolkit.CarKinem/SpatialHashGrid.cs`

The architect provided the exact refactored struct. Implement it precisely:

- Change `NativeArray<int> GridValues` → `NativeArray<Entity> GridValues`
- Change `Add(int entityIndex, Vector2 position)` → `Add(Entity entity, Vector2 position)`
- Change `QueryNeighbors(Vector2 position, float radius, Span<(int entityId, Vector2 pos)> output)` → `QueryNeighbors(Vector2 position, float radius, Span<(Entity entity, Vector2 pos)> output)` returning full `Entity` structs
- `GridValues = new NativeArray<Entity>(maxEntities, allocator)` in `Create`

Update `SpatialHashSystem.cs` (the main-thread builder) to call `_grid.Add(entity, pos)` passing the full `Entity` from the query iterator instead of `entity.Index`.

Update `AudioPerceptionSystem.cs`:
- `Span<(int entityId, Vector2 pos)>` → `Span<(Entity entity, Vector2 pos)>`
- Remove `World.GetEntity(candidateIdx)` call — the entity is now returned directly from `QueryNeighbors`
- Remove `World.IsAlive(listener)` guard if the snapshot already guarantees alive entities (keep the `HasComponent` checks)
- Update `QueryFallback` similarly: return tuples now hold `Entity` directly, not raw `int`

**Test to add** in `FDP.Toolkit.CarKinem.Tests/SpatialHashGridTests.cs` (new file or existing):
```csharp
[Fact]
void SpatialHashGrid_QueryNeighbors_ReturnsFullEntity_NotRawIndex()
// Add an Entity (index=5, generation=3) to the grid at (0,0)
// QueryNeighbors → verify result.entity.Index == 5 AND result.entity.Generation == 3
// (generational equality, not just index equality)
```

---

### Task 0b (P1 Corrective): Make `EntityRepository.GetEntity(int)` internal (DEBT-010)

**File:** `FDP/Kernel/Fdp.Kernel/EntityRepository.cs`

Change:
```csharp
public Entity GetEntity(int index)
```
to:
```csharp
/// <summary>
/// Reconstructs an Entity handle from a raw index.
/// INTERNAL USE ONLY — valid for:
///   1. Translating raw array indices returned by C++ physics/navmesh plugins back to Entity handles
///      within the same frame (the generation is looked up and validated).
///   2. Kernel-internal bit-scanning in EntityQuery.
/// DO NOT use this to restore entity references stored from previous frames.
/// Always pass and store the full Entity struct instead.
/// </summary>
internal Entity GetEntity(int index)
```

After making this change, `dotnet build FDP.sln` will identify every incorrect call site with a compiler error. Fix every error before proceeding. There should be exactly one or two: `AudioPerceptionSystem.QueryFallback` (already being fixed in Task 0a) and possibly a test.

**Do not** add a `// ReSharper disable` or `#pragma warning` suppression — fix the root cause.

---

### Task 0c (P1 Corrective): Local Rebuild Pattern for `PerceptionModule` (DEBT-011)

**New file:** `FDP/Toolkits/FDP.Toolkit.Perception/Systems/LocalGridBuilderSystem.cs`

The architect provided the full implementation. Implement it precisely:

```csharp
// LocalGridBuilderSystem runs first inside PerceptionModule.
// It clears the module-private SpatialHashGrid and rebuilds it from the snapshot.
// Uses view.GetComponentRO<SimTransform> — no world writes.
```

**Update `PerceptionModule.cs`:**
- Add `private SpatialHashGrid _localGrid;` field
- Allocate in constructor: `_localGrid = SpatialHashGrid.Create(width, height, cellSize, maxEntities, Allocator.Persistent)` — choose sensible defaults (e.g. 200×200 cells, 5m cell size, 50 000 max entities) and define these as named constants in `PerceptionConstants.cs`:
  ```csharp
  public const int LocalGridWidth      = 200;
  public const int LocalGridHeight     = 200;
  public const float LocalGridCellSize = 5.0f;
  public const int LocalGridMaxEntities = 50_000;
  ```
- Register `LocalGridBuilderSystem` before `VisionBroadphaseSystem`
- Implement `IDisposable` and call `_localGrid.Dispose()` in `Dispose()`

**Update `VisionBroadphaseSystem.cs`:**
- Accept `SpatialHashGrid grid` via constructor injection (remove the brute-force fallback)
- Query the injected local grid using `grid.QueryNeighbors(...)` — no direct world/snapshot scan
- Use `view.IsAlive(target)` for generational validation (since the grid now stores full `Entity` structs, no `GetEntity` reconstruction needed)

**Test to add** in `PerceptionModule.Dispose()` and the existing `VisionBroadphaseSystemTests`:
```csharp
[Fact]
void VisionBroadphase_UsesLocalGrid_DoesNotBruteForce()
// Setup: two entities both within VisionRange + FOV
// Pass a pre-populated SpatialHashGrid containing only one of them
// Assert: only one LosCheckRequestEvent emitted (the one in the grid)
// This proves the system queries the grid, not the whole world
```

---

### Task 0d (P2/P1 Corrective): DEBT-001 — SpatialHashSystem integration test with Perception entities

**File:** New test in `FDP.Toolkit.CarKinem.Tests/SpatialHashSystemTests.cs` (or existing suite)

```csharp
[Fact]
void SpatialHashSystem_IndexesEntity_WithSimTransformButNoVehicleState()
// Create an entity with SimTransform but NO VehicleState
// Run SpatialHashSystem
// Assert: SpatialHashGrid.QueryNeighbors includes the entity at its SimTransform position
```

This confirms DEBT-001 is resolved and that Perception entities are properly indexed.

---

### Task 0e (P2 Corrective): Fix test gaps (DEBT-012, DEBT-013, DEBT-014)

**DEBT-012** (`VisionBroadphaseSystemTests.cs` tests 2 and 3):

Replace raw `0.866f` literals with the expression form:
```csharp
FieldOfViewCos = MathF.Cos(MathF.PI / 6f), // cos(30°) — 60° full FOV
```
Apply consistently across all 4 tests.

**DEBT-013** (`ThreatEvaluationSystemTests.cs`):

Add two tests:
```csharp
[Fact]
void ThreatEvaluation_BoostsScore_OnTargetVisibleEvent()
// Publish a TargetVisibleEvent for an entity in TargetMemory
// Assert: ThreatScores[0] increased by boost=50f

[Fact]
void ThreatEvaluation_ZeroScoreEntry_IsRetained() // OR void ThreatEvaluation_ZeroScoreEntry_IsEvicted()
// Seed a TargetMemory with score 1.0f
// Run with dt large enough that 1.0f * (1 - decay*dt) reaches 0
// Assert the expected retention/eviction policy matches the implementation
// (Document the policy in a XML comment on AddOrUpdateTarget if not already clear)
```

**DEBT-014** (`AudioPerceptionSystemTests.cs` Test 2):

Replace `SourceEntityIndex = 99` with a real entity:
```csharp
var dummySource = world.CreateEntity();
// ...
SourceEntityIndex = dummySource.Index,
```

---

### Task 1: Navigation Action IDs + Parameter Structs (BCS-P3-T1)

**New project:** `FDP/Toolkits/FDP.Toolkit.Navigation/FDP.Toolkit.Navigation.csproj`  
**References:** `Fdp.Kernel`, `FDP.Toolkit.Behavior`, `FDP.Toolkit.CarKinem`

**File:** `FDP/Toolkits/FDP.Toolkit.Navigation/NavigationActions.cs`

**Task Definition:** [TASK-DETAIL.md §BCS-P3-T1](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p3-t1--navigation-action-ids--parameter-structs) — lines 636–662

Define named action ID constants and all parameter/state structs. All structs must be `unmanaged`, `[StructLayout(LayoutKind.Sequential)]`, and ≤ 32 bytes.

Add a `NavigationConstants.cs`:
```csharp
public static class NavigationConstants
{
    // Locomotion action IDs written into LocomotionChannel.ActiveAction by BTree/HSM nodes.
    public const ushort ActionIdMoveTo          = 1;
    public const ushort ActionIdFlee            = 2;
    public const ushort ActionIdFollowRoute     = 3;
    public const ushort ActionIdFollowRoadGraph = 4;

    // Frustration guard: consecutive ticks below speed threshold before reporting Failure.
    public const int FrustrationTickThreshold = 120;
    public const float FrustrationSpeedThreshold = 0.1f; // m/s
}
```

**Structs to define:**
- `MoveToParams { Vector2 Destination; float ArrivalRadius; float Speed; }` — 16 bytes
- `FleeParams { Entity Threat; float SafeDistance; float Speed; }` — 16 bytes (Entity = 8 bytes)
- `FleeState { uint NextReplanTick; }` — 4 bytes
- `FollowRouteParams { int TrajectoryId; byte IsLooped; /* 3 bytes padding */ }` — 8 bytes
- `FollowRoadGraphParams { int TargetNodeId; float Speed; }` — 8 bytes

**Phase 0 note:** `MoveToParams.Destination` and `FleeParams`-derived positions are `Vector2` (XY ground plane). When a BTree node populates these, it must project the 3D target: `new Vector2(target.X, target.Y)`. No changes needed to the structs themselves — this is a usage convention.

**New test project:** `FDP/Toolkits/FDP.Toolkit.Navigation.Tests/FDP.Toolkit.Navigation.Tests.csproj`

**Tests** (new file `NavigationActionTests.cs`):
```csharp
[Fact] void MoveToParams_SizeWithinChannelLimit()
// Assert.True(Unsafe.SizeOf<MoveToParams>() <= 32)

[Fact] void FleeParams_SizeWithinChannelLimit()
// Assert.True(Unsafe.SizeOf<FleeParams>() <= 32)

[Fact] void FleeState_SizeWithinChannelLimit()
// Assert.True(Unsafe.SizeOf<FleeState>() <= 32)

[Fact] void FollowRouteParams_SizeWithinChannelLimit()
// Assert.True(Unsafe.SizeOf<FollowRouteParams>() <= 32)

[Fact] void FollowRoadGraphParams_SizeWithinChannelLimit()
// Assert.True(Unsafe.SizeOf<FollowRoadGraphParams>() <= 32)

[Fact] void AllNavigationActionIds_AreDistinct()
// Assert that ActionIdMoveTo, ActionIdFlee, ActionIdFollowRoute, ActionIdFollowRoadGraph
// are all different values (catches accidental copy-paste if constants are ever rearranged)

[Fact] void FleeParams_ContainsFullEntityStruct_NotRawIndex()
// FleeParams.Threat is Entity (8 bytes), not int (4 bytes)
// Assert.Equal(8, Unsafe.SizeOf<Entity>()) — documents the expectation
// Assert.Equal(typeof(Entity), typeof(FleeParams).GetField("Threat").FieldType)
```

Note the last test specifically validates that `FleeParams.Threat` is a full `Entity`, not a raw `int` — this guards against the exact anti-pattern fixed in DEBT-009.

---

## 🧪 Testing Requirements

- **Minimum 10 new tests:** 1 generational grid test + 1 SpatialHash integration test + 1 local-grid proof-of-isolation test + 2 threat eval gap tests + fix 1 (debt-014) + 7 navigation struct tests.
- All 25 Behavior tests and 15 Perception tests must remain green.
- `dotnet build FDP.sln` must be 0 errors after every task, not just at the end.

---

## ⚠️ Quality Standards

See `.dev-workstream/guides/CODE-STANDARDS.md`.

**❗ Zero calls to `World.GetEntity(int)` in toolkit code** after Task 0b. The compiler will enforce this.

**❗ `SpatialHashGrid.Add` always receives a full `Entity`** — never `entity.Index` alone. Grep for `.Add(entity.Index,` and `.Add(.*\.Index,` before submitting.

**❗ `PerceptionModule._localGrid` must be `Dispose()`d** — `NativeArray<T>` allocates native memory. Missing disposal = memory leak. Implement `IDisposable` on `PerceptionModule`.

**❗ `LocalGridBuilderSystem` uses only `view.GetComponentRO`** — it is an async module system. Zero direct world writes.

**❗ Named constants for all grid dimensions** in `PerceptionConstants.cs` — no raw `200`, `5.0f`, `50000`.

**❗ `FleeParams.Threat` is `Entity`, not `int`** — the whole point of DEBT-009 is to never store raw indices.

---

## 📊 Report Requirements

Submit `.dev-workstream/reports/BATCH-06-REPORT.md`:

- **Test results:** `dotnet test FDP.sln` full summary.
- **Q1:** After making `GetEntity(int)` internal, how many compiler errors appeared across the solution? List every call site and what the fix was.
- **Q2:** Walk through the memory lifecycle of `PerceptionModule._localGrid`: allocated where, cleared when, rebuilt how, disposed where. What happens if `PerceptionModule.Dispose()` is never called (e.g. in a test that doesn't dispose the world)?
- **Q3:** The `VisionBroadphase_UsesLocalGrid_DoesNotBruteForce` test must prove the system queries the grid and not the world. Describe exactly how you set up the test — what's in the grid vs. what's in the world — and why the assertion is not trivially satisfied by coincidence.
- **Q4:** `FleeParams` contains an `Entity` field for the threat. What happens when that entity is destroyed mid-flee (i.e., the threat is killed)? Where in `FleeExecutor` (Phase 3 T3) should this be handled, and what should the executor report?

---

## 🎯 Success Criteria

- [ ] **DEBT-009** — `SpatialHashGrid.GridValues` is `NativeArray<Entity>`; `Add` takes `Entity`; `QueryNeighbors` returns `Span<(Entity, Vector2)>`; generational test passes
- [ ] **DEBT-010** — `EntityRepository.GetEntity(int)` is `internal`; 0 compiler errors; XML doc added
- [ ] **DEBT-011** — `LocalGridBuilderSystem` created; `PerceptionModule` owns private grid; `VisionBroadphaseSystem` accepts grid via constructor; isolation test passes
- [ ] **DEBT-001** — `SpatialHashSystem` integration test for Perception entity (SimTransform, no VehicleState) passes in CarKinem.Tests
- [ ] **DEBT-012** — `VisionBroadphaseSystemTests` uses `MathF.Cos(MathF.PI / 6f)` throughout
- [ ] **DEBT-013** — Two new `ThreatEvaluationSystemTests` (boost path + zero-score policy) pass
- [ ] **DEBT-014** — `AudioPerceptionSystemTests` Test 2 uses a real entity index
- [ ] **BCS-P3-T1** — `NavigationActions.cs` + `NavigationConstants.cs`; all 7 navigation struct tests pass; added to `FDP.sln`
- [ ] **No raw `entity.Index` stored anywhere** — grep check confirms zero `.Add(.*\.Index,` calls to `SpatialHashGrid`
- [ ] **Full solution** — `dotnet build FDP.sln` zero errors; `dotnet test FDP.sln` all green
- [ ] **Report submitted**

---

## 📚 Reference Materials

- **BATCH-05 Review:** `.dev-workstream/reviews/BATCH-05-REVIEW.md`
- **DEBT-TRACKER.md:** `.dev-workstream/DEBT-TRACKER.md` — DEBT-009, 010, 011 full descriptions
- **Architect design patterns:** User message preceding BATCH-05 report — full code samples for `SpatialHashGrid`, `LocalGridBuilderSystem`, `VisionBroadphaseSystem`
- **CODE-STANDARDS.md:** `.dev-workstream/guides/CODE-STANDARDS.md`
- **Task Details BCS-P3-T1:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — lines 636–662
- **SimComponents:** `FDP/Kernel/Fdp.Kernel/CoreComponents/SimComponents.cs`
- **Entity struct definition:** `FDP/Kernel/Fdp.Kernel/Entity.cs` (or equivalent) — verify exact byte size
