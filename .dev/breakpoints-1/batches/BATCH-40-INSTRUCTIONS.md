# BATCH-40 Instructions

**Workstream:** breakpoints-1
**Batch:** BATCH-40
**Previous batch:** BATCH-39 (APPROVED and committed)
**Responsible:** Developer

---

## Context

Read the following documents before writing any code:

- `.dev/breakpoints-1/DESIGN.md` — focus on §8.1, §8.2, §8.3, §8.4
- `.dev/breakpoints-1/TASK-DETAIL.md` — focus on `UBP-P4T1` and `UBP-P4T3` sections
- `.dev/breakpoints-1/TASK-TRACKER.md`
- `AGENTS.md` — editing invariants (non-negotiable)

**Note:** UBP-P4T2 (StructEdit commit interception, cross-project) is deferred to BATCH-41.
This batch implements only the backend mutation infrastructure.

---

## Tasks

### Task 1: UBP-P4T1 — `PendingDebugMutation` envelope + real `StageMutation` API

**Design reference:** DESIGN.md §8.1, §8.2

**Goal:** Replace the P3T3 stub implementation of `StageMutation` with the real data envelope
and queue. The current stub just increments `_pendingMutationsCount`; this task creates
`PendingDebugMutation`, a real queue, and the full classification logic.

**Implementation:**

**A. Add `PendingDebugMutation` struct** to a new file
`Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/PendingDebugMutation.cs`:

```csharp
using System;
using Fdp.Core;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Describes a single component mutation staged by the operator while the
/// simulation is paused. Applied at the N+1 tick boundary when the operator
/// clicks Step or Continue.
/// </summary>
public readonly struct PendingDebugMutation
{
    /// <summary>The entity whose component is to be mutated.</summary>
    public readonly Entity Target;

    /// <summary>Component type id resolved via ComponentTypeRegistry.</summary>
    public readonly int ComponentTypeId;

    /// <summary>
    /// True for managed-reference components (classes); false for unmanaged structs.
    /// </summary>
    public readonly bool IsManaged;

    /// <summary>
    /// Boxed payload: either a boxed unmanaged struct or a managed class reference.
    /// </summary>
    public readonly object Payload;

    /// <summary>
    /// Size in bytes for unmanaged structs (Marshal.SizeOf of the component type).
    /// 0 for managed components.
    /// </summary>
    public readonly int SizeBytes;

    public PendingDebugMutation(
        Entity target,
        int componentTypeId,
        bool isManaged,
        object payload,
        int sizeBytes)
    {
        Target          = target;
        ComponentTypeId = componentTypeId;
        IsManaged       = isManaged;
        Payload         = payload;
        SizeBytes       = sizeBytes;
    }
}
```

**B. Modify `DataBreakpointManager.cs`** — replace stub with real queue:

1. **Remove** `private int _pendingMutationsCount;`
2. **Add** `private readonly Queue<PendingDebugMutation> _pendingMutations = new();`
3. **Update** `PendingMutationsCount` property: change `=> _pendingMutationsCount;` to
   `=> _pendingMutations.Count;`
4. **Replace** the `StageMutation` body:

```csharp
public void StageMutation(Entity entity, Type componentType, object componentValue)
{
    if (componentType == null) throw new ArgumentNullException(nameof(componentType));
    if (componentValue == null) throw new ArgumentNullException(nameof(componentValue));

    int typeId    = ComponentTypeRegistry.GetId(componentType);
    bool isManaged = !componentType.IsValueType;
    int sizeBytes  = isManaged ? 0 : System.Runtime.InteropServices.Marshal.SizeOf(componentType);

    _pendingMutations.Enqueue(new PendingDebugMutation(
        entity, typeId, isManaged, componentValue, sizeBytes));
}
```

5. **Update `RequestStep` and `RequestContinue`**: remove `_pendingMutationsCount = 0;` from
   both methods (the drain in P4T3 will clear the queue via dequeue). If P4T3 is not yet
   done, add `_pendingMutations.Clear();` in the same position where `_pendingMutationsCount = 0`
   was. (P4T3 will replace Clear with a drain call; but since we are implementing both in this
   batch, do P4T3 first and let the drain handle cleanup naturally.)

**C. Required `using` additions** at the top of `DataBreakpointManager.cs`:
- `using System.Collections.Generic;` — already present
- `using System.Runtime.InteropServices;` — add if not present

---

### Task 2: UBP-P4T3 — ECB drain pipeline

**Design reference:** DESIGN.md §8.3, §8.4

**Goal:** Implement `DrainPendingMutations` as described in DESIGN §8.3 and hook it into
`RequestStep` and `RequestContinue`.

**Implementation:**

**A. Add `DrainPendingMutations` method** to `DataBreakpointManager.cs`:

```csharp
/// <summary>
/// Plays back all staged mutations into the repository via its command buffer.
/// The ECB will be applied at the next tick boundary (when the kernel calls Tick()).
/// No-op when the queue is empty.
/// </summary>
private unsafe void DrainPendingMutations(EntityRepository repo)
{
    if (_pendingMutations.Count == 0) return;

    var ecb = ((Fdp.ModuleHost.Abstractions.ISimulationView)repo).GetCommandBuffer();
    while (_pendingMutations.TryDequeue(out var m))
    {
        if (m.IsManaged)
        {
            ecb.SetManagedComponentRaw(m.Target, m.ComponentTypeId, m.Payload);
        }
        else
        {
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(
                m.Payload, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                ecb.SetComponentRaw(
                    m.Target, m.ComponentTypeId,
                    (void*)handle.AddrOfPinnedObject(),
                    m.SizeBytes);
            }
            finally
            {
                handle.Free();
            }
        }
    }
}
```

**B. Update `RequestStep`** to call `DrainPendingMutations` AFTER the SyncFrom restore
and BEFORE the time-controller step:

```csharp
public void RequestStep()
{
    if (!_isPaused) return;

    // Restore end-of-tick state (clean step -- no resimulation, no event injection).
    _liveRepo.SyncFrom(_postTickSnapshot);

    // Apply staged mutations at the N+1 boundary.
    DrainPendingMutations(_liveRepo);

    _timeController.RequestStepOneTick();
    _isPaused = false;
    _pausedTick = 0;
    // _pendingMutations is empty after drain; no explicit clear needed.

    OnPauseStateChanged?.Invoke(false);
}
```

**IMPORTANT:** Check whether the existing `RequestStep` already calls `OnPauseStateChanged?.Invoke(false)`.
If not, add it. If it already does, don't duplicate it.

**C. Update `RequestContinue`** with the same drain call in the same position:

```csharp
public void RequestContinue()
{
    if (!_isPaused) return;

    // Restore end-of-tick state.
    _liveRepo.SyncFrom(_postTickSnapshot);

    // Apply staged mutations at the N+1 boundary.
    DrainPendingMutations(_liveRepo);

    _timeController.RequestResume();
    _isPaused = false;
    _pausedTick = 0;
    // _pendingMutations is empty after drain; no explicit clear needed.

    OnPauseStateChanged?.Invoke(false);
}
```

**IMPORTANT:** Check the existing `RequestContinue` — add `DrainPendingMutations` and preserve
all existing logic. Do not remove existing `OnPauseStateChanged` calls if they exist.

---

## Tests

### P4T1 tests — add to new file
`Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/PendingMutationTests.cs`

Test class must be `[Collection("ComponentRegistry")]` (uses ComponentTypeRegistry).

**`Stage_UnmanagedStruct_StoresSizeAndClassification`:**

Setup:
1. Use `ManagerFactory.Create()` to get manager.
2. `ComponentTypeRegistry.Register<TestHealth>(200);` — already registered globally; just use it.
3. Create a repo entity (not strictly needed for staging but the API requires an Entity).
4. Create entity in `liveRepo`.
5. `manager.StageMutation(entity, typeof(TestHealth), new TestHealth { Current = 5 });`

Assertions:
- `Assert.Equal(1, manager.PendingMutationsCount)`
- To inspect the queued mutation, use the `internal` test seam (add one if missing):
  add `internal Queue<PendingDebugMutation> PendingMutationsQueue => _pendingMutations;` to
  `DataBreakpointManager.cs`
- `var m = manager.PendingMutationsQueue.Peek();`
- `Assert.False(m.IsManaged)`
- `Assert.Equal(System.Runtime.InteropServices.Marshal.SizeOf<TestHealth>(), m.SizeBytes)`
- `Assert.Equal(ComponentTypeRegistry.GetId(typeof(TestHealth)), m.ComponentTypeId)`

**`Stage_ManagedRef_StoresClassificationOnly`:**

Setup:
1. `ComponentTypeRegistry.RegisterManagedComponent<EntityLabel>(212);`
   Note: `EntityLabel` is `internal class EntityLabel { public string? Name; }` already declared
   in `DataBreakpointSystemStatefulTests.cs`. Do NOT redeclare it.
2. Create entity in `liveRepo`.
3. `manager.StageMutation(entity, typeof(EntityLabel), new EntityLabel { Name = "test" });`

Assertions:
- `var m = manager.PendingMutationsQueue.Peek();`
- `Assert.True(m.IsManaged)`
- `Assert.Equal(0, m.SizeBytes)`

### P4T3 tests — add to the same file `PendingMutationTests.cs`

**`Drain_UnmanagedPayload_PinnedAndCopiedToECB`:**

Setup:
1. Use `ManagerFactory.Create()` to get `(manager, liveRepo, snapshotProvider, tc)`.
2. Register `TestHealth` (ComponentId 200).
3. Create entity in `liveRepo`; `liveRepo.AddComponent(entity, new TestHealth { Current = 0 });`
4. `preTickSnapshot = manager.PreTickSnapshot;`
5. `preTickSnapshot.SyncFrom(liveRepo);` — preTick also has Current=0.
6. Mutate liveRepo to Current=50 (post-tick): `liveRepo.Tick(); ref var h = ref liveRepo.GetComponentRW<TestHealth>(entity); h.Current = 50;`
7. Add a breakpoint and trigger pause:
   - `var bpId = manager.Add(new Breakpoint { Enabled=true, OccurrenceThreshold=1, DisplayName="drain" });`
   - `var bp = manager.AllBreakpoints.First(b => b.Id == bpId);`
   - `manager.OnHit(bp, entity);` — pauses manager, rewinds liveRepo to preTickSnapshot (Current=0)
8. Stage a mutation: `manager.StageMutation(entity, typeof(TestHealth), new TestHealth { Current = 999 });`
9. Get the ECB BEFORE calling RequestStep:
   `var ecb = (Fdp.Core.EntityCommandBuffer)((Fdp.ModuleHost.Abstractions.ISimulationView)liveRepo).GetCommandBuffer();`
10. `manager.RequestStep();` — drains to ECB, restores liveRepo to postTickSnapshot (Current=50)
11. `ecb.Playback(liveRepo);` — applies ECB mutations to liveRepo

Assertions:
- `Assert.False(manager.IsPaused)`
- `Assert.Equal(0, manager.PendingMutationsCount)`
- `Assert.Equal(999, liveRepo.GetComponentRO<TestHealth>(entity).Current)`

**`Drain_ManagedPayload_RoutedViaSetManagedRaw`:**

Same pattern as above but with a managed component:
1. Register `EntityLabel` (ComponentId 212), same as in P4T1 test.
2. Create entity; add `EntityLabel { Name = "original" }` to liveRepo.
3. Sync preTick, mutate liveRepo to `{ Name = "post" }`.
4. Trigger pause via OnHit.
5. Stage mutation: `manager.StageMutation(entity, typeof(EntityLabel), new EntityLabel { Name = "staged" });`
6. Get ECB, call `manager.RequestStep()`, then `ecb.Playback(liveRepo)`.

Assertions:
- `Assert.Equal("staged", liveRepo.GetManagedComponentRO<EntityLabel>(entity).Name)`

**`Drain_AppliesAtN_Plus_1_BoundaryNotN`:**

This test verifies the N vs N+1 boundary invariant. Use the same setup as
`Drain_UnmanagedPayload_PinnedAndCopiedToECB`.

1. Trigger pause (liveRepo rewound to preTickSnapshot, Current=0).
2. Verify tick-N state (pre-step):
   - `Assert.Equal(0, liveRepo.GetComponentRO<TestHealth>(entity).Current)` — Original/pre-tick value
3. Stage mutation: `manager.StageMutation(entity, typeof(TestHealth), new TestHealth { Current = 777 });`
4. Still at tick N: assert liveRepo still has Current=0 (mutation not applied yet):
   - `Assert.Equal(0, liveRepo.GetComponentRO<TestHealth>(entity).Current)`
5. Get ECB, call `manager.RequestStep()`.
   After RequestStep but before Playback, liveRepo has Current=50 (postTickSnapshot restored).
   Assert `Assert.Equal(50, liveRepo.GetComponentRO<TestHealth>(entity).Current)` (pre-ECB-flush).
6. `ecb.Playback(liveRepo)` — now at N+1:
   - `Assert.Equal(777, liveRepo.GetComponentRO<TestHealth>(entity).Current)`

---

## File Checklist

**Existing files to modify:**
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs`
  — Remove `private int _pendingMutationsCount;`
  — Add `private readonly Queue<PendingDebugMutation> _pendingMutations = new();`
  — Update `PendingMutationsCount` to `=> _pendingMutations.Count;`
  — Replace `StageMutation` stub with real implementation
  — Add `DrainPendingMutations(EntityRepository repo)` private unsafe method
  — Add `internal Queue<PendingDebugMutation> PendingMutationsQueue => _pendingMutations;` test seam
  — Update `RequestStep()` to call `DrainPendingMutations` after SyncFrom
  — Update `RequestContinue()` to call `DrainPendingMutations` after SyncFrom
  — Add `using System.Runtime.InteropServices;` if not already present

**New files to create:**
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/PendingDebugMutation.cs`
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/PendingMutationTests.cs`

---

## Build and Test Requirements

1. Run: `dotnet build IOS-IG-SimHost.sln -c Debug` — must complete with 0 errors, 0 warnings.
2. Run: `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/...` — must pass ALL
   tests (40 existing + 5 new = 45 minimum).
3. Check for regressions in existing tests, especially:
   - `TemporalStatusBannerTests.Banner_ShowsTickAndCount_WhenPaused` — uses `StageMutation`
   - `TripleBufferPauseTests.RequestStep_RestoresLiveRepoToPostTickState` — verify still passes
     after the drain is added (drain with empty queue = no-op, so still passes)
   - `TripleBufferPauseTests.RequestContinue_RestoresLiveRepoToPostTickState` — same reasoning

---

## Report

Write the report to: `.dev/breakpoints-1/reports/BATCH-40-REPORT.md`

The report must include:
- List of all files modified/created
- Full list of test names and pass/fail status
- Build result (0 errors)
- The exact implementation of `StageMutation`, `DrainPendingMutations`, and the
  test seam `PendingMutationsQueue`
- The final implementation of `RequestStep` and `RequestContinue` (complete method bodies)
- Any issues encountered and solutions

---

## Key Rules (from AGENTS.md)
- Do NOT use Unicode characters in new comments or string literals
- Do NOT rewrite existing comments unless they are wrong
- TreatWarningsAsErrors — fix every warning
- Make sure the solution compiles before finishing
- Do NOT redeclare test components already declared in other test files in the same project
  (EntityLabel is in DataBreakpointSystemStatefulTests.cs, TestHealth is in DataBreakpointManagerTests.cs,
  TestHealthP3 is in DataBreakpointGizmoViewTests.cs — all are in the same test project)
