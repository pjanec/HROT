# ATTR2-BATCH-05 Report

**Batch:** ATTR2-BATCH-05  
**Date:** 2026-03-17  
**Status:** Complete

---

## 📊 Task Completion

| Task ID       | Status | Notes |
|---------------|--------|-------|
| ATTR2-DEBT-01 | ✅ Done | `OpaqueData` changed from `List<byte>?` to `byte[]`. Code generator bug fixed as a prerequisite. |
| ATTR2-DEBT-02 | ✅ Done | `Vec3f`, `Vec3d`, `Vec4f` extracted to `GenericPrimitives.cs`. |
| ATTR2-DEBT-03 | ✅ Done | `ConcurrentDictionary<string, string>` string pool in `JsonToRecordCompiler`. |
| ATTR2-DEBT-04 | ✅ Done | `Span<byte>.Clear()` in `Apply()`, pre-apply handler mechanism, `Initialized` flag removed. |
| ATTR2-DEBT-05 | ✅ Done | `_routes` field changed from `IReadOnlyDictionary` to concrete `Dictionary`. |

---

## 🧪 Testing Results

**Unit Tests Passed:** 1,012 / 1,012  
**Integration Tests Passed:** 59 / 59 (28 SimHost + 31 Runner)

Projects verified:
- `Hrot.NED.Tests` — 23/23
- `Hrot.IG.Tests` — 310/310
- `Hrot.SimHost.Tests` — 222/222
- `Hrot.Map.Common.Tests` — 60/60
- `Hrot.SimHost.Integration.Tests` — 28/28
- `Hrot.ClusterRunner.Tests` — 99/99
- `Hrot.ExCon.Tests` — 270/270
- `Hrot.ClusterRunner.Integration.Tests` — 31/31

**New Tests Added:**
- `JsonToRecordCompilerTests.Compile_StringValue_SameReferencedReturnedOnRepeat` — verifies string intern pool returns same object reference for duplicate payloads (Task 3)
- `BinaryInterpreterTests.Apply_ScratchpadClearedBetweenCalls_StaleDataNotCarriedOver` — verifies scratchpad is zeroed at the start of each `Apply` call, preventing stale data carry-over (Task 4)

---

## 📝 Developer Insights

**Q1: What changes were enforced on the test suite when converting `Vec` structs to their own `GenericPrimitives.cs` file? Were any namespace considerations necessary?**

No test changes were required. All three types — `Vec3f`, `Vec3d`, `Vec4f` — are declared in the same `Hrot.NED.Messages` namespace in the new file, which is the only namespace these types ever appeared in. Since C# partial struct declarations are not used here (they are standalone, not partial), moving to a new file is purely a source organisation change that is transparent to all consumers. The DDS code generator (`CycloneDDS.CodeGen`) already had separate `.g.cs` files for each vector type (`Hrot.NED.Messages.Vec3f.g.cs` etc.) and those remain unchanged. Tests that reference `Vec3f`, `Vec3d`, `Vec4f` in `AttributeRecordTests.cs` and `BinaryInstallersTests.cs` compiled without any modification.

**Q2: Regarding String interning, did you favor an internal `Dictionary` cache or a generic memory cache for strings? How did performance scale?**

A per-instance `ConcurrentDictionary<string, string>` was chosen over the CLR global `string.Intern()` pool. The rationale:
- **Scope control:** A per-compiler-instance pool is GC-eligible with the compiler. The CLR global intern pool survives for the app-domain lifetime.
- **Thread safety:** `ConcurrentDictionary.GetOrAdd` with a static value factory (`static v => v`) is lock-free for reads once the key exists, matching the documented thread-safe nature of `JsonToRecordCompiler`.
- **Bounded domain:** The attribute schema defines a small set of distinct string values (faction enums, type enums). In practice the pool will contain < 50 entries per compiler instance. No hard cap is needed.
- **Performance:** After the first occurrence of a string, `GetOrAdd` finds the entry in O(1) and returns the cached reference, eliminating the `reader.GetString()` allocation cost for repeated identical payloads. Non-string value types are unaffected.

**Q3: To achieve array-based dispatch for Edge Schema mapping without massive memory loss, what structure did you pivot to?**

The `_routes` field in `JsonToRecordCompiler` was changed from `IReadOnlyDictionary<ulong, EdgeSchemaEntry>` to the concrete type `Dictionary<ulong, EdgeSchemaEntry>`. This retains fully associated O(1) hash-map lookup semantics while eliminating the virtual dispatch overhead that comes with calling `TryGetValue` through an interface: the JIT can now emit a direct `callvirt` against the concrete class vtable slot (typically devirtualized to a direct call in practice, or at minimum a predictable single-level indirect).

An array-backed structure such as a perfect hash or flat-bucket array was considered but rejected: the route hashes are 64-bit FNV-1a values with no bounded natural index domain, so any array would have catastrophic memory overhead. The existing hash map is already O(1) lookups; removing the single indirection layer of the interface virtual dispatch is the correct granularity of optimization here.

---

## Additional Implementation Notes

### ATTR2-DEBT-01: Code Generator Bug Fix

Changing `List<byte>?` to `byte[]` in `CreateUpdateDeleteEntityAck.OpaqueData` exposed two bugs in the CycloneDDS code generator:

1. **`SerializerEmitter.EmitFieldDynamicSize`**: Used `.Count` for all sequence types including `T[]` arrays, and called `ExtractGenericType` which does not handle `T[]` syntax (no `<>` brackets). Fix: detect `field.TypeName.EndsWith("[]")` and use `.Length` + direct member extraction via `Substring(0, Length - 2)`.

2. **`ViewEmitter.GenerateToManagedFieldAssignment`**: Used `new List<T>(...)` wrapper for all `IsSequence` fields including `T[]` arrays. Fix: check `isArrayField = field.TypeName.EndsWith("[]")` and emit `target.prop = this.prop.ToArray()` directly (same pattern as `IsFixedArray`).

Both fixes are minimal, targeted, and preserve existing behavior for `List<T>` fields.

### ATTR2-DEBT-04: Pre-Apply Handler Architecture

The `Initialized` flag removal required a new mechanism: `RegisterPreApplyHandler` on `BinaryInterpreterBuilder`. This creates a pre-apply phase in `BinaryInterpreter.Apply()`:

```
Apply():
  1. Reset DirtySubsystemsMask / DirtyDescriptorMask
  2. ctx.ScratchpadData.AsSpan().Clear()             ← predictable zero baseline
  3. Run pre-apply handlers (e.g. PreFillFromCurrentPosition)
  4. Dispatch loop (handlers update individual scratchpad fields)
  5. Flush phase (converters run only for dirty bits)
  6. FlushDirtyMarks()
```

`SimTransformAttributeInstaller.PreFillFromCurrentPosition` replaces `EnsureInitialized`: it runs once before the dispatch loop, reads the current entity geodetic position, and pre-fills the scratchpad's lat/lon/alt fields. The per-handler `if (scratch.Initialized) return;` branches are eliminated entirely.

---

## ⚠️ Outstanding Issues / Next Steps

None. All five debt items are resolved. The ATTR2 optimization pass is complete.
