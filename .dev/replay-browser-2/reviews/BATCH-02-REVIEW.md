# BATCH-02 Review

**Batch:** BATCH-02 -- Stage 1 Completion -- Context, Export Service, CLI, and Acceptance Gate
**Reviewer:** Dev Lead
**Decision:** CHANGES REQUIRED

---

## 1. Build Gate

PASS. `dotnet build FDP/FDP.sln` = 0 errors, 2 warnings (unrelated to new code).

---

## 2. Test Results Summary

| Suite | Total | Passed | Failed |
|-------|-------|--------|--------|
| Fdp.Toolkits.Tests (ReplayBrowser filter) | 38 | 38 | 0 |
| Fdp.Tools.RecordingDumper.Tests | 4 | 4 | 0 |

EX-T27, EX-T28, EX-T29 explicitly deferred to BATCH-03 (changelog mode depends on Stage 3 diff engine). Correct.

---

## 3. Production Code Review

### RecordingExportService

**FdpEventBus.PrepareForNativeEventReplay<T>()** -- sound fix. The root cause analysis in the report
is accurate: replayed events landing in `UntypedNativeEventStream` were invisible to
`GetDebugInspectors()`. Adding a typed pre-registration stream is the correct approach.

**Windowing off-by-one fix** (`SeekToFrame(StartFrame - 1)`) -- correct. EX-T12 now accurately
validates the exact frame count.

**AutoRegisterAllEventTypes** -- reflection-based pre-registration is acceptable given the
`RecordingExportService` runs in a dedicated sandbox context.

### ReplayBrowserContext

Implemented correctly. Clean isolation: dedicated `EntityRepository`, `FdpEventBus`,
`PlaybackController`. Dispose is idempotent. FND-T06/T07/T07b all exercise meaningful paths.

### CLI (RecordingDumper)

Argument mapping, exit codes, and mutual-exclusion enforcement all match DESIGN.md §3.7.

---

## 4. Test Quality Review

### Green tests -- no issues

| Test | Assessment |
|------|------------|
| EX-T01..EX-T07 | Correct. Fixtures use real `.fdp` files; assertions are non-trivial. |
| EX-T08, EX-T09 | Dual-sandbox parity verification is exactly right. |
| EX-T10, EX-T11 | Tight per-frame assertions. |
| EX-T12 | Exact frame count assertion (`== 2`). Correct. |
| EX-T15, EX-T16 | Filter tests properly verify both entities block and destroyed-entities block. |
| EX-T17, EX-T18 | Block-omission tests check key absence. |
| EX-T19 | Newline absence check is appropriate for minified mode. |
| EX-T21 | Verifies the `"[Index, vGen]"` string format in actual JSON text. |
| EX-T23, EX-T24 | Both managed and unmanaged event path tested. Events verified by type name and `IsManaged` field. EX-T24 also checks `Payload` not null. |
| EX-T25 | Reduced to 200 frames (from 10k) "for test speed" -- acceptable, still validates streaming. Budget is 32 MB. |
| EX-T26 | Parallel-context isolation test is thorough (checks both `CurrentFrame` and `GlobalVersion`). |
| EX-T30..EX-T32 | CLI round-trip and integration tests are meaningful. |

---

## 5. Issues Found

### P2 -- EX-T22 tests the wrong scenario

**Severity:** P2 -- spec deviation, wrong feature path tested.

**Design spec (DESIGN.md §3.8):**
> EX-T22 | Custom `IEntityScenarioTranslator` (a stub `FooBlackboardTranslator`) is honored;
> its projected DTO appears under `Payload`.

**What was implemented:**
```csharp
public void EX_T22_NullSerializer_FallsBackToAutoSerializer()
{
    // Constructing without a ScenarioSerializer uses FdpAutoSerializer fallback.
    new RecordingExportService(serializer: null).ExportToJson(...);
    // asserts: components are present (any components)
}
```

**Problem:** This tests the null-serializer fallback path, not the custom translator injection
path. The design requires a stub `FooBlackboardTranslator : IEntityScenarioTranslator` whose
`Extract` method returns a specific DTO object, and the test must verify that this DTO string
(or a recognizable marker from it) appears in the `Payload` field for the matching component.

The `IEntityScenarioTranslator` interface exists at
`FDP/Toolkits/Fdp.Toolkits/Scenario/IEntityScenarioTranslator.cs` and is fully implemented
elsewhere in the codebase (e.g., `UnitSubordinateTranslator`, `MissionPlanTranslator`). A stub
is straightforward to implement in the test file.

**Required fix:** Replace `EX_T22_NullSerializer_FallsBackToAutoSerializer` with the correct
`EX_T22_CustomTranslator_IsHonored_PayloadReflectsStubDto` test. The null-serializer behavior
can be retained as a separate small check inside the same test or collapsed into EX-T01.

### P3 -- EX-T13 assertion too loose

**Severity:** P3 -- test passes but does not fully constrain the behavior.

**Test assertion:** `Assert.True(frames.Count >= 1 && frames.Count <= 3, ...)`

**Expected assertion:** The test comment explicitly states: "StartTimeSec=1.5 -> seek past frame 1
(t=1.0), should start at frame 2 (t=2.0); EndTimeSec=3.0 -> include frame 2 (t=2.0) and frame 3
(t=3.0)". Given deterministic 1-second frame spacing, the expected count is exactly 2. The current
`>= 1 && <= 3` range is too wide and would pass even if the windowing logic skipped or included
the wrong frames.

**Required fix:** Change to `Assert.Equal(2, frames.Count)`.

### P3 -- EX-T20 does not exercise FlattenNumericArrays on actual array-typed payloads

**Severity:** P3 -- logged in DEBT-TRACKER for future resolution.

**Problem:** `HarnessPosition` (float X, Y, Z individual fields) serializes as a JSON object
`{"X": 1.0, "Y": 0.0, "Z": 0.0}`, not as a numeric array `[1.0, 0.0, 0.0]`.
`JsonAestheticFormatter.FlattenNumericArrays` operates on `JsonArray` nodes; it has no effect
on `HarnessPosition`-style structs. The current assertion (`DoesNotContain("  1,\n", text)`) is
trivially satisfied and does not validate `FlattenNumericArrays` behavior at all.

**Root cause:** The harness component types (`HarnessPosition`, `HarnessVelocity`) do not include
any `Vector3` or `Quaternion`-shaped array fields, which is the actual trigger for
`FlattenNumericArrays`.

**Resolution:** Add a `HarnessTransform` component to the harness with a `Vector3 Position` field
(or a `float[3]` / `[InlineArray]` equivalent), record it in a dedicated fixture, and assert that
the exported JSON contains `"Position": [x, y, z]` on a single line. This component registration
must use an ID outside the reserved range (200-203, 99001-99003). Deferred to BATCH-03.

---

## 6. Carry-Forward Debt

| ID | Priority | Description | Target |
|----|----------|-------------|--------|
| RB01-P3-001 | P3 | `JsonExportOptions` round-trip Entity test uses empty list because `Entity` lacks `[JsonConstructor]` | BATCH-03 or later |
| RB02-P2-001 | P2 | EX-T22 tests null-fallback instead of custom `IEntityScenarioTranslator` injection | BATCH-02C (corrective) |
| RB02-P3-002 | P3 | EX-T13 assertion `>= 1 && <= 3` should be `== 2` | BATCH-02C (corrective) |
| RB02-P3-003 | P3 | EX-T20 does not exercise `FlattenNumericArrays` on Vector3/Quaternion payloads | BATCH-03 |

---

## 7. Corrective Action

A corrective batch (BATCH-02C) is required before proceeding to BATCH-03. It must fix:

1. **EX-T22** (P2): Implement `FooHarnessBlackboardTranslator : IEntityScenarioTranslator` as a
   private nested test class or file-private class in `RecordingExportServiceTests.cs`. The
   translator must: (a) claim a distinct component type (e.g., `HarnessVelocity` or a new
   `HarnessExtra` component), (b) implement `Extract` returning a `JsonNode` with a known marker
   field (`"Source": "FooBlackboard"`), (c) implement `GetConsumedComponentsMask` correctly. The
   test must build a `ScenarioSerializer` with this translator registered, pass it to
   `RecordingExportService`, export, and assert the marker field appears in the JSON output.

2. **EX-T13** (P3): Change assertion to `Assert.Equal(2, frames.Count)`.

All 38 + 4 tests must remain green after corrections.

---

## 8. Approved Tasks

The following tasks from BATCH-02 are accepted as-is:
- P2-corrective: Harness self-test now steps through frames and verifies destruction + events. ACCEPTED.
- RB-1.4: `ReplayBrowserContext` -- correct isolation, FND-T06/T07/T07b green. ACCEPTED.
- RB-1.5: `RecordingExportService` -- implementation is solid; bugs were found and fixed with good
  root-cause analysis. ACCEPTED pending EX-T22/EX-T13 corrections in BATCH-02C.
- RB-1.6: CLI `RecordingDumper` -- correct. ACCEPTED.
- RB-1.7: Acceptance gate passes for all non-deferred tests. ACCEPTED.
