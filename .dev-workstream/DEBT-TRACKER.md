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
| DEBT-006 | P2 | BATCH-04-REVIEW | `DoctrineRegistry` keys on `string.GetHashCode()` — process-randomised. Serialised doctrine IDs non-reproducible across runs. Needs stable key (CRC32 or assigned `int`). | BATCH-13 ✅ | ✅ Resolved |
| DEBT-007 | P2 | BATCH-04-REPORT | `FdpHsmContext` carries only `Entity Self` — HSM action delegates cannot access ECS world. Strategy needed before Phase 7 Demo App. | BATCH-13 ✅ partial; HSM delegate stubs remain | 🟡 Partial — BTree path resolved; HSM action delegates (`ApcHsmActions`) still stubs. `EntityRepository` cannot pass through `void* context` (unmanaged constraint). Fix: `ApcBrainOutputSystem` bridge reads HSM state index and writes channels externally. BATCH-17. |
| DEBT-008 | P3 | BATCH-04-REPORT | `DoctrineIngressSystem` — no try/catch around `ParseParams`. Malformed JSON throws with no entity context. | BATCH-13 ✅, BATCH-14 ✅ (DEBT-035) | ✅ Resolved |
| DEBT-021 | P2 | BATCH-08-REVIEW (Q4) | `RaycastSolverSystem` — no bounds check on `batch.Count`; overflow → `IndexOutOfRangeException`. Add `Math.Min(batch.Count, PhysicsConstants.RaycastBatchCapacity)` before `Parallel.For`. Add `Debug.Assert` at fill sites. | BATCH-09 ✅ | ✅ Resolved |
| DEBT-022 | P3 | BATCH-08-REVIEW | `Intersection2DTests` — missing degenerate boundary case: segment starts exactly at circle edge (t=0). | BATCH-13 ✅ | ✅ Resolved |
| DEBT-023 | P2 | BATCH-08-REVIEW (Q3) | `HitEvent` temporarily in `FDP.Toolkit.Physics`. Must move to `FDP.Toolkit.Combat` in Phase 5. | BATCH-09 ✅ | ✅ Resolved |
| DEBT-024 | P2 | BATCH-08-REVIEW (Q1) | `DispatcherSystemBase` — no `OnExit` on entity destruction; per-entity executor state can leak. Full fix requires kernel lifecycle hook. | BATCH-13 ✅ partial mitigation | ✅ Resolved (partial) |
| DEBT-025 | **P1** | BATCH-08-REVIEW (external lead) | `SimTransformBridgeSystem.UpdateEntity` hardcodes `PitchDeg = 0f`, `RollDeg = 0f`. All non-level entities have orientation stripped before egress. Add `RotationToPitchRollDeg` static helper + call from `UpdateEntity`. | BATCH-09 ✅ | ✅ Resolved |
| DEBT-026 | P2 | BATCH-08-REVIEW (code review) | `RaycastSolverSystem` — `stackalloc` candidate buffer capped at 64; entities beyond that are silently dropped. Undocumented. Add `PhysicsConstants.MaxBroadphaseCandidates = 64` constant and a doc comment explaining the cap and implication. | BATCH-09 ✅ | ✅ Resolved |
| DEBT-027 | P2 | BATCH-08-REVIEW (code review) | `HitResolutionSystem` emits `TargetVisibleEvent` with raw `int` indices (ObserverEntityIndex, TargetEntityIndex). If an entity is recycled between LOS submission and event consumption, the wrong entity's threat memory is updated. LOS pipeline should carry full `Entity` handles. | BATCH-09 (comment added) — full fix deferred to LOS pipeline rework | 🟡 Partial |
| DEBT-028 | P2 | BATCH-08-REVIEW (test review) | `Intersection2DTests` Test 4 (`ReturnsTMin_WhenTwoIntersections`) is functionally identical to Test 1 — same geometry, same assertion range. Doesn't actually prove the min-is-returned-not-max behaviour. Use a geometry where entry and exit t values are well-separated (e.g. r=4, 10-unit ray). | BATCH-09 ✅ | ✅ Resolved |
| DEBT-031 | P3 | BATCH-10-REVIEW (Issue 4) | `HitEvent` now lives in `Fdp.Kernel` — a combat game event in the engine core layer. Violates kernel purity. Should move to `FDP.Toolkit.Combat.Contracts` (thin events-only assembly) or back to Combat once Physics no longer depends on it. | BATCH-13 ✅ | ✅ Resolved |
| DEBT-032 | P2 | BATCH-10-REPORT (Q1) | `LinearKinematicsSystem` referenced by `BallisticsSystem` design but does not exist. Blocks correct ordering attribute for `BallisticsSystem`. Implement in BATCH-11. | BATCH-11 ✅ | ✅ Resolved |
| DEBT-033 | P2 | BATCH-11-REVIEW | `MissionDirectorSystem.HealthCritical` trigger not implemented: `FDP.Toolkit.Behavior` cannot reference `FDP.Toolkit.Combat` (circular dependency — Combat references Behavior for `ActorCapabilityState`). Requires shared health interface in `Fdp.Kernel` or assembly restructure. | BATCH-13 ✅ | ✅ Resolved |
| DEBT-034 | P3 | BATCH-12-REVIEW | `EjectPassengersExecutor` XML doc comment describes symmetric slot offsets (e.g. ±0.75 m for Count=2) but the actual formula produces asymmetric offsets (−1.5 m and 0.0 m). Fix the comment to match the actual computed values. | BATCH-13 ✅ | ✅ Resolved |
| DEBT-035 | **P1** | BATCH-13-REVIEW (Issue 1) | `DoctrineIngressSystem` try/catch added (DEBT-008) but DoctrineState writes occur BEFORE the try block. A `ParseParams` failure leaves the entity in a partial doctrine transition (hash+InstanceId bumped, BTree reset, but blackboard zero). Must reorder: attempt `ParseParams` first (inside try), then write `DoctrineState`/`BrainBTreeState` only on success. Add test `DoctrineIngress_DoctrineStateUnchanged_WhenParseParamsFails`. | BATCH-14 Corrective-0 ✅ | ✅ Resolved |
| DEBT-036 | P3 | BATCH-16-REVIEW | `SpatialHashSystem.OnCreate()` uses literals `150`, `150`, `5.0f`, `-375f`, `-375f` (CODE-STANDARDS §1). Add named constants to a `SpatialHashConstants` class or `PhysicsConstants`. | BATCH-17 | 🔴 Open |
| DEBT-037 | P2 | BATCH-16-REVIEW | `ScenarioDirector.cs` line 191 uses `Quaternion.CreateFromYawPitchRoll` — banned by CODE-STANDARDS §2. Replace with `SimMath.FromYaw(yawRadians)`. | BATCH-17 Corrective-0 | 🔴 Open |
| DEBT-038 | P2 | BATCH-16-REVIEW | `TelemetryReporterSystem.cs` defines `private const ushort EjectPassengersActionId = 3` — magic number with no toolkit constant. Add `BehaviorConstants.ActionIdEjectPassengers = 3` in `BehaviorConstants.cs`; remove private const; update `TelemetryReporterSystem` to reference it. Also update `EjectPassengersExecutor` doc comment. | BATCH-17 Corrective-0 | 🔴 Open |

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

- **All P2/P3 debts resolved** (BATCH-13) — DEBT-006, 007, 008(partial→now full via DEBT-035), 022, 024, 031, 033, 034.
- **DEBT-035 (P1):** DoctrineIngressSystem catch ordering bug — fixed as Corrective-0 in BATCH-14. Test `DoctrineIngress_DoctrineStateUnchanged_WhenParseParamsFails` passes.
- **DEBT-008 now fully resolved** via DEBT-035 corrective in BATCH-14.
- **DEBT-027** (raw int LOS indices): lower priority, deferred to when `LosRequestBatchingSystem` is reworked.
- **DEBT-007 REOPENED (Partial):** BATCH-13 resolved the BTree path (`BTreeContext.World` available). BATCH-15/16 confirmed HSM action delegates (`ApcHsmActions`) are still stubs because `EntityRepository` (managed class) cannot be passed through the `void* context` of `Fhsm.Kernel` dispatch. Fix via `ApcBrainOutputSystem` bridge in BATCH-17.
- **DEBT-036/037/038:** New P2/P3 items from BATCH-16 review. Fix as Corrective-0 in BATCH-17.
