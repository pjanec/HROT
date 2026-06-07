# BATCH-52 Review

**Status:** APPROVED WITH REVIEWER FIX
**Reviewer fix applied:** Updated `PendingMutationTests.cs` to assert `Unsafe.SizeOf<TestHealth>()` instead of `Marshal.SizeOf<TestHealth>()`.

---

## Implementation Review

### P11T8 — `StageMutation` size via ECS registry ✅

- `_componentSizeCache` is correctly placed as a `private static readonly` field — shared across all instances, consistent with the pattern.
- `GetEcsComponentSize` uses `lock (_componentSizeCache)` — correct for a static mutable dictionary.
- Reflection path (`ComponentType<>.Size` via `GetProperty`) is only taken on first call per type; subsequent calls return from cache.
- `StageMutation` now uses `GetEcsComponentSize(componentType)` — correct fix.

**Issue found and fixed by reviewer:** `PendingMutationTests.Stage_UnmanagedStruct_StoresSizeAndClassification` (line 33) was asserting `Marshal.SizeOf<TestHealth>()` as expected — this was the old contract (interop size). After P11T8, the code stores `Unsafe.SizeOf<TestHealth>()`. For `TestHealth` (single `int` field), both are equal, so the test passed, but it was asserting the wrong property. Fixed: changed to `Unsafe.SizeOf<TestHealth>()` and swapped the `using System.Runtime.InteropServices` import for `using System.Runtime.CompilerServices`.

### P11T10 — Compiled spatial position accessor ✅

- `SpatialPositionDelegate<T>` delegate declared at namespace scope as `internal` — correct.
- `CompileSpatialPositionAccessorGeneric<T>`: expression tree pattern mirrors `PredicateCompiler.BuildUnmanagedMatcher<T>` exactly. `GetComponentRO<T>` used instead of the old `Marshal.PtrToStructure` + `FieldInfo.GetValue` path — zero allocation per call.
- `_spatialTrackers` tuple correctly extended to 4 elements.
- `TryMountDelegate` case for `SpatialBoundingPredicateDto` compiles and stores accessor at mount time.
- `EvaluateSpatialTrackers` uses compiled accessor when non-null; falls back to `ReadPosition2D` for managed components.
- `ReadPosition2D`/`ReadFloatField` retained as fallback — correct per instructions.
- The `catch { return null; }` in `CompileSpatialPositionAccessor` swallows compilation errors silently; the developer noted this as a weak point. This is acceptable for now (falls back to the slow path) but a debug log would improve visibility in future.

**Deviations from instructions (all correct):**
- Batch instructions had wrong DTO field names (`MinX/MaxX` → real API is `Bounds = new BoundingBox2D { Min, Max }`). Developer read the actual source and used correct API. ✅
- `CompileSpatialPositionAccessorGeneric<T>` not marked `unsafe` — the method doesn't need it (`Unsafe.AsRef` is accessible without an `unsafe` block in .NET 6+). ✅

### P11T13 — Lifecycle `NetworkId` throws ✅

- `MatchesLifecycleCriteria` now throws `NotSupportedException` with a clear actionable message for `EntityIdentifierType.NetworkId`. The wildcard `_ => false` arm is kept for unknown enum values — correct.
- XML doc updated with `<exception cref="NotSupportedException">` — correct.

**Deviation from instructions (correct):**
- Batch instructions test used non-existent `EventType = LifecycleEventType.Spawned` on `LifecyclePredicateDto`. Developer read the actual DTO definition and removed the non-existent property. The test still exercises the NetworkId throw path via `IdentifierType = EntityIdentifierType.NetworkId`. ✅

---

## Test Quality Assessment

### `StageMutationSizeTests` — ADEQUATE
- `StageMutation_SimpleStruct_SizeMatchesUnsafeSizeOf`: Verifies `SizeBytes == Unsafe.SizeOf<TestHealth>()`. Correct contract test.
- `StageMutation_StagedSize_EqualsManagedSize_NotInteropSize`: Redundant with test 1 but documents the intended behavior with a comment about fixed-buffer components. Acceptable as documentation.
- **Weakness:** Neither test exercises a component where `Marshal.SizeOf ≠ Unsafe.SizeOf` (fixed-buffer struct). This is acceptable because such components would require `unsafe` test code and a registered component ID. The comment in the test documents the gap clearly.

### `SpatialPositionAccessorTests` — GOOD
- Both tests verify observable behavior (`IsPaused`) rather than implementation details.
- Test 1 verifies inside-bounds fires (entry detection on first evaluation). Test 2 verifies outside-bounds does not fire. Together they cover the core compiled-accessor correctness.
- **Could be stronger:** A third test verifying that `exit` fires when an entity moves outside bounds after initially being inside, to exercise the full state machine. However, the two tests are sufficient to verify the compiled accessor works correctly.

### `LifecycleNetworkIdTests` — GOOD
- Single focused test verifying the `NotSupportedException` is thrown rather than `false` returned.
- Correct use of `Assert.Throws<NotSupportedException>`.

---

## Verified Counts

- Build: 0 errors, 0 warnings
- Breakpoints unit tests: 124 passed (124/0)
- Integration wiring tests: 20 passed (per report)

---

## Reviewer Fix Applied

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/PendingMutationTests.cs`
- Changed `using System.Runtime.InteropServices;` → `using System.Runtime.CompilerServices;`
- Changed `Assert.Equal(Marshal.SizeOf<TestHealth>(), m.SizeBytes)` → `Assert.Equal(Unsafe.SizeOf<TestHealth>(), m.SizeBytes)` on line 33

This aligns the existing P4T1 test with the new P11T8 contract.
