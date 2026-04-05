# BATCH-04: ACL Backdoor Elimination (Phase 3) + NetworkGateway Integration Test (N004)

**Batch Number:** BATCH-04  
**Tasks:** PACK3-A001, PACK3-A002, PACK3-A003, PACK3-A004, PACK3-A005, PACK3-N004  
**Phase:** Phase 3 (ACL Backdoor Elimination), Phase 4 completion (N004)  
**Estimated Effort:** 14–18 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (CgfComponentRegistry, NetworkGatewaySystem canonical), BATCH-02, BATCH-03

---

## 📋 Onboarding & Workflow

### Developer Instructions

This is the **final batch** of `packs-3`. When this batch is complete, all tasks in
`TASK-TRACKER.md` will be marked done.

**What remains:**
1. **Phase 3 — ACL Backdoor Elimination (A001–A005):** Remove the hidden `tryGetPrebuilt`
   delegate that allows `MapCommandController` to bypass the FDP event bus. Fix `AreaAuthoringTool`
   (and `RouteAuthoringTool`) to build `SpawnEntityCommand.InitialComponents` correctly.
   Prove the clean ACL path with three verification tests.
2. **Phase 4 completion — N004:** Add the `NetworkGatewaySystem` integration test (SimHost + IG,
   `AllPeers` handshake, `EntityLifecycle.Active`). (N001-N003 were already done in BATCH-01.)

**Critical Phase 3 rule from DESIGN.md §3.A:**  
> Phase 3.E (tool `InitialComponents` fix) must be done **together** with 3.B and 3.C, since
> closing the backdoor without fixing the tools would break area authoring.

Do all of A001, A002, A003, A004 before running any tests — the system must compile and work
end-to-end before running the ACL verification tests (A005).

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Onboarding:** `.dev/packs-3/ONBOARDING.md`
3. **Design:** `.dev/packs-3/DESIGN.md` — read §Phase 3 (§3.A–§3.F) and §Phase 4 (§4.D) carefully
4. **Task Definitions:** `.dev/packs-3/TASK-DETAIL.md` — PACK3-A001–A005, PACK3-N004
5. **Previous Reviews:** `.dev/packs-3/reviews/BATCH-01-REVIEW.md` (N001-N003 context)
6. **BATCH-02 review:** `.dev/packs-3/reviews/BATCH-02-REVIEW.md` (BallisticsSystem order bug context)

### Source Code Locations
- **SpawnEntityCommandEgressTranslator:** `Hrot.Map.Common/Replication/Egress/SpawnEntityCommandEgressTranslator.cs`
- **MapCommandController:** `Hrot.IG/Systems/MapCommandController.cs` (or nearby)
- **IgApplication:** `Hrot.IG/IgApplication.cs`
- **AreaAuthoringTool:** `Hrot.IG/Tools/AreaAuthoringTool.cs`
- **RouteAuthoringTool:** `Hrot.IG/Tools/RouteAuthoringTool.cs` (if applicable)
- **Map Common Tests:** `Hrot.Map.Common.Tests/`
- **ACL Backdoor Elimination Tests:** `Hrot.ClusterRunner.Integration.Tests/AclBackdoorEliminationTests.cs` (NEW)
- **Egress Translator Tests:** `Hrot.Map.Common.Tests/SpawnEntityCommandEgressTranslatorTests.cs` (NEW or extend if exists)
- **NetworkGatewayIntegrationTests:** `Hrot.ClusterRunner.Integration.Tests/NetworkGatewayIntegrationTests.cs` (NEW)
- **HrotRunnerHarness:** `Hrot.ClusterRunner.Integration.Tests/HrotRunnerHarness.cs`
- **RunMode:** Look for it near HrotRunnerHarness

### Report Submission
**When done, submit your report to:**  
`.dev/packs-3/reports/BATCH-04-REPORT.md`

**If you have questions, create:**  
`.dev/packs-3/questions/BATCH-04-QUESTIONS.md`

---

## Context

The `tryGetPrebuilt` backdoor was left from an earlier iteration where the egress translator
couldn't synthesise `CreateEntityRequest` from a `SpawnEntityCommand` with geometry components.
The packs-2 ACL mandate required this to be fixed, but the work was deferred. Now it must be
fully eliminated:

```
BEFORE (backdoor):
  AreaAuthoringTool → MapCommandController._prebuiltRequests[requestId] = preBuiltDDS
  AreaAuthoringTool → SpawnEntityCommand (minimal, no geometry) → Bus
  SpawnEntityCommandEgressTranslator._tryGetPrebuilt(requestId) → skip BuildCreateEntityRequest
  → use pre-built DDS directly (ACL violation!)

AFTER (clean):
  AreaAuthoringTool → SpawnEntityCommand { InitialComponents = [EditablePolyline, MapOverlayStyle] }
  SpawnEntityCommandEgressTranslator.BuildCreateEntityRequest → extract geometry → DDS
  No delegate. No pre-built cache.
```

---

## 🎯 Batch Objectives

1. Remove `_tryGetPrebuilt` delegate and `_prebuiltRequests` cache completely from the codebase.
2. Fix `AreaAuthoringTool` / `RouteAuthoringTool` to emit `SpawnEntityCommand.InitialComponents` with geometry domain objects.
3. Extend `BuildCreateEntityRequest` to translate geometry types to DDS descriptors.
4. Prove the clean path with 3 tests: translator unit test, E2E area authoring test, offline editor isolation test.
5. Add `NetworkGatewaySystem` integration test proving `AllPeers` handshake → `EntityLifecycle.Active`.

---

## ✅ Tasks

### Task 1: Purge `tryGetPrebuilt` from `SpawnEntityCommandEgressTranslator` (PACK3-A001)

**File:** `Hrot.Map.Common/Replication/Egress/SpawnEntityCommandEgressTranslator.cs` — **UPDATE**

**Task Definition:** See [TASK-DETAIL.md — PACK3-A001](../TASK-DETAIL.md#pack3-a001--purge-trygetprebuilt-from-spawnentitycommand-egresstranslator)

**Key Requirements:**
- Delete `_tryGetPrebuilt` field (type: `Func<Guid, CreateEntityRequest?>`).
- Delete the constructor overload that accepts the delegate.
- In `PollIngress`, remove the bypass conditional block (`if (_tryGetPrebuilt != null) { ... }`).
- The standard `BuildCreateEntityRequest(spawnCmd)` path now handles ALL commands.
- If `BuildCreateEntityRequest` doesn't yet handle geometry types (`EditablePolyline`, `MapOverlayStyle`, `RoutePlan`) you will extend it in A004.

**Do NOT break area authoring compilation yet** — A004 (fixing the tools) must happen in the
same build pass before the tests run.

---

### Task 2: Remove DTO Cache from `MapCommandController` (PACK3-A002)

**File:** `Hrot.IG/Systems/MapCommandController.cs` — **UPDATE**

**Task Definition:** See [TASK-DETAIL.md — PACK3-A002](../TASK-DETAIL.md#pack3-a002--remove-dto-cache-from-mapcommandcontroller)

**Key Requirements:**
- Delete `_prebuiltRequests` dictionary.
- Delete `TryDequeuePrebuilt(Guid requestId, out CreateEntityRequest)` method.
- Simplify `OnAreaEntityCreated` to accept only `SpawnEntityCommand cmd` and call `_eventBus.PublishManaged(cmd)` directly (no pre-built cache lookup).

---

### Task 3: `IgApplication` Composition Root Cleanup (PACK3-A003)

**File:** `Hrot.IG/IgApplication.cs` — **UPDATE**

**Task Definition:** See [TASK-DETAIL.md — PACK3-A003](../TASK-DETAIL.md#pack3-a003--igapplication-composition-root-cleanup)

**Key Requirements:**
- Remove the `MapCommandController? mapCmdCtrlRef = null;` local variable.
- Remove the lambda expression that captures it.
- Construct `SpawnEntityCommandEgressTranslator` using the single clean constructor (participant, bus, geoTransform).

---

### Task 4: Fix `AreaAuthoringTool` (and `RouteAuthoringTool`) to use `InitialComponents` (PACK3-A004)

**Files:**
- `Hrot.IG/Tools/AreaAuthoringTool.cs` — **UPDATE**
- `Hrot.IG/Tools/RouteAuthoringTool.cs` — **UPDATE** (if applicable, check if it uses the same pattern)
- `Hrot.Map.Common/Replication/Egress/SpawnEntityCommandEgressTranslator.cs` — **EXTEND** `BuildCreateEntityRequest` for geometry types

**Task Definition:** See [TASK-DETAIL.md — PACK3-A004](../TASK-DETAIL.md#pack3-a004--fix-areaauthoringtool-to-use-initialcomponents)

**Key Requirements:**
- Refactor each tool to construct a `SpawnEntityCommand` with `InitialComponents` list carrying:
  - `EditablePolyline { Points = ... }` for area polygons.
  - `MapOverlayStyle { ... }` for visual appearance.
  - Route equivalent types for `RouteAuthoringTool`.
- Extend `SpawnEntityCommandEgressTranslator.BuildCreateEntityRequest` to:
  - If `InitialComponents` contains `EditablePolyline` → populate DDS descriptor `dtMapVisualOverlay` with `Points`.
  - If `InitialComponents` contains `RoutePlan` → populate DDS descriptor `dtMapRoute`.
  - The extension must fit the existing `BuildCreateEntityRequest` signature.

---

### Task 5: ACL Verification Tests (PACK3-A005)

**Files:**
- `Hrot.Map.Common.Tests/SpawnEntityCommandEgressTranslatorTests.cs` — **NEW or EXTEND**
- `Hrot.ClusterRunner.Integration.Tests/AclBackdoorEliminationTests.cs` — **NEW** (Tests 2 and 3)

**Task Definition:** See [TASK-DETAIL.md — PACK3-A005](../TASK-DETAIL.md#pack3-a005--acl-verification-tests)

**Test 1 — Boundary unit test** (in `Hrot.Map.Common.Tests`):
- `EgressTranslator_SynthesizesDdsPayload_StrictlyFromDomainEvent`
- Mock/recording DDS writer.
- Instantiate `SpawnEntityCommandEgressTranslator(mockWriter, bus, geoTransform)` — **no delegate**.
- Publish `SpawnEntityCommand { TkbType = 1001, InitialComponents = [new EditablePolyline { Points = [(10,10)] }, new MapOverlayStyle { FillR = 255 }] }`.
- Assert mock writer called once.
- Assert published `CreateEntityRequest` contains descriptor `d._d == EDescriptorType.dtMapVisualOverlay` with `Points.Count == 1` (or equivalent assertion for the geometry payload).

**Test 2 — E2E area authoring** (in `Hrot.ClusterRunner.Integration.Tests`):
- `AreaAuthoring_EndToEnd_NoBackdoor_PublishesCorrectCreateEntityRequest`
- `HrotRunnerHarness(RunMode.SimHost | RunMode.IG, domainId)`.
- Simulate area placement tool activation (look for `TestHook_SimulateMapClick` or equivalent).
- Assert exactly 1 `CreateEntityRequest` received with geometry payload via an independent `DdsReader<CreateEntityRequest>`.
- Assert `MapCommandController._prebuiltRequests` does **not** exist (compile-time proof).

**Test 3 — Offline editor isolation** (in `Hrot.ClusterRunner.Integration.Tests`):
- `SpawnCommand_OfflineEditor_NoNetworkCallsMade`
- `EditorHarness` (no DDS translator packs).
- Publish `SpawnEntityCommand`.
- Assert `repo.EntityCount == 1`.
- Assert mock DDS writer call count == 0.

---

### Task 6: `NetworkGatewaySystem` Integration Test (PACK3-N004)

**File:** `Hrot.ClusterRunner.Integration.Tests/NetworkGatewayIntegrationTests.cs` — **NEW**

**Task Definition:** See [TASK-DETAIL.md — PACK3-N004](../TASK-DETAIL.md#pack3-n004--networkgatewaysystem-integration-test)

**Key Requirements:**
- `[Collection("LogCapture")]`
- Allocate unique `domainId` (thread-safe counter, starting at 350).
- `HrotRunnerHarness(RunMode.SimHost | RunMode.IG, domainId)`.
- Publish `SpawnEntityCommand { TkbType = ..., InitType = ReliableInitType.AllPeers, InitialComponents = [...] }` on SimHost bus.
- `PumpUntil` SimHost `NetworkEntityMap` contains the entity (timeout: 60 frames).
- `PumpUntil` SimHost entity reaches `EntityLifecycle.Active` (timeout: 150 frames).
- `PumpUntil` IG entity reaches `EntityLifecycle.Active` (timeout: 150 frames).
- `Assert.True(success)` for both.
- If you cannot wire the full `AllPeers` handshake through CycloneDDS in a reasonable amount of investigation time, write a note in your report documenting what you understood and what the blocker is. Do NOT fabricate a passing test.

---

## 🧪 Testing Requirements

### Minimum Test Coverage

| Task | Test Type | Minimum |
|------|-----------|---------|
| A001–A004 | Compile check | Solution builds clean after all four changes |
| A005 Test 1 | Unit (boundary) | Translator produces correct DDS payload from InitialComponents |
| A005 Test 2 | Integration (E2E) | 1 `CreateEntityRequest` with geometry via independent DDS reader |
| A005 Test 3 | Integration (offline) | EntityCount == 1, DDS writer not called |
| N004 | Integration (SimHost+IG) | Both entities reach Active |

### Critical Sequencing

Do NOT run the ACL tests before completing A001–A004. The backdoor removal without the tool fix
(A004) will break the existing area authoring pipeline. Complete all four of A001–A004 first,
verify the solution builds, then run A005 tests.

### Test-Driven Task Progression

**MANDATORY WORKFLOW — Test-Driven Task Progression:**

> For each task, before writing production code:
> 1. Read the existing tests to understand what currently passes.
> 2. Write the failing test(s) first (unit or integration as specified).
> 3. Implement the production code to make the tests pass.
> 4. Run the full relevant test suite to confirm no regressions.
> 5. Only then mark the task done in your report.
>
> **Never consider a task complete until all its tests pass AND existing tests remain green.**

**Exception for A001–A004:** These four tasks form a single atomic change. Write the failing
tests for A005 first, then implement A001–A004 together until the tests pass and the solution
builds clean.

Run regressions before submitting:
```
dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln --no-incremental
dotnet test Hrot.ClusterRunner.Integration.Tests --filter "AclBackdoor|NetworkGate|OfflineEditor|AreaAuthoring" --no-build
dotnet test Hrot.Map.Common.Tests --filter "EgressTranslator" --no-build
```

---

## 📊 Report Requirements

Submit your report to `.dev/packs-3/reports/BATCH-04-REPORT.md`.

```markdown
# BATCH-04 Report

## Implementation Summary
[Per-task: what was done]

## Tests Added
[List new test methods and files]

## Test Results
[Pass/fail counts, any skips]

## Developer Insights
1. **Issues Encountered:** What problems did you hit? How resolved?
2. **Weak Points Spotted:** Fragile or unclear areas.
3. **Design Decisions Beyond the Spec:** Any choices not explicitly stated?

## Deviations from Spec (if any)
[List with justification]
```

---

## ⚠️ Important Notes

1. **Areas A001–A004 are atomic** — close the backdoor AND fix the tools in the same
   compilation pass. Closing A001-A003 alone without A004 will break area authoring.
2. **`SpawnEntityCommand.InitialComponents`**: Check if this property already exists on
   `SpawnEntityCommand` (it was delivered in packs-2). If it exists, use it. If not, add it.
3. **DDS writer mock for A005 Test 1**: Look in existing test infrastructure for a `RecordingDdsWriter`
   or similar test double. If one doesn't exist, create a minimal one that counts `Write` calls
   and records the last payload.
4. **N004 `AllPeers` handshake**: This test requires CycloneDDS loopback to be functional.
   If your test environment doesn't support DDS, write the test as a placeholder with a
   `Skip("Requires DDS loopback")` attribute and document the limitation clearly.
5. **FDP submodule**: Changes for A001-A004 are in the parent repo (Hrot.IG, Hrot.Map.Common).
   No FDP/ changes should be needed for this batch.
6. This is the last batch — once all tests pass, the `packs-3` workstream is complete.
