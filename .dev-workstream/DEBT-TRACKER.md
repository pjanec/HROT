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
| DEBT-006 | P2 | BATCH-04-REVIEW | `DoctrineRegistry` keys on `string.GetHashCode()` — process-randomised in .NET. Serialised/networked doctrine IDs will be non-reproducible across runs. Needs a stable `DoctrineId` (CRC32 or manually assigned `int`). | Phase 5 / networking batch | 🔴 Open |
| DEBT-007 | P2 | BATCH-04-REPORT | `FdpHsmContext` carries only `Entity Self` — HSM action delegates cannot access the ECS world. Phase 3 must define a strategy: thread-local world reference, event queue, or service locator injection. Decide before writing the first HSM executor test. | Phase 3 batch | 🔴 Open |
| DEBT-008 | P3 | BATCH-04-REPORT | `DoctrineIngressSystem` passes `JsonParams` to `ParseParams` with no try/catch. Malformed string throws inside delegate with no entity context. Add try/catch + entity index logging in `#if DEBUG`. | Any batch touching DoctrineIngressSystem | 🔴 Open |
| DEBT-016 | P2 | BATCH-06-REVIEW | `NavigationActionTests` missing frustration-guard constant linkage — when `MoveToExecutor` is implemented (BCS-P3-T2), the frustration-guard test must assert using `NavigationConstants.FrustrationTickThreshold`, not hardcoded `120`. | BATCH-07 ✅ | ✅ Resolved |
| DEBT-018 | P2 | BATCH-07-REVIEW | `MoveToExecutor._stuckTicks` dictionary — verify `DispatcherSystemBase` always calls `OnExit` before entity destruction. If not, the dictionary leaks one `int` per recycled entity index. Verify contract and add a guard or comment. | BATCH-08 | 🔴 Open |
| DEBT-019 | P3 | BATCH-07-REVIEW | `DispatcherSystemBase` — add a comment near OnEnter/Execute dispatch documenting "same-frame double-write safety" invariant: `OnEnter` sets up state; the same-frame `Execute` must be safe without overwriting OnEnter's writes. All four Phase 3 executors satisfy this but it should be documented. | BATCH-08 or any batch touching DispatcherSystemBase | 🔴 Open |
| DEBT-020 | P2 | BATCH-07-REVIEW | `FollowRoadGraphExecutorTests.SetsRoadGraphMode_OnEnter` — verify the test asserts all three `OnEnter` writes: `Mode == RoadGraph`, `TargetNodeId == params.TargetNodeId`, AND `TargetSpeed == params.Speed`. Add missing assertions if any. | BATCH-08 | 🔴 Open |

---

## Resolved Items (archive)

| ID | Sev | Description | Resolved In |
|---|---|---|---|
| DEBT-001 | P2 | `SpatialHashSystem` no integration tests for non-vehicle entities | BATCH-06 |
| DEBT-002 | P2 | `IActionExecutor<T>.Execute` status-write contract undocumented | BATCH-04 |
| DEBT-003 | P3 | `OnExit` field-state invariant undocumented | BATCH-04 |
| DEBT-004 | P2 | `DoctrineIngress` Test 4 missing `channel.DoctrineInstanceId == 0` assertion | BATCH-05 |
| DEBT-005 | P2 | `InputSystemGroup` cross-group ordering by convention only — no doc comment | BATCH-05 |
| DEBT-009 | P1 | `SpatialHashGrid` stored raw `int` entity indices — generational safety bypassed | BATCH-06 |
| DEBT-010 | P1 | `EntityRepository.GetEntity(int)` was `public` — loaded gun for raw-index storage | BATCH-06 |
| DEBT-011 | P1 | `VisionBroadphaseSystem` O(N×M) brute force + data-race risk on async boundary | BATCH-06 |
| DEBT-012 | P2 | `VisionBroadphaseSystemTests` tests 2 & 3 hardcoded `0.866f` literal | BATCH-06 |
| DEBT-013 | P2 | `ThreatEvaluationSystemTests` missing boost-path and zero-score policy tests | BATCH-06 |
| DEBT-014 | P3 | `AudioPerceptionSystemTests` Test 2 used non-existent `SourceEntityIndex = 99` | BATCH-06 |
| DEBT-015 | P2 | `ThreatEvaluation_ZeroScoreEntry_IsRetained` — policy confirmed via DESIGN.md §4.3; doc added | BATCH-07 |
| DEBT-016 | P2 | `MoveToExecutor` frustration test used hardcoded `120` instead of constant | BATCH-07 |
| DEBT-017 | P3 | `FollowRouteParams` comment said "aligns to 4-byte boundary" (misleading; struct is 8B) | BATCH-07 |

---

## Notes

- **DEBT-018, DEBT-020** are the immediate priority for BATCH-08 (quick fixes, no new design needed).
- **DEBT-006** does not affect correctness until Phase 5. Do not refactor early — but do not build more code that assumes hash-key stability.
- **DEBT-007** is architecturally significant. The ECS-access strategy for HSM delegates determines all Phase 6 behaviour. Resolve before Phase 6 begins.
