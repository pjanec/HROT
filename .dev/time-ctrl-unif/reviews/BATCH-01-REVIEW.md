# BATCH-01 Review

**Batch:** BATCH-01 — CQRS Message Layer Foundation  
**Tasks:** TCU-M001, TCU-M002, TCU-T005  
**Reviewer:** Dev Lead  
**Date:** 2026-04-01

---

## ✅ Verdict: APPROVED

All three tasks are complete and correct. Tests pass. No rework required.

---

## Review Findings

### Scope Check

| Task | Expected | Result |
|------|----------|--------|
| TCU-M001 — Fix Wire DTOs | All structs to plain fields; add new fields; update ToWire/ToEvent | ✅ Done. `SetTimeScaleDescriptor` was also migrated (correct judgement call). |
| TCU-M002 — Domain Messages | New `TimeLocalEvents.cs` with 2 plain-field structs; no attributes | ✅ Done. Exact spec compliance. |
| TCU-T005 — Tests | 6+ tests asserting values and round-trips | ✅ 9 tests, all asserting specific field values. |

### Design Alignment

- Wire DTOs converted to plain fields — ✅
- `[DdsId(N)]` ordinals preserved — ✅
- `[DdsTopic]` remains on `TimePulseDescriptor` and `SwitchTimeModeWireDto` — ✅
- `[MessagePackObject]`/`[Key(N)]` remain on local structs — ✅
- No serialisation attributes on `TimeLocalEvents.cs` types — ✅
- `TargetSimTime` at `[Key(4)]`/`[DdsId(4)]` (not `[3]` as spec table said) — ✅ **Correct:** `TimeScale` already occupies `[3]`; spec table was written before that field was added. Constraint "do not renumber" takes precedence.
- `FdpEventBus.PublishManaged`/`ConsumeManaged` used for domain types — ✅ Legitimate workaround since `[EventId]` is forbidden on domain types per spec.

### Test Quality Assessment

Tests in `TimeMessagesTests.cs` are **behavioural**: they assert specific values, not "no exception". The reflection tests guard structural invariants. The round-trip test validates all fields end-to-end. **Quality is high.**

The `FutureBarrierTests.cs` regression fix (GetProperty → GetField) is correct and essential.

### Code Quality

No silent error swallowing, no dead code. Clean implementation.

---

## Debt Tracker Updates

Promoting developer findings to DEBT-TRACKER.md:

- **DT-001 (P3):** `FdpEventBus.Publish<T>()` enforces `[EventId]` with no documented opt-out path for in-process-only types. Domain types must use `PublishManaged/ConsumeManaged`. This distinction is undocumented at the call site. Target: future documentation batch or FdpEventBus improvement.
- **DT-002 (P3):** TASK-DETAIL.md spec table for `FrameOrderDescriptor` says add `TargetSimTime` at `[Key(3)]` but `TimeScale` already occupies that ordinal. The spec table is stale. No code impact (ordinal correctly assigned at `[Key(4)]`). Target: update TASK-DETAIL.md in-place in a later maintenance batch.

---

## Suggested Git Commit Message

```
feat(TCU-M001/M002/T005): convert wire DTOs to plain fields, add domain events and tests

- All wire DTO structs converted from C# properties to plain fields
- FrameOrderDescriptor gains TargetSimTime at [Key(4)]/[DdsId(4)]
- SwitchTimeModeWireDto/Event gain SimTimeSnapshot and TimeScale
- ToWire()/ToEvent() helpers updated to map new fields
- New Domain/TimeLocalEvents.cs: AdvanceFrameIntent + FrameStepCompletedEvent (no DDS attrs)
- New TimeMessagesTests.cs: 9 tests covering round-trips and field invariants
- Fixed FutureBarrierTests regression: GetProperty → GetField after field migration
```

---

## Next Batch

**BATCH-02** should cover:
- **TCU-MC001** — MasterSyncController (Phase 2)
- **TCU-T001** — Unit Tests: MasterSyncController (Phase 6)

Estimated effort: 6–8 hours.
