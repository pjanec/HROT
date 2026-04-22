# BATCH-03: Time-Control Bus Wiring — Sever Callbacks, Wire MasterSyncController to Bus

**Batch Number:** BATCH-03
**Tasks:** HEXAG2-DEBT-005, HEXAG2-S010, HEXAG2-S011
**Phase:** Phase 2 — Hexagonal Architecture Compliance (time-control decoupling)
**Estimated Effort:** 10-14 hours

---

## Onboarding

You are continuing Phase 2 of the hexag-2 design. BATCH-01 and BATCH-02 are complete and
committed. Read these documents before starting:

- `.dev/hexag-2/DESIGN.md` — Section 4.2.3 (S010), Section 4.2.8 (S011)
- `.dev/hexag-2/TASK-DETAIL.md` — tasks HEXAG2-S010, HEXAG2-S011
- `.dev/hexag-2/reviews/BATCH-02-REVIEW.md` — feedback and known issues from BATCH-02
- `.dev/hexag-2/ONBOARDING.md`

**Goal of this batch:**
Eliminate the two structural couplings that keep time-control commands flowing through domain
objects (`ClusterMaster`) rather than through the bus:

1. `ClusterOpMasterTranslator` still has a `_unhandledRequestCallback` that calls
   `_clusterMaster.HandleClusterOpRequest` for time-control ops. Remove this callback.
   The translator itself must publish the four typed intents to the bus.

2. `ClusterMaster` still fires a `TimeControlRequested` C# event that
   `OrchestratorSubsystem` subscribes to, routing time-control commands to
   `MasterSyncController`. Remove this event and subscription entirely.
   `MasterSyncController.Update()` must drain the four time-control intents
   directly from the bus read buffer.

Also fix the pre-existing audit test path bug (HEXAG2-DEBT-005).

**Development branch:** All changes go on the current working branch (hexag).

**Build command:** `dotnet build IOS-IG-SimHost.sln -v q`
**Test command single project:** `dotnet test <project.csproj> -v q`
**Test command full:** `dotnet test IOS-IG-SimHost.sln -v q`

**Key project paths:**
- `Hrot.Core` (interfaces): `Hrot/Engine/Hrot.Core/Hrot.Core.csproj`
- `Fdp.Toolkits`: `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj`
- `Fdp.Toolkits.Tests`: `FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj`
- `Hrot.Network.Orchestration`: `Hrot/Network/Hrot.Network.Orchestration/Hrot.Network.Orchestration.csproj`
- `Hrot.Orchestrator`: `Hrot/Subsystems/Hrot.Orchestrator/Hrot.Orchestrator.csproj`
- `Hrot.Orchestrator.Tests`: `Hrot/Subsystems/Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj`
- `Hrot.ClusterRunner.Tests`: `Hrot/Runner/Hrot.ClusterRunner.Tests/Hrot.ClusterRunner.Tests.csproj`

---

## Developer Insights Section

When writing your report, answer these questions explicitly:
1. **What issues were encountered?** (namespace conflicts, test failures, unexpected
   caller sites of `TimeControlRequested`)
2. **What weak points did you spot?** (missing test coverage, fragile coupling patterns)
3. **What design decisions did you make beyond the spec?** (approach for slave-node ID
   set propagation, handling of tests that tested old behavior)

---

## Test-Driven Task Progression (MANDATORY)

For every task:
1. Read the success conditions in TASK-DETAIL.md before touching any code.
2. Write or verify tests first where applicable.
3. Implement until all tests pass.
4. Run the full test suite after each task.
5. Do not move to the next task until current task's tests pass.

---

## Tasks

---

### HEXAG2-DEBT-005 — Fix Audit Test Hard-Coded Path

**File to change:**
- `Hrot/Runner/Hrot.ClusterRunner.Tests/ExConSubsystemClusterTests.cs`

**Problem:**
The audit test `ExConSubsystem_HasNoDirectClusterMasterReference` navigates to
`ExConSubsystem.cs` using a relative path built from `AppDomain.CurrentDomain.BaseDirectory`
and four `".."` segments. This path resolves to `Hrot/Runner/Hrot.ExCon/ExConSubsystem.cs`
which does not exist. The file actually lives at `Hrot/Subsystems/Hrot.ExCon/ExConSubsystem.cs`.

**What to do:**
Replace the hard-coded relative path traversal with a robust approach. The recommended fix
is to walk up from `AppDomain.CurrentDomain.BaseDirectory` until a known sentinel file is
found (e.g., `IOS-IG-SimHost.sln`), then build the path from there. Use this helper pattern:

```csharp
private static string FindWorkspaceRoot()
{
    var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IOS-IG-SimHost.sln")))
        dir = dir.Parent;
    return dir?.FullName ?? throw new DirectoryNotFoundException("Workspace root not found.");
}
```

Then change the path construction to:
```csharp
var source = File.ReadAllText(
    Path.Combine(
        FindWorkspaceRoot(),
        "Hrot", "Subsystems", "Hrot.ExCon", "ExConSubsystem.cs"));
```

**Success condition:**
The test passes when run from the standard `dotnet test` output directory.

---

### HEXAG2-S010 — Sever unhandledRequestCallback from ClusterOpMasterTranslator

**Files to change:**
- `Hrot/Network/Hrot.Network.Orchestration/ClusterOpMasterTranslator.cs`
- `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs`
- `Hrot/Subsystems/Hrot.Orchestrator.Tests/ClusterMasterTimeControlTests.cs`
  (update tests that relied on `TimeControlRequested` from the translator side)

**What the intent structs look like (already exist in `Fdp.Toolkit.Time.Domain`):**
```
FDP/Toolkits/Fdp.Toolkits/Time/Domain/TimeLocalEvents.cs:
    public struct PauseTimeIntent    { }
    public struct ResumeTimeIntent   { }
    public struct StepTimeIntent     { public float DeltaSeconds; }
    public struct SetTimeScaleIntent { public float TimeScale; }
```
Do NOT redefine these. Add a project reference from `Hrot.Network.Orchestration` to
`Fdp.Toolkits` if one does not already exist.

**Step 1: Add the four time-control cases to `ClusterOpMasterTranslator.ProcessRequest()`**

Add these cases before the `default:` clause in `ProcessRequest()`. For `StepTime`,
deserialize the payload JSON to extract a `DeltaSeconds` float. If deserialization fails or
the field is missing, use `1f/60f` as the default. For `SetTimeScale`, parse a float from
`PayloadJson` (as done in the current `TimeControlRequested` handler in
`OrchestratorSubsystem.Initialize()`).

```csharp
case NedClusterOpType.PauseTime:
    _bus.PublishManaged(new PauseTimeIntent());
    break;

case NedClusterOpType.ResumeTime:
    _bus.PublishManaged(new ResumeTimeIntent());
    break;

case NedClusterOpType.StepTime:
{
    float delta = TryParseStepDelta(req.PayloadJson, 1f / 60f);
    _bus.PublishManaged(new StepTimeIntent { DeltaSeconds = delta });
    break;
}

case NedClusterOpType.SetTimeScale:
{
    float scale = TryParseFloat(req.PayloadJson, 1f);
    _bus.PublishManaged(new SetTimeScaleIntent { TimeScale = scale });
    break;
}
```

Add `private static float TryParseStepDelta(string? json, float defaultVal)` and
`private static float TryParseFloat(string? json, float defaultVal)` private helpers.
For `StepTime`, the payload is a plain float string (matching the existing
`ParseStepDelta` in `OrchestratorSubsystem`). For `SetTimeScale`, same.

**Step 2: Remove the `_unhandledRequestCallback` field and parameter**

- Delete `private readonly Action<ClusterOpRequest>? _unhandledRequestCallback;`
- Delete the `unhandledRequestCallback` parameter from the constructor.
- Delete the assignment `_unhandledRequestCallback = unhandledRequestCallback;`
- Delete `default: _unhandledRequestCallback?.Invoke(req);` from `ProcessRequest()`.
  Replace it with: `default: break;` (or remove default entirely — both are acceptable).

**Step 3: Update OrchestratorSubsystem.Initialize()**

Remove the `unhandledRequestCallback: _clusterMaster.HandleClusterOpRequest` argument from
the `ClusterOpMasterTranslator` constructor call (line ~115 in `OrchestratorSubsystem.cs`).
The constructor now takes no callback parameter.

**Step 4: Update tests**

`ClusterMasterTimeControlTests` currently tests that `TimeControlRequested` fires after
`HandleClusterOpRequest`. Those tests cover behavior that is being moved to the bus.
Replace the two existing tests in `ClusterMasterTimeControlTests.cs` with bus-based tests:

```
ClusterOpMasterTranslator_PauseTime_PublishesIntentToBus
ClusterOpMasterTranslator_ResumeTime_PublishesIntentToBus
ClusterOpMasterTranslator_StepTime_PublishesIntentToBus
ClusterOpMasterTranslator_SetTimeScale_PublishesIntentToBus
```

For each test:
- Create a `FdpEventBus` and a `MockDdsReader<ClusterOpRequest>` (or use the existing DDS
  stub approach in the test project).
- Feed a `ClusterOpRequest` with the appropriate `OperationType`.
- Call `Tick()`.
- After `Tick()`, call `bus.SwapBuffers()` then assert
  `bus.ConsumeManaged<XxxTimeIntent>()` contains exactly one item.

Check the existing `ClusterOpMasterTranslatorTests.cs` in `Hrot.Orchestrator.Tests` to see
how the mock DDS reader is wired. Use the same pattern.

**Important:** `ClusterOpMasterTranslator_PauseTime_PublishesIntentToBus` is referenced in
`.dev/hexag-2/tests.md` as a component of the "C# Event Severing Test" (Test 3). This is
the correct test to implement there.

**Success conditions from TASK-DETAIL.md HEXAG2-S010:**
1. `_unhandledRequestCallback` field removed from `ClusterOpMasterTranslator`.
2. Constructor has no `unhandledRequestCallback` parameter.
3. `ProcessRequest()` handles all `NedClusterOpType` values by publishing an intent; no
   callback invocation remains.
4. Test `ClusterOpMasterTranslator_PauseTime_PublishesIntentToBus` passes.

---

### HEXAG2-S011 — Eliminate ClusterMaster.TimeControlRequested C# Event

**Files to change:**
- `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs`
- `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/MasterSyncController.cs`
- `Hrot/Subsystems/Hrot.Orchestrator.Tests/ClusterMasterTimeControlTests.cs`
  (tests for the C# event must be deleted or replaced with bus-based equivalents)
- `Hrot/Runner/Hrot.ClusterRunner.Tests/OrchestratorSubsystemTests.cs`
  (update/delete tests that called `TimeControlRequested`)
- `FDP/Toolkits/Fdp.Toolkits.Tests/Time/` (add new tests for `MasterSyncController` bus drain)

**Step 1: Wire MasterSyncController to drain time-control intents from bus**

In `MasterSyncController.Update()` (in `Fdp.Toolkits/Time/Controllers/MasterSyncController.cs`),
add drain loops at the START of `Update()`, BEFORE the mode switch:

```csharp
public GlobalTime Update()
{
    // ── Drain time-control intents from bus (HEXAG2-S011) ─────────────────
    foreach (var _ in _eventBus.ConsumeManaged<PauseTimeIntent>())
    {
        SwitchToDeterministic(_expectedSlaves);
    }
    foreach (var _ in _eventBus.ConsumeManaged<ResumeTimeIntent>())
    {
        SwitchToContinuous();
    }
    foreach (var intent in _eventBus.ConsumeManaged<StepTimeIntent>())
    {
        Step(intent.DeltaSeconds);
    }
    foreach (var intent in _eventBus.ConsumeManaged<SetTimeScaleIntent>())
    {
        SetTimeScale(intent.TimeScale);
    }

    // ── Existing mode switch ───────────────────────────────────────────────
    long currentTicks  = _getTick();
    // ... rest of existing Update() remains unchanged ...
```

Note: `SwitchToDeterministic` and related methods are already public methods on
`MasterSyncController`. Verify this before coding and adjust if they have different names.

**Slave node set for PauseTimeIntent:**
The current `OrchestratorSubsystem` computes the slave ID set inline when
`TimeControlRequested` fires. After this change, `MasterSyncController.Update()` drains
`PauseTimeIntent` which has no payload. The slave set must come from `_expectedSlaves`
(the field already on `MasterSyncController`).

**Decision required:** Choose one of two approaches to keep `_expectedSlaves` current:
- **Option A (recommended):** Add a `SlaveNodeSetUpdatedEvent { public HashSet<int>
  SlaveNodeIds; }` struct to `TimeLocalEvents.cs`. Publish it from `ClusterMaster.Tick()`
  whenever the roster changes (a node joins or leaves). `MasterSyncController.Update()`
  drains this event to replace `_expectedSlaves`.
- **Option B:** Pass the current slave set as a field on `PauseTimeIntent` itself
  (`ImmutableHashSet<int> SlaveNodeIds`). This makes the intent slightly heavier but
  avoids the extra event type.

Document your choice and rationale in the batch report.

**Step 2: Delete ClusterMaster.TimeControlRequested**

In `ClusterMaster.cs`:
- Delete the `public event Action<ClusterOpType, string>? TimeControlRequested;` declaration.
- Delete all sites that raise it: `TimeControlRequested?.Invoke(...)`.
- The `HandleClusterOpRequest()` method and `Tick()` continue to exist; they just no longer
  fire a C# event for time-control ops.

Note: After S010, all non-time-control DDS requests already publish bus intents in the
translator. After S011, `HandleClusterOpRequest` is no longer called with time-control ops
by anyone. Verify the call site in `OrchestratorSubsystem.Initialize()` is already removed
after S010 (it removes `unhandledRequestCallback: _clusterMaster.HandleClusterOpRequest`).

**Step 3: Remove subscription in OrchestratorSubsystem**

In `OrchestratorSubsystem.Initialize()`, delete the entire
`_clusterMaster.TimeControlRequested += (op, payload) => { ... }` block (lines ~153-185).
Also delete the `private bool _isPaused` field (line 43) and all its read/write sites.
The authoritative pause state lives in `ClusterUiCache.IsPaused`.

**Step 4: Update or delete tests**

`ClusterMasterTimeControlTests.cs` has two tests (`TimeControlRequested_FiresOnPauseTime`
and `TimeControlRequested_BypassesTransactionHistory`) that depend on the now-deleted event.

- `TimeControlRequested_FiresOnPauseTime`: DELETE this test (the behavior it tested is
  replaced by `ClusterOpMasterTranslator_PauseTime_PublishesIntentToBus` from S010).
- `TimeControlRequested_BypassesTransactionHistory`: This test still validates a valid
  invariant (time-control ops must not create 2PC transactions). Keep the test but remove
  the `TimeControlRequested` subscription. The test can simply call
  `exercise.HandleClusterOpRequest(...)`, `exercise.Tick()`, and assert
  `exercise.TransactionHistory.Count == 0`. (The behavior should still hold — the early
  return in `HandleClusterOpRequest` for time-control ops should remain, just no event.)

`OrchestratorSubsystemTests.cs` has `TimeControlRequested_PauseTime_SetsIsPaused` (line 153).
This test is obsolete: the `_isPaused` field being tested is deleted. Replace it with a
bus-based test that asserts `ClusterUiCache.IsPaused` after pumping `PauseTimeIntent`.

Add these two new tests in `Fdp.Toolkits.Tests/Time/` or `Hrot.Orchestrator.Tests/`:

```
MasterSyncController_DrainsPauseTimeIntent_SwitchesToDeterministic
MasterSyncController_DrainsResumeTimeIntent_SwitchesToContinuous
```

For each test (from TASK-DETAIL.md success conditions):
- Create `FdpEventBus bus = new FdpEventBus()`.
- For the first: `bus.PublishManaged(new PauseTimeIntent())`, call `bus.SwapBuffers()`,
  then call `masterSync.Update()`, assert `MasterSyncController` mode is `BarrierPending`.
  (Access the mode via a `CurrentModeForTest` internal property or reflection — check the
  existing MasterSyncController test files to see how mode is currently exposed.)
- For the second: Pause first, then `bus.PublishManaged(new ResumeTimeIntent())`, swap,
  `Update()`, assert mode is `Continuous`.

Check `FDP/Toolkits/Fdp.Toolkits.Tests/Time/` for existing tests to understand how mode
is accessed in tests.

**Success conditions from TASK-DETAIL.md HEXAG2-S011:**
1. `ClusterMaster` has no `TimeControlRequested` event member.
2. `OrchestratorSubsystem` has no `_isPaused` field and no `TimeControlRequested`
   subscription.
3. `MasterSyncController.Update()` contains drain loops for all four time-control intent
   types.
4. Test `MasterSyncController_DrainsPauseTimeIntent_SwitchesToDeterministic` passes.
5. Test `MasterSyncController_DrainsResumeTimeIntent_SwitchesToContinuous` passes.

---

## Report Format

Write your report to `.dev/hexag-2/reports/BATCH-03-REPORT.md` with these sections:

```
# BATCH-03 Report

## Tasks Completed
- [ ] HEXAG2-DEBT-005
- [ ] HEXAG2-S010
- [ ] HEXAG2-S011

## Tests Written
<table with test name and file location>

## Test Results
<dotnet test output summaries for all relevant test projects>

## Developer Insights
### Issues Encountered
### Weak Points Spotted
### Design Decisions Made Beyond Spec

## Files Changed
### New files
### Modified files
### Deleted files
```
