# BATCH-03 Review

**Batch:** BATCH-03
**Reviewer:** Dev Lead
**Verdict:** APPROVED

---

## Checklist

| Area | Status | Notes |
|------|--------|-------|
| Build clean | PASS | 0 errors, 0 warnings |
| 21/21 tests pass | PASS | Confirmed locally |
| D01 fixed | PASS | `SetNodeOffset` offset-displacement test added |
| D02 fixed | PASS | Guard added; throws `ArgumentOutOfRangeException` for unknown nodeId |
| D03 closed | PASS | Not-feasible rationale documented |
| Q1-Q5 answers | PASS | All five questions answered in report |
| `TransientMasterBuilder` — step ordering | PASS | `RepositoryPriming` called first before any `GetId` calls |
| Consensus mask COPY before `BitwiseAnd` | PASS | `var candidate = presenceMask;` copies struct; `presenceMask` not mutated |
| `alreadyClaimed.BitwiseOr(extract)` | PASS | Correctly accumulates claimed bits across ordered node list |
| Primary owner ordering | PASS | `ordered.Sort(...)` puts `nodeId == primaryOwner` first; THEN `CompareTo` for stable sort |
| Save-map populated before `SerializeEntity` | PASS | `resolver.SetSaveMap(saveMap)` called inside loop before serialization |
| Local-entity save-map seeding | PASS | `localEntityKeys` entries appended to provider node's save-map before global entity serialization AND before local entity extraction loop |
| P3T7 full presence mask | PASS | `var fullMask = providerRepo.GetComponentMask(...)` — no `BitwiseAnd(authorityMask)` applied |
| Cross-reference global→local resolves | PASS | `RBF_P3T7_LocalEntities_GlobalHandleToLocalResolves` verifies exact entity + component value |
| `MakeSyntheticKey` determinism | PASS | Same inputs → same MD5 → same GUID; Guid.TryParse succeeds; different inputs differ |
| `AutoSerializer.Build()` not called | PASS | Redundant call removed; BATCH-03 fix note in report |
| Test constructor pre-registers before serializer build | PASS | `ScenarioSerializerBuilder.Build()` sees only the 4 needed component types |

---

## Critical Path Analysis

### TransientMasterBuilder.Build — Algorithm Correctness

**§7.3 Consensus mask:**
```csharp
var candidate = presenceMask;        // struct copy (correct)
candidate.BitwiseAnd(authorityMask); // mutates candidate
var extract = candidate;             // another copy
extract.BitwiseAndNot(alreadyClaimed);
alreadyClaimed.BitwiseOr(extract);  // accumulate
```
Correct: no mutation of original `presenceMask`.

**Primary-owner detection:** Reads `NetworkAuthority.PrimaryOwnerId` from any node that carries `NetworkAuthority`. Consistent with design intent: all nodes agreeing on who the primary is.

**Ordering sort:** Stable — for equal priority, falls back to `nodeId.CompareTo(b.nodeId)`. ✅

### Test Quality

All 12 new tests verify actual values, not just absence of exceptions:

- `RelationalHandleRemapped` — exact entity equality `masterB == gt.TargetId`
- `GlobalHandleToLocalResolves` — traces full chain to `DummyPosition.X == 8f`
- `SplitBrainConflict_PrimaryOwnerWins` — two conflicting authority nodes; only one value survives
- `UseFullPresenceMask_NotAuthorityMask` — authority=false, yet component appears (tests the behavioral difference from consensus path)
- `SwitchProviderRebuilds` — both provider=1 and provider=2 states verified in the same test

No fake assertions, no empty loops, no pass-by-default tests.

---

## New Debt Items

None.

---

## Action Items

- [x] Update TASK-TRACKER.md: tick P3T5, P3T7
- [x] Commit BATCH-03 changes
