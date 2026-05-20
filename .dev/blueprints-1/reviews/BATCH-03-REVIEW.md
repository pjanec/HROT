# BATCH-03 Review

**Batch:** BATCH-03 -- Debug Interfaces, TestData, CapturingDebugSession, Foundation Stubs
**Reviewer:** Dev Lead
**Verdict:** APPROVED WITH CORRECTIONS (P2 items must be fixed in BATCH-04)

---

## Summary

BATCH-03 is substantially complete. 76 tests pass (1 skipped -- pre-existing TierUpgrade stub),
0 failures, 0 build errors. TH-009 (TestData) is excellent. TH-008 (CapturingDebugSession)
has two P2 defects against the design spec that must be corrected in BATCH-04 as Corrective
Task 0.

---

## Scope Check

- **TASK-TH-008:** MOSTLY COMPLETE. `IBlueprintProbeSink`, `IBlueprintDebugSession`, `DebugProbe`,
  `NullProbeSink`, `CapturingDebugSession` all present and in the correct namespaces.
  8 contract tests cover SC1-SC8. Two P2 defects noted below.

- **TASK-TH-009:** COMPLETE. All 9 valid sample assets present and parseable. 4 required invalid
  assets present. `TestData` class with `LoadAsset`, `LoadSnapshot`, `ReadOrRegenerateSnapshot`,
  `ResolveTestAssetsDir`, `SampleAssets` constants. `TestEventDefinitions` with `HitEvent`.
  `Snapshots/` directory structure with `.gitkeep` placeholders. `SampleAssetLoadTests`
  with 9 Theory tests + 2 Fact tests = 11 tests. `.csproj` `CopyToOutputDirectory` updates
  verified (tests pass in output dir).

- **Foundation Stubs:** COMPLETE. `BlackboardTier`, `BlueprintLatentCursor`,
  `BlueprintCompileException`, `CompilerMode`, `BlueprintRegistry` (minimal with staging),
  `BlueprintDefinition` (minimal), `BlueprintTickSystem` stub,
  `BlueprintMaintenanceSystem` stub, `BlueprintBlackboard1024/4096/16384` with ComponentIds
  205-207, `BlueprintBlackboardPartitions` stub, `BlueprintCompiler` stub,
  `InMemoryRoslynCompiler` stub.

---

## Design Alignment

### TH-008 Defects

**P2-BATCH03-001: `NodeEnterRecord` missing `Time` field**

The design (TASK-DETAIL.md TASK-TH-008 scope) specifies:
```
NodeEnterRecord(Entity Self, string NodeId, float Time)
```
The implementation has:
```csharp
public sealed record NodeEnterRecord(Entity Self, string NodeId);
```
The `Time` field is absent. Later tasks (compiler-generated call sites, Phase 3 latency
tests) will use `NodeEnterRecord.Time` to measure timing and validate that node execution
happens in the expected tick order. Adding `Time` after the fact will break all current
uses of the record if this is not fixed early.

**Must be fixed in BATCH-04 as Corrective Task 0.**

**P2-BATCH03-002: Two skip-annotated usage-pattern tests missing**

TASK-TH-008 scope explicitly requires:
> "Two usage-pattern tests from §10.4 (`Debug_TraceMode_RecordsAllNodeEntries`,
>  `Debug_Breakpoint_FiresWhenNodeEntered`) tagged `[Trait("Category", "RequiresCompiler")]`
>  and marked with `Skip = "Requires Phase 3 compiler"`."

These tests were not added. They provide the scaffolding for Phase 3 test expansion -- their
absence means Phase 3 will have nowhere to anchor the compiler-based debug integration tests.

**Must be added in BATCH-04 as Corrective Task 0.**

### Minor Issues (P3)

**P3-BATCH03-003: `BreakprintKey` record not defined**

TASK-TH-008 lists `BreakpointKey(string NodeId)` as a required record. Not defined in the
implementation (breakpoints use raw strings). Low impact for current slice, but the design
references it by name. Track as P3 debt.

**P3-BATCH03-004: `IBlueprintDebugSession.OnPinValueChanged` event renamed**

The interface event is named `OnPinValueChangedEvent` (to avoid conflict with the generic
`OnPinValueChanged<T>` method from `IBlueprintProbeSink`). The design names the event
`OnPinValueChanged`. The naming mismatch is acceptable given the C# language constraint,
but should be documented as a deliberate deviation, and the implementation note added as a
comment in `IBlueprintDebugSession.cs`.

---

## Test Quality Assessment

### TH-008 Contract Tests

Tests are GOOD:
- SC1 (append order), SC2 (Hit/HitCount), SC3 (PinValue), SC4 (breakpoint fires),
  SC5 (no false fire), SC6 (Clear), SC7 (ClearBreakpoint), SC8 (HitsFor by entity) --
  all covered by the 8 tests.
- Tests verify specific values, not just "no exception".
- Entity isolation test (SC8) confirms per-entity filtering works.

Missing: SC6 from TASK-DETAIL: `fixture.DebugSession != null` and `DebugProbe.Sink` references
same instance -- this is a TASK-TH-003 concern (BlueprintTestFixture not yet implemented),
acceptable deferral.

### TH-009 Asset Quality

Assets are GOOD:
- `MoveToAndFire.bp.json` has `Dispatch: "AiPrimitive"` and `ChannelCommandNode` -- satisfies constraint.
- `HealthRegen.bp.json` has `Dispatch: "Instance"` with `CurrentHealth` and `MaxHealth` variables -- satisfies constraint.
- `LibraryMath.bp.json` has `Dispatch: "Library"` -- satisfies full dispatch-kind coverage.
- Invalid assets are syntactically valid JSON with semantically wrong content (correct per spec).
- Extra assets beyond the required 9 (`with-branch`, `simple-action`, etc.) are bonus from earlier batches -- harmless.

`SampleAssetLoadTests` tests the right things: parses without exception, name is non-empty.
The invalid asset test verifies parse-ok-but-semantically-wrong contract.
The snapshot test verifies graceful FileNotFoundException for not-yet-created snapshots.

---

## Issues Found (Summary)

| ID | Priority | Description | Target |
|----|----------|-------------|--------|
| P2-BATCH03-001 | P2 | `NodeEnterRecord` missing `Time` field | BATCH-04 Corrective Task 0 |
| P2-BATCH03-002 | P2 | Missing 2 skip-annotated placeholder tests for Phase 3 | BATCH-04 Corrective Task 0 |
| P3-BATCH03-003 | P3 | `BreakpointKey` record not defined | DEBT-TRACKER |
| P3-BATCH03-004 | P3 | `IBlueprintDebugSession` event renamed `OnPinValueChangedEvent` | DEBT-TRACKER |

---

## Approved Git Commit Message

```
feat(blueprints): Phase 1 TH-008 CapturingDebugSession + TH-009 TestData (BATCH-03)

- TH-008: IBlueprintProbeSink, IBlueprintDebugSession, DebugProbe, NullProbeSink
  CapturingDebugSession with 8 contract tests
- TH-009: 9 valid + 4 invalid .bp.json test assets, TestData class,
  TestEventDefinitions, SampleAssetLoadTests (11 tests), Snapshots dirs
- Foundation stubs: BlackboardTier, BlueprintLatentCursor, BlueprintCompileException,
  CompilerMode, BlueprintRegistry (minimal), BlueprintDefinition (minimal),
  BlueprintTickSystem stub, BlueprintMaintenanceSystem stub,
  BlueprintBlackboard1024/4096/16384 with ComponentIds 205-207,
  BlueprintBlackboardPartitions stub, BlueprintCompiler stub,
  InMemoryRoslynCompiler stub
- AllowUnsafeBlocks enabled in Fdp.Toolkits.csproj

Tests: 76 passed, 1 skipped (+19 new tests over BATCH-02 baseline)

Resolves: TASK-TH-008, TASK-TH-009
Known P2 defects (to be fixed in BATCH-04): NodeEnterRecord missing Time field,
missing Phase 3 skip-annotated debug tests
```
