# BATCH-01 Review

**Reviewer:** Dev Lead
**Batch:** BATCH-01 — Stage 1 Foundation (RB-1.0, RB-1.1, RB-1.2, RB-1.3)
**Decision:** APPROVED with P2 issue carried into BATCH-02

---

## Overall Assessment

Solid foundational batch. All four tasks are implemented, 8 new tests added, all passing, zero regressions. The codebase remains headless (no `Fdp.Presentation` / Raylib references). Code quality is good: fluent harness API, proper disposal patterns, well-organized test namespaces. APPROVED to proceed.

---

## Task-by-Task Assessment

### RB-1.0 — Codebase Audit ✅

- `ComponentTypeRegistry.GetAllRegistered()` correctly exposes a snapshot of the internal map.
- `EventType.GetAllRegistered()` is correctly implemented via a public static gateway class in `EventType.cs`.
- `HasComponentByTypeId` already existed as a public instance method on `EntityRepository` — good discovery, no redundant extension method created.
- Audit tests register two types, clear the registry first, and verify both appear. **Positive and negative cases for `HasComponentByTypeId` are present.** ✅

### RB-1.1 — `FdpRecordingHarness` ⚠️ (P2 issue — see below)

- The harness is well-designed: fluent API, `AsyncRecorder` usage is correct, `SwapBuffers()` called after each capture, proper `IDisposable` cleanup, and double-dispose guarded.
- The self-test verifies frame count (5), frame types (Keyframe + 4 Deltas), and wall clock ticks sequence. **PASS on structure.**
- **P2: The self-test does NOT step through frames and verify content.** The spec requires "asserts byte-level equality of those features" — i.e., verifying that the destroyed entity appears in the destruction log on frame 3, and that the unmanaged + managed events are readable on frame 4, by calling `PlaybackController.StepForward(repo)` and reading the event bus / destruction log. The current test only validates frame metadata, not frame payload. This must be fixed in BATCH-02.

### RB-1.2 — Domain DTOs ✅ (with P3 note)

- `JsonExportOptions` defaults test is exhaustive — all fields checked. ✅
- Round-trip test covers all scalar fields correctly.
- **P3: `List<Entity>` round-trip is not tested with actual entities** because `Entity` has no `[JsonConstructor]` / custom converter. The workaround (empty list) is acceptable for now. BATCH-02 should add a `[JsonConstructor]` to `Entity` or a `JsonConverter<Entity>` in the `Fdp.Core` serialization layer, then re-test with non-empty entity list.

### RB-1.3 — Service Contract ✅

- Interface signature matches DESIGN.md §3.3 exactly.
- Stub throws `NotImplementedException` as required.
- Assembly-reference test correctly validates no `Fdp.Presentation` or Raylib reference in the toolkits assembly. ✅

---

## Issues Summary

| ID | Priority | Description | Projected To |
|----|----------|-------------|--------------|
| RB01-P2-001 | P2 | `HarnessSelfTest` does not verify frame payload: destruction log on frame 3 and events (unmanaged + managed) on frame 4 are never read back and asserted. Must add `StepForward` playback loop with destruction log check and event bus read assertions. | BATCH-02 Corrective Task 0 |
| RB01-P3-001 | P3 | `JsonExportOptions` round-trip test does not exercise `List<Entity>` with actual entities due to missing `[JsonConstructor]` on `Entity`. | BATCH-02 or BATCH-03 |

---

## Debt Tracker Updates

P2 → Corrective Task 0 in BATCH-02 (never enters the debt tracker).
P3-001 → Added to DEBT-TRACKER.md.

---

## Commit Approval

The batch is approved. Suggested commit message:

```
BATCH-01: Stage 1 foundation — registry APIs, recording harness, DTOs, service contract

RB-1.0: Add ComponentTypeRegistry.GetAllRegistered() and EventType.GetAllRegistered() accessors
RB-1.1: FdpRecordingHarness test substrate (AsyncRecorder-backed, IDisposable, fluent API)
RB-1.2: JsonExportOptions, ExportWindowMode, ExportFormatMode, ChangelogEntryDto, DiffNode stub
RB-1.3: IRecordingExportService interface + NotImplementedException stub
Tests: 8 new tests all green; 0 regressions
```
