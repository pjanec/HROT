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
| DEBT-001 | P2 | BATCH-01-REVIEW | `SpatialHashSystem` has no integration tests for non-vehicle entities (`SimTransform` but no `VehicleState`). Perception entities now exist — natural moment to add this test to `FDP.Toolkit.CarKinem.Tests`. | BATCH-06 | 🔴 Open |
| DEBT-006 | P2 | BATCH-04-REVIEW | `DoctrineRegistry` keys on `string.GetHashCode()` — process-randomised in .NET. Self-consistent within a process, but serialised/networked doctrine IDs will be non-reproducible across runs. Needs a stable `DoctrineId` (CRC32 or manually assigned `int`). | Phase 5 / networking batch | 🔴 Open |
| DEBT-007 | P2 | BATCH-04-REPORT | `FdpHsmContext` carries only `Entity Self` — HSM action delegates cannot access the ECS world. Phase 3 must define a strategy: thread-local world reference, event queue, or service locator injection. Decide before writing the first HSM executor test. | Phase 3 batch | 🔴 Open |
| DEBT-008 | P3 | BATCH-04-REPORT | `DoctrineIngressSystem` passes `JsonParams` to `ParseParams` with no try/catch. Malformed string throws inside delegate with no entity context. Add try/catch + entity index logging in `#if DEBUG`. | Any batch touching DoctrineIngressSystem | 🔴 Open |
| DEBT-009 | **P1** | BATCH-05-REVIEW | `SpatialHashGrid` stores raw `int` entity indices — bypasses generational safety. Must store full `Entity` structs. `Add(int, Vector2)` → `Add(Entity, Vector2)`. `QueryNeighbors` returns `Span<(Entity entity, Vector2 pos)>`. Breaking change — all callers must update in the same batch. | BATCH-06 | 🔴 Open |
| DEBT-010 | **P1** | BATCH-05-REVIEW | `EntityRepository.GetEntity(int)` is `public` — implicit invitation to store raw indices, bypassing generational safety. Must be `internal`. Add XML doc for the only valid use cases (C++ interop, kernel bit-scanning). Causes compiler errors in toolkit callers — clean up alongside DEBT-009. | BATCH-06 | 🔴 Open |
| DEBT-011 | **P1** | BATCH-05-REVIEW | `VisionBroadphaseSystem` uses O(N×M) brute force because sharing the main-thread `SpatialHashGrid` across the async boundary causes a data race (shallow copy of `NativeArray<T>` pointers). Fix: `PerceptionModule` owns a private `SpatialHashGrid`, rebuilt each tick by `LocalGridBuilderSystem`; `VisionBroadphaseSystem` queries the private grid. Full code pattern provided by architect in user request preceding BATCH-05. | BATCH-06 | 🔴 Open |
| DEBT-012 | P2 | BATCH-05-REVIEW | `VisionBroadphaseSystemTests` tests 2 and 3 hardcode `FieldOfViewCos = 0.866f` — raw float literal instead of `MathF.Cos(MathF.PI / 6f)` or a named constant. Inconsistent with tests 1 and 4. | BATCH-06 | 🔴 Open |
| DEBT-013 | P2 | BATCH-05-REVIEW | `ThreatEvaluationSystemTests` has only 1 test (decay path). Missing: (a) boost-path test — `TargetVisibleEvent` increases score; (b) zero-score eviction test or doc comment clarifying retention policy. | BATCH-06 | 🔴 Open |
| DEBT-014 | P3 | BATCH-05-REVIEW | `AudioPerceptionSystemTests` Test 2 uses `SourceEntityIndex = 99` — non-existent entity. Harmless because `Count == 0` in that test, but dangerous pattern if copied to a positive-path test. Replace with a real entity index. | BATCH-06 | 🔴 Open |

---

## Resolved Items (archive)

| ID | Sev | Description | Resolved In |
|---|---|---|---|
| DEBT-002 | P2 | `IActionExecutor<T>.Execute` status-write contract undocumented | BATCH-04 |
| DEBT-003 | P3 | `OnExit` field-state invariant undocumented | BATCH-04 |
| DEBT-004 | P2 | `DoctrineIngress` Test 4 missing `channel.DoctrineInstanceId == 0` assertion | BATCH-05 |
| DEBT-005 | P2 | `InputSystemGroup` cross-group ordering by convention only — no doc comment | BATCH-05 |

---

## Notes

- **DEBT-009 + DEBT-010 + DEBT-011** must all be resolved in the same batch. They are architecturally coupled: once `SpatialHashGrid` returns `Entity` structs and `GetEntity(int)` is internal, `AudioPerceptionSystem` and `VisionBroadphaseSystem` must be updated together or the build will not compile.
- **DEBT-006** does not affect correctness until Phase 5. Do not refactor early — but do not build more code that assumes hash-key stability.
- **DEBT-007** is architecturally significant. The chosen ECS-access strategy for HSM delegates will determine how all Phase 3 executors are written. Decide before the first HSM executor test is written.
