# BATCH-15: Update Integration Test Harnesses

**Batch Number:** BATCH-15
**Tasks:** TASK-P6-001
**Phase:** Phase 6 — Test Harness Update
**Estimated Effort:** 3-4 hours
**Priority:** HIGH
**Dependencies:** BATCH-14 complete

---

## Onboarding & Workflow

### Developer Instructions

This is the final batch of the modular-2 refactoring plan.

TASK-P6-001 updates test harnesses to use the `INetworkFactory` injection pattern:

1. **Create `MockNetworkFactory`** in a test utilities location — null-stub DDS that unit tests can use
2. **Update `EditorHarness`** to inject `OfflineNetworkFactory` (already created in BATCH-13)
3. **Review `HrotRunnerHarness` and `CgfHarness`** to confirm they manage `DdsParticipant` correctly
4. **Update `Hrot.SimHost.Tests`** if it hardcodes NED types — switch to `MockNetworkFactory`

### Required Reading (in order)

1. **Task Definition:** `.dev/modular-2/TASK-DETAIL.md#task-p6-001`
2. **Previous report:** `.dev/modular-2/reports/BATCH-14-REPORT.md`
3. **OfflineNetworkFactory:** `Hrot.Editor/OfflineNetworkFactory.cs`
4. **HrotRunnerHarness:** `Hrot.ClusterRunner.Integration.Tests/HrotRunnerHarness.cs`
5. **CgfHarness:** `Hrot.ClusterRunner.Integration.Tests/CgfHarness.cs`
6. **EditorHarness:** search for `EditorHarness` across integration test projects
7. **Hrot.SimHost.Tests csproj:** `Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj` — check if it references Hrot.Network.NED

### Source Code Areas

- `Hrot.ClusterRunner.Integration.Tests/`
- `Hrot.SimHost.Tests/`
- `Hrot.Editor.Tests/` (if it exists)
- New file location for `MockNetworkFactory` — determine best home

### Report Submission

When done, submit your report to: `.dev/modular-2/reports/BATCH-15-REPORT.md`

---

## Context

After BATCH-14, `Program.cs` uses `INetworkFactory` for the real app. But tests still create
subsystems directly without factory injection. TASK-P6-001 closes this gap:
- Pure unit tests (no DDS) should use `MockNetworkFactory`
- DDS loopback integration tests should use `NedNetworkFactory`
- The Editor, which has no DDS, should use `OfflineNetworkFactory`

---

## Phase 1: Investigate Current State

Before writing any code, investigate:

1. Read `Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj`:
   - Does it reference `Hrot.Network.NED`? If yes, is it for test stubs or real NED types?
   - List all test files that import `Hrot.NED.*` or `Hrot.Network.NED.*`

2. Read `Hrot.ClusterRunner.Integration.Tests/HrotRunnerHarness.cs` (lines 1-120):
   - Does it create a `DdsParticipant` explicitly?
   - Does it instantiate `NedNetworkFactory`?
   - Check `Dispose()` method for participant teardown

3. Read `Hrot.ClusterRunner.Integration.Tests/CgfHarness.cs`:
   - Same as above — how does it create the DDS participant?

4. Find if `EditorHarness` exists — search across all integration test files.

From this investigation, determine:
- Which tests genuinely need NED types (loopback DDS tests)
- Which tests could work with a null-stub factory (pure domain logic tests)

---

## Phase 2: Create MockNetworkFactory

**Location decision:** Create `MockNetworkFactory` in a test utilities location.
Options:
a) `Hrot.ClusterRunner.Integration.Tests/MockNetworkFactory.cs` (if only used there)
b) A shared test utilities file included by multiple test projects (if needed broadly)

Check: does `Hrot.SimHost.Tests` need it? If yes, put it somewhere both projects can access.

**Implementation:** `MockNetworkFactory` is identical in structure to `OfflineNetworkFactory`
in `Hrot.Editor` (same all-null stubs), but lives in a test assembly. The simplest approach:
copy the structure from `Hrot.Editor/OfflineNetworkFactory.cs`.

Since `OfflineNetworkFactory` is in `Hrot.Editor` (not a test assembly), test projects
cannot directly reference it. Create a separate `MockNetworkFactory` in the test projects.

```csharp
// MockNetworkFactory — all no-op stubs for testing without DDS
internal sealed class MockNetworkFactory : INetworkFactory
{
    // Same structure as OfflineNetworkFactory; all methods return null stubs
    // ...
}
```

---

## Phase 3: Update EditorHarness (if it exists)

Search for `EditorHarness` in integration tests. If found:
- Read it in full
- Check if it creates a DDS participant or uses NED types
- If yes, inject `new OfflineNetworkFactory()` — remove DDS dependencies
- Add `using Hrot.Editor;` for OfflineNetworkFactory

If EditorHarness doesn't exist or is already clean, document that in the report.

---

## Phase 4: Review HrotRunnerHarness and CgfHarness

The success condition says: "HrotRunnerHarness and CgfHarness exclusively instantiate
and Dispose the DdsParticipant for E2E loopback tests, passing it down into the concrete
INetworkFactory constructor. No static or shared participant is used."

**Read both files fully.** Check:
- Is `DdsParticipant` created as a local variable and properly disposed in `Dispose()`?
- Is `DomainId` unique per harness instance (check the Interlocked counter pattern)?
- Does each harness instance use its own participant or a shared static one?

If participant lifecycle is already correct (unique domain ID, properly disposable), 
document this as confirmed and no changes needed.

If participant is shared/static, fix it to be per-instance.

**If the harnesses don't use INetworkFactory at all** (they create subsystems directly and
subsystems create their own participants internally), note this as the remaining gap and
add it to DEBT-TRACKER.md for a future cleanup batch.

---

## Phase 5: Update Hrot.SimHost.Tests if it references NED

Check `Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj`. If it references `Hrot.Network.NED`:

1. Find which test files use NED types (search for `using Hrot.NED`)
2. For each test file, determine if NED types are used for:
   a. Real DDS loopback testing — keep NED reference (it's a loopback test)
   b. Creating stubs/mocks that can be replaced by `MockNetworkFactory` — migrate
   c. Providing entity data types (EntityCreationRequest etc.) — these neutral types
      should be in `Hrot.Core.Network`, not NED; verify they're neutral

If unit tests (non-integration) reference NED, that's the gap to fix: add `MockNetworkFactory`
to the test utilities and update those tests to use it.

---

## Build and Test Verification

```powershell
cd D:\Work\IOS-IG-SimHost-FDP-2

dotnet build IOS-IG-SimHost.sln -v quiet

# Run all tests including integration (some may require DDS environment)
dotnet test IOS-IG-SimHost.sln -v quiet
```

**Success conditions from TASK-P6-001:**
- `Hrot.SimHost.Tests` passes (uses `MockNetworkFactory` for unit tests, NED for loopback)
- `Hrot.ClusterRunner.Integration.Tests` passes (uses `NedNetworkFactory` for DDS tests)
- Build: **0 errors**

---

## Report Requirements

Create `.dev/modular-2/reports/BATCH-15-REPORT.md` with:

1. **Investigation findings**:
   - Current NED usage in Hrot.SimHost.Tests (list files and what they use)
   - HrotRunnerHarness DDS participant lifecycle (per-instance vs shared? Properly disposed?)
   - CgfHarness DDS participant lifecycle
   - EditorHarness current state

2. **Changes made**: 
   - MockNetworkFactory created (where?)
   - EditorHarness updated (yes/no, what changed?)
   - Hrot.SimHost.Tests updated (yes/no, what changed?)
   - HrotRunnerHarness/CgfHarness: confirmed correct or fixed

3. **Test results**: pass counts for unit tests and integration tests

4. **Build result**: 0 errors

5. **Deferred items**: any gaps that could not be closed, proposed as DEBT items

6. **TASK-P6-001 completion assessment**: which success conditions are met,
   which are not yet met (acceptable deferred as tech debt)
