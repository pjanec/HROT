# BATCH-08: P2 Gap Fixes + Phase 4 Physics Toolkit (BCS-P4-T1 through T4)

**Batch Number:** BATCH-08  
**Tasks:** CORRECTIVE (DEBT-018, DEBT-019, DEBT-020), BCS-P4-T1, BCS-P4-T2, BCS-P4-T3, BCS-P4-T4  
**Phase:** Corrective + Phase 4 — FDP.Toolkit.Physics  
**Estimated Effort:** 10–14 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-07 ✅ (Phase 3 complete)

---

## 📋 Onboarding & Workflow

### Developer Instructions

Two parts:

1. **Correctives (1–2 h):** Three small gap fixes (DEBT-018/019/020). All straightforward — read, verify, comment/add assertion. Do not skip them.

2. **Physics Toolkit (8–12 h):** Four tasks building `FDP.Toolkit.Physics` — the collision/raycast backbone that bridges Perception LOS checks and Combat ballistics. This is a parallel-execution-heavy area: `RaycastSolverSystem` uses `Parallel.For`. Think carefully about thread safety.

### Required Reading (IN ORDER)

1. **BATCH-07 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-07-REVIEW.md`
2. **DEBT-TRACKER.md:** DEBT-018, DEBT-019, DEBT-020
3. **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\CODE-STANDARDS.md`
4. **Task Details BCS-P4-T1 through T4:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — lines 751–868
5. **DispatcherSystemBase (OnExit guarantee):** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/DispatcherSystemBase.cs`
6. **SpatialHashGrid API (post-BATCH-06):** `FDP/Toolkits/FDP.Toolkit.CarKinem/SpatialHashGrid.cs` — `Add(Entity, Vector2)`, `QueryNeighbors` returns `Span<(Entity entity, Vector2 pos)>`

### Source Locations

| Area | Path |
|---|---|
| **DispatcherSystemBase** (DEBT-019) | `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/DispatcherSystemBase.cs` |
| **FollowRoadGraphExecutorTests** (DEBT-020) | `FDP/Toolkits/FDP.Toolkit.Navigation.Tests/ExecutorTests/FollowRoadGraphExecutorTests.cs` |
| **New project** | `FDP/Toolkits/FDP.Toolkit.Physics/FDP.Toolkit.Physics.csproj` ← create |
| **New test project** | `FDP/Toolkits/FDP.Toolkit.Physics.Tests/FDP.Toolkit.Physics.Tests.csproj` ← create |
| **Components** | `FDP/Toolkits/FDP.Toolkit.Physics/Components/PhysicsComponents.cs` ← create |
| **Module** | `FDP/Toolkits/FDP.Toolkit.Physics/PhysicsToolkitModule.cs` ← create |
| **Math** | `FDP/Toolkits/FDP.Toolkit.Physics/Math/Intersection2D.cs` ← create |
| **Systems** | `FDP/Toolkits/FDP.Toolkit.Physics/Systems/RaycastSolverSystem.cs` ← create |
| **Systems** | `FDP/Toolkits/FDP.Toolkit.Physics/Systems/HitResolutionSystem.cs` ← create |

### Build & Test Commands

```powershell
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln
dotnet test FDP.sln
dotnet test Toolkits/FDP.Toolkit.Physics.Tests/
dotnet test Toolkits/FDP.Toolkit.Navigation.Tests/   # must stay green after DEBT-020
```

### Report Submission

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-08-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW

1. Corrective DEBT-018 — verify `DispatcherSystemBase` OnExit guarantee ✅
2. Corrective DEBT-019 — add same-frame double-write comment to `DispatcherSystemBase` ✅
3. Corrective DEBT-020 — verify `FollowRoadGraphExecutorTests` and add missing assertions ✅
4. BCS-P4-T1 — `PhysicsCollider`, `RaycastBatchData`, `PhysicsToolkitModule` ✅
5. BCS-P4-T2 — `Intersection2D.RaycastCircle` + 4 tests ✅
6. BCS-P4-T3 — `RaycastSolverSystem` (`Parallel.For`) + 5 tests ✅
7. BCS-P4-T4 — `HitResolutionSystem` + 3 tests ✅
8. Full solution green ✅

---

## ✅ Tasks

### Task 0a (Corrective): Verify `DispatcherSystemBase` OnExit guarantee (DEBT-018)

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/DispatcherSystemBase.cs`

**Step:** Read the dispatching logic. Find the path where `OnExit` is called.

Confirm: Is `OnExit` called in **all** of these cases?
- `a)` When a new action preempts the current one (new `ActiveAction` / `DoctrineInstanceId` mismatch)
- `b)` When an entity is destroyed while an action is Running (if this case is even handled)

**Expected finding:** Case (a) should be guaranteed by design. Case (b) depends on whether the world has an "entity destroyed" hook that triggers OnExit. If not, `MoveToExecutor._stuckTicks` will leak one `int` per entity that dies mid-action.

**Action based on findings:**
- If **`OnExit` guaranteed in all cases:** Add a comment next to `_stuckTicks` in `MoveToExecutor` saying `// OnExit is guaranteed by DispatcherSystemBase even on entity destruction (verified BATCH-08)`. Mark DEBT-018 resolved.
- If **`OnExit` NOT guaranteed on entity death:** Add a fallback in `MoveToExecutor.Execute`: `if (!world.IsAlive(entity)) { _stuckTicks.Remove(entity.Index); return; }`. Add a comment explaining why. Add this finding to the report Q1. Mark DEBT-018 resolved.

Either way: DEBT-018 is closed this batch.

### Task 0b (Corrective): Add same-frame double-write safety comment (DEBT-019)

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/DispatcherSystemBase.cs`

Find the code block that calls `OnEnter` and then `Execute` in the same frame. Add a comment block:

```csharp
// ── Same-frame OnEnter + Execute safety invariant ────────────────────────────
// When an action first becomes active, OnEnter and Execute are both called in
// the same frame (OnEnter sets up state; Execute runs the first tick).
// ALL IActionExecutor implementations MUST be designed so that:
//   1. OnEnter writes NavState/channel fields to valid initial values.
//   2. The first Execute call (same frame) does NOT overwrite those writes
//      under normal conditions (e.g. HasArrived=0, IsAlive=true, ReplanGate not yet open).
// This invariant is verified in each Phase 3 executor's tests.
// See BATCH-07 Q4 for analysis.
```

### Task 0c (Corrective): Verify `FollowRoadGraphExecutorTests.SetsRoadGraphMode_OnEnter` (DEBT-020)

**File:** `FDP/Toolkits/FDP.Toolkit.Navigation.Tests/ExecutorTests/FollowRoadGraphExecutorTests.cs`

Open the test and verify it asserts **all three** `OnEnter` observable writes:
1. `NavState.Mode == NavigationMode.RoadGraph`
2. `NavState.TargetNodeId == params.TargetNodeId`  
3. `NavState.TargetSpeed == params.Speed`

If any assertion is missing, add it. The test set-up must use a non-default `TargetNodeId` (e.g. `42`) and non-default `Speed` (e.g. `15f`) so the assertions are meaningful. Mark DEBT-020 resolved.

---

### Task 1: `PhysicsCollider` + `RaycastBatchData` + `PhysicsToolkitModule` (BCS-P4-T1)

**New project:** `FDP/Toolkits/FDP.Toolkit.Physics/FDP.Toolkit.Physics.csproj`  
References: `Fdp.Kernel`, `FDP.Toolkit.CarKinem`  
`AllowUnsafeBlocks = true`

**Task Definition:** [TASK-DETAIL.md §BCS-P4-T1](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p4-t1--physicscollider--raycastbatchdata) — lines 758–776

**File:** `Components/PhysicsComponents.cs`

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct PhysicsCollider
{
    /// <summary>Radius of the bounding circle (metres). Used by Intersection2D.RaycastCircle.</summary>
    public float Radius;
    /// <summary>Layer bitmask. Rays only hit this entity if (req.LayerMask & CollisionLayer) != 0.</summary>
    public int CollisionLayer;
}
```

**File:** `Components/RaycastBatchData.cs` (or within `PhysicsComponents.cs`)

```csharp
// Singleton. Pre-allocated on module init. Reset Count to 0 after each frame's hits are processed.
public struct RaycastBatchData
{
    public int Count;
    // Pre-allocated arrays — Persistent allocator, sized at PhysicsConstants.RaycastBatchCapacity
    public NativeArray<RaycastRequest> Requests;
    public NativeArray<RaycastHit>     Hits;
}

[StructLayout(LayoutKind.Sequential)]
public struct RaycastRequest
{
    public Vector3 Start;
    public Vector3 End;
    public long    RayId;          // packed uint64: high 32 = ObserverIndex | BulletIndex, low 32 = TargetIndex
    public long    IgnoreEntityId; // Entity.Index << 16 | Entity.Generation (or store as Entity directly)
    public int     LayerMask;
}

[StructLayout(LayoutKind.Sequential)]
public struct RaycastHit
{
    public float  T;           // [0,1] hit parameter along Start→End
    public Entity HitEntity;
    public long   RayId;       // mirrors RaycastRequest.RayId for correlation
    public byte   HasHit;      // 0 = miss, 1 = hit
}
```

Add `PhysicsConstants.cs`:
```csharp
public static class PhysicsConstants
{
    public const int RaycastBatchCapacity = 4096;
    public const int QueryExpansionMeters = 5; // AABB expansion for SpatialHash broadphase
}
```

**File:** `PhysicsToolkitModule.cs`

Implements something equivalent to `IStartupModule` or uses a system that runs once on `Initialize`:
- Allocates `RaycastBatchData` with `Requests = new NativeArray<RaycastRequest>(PhysicsConstants.RaycastBatchCapacity, Allocator.Persistent)` and `Hits = new NativeArray<RaycastHit>(PhysicsConstants.RaycastBatchCapacity, Allocator.Persistent)`.
- Registers the singleton on the world: `world.SetSingleton(batchData)`.
- Implements `IDisposable`; disposes both arrays on shutdown.

**Tests (new file `PhysicsModuleTests.cs`)**:

```csharp
[Fact]
void PhysicsModule_Initialize_CreatesSingleton()
// Register PhysicsToolkitModule, call Initialize
// Assert: world.HasSingleton<RaycastBatchData>() == true

[Fact]
void RaycastBatchData_Capacity_Is4096()
// After Initialize: world.GetSingleton<RaycastBatchData>().Requests.Length == PhysicsConstants.RaycastBatchCapacity
// Also verify Hits.Length == PhysicsConstants.RaycastBatchCapacity

[Fact]
void PhysicsCollider_IsUnmanagedValueType()
// Assert.True(typeof(PhysicsCollider).IsValueType)
// Assert: sizeof(PhysicsCollider) == 8 (float + int)
```

---

### Task 2: `Intersection2D.RaycastCircle` (BCS-P4-T2)

**File:** `FDP/Toolkits/FDP.Toolkit.Physics/Math/Intersection2D.cs`  
**Task Definition:** [TASK-DETAIL.md §BCS-P4-T2](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p4-t2--intersection2d-math) — lines 780–809

Implement `Intersection2D.RaycastCircle` using the quadratic discriminant method:

```csharp
/// <summary>
/// Tests whether the line segment [start, end] intersects a circle.
/// </summary>
/// <param name="start">Segment start (2D ground plane).</param>
/// <param name="end">Segment end (2D ground plane).</param>
/// <param name="center">Circle centre (2D ground plane).</param>
/// <param name="radius">Circle radius (metres).</param>
/// <param name="t">Output: hit parameter ∈ [0,1] along start→end (undefined if no hit).</param>
/// <returns>True if the segment hits the circle.</returns>
public static bool RaycastCircle(Vector2 start, Vector2 end, Vector2 center, float radius, out float t)
{
    // d = direction vector of segment
    // f = start - center (vector from center to segment start)
    // At^2 + Bt + C = 0 where:
    //   A = d·d
    //   B = 2(f·d)
    //   C = f·f - r^2
    // discriminant < 0 → no intersection
    // t = (-B - sqrt(discriminant)) / (2A) → entry point (smaller t)
    // t must be in [0, 1] for the segment to hit

    Vector2 d = end - start;
    Vector2 f = start - center;

    float a = Vector2.Dot(d, d);
    float b = 2f * Vector2.Dot(f, d);
    float c = Vector2.Dot(f, f) - radius * radius;

    float discriminant = b * b - 4f * a * c;
    if (discriminant < 0f)
    {
        t = 0f;
        return false;
    }

    float sqrtDisc = MathF.Sqrt(discriminant);
    float t1 = (-b - sqrtDisc) / (2f * a);
    float t2 = (-b + sqrtDisc) / (2f * a);

    // Return the smallest t that is within [0, 1].
    if (t1 >= 0f && t1 <= 1f) { t = t1; return true; }
    if (t2 >= 0f && t2 <= 1f) { t = t2; return true; }

    t = 0f;
    return false;
}
```

**Tests (new file `Intersection2DTests.cs`):**

```csharp
[Fact]
void RaycastCircle_HitsCenter()
// Ray: start=(-5,0), end=(5,0). Circle: center=(0,0), radius=1.
// Result: hit=true, t ∈ [0.35f, 0.45f]  (entry at x≈-1, i.e. t≈0.4 along a 10-unit ray)

[Fact]
void RaycastCircle_MissesCircle_WhenRayPassesBeside()
// Ray: start=(-5,5), end=(5,5). Circle: center=(0,0), radius=1.
// Result: hit=false  (ray 4m above circle centre)

[Fact]
void RaycastCircle_MissesCircle_WhenSegmentTooShort()
// Ray stops before reaching circle: start=(-5,0), end=(-2,0). Circle at (3,0), radius=1.
// Result: hit=false

[Fact]
void RaycastCircle_ReturnsTMin_WhenTwoIntersections()
// Full diameter crossing: start=(-5,0), end=(5,0). Circle at (0,0), radius=1.
// Entry at t≈0.4 (x=-1), exit at t≈0.6 (x=+1).
// Result: t ∈ [0.35f, 0.45f]  — method returns entry point, not exit

[Fact]
void RaycastCircle_HitsCircle_WhenRayStartsInside()
// Ray starts inside the circle: start=(0,0), end=(5,0). Circle at (0,0), radius=1.
// t1 < 0 (entry behind start), t2 > 0 (exit in front)
// Result: hit=true (exit intersection), t ∈ [0.15f, 0.25f]  (exit at x=1, t=0.2 on 5-unit ray)
```

> **Important edge case:** A ray starting inside the circle has `t1 < 0`. The implementation above returns `t2` in this case. Verify this is correct by asking: does the caller (RaycastSolverSystem) need to know about "exit" hits for bullets that spawn inside a collider? Yes — a bullet spawned inside a vehicle (e.g. after ejection edge case) should still detect the exit. So returning `t2` is correct.

---

### Task 3: `RaycastSolverSystem` (BCS-P4-T3)

**File:** `FDP/Toolkits/FDP.Toolkit.Physics/Systems/RaycastSolverSystem.cs`  
**Task Definition:** [TASK-DETAIL.md §BCS-P4-T3](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p4-t3--raycastsolversystem) — lines 813–848

This is a main-thread system in the `InputSystemGroup` (or `PostSimulation` phase — confirm from DESIGN.md §7.3). It uses `Parallel.For` for the hot loop.

**Key implementation points:**

```csharp
[UpdateInGroup(typeof(InputSystemGroup))]
public class RaycastSolverSystem : ComponentSystem
{
    protected override void OnUpdate()
    {
        if (!World.HasSingleton<RaycastBatchData>()) return;
        ref var batch = ref World.GetSingletonRW<RaycastBatchData>();
        if (batch.Count == 0) return;

        if (!World.HasSingleton<SpatialGridData>()) return;
        var grid = World.GetSingleton<SpatialGridData>().Grid; // read-only snapshot OK (main thread)

        int count = batch.Count;
        Parallel.For(0, count, i =>
        {
            ref readonly var req = ref batch.Requests[i];
            var start2D = new Vector2(req.Start.X, req.Start.Y);
            var end2D   = new Vector2(req.End.X,   req.End.Y);

            // AABB query on spatial grid
            Vector2 center = (start2D + end2D) * 0.5f;
            float   radius = Vector2.Distance(start2D, end2D) * 0.5f + PhysicsConstants.QueryExpansionMeters;
            Span<(Entity entity, Vector2 pos)> candidates = stackalloc (Entity, Vector2)[64];
            int nc = grid.QueryNeighbors(center, radius, candidates);

            float bestT    = float.MaxValue;
            Entity bestEnt = default;
            bool   anyHit  = false;

            for (int j = 0; j < nc; j++)
            {
                Entity candidate = candidates[j].entity;
                if (!World.IsAlive(candidate)) continue;
                if (candidate.Index == req.IgnoreEntityIndex) continue;  // TODO: use full Entity comparison
                if (!World.HasComponent<PhysicsCollider>(candidate)) continue;

                var collider = World.GetComponent<PhysicsCollider>(candidate);
                if ((req.LayerMask & collider.CollisionLayer) == 0) continue;

                var tf = World.GetComponent<SimTransform>(candidate);
                var c2 = new Vector2(tf.Position.X, tf.Position.Y);

                if (Intersection2D.RaycastCircle(start2D, end2D, c2, collider.Radius, out float t))
                {
                    if (t < bestT) { bestT = t; bestEnt = candidate; anyHit = true; }
                }
            }

            batch.Hits[i] = new RaycastHit
            {
                T         = bestT,
                HitEntity = bestEnt,
                RayId     = req.RayId,
                HasHit    = (byte)(anyHit ? 1 : 0),
            };
        });
    }
}
```

**Thread safety note:** `Parallel.For` reads `batch.Requests[i]` (read-only per iteration) and writes `batch.Hits[i]` (exclusive write per index `i` — no two iterations share the same `i`). `World.GetComponent<T>` without writing is safe if the ECS guarantees read-only access to components during parallel loops. Verify that `EntityRepository.GetComponent<T>` is thread-safe for concurrent reads (it should be — confirm with the kernel docs or source). If not, use a lock-free read via raw array access. Document your finding in the report Q2.

**Tests (new file `RaycastSolverSystemTests.cs`):**

```csharp
[Fact]
void RaycastSolver_DetectsHit_WhenBulletPathCrossesCollider()
// Spawn entity at (5,0) with PhysicsCollider(radius=1, layer=1)
// BatchData.Requests[0] = {Start=(−5,0,0), End=(10,0,0), LayerMask=1}
// Run solver → Hits[0].HasHit == 1, Hits[0].HitEntity == spawned entity

[Fact]
void RaycastSolver_ReturnsNoHit_WhenNoEntitiesInPath()
// No entities with PhysicsCollider → Hits[0].HasHit == 0

[Fact]
void RaycastSolver_RespectsLayerMask()
// Entity on CollisionLayer=2 (bit 1), request with LayerMask=1 (bit 0)
// (LayerMask & CollisionLayer) == 0 → no hit even though path crosses

[Fact]
void RaycastSolver_IgnoresIgnoreEntityIndex()
// Pass IgnoreEntityIndex == spawned entity's index → no hit
// This verifies that a bullet doesn't hit its own shooter

[Fact]
void RaycastSolver_ReturnsClosestHit_WhenMultipleInPath()
// Two entities at (3,0) and (7,0), ray from (0,0) to (10,0)
// Assert: HitEntity is the entity at (3,0) — closest intersection
```

---

### Task 4: `HitResolutionSystem` (BCS-P4-T4)

**File:** `FDP/Toolkits/FDP.Toolkit.Physics/Systems/HitResolutionSystem.cs`  
**Task Definition:** [TASK-DETAIL.md §BCS-P4-T4](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p4-t4--hitresolutionsystem-physicscombat-bridge) — lines 852–867

This system runs in `InputSystemGroup`, `[UpdateAfter(typeof(RaycastSolverSystem))]`.

**RayId encoding convention (define in `PhysicsConstants.cs`):**
```csharp
// RayId packing — encode in RaycastRequest, decoded here:
// If bit 63 (sign) is 0 → LOS check:  high 32 bits = ObserverEntityIndex, low 32 bits = TargetEntityIndex
// If bit 63 (sign) is 1 → Bullet ray: high 31 bits = BulletEntityIndex (clear sign bit)
// This is simple and sufficient for Phase 4/5 (entity indices fit in 31 bits).
public static long PackLosRayId(int observerIndex, int targetIndex)
    => ((long)observerIndex << 32) | (uint)targetIndex;
public static long PackBulletRayId(int bulletEntityIndex)
    => (1L << 63) | bulletEntityIndex;
public static bool IsBulletRay(long rayId) => (rayId & (1L << 63)) != 0;
```

**Logic:**

```csharp
protected override void OnUpdate()
{
    if (!World.HasSingleton<RaycastBatchData>()) return;
    ref var batch = ref World.GetSingletonRW<RaycastBatchData>();

    for (int i = 0; i < batch.Count; i++)
    {
        ref readonly var hit = ref batch.Hits[i];
        if (hit.HasHit == 0) continue;

        if (PhysicsConstants.IsBulletRay(hit.RayId))
        {
            // Bullet hit → emit HitEvent (Combat toolkit will consume)
            World.Bus.Publish(new HitEvent
            {
                HitEntity    = hit.HitEntity,
                BulletIndex  = (int)(hit.RayId & 0x7FFFFFFFL),
                HitT         = hit.T,
            });
        }
        else
        {
            // LOS hit → emit TargetVisibleEvent (Perception toolkit will consume)
            int observerIdx = (int)(hit.RayId >> 32);
            int targetIdx   = (int)(hit.RayId & 0xFFFFFFFFL);
            World.Bus.Publish(new TargetVisibleEvent
            {
                ObserverEntityIndex = observerIdx,
                TargetEntityIndex   = targetIdx,
            });
        }
    }

    // Reset for next frame.
    batch.Count = 0;
}
```

> **Note on dependencies:** `HitResolutionSystem` publishes `HitEvent` (defined in `FDP.Toolkit.Combat`) and `TargetVisibleEvent` (defined in `FDP.Toolkit.Perception`). These are inter-toolkit dependencies. `FDP.Toolkit.Physics.csproj` must reference both, OR the events must be defined in a shared events assembly, OR only event IDs are used and events are looked up by ID. Use whichever approach matches the existing pattern in the codebase (check how `LosCheckRequestEvent` is currently consumed by `LosRequestBatchingSystem`). Document the approach in the report Q3.

**Tests (new file `HitResolutionSystemTests.cs`):**

```csharp
[Fact]
void HitResolution_EmitsTargetVisibleEvent_ForLosHit()
// Seed batch with 1 hit: HasHit=1, RayId=PackLosRayId(observerIdx, targetIdx)
// Run HitResolutionSystem
// Consume<TargetVisibleEvent>() → Length == 1, ObserverEntityIndex == observerIdx

[Fact]
void HitResolution_EmitsHitEvent_ForBulletHit()
// Seed batch with 1 hit: HasHit=1, RayId=PackBulletRayId(bulletIdx)
// Run HitResolutionSystem
// Consume<HitEvent>() → Length == 1, BulletIndex == bulletIdx

[Fact]
void HitResolution_ClearsCount_AfterProcessing()
// Seed batch.Count = 3
// Run system
// Assert: batch.Count == 0
```

---

## 🧪 Testing Requirements

- **Minimum 16 new tests:** 3 corrective (DEBT-020 assertions) + 3 module + 5 math + 5 solver + 3 hit-resolution.
- **`Intersection2D` tests must cover the inside-the-circle edge case** (`RaycastCircle_HitsCircle_WhenRayStartsInside`).
- **`RaycastSolverSystem` tests MUST use a real `SpatialHashGrid`** (not mocked) so that the grid broadphase path is exercised. Set up a world with a `SpatialGridData` singleton seeded with the test entity.
- **All existing tests must remain green** — both Phase 1 navigation executor tests (DEBT-020 changes) and all prior suites.

### PhysicsTestWorldFactory

Create `FDP/Toolkits/FDP.Toolkit.Physics.Tests/PhysicsTestWorldFactory.cs`:

```csharp
public static class PhysicsTestWorldFactory
{
    public static EntityRepository Create()
    {
        var world = EntityRepository.Create();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<SimVelocity>();
        world.RegisterComponent<PhysicsCollider>();
        // Initialize RaycastBatchData singleton with Persistent allocator.
        var batch = new RaycastBatchData
        {
            Requests = new NativeArray<RaycastRequest>(PhysicsConstants.RaycastBatchCapacity, Allocator.Persistent),
            Hits     = new NativeArray<RaycastHit>(PhysicsConstants.RaycastBatchCapacity, Allocator.Persistent),
            Count    = 0,
        };
        world.SetSingleton(batch);
        return world;
    }
}
```

> The factory allocates Persistent native arrays. Wrap each test class in `IDisposable` and dispose the world (which should dispose the singleton) OR explicitly call `world.GetSingleton<RaycastBatchData>().Requests.Dispose()` in an `IDisposable.Dispose`. Do not leak native memory in tests.

---

## ⚠️ Quality Standards

See `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\CODE-STANDARDS.md`.

**❗ All RaycastBatchData capacities from `PhysicsConstants.RaycastBatchCapacity`** — no raw `4096`.

**❗ `Parallel.For` — each index `i` writes only to `batch.Hits[i]`** — verify this is truly index-exclusive. Do NOT share any write target across iterations.

**❗ `World.GetComponent<T>` inside `Parallel.For`** — verify thread safety (see Task 3 note). Document your finding in Q2.

**❗ `SimTransform` for all position reads in `RaycastSolverSystem`** — no `VehicleState.Position`.

**❗ `PhysicsCollider.CollisionLayer` checked via bitmask** — `(LayerMask & CollisionLayer) != 0`.

**❗ `Intersection2D.RaycastCircle` is pure/static with no side effects** — testable without ECS world.

**❗ `HitResolutionSystem` resets `batch.Count = 0` after processing** — verified by a dedicated test.

**❗ Native arrays in `RaycastBatchData` must be disposed** — `PhysicsToolkitModule.Dispose()` owns them.

---

## 📊 Report Requirements

Submit `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-08-REPORT.md`:

- **Test results:** `dotnet test FDP.sln` full summary.
- **Q1 (DEBT-018 finding):** Does `DispatcherSystemBase` guarantee `OnExit` is called when an entity is destroyed mid-action? Quote the relevant code block. What action did you take?
- **Q2 (Parallel.For thread safety):** Is `EntityRepository.GetComponent<T>` safe to call concurrently from multiple threads in `RaycastSolverSystem`? What did you check to confirm this? If it's not safe, what fallback did you use?
- **Q3 (HitResolutionSystem cross-toolkit events):** How did you resolve the dependency on `HitEvent` (Combat) and `TargetVisibleEvent` (Perception) from within the Physics toolkit? Quote the approach and any `.csproj` references added.
- **Q4:** What happens if `RaycastSolverSystem` runs but `batch.Count > PhysicsConstants.RaycastBatchCapacity`? Is there a bounds check? Should there be?

---

## 🎯 Success Criteria

- [ ] **DEBT-018** — `DispatcherSystemBase` OnExit guarantee verified; `MoveToExecutor` comment added or fallback guard added
- [ ] **DEBT-019** — Same-frame double-write safety comment added to `DispatcherSystemBase`
- [ ] **DEBT-020** — `FollowRoadGraphExecutorTests.SetsRoadGraphMode_OnEnter` asserts `Mode`, `TargetNodeId`, AND `TargetSpeed`
- [ ] **BCS-P4-T1** — `PhysicsCollider`, `RaycastBatchData`, `PhysicsConstants`, `PhysicsToolkitModule`; 3 tests pass
- [ ] **BCS-P4-T2** — `Intersection2D.RaycastCircle`; 5 tests pass including inside-circle edge case
- [ ] **BCS-P4-T3** — `RaycastSolverSystem`; 5 tests pass including layer mask and closest-hit
- [ ] **BCS-P4-T4** — `HitResolutionSystem`; 3 tests pass including count reset
- [ ] **Native memory** — no leaks in tests; all `NativeArray` in test factories disposed
- [ ] **No `VehicleState` reads** — zero occurrences in new Physics toolkit code
- [ ] **Full solution** — `dotnet build FDP.sln` zero errors; `dotnet test FDP.sln` all green
- [ ] **Both projects added to `FDP.sln`**
- [ ] **Report submitted**

---

## 📚 Reference Materials

- **BATCH-07 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-07-REVIEW.md`
- **DEBT-TRACKER.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\DEBT-TRACKER.md`
- **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\CODE-STANDARDS.md`
- **Task Details BCS-P4-T1–T4:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — lines 751–868
- **SpatialHashGrid API:** `FDP/Toolkits/FDP.Toolkit.CarKinem/SpatialHashGrid.cs`
- **SimComponents:** `FDP/Kernel/Fdp.Kernel/CoreComponents/SimComponents.cs`
- **PerceptionEvents (TargetVisibleEvent):** `FDP/Toolkits/FDP.Toolkit.Perception/Events/PerceptionEvents.cs`
- **LosRequestBatchingSystem (cross-toolkit pattern):** `FDP/Toolkits/FDP.Toolkit.Perception/Systems/LosRequestBatchingSystem.cs`
