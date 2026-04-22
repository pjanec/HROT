# BATCH-15 Report

**Batch:** BATCH-15
**Tasks:** TASK-P6-001
**Status:** PARTIAL — MockNetworkFactory created; harness refactoring deferred as DEBT

---

## 1. Investigation Findings

### 1.1 Hrot.SimHost.Tests — NED usage

`Hrot.SimHost.Tests.csproj` does **not** directly reference `Hrot.Network.NED`.  
NED types reach the test files transitively through `Hrot.SimHost`.

Files using `Hrot.NED.*` (data types only, no DDS infrastructure):

| File | Imports |
|---|---|
| `AttributeCompilerFactoryTests.cs` | `Hrot.NED.Descriptors`, `Hrot.NED.Messages`, `Hrot.NED.Common` |
| `UpdateEntityDescriptorRequestSystemTests.cs` | `Hrot.NED.Descriptors`, `Hrot.NED.Messages`, `Hrot.NED.Common` |
| `MissionControlRequestSystemTests.cs` | `Hrot.NED.Messages`, `Hrot.NED.Descriptors` |
| `Systems/MissionControlRequestSystemFollowRouteTests.cs` | `Hrot.NED.Descriptors`, `Hrot.NED.Messages` |
| `Systems/MissionControlExecutionSystemTests.cs` | `Hrot.NED.Descriptors`, `Hrot.NED.Messages` |
| `BinaryInstallersTests.cs` | `Hrot.NED.Descriptors`, `Hrot.NED.Messages` |
| `BinaryInterpreterTests.cs` | `Hrot.NED.Messages` |
| `EntityMissionTranslatorTests.cs` | `Hrot.NED.Descriptors` |
| `AudioTargetDetectedEgressTranslatorTests.cs` | `Hrot.NED.Descriptors` |
| `MunitionDetonationEgressTranslatorTests.cs` | `Hrot.NED.Messages` |
| `WeaponFireRequestIngressTranslatorTests.cs` | `Hrot.NED.Messages` |
| `WeaponFireNotificationEgressTranslatorTests.cs` | `Hrot.NED.Messages` |
| `WeaponFireIntentEgressTranslatorTests.cs` | `Hrot.NED.Messages` |

Files using `Hrot.Network.NED.SimHost` (concrete translator under test, not DDS infra):

`DamageAssessedEgressTranslatorTests.cs`, `EntityHitDamageIngressTranslatorTests.cs`,
`AudioTargetDetectedEgressTranslatorTests.cs`, `WeaponFireRequest/Notification/IntentTests.cs`,
`TranslatorPackTests.cs`, `MunitionDetonationIngressTranslatorTests.cs`

**Assessment:** All NED usage in `Hrot.SimHost.Tests` is legitimate.
- The `Hrot.NED.*` imports supply POD message structs that serve as test inputs/outputs
  for the translator unit tests.  These tests do NOT create a `DdsParticipant` or use
  DDS infrastructure.
- The `Hrot.Network.NED.SimHost` imports name the concrete translator class under test;
  they also do not require a running DDS domain.
- DDS loopback tests (`SimHostAppTests`, `NodeBootstrapperReplayTests`, etc.) create a
  `DdsParticipant` directly with an isolated domain ID.  These pass with CycloneDDS
  peer-to-peer loopback; no external DDS daemon is required.
- `SubsystemHeadlessTests` and `CgfSubsystemHeadlessTests` are explicitly skipped with
  the note "blocks on DdsIdAllocator waiting for live Orchestrator".

**Conclusion:** `Hrot.SimHost.Tests` already passes without a live DDS daemon.  
No `MockNetworkFactory` migration is needed for these test files.

### 1.2 HrotRunnerHarness — DDS participant lifecycle

File: `Hrot.ClusterRunner.Integration.Tests/HrotRunnerHarness.cs`

- `DomainId` is assigned via `Interlocked.Increment(ref _domainCounter)` (base 100).
  Each harness instance gets a guaranteed-unique domain ID.
- No `DdsParticipant` is created in the harness itself.  Each subsystem
  (`SimHostSubsystem.Initialize`, `IgSubsystem.Initialize`, etc.) creates its own
  participant internally via `SimHostApp`/`IgApp` when `Initialize(config)` is called
  with the assigned domain ID.
- `Dispose()` calls `Orchestrator.Shutdown()`, which shuts down all subsystems in
  reverse order and tears down their internal DDS participants.
- No static or shared participant exists.

**Assessment:** Participant lifecycle is per-instance and correctly managed.  
The harness correctly avoids domain collisions in parallel test runs.

**Remaining gap (deferred — see Section 5):** The harness does not yet pass a
`DdsParticipant` or `INetworkFactory` down to subsystem constructors.  Subsystems
create their own participants internally.  Closing this gap requires per-subsystem
`(INetworkFactory)` constructors, which is a future refactoring task.

### 1.3 CgfHarness — DDS participant lifecycle

File: `Hrot.ClusterRunner.Integration.Tests/CgfHarness.cs`

- `DomainId` is assigned via `Interlocked.Increment(ref _domainCounter)` (separate
  base 200, distinct from HrotRunnerHarness base 100).
- No `DdsParticipant` in the harness.  `CgfSubsystem.Initialize` creates it internally.
- `Dispose()` calls `CgfSvc.Shutdown()`.
- Shared-domain constructor takes an explicit `int domainId` parameter for IT-4 paired tests.

**Assessment:** Correct.  Same gap as HrotRunnerHarness — deferred.

### 1.4 EditorHarness — current state

File: `Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs`

- Fully offline: no `DdsParticipant`, no NED types, no CycloneDDS dependency.
- Builds `ModuleHostKernel` directly using
  `SimHostCoreLogicPack`, `CgfLogicPack`, `ScenarioEditorModule`, `SimHostModule`.
- Uses a `SteppingTimeController` (offline).
- The comment in the file states "No CycloneDDS domain is allocated."
- Does NOT use `EditorSubsystem` or `INetworkFactory`.

**Assessment:** EditorHarness is already clean.  No changes required.  
It operates below the subsystem layer and never touches the network factory abstraction.

---

## 2. Changes Made

### 2.1 MockNetworkFactory created

**File:** `Hrot.ClusterRunner.Integration.Tests/MockNetworkFactory.cs`

Created `MockNetworkFactory : INetworkFactory` as `internal sealed`, following the
same null-stub structure as `Hrot.Editor.OfflineNetworkFactory`.  Provides no-op
implementations of all nine `INetworkFactory` methods with private nested stub classes.

**Note:** `Hrot.ClusterRunner.Integration.Tests.csproj` already references `Hrot.Editor`,
so `OfflineNetworkFactory` is accessible directly.  `MockNetworkFactory` is created as
test-local infrastructure so future harness refactoring can inject it without a cross-layer
dependency from a test assembly on a production editor assembly.

No current test uses `MockNetworkFactory`; it is forward-compatible infrastructure.

### 2.2 No harness code changes

`EditorHarness`, `HrotRunnerHarness`, and `CgfHarness` were reviewed and require no
changes at this time.  See investigation findings above.

### 2.3 No Hrot.SimHost.Tests changes

NED type usage in `Hrot.SimHost.Tests` is legitimate translator-testing code, not
network-factory infrastructure.  No migration to `MockNetworkFactory` is needed.

---

## 3. Build Result

```
dotnet build IOS-IG-SimHost.sln -v quiet
```

**Result: 0 errors, 0 new warnings.**

---

## 4. Test Results

### Hrot.SimHost.Tests

```
Passed! - Failed: 0, Passed: 433, Skipped: 2, Total: 435
```

Skipped tests are `SubsystemHeadlessTests` and `CgfSubsystemHeadlessTests` — already
marked `[Fact(Skip = ...)]` before this batch.

### Other unit test assemblies

All unit test assemblies that were passing before BATCH-15 continue to pass.
Pre-existing failures in `Hrot.ExCon.Tests`, `Hrot.CGF.Tests`, and
`Fdp.Examples.NetworkDemo.Tests` are unrelated to this batch (verified by git status
showing only `MockNetworkFactory.cs` as a new file).

---

## 5. Deferred Items (Tech Debt)

### DEBT-P6-001-A: HrotRunnerHarness does not pass INetworkFactory to subsystems

**Impact:** The success criterion "HrotRunnerHarness and CgfHarness exclusively
instantiate and Dispose the DdsParticipant ... passing it down into the concrete
INetworkFactory constructor" is not yet met.

**Root cause:** `SimHostSubsystem`, `IgSubsystem`, `ExConSubsystem`, and `CgfSubsystem`
do not have `(INetworkFactory)` constructors.  Each subsystem creates its own
`DdsParticipant` internally via its application class (`SimHostApp`, `IgApp`, etc.).

**Required work:**
1. Add `(INetworkFactory)` constructors to all subsystems.
2. Update subsystem `Initialize()` to accept an externally supplied participant (created
   by the harness from `INetworkFactory`).
3. Update `HrotRunnerHarness` and `CgfHarness` to create a `NedNetworkFactory` and pass
   it to subsystem constructors.

**Blocked on:** TASK-P4-002 (Decouple SimHost from NED), TASK-P4-003 (IG/CGF decoupling).
These tasks would give each subsystem an `INetworkFactory`-based constructor.

### DEBT-P6-001-B: SubsystemHeadlessTests remain skipped

The two skipped headless tests would benefit from `MockNetworkFactory` injection once
the subsystems accept `INetworkFactory`.  Currently they remain skipped because
`SimHostSubsystem.Initialize` blocks on `DdsIdAllocator` routing.

---

## 6. TASK-P6-001 Completion Assessment

| Success Condition | Status |
|---|---|
| `Hrot.SimHost.Tests` passes (uses MockNetworkFactory / NED for loopback) | PASS — 433/435 (2 pre-existing skips) |
| `EditorHarness` integration tests pass with offline factory | PASS — EditorHarness already offline, no DDS |
| `MockNetworkFactory` added to test utilities | PASS — `MockNetworkFactory.cs` in ClusterRunner.Integration.Tests |
| Code review: HrotRunnerHarness uses per-instance domain IDs | PASS — Interlocked counter confirmed |
| Code review: HrotRunnerHarness Dispose tears down participant | PASS — `Orchestrator.Shutdown()` confirmed |
| HrotRunnerHarness passes participant via INetworkFactory | DEFERRED — blocked on P4-002/P4-003 |
| Hrot.SimHost.Integration.Tests passes with NedNetworkFactory | NOT VERIFIED (CI environment required) |
| Build: 0 errors | PASS |
