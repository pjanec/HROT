# BATCH-03: Utility AI — UtilityResultBuffer + Trace Buffer + UtilityScorer Core

**Batch Number:** BATCH-03
**Tasks:** Debt D-01 (P2 fix), TASK-UAI-P1-04, TASK-UAI-P1-05
**Phase:** Phase 1 — Runtime core + trace buffer
**Estimated Effort:** 15–20 hours
**Priority:** HIGH
**Dependencies:** BATCH-02 (scoring core data structures, curve evaluation, aggregator — complete)

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Task Detail:** `.dev/utility-ai/TASK-DETAIL.md` — Phase 1 tasks P1-04 and P1-05 (sections `### TASK-UAI-P1-04` and `### TASK-UAI-P1-05`)
2. **Architecture:** `.dev/utility-ai/Utility_AI_Design_v1_1.md`
   - §8 "Storage" (full section) — `UtilityResultBuffer` layout and `[InlineArray]` mutation trap
   - §9 "Debug" (full section) — trace buffer shape, gating discipline, ring-buffer design
   - §4.5 "Selection and tie-break" — hysteresis bonus for `PostureSelect`
   - §3 "Architecture overview" — how scorer, buffer, and inputs connect
3. **Previous Review:** `.dev/utility-ai/reviews/BATCH-02-REVIEW.md` — Corrective-0 context and P1-01/02/03 decisions
4. **Existing precedent — `[InlineArray]` pattern:** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs`
   Lines 33–105: `EqsResultArray`, `EqsCognitiveBuffer`, `GetSpanRW()`, `GetSpanRO()`.
   Mirror this pattern exactly.
5. **Existing precedent — trace buffer shape:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Diagnostics/BTreeTraceWorkingMemory1024.cs`
   Understand the `[StructLayout(LayoutKind.Sequential, Size = 1024)]` fixed-byte ring buffer.
   `UtilityTraceWorkingMemory1024` shares this shape.
6. **Existing precedent — trace gating discipline:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BTreeTickSystem.cs`
   Lines ~100–120: how `BehaviorDebugFlags` gates trace pointer acquisition.
   Mirror this discipline for `UtilityDebugFlags`.
7. **Existing precedent — reader dispatch (unmanaged function pointer):** `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BTreeTickSystem.cs`
   Lines ~175–195: the `unsafe static` thunk-dispatch pattern.
   `UtilityScorer` uses the same mechanism for reader dispatch.
8. **Debt Tracker:** `.dev/utility-ai/DEBT-TRACKER.md` — D-01 (P2, fix required this batch)

### Source Code Locations

- **Existing Utility Core:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/` (4 files already here)
- **New files (Utility layer):**
  - `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityResultBuffer.cs` — NEW
  - `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityTraceWorkingMemory1024.cs` — NEW
  - `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityScorer.cs` — NEW
- **New test files:**
  - `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityResultBufferTests.cs` — NEW
  - `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityScorerTests.cs` — NEW
- **Existing test to fix (Debt D-01):** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorldTests.cs`

### Build and Test Commands

```bat
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj
```

All prior passing tests (58 tests) **plus all new BATCH-03 tests** must pass before you write the report. Do not stop early. Fix compilation errors and test failures at their root cause.

### Report Submission

When done, submit your report to: `.dev/utility-ai/reports/BATCH-03-REPORT.md`
If you have questions: `.dev/utility-ai/questions/BATCH-03-QUESTIONS.md`

---

## Context

BATCH-02 delivered the scoring core data structures (P1-01), curve evaluation (P1-02), and aggregator (P1-03). BATCH-03 builds the two remaining runtime pieces:

- **P1-04** — the storage layer: `UtilityResultBuffer` (top-N ranked candidates) and `UtilityTraceWorkingMemory1024` (per-entity debug ring buffer), following the EQS `[InlineArray]` and BTree trace precedents exactly.
- **P1-05** — the scoring engine: `UtilityScorer.Evaluate` and `SelectPosture`, which drive the aggregator over a `UtilityDecisionDef` for one entity, populate the buffer, and emit trace entries when gated on.

After this batch, headless scoring is complete (sans input readers — those come in BATCH-04).

**Related Tasks:**
- [Debt D-01](../DEBT-TRACKER.md) — Activate pinned hash assertion in `Fnv1a32` test (P2, must fix this batch)
- [TASK-UAI-P1-04](../TASK-DETAIL.md#task-uai-p1-04-utilityresultbuffer-and-trace-buffer) — Result and trace buffers
- [TASK-UAI-P1-05](../TASK-DETAIL.md#task-uai-p1-05-utilityscorer-core-tick-path) — UtilityScorer core

---

## 🎯 Batch Objectives

1. Activate the deferred pinned-value assertion in the Fnv1a32 stability test (Debt D-01).
2. Implement `UtilityResultBuffer` with the safe `GetSpanRW()` / `GetSpanRO()` accessor pair and a companion test that proves the silent-write-loss trap.
3. Implement `UtilityTraceWorkingMemory1024` — a 1024-byte ring buffer gated by `UtilityDebugFlags.TraceEnabled` — mirroring `BTreeTraceWorkingMemory1024`.
4. Implement `UtilityScorer.Evaluate` and `SelectPosture` with hysteresis, using stub reader dispatch acceptable for Phase 1.
5. All success criteria SC-P1-04-1 through SC-P1-04-3 and SC-P1-05-1 through SC-P1-05-4 pass as concrete test assertions.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Corrective (D-01):** Fix → **ALL tests pass** ✅
2. **P1-04:** Implement `UtilityResultBuffer` + `UtilityTraceWorkingMemory1024` → Write tests → **ALL tests pass** ✅
3. **P1-05:** Implement `UtilityScorer` → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- Current task implementation is complete
- Current task tests are written
- **ALL tests passing** (including all previous batch tests)

**Why:** Each component is a prerequisite for the one above it. A broken buffer makes scorer tests impossible to trust.

---

## ✅ Tasks

---

### Corrective Task 0: Activate Fnv1a32 pinned-value assertion (Debt D-01)

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorldTests.cs` — UPDATE

**Problem:** `Fnv1a32_CoverQuery_ProducesStableNonZeroValue` (line ~104) has the exact hash assertion commented out:
```csharp
// Assert.Equal(0x9317A97Bu, hash1); // Uncomment to pin exact value after verification
```

**Fix:**
1. Run the existing test to capture the actual hash value produced by `UtilityTestWorld.Fnv1a32("CoverQuery")`.
2. Verify the value is non-zero and stable across two calls (the test already checks stability and non-zero).
3. Uncomment and update the `Assert.Equal` line with the verified value so algorithm regressions would be caught.

**Acceptance:** The test must fail if you change the FNV-1a algorithm (it is a regression guard, not a sanity check).

---

### Task 1: `UtilityResultBuffer` + `UtilityTraceWorkingMemory1024` (TASK-UAI-P1-04)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-uai-p1-04-utilityresultbuffer-and-trace-buffer) for full scope and success criteria.

**Files:**
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityResultBuffer.cs` — NEW
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityTraceWorkingMemory1024.cs` — NEW
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityResultBufferTests.cs` — NEW

**Design Reference:** `Utility_AI_Design_v1_1.md` §8 (all subsections) and §9 (all subsections).

**Key implementation notes:**

`UtilityResultBuffer`:
- One entry per top-N slot: `UtilityResultEntry { long CandidateHandle; float Score; byte WinningPostureId; }` (size it to match an unmanaged-safe layout).
- The inline array (`[InlineArray(16)]`) is named `Results`.
- Provide `GetSpanRW()` using `MemoryMarshal.CreateSpan(ref Unsafe.As<…>(ref Results), 16)` and `GetSpanRO()` — mirror `EqsCognitiveBuffer` exactly.
- Include a `Count` field (valid entries) and a `RunnerUpMargin` field (winner score minus second-place score).
- Add a XML doc comment on the `Results` field (and/or the struct) stating the mutation trap explicitly: direct index assignment (`Results[i] = …`) is silently lost; always cast to `Span<T>` first.

`UtilityDebugFlags`:
- Component struct with a `bool TraceEnabled` field (or `byte` for unmanaged friendliness).
- Add `[ComponentId(…)]` and `[DataPolicy(DataPolicy.NoSave)]` following existing component convention (see `BehaviorApplicationComponentIds` for how component IDs are declared; add a `UtilityComponentIds` class or similar).

`UtilityTraceWorkingMemory1024`:
- `[StructLayout(LayoutKind.Sequential, Size = 1024)]` unmanaged ring buffer, same physical layout as `BTreeTraceWorkingMemory1024` (8-byte header + payload).
- Per-entry record `UtilityTraceRecord` capturing: `InputId` (ushort), `RawValue` (float), `NormalizedValue` (float), `CurveOutput` (float), `Weight` (float), `RunningAggregate` (float), `OptionIndex` (byte), and fit into a fixed stride (align to 32 bytes).
- Provide `WriteConsiderationRecord(...)` and `WriteWinnerRecord(ushort tick, byte winnerOptionIdx, float winnerScore, float runnerUpMargin)` helpers analogous to `BTreeTraceWorkingMemory1024.WriteNodeEvaluated(...)`.
- Provide a `ReadRecord(int index, out UtilityTraceRecord record)` helper for tests.

**Tests Required (SC-P1-04-1 through SC-P1-04-3):**

SC-P1-04-1 (trap regression guard):
```csharp
// Write via GetSpanRW() -- entry must survive
Span<UtilityResultEntry> span = buf.GetSpanRW();
span[0] = new UtilityResultEntry { Score = 0.9f, CandidateHandle = 42L };
// Read via GetSpanRO() -- must be observable
Assert.Equal(0.9f, buf.GetSpanRO()[0].Score);
Assert.Equal(42L, buf.GetSpanRO()[0].CandidateHandle);

// Direct indexer write -- silently lost (demonstrate the trap)
buf.Results[0] = new UtilityResultEntry { Score = 0.1f };  // ← compiler emits defensive copy
Assert.Equal(0.9f, buf.GetSpanRO()[0].Score);  // still 0.9 -- write was lost
```
This test IS the regression guard: it proves the trap is real, not just documented. If the IL changes in a future runtime and the trap is fixed, this test will fail and alert maintainers.

SC-P1-04-2: `TraceEnabled = false` → zero entries written to `UtilityTraceWorkingMemory1024`. Simulate a scoring pass that would write trace entries but is called without the `UtilityDebugFlags` component present (or with `TraceEnabled = false`). Assert `traceBuffer.RecordCount == 0`.

SC-P1-04-3: `TraceEnabled = true` → after a scoring pass with 2 options × 3 considerations each (6 consideration records) plus a winner record, assert `RecordCount == 7` (or the exact count your design emits per pass), and assert that the winner record's score and runner-up margin are correct.

---

### Task 2: `UtilityScorer` core tick path (TASK-UAI-P1-05)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-uai-p1-05-utilityscorer-core-tick-path) for full scope and success criteria.

**Files:**
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityScorer.cs` — NEW
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityScorerTests.cs` — NEW

**Design Reference:** `Utility_AI_Design_v1_1.md` §3, §4 (all subsections), §6.1 (reader dispatch).

**Key implementation notes:**

Stub reader dispatch (Phase 1 only):
- Define `UtilityInputRegistrar` as a static class with a `Register(ushort inputId, delegate*<in UtilityInputCtx, float> reader)` method and an internal dictionary/array.
- `UtilityScorer` calls registered readers via the unmanaged function pointer. Since the real source-gen registrar is Phase 2, a stub that returns a fixed value (or reads from a per-test-injected delegate) is acceptable. Design the reader dispatch interface so Phase 2 can replace only the registrar, not the scorer.

`UtilityInputCtx` struct:
- Carries: `EntityRepository Repo`, `EntityId Self`, `EntityId Context` (target/candidate), `InputParams Params` (from the `UtilityConsideration`).
- Must be a `readonly ref struct` (stack-only) to prevent unintended capture; or at minimum a plain struct passed `in` to readers.

`UtilityScorer.Evaluate(...)`:
- Signature: `static void Evaluate(EntityRepository repo, EntityId self, in UtilityDecisionDef def, EntityId context, ref UtilityResultBuffer output, UtilityTraceWorkingMemory1024* trace)` (trace may be null).
- For each option in `def.Options`: build `UtilityInputCtx`, call `Aggregator.Aggregate` over the option's considerations (using `ResponseCurve.Evaluate` for each), record the final score.
- Sort results descending by score into the output buffer via `GetSpanRW()`.
- If `trace != null`, write one `UtilityTraceRecord` per consideration and one winner record.
- For `ThreatRanking` / `WeaponSelection` decisions: no hysteresis.
- `RunnerUpMargin = Results[0].Score - Results[1].Score` (or 0 if fewer than 2 options).

`UtilityScorer.SelectPosture(...)`:
- Signature: `static byte SelectPosture(EntityRepository repo, EntityId self, in UtilityDecisionDef def, byte activePostureId, float hysteresisBonus, ref UtilityResultBuffer output, UtilityTraceWorkingMemory1024* trace)`.
- Apply hysteresis: before scoring, if candidate option index equals `activePostureId`'s option, add `hysteresisBonus` to that option's final score, then re-sort.
- Return the winning `OptionId` (byte).

**Tests Required (SC-P1-05-1 through SC-P1-05-4):**

SC-P1-05-1 (gating zero): Construct a 3-option `UtilityDecisionDef` where one option has a `Step` curve consideration that outputs 0.0 (threshold not met). Assert that option's final score is 0.0f in the result buffer. The other two options must have non-zero scores.

SC-P1-05-2 (winner ordering + runner-up margin): A decision where options score 0.9, 0.6, 0.3 (in declaration order). After `Evaluate`, assert `buffer.GetSpanRO()[0].Score == 0.9f` (winner at index 0) and `buffer.RunnerUpMargin == 0.3f` (0.9 - 0.6).

SC-P1-05-3 (hysteresis): Use `SelectPosture` with `hysteresisBonus = 0.08f`. Set up two options where:
- Option A (active) scores 0.70, Option B scores 0.75. **0.75 - 0.70 = 0.05 < 0.08**: active should hold (A wins with bonus: 0.70 + 0.08 = 0.78 > 0.75).
- Then adjust so Option B scores 0.80. **0.80 - 0.70 = 0.10 > 0.08**: B wins (B=0.80 > A+bonus=0.78).
Assert both outcomes.

SC-P1-05-4 (16-candidate ranking): A `ThreatRanking` evaluation over 16 contacts (set up with 16 options in the def), each with a `Linear` curve reader returning a distinct value. Assert that after `Evaluate`, `buffer.Count == 16` and `buffer.GetSpanRO()` is sorted descending by score.

**Reader stub for tests:** For these tests, register stub readers that return a constant or fixture-controlled value via `UtilityInputRegistrar.Register(inputId, &StubReader)`. The stub mechanism is test-only scaffolding that Phase 2 replaces with the generated registrar.

**No allocations on the hot path.** Do not use `new`, LINQ, or boxing inside `Evaluate` or `SelectPosture`. Use stack-local buffers (stackalloc or fixed arrays) for intermediate sorting.

---

## 🧪 Testing Requirements

- **Minimum test count:** 15–20 new tests across `UtilityResultBufferTests.cs` and `UtilityScorerTests.cs`.
- **SC coverage:** All 7 success criteria (SC-P1-04-1 through SC-P1-04-3 and SC-P1-05-1 through SC-P1-05-4) must map 1:1 to named test methods.
- **Test quality bar:**
  - All assertions must check specific numeric values, not just "non-null" or "no exception".
  - SC-P1-04-1 trap test must assert that the value written via direct indexer is NOT visible — this is the regression guard.
  - SC-P1-05-3 hysteresis must test both the hold case and the switch case in separate assertions (or as a parameterized theory).
  - SC-P1-05-4 must verify sort order, not just count.
- **All 58 prior tests must continue to pass.**

---

## ⚠️ Quality Standards

**NOT ACCEPTABLE:**
- Tests that only verify compilation or "does not throw" for scoring logic.
- Skipping the SC-P1-04-1 trap test because it seems redundant with the comment.
- `SelectPosture` that applies hysteresis as pre-scoring bias rather than post-scoring bonus (must be post-scoring: score all options, then add bonus to the active option's score, re-sort).
- Trace buffer that allocates per-entry (must be fixed-byte writes into the ring buffer — see `BTreeTraceWorkingMemory1024.WriteNodeEvaluated` for the pattern).

**REQUIRED:**
- `UtilityTraceWorkingMemory1024.RecordCount` saturates at capacity (does not overflow) — test this.
- `UtilityResultBuffer` struct must be unmanaged (no managed fields).
- Sorting in `Evaluate` must handle ties deterministically (lower option index wins on tie).

---

## 📊 Report Requirements

Submit `.dev/utility-ai/reports/BATCH-03-REPORT.md` with:

1. **Implementation Summary:** Files created/modified, key design decisions.
2. **Test Results:** Output of `dotnet test` (pass/fail counts). Confirm all 58 prior tests still pass plus the new batch tests.
3. **SC Checklist:** Confirm each SC (D-01, SC-P1-04-1/2/3, SC-P1-05-1/2/3/4) is covered by a named test.
4. **Developer Insights:**
   - What issues were encountered during implementation?
   - What weak points did you spot in the existing codebase?
   - What design decisions were made beyond the spec?
5. **Deferred Items:** Anything explicitly deferred (with justification).
