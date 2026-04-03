# BATCH-01: Phase 1 Cleanups — Enum Promotion, Primitive Obsession Removal, Bootstrap Bug Fix

**Batch Number:** BATCH-01  
**Tasks:** TASK-D03, TASK-D04, TASK-D05, TASK-D06  
**Phase:** 1 — Low-Risk Cleanups and Bug Fixes  
**Estimated Effort:** 4–6 hours  
**Priority:** HIGH  
**Dependencies:** None (no previous batches required)

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.github/skills/developer/SKILL.md` — how to work with batches
2. **Task Definitions:** `.dev/cluster-master-cqrs-2/TASK-DEFINITIONS.md` — see TASK-D03, TASK-D04, TASK-D05, TASK-D06
3. **Code Standards:** `.github/skills/CODE-STANDARDS.md` — required coding/testing standards

### Source Code Locations
- **FDP Orchestration events:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterCqrsEvents.cs`
- **FDP Orchestration status codes:** `FDP/Toolkits/FDP.Toolkit.Orchestration/OrchestrationStatusCode.cs`
- **FDP Orchestration events (ClusterOpIntents):** `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterOpIntents.cs`
- **All FDP handlers:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/` (ReferenceArchiveHandler.cs, ReferenceCheckpointHandler.cs, ReferenceEditLoadHandler.cs, ReferenceEpisodeLoadHandler.cs, ReferenceLiveLoadHandler.cs, ReferencePrefetchHandler.cs, ReferencePreviewHandler.cs, ReferenceReplayLoadHandler.cs, ReferenceScenarioLoadHandler.cs)
- **Hrot.IG handler:** `Hrot.IG/Modules/Orchestration/IgZoneDummyHandler.cs`
- **ClusterMaster:** `Hrot.Orchestrator/ClusterMaster.cs`
- **Slave translator:** `Hrot.Common/Orchestration/NodeOpSlaveTranslator.cs`
- **Master translator:** `Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs`
- **ClusterOp translator:** `Hrot.Orchestrator/Translators/ClusterOpMasterTranslator.cs`

### Test Projects
- `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/` — FDP domain tests
- `Hrot.Orchestrator.Tests/` — Orchestrator unit tests
- `Hrot.SimHost.Tests/` — SimHost handler tests

### Report Destination
`.dev/cluster-master-cqrs-2/reports/BATCH-01-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in this exact sequence with all tests passing at each step:**

1. **TASK-D04:** Remove handler const int OperationId → Write/verify tests → **ALL tests pass ✅**
2. **TASK-D03:** ClusterStateTransitionedEvent.NewStateId → ClusterState → Write/verify tests → **ALL tests pass ✅**
3. **TASK-D05:** OrchestrationStatusCode → enum → Write/verify tests → **ALL tests pass ✅**
4. **TASK-D06:** Bootstrap latch case-insensitive fix → Write/verify tests → **ALL tests pass ✅**

**DO NOT** move to the next task until current task tests are all passing.  
**DO NOT** stop working and ask permission to proceed with obvious steps (like running tests or fixing compilation errors). Fix everything and run tests autonomously until all pass.

Build & test command (run from repo root):
```powershell
dotnet build IOS-IG-SimHost.sln
dotnet test FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/FDP.Toolkit.Orchestration.Tests.csproj --no-build -v n
dotnet test Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj --no-build -v n
dotnet test Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build -v n
```

---

## Context

This batch eliminates Primitive Obsession from the core CQRS orchestration layer. All four tasks are independent of each other but are grouped here because they are low-risk, isolated, and collectively restore strong type safety.  
Previous work established the CQRS domain enums in `cluster-master-cqrs-1`. These tasks are the cleanup follow-up.

**Related Tasks:**
- [TASK-D03](../TASK-DEFINITIONS.md#task-d03--clusterstatesitionedeventnewstateid--clusterstate-enum)
- [TASK-D04](../TASK-DEFINITIONS.md#task-d04--remove-handler-const-int-operationid-constants)
- [TASK-D05](../TASK-DEFINITIONS.md#task-d05--orchestrationstatuscode--enum)
- [TASK-D06](../TASK-DEFINITIONS.md#task-d06--bootstrap-latch-case-insensitive-fix)

---

## 🎯 Batch Objectives

1. Remove all `public const int *OperationId` fields from all `IClusterStateHandler` implementations.
2. Promote `ClusterStateTransitionedEvent.NewStateId` from `int` to the `ClusterState` domain enum.
3. Convert `OrchestrationStatusCode` from a static class with `const int` fields to a proper C# `enum`, and change event `StatusCode` fields to use that enum.
4. Fix the critical bootstrap latch bug where case-sensitive string matching permanently blocks cluster startup.

---

## ✅ Tasks

---

### Task 1: Remove Handler const int OperationId Constants (TASK-D04)

**Task Definition:** See [TASK-DEFINITIONS.md](../TASK-DEFINITIONS.md#task-d04--remove-handler-const-int-operationid-constants)

**What to do:** Simply delete the `public const int *OperationId` fields from these files. The `CanHandle()` methods already use `NodeOpType` enum directly — the constants are dead code.

**Files to modify:**

| File | Field(s) to Delete |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferenceEpisodeLoadHandler.cs` | `public const int StartEpisodeOperationId = 20;` and `public const int StopEpisodeOperationId = 21;` |
| `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferenceArchiveHandler.cs` | `public const int SerializeLocalOperationId = 15;` |
| `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferenceCheckpointHandler.cs` | `public const int TakeSnapshotOperationId = 4;` |
| `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferenceEditLoadHandler.cs` | `public const int PrepareStateOperationId = 1;` |
| `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferenceLiveLoadHandler.cs` | `public const int PrepareLiveOperationId = 9;` and `public const int FinalizeLiveOperationId = 10;` |
| `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferencePrefetchHandler.cs` | `public const int PrefetchFilesOperationId = 25;` |
| `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferencePreviewHandler.cs` | `public const int PrepareStateOperationId = 1;` |
| `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferenceReplayLoadHandler.cs` | `PrepareReplayOperationId = 11`, `FinalizeReplayOperationId = 12`, `PrepareLiveOperationId = 9` |
| `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferenceScenarioLoadHandler.cs` | `public const int PrepareLiveOperationId = 9;` |
| `Hrot.IG/Modules/Orchestration/IgZoneDummyHandler.cs` | `PrepareZoneOperationId` and `CommitZoneOperationId` |

Also remove the `/// <summary>Integer value of <c>NodeOpType.*</c>.</summary>` doc comments above each deleted field.

**Search for consumers:** Run a grep across the entire codebase for each constant name (e.g., `StartEpisodeOperationId`, `SerializeLocalOperationId`, etc.). If any tests or other code references them, update those references to use `(int)NodeOpType.StartEpisode` or the enum member directly. The `ClusterSlaveTests.cs` in `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/` comments mention `OperationId` but should only reference numerics in comments, not const fields.

**Tests to verify:** Run the full FDP.Toolkit.Orchestration.Tests project. The existing tests must all continue to pass without modification (except for any that reference the now-deleted constants).

---

### Task 2: ClusterStateTransitionedEvent.NewStateId → ClusterState Enum (TASK-D03)

**Task Definition:** See [TASK-DEFINITIONS.md](../TASK-DEFINITIONS.md#task-d03--clusterstatesitionedeventnewstateid--clusterstate-enum)

**File:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterCqrsEvents.cs`

**Change:** In `ClusterStateTransitionedEvent`, change:
```csharp
// Before:
/// <summary>New cluster state numeric value (<c>ClusterState</c> enum).</summary>
public int    NewStateId;
```
to:
```csharp
// After:
/// <summary>New cluster state.</summary>
public ClusterState NewStateId;
```

**File:** `Hrot.Orchestrator/ClusterMaster.cs`

Find `PublishClusterState(ClusterState state)` (around line 1120). Change:
```csharp
// Before:
NewStateId    = (int)state,
```
to:
```csharp
// After:
NewStateId    = state,
```

**Search for other consumers:** Grep for `ClusterStateTransitionedEvent` across the entire codebase — currently only the publisher in `ClusterMaster.cs` references it and the definition in `ClusterCqrsEvents.cs`. If any other consumer is found, update it to treat `NewStateId` as `ClusterState` enum.

**Tests to add:** In `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/FdpOrchestrationCqrsStructTests.cs` (or create a companion test in the same project), add:
```csharp
[Fact]
public void ClusterStateTransitionedEvent_NewStateId_IsClusterStateEnum()
{
    var ev = new ClusterStateTransitionedEvent { NewStateId = ClusterState.Live, SubsystemName = "Cluster" };
    Assert.Equal(ClusterState.Live, ev.NewStateId);
}
```
Verify all FDP.Toolkit.Orchestration.Tests pass.

---

### Task 3: OrchestrationStatusCode → Enum (TASK-D05)

**Task Definition:** See [TASK-DEFINITIONS.md](../TASK-DEFINITIONS.md#task-d05--orchestrationstatuscode--enum)

This is the largest task in this batch due to the number of call-site changes. Work through it methodically.

#### Step 3a: Convert OrchestrationStatusCode class to enum

**File:** `FDP/Toolkits/FDP.Toolkit.Orchestration/OrchestrationStatusCode.cs`

Replace the entire file contents with:
```csharp
namespace FDP.Toolkit.Orchestration
{
    /// <summary>
    /// Strongly-typed status codes used across all FDP orchestration domain events.
    ///
    /// <para>
    /// <b>Range design:</b>
    /// <list type="table">
    ///   <item><term>0–9</term><description>Lifecycle (0 = Success, 1 = InProgress, 2 = Pending)</description></item>
    ///   <item><term>10–99</term><description>Generic errors (Rejected, Timeout, Cancelled)</description></item>
    ///   <item><term>100–999</term><description>Federation errors</description></item>
    ///   <item><term>1000+</term><description>Node / slave errors</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Default-value guarantee:</b> <c>0</c> is the C# default for uninitialized
    /// fields, so a zero-initialised event struct naturally means "OK" — consistent
    /// with <c>NedStatusCode</c> already used in Hrot DDS messages.
    /// </para>
    /// </summary>
    public enum OrchestrationStatusCode : int
    {
        // ── Lifecycle (0–9) ────────────────────────────────────────────────────
        Success    = 0,
        InProgress = 1,
        Pending    = 2,

        // ── Generic errors (10–99) ─────────────────────────────────────────────
        Rejected  = 10,
        Timeout   = 11,
        Cancelled = 12,
        Failure   = 13,

        // ── Federation errors (100–999) ────────────────────────────────────────
        InvalidZone      = 101,
        ExerciseMismatch = 102,

        // ── Node / slave errors (1000+) ────────────────────────────────────────
        OutOfMemory   = 1000,
        AssetNotFound = 1001,
    }

    /// <summary>
    /// Extension methods for <see cref="OrchestrationStatusCode"/> and raw <c>int</c> wire values.
    /// </summary>
    public static class OrchestrationStatusCodeExtensions
    {
        /// <summary>
        /// Returns <c>true</c> when <paramref name="code"/> represents a terminal
        /// failure (i.e. any code ≥ 10).
        /// </summary>
        public static bool IsError(this OrchestrationStatusCode code) => (int)code >= 10;

        /// <summary>
        /// Returns <c>true</c> when <paramref name="code"/> represents a terminal
        /// failure (i.e. any code ≥ 10). Overload for raw DDS wire-format integers.
        /// </summary>
        public static bool IsError(this int code) => code >= 10;
    }
}
```

#### Step 3b: Update domain events — change StatusCode fields from int to OrchestrationStatusCode

**File:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterCqrsEvents.cs`

Change `ClusterOpCompletedEvent.StatusCode` from `int` to `OrchestrationStatusCode`.  
Change `NodeOpCompletedEvent.StatusCode` from `int` to `OrchestrationStatusCode`.  
Remove the `/// <summary>Uses <c>OrchestrationStatusCode</c> constants.</summary>` comments (they are now obvious from the type).

**File:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterOpIntents.cs`

Change `StorageOpCompletedEvent.StatusCode` from `int` to `OrchestrationStatusCode`.  
(The `StorageOpCompletedEvent` struct is defined here — search for it.)

**DDS structs in `Hrot.NED/Orchestration/OrchestrationMessages.cs`:** Do **NOT** change `NodeOpStatus.StatusCode` or `ClusterOpStatus.StatusCode` in this file. DDS wire structs must remain `int`.

#### Step 3c: Update ClusterMaster.cs

1. Change `PublishOpStatus(Guid requestId, int statusCode)` signature to `PublishOpStatus(Guid requestId, OrchestrationStatusCode statusCode)` (line ~1099).  
2. Update the body of `PublishOpStatus` — the DDS path writes to `ClusterOpStatus.StatusCode` (int): cast with `(int)statusCode`.  
3. The internal private nested struct field near line 182: `public int FailureCode;` — change to `public OrchestrationStatusCode FailureCode;`.  
4. At line 1269: `tracker.FailureCode = ev.StatusCode;` — leave as-is (both are now `OrchestrationStatusCode`).  
5. At line 1312: `OrchestrationStatusCode.IsError(status.StatusCode)` — here `status` is a DDS `NodeOpStatus` (int). Change to `status.StatusCode.IsError()`.  
6. At line 1266: `OrchestrationStatusCode.IsError(ev.StatusCode)` — here `ev` is `NodeOpCompletedEvent` (domain event, now `OrchestrationStatusCode`). Change to `ev.StatusCode.IsError()`.  
7. At line 1359: `OrchestrationStatusCode.IsError(status.StatusCode)` — same as item 5, DDS struct int. Change to `status.StatusCode.IsError()`.  
8. Lines 1319–1329 (the `_sysOpStatusWriter.Write(new ClusterOpStatus { StatusCode = OrchestrationStatusCode.Rejected, ... })` etc.) — cast: `StatusCode = (int)OrchestrationStatusCode.Rejected`.  
9. Around line 1276: `tracker.HasFailure ? tracker.FailureCode : OrchestrationStatusCode.Success` — this passes to `PublishOpStatus(..., OrchestrationStatusCode)`, now both sides must be `OrchestrationStatusCode`. This is fine after step 3.

#### Step 3d: Update NodeOpSlaveTranslator.cs (Hrot.Common/Orchestration/NodeOpSlaveTranslator.cs)

In the status egress section (around line 103):
```csharp
// Before:
StatusCode      = ev.StatusCode,
// After:
StatusCode      = (int)ev.StatusCode,
```
(DDS `NodeOpStatus.StatusCode` is `int`; domain event `ev.StatusCode` is now `OrchestrationStatusCode`.)

#### Step 3e: Update NodeOpMasterTranslator.cs (Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs)

In the ingress section (around line 94):
```csharp
// Before:
StatusCode      = status.StatusCode,
// After:
StatusCode      = (OrchestrationStatusCode)status.StatusCode,
```
(DDS `status.StatusCode` is `int`; domain event field is now `OrchestrationStatusCode`.)

#### Step 3f: Update ClusterOpMasterTranslator.cs (Hrot.Orchestrator/Translators/ClusterOpMasterTranslator.cs)

Around lines 59 and 70: `StatusCode = ev.StatusCode` — here `ev` is `ClusterOpCompletedEvent` or `StorageOpCompletedEvent` (domain events, now `OrchestrationStatusCode`). DDS `ClusterOpStatus.StatusCode` is int. Change to:
```csharp
StatusCode = (int)ev.StatusCode,
```

#### Step 3g: Update Hrot.ClusterRunner

**File:** `Hrot.ClusterRunner/Testing/OrchestratorActionHandlers.cs` (line ~181)  
```csharp
// Before:
if (OrchestrationStatusCode.IsError(data.StatusCode))
// After:
if (data.StatusCode.IsError())
```
(`data` is a DDS struct with `int StatusCode` — the int extension method handles this.)

#### Step 3h: Update Tests

**File:** `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/OrchestrationContractTests.cs`

Update the `OrchestrationStatusCode_IsError_CorrectlyCategorises` test:
```csharp
// Before:
Assert.False(OrchestrationStatusCode.IsError(OrchestrationStatusCode.Success), ...);
Assert.False(OrchestrationStatusCode.IsError(OrchestrationStatusCode.InProgress), ...);
Assert.True(OrchestrationStatusCode.IsError(OrchestrationStatusCode.Rejected), ...);
Assert.True(OrchestrationStatusCode.IsError(1001), ...);
// After:
Assert.False(OrchestrationStatusCode.Success.IsError(), ...);
Assert.False(OrchestrationStatusCode.InProgress.IsError(), ...);
Assert.True(OrchestrationStatusCode.Rejected.IsError(), ...);
Assert.True(((OrchestrationStatusCode)1001).IsError(), ...);
```

**File:** `Hrot.Orchestrator.Integration.Tests/CqrsOrchestrationIntegrationTests.cs` (line ~344)  
```csharp
// Before:
Assert.True(OrchestrationStatusCode.IsError(result!.Value.StatusCode), ...);
// After:
Assert.True(result!.Value.StatusCode.IsError(), ...);
```
(Check the type of `result.Value` — if it's a domain event, `StatusCode` is now `OrchestrationStatusCode` and `.IsError()` is the extension method. If it's a DDS struct, the int overload handles it.)

**File:** `Hrot.Orchestrator.Tests/ClusterMasterPrefetchTests.cs` (lines ~160, ~176)  
```csharp
// Before:
while (DateTime.UtcNow < deadline && !OrchestrationStatusCode.IsError(observedStatus ?? 0))
Assert.True(OrchestrationStatusCode.IsError(observedStatus ?? 0), ...);
// After:
while (DateTime.UtcNow < deadline && !(observedStatus ?? 0).IsError())
Assert.True((observedStatus ?? 0).IsError(), ...);
```
Or if `observedStatus` has type changed to `OrchestrationStatusCode?`:
```csharp
while (DateTime.UtcNow < deadline && !(observedStatus ?? OrchestrationStatusCode.Success).IsError())
```
Check the declaration of `observedStatus` first — if it's `int?`, leave as int extension; if you can cleanly change it to `OrchestrationStatusCode?`, do so.

**File:** `Hrot.ClusterRunner.Tests/ClusterUiCacheTests.cs` (line ~164)  
```csharp
StatusCode = 0,      // OrchestrationStatusCode.Success
```
This is setting `ClusterOpStatus.StatusCode` (DDS struct, int) — leave as-is (`0` is fine for a DDS int field). No change needed here.

**File:** `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/ReferenceHandlerTests.cs` (line ~82)  
```csharp
Assert.Equal(OrchestrationStatusCode.Success, status.StatusCode);
```
After conversion, `status.StatusCode` is `OrchestrationStatusCode` and `OrchestrationStatusCode.Success` is an enum member. This should compile and work without change.

**File:** `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/ClusterSlaveTests.cs` (line ~227)  
Same as above — should work without change.

---

### Task 4: Bootstrap Latch Case-Insensitive Fix (TASK-D06)

**Task Definition:** See [TASK-DEFINITIONS.md](../TASK-DEFINITIONS.md#task-d06--bootstrap-latch-case-insensitive-fix)

**File:** `Hrot.Orchestrator/ClusterMaster.cs`

Find `CheckBootstrapLatch()` (around line 526–550). Change:
```csharp
// Before:
if (kv.Value.SubsystemName == name && kv.Value.LocalClusterState == ClusterState.Idle)
// After:
if (string.Equals(kv.Value.SubsystemName, name, StringComparison.OrdinalIgnoreCase)
    && kv.Value.LocalClusterState == ClusterState.Idle)
```

**Tests to add:** Add to `Hrot.Orchestrator.Tests/ClusterMasterBootstrapTests.cs`:

```csharp
/// <summary>
/// Verifies bootstrap latch releases when subsystem name differs only in casing.
/// Regression test for the case-sensitive comparison bug.
/// </summary>
[Fact(Timeout = 10_000)]
public void BootstrapLatch_ReleasesWithCaseInsensitiveSubsystemName()
{
    var config = new ClusterConfiguration
    {
        Mandatory               = new[] { "simhost" },   // lowercase in config
        HeartbeatTimeoutSeconds = 60f,
        TransactionHistoryCapacity = 10,
    };

    var bus = new FdpEventBus();
    var master = new ClusterMaster(bus, config);

    // Feed a heartbeat with mixed-case name "SimHost" (not "simhost")
    bus.Publish(new NodeHeartbeatEvent
    {
        NodeId        = 1,
        LocalStateId  = (int)ClusterState.Idle,
        WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        SubsystemName = "SimHost",  // different case from config
    });

    master.Tick();

    // Bootstrap should now be complete — Standby should have been published
    var events = bus.ConsumeManaged<ClusterStateTransitionedEvent>().ToList();
    Assert.True(events.Any(e => e.NewStateId == ClusterState.Idle || 
                                e.NewStateId == ClusterState.Standby),
        "Expected bootstrap latch to release when subsystem name matches case-insensitively.");
}

[Fact(Timeout = 5_000)]
public void BootstrapLatch_DoesNotReleaseForWrongSubsystemName()
{
    var config = new ClusterConfiguration
    {
        Mandatory               = new[] { "simhost" },
        HeartbeatTimeoutSeconds = 60f,
        TransactionHistoryCapacity = 10,
    };

    var bus = new FdpEventBus();
    var master = new ClusterMaster(bus, config);

    // Feed a heartbeat with completely different name
    bus.Publish(new NodeHeartbeatEvent
    {
        NodeId        = 1,
        LocalStateId  = (int)ClusterState.Idle,
        WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        SubsystemName = "IG",
    });

    master.Tick();

    // Should not publish Standby — latch unreleased
    var events = bus.ConsumeManaged<ClusterStateTransitionedEvent>().ToList();
    Assert.DoesNotContain(events, e => e.NewStateId == ClusterState.Standby);
}
```

**Note:** Adjust the constructor call for `ClusterMaster(bus, config)` to match the actual test constructor used in `ClusterMasterBootstrapTests.cs` (look at the existing test file for the correct pattern). The existing test at line ~63 shows `new ClusterMaster(orchParticipant, config)` for DDS mode — use the bus-mode constructor (with `FdpEventBus`) for these unit tests.  
If the bus-mode constructor for ClusterMaster requires additional setup (like `DdsIdAllocatorServer`), look at existing bus-mode tests in `Hrot.Orchestrator.Tests/` for the correct setup pattern.

---

## 🧪 Testing Requirements

- **Minimum new tests:** 2 (TASK-D06 bootstrap tests) + 1 (TASK-D03 struct type test).
- **All existing tests must continue to pass** — this batch makes no behavioral changes except the bootstrap latch fix.
- Tests must verify **actual behavior**, not just compilation.

---

## ⚠️ Quality Standards

**❗ TEST QUALITY:**
- NOT ACCEPTABLE: Tests that only verify code compiles or properties can be set.
- REQUIRED: The TASK-D06 tests must use the bus-mode `ClusterMaster` to simulate heartbeats and verify the actual bootstrap behavior (latch release / non-release).

**❗ DDS STRUCTS:**
- NEVER change `int StatusCode` in DDS structs (`NodeOpStatus`, `ClusterOpStatus` in `Hrot.NED/Orchestration/OrchestrationMessages.cs`). These are wire-format structs — they stay as int. Only domain event structs change to the enum.

**❗ NO BACKWARD-COMPAT HACKS:**
- Remove the old `const int` fields entirely. Do not keep them as `[Obsolete]` aliases.

---

## 📊 Report Requirements

In your report, address:

**Q1:** What issues did you encounter while updating the call sites? Were there any non-obvious cast locations?

**Q2:** Did you find any other const int fields or int StatusCode usages beyond the ones listed? What were they?

**Q3:** Were there any test constructors or infrastructure you had to adapt for the bootstrap latch tests? How did you handle them?

**Q4:** What edge cases or unexpected interactions did you discover?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] TASK-D04: All 10 handler files have no `const int *OperationId` fields; all handler tests pass.
- [ ] TASK-D03: `ClusterStateTransitionedEvent.NewStateId` is `ClusterState` type; new structural test passes.
- [ ] TASK-D05: `OrchestrationStatusCode` is an `enum`; all three event `StatusCode` fields are `OrchestrationStatusCode`; `IsError` is an extension method; all 4 test files updated; full test suite passes.
- [ ] TASK-D06: `CheckBootstrapLatch()` uses `OrdinalIgnoreCase`; new bootstrap case-sensitivity tests pass.
- [ ] Full build succeeds: `dotnet build IOS-IG-SimHost.sln`
- [ ] All affected test projects pass.
- [ ] Report submitted to `.dev/cluster-master-cqrs-2/reports/BATCH-01-REPORT.md`.

---

## ⚠️ Common Pitfalls to Avoid

1. **Forgetting DDS struct casts:** `NodeOpStatus.StatusCode` and `ClusterOpStatus.StatusCode` remain `int`. Every time you write to a DDS struct, cast: `(int)ev.StatusCode`. Every time you read from a DDS struct, cast: `(OrchestrationStatusCode)status.StatusCode`.
2. **Missing translation in ClusterOpMasterTranslator:** The `StatusCode = ev.StatusCode` assignments there will fail silently if you only find them in `NodeOpMasterTranslator`. Check both translators.
3. **The `_sysOpStatusWriter.Write(...)` calls in ClusterMaster:** These write to DDS `ClusterOpStatus` (int). Cast: `StatusCode = (int)OrchestrationStatusCode.Rejected`.
4. **Bus-mode vs. DDS-mode paths in ClusterMaster:** ClusterMaster has two paths — bus-mode (uses FdpEventBus) and DDS-mode (uses writers directly). Ensure both paths remain correct after the change.

---

## 📚 Reference Materials
- **Task Definitions:** [TASK-DEFINITIONS.md](../TASK-DEFINITIONS.md) — TASK-D03, TASK-D04, TASK-D05, TASK-D06
- **Previous cqrs-1 work:** `.dev/cluster-master-cqrs-1/TASK-TRACKER.md` — context on what was already done
- **FDP Orchestration tests:** `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/OrchestrationContractTests.cs`
- **Bootstrap tests:** `Hrot.Orchestrator.Tests/ClusterMasterBootstrapTests.cs`
