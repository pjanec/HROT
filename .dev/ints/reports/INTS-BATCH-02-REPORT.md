# INTS-BATCH-02 Report

**Batch:** INTS-BATCH-02  
**Developer:** GitHub Copilot  
**Date:** 2026-02-27  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| INTS-P2-006 | ✅ Complete | Added `HrotEnvironment` shared bootstrapper with `CreateTkb()`, `CreateGeoTransform()`, `CreateParticipant(int)` |
| INTS-P2-007 | ✅ Complete | `SubsystemOrchestrator.Initialize()` now forces `SimHost` headless when `IG` subsystem is present |
| INTS-P2-008 | ✅ Complete | `IgApplication.InitializeNetwork()` now uses `HrotEnvironment` for TKB, geo transform, participant |
| INTS-P2-009 | ✅ Complete | `SimHostApp.OnLoad()` now uses `HrotEnvironment` for TKB, geo transform, participant |
| INTS-P2-010 | ✅ Complete | `IosSubsystem.Initialize()` now uses `HrotEnvironment.CreateParticipant(config.DomainId)` |

---

## 🧪 Testing Results

**Unit Tests Passed:** 583 / 583  
**Integration Tests Passed:** N/A (no new integration test project changes in this batch)

**Executed test commands:**
- `dotnet test Hrot.Map.Common.Tests/Hrot.Map.Common.Tests.csproj --no-build`
- `dotnet test Hrot.ClusterRunner.Tests/Hrot.ClusterRunner.Tests.csproj --filter "FullyQualifiedName~SubsystemOrchestratorTests|FullyQualifiedName~IosSubsystemTests"`
- `dotnet test Hrot.IG.Tests/Hrot.IG.Tests.csproj`
- `dotnet test Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj`
- `dotnet test Hrot.ExCon.Tests/Hrot.ExCon.Tests.csproj`

**Key Test Scenarios Verified:**
- [x] `HrotEnvironment.CreateTkb()` returns populated TKB (`Tank_M1Abrams`, `Infantry_Rifleman` templates resolvable)
- [x] `HrotEnvironment.CreateGeoTransform()` maps local origin `(0,0,0)` to Berlin origin within tolerance
- [x] `HrotEnvironment.CreateParticipant(10)` creates participant with `DomainId == 10`
- [x] `SubsystemOrchestrator` forces `SimHost` headless when `IG` is present
- [x] `SubsystemOrchestrator` preserves non-headless `SimHost` when `IG` is absent
- [x] Existing `Hrot.IG.Tests`, `Hrot.SimHost.Tests`, and `Hrot.ExCon.Tests` regression suites pass after refactors

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Main issue was a circular project dependency when trying to reference `Hrot.Map.Definitions` directly from `Hrot.Map.Common` for `BdcTkbCatalog`. `Hrot.Map.Definitions` already depends on `Hrot.Map.Common` (for `TkbEntityTypes`), so direct reverse-reference caused restore/build failure. I resolved this by invoking `BdcTkbCatalog.RegisterAll(TkbDatabase)` via reflection inside `HrotEnvironment.CreateTkb()`, preserving the required behavior without introducing a compile-time cycle.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

The environment bootstrap concerns were duplicated across `IG`, `SimHost`, and `Runner/IOS`, which made drift likely (different participant/domain/origin setups). This batch reduced duplication, but I would further improve by creating an explicit shared registration contract in a neutral assembly to remove reflection and make catalog wiring compile-time safe.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

I chose reflection-based invocation for `BdcTkbCatalog.RegisterAll` to satisfy both constraints: (1) keep `HrotEnvironment` in `Hrot.Map.Common`, and (2) keep `CreateTkb()` responsible for catalog population. Alternative considered: move `HrotEnvironment` into `Hrot.Map.Definitions` or add a new intermediate abstractions project. Both would be more invasive than required for this batch.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

If `Hrot.Map.Definitions` is absent from the runtime graph and `CreateTkb()` is called, registration cannot happen. This now fails fast with a clear `InvalidOperationException` rather than returning an empty TKB silently. Also validated that orchestrator behavior still keeps `IG` graphical when global mode is non-headless and only `SimHost` is auto-forced headless.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

Reflection in `CreateTkb()` adds small overhead but is one-time bootstrap cost, not in simulation hot paths. The bigger long-term optimization is architectural: remove reflection by introducing a compile-time registration surface (e.g., `ITkbCatalogRegistrar`) in a shared layer. Current implementation is acceptable for this batch scope.

---

## ⚠️ Outstanding Issues / Next Steps

- [ ] Replace reflection-based catalog invocation with a compile-time shared registrar contract to improve type safety.
- [ ] Reconcile SimHost geodetic config usage with shared Berlin-default bootstrap policy if runtime-configurable origins are required in future phases.
- [ ] `.dev-workstream/README.md` referenced by batch instructions is missing in this workspace; existing guidance appears in `.dev-workstream/guides/DEV-GUIDE.md`.
