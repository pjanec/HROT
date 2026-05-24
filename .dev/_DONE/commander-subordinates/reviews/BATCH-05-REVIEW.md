# BATCH-05 Review

**Batch:** BATCH-05
**Reviewer:** Dev Lead
**Status:** APPROVED

---

## Review Summary

All 4 tasks (CS020, CS021, CS024, CS025) are fully implemented, built, and tested.

---

## Correctness Review

### CS020 — EditorOrbatAdapter
- `RequestAssignSubordinate` correctly publishes `CmdAssignSubordinate` with Subordinate/Commander/Designation fields
- `RequestRemoveSubordinate` correctly publishes `CmdRemoveSubordinate` with Subordinate field
- Both methods look up renderer IDs via `_orbat.TryGetEntityByRendererId` and log warn + return on unknown ID
- `CanAcceptSubordinates` delegates to `IsCompositeType(entity.TkbType)` — correct field access
- **PASS**

### CS021 — ExConOrbatAdapter
- `ICommandGateway` extended with `SendUpdateAttributeAsync` — correct async signature with default CancellationToken
- `NedCommandGateway` writes DDS `UpdateEntityAttributeRequest` with correct target/payload fields
- All null stubs return `Task.CompletedTask` — correct no-op
- `ExConOrbatAdapter.RequestAssignSubordinate` builds `{"CommanderId": N}` patch, calls gateway — correct
- `ExConOrbatAdapter.RequestRemoveSubordinate` builds `{"CommanderId": 0}` patch, calls gateway — correct
- `CanAcceptSubordinates` uses `entity.TkbType` (on `IDerEntity`) not `GetDescriptor<EntityInfoDescriptor>().TkbType` — correct
- No `Fdp.Core` import added to `Hrot.ExCon` project — constraint respected
- **PASS**

### CS024 — UpdateEntityAttributeRequestSystem
- `InterceptCommanderId` runs before null-compiler guard — critical ordering fix
- Correctly distinguishes assign (CommanderId != 0) from remove (CommanderId == 0)
- Assign guard: commander must exist in `_entityMap` before publishing
- Remove guard: target must have `UnitSubordinate` before publishing
- `RebuildJsonWithout("CommanderId")` cleanly removes the key for downstream processing
- When `commanderIntercepted` and no compiler: sends `WriteAck` (not error) — correct semantics
- Empty-mask ack when only hierarchy command intercepted and no other attributes applied — correct
- **PASS**

### CS025 — Integration Tests
- CS025-T02: correctly tests capacity overflow; uses `UnitRoster.Capacity` constant; checks
  `CmdAssignSubordinateRejected` published exactly once for the 17th entity — correct
- CS025-T06: full serialization round-trip; uses `HrotScenarioSerializerFactory.Build(new BehaviorRegistry())`
  after component registration (correct order for `FdpAutoSerializer`); finds entities via `Query().With<NetworkIdentity>()`;
  asserts `InitialUnitSubordinateIntent` fields before genesis, then checks reconstituted `UnitSubordinate` after genesis;
  also verifies `UnitRoster.Count == 1` on commander — thorough
- **PASS**

---

## Test Quality

- All new tests follow Arrange/Act/Assert pattern
- CS024 test stubs (`QueuedRequestSource`, `RecordingAckSink`) are minimal and focused
- CS025-T06 correctly uses `((ISimulationView)_repo2).GetManagedComponentRO<T>()` for managed component access
- Component registrations match `GenesisMaterializationSystemTests` constructor — consistent

---

## Decision

**APPROVED** — CS020, CS021, CS024, CS025 complete. Ready for next batch.
