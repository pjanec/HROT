# ATTR-BATCH-03 Report

**Batch:** ATTR-BATCH-03  
**Developer:** GitHub Copilot  
**Date:** 2026-03-12  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| ATTR-S5T1 | ✅ Done | `AttributeCompilerFactory.Build(geoTransform?)` in `Hrot.SimHost/AttributeCompilerFactory.cs` |
| ATTR-S5T2 | ✅ Done | `CreateEntityRequestSystem` accepts `JsonAttributeCompiler?`; applies `ListPatchContext` in `ProcessPendingRequest` |
| ATTR-S5T3 | ✅ Done | `UpdateEntityAttributeRequestSystem` fully refactored with injectable interfaces + `EcsPatchContext` + `FlushDirtyMarks` |
| ATTR-S5T4 | ✅ Done | Ordinals `dtEntityInfo` and `dtWorldPos` passed as `descriptorOrdinal` in `AttributeCompilerFactory` |
| ATTR-S6T1 | ✅ Done | `DescriptorMapper.MapToComponents(descriptors, geo, compiler)` overload routes `dtEntityInfo` through compiler |
| ATTR-S6T2 | ✅ Done | `ApplyWorldPosDescriptor(ctx, geoSpatial, geoTransform)` public static — sets Position only, no rotation |

---

## 🧪 Testing Results

**Unit Tests Passed: 37 / 37 — `Hrot.Map.Common.Tests`**  
**Unit Tests Passed: 99 / 99 — `Hrot.SimHost.Tests`**

**New tests added this batch (17 total):**

| Test | File | Task |
|------|------|------|
| `ListPatchContext_FlushDirtyMarks_IsNoOp` | `JsonAttributeCompilerTests.cs` | ATTR-S5T2 |
| `UpdateEntityAttributeRequestSystem_JsonPatch_PatchesNameOnLiveEntity` | `UpdateEntityAttributeRequestSystemTests.cs` | ATTR-S5T3 |
| `UpdateEntityAttributeRequestSystem_JsonPatch_FlushDirtyMarksCalledForEntityInfoOrdinal` | `UpdateEntityAttributeRequestSystemTests.cs` | ATTR-S5T3 |
| `UpdateEntityAttributeRequestSystem_DualFieldPatch_BothApplied_SingleDirtyFlush` | `UpdateEntityAttributeRequestSystemTests.cs` | ATTR-S5T3 |
| `UpdateEntityAttributeRequestSystem_UnknownEntityId_AcksEntityNotFound` | `UpdateEntityAttributeRequestSystemTests.cs` | ATTR-S5T3 |
| `UpdateEntityAttributeRequestSystem_EmptyJson_AcksSuccess_NoMutation` | `UpdateEntityAttributeRequestSystemTests.cs` | ATTR-S5T3 |
| `SimHostAttributeCompiler_Name_Registered` | `AttributeCompilerFactoryTests.cs` | ATTR-S5T1/T4 |
| `SimHostAttributeCompiler_Affiliation_Registered` | `AttributeCompilerFactoryTests.cs` | ATTR-S5T1/T4 |
| `SimHostAttributeCompiler_Affiliation_PreservesExistingName` | `AttributeCompilerFactoryTests.cs` | ATTR-S5T1/T4 |
| `AttributeCompiler_NamePatch_TriggersEntityInfoDirtyOnEcsPatchContext` | `AttributeCompilerFactoryTests.cs` | ATTR-S5T1/T4 |
| `AttributeCompiler_GeoPatch_TriggersWorldPosDirty` | `AttributeCompilerFactoryTests.cs` | ATTR-S5T1/T4 |
| `CreateEntityRequestSystem_InitialAttributesJson_PatchesName` | `AttributeCompilerFactoryTests.cs` | ATTR-S5T2 |
| `CreateEntityRequestSystem_InitialAttributesJson_DoesNotOverwriteAffiliation` | `AttributeCompilerFactoryTests.cs` | ATTR-S5T2 |
| `CreateEntityRequestSystem_NullJson_NoPatch` | `AttributeCompilerFactoryTests.cs` | ATTR-S5T2 |
| `DescriptorMapper_WithCompiler_DtEntityInfoProducesIgEntityData` | `AttributeCompilerFactoryTests.cs` | ATTR-S6T1/T2 |
| `DescriptorMapper_WithCompiler_NoDuplicateIgEntityData` | `AttributeCompilerFactoryTests.cs` | ATTR-S6T1/T2 |
| `DescriptorMapper_WorldPos_SharedDelegate_ProducesSameResultAsDirectPath` | `AttributeCompilerFactoryTests.cs` | ATTR-S6T1/T2 |

---

## 📝 Developer Insights

**Q1: What difficulties did you encounter when wiring up the multi-coordinate `GeoPoint` struct logic for `SimTransform` conversions?**

The core difficulty is that `IGeographicTransform.ToCartesian(lat, lon, alt)` requires all three coordinates simultaneously, but `Utf8JsonReader` delivers them one token at a time. Each registered delegate fires in isolation — the `"Latitude"` delegate has no access to whatever the `"Longitude"` delegate will later see.

The solution was a stateful `GeoCoordAccumulator` inner class with three nullable fields (`Lat?`, `Lon?`, `Alt?`). Each delegate stores its coordinate then calls `TryApply(ref SimTransform)`, which only fires `ToCartesian` when all three are non-null. After a successful conversion the accumulator resets all three to null, preventing partial coordinates from contaminating a subsequent patch that provides only one or two fields.

One subtlety: within a single well-formed JSON object `{"GeoPoint":{"Latitude":…,"Longitude":…,"Altitude":…}}` the delegates fire sequentially, so the third delegate always triggers the conversion. The nullable guard makes the accumulator robust when a caller patches only latitude — in that case `TryApply` silently no-ops and the position is left unchanged, which is the correct semantics for a partial patch.

**Q2: Does the `DescriptorMapper` Phase 6 structure feel sustainable going forward?**

Yes, with one caveat. The new compiler overload eliminates the hardcoded string-literal assignments for `Name` and `Affiliation`, replacing them with the same delegate table used at runtime. Adding a new field (e.g. `Health`) now only requires one `RegisterReferencePath` call in `AttributeCompilerFactory.Build()` — `DescriptorMapper` picks it up automatically.

The duplication risk vector lies in `ApplyWorldPosDescriptor`: it sets `Position` only, matching the JSON path delegates which also do not touch `Rotation`. If someone later adds a `"Heading"` JSON path to the compiler, they must also update `ApplyWorldPosDescriptor` to remain consistent, otherwise the two creation paths — JSON-string route vs descriptor-union route — diverge. The `DescriptorMapper_WorldPos_SharedDelegate_ProducesSameResultAsDirectPath` test explicitly asserts this invariant, which provides a regression safety net but not compile-time enforcement.

**Q3: Were you able to verify any lingering allocations in the `UpdateEntityAttributeRequestSystem` hot path?**

The streaming `Utf8JsonReader` path (`stackalloc`-backed depth queue, FNV-1a hashing) is zero-allocation as designed. However, `_jsonCompiler.CreatePatchContext(World, entity)` allocates a new `EcsPatchContext` instance — which in turn allocates a `HashSet<long>` for `_touchedOrdinals` — on every `ProcessRequest` invocation. This is a managed allocation per processed attribute request.

For low-frequency attribute updates (operator UI interactions) the allocation is inconsequential. If the `UpdateEntityAttributeRequest` topic were ever used for high-frequency physics updates this would be a concern. The simplest mitigation would be to pool (or pre-allocate and clear) the `EcsPatchContext` instance and its inner `HashSet<long>`, reusing them across invocations rather than constructing a fresh instance per message.

**Q4: In what scenarios could a caller bypass the compile safety bounds of `FlushComponents()` and `FlushDirtyMarks()`?**

**`ListPatchContext` — silent discard via missing `FlushComponents()`:**  
A caller can invoke `Compile(json, ctx)` and then never call `ctx.FlushComponents()`. All delegate mutations land on the internal slot dictionary but are never merged back into any component list. The call returns with no error. This can happen if a future developer calls `Compile` for validation purposes and discards the result, silently dropping all attribute values from the entity creation pipeline.

**`EcsPatchContext` — silent missing egress via missing `FlushDirtyMarks()`:**  
`Compile(json, ctx)` updates ECS components in-place. If `FlushDirtyMarks()` is never called, the components are correct in memory but `SmartEgressUtil.MarkDirty` is never invoked. Remote peers receive no update; the state diverges without any error or warning. This would occur if a future developer extends `ProcessRequest` with early-return branches that skip `FlushDirtyMarks()`, or if the method is called directly in an ad-hoc context outside the system loop.

Neither context type enforces the flush via a disposable/`using` pattern. A `[MustDisposeResource]` annotation or wrapping `EcsPatchContext` in an `IDisposable` that calls `FlushDirtyMarks()` on `Dispose()` would provide stronger guarantees.

---

## ⚠️ Outstanding Issues / Next Steps
- No open blocking issues.
- Consider pooling `EcsPatchContext` + inner `HashSet<long>` if attribute update frequency ever increases significantly.
- Consider `IDisposable`-based guarantee for `EcsPatchContext.FlushDirtyMarks()` to prevent silent missing-egress bugs in future extensions.
- The `ApplyWorldPosDescriptor` / JSON-path convergence invariant is currently enforced only by the `DescriptorMapper_WorldPos_SharedDelegate_ProducesSameResultAsDirectPath` test; a comment co-locating the two code paths would aid future maintainers.
