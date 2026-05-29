# TASK-TRACKER — 3D Cognitive Spatial Awareness Promotion

Progress checklist. Cross-reference: [TASK-DETAIL.md](./TASK-DETAIL.md),
[3D_Cognitive_Spatial_Awareness_Promotion_Design_v1_1.md](./3D_Cognitive_Spatial_Awareness_Promotion_Design_v1_1.md),
[DEBT-TRACKER.md](./DEBT-TRACKER.md).

> **One atomic PR (Design §7).** Phases are a build/review order, not separate merges. The whole set
> merges together behind the Phase 4 gate (P3D-403 + P3D-402 + P3D-104). Status is binary: `[ ]` not done
> / `[x]` done.

---

## Phase 0 — Baseline capture (run BEFORE any change)

- [x] **P3D-001** Capture flat-terrain golden `EqsCognitiveBuffer` baseline (Axis-1) [details](./TASK-DETAIL.md#p3d-001--capture-flat-terrain-golden-eqscognitivebuffer-baseline)

## Phase 1 — Tier 1: Authoritative altitude (the root)

- [x] **P3D-101** Slim `GroundClampingState` → terrain-clamp baseline component [details](./TASK-DETAIL.md#p3d-101--slim-groundclampingstate-to-a-terrain-clamp-baseline-component)
- [x] **P3D-102** `TerrainQueryResolutionSystem` writes `HitZ` → authoritative `SimTransform.Position.Z` [details](./TASK-DETAIL.md#p3d-102--terrainqueryresolutionsystem-writes-hitz-into-authoritative-simtransformpositionz)
- [x] **P3D-103** `TransformSyncSystem` stops applying the visual-only Z correction [details](./TASK-DETAIL.md#p3d-103--transformsyncsystem-stops-applying-the-visual-only-z-correction)
- [x] **P3D-104** Dead-reckoning regression fixture on slopes/steps (Axis-3 risk probe) [details](./TASK-DETAIL.md#p3d-104--dead-reckoning-regression-fixture-on-slopes-and-steps-axis-3-risk-probe)

## Phase 2 — Tier 2: Cognitive carriers read the real Z

- [x] **P3D-201** Widen `EqsResult` to carry `PositionZ` (24 B → 32 B) + buffer footprint [details](./TASK-DETAIL.md#p3d-201--widen-eqsresult-to-carry-positionz-24-b--32-b-and-update-the-buffer)
- [x] **P3D-202** Widen `EqsResultEntry` DDS wire + EQS result translators to carry altitude [details](./TASK-DETAIL.md#p3d-202--widen-the-eqsresultentry-dds-wire--eqs-result-translators-to-carry-altitude)
- [x] **P3D-203** Generators retain real Z (the 3 existing production generators) [details](./TASK-DETAIL.md#p3d-203--generators-retain-real-z-the-three-existing-production-generators)
- [x] **P3D-204** Widen `CoverPoint` + `ICoverProvider` family to 3D [details](./TASK-DETAIL.md#p3d-204--widen-coverpoint--icoverprovider-family-to-3d)
- [x] **P3D-205** Scoring/filter tests use real Z (correct axis mapping) [details](./TASK-DETAIL.md#p3d-205--scoringfilter-tests-use-real-z-with-correct-axis-mapping)
- [x] **P3D-206** Widen `TargetMemory` to 3D; `ThreatEvaluationSystem` passes real Z [details](./TASK-DETAIL.md#p3d-206--widen-targetmemory-to-3d-contacts-threatevaluationsystem-passes-real-z)

## Phase 3 — Tier 3: Cost, destination, and trajectory symmetry

- [x] **P3D-301** `PathCost` uniformly 3D (`StubNavmeshProvider`) [details](./TASK-DETAIL.md#p3d-301--pathcost-uniformly-3d-stubnavmeshprovider)
- [x] **P3D-302** Widen the navigation destination/intent chain to `Vector3` [details](./TASK-DETAIL.md#p3d-302--widen-the-navigation-destinationintent-chain-to-vector3)
- [x] **P3D-303** Trajectory pool stores + interpolates Z (steering stays 2D) [details](./TASK-DETAIL.md#p3d-303--trajectory-pool-stores--interpolates-z-steering-stays-2d)
- [x] **P3D-304** Navigation egress/ingress translators carry real altitude [details](./TASK-DETAIL.md#p3d-304--navigation-egressingress-translators-carry-real-altitude)

## Phase 4 — Presentation, proof, and the merge gate

- [x] **P3D-401** Presentation renders/uses Z (`EqsCognitiveBufferRenderer`, `EqsSensorGizmo`) [details](./TASK-DETAIL.md#p3d-401--presentation-rendersuses-z)
- [x] **P3D-402** Multi-level proof fixture (Axis-2; deck clearance > `walkableHeight`) [details](./TASK-DETAIL.md#p3d-402--multi-level-proof-fixture-axis-2)
- [x] **P3D-403** Flat-terrain parity gate (Axis-1) — **merge gate** [details](./TASK-DETAIL.md#p3d-403--flat-terrain-parity-gate-axis-1)
- [x] **P3D-404** Mandatory `, 0f)` / `Position.Z` pre-merge grep sweep (O-3) [details](./TASK-DETAIL.md#p3d-404--mandatory--0f--positionz-pre-merge-grep-sweep-o-3)
- [x] **P3D-405** Flight Recorder break coordination + schema version bump [details](./TASK-DETAIL.md#p3d-405--flight-recorder-break-coordination--schema-version-bump)

---

## Out of scope (tracked elsewhere)

- **Utility AI `TargetMemory` readers** → `../group-maneuvers/Step_1_5_TargetMemory_3D_Reconciliation.md`
  (runs AFTER this promotion and Utility AI both merge).
- **Squad Coordination** (the dependent) → `../group-maneuvers/Squad_Coordination_Design_v1_1.md`.
- **Non-existent EQS generators** (`Self`/`Donut`/`Grid`/`Cone`/`EntitiesInArea`/`OffsetFromContext`) —
  not implemented in the codebase; see TASK-DETAIL §0.3.
- **3D vehicle dynamics** (slope-aware Hermite/Catmull-Rom curvature + `CarKinematicsSystem` steering) —
  deliberately deferred; this PR carries Z but steers in 2D (TASK-DETAIL §0.2).
