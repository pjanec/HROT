# BATCH-01 Review

**Batch:** BATCH-01
**Reviewer:** Development Lead
**Date:** 2026-05-23
**Status:** ✅ APPROVED (with 1 P1 corrective task for BATCH-02)

---

## Summary

Phase 1 (ID type widening + config) and Phase 2 (BitMask512, EntityMetadataCold) fully
implemented. 764 tests pass, 2 skipped (pre-existing benchmark skips), 0 failures. Code
quality is high. One critical silent test regression must be fixed in BATCH-02.

---

## Issues Found

### Issue 1 (P1 — Corrective Task for BATCH-02): `GlobalComponentIds_NoToolkitBlockDuplicates` checks zero fields

**File:** `FDP/Engine/Fdp.Core.Tests/ComponentIdAttributeTests.cs` (Line 192)
**Problem:**
```csharp
.Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(byte))
```
After TASK-E001 widened all constants from `const byte` to `const int`, `f.FieldType == typeof(byte)` matches zero fields. The test iterates over an empty list and passes vacuously — the duplicate-detection safety net is silently disabled. This is critical: a future developer adding a duplicate component ID would get no warning.

**Fix required in BATCH-02:**
- Change `f.FieldType == typeof(byte)` → `f.FieldType == typeof(int)`
- Change `var value = (byte)field.GetRawConstantValue()!` → `var value = (int)field.GetRawConstantValue()!`
- Update `Dictionary<byte, string>` → `Dictionary<int, string>`

### Issue 2 (P2 — tracked in DEBT-TRACKER): `BitMask512` missing `Pack=64`

**File:** `FDP/Engine/Fdp.Core/BitMask512.cs` (Line 14)
**Problem:**
```csharp
[StructLayout(LayoutKind.Explicit, Size = 64)]
```
`BitMask256` declares `Pack=32` ensuring AVX2-aligned vector loads. `BitMask512` omits `Pack=64`. When instances appear in arrays (hot path in Phase 3), unaligned 32-byte vector loads may incur a cycle penalty on some microarchitectures.

**Fix:** Add `Pack = 64` before Phase 3 integration. Deferred to BATCH-02 so it's in place before the `EntityIndex` hot table is created.

---

## Positive Findings

- **Test quality: excellent.** All new tests verify actual behavior (byte sizes via `Unsafe.SizeOf`, exact bit values at all 8 boundary indices, exception types, quad-boundary cross-checks). No string-presence or compilation-only tests.
- **BitMask512 boundary testing**: explicitly covers bits 0, 63, 64, 127, 255, 256, 383, 511 with bleed checks across quad boundaries — the most important category.
- **AVX2 two-stage path** correctly implemented: lower-half early-return pattern matches DESIGN.md spec.
- **EntityMetadataCold layout** matches design exactly; `SetActive` flag-isolation test is correct.
- **Scope decision** (upgrading QueryBuilder/EntityQuery masks to BitMask512 in BATCH-01) was correct and necessary given the TASK-E002 success criterion; the compatibility overloads are clearly documented and will be cleaned up in Phase 3.
- **Developer insights** are high-quality and actionable (alignment, cross-project paranoid-mode, vacuous test).

---

## Suggested Git Commit Message

```
feat(ecs): Phase 1+2 prerequisites, BitMask512, EntityMetadataCold (BATCH-01)

TASK-E001: Widen component ID type byte->int
- ComponentIdAttribute.Id: byte -> int; constructor parameter widened
- GlobalComponentIds: all const byte -> const int (values unchanged)
- ID block 256-511 reserved for future components

TASK-E002: Engine capacity constants + QueryBuilder guard
- FdpConfig.MAX_COMPONENT_TYPES: 256 -> 512
- FdpConfig.FORMAT_VERSION: 4 -> 5
- QueryBuilder/EntityQuery internal masks upgraded from BitMask256 to
  BitMask512 to satisfy WithComponentId(256-511) test requirement

TASK-E003: New BitMask512 (64 bytes / one L1 cache line)
- 8 ulong fields at LayoutKind.Explicit offsets 0-56
- AVX2 two-stage path: lower 256 bits first, upper 256 bits second
- Scalar fallback: interleaved include/exclude checks per quad
- FDP_PARANOID_MODE bounds guards for SetBit/ClearBit/IsSet
- Phase-2 compatibility overloads HasAll/HasAny(BitMask256, BitMask512)

TASK-E004: New EntityMetadataCold (128 bytes / 2 cache lines)
- AuthorityMask (BitMask512 at offset 0), Generation, Flags,
  LastChangeTick, DisType, LifecycleState
- IsActive/SetActive operate on Flags bit 0 without touching other bits

Tests: 764 passed, 2 skipped, 0 failed
```
