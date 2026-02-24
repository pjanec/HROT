# Technical Debt & Deferred Issues Tracker

Tracks P2/P3 issues, known risks, and design decisions deferred from batch reviews.  
**P1 issues are never deferred** — they become Corrective Task 0 in the next batch.

Update this file when an item is resolved. Do not delete resolved rows — mark them ✅.

---

## How to Use

- **Dev lead:** during each review, add any new P2/P3 items here before writing the next batch.  
- **Developer:** check this file during onboarding. If your batch touches a file mentioned here, fix the relevant item even if it wasn't explicitly assigned.
- **Priority:** P2 = fix within the next 1–2 batches; P3 = fix before Phase complete or whenever the area is touched.

---

## Open Items

| ID | Sev | Source | Description | Target | Status |
|---|---|---|---|---|---|
| DEBT-006 | P2 | BATCH-04-REVIEW | `DoctrineRegistry` keys on `string.GetHashCode()` — process-randomised. Serialised doctrine IDs non-reproducible across runs. Needs stable key (CRC32 or assigned `int`). | Phase 5 networking batch | 🔴 Open |
| DEBT-007 | P2 | BATCH-04-REPORT | `FdpHsmContext` carries only `Entity Self` — HSM action delegates cannot access ECS world. Strategy needed before Phase 6. | Phase 6 batch | 🔴 Open |
| DEBT-008 | P3 | BATCH-04-REPORT | `DoctrineIngressSystem` — no try/catch around `ParseParams`. Malformed JSON throws with no entity context. | Any batch touching DoctrineIngressSystem | 🔴 Open |
| DEBT-021 | P2 | BATCH-08-REVIEW (Q4) | `RaycastSolverSystem` — no bounds check on `batch.Count`; overflow → `IndexOutOfRangeException`. Add `Math.Min(batch.Count, PhysicsConstants.RaycastBatchCapacity)` before `Parallel.For`. Add `Debug.Assert` at fill sites. | BATCH-09 | 🔴 Open |
| DEBT-022 | P3 | BATCH-08-REVIEW | `Intersection2DTests` — missing degenerate boundary case: segment starts exactly at circle edge (t=0). | Any batch touching Intersection2D | 🔴 Open |
| DEBT-023 | P2 | BATCH-08-REVIEW (Q3) | `HitEvent` temporarily in `FDP.Toolkit.Physics`. Must move to `FDP.Toolkit.Combat` in Phase 5. | BATCH-10 (Combat batch) | 🔴 Open |
| DEBT-024 | P2 | BATCH-08-REVIEW (Q1) | `DispatcherSystemBase` — no `OnExit` on entity destruction; per-entity executor state can leak. Full fix requires kernel lifecycle hook. | Phase 5+ | 🔴 Open |
| DEBT-025 | **P1** | BATCH-08-REVIEW (external lead) | `SimTransformBridgeSystem.UpdateEntity` hardcodes `PitchDeg = 0f`, `RollDeg = 0f`. All non-level entities have orientation stripped before egress. Add `RotationToPitchRollDeg` static helper + call from `UpdateEntity`. | BATCH-09 Corrective 0 | 🔴 Open |
| DEBT-026 | P2 | BATCH-08-REVIEW (code review) | `RaycastSolverSystem` — `stackalloc` candidate buffer capped at 64; entities beyond that are silently dropped. Undocumented. Add `PhysicsConstants.MaxBroadphaseCandidates = 64` constant and a doc comment explaining the cap and implication. | BATCH-09 | 🔴 Open |
| DEBT-027 | P2 | BATCH-08-REVIEW (code review) | `HitResolutionSystem` emits `TargetVisibleEvent` with raw `int` indices (ObserverEntityIndex, TargetEntityIndex). If an entity is recycled between LOS submission and event consumption, the wrong entity's threat memory is updated. LOS pipeline should carry full `Entity` handles. | BATCH-09 or when LOS pipeline is reworked | 🔴 Open |
| DEBT-028 | P2 | BATCH-08-REVIEW (test review) | `Intersection2DTests` Test 4 (`ReturnsTMin_WhenTwoIntersections`) is functionally identical to Test 1 — same geometry, same assertion range. Doesn't actually prove the min-is-returned-not-max behaviour. Use a geometry where entry and exit t values are well-separated (e.g. r=4, 10-unit ray). | BATCH-09 | 🔴 Open |

---

## Resolved Items (archive)

| ID | Sev | Description | Resolved In |
|---|---|---|---|
| DEBT-001 | P2 | `SpatialHashSystem` no integration tests for non-vehicle entities | BATCH-06 |
| DEBT-002 | P2 | `IActionExecutor<T>.Execute` status-write contract undocumented | BATCH-04 |
| DEBT-003 | P3 | `OnExit` field-state invariant undocumented | BATCH-04 |
| DEBT-004 | P2 | `DoctrineIngress` Test 4 missing assertion | BATCH-05 |
| DEBT-005 | P2 | `InputSystemGroup` cross-group ordering undocumented | BATCH-05 |
| DEBT-009 | P1 | `SpatialHashGrid` stored raw `int` entity indices | BATCH-06 |
| DEBT-010 | P1 | `EntityRepository.GetEntity(int)` was `public` | BATCH-06 |
| DEBT-011 | P1 | `VisionBroadphaseSystem` O(N×M) brute force + async data-race | BATCH-06 |
| DEBT-012 | P2 | `VisionBroadphaseSystemTests` hardcoded `0.866f` literal | BATCH-06 |
| DEBT-013 | P2 | `ThreatEvaluationSystemTests` missing boost-path and zero-score tests | BATCH-06 |
| DEBT-014 | P3 | `AudioPerceptionSystemTests` Test 2 used non-existent entity index | BATCH-06 |
| DEBT-015 | P2 | Zero-score retention policy unverified against DESIGN.md §4.3 | BATCH-07 |
| DEBT-016 | P2 | `MoveToExecutor` frustration test used hardcoded `120` | BATCH-07 |
| DEBT-017 | P3 | `FollowRouteParams` comment misleading re struct padding | BATCH-07 |
| DEBT-018 | P2 | `MoveToExecutor._stuckTicks` — fallback `IsAlive` guard added | BATCH-08 |
| DEBT-019 | P3 | `DispatcherSystemBase` missing same-frame OnEnter+Execute safety comment | BATCH-08 |
| DEBT-020 | P2 | `FollowRoadGraphExecutorTests` assertions verified (all three present) | BATCH-08 |

---

## Notes

- **DEBT-025** is the immediate P1 for BATCH-09. The fix is localised to `SimTransformBridgeSystem.cs` and its test file.
- **DEBT-021, DEBT-026, DEBT-028** are quick fixes; do them before touching Phase 5 code.
- **DEBT-027** requires a design decision: the LOS event chain (`LosCheckRequestEvent` → `RaycastRequest` → `RaycastHit` → `TargetVisibleEvent`) currently passes raw int indices end-to-end. Moving to full `Entity` handles would require changing the `RayId` packing or adding separate fields. This is the same architectural issue as DEBT-009 but in the event pipeline instead of a data structure.
- **DEBT-023** must be done during the Combat batch — do not move `HitEvent` prematurely.
- **DEBT-006** and **DEBT-007** are Phase 5/6 concerns. Do not refactor early.
