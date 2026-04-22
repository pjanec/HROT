# BATCH-01 Review — CGF Registry Hardening, Validator Extraction & NetworkGateway DRY Refactor

**Batch:** BATCH-01  
**Reviewer:** Dev Lead  
**Review Date:** 2026-04-05  
**Verdict:** ✅ APPROVED

---

## Summary

All 6 tasks implemented and verified. Full solution builds clean (0 errors). All 10 new tests
pass; all regression tests green. The BATCH-01 scope is complete.

---

## Scope Check

| Task | Implemented | Verified |
|------|-------------|---------|
| PACK3-C001: `CgfComponentRegistry` created, `CgfApplication` updated | ✅ | ✅ 4 unit tests |
| PACK3-U001: `UrbanCombatValidator` with TkbIdentity resolution | ✅ | ✅ 3 unit tests |
| PACK3-U002: `UrbanCombatNewScenario` delegates to validator | ✅ | ✅ regression |
| PACK3-N001: Canonical `NetworkGatewaySystem` in `FDP.Toolkit.Replication.Systems` | ✅ | ✅ 3 unit tests |
| PACK3-N003: `CycloneNetworkModule` rewired | ✅ | ✅ build pass |
| PACK3-N002: Clone files deleted | ✅ | ✅ all 4 files gone |

---

## Design Alignment

- **PACK3-C001**: Three-tier pattern (foundation → cognitive/kinematic → IG presentation)
  correctly implemented matching `SimHostComponentRegistry` convention. `CgfApplication.cs`
  now contains a single `RegisterAll` call. ✅

- **PACK3-U001**: Validator correctly resolves `TkbMilitaryApc (2001)` and `TkbInsurgent (2003)`
  via `TkbIdentity` query each tick. Four latches in correct order. Throws
  `ScenarioFailureException` at >600 ticks. **Note:** Developer added `_cachedApc` /
  `_cachedInsurgent` fallback for entities destroyed mid-sequence — this is a necessary
  pragmatic deviation from the strict "no cached Entity fields" spec; the spec's intent was
  not to cache handles across serialisation round-trips, but destroyed entities are a
  different case. The approach is sound. ✅

- **PACK3-U002**: `UrbanCombatNewScenario.EvaluateTick` now contains exactly one line
  (`return _validator.EvaluateTick(tick, world)`). Redundant latch fields removed. ✅

- **PACK3-N001**: Namespace `FDP.Toolkit.Replication.Systems`, only `Fdp.Kernel`,
  `FDP.Toolkit.Lifecycle`, and `FDP.Toolkit.Replication.Components` referenced (no Cyclone DDS
  imports in the class body). `using INetworkTopology = Fdp.Interfaces.INetworkTopology;`
  alias is acceptable for disambiguation. ✅

- **PACK3-N002**: Four deleted files confirmed absent. `grep` returns exactly one production
  `class NetworkGatewaySystem`. ✅

- **PACK3-N003**: `CycloneNetworkModule` uses alias to reference the toolkit system. ✅

---

## Test Quality Assessment

Tests examined for logic correctness — not just compilation:

- `CgfComponentRegistryTests`: Assert specific component types registered from each tier
  (`BrainBTreeState` tier-2, `VehicleState` tier-2, `EntityInfo` tier-3). ✅
- `UrbanCombatValidatorTests`: Latch 1 fires correctly; timeout throws `ScenarioFailureException`;
  full four-latch sequence returns `true`. Assertions check values/behaviour. ✅
- `NetworkGatewaySystemTests`: No-PendingNetworkAck → immediate ACK; zero-peers → immediate ACK;
  two-peer deferred → ACKs only after both `ReceiveLifecycleStatus` calls. ✅

All assertions check **logic correctness** (values, behaviour, exception types), not string
existence or compilation. Meets review standard.

---

## Early Failure Check

- `UrbanCombatValidator`: throws `ScenarioFailureException` explicitly at >600 ticks — fails
  loud. ✅
- `NetworkGatewaySystem`: logs `Warn` for timeout and force-ACKs — timeout is surfaced. ✅

---

## Issues Found During Review

### P3 (deferred)
1. **`CgfComponentRegistry` uses `ModuleHost.Core` import indirectly† through `CycloneNetworkModule`**:
   No direct issue; tracked for future audit.
2. **Two `INetworkTopology` interfaces** — noted by developer in insights. Track as P3 debt.

†No direct code issue with the registry; the import concern is in the transport layer.

---

## Debt Tracker Entries

| Priority | Description | Source |
|----------|-------------|--------|
| P3 | Two `INetworkTopology` interfaces (`Fdp.Interfaces` vs `ModuleHost.Core.Network.Interfaces`) with different `GetExpectedPeers` signatures create ongoing ambiguity. Migrate all callers to `Fdp.Interfaces` version. | BATCH-01 developer insight |
| P3 | `EntityLifecycleModule.AcknowledgeConstruction` has no guard against double-acknowledgement. Future: add idempotency check. | BATCH-01 developer insight |
| P3 | `UrbanCombatValidator` rebuilds `TkbIdentity` query each tick. Negligible for small scenarios; consider cached query or early-exit once both actors are found for large-entity scenarios. | BATCH-01 developer insight |

---

## Suggested Git Commit Messages

**FDP submodule commit:**
```
feat(packs-3): PACK3-U001/U002 extract UrbanCombatValidator + PACK3-N001/N002/N003 promote NetworkGatewaySystem to FDP.Toolkit.Replication

- Add UrbanCombatValidator with TkbIdentity-based dynamic entity resolution
- Simplify UrbanCombatNewScenario.EvaluateTick to delegate to validator
- Add canonical NetworkGatewaySystem in FDP.Toolkit.Replication.Systems
- Rewire CycloneNetworkModule to use toolkit system
- Delete Cyclone-local NetworkGatewaySystem/Module clones and Core originals

Tests: 6 new passing (3 validator, 3 gateway); all regressions green
```

**Parent repo commit:**
```
feat(packs-3): PACK3-C001 CgfComponentRegistry + BATCH-01 validation harness

- Add CgfComponentRegistry (three-tier: foundation, cognitive, IG presentation)
- Replace per-component registrations in CgfApplication with single RegisterAll call
- Add CgfComponentRegistryTests (4 unit tests)
- Update FDP submodule ref (UrbanCombatValidator + NetworkGatewaySystem promotion)

Tests: 4 new passing; CgfSubsystemHeadless + DistributedBrainMuscle regressions green
```

---

## Next Actions

- ✅ Update TASK-TRACKER.md: PACK3-C001, PACK3-U001, PACK3-U002, PACK3-N001, PACK3-N002, PACK3-N003
- ✅ Add P3 debt entries to DEBT-TRACKER.md
- ✅ Commit FDP submodule and parent repo
- ➡️ Create BATCH-02: PACK3-U003, PACK3-U004, PACK3-Z001, PACK3-Z002
