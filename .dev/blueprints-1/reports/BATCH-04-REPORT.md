# BATCH-04-REPORT

## Tasks Completed

| Task | Title | Status |
|---|---|---|
| Corrective Task 0 | P2 defect fixes from BATCH-03 | COMPLETE |
| TASK-TH-003 | BlueprintTestFixture Core Infrastructure | COMPLETE |
| TASK-TH-005 | ALC Lifecycle and Unload Verification | COMPLETE |

---

## 1. Corrective Task 0

### CT0-1: Add `Time` field to `NodeEnterRecord`

**File modified:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CapturingDebugSession.cs`

`NodeEnterRecord` updated from 2-param to 3-param form:

```csharp
// Before:
public sealed record NodeEnterRecord(Entity Self, string NodeId);

// After:
public sealed record NodeEnterRecord(Entity Self, string NodeId, float Time);
```

`OnNodeEnter` updated to pass `Time: 0f` (Phase 1 placeholder). Existing tests in
`CapturingDebugSessionTests.cs` required no changes -- they do not reference `.Time`.

### CT0-2: Two skip-annotated placeholder tests added

**File modified:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CapturingDebugSessionTests.cs`

Added at bottom of `CapturingDebugSessionTests`:
- `Debug_TraceMode_RecordsAllNodeEntries` -- `[Fact(Skip = "Requires Phase 3 compiler")]`
- `Debug_Breakpoint_FiresWhenNodeEntered` -- `[Fact(Skip = "Requires Phase 3 compiler")]`

Test count in `CapturingDebugSessionTests.cs` increased from 5 to 7 (2 new, both skipped).

---

## 2. TASK-TH-003 -- BlueprintTestFixture Core Infrastructure

### Files created

| File | Status |
|---|---|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixtureOptions.cs` | CREATED |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs` | CREATED |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixtureTests.cs` | CREATED |
| `FDP/Toolkits/Fdp.Toolkits/Blueprints/Attributes/BlueprintRegistrarAttribute.cs` | CREATED (stub needed by fixture) |
| `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintStateView.cs` | CREATED (stub needed by fixture) |

### Files modified

| File | Change |
|---|---|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj` | Added `<NoWarn>xUnit2013;CS0649;CS0067</NoWarn>` to suppress pre-existing warnings |

### TH-003 test results (`BlueprintTestFixtureTests.cs` -- 9 tests)

| Test | SC | Result |
|---|---|---|
| `Constructor_InitializesAllProperties` | SC1 | PASS |
| `PublishEvent_ViaBus_ReadableInNextTickFrame` | SC2 | PASS |
| `EcbAddComponent_DeferredUntilTickFrame` | SC3 | PASS |
| `CompileAndLoad_IncrementsAlcWeakReferences` | SC4 | SKIP (Requires Phase 3 compiler) |
| `ChooseTier_CorrectBoundaries` | SC5 | PASS |
| `AttachBlueprint_RegisteredAsset_SetsHasSlot` | SC6 | SKIP (Requires Phase 3 compiler) |
| `Dispose_WithNoAlcsLoaded_Succeeds` | additional | PASS |
| `AddSimulationSystem_SystemExecutedEachTick` | additional | PASS |
| `DebugProbe_WiredToDebugSession_RecordsProbeCall` | additional | PASS |

7 pass, 2 skip.

---

## 3. TASK-TH-005 -- ALC Lifecycle and Unload Verification

### Files created

| File | Status |
|---|---|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/AlcUnloadTests.cs` | CREATED |

### TH-005 test results (`AlcUnloadTests.cs` -- 4 tests)

| Test | SC | Result |
|---|---|---|
| `Fixture_DisposeAfterLoadAssembly_ReclaimsAlc` | SC2 / SS7.5 | PASS |
| `Fixture_AfterMultipleLoads_OldAlcsReclaimedNewestStillLive` | SC3 / SS7.5 | PASS |
| `Fixture_DisposeNoAlcs_Succeeds` | SC1 / SS7.5 | PASS |
| `Fixture_StrongRefToAlc_DetectsLeakAndThrows` | SC5 leak detection | PASS |

4 pass, 0 skip.

### GC behaviour -- significant findings

The batch instructions suggested using `typeof(AlcUnloadTests).Assembly.Location` bytes and
a block-scoped `WeakReference<AssemblyLoadContext>` check in the GC loop. Both approaches
caused `Fixture_DisposeAfterLoadAssembly_ReclaimsAlc` to fail reliably. Investigation
identified two compounding root causes:

**Root cause 1 -- DEBUG JIT keeps all local temporaries alive for the method scope.**
In a Debug build, the JIT does not release local-variable slots (including hidden temporaries
for discarded `out _` parameters and discarded method return values) until the enclosing
method returns. Any variable that held a strong reference to the `Assembly` or to the
`AssemblyLoadContext` -- even a discarded return value or a `TryGetTarget(out _)` temp --
was a GC root throughout the test method, preventing collection inside the GC loop.

**Root cause 2 -- `WeakReference<T>.TryGetTarget(out T)` creates a strong ref via its `out` parameter.**
When called as `alcRef.TryGetTarget(out _)` in the GC loop's early-exit check
(`if (!alcRef.TryGetTarget(out _)) return;`), the JIT allocates a temp slot for the `out`
parameter. On the first iteration where the ALC is still alive, the slot is filled with the
live ALC reference. The DEBUG JIT keeps that slot alive for the remainder of the method,
pinning the ALC through every subsequent `GC.Collect()`.

**Fix applied -- official .NET unloadability pattern:**

1. Moved all ALC-touching operations (fixture creation, `LoadTestAssemblyFromBytes`,
   `GetAlcWeakReferences`, `Dispose`) into a single `[MethodImpl(MethodImplOptions.NoInlining)]`
   helper method `CreateLoadAndDispose`. When this method returns, its entire frame (including
   all Assembly/ALC temporaries and the `alc` local, which is explicitly nulled before return)
   is off the stack.

2. Changed to non-generic `WeakReference` with `.IsAlive` in the GC loop. Unlike
   `WeakReference<T>.TryGetTarget(out T)`, `WeakReference.IsAlive` is a property that checks
   liveness without retrieving the target and without creating any strong reference.

3. Replaced `typeof(AlcUnloadTests).Assembly.Location` bytes with `Fdp.Diagnostics.Contracts.dll`
   bytes (6 KB, loaded as a reference of the test project), as a small
   non-test assembly that avoids any xUnit runner assembly-discovery caching.

This pattern matches the official Microsoft documentation at
*"How to use and debug assembly unloadability in .NET"*.

---

## 4. Build Status

```
dotnet build IOS-IG-SimHost.sln
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## 5. Test Summary

```
Passed!  - Failed: 0, Passed: 87, Skipped: 5, Total: 92, Duration: 228 ms
```

### Breakdown of all 92 tests

| Test class | Pass | Skip | Total |
|---|---|---|---|
| `CapturingDebugSessionTests` | 5 | 2 | 7 |
| `BlueprintTestFixtureTests` | 7 | 2 | 9 |
| `AlcUnloadTests` | 4 | 0 | 4 |
| All other pre-existing classes | 71 | 1 | 72 |
| **Total** | **87** | **5** | **92** |

---

## 6. Deviations from Instructions

### 6a. `IEcsModuleSystem.Execute` signature is two-argument, not one

The batch instructions show:
```csharp
// (from TickFrame spec)
sys.Execute(View);

// (from CountingSystem helper)
public void Execute(ISimulationView view) => ExecuteCount++;
```

The actual `IEcsModuleSystem` interface in `Fdp.ModuleHost.Abstractions` declares:
```csharp
void Execute(ISimulationView view, float deltaTime);
```

All `Execute` calls in `TickFrame` and in the test helper `CountingSystem` were updated to
use the two-argument form. `BlueprintTickSystem.Execute(ISimulationView)` (single-argument,
from `Fdp.Toolkit.Blueprints`) is a different interface; that call was left single-argument.

### 6b. `.ToList()` not available on `ReadOnlySpan<T>` -- used `.ToArray()` instead

`view.ReadEvents<HitEvent>()` returns `ReadOnlySpan<HitEvent>`. The batch instructions
show `.ToList()`, which is not available on `ReadOnlySpan<T>`. Changed to `.ToArray()`
and updated the test to use `IReadOnlyList<HitEvent>` (backed by an array).

### 6c. `MockTestComponents.Register(_repo)` required in constructor

`TestComponent` (used in `EcbAddComponent_DeferredUntilTickFrame`) must be registered with
`EntityRepository` before `AddComponent<T>` can be called. Added
`MockTestComponents.Register(_repo)` to the `BlueprintTestFixture` constructor after the
repository is created.

### 6d. `VerifyAlcUnloadOnDispose` changed to `false` for ALC reclaim test

The batch instructions use `VerifyAlcUnloadOnDispose = true` (default) for
`Fixture_DisposeAfterLoadAssembly_ReclaimsAlc`, relying on `Dispose()` itself to verify
collection. Because the DEBUG JIT root-cause described in §3 makes that impossible inside
`Dispose()` when it is called from the test method frame, the test uses
`VerifyAlcUnloadOnDispose = false` and performs the GC assertion externally via the
`[NoInlining]` + `WeakReference.IsAlive` pattern. The assertion is equivalent: the ALC
must be GC-collected after `Dispose()` clears all strong references.

### 6e. `GetTestAsmBytes()` uses `Fdp.Diagnostics.Contracts.dll` instead of test assembly

The batch instructions use `typeof(AlcUnloadTests).Assembly.Location`. Loading the test
assembly itself into a collectible ALC can trigger internal runtime or runner caching via
`AppDomain.AssemblyLoad`, producing a persistent GC root. The fix uses
`Fdp.Diagnostics.Contracts.dll` (already a project reference, always present in the test
output directory) as the PE byte source. This avoids runner interference without affecting
the correctness of the ALC lifecycle test.

### 6f. Pre-existing CS0649 / CS0067 warnings suppressed

`Fdp.Diagnostics.Contracts` and related referenced assemblies generate pre-existing
`CS0649` (unassigned field) and `CS0067` (unused event) warnings on the test project.
These were suppressed via `<NoWarn>xUnit2013;CS0649;CS0067</NoWarn>` in the csproj to
maintain a clean 0-warning build.
