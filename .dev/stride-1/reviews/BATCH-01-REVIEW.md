# BATCH-01 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Summary
Two new Stride class libraries (`Hrot.Stride.Core`, `Hrot.Stride.Animation`) created with correct references and TFMs, `HrotStrideApp.Game` wired to them + `Hrot.StrideMock`, and the pure `FdpStrideTransform` coordinate seam implemented with rigorous, behavior-proving tests. Verified independently: ran both test projects (37/37 + 4/4 pass) and built `HrotStrideApp.Game` (0 errors).

## Verification performed
- Opened `FdpStrideTransform.cs` and `FdpStrideTransformTests.cs` — assertions are real numeric checks, not string/`NotNull`.
- Confirmed the handedness derivation independently: the swizzle M (swap Y/Z) is an improper rotation (det −1), so the conjugated quaternion negates imaginary parts after relabel — exactly `(−x, −z, −y, w)`. Angular velocity (a pseudovector) gets the additional sign flip — correct.
- The **rotation homomorphism test** (`ToStridePosition(Transform(v,q)) ≈ Transform(ToStridePosition(v), ToStrideRotation(q))`) is the gold standard: it uniquely pins `ToStrideRotation` to `M·R·M⁻¹` and demonstrably fails for a no-sign-flip relabel on the combined yaw+pitch case. This is a strong regression guard.
- Ran `dotnet test` on both test projects and `dotnet build` on the Game project myself — counts match the report.

## Issues Found
No blocking issues. Minor items recorded as debt (see DEBT-TRACKER): `ScreenRayToFdp` untested + muddy screen-coordinate contract (P3); CycloneDDS codegen disabled on the Stride app projects, must be re-verified for Mode 2 DDS in P6 (P2); `Stride.Engine.Entity` vs `Fdp.Core.Entity` namespace ambiguity footgun (P3).

## Test Quality
Adequate and strong for T4. T1–T3 guard/contract tests are reflection-over-real-assemblies (reference closure, full `IAnimationBackend` interface-map coverage, `StrideNodeBootstrapper` loadable) — they verify real runtime facts, not csproj text. The `NotImplementedException` stubs are asserted for *presence/assignability*, never invoked as passing behavior. Good.

## Notes carried to BATCH-02
- Asset compilation was verified only as an "up-to-date skip" of `StrideCompileAsset`, not a forced clean compile. BATCH-02's end-to-end smoke (STR-P0-T8) must actually **boot the app**, which is the real proof the content pipeline + references are sound. Treat any asset-pipeline breakage discovered there as the true T3 regression signal.
- `ScreenRayToFdp` will first be exercised by editor picking in P5; fold the testable-seam cleanup in then.

## Verdict
APPROVED. Proceed to BATCH-02 (STR-P0-T5..T8: external host loop, `EditorStrideSubsystem`, `StrideVisualBindingSystem`, end-to-end spawn+render smoke).

## Commit Message
```
feat(stride): scaffold Hrot.Stride.Core + Hrot.Stride.Animation, wire HrotStrideApp.Game, add FdpStrideTransform seam (BATCH-01)

Completes STR-P0-T1, STR-P0-T2, STR-P0-T3, STR-P0-T4
- Hrot.Stride.Core (net8.0-windows): Stride.Engine/Physics/Rendering/Games + DotRecast 2026.1.3
  + Fdp.Core/Fdp.Toolkits refs; FdpStrideTransform coordinate seam (pos/rot/vel/angvel + ScreenRayToFdp)
- Hrot.Stride.Animation (net8.0-windows): StrideAnimationBackend : IAnimationBackend P4 stub
- HrotStrideApp.Game wired to both libs + Hrot.StrideMock; CycloneDdsDisableCodeGen workaround;
  added missing app.manifest; new projects added to HrotStrideApp.sln
Tests: 41 (37 Core incl. rotation-homomorphism handedness proof, exact axis/velocity/angular
  swizzle, round-trips; 4 Animation interface-contract). Game builds 0 errors.
```
