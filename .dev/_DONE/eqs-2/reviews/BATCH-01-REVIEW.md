# BATCH-01 Review

**Batch:** BATCH-01
**Reviewer:** Development Lead
**Date:** 2026-05-24
**Status:** ⚠️ APPROVED WITH CORRECTIVE TASK IN BATCH-02

---

## Summary

Core EQS data model (components, pool, event, DDS topics, translator stubs) is correctly
implemented and all 7 EQS unit tests pass. Build is clean. One P1 type mismatch must be
fixed in BATCH-02 as Corrective Task 0.

---

## Issues Found

### Issue 1 (P1): `EqsResultEvent.Epoch` and `RefreshTick` are `int`, should be `uint`

**File:** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsResultPool.cs` (lines 68–69)

**Problem:** `EqsResultEvent.Epoch` and `EqsResultEvent.RefreshTick` are declared as `int`.
IMPLEM_DETAILS.md L:243–247 explicitly shows `uint Epoch` and `uint RefreshTick`. The mismatch
causes:
- The staleness check `evt.Epoch != sensor.Epoch` compares `int` against `EqsSensor.Epoch` (uint)
  — implicit sign-widening, potential silent mismatch near `uint.MaxValue`.
- The DDS translator (TASK-EQS-007) must cast `int → uint` when writing to `EqsResultTopic`
  (which correctly uses `uint Epoch`).
- The root cause is a batch instruction error (spec said `(int)` — that was wrong).

**Fix for BATCH-02 Corrective Task 0:**
```csharp
public uint Epoch;      // was int
public uint RefreshTick; // was int
```

### Issue 2 (P3): `GetSpanRW_NoDefensiveCopy` test doesn't reproduce the actual compiler bug

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EqsComponentLayoutTests.cs` (lines 48–74)

**Problem:** The test demonstrates struct copy semantics (mutating a local copy doesn't
affect the original), not the actual `[InlineArray]` defensive-copy trap described in Design §8.1.
The real bug occurs when writing through a readonly-qualified receiver (e.g., `in` parameter,
`readonly struct`, or a component accessed via `ref readonly`). The test would not catch a
regressed implementation in that scenario.

**Note:** P3 — the key property tested (GetSpanRW writes persist) is correct. Track as debt.

---

## Test Quality Assessment

All 7 EQS tests verify actual behavior:
- `EqsResult_SizeIs24Bytes` — real `Marshal.SizeOf` check ✅
- `EqsCognitiveBuffer_GetSpanRW_WritePersists` — real round-trip write+read ✅
- `EqsCognitiveBuffer_GetSpanRW_NoDefensiveCopy` — correct key property, weak scenario (P3) ⚠️
- `GlobalComponentIds_EqsSensorAndBufferAreUnique` — real uniqueness and range check ✅
- `EqsResultEvent_IsUnmanaged` — compile-time constraint + size check ✅
- `EqsResultPool_WrapWriteAt16382_WrapsCorrectly` — actual ring-buffer wrap with value checks ✅
- `EqsResultPool_WrapWriteExactlyAtEnd_NoWrap` — boundary condition with value checks ✅

**Pre-existing failure count:** The report states "1 pre-existing failure" but actual run shows
54 pre-existing failures (ReplayBrowser, Combat, Geographic, Replication, etc.). These are all
unrelated to EQS. Report accuracy must improve — always state the actual count.

---

## Commit Message

```
dev: BATCH-01 complete -- Phase 1 EQS foundations (EQS-001, EQS-002, EQS-003)

Core data model:
- EqsComponents.cs: EqsResult (24B), EqsResultArray ([InlineArray(16)]),
  EqsCognitiveBuffer with GetSpanRW/GetSpanRO bypassing [InlineArray] defensive-copy trap,
  EqsSensor component
- EqsResultPool.cs: EqsResultPool singleton with WriteAndWrap ring-buffer helper,
  EqsResultEvent [EventId(2050)] (NOTE: Epoch/RefreshTick fixed to uint in BATCH-02)
- GlobalComponentIds: EqsSensor=207, EqsCognitiveBuffer=208, EqsResultPool=209

DDS stubs (compile-only, logic deferred to TASK-EQS-007):
- EqsDdsTopics.cs: EqsSensorConfigTopic, EqsResultEntry, EqsResultTopic
- 4 translator stubs (EqsSensorConfigEgress, EqsSensorConfigIngress,
  EqsResultEventEgress, EqsResultIngress)
- AllDescriptors: dtEqsSensorConfig=95, dtEqsResult=96

Tests: 7 EQS tests passing
Build: dotnet build IOS-IG-SimHost.sln -> succeeded, 0 errors
```
