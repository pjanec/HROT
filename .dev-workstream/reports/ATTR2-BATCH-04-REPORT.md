# ATTR2-BATCH-04 Report

**Batch:** ATTR2-BATCH-04  
**Developer:** GitHub Copilot  
**Date:** 2026-03-17  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| ATTR2-DEBT-06 | ✅ Complete | Standalone `EcsPatchContext.Create` factory extracted; binary path no longer depends on `_jsonCompiler` |
| ATTR2-DEBT-07 | ✅ Complete | `JsonToRecordCompiler` built in `InitializeEcs`, injected via `MapCommandController` into `CreationTool` |

---

## 🧪 Testing Results

**Bagira.Map.Common.Tests:** 60 / 60 ✅ (was 59 before — 1 new test added)  
**Bagira.IG.Tests:** 310 / 310 ✅ (was 308 before — 2 new tests added)  
**Bagira.SimHost.Tests:** 220 / 220 ✅ (no regressions)  
**Bagira.Runner.Tests:** 99 / 99 ✅ (no regressions)

**Key Test Scenarios Verified:**

- ✅ `UpdateEntityAttributeRequestSystem`: binary path applies `AttributeRecords` when `JsonAttributeCompiler = null` (`UpdateEntityAttributeRequestSystem_BinaryPath_WorksWithNullJsonCompiler`)
- ✅ `MapCommandController` with edge compiler: `CreationTool` left-click emits `InitialAttributeRecords` (non-null), `InitialAttributesJson = null` (`ActivatePlacementCommand_WithEdgeCompiler_CreationToolEmitsBinaryRecords`)
- ✅ `MapCommandController` without edge compiler: legacy JSON path unchanged (`ActivatePlacementCommand_WithoutEdgeCompiler_CreationToolUsesJsonPath`)
- ✅ All pre-existing binary-path tests continue to pass with the new `EcsPatchContext.Create` API

---

## 🔧 Implementation Details

### ATTR2-DEBT-06: Standalone `EcsPatchContext` Factory

**Problem:** `UpdateEntityAttributeRequestSystem`'s binary branch called `_jsonCompiler.CreatePatchContext(World, entity)`. When `_jsonCompiler` was `null` (binary-only mode without JSON compiler), the system logged a warning and silently skipped all `AttributeRecords`.

**Solution:**
- Added `EcsPatchContext.Create(EntityRepository, Entity)` public static factory method to `FDP.Toolkit.Replication.Patching.EcsPatchContext`. Internally it passes a private `s_emptyRoutes` (empty `Dictionary<ulong, RoutingEntry>`) to the existing `internal` constructor, so no `_ordinalByType` entries are populated. The `FlushDirtyMarks()` call in `BinaryInterpreter.Apply` becomes a no-op (touching no ordinals), which is correct because the binary installer flushers drive their own `SmartEgress` marks.
- Rewrote the binary branch in `UpdateEntityAttributeRequestSystem.ProcessRequest` to call `EcsPatchContext.Create(World, entity)` directly, removing the `if (_jsonCompiler == null) { … return; }` guard entirely.
- The old "binary records skipped because no json compiler is injected" log message was deleted.

**Files changed:**
- `FDP/Toolkits/FDP.Toolkit.Replication/Patching/EcsPatchContext.cs` — added `s_emptyRoutes` field and `Create(EntityRepository, Entity)` factory.
- `Bagira.Map.Common/Systems/UpdateEntityAttributeRequestSystem.cs` — binary path now uses `EcsPatchContext.Create`; `_jsonCompiler` null-guard removed from that branch.
- `Bagira.Map.Common.Tests/UpdateEntityAttributeRequestSystemTests.cs` — new test `UpdateEntityAttributeRequestSystem_BinaryPath_WorksWithNullJsonCompiler`.

---

### ATTR2-DEBT-07: IG DI Wiring for `CreationTool`

**Problem:** `CreationTool` accepts a `JsonToRecordCompiler? edgeCompiler` parameter but it was never wired in production. `MapCommandController.ActivatePlacementCommand` always passed `null`, so all placement requests went over the legacy JSON wire instead of binary `AttributeRecords`.

**Solution:**
- Added `using FDP.Toolkit.Replication.Patching;` and `_edgeCompiler` private field to `IgApplication`.
- Built the compiler in `InitializeEcs()` using `JsonToRecordCompilerBuilder` with the same five paths registered by `AttributeCompilerFactory.BuildEdgeCompiler()` in `Bagira.SimHost` (Name, Affiliation, GeoLat, GeoLon, GeoAlt). Building it in `InitializeEcs` (rather than `InitializeNetwork`) makes it available regardless of DDS state and avoids re-creation on reconnect.
- Added `JsonToRecordCompiler? _edgeCompiler` field and optional constructor parameter to `MapCommandController`. Existing tests (which pass only 3 args) continue to work because the parameter defaults to `null`.
- `ActivatePlacementCommand` now passes `edgeCompiler: _edgeCompiler` to the `CreationTool` constructor.
- Passed `_edgeCompiler` as the 4th argument when constructing `MapCommandController` in `InitializeNetwork`.

**Files changed:**
- `Bagira.IG/IgApplication.cs` — added using, field, compiler initialization, passed to `MapCommandController`.
- `Bagira.IG/Systems/MapCommandController.cs` — added `using`, `_edgeCompiler` field, optional constructor parameter, passed to `CreationTool`.
- `Bagira.IG.Tests/MapCommandControllerTests.cs` — two new tests: `ActivatePlacementCommand_WithEdgeCompiler_CreationToolEmitsBinaryRecords` and `ActivatePlacementCommand_WithoutEdgeCompiler_CreationToolUsesJsonPath`.

---

## 📝 Developer Insights

**Q1: How did decoupling `EcsPatchContext` change the module visibility of `EntityRepository` and related properties? Did you notice any other internal architecture tightly coupled to `JsonAttributeCompiler`?**

The decoupling required no visibility changes — `EcsPatchContext`'s public `GetUnmanagedComponent<T>` / `GetManagedComponent<T>` / `CanWrite<T>` already delegated directly to `EntityRepository` methods; only the ordinal-lookup dictionary became a non-issue since we pass an empty routes map. The result is that `EcsPatchContext` is now a first-class standalone type rather than a factory product of `JsonAttributeCompiler`. The only remaining coupling is that `JsonAttributeCompiler.CreatePatchContext()` still exists as a convenience overload (passing `_routes` to populate ordinals for the JSON path) — this is correct and desirable, since the JSON path genuinely needs the routing table for `FlushDirtyMarks`.

No other internal architecture was found to be tightly coupled to `JsonAttributeCompiler`. The `AttributeCompilerBuilder` builds routes that are then owned by the compiler; nothing else reads the private `_routes` field.

**Q2: When wiring the Edge Compiler to the IG UI DI container, did you encounter any singleton/transient lifecycle concerns given its zero-allocation architecture?**

No lifecycle concerns. `JsonToRecordCompilerBuilder.Build()` produces an immutable, stateless `JsonToRecordCompiler` (its routing table is a frozen `IReadOnlyDictionary`). Building it once in `InitializeEcs()` and sharing it as a long-lived field in both `IgApplication` and (indirectly) in `MapCommandController` is safe. `CreationTool` holds a reference to the compiler but only calls `Compile(…, rentedSpan)` on the hot path. Since `JsonToRecordCompiler` contains zero mutable state, sharing across tool instances has no threading or re-entrancy implications.

---

## ⚠️ Outstanding Issues / Next Steps

None for this batch. Both P2/P3 debt items are resolved. The remaining open items (ATTR2-DEBT-01 through ATTR2-DEBT-05) are P3/P4 optimizations deferred to future batches.
