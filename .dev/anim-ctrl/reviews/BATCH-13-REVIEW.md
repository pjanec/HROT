# BATCH-13 Review

**Batch:** BATCH-13  
**Reviewer:** Development Lead  
**Date:** 2026-05-27  
**Status:** CHANGES REQUIRED

---

## Findings (Ordered by Severity)

### P1-1: ANC-P6-04 partial serialization requirement is not implemented

**Why this is blocking:** DD-2 requires shipping only live queue entries (`Count * 16`), but current implementation always carries the full 128-byte entries block.

**Evidence:**
- `DdsMontageQueue` has fixed `EntriesData[128]` (full capacity on every sample).
- `AnimationMontageQueueEgressTranslator` copies all 128 bytes every publish.
- Test coverage validates only fixed struct size budget (`<= 160`), not the required `Count=3 => ~60B` payload behavior.

**Impact:** Bandwidth and design contract mismatch for side-buffer replication. ANC-P6-04 success criteria are not met.

### P1-2: LookAt entity intent ingress does not resolve entity-ref params through NetworkEntityMap

**Why this is blocking:** DD-2 specifies `LookAtEntity` payload entity refs must be resolved on ingress through `NetworkEntityMap`.

**Evidence:**
- `LookAtChannelIntentIngressTranslator` copies raw params bytes directly into channel params.
- No conversion/remap path for `LookAtEntityParams.TargetEntityId` is implemented.
- No tests cover network-id to local-entity mapping behavior for `LookAtEntity` intents.

**Impact:** Cross-node LookAt entity targeting can bind to invalid or wrong entity IDs on receiving node. ANC-P6-02 is not complete.

### P1-3: New test project has an invalid project reference path

**Why this is blocking:** The new test project emits a build warning due to a bad reference path, violating clean-build expectations.

**Evidence:**
- `Hrot.Animation.Replication.Tests.csproj` references `..\\..\\..\\..\\FDP\\Engine\\Fdp.Core\\Fdp.Core.csproj` which does not exist from that location.
- `dotnet test Hrot/Subsystems/Hrot.Animation.Replication.Tests/Hrot.Animation.Replication.Tests.csproj -c Debug` reports `MSB9008` warning.

**Impact:** Warning hygiene regression in the newly added project; must be fixed before approval.

### P2-1: ANC-P6-06 QoS requirements are not verified by tests

**Why this matters:** Batch requirements call out QoS correctness (Reliable + TransientLocal for state topics, Reliable + Volatile for events). Current tests only verify topic names and direction counts.

**Evidence:**
- `AnimationReplicationModule` only builds a translator list; no QoS assertions exist in tests.
- `AnimationReplicationModuleTests` validate topic set membership/count but do not validate durability/reliability behavior.

**Impact:** High risk of silent QoS misconfiguration escaping CI.

---

## Test Quality Assessment

Positive:
- Field-level encode/decode checks exist for all event translators.
- Multiple negative-path tests exist for dirty filters.

Gaps:
- Side-buffer test named for partial payload behavior does not assert partial payload length semantics.
- No test exercises LookAt entity-ID remap semantics (the highest-risk behavior in cross-node intent ingestion).
- No QoS-level verification despite ANC-P6-06 requirement.

Conclusion: test suite is substantial in count, but not yet aligned with the highest-value behavioral risks in DD-2.

---

## Verdict

**Status: CHANGES REQUIRED**

BATCH-13 is not approved. ANC-P6 tasks remain incomplete pending corrective work for:
1. Queue partial serialization contract.
2. LookAt entity-ref remap on ingress.
3. Test-project warning cleanup.
4. QoS verification coverage.

---

## Next Batch

Proceed with **BATCH-14 (Corrective): ANC-P6-01..06 completion + test-hardening**.

No approval commit for BATCH-13.
