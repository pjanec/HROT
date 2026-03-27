# ATTR2-BATCH-02 Developer Report

**Batch:** ATTR2-BATCH-02  
**Date:** 2025-07  
**Status:** ✅ Complete — all tasks implemented, all tests passing

---

## Task Completion Summary

| Task ID           | Title                              | Status |
|-------------------|------------------------------------|--------|
| CORRECTIVE-0      | Fix `AttributeValueUnion` DDS union | ✅ Done |
| ATTR2-P2T1        | `JsonToRecordCompiler` + Builder   | ✅ Done |
| ATTR2-P2T2        | `BuildEdgeCompiler()` in factory   | ✅ Done |
| ATTR2-P3T1        | `BinaryInterpreter` core           | ✅ Done |
| ATTR2-P4T1        | `EntityDataAttributeInstaller`     | ✅ Done |
| ATTR2-P4T2        | `SimTransformAttributeInstaller`   | ✅ Done |
| ATTR2-P4T3        | `BuildBinaryInterpreter()` wiring  | ✅ Done |
| Tests             | All new test files written         | ✅ Done |

**Test results:**
- `Bagira.SimHost.Tests`: **Passed 132 / 132**
- `Bagira.DDS.DataModel.Tests`: **Passed 16 / 16**

---

## Q1 — Corrective Task 0: CycloneDDS Union Fix

**Issues encountered:**

1. **Wrong struct annotation.** `AttributeValueUnion` had `[DdsStruct]` — the default annotation for plain structs. The correct annotation for a discriminated union in the CycloneDDS DSL is `[DdsUnion]`. The reference pattern was extracted from `EntityDescriptorUnion` in `AllDescriptors.cs`, which correctly demonstrates: `[DdsUnion]` on the container struct, `[DdsDiscriminator]` on the discriminator field, and `[DdsCase(EnumValue)]` on each branch field. Applying this triple pattern to `AttributeValueUnion` fixed the code-generator input.

2. **`System.Text.Json` does NOT serialize public fields by default.** The pre-existing test `AttributeRecord_Float64_RoundTripsViaJsonSerializer` was failing silently (round-tripping to `{}`). The fix was to add `[JsonInclude]` to every public field of `AttributeRecord`, `AttributeValueUnion`, `Vec3f`, `Vec3d`, and `Vec4f`, along with `using System.Text.Json.Serialization`. This was discovered by running the DataModel tests and observing all 16 now pass.

**Confirmation of CycloneDDS acceptance:** The CycloneDDS schema annotations (`[DdsUnion]`, `[DdsDiscriminator]`, `[DdsCase]`) are validated at build time by `CycloneDDS.CodeGen`. A clean `dotnet build` of `Bagira.DDS.DataModel` with 0 errors, combined with all 16 existing DataModel unit tests passing, confirmed acceptance.

---

## Q2 — `JsonToRecordCompiler` Nesting Without Array Allocation

**Hardest part: maintaining a depth-keyed hash context stack without heap allocation.**

`Utf8JsonReader` is a ref struct that processes tokens one-by-one, but has no concept of "accumulated path". To reconstruct the dotted path at each leaf:

- A `stackalloc ulong[MaxDepth + 1] contextStack` stores the FNV-1a hash of the parent path at each depth level. When `StartObject` is encountered, `contextStack[depth+1] = currentLeafHash` and `depth++`. When `EndObject`, `depth--`.
- When a `PropertyName` token is encountered, the current hash is built as `HashBytes(HashBytes(contextStack[depth], "."), nameBytes)` — using `reader.ValueSpan` which returns the raw UTF-8 bytes directly off the reader's internal buffer with zero allocation.
- Numeric string keys (array indices) are instead hashed with the wildcard `"*"` bytes, matching the pattern registered via `JsonToRecordCompilerBuilder.Register("Weapon.*.Ammo", ...)`.

This is the key insight: because we maintain a per-depth hash rather than a per-depth string, we never construct an intermediate path string. The hash accumulation is purely arithmetic on the stack. The only non-trivial state is a second `stackalloc byte[MaxDepth + 1] hadNumericAtDepth` used to properly restore sub-index tracking on `EndObject`.

The approach has a fixed ceiling of `MaxDepth = 16` nesting levels. This is sufficient for the known payload shapes and avoids renting from `ArrayPool` (which would require extra bookkeeping).

---

## Q3 — `BinaryInterpreter` Dispatch and Memory Offsets

**Dispatch array design:**

The interpreter uses a plain `Action<BinaryPatchContext, AttributeRecord>[]` array indexed directly by `AttributeId`. This gives O(1) dispatch with no dictionary lookup on the hot path. The array is sized to `maxId + 1` where `maxId` is the highest registered ID seen during `BinaryInterpreterBuilder.RegisterHandler`. IDs above the array bound (or null slots) are silently skipped via `handler?.Invoke(ctx, record)`.

The tradeoff is memory: a sparse ID space wastes slots. For the known attribute ID range (GeoLat=10, GeoLon=11, GeoAlt=12, Name/Affiliation in low single-digits) the array is tiny. If future attribute IDs grow large (e.g., thousands), a two-level table or a small sorted array + binary search should be considered.

**Scratchpad / `MemoryMarshal` usage:**

`BinaryPatchContext.ScratchpadData` is a `byte[]` block allocated once per context. Typed access is via:
```csharp
ref T GetScratchpad<T>(int byteOffset) =>
    ref MemoryMarshal.Cast<byte, T>(ScratchpadData.AsSpan(byteOffset))[0];
```

`MemoryMarshal.Cast` does a zero-copy reinterpretation of the byte span into `T`. Because `GeoCoordScratchpad` is an unmanaged struct (all fields are `double` / `bool`), it is safe to cast without padding concerns. The `ReserveScratchpad(int bytes)` builder method accumulates a running total offset and returns the base offset for each new reservation, providing contiguous non-overlapping sub-ranges.

The scratchpad is zeroed implicitly by `new byte[size]` in the `BinaryPatchContext` constructor. Between `Apply` calls, `DirtySubsystemsMask` and `DirtyDescriptorMask` are reset but the scratchpad byte content is NOT re-zeroed — instead, `SimTransformAttributeInstaller` uses a `GeoCoordScratchpad.Initialized` flag to trigger a reverse-geodetic pre-fill on the first handler call of each `Apply` invocation. This avoids a `Array.Clear` call per Apply while still giving correct partial-update semantics.

No `Span<T>` parameters are stored across async boundaries. All scratchpad refs are used entirely within `Apply` (synchronous stack frames), making the `ref T` return from `GetScratchpad<T>` safe.

---

## Q4 — Performance Reflections and Possible Optimizations

**Current profile:**

The hot path has:
- `Apply`: O(N) record scan, O(1) handler dispatch per record, one bit-scan flusher loop (at most 32 iterations), one `FlushDirtyMarks` call.
- `Compile` (edge): single-pass `Utf8JsonReader` token scan, O(1) dictionary lookup per leaf.
- No heap allocation on the `Apply` path.
- One heap allocation on `Compile` per string-type leaf (`reader.GetString()` creates a managed `string`). All numeric-type leaves allocate nothing.

**Possible optimizations:**

1. **Scratchpad zero on Apply.** Currently the `Initialized` flag gates reverse-geodetic pre-fill but leaves stale bytes in the scratchpad between `Apply` calls if the dirty-bit for a subsystem was never set. A `Span<byte>.Clear` of just the used scratchpad region (not the whole array) on `Apply` entry would make scratchpad state predictable without the `Initialized` flag. For the current scratchpad sizes (< 64 bytes) a `MemoryMarshal.Cast<byte, Vector4Long>` blanket zero with a single 64-byte store would be negligible.

2. **Dispatch array vs. sparse handlers.** If the attribute ID space becomes sparse (e.g., IDs > 256), switch to a small sorted `(ushort id, Action handler)[]` array and binary search — still O(log n) with no dictionary overhead and better CPU cache performance than a large sparse array with mostly null entries.

3. **String interning for `KindString` records.** Currently `reader.GetString()` allocates per compile call for string-type attributes. For low-cardinality string values (like `"FORCE_FRIENDLY"`, entity names that repeat), an intern pool keyed by UTF-8 bytes (using `Encoding.UTF8.GetString` + `string.IsInterned`) could reduce GC pressure. This is only worthwhile if the edge compiler is called at high frequency with repeated values.

4. **`IReadOnlyDictionary<ulong, EdgeSchemaEntry>` interface dispatch.** The `_routes` field is typed as an interface, which causes virtual dispatch on `TryGetValue`. Changing it to `Dictionary<ulong, EdgeSchemaEntry>` (concrete) would let the JIT inline the lookup or at minimum avoid a vtable indirection. This is a micro-optimization but measurable in tight loops.

5. **Flusher loop with `BitOperations.TrailingZeroCount`.** This is already the optimal approach for sparse dirty masks up to 32 bits. If the number of subsystems grows beyond 32, extending to `ulong` and using two separate mask words (one for subsystems, one already present as `DirtyDescriptorMask`) would be straightforward.
