# BATCH-02: Blueprint Phase 1 Test Harness — Mocks and Builders

**Batch Number:** BATCH-02
**Tasks:** TASK-TH-001 (+ TH-006 patches integrated), TASK-TH-002, TASK-TH-004, TASK-TH-007
**Phase:** Phase 1 — Test Harness (Part 1 of 3)
**Estimated Effort:** 12-15 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 (project skeleton + asset schema must be in place)

---

## Onboarding & Workflow

### Developer Instructions

This batch implements the foundational test mock classes and the `BlueprintAssetBuilder` fluent API.
These are self-contained pieces that do NOT depend on `BlueprintTestFixture` (TASK-TH-003, BATCH-03).

**Important:** TASK-TH-001 and TASK-TH-006 are implemented together.  The inline patches
from TH-006 must be pre-applied into the TH-001 implementation directly -- do NOT write
the original TH-001 version and then correct it.  The patched design is the target.

### Required Reading (IN ORDER)

1. **Test Harness DD:** `.dev/blueprints-1/Blueprint_Subsystem_Test_Harness_Detailed_Design.md`
   — Read §3 (MockSimulationView), §4 (MockEntityCommandBuffer), §6 (BlueprintAssetBuilder), §8 (Mock Contract Tests) in full.
2. **Test Harness Inline Patches:** `.dev/blueprints-1/Blueprint_Subsystem_Test_Harness_Detailed_Design_InlinePatches.md`
   — Read Patches 1 and 2 carefully.  These OVERRIDE §3 and §5 of the main DD.
   After reading, your MockSimulationView must NOT have `_eventStreamsByType` or `BeginTick`.
3. **Task Definitions:** `.dev/blueprints-1/TASK-DETAIL.md`
   — Read TASK-TH-001, TASK-TH-002, TASK-TH-004, TASK-TH-006, TASK-TH-007 in full.
4. **Engine interfaces (for mock implementation):**
   - `FDP/Engine/Fdp.Core/Abstractions/ISimulationView.cs` — full interface to implement
   - `FDP/Engine/Fdp.Core/Abstractions/IEntityCommandBuffer.cs` — full interface to implement
   - `FDP/Engine/Fdp.Core/EntityRepository.cs` — understand `Bus` property
   - `FDP/Engine/Fdp.Core/FdpEventBus.cs` — understand `Read<T>()`, `SwapBuffers()`, `Publish<T>()`
5. **Architecture v1.2 §5:** `.dev/blueprints-1/Blueprint_Subsystem_Architecture_v1.2.md`
   — §5 asset schema (already implemented in BATCH-01, needed for BlueprintAssetBuilder)
6. **Previous batch review:** `.dev/blueprints-1/reviews/BATCH-01-REVIEW.md`
7. **Developer workflow:** `.dev/.guides/DEV-GUIDE.md`

### Source Code Locations

- **All new code goes in:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`
  - `Mocks/MockSimulationView.cs`
  - `Mocks/MockEntityCommandBuffer.cs`
  - `Builders/BlueprintAssetBuilder.cs` (contains BlueprintAssetBuilder, GraphBuilder, NodeBuilder, SyntheticGuidHelper)
  - `Mocks/MockSimulationViewContractTests.cs`
  - `Mocks/MockEntityCommandBufferContractTests.cs`
  - `Mocks/MockContractTests.cs`
- **Test project:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj`
  — Add `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` if needed for EcbOp zero-byte checks.

### Build & Test Commands

```powershell
# From repo root:
dotnet build IOS-IG-SimHost.sln
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj
```

### Report Submission

**When done, submit your report to:**
`.dev/blueprints-1/reports/BATCH-02-REPORT.md`

**If you have questions, create:**
`.dev/blueprints-1/questions/BATCH-02-QUESTIONS.md`

---

## Context

Phase 1 builds the test harness before any production runtime or compiler code.  This batch
creates the two mock classes (`MockSimulationView`, `MockEntityCommandBuffer`) and the
`BlueprintAssetBuilder` fluent API.  Together they enable writing tests that exercise
ECS state without a full engine host.

---

## Tasks

### Task 1: MockSimulationView (TASK-TH-001 + TASK-TH-006 integrated)

**Full spec:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-th-001----mocksimulationview)
and [TASK-DETAIL.md](../TASK-DETAIL.md#task-th-006----tickframe-refinements-patches-1--2-applied)

**Files:**
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Mocks/MockSimulationView.cs` (NEW)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Mocks/MockSimulationViewContractTests.cs` (NEW)

**Key implementation points (TH-001 + TH-006 patches already applied):**

- `MockSimulationView : ISimulationView` in namespace `Hrot.Blueprints.Tests.Mocks`.
- Constructor takes `(EntityRepository repo, MockEntityCommandBuffer ecb)`.
- Forward ALL read methods to the underlying `_repo`: `IsAlive`, `GetComponentRO<T>`,
  `GetManagedComponentRO<T>`, `HasComponent<T>`, `HasManagedComponent<T>`,
  `HasSingleton<T>`, `GetSingletonRO<T>`, `Query()`.
- `GetCommandBuffer()` returns the `MockEntityCommandBuffer` passed at construction.
- Time state: `internal float _time`, `internal float _deltaTime`, `internal uint _tick`.
  `internal void AdvanceTime(float dt)`: `_time += dt; _deltaTime = dt; _tick++`.
- `float Time => _time`, `float DeltaTime => _deltaTime`, `uint Tick => _tick`.
- **TH-006 patch applied:** `ReadEvents<T>()` is a one-liner: `return _repo.Bus.Read<T>();`
  **Do NOT add `_eventStreamsByType` or `BeginTick(...)` -- these are the pre-patch design.**
- The 3 contract tests from TH-DD §3.9 go in `MockSimulationViewContractTests.cs`.

**Tests Required (from TASK-TH-001 SC1-SC5):**

- SC1: Construct `MockSimulationView(repo, ecb)`. Write to repo then read via view.  Assert
  same backing chunk memory (no copy).
- SC2: `AdvanceTime(0.016f)` x3. Assert `Time ≈ 0.048f`, `DeltaTime == 0.016f`, `Tick == 3`.
- SC3: After publishing an event via `repo.Bus.Publish(evt)` and calling `repo.Bus.SwapBuffers()`,
  `view.ReadEvents<T>()` returns a span with count 1 (the bus was swapped).
- SC4: `GetCommandBuffer()` returns the same instance passed to the constructor.
- SC5: The 3 TH-DD §3.9 tests pass.

---

### Task 2: MockEntityCommandBuffer (TASK-TH-002)

**Full spec:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-th-002----mockentitycommandbuffer)

**Files:**
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Mocks/MockEntityCommandBuffer.cs` (NEW)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Mocks/MockEntityCommandBufferContractTests.cs` (NEW)

**Key implementation points:**

- `MockEntityCommandBuffer : IEntityCommandBuffer` in namespace `Hrot.Blueprints.Tests.Mocks`.
- Constructor takes `(EntityRepository repo)`.
- `internal abstract class EcbOp` with `abstract void Apply(EntityRepository repo)`.
- Sealed `EcbOp` subclasses (per TH-DD §4.4):
  `EcbOp_CreateEntityRecord` (no-op on playback, entity already created eagerly),
  `EcbOp_DestroyEntity`, `EcbOp_AddComponentUnmanaged<T>`, `EcbOp_AddEmptyComponentUnmanaged<T>`,
  `EcbOp_RemoveComponentUnmanaged<T>`, `EcbOp_SetComponentUnmanaged<T>`,
  `EcbOp_AddComponentManaged<T>`, `EcbOp_RemoveComponentManaged<T>`,
  `EcbOp_SetSingletonUnmanaged<T>`, `EcbOp_PublishEventUnmanaged<T>`.
- `CreateEntity()`: calls `_repo.CreateEntity()` immediately (real handle), records
  `EcbOp_CreateEntityRecord`, returns the real `Entity`.
- `AddEmptyComponent<T>(Entity)` method -- even if not on `IEntityCommandBuffer` interface,
  add it directly on `MockEntityCommandBuffer` for test use.
- `internal void Playback()`: iterates `_ops` in insertion order, calls each `Apply(_repo)`,
  then clears `_ops`.
- `internal IReadOnlyList<EcbOp> OpsForInspection` and `internal int OpCount`.
- Every `Apply` that targets an `Entity` must guard with `repo.IsAlive(entity)`.
- `EcbOp_SetComponentUnmanaged<T>.Apply` additionally checks `repo.HasComponent<T>(entity)`.
- `EcbOp_AddEmptyComponentUnmanaged<T>.Apply` calls `repo.AddComponent(entity, default(T))`.
- 4 contract tests from TH-DD §4.10 in `MockEntityCommandBufferContractTests.cs`.

**Tests Required (from TASK-TH-002 SC1-SC6):**

- SC1: CreateEntity, assert OpCount==1, entity alive, no component yet.
- SC2: AddComponent then Playback. Assert OpCount goes 0, component appears with correct value.
- SC3: AddEmptyComponent<TBigStruct> then Playback. Assert all bytes are zero.
- SC4: DestroyEntity: alive before playback, gone after.
- SC5: Queue 3 SetComponent ops (values 1, 2, 3); after Playback, value==3 (last write wins).
- SC6: 4 TH-DD §4.10 contract tests pass.

---

### Task 3: BlueprintAssetBuilder Fluent API (TASK-TH-004)

**Full spec:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-th-004----blueprintassetbuilder-fluent-api)

**Files:**
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Builders/BlueprintAssetBuilder.cs` (NEW)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Builders/BlueprintAssetBuilderTests.cs` (NEW)

**Key implementation points:**

- `BlueprintAssetBuilder` in `Hrot.Blueprints.Tests.Builders` with static factories:
  `Library(string name)`, `AiPrimitive(string name)`, `Instance(string name)`.
- Fluent: `WithAssetId`, `WithTierHint`, `WithWorldSingleton`, `WithIntent`, `WithHostings`,
  `WithParameter`, `WithWorkingStateField`, `WithVariable`, `WithCallablePeer`, `WithCustomEvent`,
  `WithGraph(string, Action<GraphBuilder>)`, `WithGraph(string, GraphKind, Action<GraphBuilder>)`,
  `WithEventGraph`.
- `Build()` returns a `BlueprintAsset`; all list fields non-null; `Header.SubsystemType = "Hrot.Blueprints"`,
  `Header.SchemaVersion = "1.0"`.
- `private Guid NewSyntheticGuid(params object[])` -- SHA256 of assetId bytes + UTF-8 parts,
  first 16 bytes become a Guid.  MUST be deterministic.
- `GraphBuilder` in same namespace: constructor `(string name, GraphKind kind, Guid assetId)`,
  methods: `Entry()`, `Return(NodeStatus)`, `Delay(float)`,
  `ChannelCommand(string channelType, string actionId, Action<NodeBuilder>)`,
  `WaitForChannel(string channelType)`, `SetVariable(string name, string valueExpr)`,
  `Branch(string conditionExpr, Action<GraphBuilder> trueBranch, Action<GraphBuilder> falseBranch)`,
  `Build()` -> `Graph`.
- Auto exec-wire chaining: each call to a node-producing method auto-wires previous node's
  exec-out to new node's exec-in via `LinkExec` helper.
- `LinkExec` silently does nothing when `fromNode == Guid.Empty` (first node).
- `NodeBuilder` helper: attaches data pins to specific nodes (used inside `ChannelCommand` callback).
- `SyntheticGuidHelper.Compute(Guid assetId, Guid graphId, params object[] parts)` static utility.
- `WithIntent` and `WithHostings` throw `InvalidOperationException` when called on non-AiPrimitive.
- `AiPrimitive(string)` pre-initializes `_primitive` with `Intent = Action`, empty Hostings list.
- `WithCallablePeer` records the peer's `_assetId` only (no retained reference to builder).

**Tests Required (from TASK-TH-004 SC1-SC6):**

- SC1: `Library("Foo").Build()` -- correct Dispatch, empty lists.
- SC2: AiPrimitive with Intent/Hostings/Parameter/WorkingState/Graph with Entry+Return.
  Verify node count == 2, link count == 1.
- SC3: Same builder sequence twice -- JSON strings identical (determinism).
- SC4: Instance with Variable + CustomEvent. Verify counts.
- SC5: Throws for WithIntent/WithHostings on non-AiPrimitive.
- SC6: `Entry().Delay(2.0f).Return(NodeStatus.Success)` -> 3 nodes, 2 links,
  first link From==entryNode.Id and To==delayNode.Id.

---

### Task 4: Mock Contract Tests (TASK-TH-007)

**Full spec:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-th-007----mock-contract-tests-8)

**Files:**
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Mocks/MockContractTests.cs` (NEW)

**Key implementation points:**

These 8 tests exercise the PATCHED behaviour (TH-006 patches already applied).
Each test must use `using var fixture = new BlueprintTestFixture()` -- which does NOT yet
exist!  Since `BlueprintTestFixture` is TASK-TH-003 (BATCH-03), write these tests against
`MockSimulationView` and `MockEntityCommandBuffer` directly (constructed manually), NOT
via `BlueprintTestFixture`.  Where a test needs fixture-level TickFrame semantics, construct
the components manually.  See below.

The 8 tests (from TH-DD §8.3, patched):

1. `IsAlive_AfterEcbDestroy_RemainsTrueUntilPlayback` -- create entity directly in repo,
   call `ecb.DestroyEntity(e)`, assert `repo.IsAlive(e) == true`, then `ecb.Playback(repo)`,
   assert `repo.IsAlive(e) == false`.
2. `GetComponentRO_ReturnsRefIntoChunkMemory` -- add component via repo RW, get ref via
   view RO, write via repo RW again, assert the first ref reflects the new value (same memory).
3. `ReadEvents_SameListThroughoutTick` -- publish event via `repo.Bus.Publish(evt)`,
   call `repo.Bus.SwapBuffers()`, call `view.ReadEvents<TestEvent>()` twice; confirm both
   spans point to the same data (span size == 1 both calls, same element value).
4. `MockView_DoesNotExposeDirectSingletonSetter` -- reflection finds zero methods named
   `"SetSingleton"` on `MockSimulationView`.
5. `Playback_PreservesInsertionOrder` -- queue SetComponent ops 1, 2, 3; after Playback,
   value == 3.
6. `TierUpgrade_HappensInBeforeSync_NotInSimulation` -- this test requires `BlueprintMaintenanceSystem`
   to exist. Since it is a stub, mark this test with `[Fact(Skip = "Requires BlueprintMaintenanceSystem (BATCH-04)")]`.
7. `AddEmptyComponent_LargeUnmanaged_DefaultInitsAfterPlayback` -- `ecb.AddEmptyComponent<LargeTestStruct>(e)`,
   `ecb.Playback(repo)`, assert all bytes zero (use `MemoryMarshal.AsBytes`).
8. `CreateEntity_ReturnsRealHandleImmediately` -- `var e = ecb.CreateEntity()`, assert
   `repo.IsAlive(e) == true` immediately (before playback).

Define `internal struct TestComponent { public int Value; }`,
`internal struct TestEvent { public int Value; }`,
`[StructLayout(LayoutKind.Sequential)] internal struct LargeTestStruct { public fixed byte Data[256]; }`
inside the test file (or a shared test-types file).

**Note on test 6 (TierUpgrade):** The test body from §8.3 requires a running `MaintenanceSystem`.
Skip it for now.  It will be un-skipped in BATCH-04 when `BlueprintMaintenanceSystem` is implemented.

**Tests Required:**
- SC1: 7 of 8 tests pass (test 6 is [Skip]).
- SC2-SC5: See the SCs listed in TASK-TH-007.

---

## Mandatory Developer Workflow: Test-Driven Task Progression

1. Implement MockSimulationView first. Write its contract tests; get them green.
2. Implement MockEntityCommandBuffer. Write its contract tests; get them green.
3. Implement BlueprintAssetBuilder + tests.
4. Implement MockContractTests (the 8 tests, test 6 skipped).
5. Run full solution build: zero errors, zero warnings.

---

## Testing Requirements

- All new test classes must have at least one passing `[Fact]` or `[Theory]`.
- Test 6 (`TierUpgrade_HappensInBeforeSync_NotInSimulation`) must be `[Fact(Skip = "...")]`.
- Tests must check actual values, not just "no exception" (except tolerance tests where noted).
- The `MockEntityCommandBuffer.Playback` must accept `EntityRepository` as parameter
  (see TickFrame design: `Ecb.Playback(_repo)`).
- Total test count after this batch: at least 40 tests in `Hrot.Blueprints.Tests`.

---

## Developer Insights (answer these in your report)

1. Did `IEntityCommandBuffer` require any interface changes to support `AddEmptyComponent<T>`?
   How did you handle it?
2. Were there any issues with `ISimulationView.ReadEvents<T>()` returning `ReadOnlySpan<T>`
   vs `IReadOnlyList<T>` as mentioned in some TH-DD sections? Which does the engine interface use?
3. Did the `ISimulationView` interface have all methods listed in TH-DD §3.2? Any missing?
4. Were there compiler/nullability warnings that needed suppression or workarounds?
5. Any design decisions made beyond the spec?

---

## Report Format

Submit `.dev/blueprints-1/reports/BATCH-02-REPORT.md` with:

```markdown
# BATCH-02 Report

## Tasks Completed
[List each TASK-ID with status]

## Test Results
[Full dotnet test output]

## Developer Insights
[Answer all 5 questions]

## Deviations from Spec
[Any deliberate deviation with rationale]

## Issues / Technical Debt
[Anything deferred]

## Build Verification
[Last 20 lines of `dotnet build IOS-IG-SimHost.sln` output]
```
