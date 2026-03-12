# ATTR-BATCH-04: Hot-Path Optimization & Architectural Hardening

**Batch Number:** ATTR-BATCH-04  
**Phase:** Optimization  
**Estimated Effort:** 2-4 hours  
**Priority:** MEDIUM  
**Dependencies:** ATTR-BATCH-03  

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to ATTR-BATCH-04. This batch focuses entirely on addressing the developer insights and TODO architectural hazards raised at the end of the ATTR feature development.

Specifically, you will optimize the live-update hot path to be perfectly zero-allocation by pooling the `EcsPatchContext`, harden its memory-flushing semantics using `IDisposable` and ReSharper compiler annotations, and definitively eliminate any risk of coordinate mapping drift in `DescriptorMapper`.

### Source Code Location
- `Bagira.Map.Common/Systems/UpdateEntityAttributeRequestSystem.cs`
- `Bagira.Map.Common/Replication/Utils/JsonAttributeCompiler.cs`
- `Bagira.Map.Common/Replication/Utils/EcsPatchContext.cs`
- `Bagira.Map.Common/Replication/Utils/DescriptorMapper.cs`
- `Bagira.Map.Common.Tests/Bagira.Map.Common.Tests.csproj`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/ATTR-BATCH-04-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task 3:** Implement → Write tests → **ALL tests pass** ✅

---

## ✅ Tasks

### Task 1: Pool `EcsPatchContext` (Zero-allocation Update)

**Files:** `JsonAttributeCompiler.cs`, `EcsPatchContext.cs`

**Description:** Eliminate the per-message heap allocation of `EcsPatchContext` and its internal `HashSet<long>`.

**Requirements:**
- Implement an object pooling mechanism for `EcsPatchContext` inside `JsonAttributeCompiler` (e.g. `ConcurrentBag<EcsPatchContext>` or `ObjectPool<EcsPatchContext>`).
- Add `public EcsPatchContext RentPatchContext(EntityRepository repo, Entity entity)` to `JsonAttributeCompiler`, returning a pooled instance.
- Add an internal `Reset(EntityRepository repo, Entity entity)` method to `EcsPatchContext` to clear the `_touchedOrdinals` HashSet and reassign the repo/entity.
- Ensure the `HashSet<long>` is `.Clear()`ed, not replaced, so its capacity is retained across leases.

**Tests Required:**
- ✅ `JsonAttributeCompiler_RentPatchContext_ReusesInstancesAndClearsOrdinals`

---

### Task 2: Safety via `IDisposable` and `[MustDisposeResource]`

**Files:** `EcsPatchContext.cs`, `UpdateEntityAttributeRequestSystem.cs`

**Description:** Ensure that `FlushDirtyMarks` is always called, protecting against future developers accidentally returning early from `ProcessRequest` and causing silent network state divergence.

**Requirements:**
- Make `EcsPatchContext` implement `IDisposable`.
- Annotate `EcsPatchContext` with `[JetBrains.Annotations.MustDisposeResource]`. *(Define the attribute locally or use the FDP.Toolkit/ReSharper equivalent if available).*
- In `Dispose()`, run `FlushDirtyMarks()` and automatically return the context to the pool.
- Update `UpdateEntityAttributeRequestSystem` to use a `using var context = _jsonCompiler.RentPatchContext(...)` block.
- Remove the explicit `context.FlushDirtyMarks()` call in the system, as `Dispose()` now handles it.

**Tests Required:**
- ✅ `EcsPatchContext_Dispose_FlushesMarksAndReturnsToPool`
- ✅ Verify `UpdateEntityAttributeRequestSystemTests` continue to pass perfectly since `IDisposable` mimics the previous explicit flush.

---

### Task 3: Eliminate `ApplyGeoSpatialDescriptor` Drift Risk

**File:** `DescriptorMapper.cs`

**Description:** The previous batch noted that if a new `"Heading"` JSON delegate is added, `ApplyGeoSpatialDescriptor` would silently fail to apply it. We will solve this by eliminating the duplicate conversion logic entirely.

**Requirements:**
- Modify the `dtGeoSpatial` switch case handling (Phase 6 compiler overload) inside `DescriptorMapper.MapToComponents`.
- Delete `ApplyGeoSpatialDescriptor` in its entirety.
- Replicate the success of `dtEntityInfo`'s Json generation: dynamically construct a minimal JSON string for the GeoSpatial coordinates:
  `{"GeoPosition":{"Latitude":..., "Longitude":..., "Altitude":...}}`
- Apply that JSON payload onto the `ListPatchContext` via `compiler.Compile()`. This guarantees the descriptor path utilizes the exact same conversion route as pure JSON attribute patches.

**Tests Required:**
- ✅ `DescriptorMapper_GeoSpatial_JsonRoute_MatchesLegacyOutput` (ensure precision isn't lost during fast JSON printing of doubles)

---

### Task 4: Support Partial Geodetic Patches (Inverse Translation)

**File:** `AttributeCompilerFactory.cs`

**Description:** Currently, `GeoCoordAccumulator` restricts `GeoPosition` updates by requiring Latitude, Longitude, and Altitude to be updated simultaneously before translating to Cartesian space. To support partial patches (e.g., updating only `Altitude`), we must extract the missing geodetic coordinates using an inverse calculation (`geoTransform.ToGeodetic`). 

**Requirements:**
- Refactor the geographic property delegates (`GeoPosition.Latitude`, `GeoPosition.Longitude`, `GeoPosition.Altitude`) in `AttributeCompilerFactory` to safely handle partial updates.
- If a coordinate is received but we do not receive all three, read the current Cartesian `SimTransform.Position` and pass it through `geoTransform.ToGeodetic(...)` to extract the current (latitude, longitude, altitude).
- Replace the appropriate coordinate in that extracted triplet with the newly received JSON value.
- Convert back to Cartesian via `geoTransform.ToCartesian(...)` and apply back to the `SimTransform`.
- Ensure performance remains robust if all three ARE provided simultaneously in the same JSON object (we shouldn't do the inverse translation redundantly 3 times if we don't have to). 

**Tests Required:**
- ✅ `AttributeCompilerFactory_PartialGeoPatch_AltitudeOnly_SuccessfullyAppliesViaInverseMath`

---

## 📊 Report Requirements

When completing the batch, submit `.dev-workstream/reports/ATTR-BATCH-04-REPORT.md`.

**Developer Insights**  
**Q1:** What mechanism did you use to pool the patch contexts (e.g. `ConcurrentBag` vs ThreadLocal), and why?  
**Q2:** You implemented inverse geodetic calculations in Task 4 to support partial altitude updates. What are the performance implications of this if a stream of partial positional patches is received, and how might you optimize this in the future?
**Q3:** When constructing the JSON string inside `DescriptorMapper` for `dtGeoSpatial`, did you encounter any locale-specific string parsing bugs (e.g. `,` vs `.` for decimal places) with double formatting? How did you ensure Culture Invariant string generation for the compiler?

---

## 🎯 Success Criteria
- [ ] Heap allocations per `UpdateEntityAttributeRequest` have dropped back to zero.
- [ ] The `using var context...` guarantees egress marks are flushed properly.
- [ ] `DescriptorMapper` delegates correctly route 100% of spatial descriptors through JSON bounds safely.
- [ ] Partial `GeoPosition` patches (e.g. Altitude only) correctly move the entity along its local vector.
- [ ] Tests and Report are completed.
