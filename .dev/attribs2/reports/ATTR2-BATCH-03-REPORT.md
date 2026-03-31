# ATTR2-BATCH-03 Report

**Batch:** ATTR2-BATCH-03  
**Developer:** GitHub Copilot  
**Date:** 2026-03-17  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| CORRECTIVE-0 | ✅ Complete | All `[JsonInclude]` removed from `GenericMessages.cs`; tests updated with `IncludeFields = true` |
| ATTR2-P5T1 | ✅ Complete | `CreateEntityRequestSystem` binary branch implemented and tested |
| ATTR2-P5T2 | ✅ Complete | `UpdateEntityAttributeRequestSystem` binary branch implemented and tested |
| ATTR2-P6T1 | ✅ Complete | `CreationTool` edge compiler injection implemented and tested |

---

## 🧪 Testing Results

**Unit Tests Passed:** 1007 / 1007 (0 failures)

| Project | Tests |
|---------|-------|
| Hrot.NED.Tests | 23 |
| Hrot.IG.Tests | 308 |
| Hrot.SimHost.Tests | 220 |
| Hrot.Map.Common.Tests | 59 |
| Hrot.SimHost.Integration.Tests | 28 |
| Hrot.ClusterRunner.Tests | 99 |
| Hrot.ExCon.Tests | 270 |
| **Total** | **1007** |

**Key Test Scenarios Verified:**

- ✅ `[JsonInclude]` completely removed from `GenericMessages.cs`; `using System.Text.Json.Serialization` import removed
- ✅ `AttributeRecordTests` — `JsonSerializer.Serialize/Deserialize` with `IncludeFields = true` — all 23 DDS DataModel tests pass
- ✅ `CreateEntityRequestSystem`: binary records path → entity spawned with correct `IgEntityData.Name`
- ✅ `CreateEntityRequestSystem`: null binary records → JSON fallback path works
- ✅ `CreateEntityRequestSystem`: both binary + JSON present → binary takes precedence
- ✅ `CreateEntityRequestSystem`: both null → entity spawns with TKB defaults, no exception
- ✅ `UpdateEntityAttributeRequestSystem`: binary path mutates live `IgEntityData.Name`
- ✅ `UpdateEntityAttributeRequestSystem`: ACK OpaqueData bitmask has correct component ID bit set
- ✅ `UpdateEntityAttributeRequestSystem`: authority guard skips binary write when no authority (silent bystander)
- ✅ `CreationTool` without edge compiler: `InitialAttributesJson` set, `InitialAttributeRecords` null (legacy path unchanged)
- ✅ `CreationTool` with edge compiler: `InitialAttributeRecords` populated, `InitialAttributesJson` null
- ✅ `CreationTool` with edge compiler: 1-path JSON → 1 record; 3-path geo JSON → 3 records

---

## 📝 Developer Insights

**Q1: How did removing `[JsonInclude]` affect the overall test suite? Was `IncludeFields = true` sufficient for all sub-structs out of the box?**

Yes — passing `new JsonSerializerOptions { IncludeFields = true }` to every `JsonSerializer.Serialize` and `Deserialize` call in `AttributeRecordTests.cs` was sufficient for all nested structs (`AttributeValueUnion`, `Vec3f`, `Vec3d`, `Vec4f`) without any additional converter registration. The `System.Text.Json` field-inclusion flag is recursive: once enabled at the options level it applies to all fields at all nesting depths. The 23 DataModel tests all pass without modification beyond the options change. Struct equality comparisons (`Assert.Equal(default(Vec3f), ...)`) also continue to work correctly because the structs have value-type equality.

**Q2: When mixing binary and JSON payloads on `UpdateEntityAttributeRequestSystem`, was there any conflict with ECS authority guards during the separated parsing phases?**

No conflict. Both branches share the same `EcsPatchContext` as the underlying patch context wrapper. The binary path creates an `EcsPatchContext` via `_jsonCompiler.CreatePatchContext(World, entity)` (reusing the compiler purely for its `CreatePatchContext` factory, not its `Compile` path), then wraps it in a `BinaryPatchContext`. The `CanWrite<T>()` / `CanWriteManaged<T>()` calls inside each binary handler delegate to `EcsPatchContext.CanWriteManaged<T>()` / `EcsPatchContext.CanWrite<T>()`, which read `EntityHeader.AuthorityMask` exactly as the JSON path does. Authority checks are therefore identical between the two paths; no guard duplication or bypass occurs.

**Q3: During `CreationTool` implementation, what size of buffer did you rent from `ArrayPool` for `Compile`?**

64 slots (`ArrayPool<AttributeRecord>.Shared.Rent(64)`). This matches `JsonToRecordCompiler`'s `MaxDepth = 16` constant and the practical maximum of ~5 registered schema paths. Even for more elaborate creation payloads, 64 records comfortably covers the production schema. The rented buffer is converted to a `List<AttributeRecord>` via `buffer[..count].ToList()` and returned to the pool in a `finally` block, preventing any leak even if the compiler throws on malformed JSON.

---

## ⚠️ Outstanding Issues / Next Steps

- The `UpdateEntityAttributeRequestSystem` binary path requires a non-null `_jsonCompiler` to construct an `EcsPatchContext` (since `EcsPatchContext` is constructed via `JsonAttributeCompiler.CreatePatchContext`). If `_jsonCompiler` is null but binary records arrive, the system logs a warning and acks a no-op. A future improvement could introduce a standalone `EcsPatchContext` factory independent of `JsonAttributeCompiler` to remove this dependency.
- The `IG` application wiring (`IgApplication.cs`) has not been updated to inject `JsonToRecordCompiler` from `AttributeCompilerFactory.BuildEdgeCompiler()` into `CreationTool` — this production DI wiring was out of scope for this batch but should be done before the binary pipeline goes live on the DDS wire.
