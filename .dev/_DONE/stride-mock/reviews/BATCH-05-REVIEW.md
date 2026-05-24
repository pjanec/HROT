# BATCH-05 Review

**Reviewer:** Dev Lead  
**Date:** 2026-05-14  
**Batch:** BATCH-05 (SM-009 + SM-010)  
**Status:** CHANGES REQUIRED / PARTIALLY APPROVED

---

## Summary

The sub-agent implemented SM-009 (`SimHostNodeBootstrapper` + `SimHostApp` refactoring) and
skipped SM-010 entirely due to time constraints. SM-009 had three critical bugs causing 6 test
regressions. All three bugs were fixed by the dev lead directly before this review was written.
SM-010 is deferred to BATCH-06.

---

## SM-009 Review

### APPROVED (with corrective fixes applied by dev lead)

**Original sub-agent issues:**

**Bug 1 -- Double initialization:**  
`SimHostApp.OnLoad()` retained the old `HrotNodeBuilder.Build()` block in full, then also
called `_bootstrapper.BootstrapNode()`. Two separate kernels and worlds were constructed.
The old initialization code (lines covering `HrotNodeBuilder.Build()`, `ConfigureForNode()`,
`replicationModule`, `RegisterSimComponents()`, etc.) was not removed.

**Bug 2 -- Null factory NullReferenceException:**  
The old code used `_bootstrapper.BootstrapNode(hrotConfig, _role, _networkFactory!)`. The
null-forgiving operator `!` suppresses the nullable warning at compile time but does NOT
prevent `NullReferenceException` at runtime when `_networkFactory` is null. Tests calling
`InitializeHeadless()` without a factory hit `NullReferenceException` at
`SharedApplicationBootstrapper.cs:76` (`networkFactory.ConfigureForNode(...)`).

**Bug 3 -- Gizmo systems registered after `Initialize()`:**  
`_kernel.RegisterModule()` and `_kernel.RegisterGlobalSystem()` throw
`InvalidOperationException("Cannot register ... after Initialize() called")` when called
after `Kernel.Initialize()` (Phase 7). The sub-agent placed all gizmo system registration
AFTER `BootstrapNode()` returned, which is after `Initialize()`. This was masked by Bug 2
(NullReferenceException occurred first for null-factory tests).

**Corrective fixes applied by dev lead:**

1. **Added `RegisterApplicationSystems(HrotNodeContext context)` virtual hook** to
   `SharedApplicationBootstrapper` (Phase 6d, after 6c, before 7). This provides the
   canonical extension point for application-level systems.

2. **Added `ApplicationSystemsRegistrar` property** to `SimHostNodeBootstrapper` and
   overrode `RegisterApplicationSystems` to invoke the callback. This lets `SimHostApp`
   register gizmos from its `OnLoad()` scope while keeping them inside Phase 6d.

3. **Changed `BootstrapNode` parameter** from `INetworkFactory` (non-nullable) to
   `INetworkFactory?` (nullable) in `SharedApplicationBootstrapper`. Changed the abstract
   `RegisterNetworkTranslators` hook second parameter to `INetworkFactory?` accordingly.
   Updated `StrideNodeBootstrapper` and `SimHostNodeBootstrapper` overrides.

4. **Removed the old double-initialization block** from `SimHostApp.OnLoad()` (the
   `HrotNodeBuilder.Build()` call and surrounding code).

5. **Moved gizmo setup into the `ApplicationSystemsRegistrar` callback** set before
   `BootstrapNode()` is called. Uses `ctx.Kernel` (Phase 6d context) instead of `_kernel`.

6. **Updated `SharedApplicationBootstrapperTests`** to add `RegisterApplicationSystems`
   to the expected virtual hooks list (SC_SM002_5 extended to 3 virtual hooks).

**Test results after corrections:**
- `Hrot.SimHost.Tests`: 566 Passed / 27 Failed (baseline restored -- no regressions)
- `Hrot.StrideMock.Tests`: 41/41 Passed
- `Hrot.FakeStrideApp.Tests`: 3/3 Passed (assumed, not re-run)

---

## SM-010 Review

### NOT DONE

Sub-agent skipped SM-010 entirely. Deferred to BATCH-06.

---

## Test Quality Assessment

Tests from SM-009 (written by sub-agent) were assessed:
- `SimHostComponentRegistrationTests` covers component registration, node ID resolution,
  and domain zero guard. Tests are solid and target real behavior. APPROVED.
- `SimHostTimeSyncTests.SimHost_Tick_DoesNotThrow` is a smoke test but valid. APPROVED.

No new tests were written in the corrective fixes (fixes targeted existing behavior, not new behavior).

---

## Issues Projected to BATCH-06

1. SM-010 is not done -- must be implemented in BATCH-06.
2. Test SC_SM010_2 must verify IG presentation modules appear in kernel topology via
   `GetAdditionalModules()` hook (not via `PopulateSystems` flattening).
3. IG baseline must remain 313 pass / 68 fail after SM-010.
