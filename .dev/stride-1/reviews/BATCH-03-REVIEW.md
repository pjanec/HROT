# BATCH-03 Review
**Status:** ✅ APPROVED (with carried GPU-verification obligation)   **Date:** 2026-06-03

## Summary
`StrideVisualBindingSystem` + `StrideVisualReference` + the `IStrideVisualFactory` seam (T7), and the end-to-end editor_stride spawn wiring with the **real UrbanCombat TKB templates** (T8). Phase 0 is functionally complete in code. Verified independently: read all new source, confirmed the production spawn path stamps `TkbIdentity`, ran the full suite (Core 65/65, Animation 4/4, Game 17/17 = 86 green).

## Verification performed
- Read `StrideVisualBindingSystem.cs`: real resolution `entity → TkbIdentity.TkbType → TkbDatabase.TryGetByType → GetDescriptor<StrideRenderModelDefDto>`; "0⇒default" sizing from `PhysicsCollider.Radius` / `VehicleParametersDto` Length/Width; correct two-pass `IsAlive`/query reconciliation; skip-on-no-descriptor (no throw). Only `FdpStrideTransform` used for swizzles.
- **Confirmed the descriptor-query is production-correct, not test-masked:** `NetworkSpawningSystem.cs:114` stamps `TkbIdentity { TkbType = cmd.TkbType }` on every spawned entity, so the binding system's `.With<TkbIdentity>()` query matches real spawned entities. The test's manual `TkbIdentity` in `InitialComponents` is redundant, not papering over a gap.
- Read the T7 unit tests (fake factory) and T8 integration tests: behavioral — exact model/skeleton refs, exact capsule-radius/box-half defaulting, exact swizzle `(10,25,3)→(10,3,25)`, idempotent create, exact destroy count, single-thread via captured `ManagedThreadId`, real UrbanCombat templates carry `StrideRenderModelDefDto`. Strong.
- Ran all three Stride test projects myself; counts match the report.

## Issues Found (non-blocking; recorded as debt)
1. **STR-D4 is NOT actually resolved.** The headline obligation — a real `ModelComponent`/procedural primitive appearing in a rendered frame — was **not** achieved: `StrideHrotGame.Run()` needs SDL2 + a GPU/display, unavailable headlessly, and Stride 4.2.1.2487 has no off-screen test harness. The coder marked STR-D4 RESOLVED; I have **downgraded it to PARTIAL** — the binding logic is fully proven by fakes and the concrete factory's model path *looks* correct against the [VERIFY]'d APIs, but it is **unverified against a live `GraphicsDevice`**. P0 cannot be called fully closed until a developer runs the app on a GPU. This is an environment constraint, not a code defect, and was explicitly permitted by the batch — hence APPROVED — but it must be surfaced, not buried.
2. **Concrete `StrideVisualFactory` procedural path creates a mesh-less entity** (`StrideVisualFactory.cs:131-165`) — a procedural capsule/box would be invisible even on a GPU. P0's demo entities all use model refs (mannequinModel / Box2x1x1), so the smoke is unaffected, but the procedural fallback (design §6.5 "runnable before/without real art") is not truly functional. → STR-D9.
3. **`CreateModelVisual` swallows `Content.Load` failures into a placeholder** (`StrideVisualFactory.cs:94-102`) with only `Debug.WriteLine`. This is the fail-quiet pattern the DEV-GUIDE forbids — on a real run a missing/miscompiled asset would silently show placeholders instead of failing loud. → STR-D10.

## Test Quality
Strong and honest. Logic is verified via recording fakes asserting real call args/values; the full FDP spawn pipeline is exercised with the real UrbanCombat templates. The "RealGpu_BringUp_DocumentedAttempt" test only asserts the concrete factory compiles — correctly framed as documentation, not as a passing GPU proof.

## Verdict
APPROVED. Phase 0 code is complete (T1–T8). **Carried obligation:** a manual GPU run of `HrotStrideApp` to confirm models actually render (closes STR-D4); recommend doing this before/at P1 on a developer machine. Proceeding to Phase 1 (Bullet movement + reverse-sync), which will (a) introduce the togglable groups (STR-D5) and (b) consume `StrideVisualReference`'s shape in `PhysicsBodyLifecycleSystem`.

## Commit Message
```
feat(stride): StrideVisualBindingSystem + end-to-end UrbanCombat spawn/visual smoke (BATCH-03)

Completes STR-P0-T7, STR-P0-T8 — Phase 0 code complete
- StrideVisualBindingSystem + StrideVisualReference: resolve StrideRenderModelDefDto via
  TkbIdentity.TkbType -> TkbDatabase; model-vs-procedural selection; "0=>default" shape sizing
  from PhysicsCollider/VehicleParametersDto; two-pass IsAlive/query reconciliation
- IStrideVisualFactory seam keeps binding logic GPU-free; concrete StrideVisualFactory
  (Content.Load<Model> + ModelComponent + AnimationComponent) wired into EditorStrideSubsystem
- EditorStrideSubsystem: real UrbanCombat TKB templates (replaces TestUnit placeholder); P0
  forward-sync places visuals at FdpStrideTransform.ToStride(SimTransform) (P1-T6 seam marked)
Tests: 86 (65 Core incl. 14 binding, 4 Animation, 17 Game incl. 12 T8 integration). Real GPU
  render proof blocked (no headless GPU) — carried as manual-verification obligation (STR-D4).
```
