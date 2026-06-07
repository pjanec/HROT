# BATCH-14 REVIEW — APPROVED

**Batch:** BATCH-14
**Tasks:** EQS-035, EQS-036
**Reviewer:** Dev Lead
**Verdict:** APPROVED (after corrective fix)

---

## Summary

BATCH-14 implemented context-slot support on `EqsSensor` (EQS-035) and generalized LOS tests
to read threat position from context slots instead of `TargetMemory[0]` (EQS-036). The
implementation is correct and all 7 new tests (T-CS1–CS7) pass. D-02 from the debt tracker is
resolved.

**One P1 issue was found during review:** The developer migrated `CoverGeneratorAndLosTests`,
`AccurateLosTests`, `EqsFlagsMeaningfulTests`, `AccurateLosPhaseTests`, and `FindCoverFromTargetTests`
but missed migrating two tests in `EqsRoundTripTests.cs` that exercise `CheapLineOfSightTest`
with a non-null threat setup. Both tests were fixed directly by the dev lead as a corrective
action within this review cycle.

---

## Corrective Fix Applied

**File:** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsRoundTripTests.cs`

**Tests fixed:**
1. `Eqs_ThreatThreshold_AboveThreshold_RejectsAllExposedCandidates` (T-RT3a)
   — Added `threatEntity` with `SimTransform.Position = (30, 0, 0)` and set `sensor.ContextSlot1 = threatEntity`.
   — Threat score 100 > threshold 50 → LOS filter now runs → `ExposedLosServiceMock` → all rejected → `Count == 0` ✓

2. `Eqs_ThreatThreshold_BelowThreshold_BypassesFilter` (T-RT3b)
   — Also added `threatEntity` + `sensor.ContextSlot1`. Was passing for the wrong reason (null-slot bypass).
   — Now correctly tests: score 10 < threshold 50 → threshold gate fires → bypass → `Count == 1` ✓

---

## Test Results After Fix

```
Passed! - Failed: 0, Passed: 50, Skipped: 0, Total: 50
```

---

## Debt Tracker Updates

- **D-02** (BATCH-13): RESOLVED — `CheapLineOfSightTest` now sets `FlagsMeaningful |= 1` on
  both exposed and covered paths.

---

## Implementation Quality Assessment

### EQS-035: Context slots on EqsSensor

- `EqsSensor` struct correctly adds `Entity ContextSlot0/1/2` fields
- DDS topic adds `long ContextSlot0/1/2NetworkId` (network-ID encoding, not entity pairs)
- Egress translator serializes slots via `GetNetworkId`
- Ingress translator resolves network IDs back to entities via `ResolveSlot` helper
- `EqsParams` BTree struct mirrors all three slots
- `Action_MaintainEqsSensor` detects slot changes and increments `Epoch` — correct

### EQS-036: LOS test generalization

- `CheapLineOfSightTest` reads threat position from `sensor.ContextSlot[n].SimTransform.Position`
- `ContextSlotIndex` property (default=1) — configurable
- Null-slot bypass is correct sentinel behavior
- No-`SimTransform` bypass is correct defensive behavior
- `FlagsMeaningful |= 1` set on BOTH exposed and covered paths — D-02 fix confirmed
- `AccurateLineOfSightTest` has same treatment with `ContextSlotIndex` — consistent

### New Tests (T-CS1–CS7)

All well-designed and aligned with the design. Cover:
- Cheap LOS reading from slot (T-CS5, T-CS6)
- Null-slot bypass (T-CS5 variant)
- Accurate LOS reading from slot (T-CS1)
- Round-trip DDS slot preservation (T-CS2, T-CS3)
- Unresolved entity stays null (T-CS3)
- Epoch increment on slot change (T-CS4)
- Null entity survival in buffer (T-CS7)
