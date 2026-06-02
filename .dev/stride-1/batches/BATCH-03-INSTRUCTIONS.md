# BATCH-03: Visual binding + end-to-end spawn & render smoke (P0 capstone)
**Tasks:** STR-P0-T7, STR-P0-T8   **Phase:** P0 (Scaffolding)   **Est:** ~10–12h
**Dependencies:** BATCH-01 (`FdpStrideTransform`), BATCH-02 (`EditorStrideSubsystem`, `StrideHrotGame`, `StrideHostLoopDriver`).

Goal: (T7) `StrideVisualBindingSystem` instantiates a Stride visual + records a `StrideVisualReference` per entity from its `StrideRenderModelDefDto` (real model or procedural primitive), and (T8) wire it into `editor_stride`, spawn the **real UrbanCombat demo entities**, and prove they reconcile into Stride visuals at swizzled positions with movement from the P0 integrator stub. This completes Phase 0 (design §14 step 0). It also discharges the deferred GPU/asset-pipeline verification (STR-D4) and the real TKB/scenario obligation (STR-D8).

There is **no Corrective Task 0** — BATCH-02 was approved with no P1 issues. Fold these open debt items into this batch: **STR-D4** (prove the Stride app/asset pipeline by actually booting/rendering), **STR-D8** (use the real UrbanCombat TKB templates instead of the `TestUnit` placeholder).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — working contract (test-quality section binding).
2. `.dev/stride-1/Stride-Integration_v0_3.md` §6.5 (the spec for T7 — `StrideRenderModelDefDto` + `StrideVisualBindingSystem`), §7 (two-pass reconciliation / Pass-A), §14 step 0 (T8 scope), §12 (the demo TKB content already on the UrbanCombat templates).
3. `.dev/stride-1/TASK-DETAIL.md` — STR-P0-T7, STR-P0-T8 (success conditions authoritative).
4. `.dev/stride-1/reviews/BATCH-02-REVIEW.md` + `DEBT-TRACKER.md` (STR-D4, STR-D8 context).

Use the **codebase-memory MCP first** (project `D-Work-IOS-IG-SimHost-FDP`).

### Verified facts & exact references
- **`StrideRenderModelDefDto`** already exists: [StrideRenderModelDefDto.cs](../../../FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/StrideRenderModelDefDto.cs) — fields `ModelAssetRef`, `SkeletonAssetRef`, `Scale`, `Offset{X,Y,Z}`, `ShapeKind` (`CollisionShapeKind`), `ShapeRadius`, `ShapeHeight`, `BoxHalf{X,Y,Z}` (the "0 ⇒ default from …" rules are in the doc comments — implement them).
- **Reconciliation pattern to reuse** = the mock's two-pass differential sync in [SyncFdpToStrideScript.cs](../../../Hrot/Subsystems/Hrot.StrideMock/SyncFdpToStrideScript.cs): Pass 1 collect stale keys via `world.IsAlive(entity)` into a reused list and remove; Pass 2 query live entities (`view.Query().With<SimTransform>()…Build()`) and upsert. Key the dictionary by `Entity`. **Reuse this structure** for the Stride-side visual set in T7/T8 (it manages existence; it is orthogonal to transform direction).
- **Demo TKB content** is registered by `UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates` in `Fdp.Examples.Scenarios` ([UrbanCombatNewScenario.cs](../../../FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs)) — it attaches a `StrideRenderModelDefDto` to `CivilianPedestrian`/`InfantrySoldier`/`Insurgent` (mannequin + capsule) and `CivilianCar`/`MilitaryAPC` (Box2x1x1 + oriented box). T8 uses this in place of BATCH-02's `TestUnit` placeholder.
- **Descriptor resolution [VERIFY]:** the entity's `tkbType` is on its `EntityMaster` (see `DescriptorMapper_ExtractsTkbType_FromEntityMaster` test) and the typed descriptor is fetched from the TKB/`DescriptorBag` by tkbType. Confirm the exact API to go *entity → tkbType → `StrideRenderModelDefDto`* at runtime and use it; record what you find.
- Shape-default sources: `PhysicsCollider` ([PhysicsComponents.cs](../../../FDP/Toolkits/Fdp.Toolkits/Physics/Components/PhysicsComponents.cs), has `.Radius`) and `VehicleParametersDto` ([VehicleParametersDto.cs](../../../FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/VehicleParametersDto.cs), Length/Width).
- `FdpStrideTransform.ToStridePosition` / `ToStrideRotation` (BATCH-01) are the only allowed swizzle; do not hand-roll.
- The P0 movement source is the existing FDP integrator stub already wired in `EditorStrideSubsystem` (`SimHostCoreLogicPack`). T8 does **not** add Bullet (P1).

**Complete tasks in sequence; do NOT start T8 until T7 is implemented, tested, and ALL tests (incl. BATCH-01/02) pass.** Work autonomously; fix root causes. Only stop on a genuine breaking design flaw or unrecoverable blocker.

---

## Task 1: `StrideVisualBindingSystem` + `StrideVisualReference` + procedural fallback (STR-P0-T7)
**Files:** `Stride/Hrot.Stride.Core/StrideVisualBindingSystem.cs`, `Stride/Hrot.Stride.Core/StrideVisualReference.cs` (NEW). Spec: design §6.5.

**Mandatory testable seam.** The actual Stride-entity instantiation (`Content.Load<Model>(url)`, clone under a parent, attach `ModelComponent`/`AnimationComponent`, build a procedural primitive mesh) requires a `GraphicsDevice`/content and cannot run headlessly. **Abstract it** behind an interface in `Hrot.Stride.Core`, e.g.:
```
interface IStrideVisualFactory {
    object CreateModelVisual(string modelRef, string skeletonRef, float scale, Vector3 offsetFdp, in SimTransform initialPose);
    object CreateProceduralVisual(CollisionShapeKind kind, ShapeDims dims, float scale, Vector3 offsetFdp, in SimTransform initialPose);
    void UpdatePose(object visualHandle, in SimTransform pose);   // places at FdpStrideTransform.ToStride(pose)
    void Destroy(object visualHandle);
}
```
(Refine the signatures as needed — the point is the *decision/reconciliation/sizing/swizzle* logic lives in the system and is testable with a recording fake; the GPU work lives in a concrete `StrideVisualFactory` you also write, exercised by the T8 real run.)

`StrideVisualBindingSystem` must:
- Resolve the entity's class `StrideRenderModelDefDto` (entity → tkbType → descriptor, [VERIFY] path). If the entity's class has **no** descriptor, skip it (no visual) — do not throw.
- On entity-appear (Pass-2 of the reconciliation): pick **model** (`ModelAssetRef` non-empty → `CreateModelVisual`, passing `SkeletonAssetRef` for skinned) vs **procedural** (`ModelAssetRef` empty → `CreateProceduralVisual` matching `ShapeKind`); compute shape dims applying the "0 ⇒ default" rules (`ShapeRadius`←`PhysicsCollider.Radius`; `BoxHalf{X,Y,Z}`←`VehicleParametersDto` Length/Width + `ShapeHeight`); apply `Scale`/`Offset`. Record a `StrideVisualReference` (ECS entity ↔ visual handle + the resolved shape, so P1 `PhysicsBodyLifecycleSystem` can read it). Place the visual at `FdpStrideTransform.ToStride(SimTransform)`.
- On entity-death (Pass-1): `factory.Destroy(handle)` + remove `StrideVisualReference`.

**Tests required** (headless, recording fake factory — assert real values/calls):
- ModelAssetRef = `"Models/mannequinModel"` (+ skeleton ref) → exactly one `CreateModelVisual` with that `modelRef` and the skeleton ref passed; `StrideVisualReference` added linking the entity to the returned handle.
- Empty ModelAssetRef + `ShapeKind=Capsule`, `ShapeRadius=0` while the entity has `PhysicsCollider.Radius=R` → `CreateProceduralVisual(Capsule, dims)` where the radius resolved to `R` (assert the number). Same for `OrientedBox` defaulting half-extents from `VehicleParametersDto` Length/Width.
- Explicit non-zero `ShapeRadius`/`BoxHalf*` override the defaults (assert the explicit value wins).
- Pose placement: `UpdatePose` (or create) receives the **swizzled** transform — assert the Stride position equals `FdpStrideTransform.ToStridePosition(SimTransform.Position)` for a known input.
- Reconciliation: spawning a new matching entity calls create exactly once (idempotent across ticks — not re-created each frame); destroying it calls `Destroy` exactly once and removes `StrideVisualReference`; an entity with no descriptor yields no factory calls.

## Task 2: End-to-end spawn & render smoke (STR-P0-T8)
**Files:** wire into `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs` + a P0 forward-sync (new small type) + `Stride/HrotStrideApp.Game/StrideVisualFactory.cs` (NEW, the concrete GPU factory) + `StrideHrotGame` wiring. Spec: design §14 step 0, §7 (Pass-A).
- Replace BATCH-02's `TestUnit` TKB placeholder with the **real** `UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates` path (add the `Fdp.Examples.Scenarios` reference) so spawned entities carry `StrideRenderModelDefDto` (discharges STR-D8). Keep the headless boot working.
- Register `StrideVisualBindingSystem` in the boot, fed by the concrete `StrideVisualFactory` in the real game and by a fake in tests.
- Add a **P0 forward-sync** that, each frame, updates every visual's pose from its entity's `SimTransform` via `FdpStrideTransform` (all entities are owned in Mode-1; this is the simple P0 stand-in for the P1 `SplitAuthorityStrideSyncScript` — leave a clear comment that P1-T6 replaces it with the authority-forked version). Movement comes from the existing `SimHostCoreLogicPack` integrators (the P0 stub).

**Tests required:**
- **Integration (headless, fake factory):** spawn **N** UrbanCombat demo entities through the Brain spawn path (`OwnerNodeId=0`); after pumping frames, assert N visuals were created, each placed at `FdpStrideTransform.ToStride(entity.SimTransform)` (assert positions), and that infantry classes resolve a model visual while vehicle/empty-ref classes resolve the right procedural/oriented-box shape. Destroying an entity removes its visual (reconciliation). 
- **Single-thread invariant:** capture `Environment.CurrentManagedThreadId` inside the fake factory's create/update callbacks and assert it equals the test/boot thread across all ticks (no second thread touches the repository/visual set). Assert the boot+tick code path contains no `Task.Run`/`new Thread` against the repository (inspection note acceptable in the report, but the thread-id assertion is the test).
- **Real GPU/asset proof (discharge STR-D4 + `StrideHrotGame` GPU obligation):** attempt to actually bring up `StrideHrotGame` with the concrete `StrideVisualFactory` and prove a real `ModelComponent` appears for `Models/mannequinModel` (+ skeleton present) and a procedural capsule for an empty `ModelAssetRef`, per T7's stated success conditions. Use a Stride off-screen/`GameTestBase`-style harness if one is available on 4.2.1.2487; **[VERIFY]** how to load a Stride asset at runtime and instantiate a `ModelComponent`/`AnimationComponent`. If a `GraphicsDevice` genuinely cannot be created in this environment, document *exactly* what was attempted and what failed, and rely on the fake-factory tests + the concrete factory being compiled/exercised as far as possible — but make a real, documented attempt; do not silently skip it.

---

## Success Criteria
- [ ] STR-P0-T7: `StrideVisualBindingSystem` + `StrideVisualReference` implemented per §6.5 with the testable factory seam; model vs procedural selection, "0 ⇒ default" shape sizing, Scale/Offset, swizzled placement, and create/destroy reconciliation all proven by fake-factory tests.
- [ ] STR-P0-T8: UrbanCombat demo entities spawn through the Brain path and reconcile into N visuals at swizzled positions; reconciliation add/remove verified; single-thread invariant asserted via thread-id; a real Stride GPU/asset bring-up attempted and its outcome (model + procedural primitive appearing, or the precise blocker) documented.
- [ ] Full test suite green (BATCH-01/02/03); Stride solution builds clean (no new warnings beyond pre-existing NU1608); report submitted.

## Report Requirements (`reports/BATCH-03-REPORT.md`)
Answer: the exact runtime descriptor-resolution path (entity→tkbType→`StrideRenderModelDefDto`) and the Stride asset-load/instantiate API ([VERIFY] results, with symbol names); the factory-seam design and how it kept the binding logic GPU-free; **the real GPU/asset bring-up — what you attempted, whether a `ModelComponent`/procedural primitive actually appeared, and if not, the precise blocker** (this is the headline question — STR-D4); how UrbanCombat templates were wired in and any `Fdp.Examples.Scenarios` friction (STR-D8); whether the togglable-group gap (STR-D5) affected anything here; the single-thread-invariant test approach; weak points; suggested one-line commit message. Report actual test counts/output. Do NOT ask comprehension questions.
