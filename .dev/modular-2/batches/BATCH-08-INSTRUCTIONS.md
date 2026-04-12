# BATCH-08: Decouple Subsystems from NED + Fix Pre-existing Test Failures

**Batch Number:** BATCH-08
**Tasks:** TASK-P4-001, TASK-P4-002, TASK-P4-003 + DEBT-001 fixes + DEBT-005 fixes
**Phase:** Phase 4 (first half) + Debt resolution
**Estimated Effort:** 10-14 hours
**Priority:** HIGH
**Dependencies:** BATCH-07 committed (Hrot.Network.BDC, INetworkFactory, NedNetworkFactory all in place)

---

## Onboarding & Workflow

### Required Reading (IN ORDER)
1. `.dev/modular-2/DESIGN.md` — Full architecture overview, especially "Guiding Principles" and the Assembly Map.
2. `.dev/modular-2/TASK-DETAIL.md` — Sections TASK-P4-001, TASK-P4-002, TASK-P4-003 (detailed scopes and constraints).
3. `.dev/modular-2/DEBT-TRACKER.md` — DEBT-001 and DEBT-005 descriptions.
4. `.dev/modular-2/reviews/BATCH-07-REVIEW.md` — Most recent review.
5. `.dev/modular-2/reports/BATCH-07-REPORT.md` — Most recent report (API adaptations found by previous developer).

### Source Code Overview
- **Neutral interfaces (already defined):**
  - `Hrot.Core/Network/INetworkFactory.cs` — factory contract
  - `Hrot.Core/Network/ICommandGateway.cs` — neutral command gateway
  - `Hrot.Core/Network/IExConEgressWriters.cs` — neutral ExCon egress writers
  - `Hrot.Core/Network/Commands.cs` — neutral DTOs: `CreateEntityCommand`, `UpdateEntityDescriptorCommand`, `MissionControlCommand`, `MapConfigDto`, `MapCommandDto`
- **Stub factory (to be completed):**
  - `Hrot.Network.NED/Factory/NedNetworkFactory.cs` — `CreateCommandGateway()` returns `NullCommandGateway`, `CreateExConEgressWriters()` returns `NullExConEgressWriters` — these TODOs must be wired in this batch.
- **Existing NED command gateway (to be adapted):**
  - `Hrot.Network.NED/Commands/NedCommandGateway.cs` — currently implements `INedCommandGateway` from `Hrot.Map.Common`. Needs to also implement `ICommandGateway` from `Hrot.Core`.
- **Subsystems to decouple:**
  - `Hrot.ExCon/ExConLogic.cs` — uses DDS-typed writers directly
  - `Hrot.SimHost/NodeBootstrapper.cs` or `Hrot.SimHost/SimHostApp.cs` — references `Hrot.Network.NED`
  - `Hrot.IG/IgApplication.cs` — uses `INedCommandGateway`
  - `Hrot.CGF/CgfApplication.cs` — uses `Hrot.Network.NED`

### Report Submission
Submit your report to: `.dev/modular-2/reports/BATCH-08-REPORT.md`

If you have questions, create: `.dev/modular-2/questions/BATCH-08-QUESTIONS.md`

---

## Context

This batch completes the "assembly decoupling" half of Phase 4. The factory infrastructure
(`INetworkFactory`, `ICommandGateway`, `IExConEgressWriters`, `NedNetworkFactory`) was
established in BATCH-05/06; the implementations inside `NedNetworkFactory` are currently
no-op stubs waiting for this batch.

The core goal: after this batch, no subsystem library (`Hrot.ExCon`, `Hrot.SimHost`,
`Hrot.IG`, `Hrot.CGF`) shall directly reference `Hrot.Network.NED`. Each subsystem
operates through neutral interfaces from `Hrot.Core`.

Pre-existing test failures must also be resolved in this batch — they are architectural
correctness issues surfaced by the decoupling work.

---

## MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Fix DEBT-001 + DEBT-005** — Fix all pre-existing test failures → All affected test projects pass ✅
2. **TASK-P4-001** — Decouple ExCon → `Hrot.ExCon` assembly has zero NED references → All ExCon tests pass ✅
3. **TASK-P4-002** — Decouple SimHost → `Hrot.SimHost` assembly has zero NED references → All SimHost tests pass ✅
4. **TASK-P4-003** — Decouple IG and CGF → Both assemblies have zero NED references → All IG tests pass ✅
5. **Final validation** — `dotnet build IOS-IG-SimHost.sln --no-incremental` with zero errors AND `dotnet test IOS-IG-SimHost.sln` with zero failures ✅

**Do NOT stop between tasks to ask for permission. Do NOT stop after partial completion. Fix all
root causes. Only write the report after all tests pass with zero failures.**

---

## Task 0: Fix Pre-existing Test Failures (DEBT-001 + DEBT-005)

### Sub-task 0a: Fdp.Engine.Tests — TimeConfig default and related failures (DEBT-005)

Run: `dotnet test FDP\Toolkits\Fdp.Engine.Tests\Fdp.Engine.Tests.csproj --no-build`

Currently failing:
- `TimeConfigTests.TimeConfig_Default_SyncRefreshIntervalTicks_Is1Second`
- `SlaveSyncControllerTests.SlaveSyncController_Update_SendsPeriodicResync`
- `ReplayModuleTests.ReplayModule_SeekToFrameAsync_IsOffMainThread`
- and possibly `Fdp.Examples.CarKinem.Tests` — `SpatialHashSystem_IndexesEntity_WithSimTransformButNoVehicleState`

**Fix approach:**
- Find `TimeConfig.Default` in `FDP/Toolkits/Fdp.Engine/` (under `FDP.Toolkit.Time` namespace).
  Determine if the test is correct (expects 1s) or the code is correct (60s).
  The test file (`TimeConfigTests.cs`) is the specification — fix the code to match the test.
- For `SlaveSyncController` failure: likely related to the same `SyncRefreshIntervalTicks` default.
- For `ReplayModule_SeekToFrameAsync_IsOffMainThread`: run `dotnet test --filter ReplayModule_SeekToFrameAsync`
  to see the actual error message, then fix the root cause. Do NOT comment out or skip tests.
- For CarKinem `SpatialHashSystem` failure: examine the error and fix the root cause.

### Sub-task 0b: Hrot.SimHost.Tests — Routing guard failures (DEBT-001)

Run: `dotnet test Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj --no-build`

The `CreateEntityRequestSystem` routes incoming requests based on `request.Owner.AppInstanceId`.
When `AppInstanceId != localNodeId` AND `AppInstanceId != 0` (not broadcast), the request
is silently dropped. Tests calling `BuildSystem(tkb, source)` without `isDefaultProcessor: true`
and using a request with `Owner.AppInstanceId = 2` (non-matching) will see an empty collection.

**Fix approach:**
For tests that validate "valid request is processed" behavior: the requests should either:
- Use `Owner.AppInstanceId = LocalNodeId` (7) so the system knows it is targeted at this node, OR
- Use `Owner.AppInstanceId = 0` (broadcast) AND create the system with `isDefaultProcessor: true`

Do not change the routing guard logic — it is correct. Update the tests to use the right request structure.
In `CreateEntityRequestSystemTests.MakeValidRequest()`, change `AppInstanceId` to match `LocalNodeId` 
(or update each test that fails with the minimal correct change).

Similarly fix `SimHostComponentRegistrationTests` and `TranslatorPackTests` failures in
`Hrot.SimHost.Tests` — run the tests with `--filter` to see the error messages.

### Sub-task 0c: Hrot.IG.Tests — TypeInitializationException for EntityInfo (DEBT-001)

The `UniqueNameGeneratorTests` failures are caused by:
```
System.ArgumentException : GenericArguments[0], 'Hrot.NED.Descriptors.EntityInfo',
violates the constraint of type 'T' in RegisterManagedComponentInternal[T].
```

`Hrot.NED.Descriptors.EntityInfo` (a DDS-generated struct) cannot be registered as a
managed ECS component because it has fields that violate the generic constraint.

**Fix approach:**
Look at `Hrot.IG.Tests\UniqueNameGeneratorTests.cs` and the IG source where `EntityInfo`
is registered as a component. Replace the NED-specific `EntityInfo` registration with a
neutral alternative already defined in `Hrot.Core` (check `Hrot.Core/Components/` for
suitable neutral entity info types), or use a test-local stub struct that satisfies the
managed component constraint.

This is a preview of P4-003 work: the IG layer should reference neutral types, not NED schema types.

### Sub-task 0d: Hrot.ClusterRunner.Tests failures (DEBT-001)

Run: `dotnet test Hrot.ClusterRunner.Tests\Hrot.ClusterRunner.Tests.csproj --no-build`

5 tests are failing. For each failure, examine the error message and fix the root cause.
Known issues from DEBT-001: "routing guard + ActionDispatch count" — these may follow the
same pattern as the SimHost routing guard issue.

---

## Task 1: TASK-P4-001 — Decouple ExCon from NED

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-p4-001-decouple-excon-from-ned)

### Step 1a: Implement NedExConEgressWriters in Hrot.Network.NED

Create `Hrot.Network.NED/ExCon/NedExConEgressWriters.cs` implementing `IExConEgressWriters`
from `Hrot.Core.Network`. This class wraps the NED DDS writers:

```csharp
// File: Hrot.Network.NED/ExCon/NedExConEgressWriters.cs
using CycloneDDS.Runtime;
using Hrot.Core.Network;
using Hrot.NED.Messages;

namespace Hrot.Network.NED.ExCon;

/// <summary>
/// NED wire transport implementation of IExConEgressWriters.
/// Wraps four DDS writers for ExCon entity lifecycle commands.
/// </summary>
internal sealed class NedExConEgressWriters : IExConEgressWriters
{
    private readonly IDdsWriter<MapInteractionConfig>  _configWriter;
    private readonly IDdsWriter<CreateEntityRequest>   _createEntityWriter;
    private readonly IDdsWriter<MapCommandRequest>?    _commandWriter;
    private readonly IDdsWriter<DeleteEntityRequest>?  _deleteEntityWriter;

    public NedExConEgressWriters(
        IDdsWriter<MapInteractionConfig>  configWriter,
        IDdsWriter<CreateEntityRequest>   createWriter,
        IDdsWriter<MapCommandRequest>?    commandWriter,
        IDdsWriter<DeleteEntityRequest>?  deleteWriter)
    {
        _configWriter       = configWriter;
        _createEntityWriter = createWriter;
        _commandWriter      = commandWriter;
        _deleteEntityWriter = deleteWriter;
    }

    public void WriteMapConfig(MapConfigDto config)
        => _configWriter.Write(/* translate MapConfigDto -> MapInteractionConfig */ ...);

    public void WriteDeleteEntity(int entityId)
        => _deleteEntityWriter?.Write(new DeleteEntityRequest { EntityId = entityId });

    public void WriteCreateEntity(CreateEntityCommand cmd)
        => /* translate neutral CreateEntityCommand to NED CreateEntityRequest */;

    public void WriteMapCommand(MapCommandDto cmd)
        => /* translate neutral MapCommandDto to NED MapCommandRequest */;

    public void Dispose()
    {
        // Writers are owned by the caller (DdsParticipant manages lifetime).
    }
}
```

Look at how `ExConSubsystem.cs` currently creates `MapInteractionConfig` and
`CreateEntityRequest` structs from the data it has — use the same translation logic
but inverted: translate from neutral DTOs to NED wire types.

### Step 1b: Wire NedNetworkFactory.CreateExConEgressWriters()

In `Hrot.Network.NED/Factory/NedNetworkFactory.cs`, replace `NullExConEgressWriters` stub:

```csharp
public IExConEgressWriters CreateExConEgressWriters()
{
    if (_participant == null) return new NullExConEgressWriters();
    return new NedExConEgressWriters(
        new DdsWriter<MapInteractionConfig>(_participant),
        new DdsWriter<CreateEntityRequest>(_participant),
        new DdsWriter<MapCommandRequest>(_participant),
        new DdsWriter<DeleteEntityRequest>(_participant));
}
```

### Step 1c: Make NedCommandGateway implement ICommandGateway

`NedCommandGateway` currently implements `INedCommandGateway` (from `Hrot.Map.Common`).
Also make it implement `ICommandGateway` (from `Hrot.Core.Network`):

```csharp
// Hrot.Network.NED/Commands/NedCommandGateway.cs
public class NedCommandGateway : INedCommandGateway, ICommandGateway
{
    // Existing implementation...

    // ICommandGateway.CreateEntityAsync — translate neutral CreateEntityCommand to NED struct
    async Task<int> ICommandGateway.CreateEntityAsync(CreateEntityCommand cmd, CancellationToken ct)
    {
        var request = NedTranslationHelper.ToCreateEntityRequest(cmd);
        var ack = await CreateEntityAsync(request);
        return ack.EntityId;
    }

    // ICommandGateway.SendUpdateDescriptorAsync
    async Task ICommandGateway.SendUpdateDescriptorAsync(UpdateEntityDescriptorCommand cmd, CancellationToken ct)
    {
        var req = NedTranslationHelper.ToUpdateDescriptorRequest(cmd);
        SendUpdateDescriptor(req);
    }

    // ICommandGateway.SendMissionControlRequestAsync
    async Task ICommandGateway.SendMissionControlRequestAsync(MissionControlCommand cmd, CancellationToken ct)
    {
        var req = NedTranslationHelper.ToMissionControlRequest(cmd);
        await SendMissionControlRequestAsync(req);
    }

    void IDisposable.Dispose() => Dispose();
}
```

Create `Hrot.Network.NED/Commands/NedTranslationHelper.cs` with static helper methods
for the DTO → NED struct translation. Look at `ExConSubsystem.cs` for how ExCon currently
populates NED request structs from UI data to understand what fields need mapping.

### Step 1d: Wire NedNetworkFactory.CreateCommandGateway()

Replace `NullCommandGateway` stub:

```csharp
public ICommandGateway CreateCommandGateway()
{
    if (_participant == null) return new NullCommandGateway();
    return new NedCommandGateway(_participant, localNodeId: _localNodeId);
}
```

### Step 1e: Refactor ExConLogic to use IExConEgressWriters and ICommandGateway

In `Hrot.ExCon/ExConLogic.cs`:
- Replace the four `IDdsWriter<NedType>` fields with a single `IExConEgressWriters _egressWriters`
- Add `ICommandGateway _commandGateway` field
- Update the constructor to take `IExConEgressWriters egressWriters` and `ICommandGateway commandGateway`
- Replace all calls to individual writers with calls to `_egressWriters.WriteXxx(...)`
- Replace `_missionEgressTranslator` usage with `_commandGateway.SendMissionControlRequestAsync(...)`
- Replace `INedCommandGateway` usage with `ICommandGateway`
- The `MissionControlAckIngressTranslator` can either be removed (if ACK is handled by
  `ICommandGateway.SendMissionControlRequestAsync` returning the result) or moved to
  `NedNetworkFactory.CreateExConIngressHandlers()` — add this method to `INetworkFactory` if needed.
  Check how the ACK is consumed in ExConLogic and design accordingly.
- Remove all `using Hrot.NED.*` imports from ExConLogic

### Step 1f: Update Hrot.ExCon.csproj

Remove the `<ProjectReference Include="..\Hrot.Network.NED\Hrot.Network.NED.csproj" />` line.

Verify: `dotnet list Hrot.ExCon\Hrot.ExCon.csproj reference` must NOT show Hrot.Network.NED.

### Step 1g: Update ExConSubsystem in Hrot.ClusterRunner

`Hrot.ClusterRunner/Services/ExConSubsystem.cs` creates `DdsWriter<NedType>` instances
and passes them to ExConLogic. After ExConLogic is refactored:
1. Remove the individual DDS writer declarations in ExConSubsystem
2. Create a `NedNetworkFactory` instance and call `factory.CreateExConEgressWriters()`
   and `factory.CreateCommandGateway()`, then pass those to ExConLogic constructor

Note: ExConSubsystem still creates its own `DdsParticipant` — that is acceptable for now
(participant consolidation is TASK-P5-003). The key constraint is that ExConLogic itself
is decoupled.

### Step 1h: Update Hrot.ExCon.Tests

Verify all tests in `Hrot.ExCon.Tests` pass. Tests that construct `ExConLogic` directly
need to pass mock implementations of `IExConEgressWriters` and `ICommandGateway`.
Look for the existing test construction patterns and update them to use the new interface.

---

## Task 2: TASK-P4-002 — Decouple SimHost from NED

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-p4-002-decouple-simhost-from-ned)

Find which files in `Hrot.SimHost/` reference `Hrot.NED` or `Hrot.Network.NED`:

```powershell
Select-String -Path "Hrot.SimHost\**\*.cs" -Pattern "using Hrot\.NED|using Hrot\.Network" -Recurse
```

For each NED reference found, determine how to replace it with INetworkFactory or a neutral type.

Key changes:
- `NodeBootstrapper` or `SimHostApp`: if it constructs `NedReplicationModule` directly,
  refactor to accept `INetworkFactory` and call `factory.CreateReplicationModule()` instead.
- `HrotNodeBuilderReplicationExtensions.WithReplication` in `Hrot.Network.NED/Infrastructure/`:
  This extension currently lives in the NED project and is called by SimHost during init.
  Move it to a "neutral" extension that accepts `INetworkFactory` instead of constructing `NedReplicationModule`.
  Rename it to `WithReplicationModule(INetworkFactory factory)` or similar.
  Update callers accordingly.
- After removing NED references from SimHostApp, remove the ProjectReference in `Hrot.SimHost.csproj`.

Verify: `dotnet list Hrot.SimHost\Hrot.SimHost.csproj reference` must NOT show Hrot.Network.NED
or old Hrot.NED.

Run `dotnet test Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj --no-build` and fix any failures.

---

## Task 3: TASK-P4-003 — Decouple IG and CGF from NED

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-p4-003-decouple-ig-and-cgf-from-ned)

### IG decoupling

In `Hrot.IG/IgApplication.cs`:
- Replace `INedCommandGateway _commandGatewayInterface` with `ICommandGateway _commandGateway`
- Replace `TestHook_SetCommandGateway(INedCommandGateway gw)` with `TestHook_SetCommandGateway(ICommandGateway gw)`
- All calls to `_commandGatewayInterface.CreateEntityAsync(nedRequest, ...)` should become
  calls to `ICommandGateway.CreateEntityAsync(neutralCmd, ...)`
- The `EntityInfo` NED type registered as ECS component (causing UniqueNameGeneratorTests failure):
  Find the registration call in IG source and either remove it (if not needed) or replace with
  a neutral component type. After this fix, `UniqueNameGeneratorTests` must pass.
- Remove `Hrot.Network.NED` from `Hrot.IG.csproj`.

### CGF decoupling

In `Hrot.CGF/CgfApplication.cs` and related files:
- Find all `using Hrot.NED.*` and `using Hrot.Network.*` directives
- CGF may be using NED for `NodeOpCommand` / orchestration — but those types now live in
  `Hrot.Network.Orchestration`, so ensure references point to that assembly instead.
- For simulation-data schemas (entity replication etc.), replace with INetworkFactory injection.
- Remove `Hrot.Network.NED` from `Hrot.CGF.csproj`.

Also check `Hrot.Orchestrator.csproj` — it may reference `Hrot.Network.NED` for cluster
management types. Those should reference `Hrot.Network.Orchestration` instead.

Run `dotnet test Hrot.IG.Tests\Hrot.IG.Tests.csproj --no-build` — all 7 currently-failing
tests must now pass (they will once EntityInfo registration is fixed).

---

## Final Validation

After completing all tasks:

```powershell
# Verify zero NED references in subsystem assemblies
Select-String -Path "Hrot.ExCon\*.csproj","Hrot.SimHost\*.csproj","Hrot.IG\*.csproj","Hrot.CGF\*.csproj" -Pattern "Network.NED|Hrot.NED"

# Build
dotnet build IOS-IG-SimHost.sln --no-incremental -v quiet

# Run ALL tests (unit tests only - integration tests are acceptable to skip for this batch)
dotnet test IOS-IG-SimHost.sln --no-build
```

**All tests (unit tests) must pass with zero failures before writing the report.**
For integration tests that were already failing before this batch (in `Hrot.ClusterRunner.Integration.Tests`),
leave them to BATCH-09/10 unless they are directly caused by your changes.

---

## Testing Requirements

- All existing `Hrot.ExCon.Tests` pass — no test logic removed (only constructor signatures updated to use neutral mocks)
- All `Hrot.SimHost.Tests` pass — includes fixing the routing guard test failures
- All `Hrot.IG.Tests` pass — includes fixing the EntityInfo registration failure
- `Fdp.Engine.Tests` passes with zero TimeConfig/SlaveSyncController/ReplayModule failures
- Build succeeds with zero errors

---

## Report Requirements

Submit `.dev/modular-2/reports/BATCH-08-REPORT.md` including:

**Q1:** What issues did you encounter during the ExCon decoupling? How did you translate neutral
DTOs to NED wire types — what challenges arose in the mapping logic?

**Q2:** What changes were needed in the test code to fix the routing guard failures in
`Hrot.SimHost.Tests`? Was the routing guard logic itself correct, or did it need adjustment?

**Q3:** Were there any unexpected NED dependencies in IG or CGF beyond what was documented?

**Q4:** What design decisions did you make that were not fully specified in these instructions?
What alternatives did you consider?

**Q5:** What weak points remain in the codebase after this decoupling work?

**Q6 (suggested commit message):** What was accomplished in this batch?

---

## Reference Materials
- **Task Definitions:** `.dev/modular-2/TASK-DETAIL.md` — TASK-P4-001, TASK-P4-002, TASK-P4-003
- **Design:** `.dev/modular-2/DESIGN.md` — Phase 4 section
- **Debt Tracker:** `.dev/modular-2/DEBT-TRACKER.md` — DEBT-001, DEBT-005
- **Neutral interfaces:** `Hrot.Core/Network/*.cs`
- **Factory:** `Hrot.Network.NED/Factory/NedNetworkFactory.cs`
- **BDC reference:** `Hrot.Network.BDC/Factory/BdcNetworkFactory.cs` (example of a second factory implementation)
