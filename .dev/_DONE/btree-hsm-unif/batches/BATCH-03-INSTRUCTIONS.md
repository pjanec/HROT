# BATCH-03: BHU-017 — End-to-End Integration Tests

**Batch Number:** BATCH-03
**Tasks:** BHU-017 only
**Phase:** Integration
**Priority:** HIGH
**Dependencies:** All of BATCH-01 and BATCH-02 complete and committed

---

## Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Task Detail (BHU-017):** `.dev/btree-hsm-unif/TASK-DETAIL.md` — section "BHU-017" and all sub-sections (Groups A through E). Read it all before writing any code.
2. **Dev skill:** `.github/skills/developer/SKILL.md`

### Test projects involved

- `FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj` — Groups A + B tests go in `Behavior/Integration/BhuIntegrationTests.cs`
- `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/Fhsm.Tests.csproj` — Groups C + D go in `Integration/HsmSourceGenIntegrationTests.cs` and `Integration/HsmTerminalStateIntegrationTests.cs`
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/` — Group E goes in `HsmBehaviorIntegrationTests.cs`

### Verify first

Before writing any test, run the full build to confirm your baseline:
```
dotnet build IOS-IG-SimHost.sln --no-restore -v quiet
dotnet test FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/Fhsm.Tests.csproj --no-build --verbosity quiet
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build --verbosity quiet
```

Expected baseline: Fhsm.Tests 251/251, Toolkits.Tests passes (13 known pre-existing failures allowed).

### Report Submission

Write report to: `.dev/btree-hsm-unif/reports/BATCH-03-REPORT.md`

---

## MANDATORY WORKFLOW

Work through test groups in order: A → B → C → D → E. After each group:
1. Build the project
2. Run its test suite
3. Fix all failures before moving on

**Do NOT stop to ask permission.** If a build fails, fix it. If a test fails, find the root cause and fix it. Only write the report when ALL groups pass.

---

## Context

These integration tests prove that all features from Phases 1–5 work correctly end-to-end, not just in isolation. They are read-only additions — you add test files only. Do NOT modify any production code unless a bug is discovered (in which case fix it, document it in the report).

---

## Test Groups

### Group A — HSM Terminal State Routing
**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/Integration/BhuIntegrationTests.cs`

Four tests: IT-BHU-A1, IT-BHU-A2, IT-BHU-A3, IT-BHU-A4.

Full specs in TASK-DETAIL.md "Group A" section. Key points:

- Use `TestWorldFactory.Create()` to build `EntityRepository`.
- Build a 3-state HSM blob using `HsmBuilder + HsmCompiler.Compile()`:
  `"Idle" --EventX(id=10)--> "Active" --EventY(id=20)--> "Done"(IsFinal=true)`
- **IT-BHU-A1:** Drive from Idle → Done in one tick (inject EventX+EventY before tick). Assert `BehaviorFinishedEvent` published, `Terminated` bit CLEAR, `Phase == Idle` after tick.
- **IT-BHU-A2:** Second tick same entity. Assert NO new `BehaviorFinishedEvent` (dedup by InstanceId).
- **IT-BHU-A3:** Reassign behavior (new InstanceId). Drive to final again. Assert event published with new InstanceId; `ActiveLeafIds == 0xFFFF` before first tick of new behavior (proves BHU-016 reset).
- **IT-BHU-A4:** Same as IT-BHU-A1 but using `BrainHsm64` and `HsmTickSystem<BrainHsm64>`.

**Notes:**
- Find `BehaviorFinishedEvent` definition before writing — search for it in the codebase.
- Find `InstanceFlags.Terminated` and `InstancePhase.Idle` — they are in `Fhsm.Kernel`.
- Find `HsmEventQueue.TryEnqueue` and the event ID constants before writing.
- `HsmTickSystem<T>.Update()` takes `EntityRepository world, float dt`.

### Group B — Cognitive Interrupt Decoupling
**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/Integration/BhuIntegrationTests.cs` (same file, continuation)

Three tests: IT-BHU-B1, IT-BHU-B2, IT-BHU-B3.

Full specs in TASK-DETAIL.md "Group B" section. Key points:

- Run `CognitiveInterruptSystem`, `HsmTickSystem<BrainHsm128>`, `CognitiveCleanupSystem` in that order.
- Build an HSM blob where `EventId_MobilityLost` (id=1) causes `"Patrol" → "Stopped"`.
- **IT-BHU-B1:** Set `CanMove=false` (edge), run 3 systems, assert `bb.Memory[126] == 0` after cleanup, HSM is in "Stopped".
- **IT-BHU-B2:** Second frame, `CanMove` still false (no edge). Assert `bb.Memory[126] == 0` throughout, HSM stays in "Stopped".
- **IT-BHU-B3:** BTree entity (no HSM), force `bb.Memory[126] = 1`, run `CognitiveCleanupSystem`. Assert `bb.Memory[126] == 0`.

**Notes:**
- `ActorCapabilityState` — find actual struct name and field in codebase before writing.
- `CognitiveInterruptSystem.InterruptRegister_MobilityLost == 126` (constant defined there).
- Verify how to read `ActiveLeafIds` to confirm HSM state — may need `unsafe` block.

### Group C — SharedAi Cross-Paradigm Adapter
**File:** `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/Integration/HsmSourceGenIntegrationTests.cs`

Three tests: IT-BHU-C1, IT-BHU-C2, IT-BHU-C3.

Full specs in TASK-DETAIL.md "Group C" section. Key points:

- Define a local `[StructLayout(LayoutKind.Sequential)] private struct TestDto { public int Value; }` with `Value` at offset 0.
- Annotate a static method `IsValuePositive(ref int value, Entity self, EntityRepository repo)` with `[SharedAiCondition(typeof(TestDto), nameof(TestDto.Value))]`.
- **IT-BHU-C1:** BTree adapter. Call `RegisterAll()`, retrieve condition via `TryGetCondition("IsValuePositive@0")`. Invoke with positive memory → returns `NodeStatus.Success`. Negative → `NodeStatus.Failure`.
- **IT-BHU-C2:** HSM guard adapter. `ClearAll()` + `RegisterAll()`. Construct `HsmKernelBridge` with GCHandle for `EntityRepository`. Write 5 to `bb.Memory[0..3]`. Evaluate guard → returns true.
- **IT-BHU-C3:** Hash cross-check. Assert `FbtComputeHash("IsValuePositive@0") == FhsmComputeHash("IsValuePositive@0")`.

**Notes:**
- Since `Fhsm.Tests` does NOT reference `Fdp.Toolkit.*`, IT-BHU-C2 must be careful — it needs a minimal live `EntityRepository`. Check what's available in `Fhsm.Tests` (it may not have one). If not possible, adapt: create a minimal `EntityRepository` using `Fdp.Core` directly (it IS referenced by `Fhsm.Tests`? check the csproj). If no entity repo is available, document this limitation and test the thunk logic with a mock bridge that doesn't call back into ECS.
- For IT-BHU-C3, the `ComputeHash` function in both generators is private — you need to either expose it via `InternalsVisibleTo` or replicate the computation in the test and assert the expected known value.

### Group D — ClearAll / Hot-Reload Round-Trip
**File:** `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/Integration/HsmTerminalStateIntegrationTests.cs`

Three tests: IT-BHU-D1, IT-BHU-D2, IT-BHU-D3.

Full specs in TASK-DETAIL.md "Group D" section. Key points:

- **IT-BHU-D1:** Register a guard at id=1234, call `ClearAll()`. Assert `EvaluateGuard(1234, ...)` returns the default "no guard = always true" value.
- **IT-BHU-D2:** `ClearAll()` then `RegisterAll()`. Evaluate a known registered guard — it correctly dispatches (not default).
- **IT-BHU-D3:** `ClearAll()` → `RegisterAll()` → build blob with final state → advance to final → assert `InstanceFlags.Terminated` is set.

**Notes:**
- Look at existing tests in `Fhsm.Tests/Kernel/` and `Fhsm.Tests/Compiler/` for patterns on how to build blobs and run `HsmKernel.Update()`.
- `HsmActionDispatcher.RegisterAll()` re-populates; the test for D2 needs a built-in registered guard to verify — use any guard from the existing test fixtures.
- **IMPORTANT:** D2 and D3 call `RegisterAll()` which mutates shared static state in `HsmActionDispatcher`. Run these tests with `[Collection("HsmDispatcherSerial")]` or equivalent isolation so they don't race with other tests that rely on the static tables. Study how existing `Fhsm.Tests` handle this isolation.

### Group E — Full CognitiveRuntimeModule Frame
**File:** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/HsmBehaviorIntegrationTests.cs`

Two tests: IT-BHU-E1, IT-BHU-E2.

Full specs in TASK-DETAIL.md "Group E" section. Key points:

- **IT-BHU-E1:** Construct `CognitiveRuntimeModule`. Assert exact system types at exact indices 0–5. No `HsmDamageBridgeSystem` anywhere. Total count == 6.
- **IT-BHU-E2:** Full frame integration test — build a 3-state HSM behavior, entity with all components, run all 6 systems for 2 frames. Assert HSM transitions correctly in Frame 1 (mobility-lost), reaches final state in Frame 2, `BehaviorFinishedEvent` published.

**Notes:**
- Look at existing `Hrot.ClusterRunner.Integration.Tests` for patterns on how to construct `CognitiveRuntimeModule` and drive it.
- Find how to get the system list from `CognitiveRuntimeModule` — look at how `CognitiveRuntimeModuleTests.cs` does it (already exists in `Fdp.Toolkits.Tests`).
- If `CognitiveRuntimeModule` doesn't expose its system list directly, use reflection to get the internal list, or check if the test already has a pattern for this.

---

## Quality Standards

- All tests must use real runtime behavior, not just source-text assertions.
- Each test is one `[Fact]` in xunit.
- No test should mutate shared static state without cleanup.
- Tests using `HsmActionDispatcher` static state must either clear it before/after or run in isolation.
- The 13 pre-existing failures in `Fdp.Toolkits.Tests` (`CombatComponentTests`, etc.) are allowed — do not fix them in this batch.

---

## Success Criteria

- [ ] IT-BHU-A1 through A4 pass (4 tests)
- [ ] IT-BHU-B1 through B3 pass (3 tests)
- [ ] IT-BHU-C1 through C3 pass (3 tests)
- [ ] IT-BHU-D1 through D3 pass (3 tests)
- [ ] IT-BHU-E1 and E2 pass (2 tests) — or E1 alone if E2 requires infrastructure not available in ClusterRunner.Integration.Tests
- [ ] Zero regressions in any existing passing test
- [ ] `dotnet build IOS-IG-SimHost.sln --no-restore -v quiet` — zero `error CS` lines

---

## Reference Materials

- **Full test specs:** `.dev/btree-hsm-unif/TASK-DETAIL.md` — "Integration Tests" section through the end of the file
- **Existing test patterns:** `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/` (all existing test files)
- **Existing Fhsm.Tests patterns:** `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/` (Kernel/, Compiler/, SourceGen/)
- **TestWorldFactory:** `FDP/Toolkits/Fdp.Toolkits.Tests/` (find TestWorldFactory.Create())
- **BehaviorFinishedEvent:** Search for it in `Fdp.Toolkits` namespace
- **CognitiveRuntimeModuleTests.cs:** Already passes, shows how to check system types/count
