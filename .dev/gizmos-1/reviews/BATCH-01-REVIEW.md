# BATCH-01 Review

**Batch:** BATCH-01
**Reviewer:** Dev Lead
**Decision:** APPROVED

---

## Summary

All four Phase 1 tasks are complete. Build is clean. 52 new tests pass. The review below focuses on test quality per standing instructions.

---

## Test Quality Review

### Coverage breadth

All success conditions from TASK-DETAIL.md are exercised:

- **SC-GZ001-x**: Rgba32 size/channels/constants/equality; enum ordinal values for PipelineTarget, DebugPrimitiveShape, ScreenAnchor, CoordinateSpace, SizeMode; PickToken IsValid logic.
- **SC-GZ002-x**: DebugPrimitive size=64 verified with `Marshal.SizeOf`; each factory method checked for correct Shape, payload fields, and header values; `Thickness` floating-point round-trip; `Anchor` entity reconstruction; BadgeRichText/IconAtlasCoord aliasing confirmed by writing one and reading the other.
- **SC-GZ003-x**: All eight IDebugDrawBuilder methods produce primitives with expected shape/payload; gradient test verifies StartColor != EndColor; EntityLocal test verifies Space=EntityLocal and AnchorIndex/Generation round-trip; capacity overflow test fills `cap + 2` slots and asserts `DroppedCount == 2` with frame capped at `cap`; Clear resets both counters.
- **SC-GZ019-x**: Fnv1a32 determinism, empty-string returns FNV offset basis, different inputs give different hashes; Intern idempotency (first value wins); TryResolve returns null for unknown hash; Flush clears entries; DrawTextLong sets StringHash, positions, and shape, and full text recoverable from InternMap; second DrawTextLong with same text leaves map at count=1.

### Test depth — specific observations

**Offset isolation tests (DebugPrimitiveTests)**
`PayloadIsolation_LineDoesNotCorruptHeader` and `PayloadIsolation_SphereDoesNotCorruptHeader` verify that header bytes (Shape, Color, TargetView, DebugLayer, ThicknessU16) are undisturbed after payload fields are written. This is the critical correctness check for the explicit-layout struct and is properly exercised.

**Alias tests**
`BadgeRichText_AliasesTextContent` and `IconAtlasCoord_AliasesTextContent` write via the alias property and read back via `TextContent` (and vice versa). This directly validates the memory-overlay design without relying on implementation knowledge.

**Threshold boundary**
`Buffer_CapacityOverflow_DropsExtraPrimitives` uses `cap + 2` to assert both `DroppedCount == 2` and `GetFrame().Length == cap`. This is a precise boundary test, not just a "does not throw" check.

**Fnv1a32 empty-string baseline**
The test asserts the exact FNV-1a offset basis (2166136261u) for an empty string. This pins the implementation to the standard algorithm and would catch any accidental modification.

### Minor issues noted (non-blocking)

1. **Thread-safety of `DrawTextLong`**: `DebugPrimitiveBuffer` uses `Interlocked.Increment` for slot allocation, but `StringInternMap` uses an unsynchronized `Dictionary`. If two threads call `DrawTextLong` with the same hash simultaneously, the `ContainsKey` + index assignment in `Intern` could race. This is acceptable for a single-threaded-write scenario (the intended usage in a system update), but should be documented. Added to DEBT-TRACKER.md.
2. **`GetFrame()` when `_count > capacity`**: The implementation correctly uses `Math.Min(_count, _primitives.Length)`. Tested indirectly via the overflow test. A direct unit test asserting `GetFrame().Length == capacity` when `_count > capacity` would make this invariant explicit — but the existing overflow test covers it adequately.

---

## Production Code Quality

- `DebugPrimitive` comments correctly explain the Icon layout deviation (2D world position) and the StringHash/AnchorIndex overlay semantics.
- `DebugPrimitiveBuffer` comments explain the thread-safety model and the inline-preview behavior of `DrawTextLong`.
- No over-engineering: `StringInternMap` is a thin dictionary wrapper with no unnecessary abstraction.
- Namespace is consistently `Fdp.Toolkit.Diagnostics.Gizmos` across all files.

---

## Debt Logged

Added to `.dev/gizmos-1/DEBT-TRACKER.md`:
- StringInternMap is not thread-safe; document single-writer requirement or add lock in a follow-up.
