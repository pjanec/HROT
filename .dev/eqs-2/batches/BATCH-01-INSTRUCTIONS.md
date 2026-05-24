# BATCH-01: EQS Foundations — Core Data Model and DDS Stubs

**Batch Number:** BATCH-01
**Tasks:** TASK-EQS-001, TASK-EQS-002, TASK-EQS-003
**Phase:** Phase 1 — Foundations (data model + wire protocol stubs)
**Estimated Effort:** 8–10 hours
**Priority:** HIGH
**Dependencies:** None (first batch)

---

## Onboarding & Workflow

### Developer Instructions

This is the first batch of the EQS v1.3 workstream. You will lay down the core ECS data
structures (components, events, pool) and create compile-only stubs for the four DDS
translators. No solver logic or systems in this batch — only types and compile-ability.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `docs/AI_DEV_GUIDE.md` — batch-based development workflow.
2. **Onboarding:** `.dev/eqs-2/ONBOARDING.md` — EQS context, folder layout, key constraints.
3. **Task Definitions:** `.dev/eqs-2/TASK-DETAIL.md` — sections TASK-EQS-001, TASK-EQS-002,
   TASK-EQS-003 (full specs, success conditions).
4. **Design Reference:** `.dev/eqs-2/EQS_Design_v1.3_final.md` — §2, §3.1, §8, §8.1.
5. **Implementation Details:** `.dev/eqs-2/IMPLEM_DETAILS.md` — L:3–170 (components),
   L:185–285 (pool + event), L:305–460 (DDS topics + translator stubs).

### Source Code Locations

| Area | Path |
|---|---|
| New EQS types (components, pool, topics) | `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/` |
| Component ID catalog | `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` |
| DDS translator stubs (Brain-side) | `Hrot/Network/NED/Cgf/` |
| DDS translator stubs (Muscle-side) | `Hrot/Network/NED/SimHost/` |
| Existing EQS area query files (read for pattern) | `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/AreaQueryBatchData.cs` |
| FDP unit tests project | `FDP/Toolkits/Fdp.Toolkits.Tests/` |

### Build Commands

```powershell
# Build the full solution
dotnet build IOS-IG-SimHost.sln

# Build FDP only (faster iteration)
dotnet build FDP/FDP.sln

# Run FDP unit tests
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/
```

### Report Submission

When done, submit your report to: `.dev/eqs-2/reports/BATCH-01-REPORT.md`

If you have questions: `.dev/eqs-2/questions/BATCH-01-QUESTIONS.md`

---

## Context

This batch establishes the complete data model for the EQS system. All subsequent batches
depend on these types being correct. The DDS translator stubs ensure the full solution compiles
end-to-end from day one.

**Related Tasks:**
- [TASK-EQS-001](./../TASK-DETAIL.md#task-eqs-001--core-component-layouts) — EqsResult, EqsCognitiveBuffer, EqsSensor components
- [TASK-EQS-002](./../TASK-DETAIL.md#task-eqs-002--eqsresultpool-singleton-and-eqsresultevent) — EqsResultPool singleton and EqsResultEvent
- [TASK-EQS-003](./../TASK-DETAIL.md#task-eqs-003--dds-wire-topics-and-translator-contracts) — DDS wire topics and translator class stubs

---

## Batch Objectives

1. Define the 24-byte `EqsResult` struct and its `[InlineArray(16)]` wrapper with safe RW
   accessor to avoid the C# `[InlineArray]` defensive-copy trap.
2. Define `EqsSensor` and `EqsCognitiveBuffer` ECS components with correct component IDs.
3. Define `EqsResultPool` singleton and `EqsResultEvent` unmanaged event with ring-buffer
   write-and-wrap logic.
4. Define `EqsSensorConfigTopic` and `EqsResultTopic` DDS structs.
5. Create four compile-only translator stubs that compile against existing interfaces.
6. Add corresponding component IDs to `GlobalComponentIds.cs`.
7. Write unit tests proving correctness of the struct layout, accessor safety, and pool
   wrap arithmetic.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **TASK-EQS-001:** Implement → Write tests → **ALL tests pass** ✅
2. **TASK-EQS-002:** Implement → Write tests → **ALL tests pass** ✅
3. **TASK-EQS-003:** Implement (compile-only) → **Build succeeds** ✅

**DO NOT** move to the next task until:
- Current task implementation complete
- Current task tests written and passing
- `dotnet build FDP/FDP.sln` succeeds without errors

---

## Tasks

### Task 1: Core Component Layouts (TASK-EQS-001)

**Files to create:**
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs` (NEW FILE)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-eqs-001--core-component-layouts)

**Design Reference:** Design §2, §8, §8.1; Impl L:3–170

Key implementation points (read IMPLEM_DETAILS.md L:3–170 for full code):
- `EqsResult`: 24-byte, `[StructLayout(LayoutKind.Sequential)]`, fields: `EntityId` (long),
  `PositionX/Y` (float), `Score` (float), `Flags` (short), `_pad` (short).
- `EqsResultArray`: `[InlineArray(16)]` wrapper over `EqsResult`.
- `EqsCognitiveBuffer`: `[ComponentId(GlobalComponentIds.EqsCognitiveBuffer)]` component with
  `Count`, `LastUpdateTick`, `Results` (EqsResultArray), and `IsReady` property.
  **CRITICAL:** `GetSpanRW()` MUST use `MemoryMarshal.CreateSpan(ref Unsafe.As<EqsResultArray,
  EqsResult>(ref Results), 16)` — **NOT** direct `[InlineArray]` index assignment (Design §8.1).
- `EqsSensor`: `[ComponentId(GlobalComponentIds.EqsSensor)]` with `BlueprintId`, `Epoch`,
  `SearchRadius`, `FactionFilter`, `ThreatThreshold`, `PublishPolicy`, `Priority`.

**GlobalComponentIds.cs additions:**
- `EqsSensor = 207`
- `EqsCognitiveBuffer = 208`

File: `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` — add after `BlueprintBlackboard16384 = 206`.

**Tests Required** (in `FDP/Toolkits/Fdp.Toolkits.Tests/`, create new test class
`EqsComponentLayoutTests.cs`):
- `EqsResult_SizeIs24Bytes`: `Assert.Equal(24, Marshal.SizeOf<EqsResult>())`
- `EqsCognitiveBuffer_GetSpanRW_WritePersists`: call `GetSpanRW()`, write to `span[0]`,
  re-read via `GetSpanRO()`, assert value retained (proves span-cast bypasses defensive-copy).
- `EqsCognitiveBuffer_GetSpanRW_NoDefensiveCopy`: same test but also assigns result via direct
  `[InlineArray]` index to a temp copy and asserts it does NOT persist (proves the difference).
- `GlobalComponentIds_EqsSensorAndBufferAreUnique`: assert `EqsSensor != EqsCognitiveBuffer`
  and both are in range 207–255.

---

### Task 2: EqsResultPool Singleton and EqsResultEvent (TASK-EQS-002)

**Files to create:**
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsResultPool.cs` (NEW FILE)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-eqs-002--eqsresultpool-singleton-and-eqsresultevent)

**Design Reference:** Design §3.1; Impl L:185–285

Key implementation points:
- `EqsResultPool`: `[ComponentId(GlobalComponentIds.EqsResultPool)]` singleton with constants
  `MaxConcurrentInFlightResults = 1024`, `MaxTopK = 16`, `PoolCapacity = 16384`; fields
  `NextFreeIndex` (int), `Results` (NativeArray<EqsResult>).
- Ring-buffer wrap logic: `if (handle + count > PoolCapacity) handle = 0` before bulk-copy.
  The returned handle is the pre-adjusted index.
- `EqsResultEvent`: unmanaged struct decorated with `[EventId(N)]` (use the next available
  event ID — check `Hrot` event registry to find the next free slot); fields `SensorNetworkId`
  (long), `Epoch` (int), `RefreshTick` (int), `ResultHandle` (int), `EntryCount` (int).

**GlobalComponentIds.cs addition:**
- `EqsResultPool = 209`

**Tests Required** (add to `EqsResultPool` test class or new `EqsResultPoolTests.cs`):
- `EqsResultEvent_IsUnmanaged`: `Assert.Equal(0, RuntimeHelpers.IsReferenceOrContainsReferences<EqsResultEvent>() ? 1 : 0)` — or use `Unsafe.SizeOf<EqsResultEvent>() > 0`.
- `EqsResultPool_WrapWriteAt16382_WrapsCorrectly`: create `EqsResultPool` with
  `NextFreeIndex = 16382`, call `WriteAndWrap(3 results)` helper — assert `NextFreeIndex == 3`
  (not 16385), and results at indices 0, 1, 2 are the written values.
- `EqsResultPool_WrapWriteExactlyAtEnd_NoWrap`: write count that lands exactly at
  `PoolCapacity` — assert no wrap, `NextFreeIndex == 0` after.

---

### Task 3: DDS Wire Topics and Translator Stubs (TASK-EQS-003)

**Files to create:**
- `Hrot/Network/NED/SimHost/EqsSensorConfigEgressTranslator.cs` (NEW — Muscle-side stub)
- `Hrot/Network/NED/SimHost/EqsSensorConfigIngressTranslator.cs` (NEW — Muscle-side stub)
- `Hrot/Network/NED/SimHost/EqsResultEventEgressTranslator.cs` (NEW — Muscle-side stub)
- `Hrot/Network/NED/Cgf/EqsResultIngressTranslator.cs` (NEW — Brain-side stub)

**DDS topic structs:** Add to `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsDdsTopics.cs` (NEW FILE):
- `EqsSensorConfigTopic` with `[DdsTopic("EqsSensorConfig")]`, `[DdsKey] long EntityId`,
  and all sensor fields. QoS: `Reliability=Reliable, Durability=TransientLocal,
  HistoryKind=KeepLast, HistoryDepth=1`.
- `EqsResultEntry` struct with entity + position + score + flags.
- `EqsResultTopic` with `[DdsTopic("EqsResult")]`, `[DdsKey] long SensorNetworkId`,
  `int Epoch`, `int RefreshTick`, `[DdsManaged] List<EqsResultEntry> Entries`.

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-eqs-003--dds-wire-topics-and-translator-contracts)

**Design Reference:** Design §3.1; Impl L:305–460

For each translator stub:
- Implement the appropriate `IDescriptorTranslator` (or equivalent) interface with
  `ScanAndPublish` / `PollIngress` / `ScanAndPublishAsync` methods that throw
  `NotImplementedException()` (stubs — real logic in TASK-EQS-007).
- Look at existing translators in `Hrot/Network/NED/SimHost/` and `Hrot/Network/NED/Cgf/`
  for the correct base class, constructor signature, and registration pattern.
- Reserve unique `DescriptorOrdinal` values. Check the NED descriptor type registry
  (look at other translators' `DescriptorOrdinal` values) to find the next available slots.

**No tests required** for translator stubs — compile-only objective.

**Build Verification:**
- `dotnet build IOS-IG-SimHost.sln` must succeed without errors.

---

## Testing Requirements

**Minimum test count:** 7 unit tests (TASK-EQS-001 × 4 + TASK-EQS-002 × 3).

**Quality standard — these are the ONLY acceptable test types:**
- Tests that verify ACTUAL struct sizes (`Marshal.SizeOf`)
- Tests that verify ACTUAL memory write/read round-trips (compile and read back values)
- Tests that verify ACTUAL wrap arithmetic (write at known index, assert resulting index)

**NOT ACCEPTABLE:**
- Tests that only check an object is not null
- Tests that verify a constant equals a hardcoded number (e.g. `Assert.Equal(207, EqsSensor)`)
- Tests without meaningful assertions

---

## Quality Standards

**CODE QUALITY:**
- No `#nullable enable` violations.
- All structs that must be unmanaged: verify with Roslyn or a test that uses `where T : unmanaged`.
- Component IDs must be registered BEFORE any struct references them (see existing pattern).
- Keep the `Fdp.Toolkit.Spatial.Eqs` namespace consistent with existing files.

**NO LAZINESS:** Complete every task fully. Run builds and tests. Fix all errors at root cause.
Do not stop mid-batch to ask permission for obvious steps. You are expected to deliver a
working, tested implementation without interruption.

---

## Success Criteria

This batch is DONE when:
- [ ] `EqsResult`, `EqsResultArray`, `EqsCognitiveBuffer`, `EqsSensor` defined and correct
- [ ] `Marshal.SizeOf<EqsResult>() == 24` test passes
- [ ] `GetSpanRW()` write-persistence test passes
- [ ] `EqsResultPool` singleton and `EqsResultEvent` defined and correct
- [ ] Pool wrap-at-16382 test passes
- [ ] `EqsSensorConfigTopic`, `EqsResultTopic`, `EqsResultEntry` DDS structs compile
- [ ] All four translator class stubs compile against `IDescriptorTranslator`
- [ ] `GlobalComponentIds` has `EqsSensor = 207`, `EqsCognitiveBuffer = 208`, `EqsResultPool = 209`
- [ ] `dotnet build IOS-IG-SimHost.sln` succeeds without errors
- [ ] All 7+ unit tests pass
- [ ] Report submitted

---

## Common Pitfalls to Avoid

- **`[InlineArray]` defensive copy:** Direct index assignment (`buffer.Results[0] = x`) silently
  discards writes. You MUST use `GetSpanRW()` via `MemoryMarshal.CreateSpan`. This is the
  single most critical constraint in this batch — test it explicitly.
- **Rejection sentinel vs. zero:** `EntityId = -1L` is the rejection sentinel; `EntityId = 0`
  is a valid placeholder for positional candidates. Don't mix them up.
- **Epoch vs. tick:** `EqsResultEvent.Epoch` is a version counter on `EqsSensor`, NOT a
  simulation tick number.
- **DDS topic name stability:** Topic names MUST be exactly `"EqsSensorConfig"` and
  `"EqsResult"` — wire stability depends on this.
- **DescriptorOrdinal uniqueness:** Check existing translators before reserving ordinals.

---

## Reference Materials

- **Task Defs:** `.dev/eqs-2/TASK-DETAIL.md` — TASK-EQS-001, TASK-EQS-002, TASK-EQS-003
- **Design:** `.dev/eqs-2/EQS_Design_v1.3_final.md` — §2, §3.1, §8, §8.1
- **Impl Details:** `.dev/eqs-2/IMPLEM_DETAILS.md` — L:3–170, L:185–285, L:305–460
- **Existing pattern (ECS components):** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/AreaQueryBatchData.cs`
- **Existing translator pattern:** `Hrot/Network/NED/SimHost/` — any existing `*Translator.cs`
- **GlobalComponentIds:** `FDP/Engine/Fdp.Core/GlobalComponentIds.cs`

---

## Developer Insights

**Q1:** What issues did you encounter implementing the `[InlineArray]` accessor? How did you
verify the defensive-copy problem and confirm your fix works?

**Q2:** Did the DDS translator registration reveal any issues with the existing descriptor
ordinal registry? What ordinal values did you reserve and how did you find the next free slots?

**Q3:** What design decisions did you make beyond the spec? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Suggested commit message for this batch?
