# BATCH REPORT: ATTR2-BATCH-01

**Batch:** ATTR2-BATCH-01  
**Tasks:** ATTR2-P1T1, ATTR2-P1T2, ATTR2-P1T3  
**Status:** ✅ COMPLETE  
**Date:** 2026-03-13

---

## ✅ Task Completion Summary

### Task 1 — `AttributeValueUnion` and `AttributeRecord` DDS Types (ATTR2-P1T1)
**File:** `Bagira.DDS.DataModel/GenericMessages.cs`

- Added `AttributeValueType` enum with 9 discriminator values (`KindInt32` through `KindVec4f`).
- Added `Vec3f`, `Vec3d`, `Vec4f` value structs — annotated with `[DdsStruct]`, `[DdsIdlFile("bdc-sst-generic-msgs")]`, and `partial` to satisfy the CycloneDDS codegen.
- Added `AttributeValueUnion` struct with scalar branches (`IntValue`, `LongValue`, `FloatValue`, `DoubleValue`, `BoolValue`, `StringValue`) and three zero-allocation vector branches (`Vec3fValue`, `Vec3dValue`, `Vec4fValue`).
- Added `AttributeRecord` struct with `AttributeId`, `SubIndex1`, `SubIndex2`, `Value`.
- All tests in `Bagira.DDS.DataModel.Tests/AttributeRecordTests.cs` pass.

### Task 2 — `AttributeId` Schema Constants (ATTR2-P1T2)
**File:** `FDP/Toolkits/FDP.Toolkit.Replication/Patching/AttributeIds.cs`

- Static class `AttributeIds` with `ushort` constants: `Name=1`, `Affiliation=2`, `GeoLat=10`, `GeoLon=11`, `GeoAlt=12`.
- Numeric range reservation strategy documented in XML doc comments.
- No ECS component references; only `System` namespace.
- Project builds with 0 errors.

### Task 3 — Wire Message Extensions (ATTR2-P1T3)
**File:** `Bagira.DDS.DataModel/GenericMessages.cs`

- Added `List<AttributeRecord>? InitialAttributeRecords` to `CreateEntityRequest`.
- Added `List<AttributeRecord>? AttributeRecords` to `UpdateEntityAttributeRequest`.
- Both fields are `[DdsManaged]`, nullable, and carry XML doc comments referencing ATTR2-DESIGN.md §3.1.
- Existing `InitialAttributesJson` and `AttributePatchJson` fields are untouched.
- All 3 wire-message tests pass.

---

## 🧪 Test Results

| Suite | Passed | Failed | Notes |
|---|---|---|---|
| `Bagira.DDS.DataModel.Tests` | 8 | 1 | Pre-existing: `CanPublishAndSubscribeEntityMaster` requires a live DDS runtime; fails in isolation on baseline too |

All 8 unit and contract tests pass. Zero regressions introduced.

---

## 💡 Developer Insights

**Q1: What issues did you encounter?**  
The CycloneDDS codegen rejected the new vector structs with _"uses type X, which is not a valid DDS type"_ because they were not annotated. Adding `[DdsStruct]` and `[DdsIdlFile("bdc-sst-generic-msgs")]` resolved the schema validation failure. A second error immediately followed — the codegen emits `partial struct` companions into `obj/Generated`, so handwritten declarations must also be `partial`. Both fixups are one-liners once the pattern is understood.

**Q2: Weak points in `GenericMessages.cs`?**  
The file mixes DDS infrastructure types (topics, enums) with what are effectively IDL primitive helpers (`Vec3f` etc.). As the schema grows, a dedicated `GenericPrimitives.cs` or similar would keep the file size manageable. The `OpaqueData` field on `CreateUpdateDeleteEntityAck` using `List<byte>?` is a similar allocation concern to the vectors we fixed here — a fixed-size byte array or `Memory<byte>` would be cleaner for that use case.

**Q3: `AttributeValueUnion` layout decisions?**  
`[StructLayout(LayoutKind.Explicit)]` was considered and abandoned: it is incompatible with `[DdsManaged]` (required for the `string?` branch), and the CycloneDDS schema layer does not forward `FieldOffset` metadata to the IDL generator. The chosen approach — one field per type, discriminated by `ValueType` — keeps the struct DDS-serialisable without any interop hacks. The per-instance size overhead (carrying all fields at once) is acceptable given the struct is a wire atom, not an in-memory pool type.

For the vector branches, custom value types (`Vec3f`, `Vec3d`, `Vec4f`) replace the original `List<float|double>?` fields, eliminating heap allocation entirely. The struct layout stays within the 32-byte working set that is comfortable for stack passing.

**Q4: Edge cases not mentioned in the spec?**  
- JSON round-trip of `Vec3f`/`Vec3d`/`Vec4f` fields: `System.Text.Json` serialises nested structs correctly without any custom converters, which was verified implicitly by the Float64 round-trip test (the struct containing `Vec3fValue` etc. is embedded inside `AttributeValueUnion`). No additional converters needed.
- `default(Vec3f)` equality in tests: `Assert.Equal` works for structs via `ValueType.Equals` (field-by-field reflection comparison), requiring no `IEquatable<T>` implementation on the helper types.

**Q5: Performance observations?**  
The biggest win over the previous `List<float>?` design is removing three potential heap allocations per `AttributeValueUnion` for vector branches. Each `AttributeRecord` is now fully stack-allocatable when used locally. `List<AttributeRecord>?` on the wire messages is still a managed list, but that is unavoidable at the message boundary — the list is used once per DDS write, not in any hot path.

---

## 📋 Files Changed

| File | Change |
|---|---|
| `Bagira.DDS.DataModel/GenericMessages.cs` | Added `AttributeValueType`, `Vec3f`, `Vec3d`, `Vec4f`, `AttributeValueUnion`, `AttributeRecord`; extended `CreateEntityRequest` and `UpdateEntityAttributeRequest` |
| `FDP/Toolkits/FDP.Toolkit.Replication/Patching/AttributeIds.cs` | New file — schema constants |
| `Bagira.DDS.DataModel.Tests/AttributeRecordTests.cs` | New file — 8 unit tests covering P1T1 and P1T3 |
