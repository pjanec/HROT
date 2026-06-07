# BATCH-28: Fix DEBT-019 (DebugProbe.Sink race) and housekeeping

**Batch Number:** BATCH-28  
**Tasks:** DEBT-019 (CT0), TASK-TRACKER housekeeping, DEBT-023 (new entry)  
**Phase:** Maintenance  
**Estimated Effort:** 1-2 hours  
**Priority:** HIGH (CT0 first)  
**Dependencies:** BATCH-27 (committed `ffbc0699`)

---

## Onboarding & Workflow

### Developer Instructions

This is a maintenance/housekeeping batch with one corrective task (CT0) that fixes
a long-standing intermittent test failure. All three sub-tasks are mechanical changes
with no design ambiguity.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.github/skills/developer/SKILL.md`
2. **Previous Review:** `.dev/blueprints-1/reviews/BATCH-27-REVIEW.md` -- understand why DEBT-019 was escalated
3. **DEBT-TRACKER:** `.dev/blueprints-1/DEBT-TRACKER.md` -- see DEBT-019 entry
4. **TASK-TRACKER:** `.dev/blueprints-1/TASK-TRACKER.md` -- see TASK-HR-001/002/003

### Source Code Location

- **Test Project:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`
- **Dev docs:** `.dev/blueprints-1/`

### Report Submission

File your report at `.dev/blueprints-1/reports/BATCH-28-REPORT.md`.

---

## Context

`DebugProbe.Sink` is a process-wide mutable static (`IBlueprintProbeSink?`).
`BlueprintTestFixture` sets it in its constructor. `DebugSessionInterfaceTests` and
`ProbeDispatchTests` also set it directly. Because xUnit runs test classes in parallel
by default, these classes race on the static, causing intermittent test failures.

`BlueprintTestFixtureTests.Constructor_InitializesAllProperties` asserts
`Assert.Same(fixture.DebugSession, DebugProbe.Sink)` and fails when another class
has concurrently changed the static.

The fix is xUnit's `[Collection("DebugProbe")]` attribute, which makes all decorated
test classes run sequentially with respect to each other while still running in parallel
with classes that are NOT in the collection.

---

## CT0 (Corrective Task Zero): Fix DEBT-019

### Step 1 -- Create the xUnit Collection definition

**New file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/DebugProbeCollection.cs`

```csharp
using Xunit;

namespace Hrot.Blueprints.Tests;

// xUnit collection that serializes all test classes that mutate the process-wide
// DebugProbe.Sink static. Classes in this collection run sequentially with respect
// to each other; they run in parallel with classes NOT in this collection.
[CollectionDefinition("DebugProbe")]
public sealed class DebugProbeCollection { }
```

### Step 2 -- Add [Collection("DebugProbe")] to 27 test classes

Add `[Collection("DebugProbe")]` immediately before the `public sealed class` declaration
in every file listed below. Do NOT change anything else in these files.

**Root of Hrot.Blueprints.Tests:**
- `AlcUnloadTests.cs`
- `BlueprintTestFixtureTests.cs`
- `MockDispatcherSystemTests.cs`

**Demos/ subdirectory:**
- `Demos/DoorActorDoorSensorDemoTests.cs`
- `Demos/HasVisibleTargetDemoTests.cs`
- `Demos/HealthRegenDemoTests.cs`
- `Demos/LibraryMathDemoTests.cs`
- `Demos/MoveToAndFireDemoTests.cs`

**HotReload/ subdirectory (multiple subdirs):**
- `HotReload/Coordinator/AlcLifecycleTests.cs`
- `HotReload/Coordinator/FailureRollbackTests.cs`
- `HotReload/Coordinator/QuickReloadTests.cs`
- `HotReload/Coordinator/RegistrarInjectionTests.cs`
- `HotReload/PdbLoading/PdbLoadTests.cs`
- `HotReload/RuntimeIntegration/AiPrimitiveReloadTests.cs`
- `HotReload/RuntimeIntegration/HardReloadTests.cs`
- `HotReload/RuntimeIntegration/LatentCursorReloadTests.cs`
- `HotReload/RuntimeIntegration/SoftReloadTests.cs`

**Mocks/ subdirectory:**
- `Mocks/MockContractTests.cs`

**Runtime/ subdirectory:**
- `Runtime/AllocationFreeTests.cs`
- `Runtime/BlueprintMaintenanceSystem/TierUpgrade_1024_to_4096_Tests.cs`
- `Runtime/BlueprintMaintenanceSystem/TwoFrameUpgradeTimingTests.cs`
- `Runtime/BlueprintTickSystem/PhaseOrderingTests.cs`
- `Runtime/BlueprintTickSystem/ReloadReconciliationTests.cs`
- `Runtime/BlueprintTickSystem/SingleSlotTickTests.cs`
- `Runtime/BlueprintTickSystem/WorldSingletonTickTests.cs`

**Debug/ subdirectory (directly mutate DebugProbe.Sink):**
- `Debug/DebugSessionInterfaceTests.cs`
- `Debug/ProbeDispatchTests.cs`

### Example of correct placement

If the current class declaration in `BlueprintTestFixtureTests.cs` is:

```csharp
namespace Hrot.Blueprints.Tests;

public sealed class BlueprintTestFixtureTests
{
```

Change it to:

```csharp
namespace Hrot.Blueprints.Tests;

[Collection("DebugProbe")]
public sealed class BlueprintTestFixtureTests
{
```

The `using Xunit;` should already be present in all these files. Do NOT add it if it
already exists.

### Step 3 -- Add Sink reset to BlueprintTestFixture.Dispose()

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs`

In the `Dispose()` method, add `DebugProbe.Sink = NullProbeSink.Instance;` as the
FIRST line. This is defense-in-depth: it ensures that after any fixture is disposed,
`DebugProbe.Sink` is not left pointing to a disposed session object.

**Before:**
```csharp
public void Dispose()
{
    HsmActionDispatcher.ClearAll();  // clear stale function pointers before ALC unload
```

**After:**
```csharp
public void Dispose()
{
    DebugProbe.Sink = NullProbeSink.Instance;   // release reference to this session
    HsmActionDispatcher.ClearAll();  // clear stale function pointers before ALC unload
```

---

## Task 1: Update TASK-TRACKER.md

**File:** `.dev/blueprints-1/TASK-TRACKER.md`

Mark TASK-HR-001, TASK-HR-002, and TASK-HR-003 as complete. Change `[ ]` to `[x]` for
these three lines:

```
- [x] **TASK-HR-001** AiHotReloadCoordinator Core ...
- [x] **TASK-HR-002** SimulateReload Test Harness Integration ...
- [x] **TASK-HR-003** Hot Reload Test Suite ...
```

These tasks are implemented:
- TASK-HR-001: `AiHotReloadCoordinator` in `FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs`
- TASK-HR-002: `SimulateReload`, `SimulateReloadWithThrowingRegistrar`, `SimulateReloadFromAlc`
  all in `BlueprintTestFixture.cs`
- TASK-HR-003: All hot reload test files exist and pass in `HotReload/` subdirectory

---

## Task 2: Update DEBT-TRACKER.md

**File:** `.dev/blueprints-1/DEBT-TRACKER.md`

### 2a: Mark DEBT-019 as RESOLVED

Change the DEBT-019 row's Status column from `OPEN` to `RESOLVED (BATCH-28)`.

### 2b: Add DEBT-023

Append this row to the table (after DEBT-022):

```
| DEBT-023 | BATCH-27 review | `BuiltInChannelCommandCatalog` uses unqualified short action names ("MoveTo", "AimAndFire") instead of the hierarchical paths ("Locomotion/MoveTo", "Weapon/AimAndFire") from the design doc. This is correct for the current validator (matches ActionId strings in JSON assets), but diverges from the design intent. Add a comment to the catalog file explaining the naming convention. | P3 | BATCH-29+ | OPEN |
```

---

## Build and Test Verification

Run:
```
dotnet build Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj
dotnet test  Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj -q
```

Expected: 0 errors, 490 passed, 0 failed, 7 skipped (or more if any skipped tests
are now passing).

Run the full suite a second time to confirm the flaky test is gone:
```
dotnet test  Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj -q
```

Both runs must show 0 failed.

---

## Report Requirements

File your report at `.dev/blueprints-1/reports/BATCH-28-REPORT.md`.

Include:
- Files modified (the 27 `[Collection]` additions + `DebugProbeCollection.cs` + `BlueprintTestFixture.cs` + the two TRACKER files)
- Test results from TWO consecutive runs (to confirm stability)
- Any deviations from these instructions with reasoning
