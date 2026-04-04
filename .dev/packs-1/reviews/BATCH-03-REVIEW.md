# BATCH-03 Review

**Batch:** BATCH-03
**Tasks:** PACK-P003, PACK-P001
**Verdict:** ✅ APPROVED (with P2 debt noted below)

---

## Verification Summary

| Project | Result |
|---------|--------|
| `FDP.Toolkit.Combat.Tests` | ✅ 0 failed / 52 passed |
| `FDP.Toolkit.Physics.Tests` | ✅ 0 failed / 25 passed |
| `Hrot.SimHost.Tests` | ✅ 0 failed / 421 passed (1 pre-existing excluded) |
| `dotnet build IOS-IG-SimHost.sln` | ✅ 0 errors |

## Task Verification

### PACK-P003 ✅
- `DetonationNotification` and `WeaponFireIntent` use `Entity` handles (verified via combat tests).
- `FDP.Toolkit.Physics` and `FDP.Toolkit.Combat` — `NetworkEntityMap` references are **comments only** (verified grep).
- Egress translators resolve Entity → net ID; skip unknown entities without throwing.

### PACK-P001 ✅
- `MissionControlExecutionSystem.cs` — DDS/JSON references are **XML doc comments only** (design-level documentation), not code.
- `MissionControlIntent` + `MissionControlAckEvent` defined in `Hrot.SimHost/Events/` (correct: `FDP.Toolkit.Behavior` cannot reference `Hrot.NED`).
- 4 new unit tests passing with real behavioral assertions.
- EventId 6002 used (6001 was taken by `TogglePerspectiveEvent`).

## Design Decision Accepted

**Events in Hrot.SimHost/Events/ instead of FDP.Toolkit.Behavior/Events/:** Correct. The
`MissionCommandUnion` payload references `Hrot.NED.Messages` types; putting these events in
the FDP toolkit would create an illegal dependency. The spec's target location was overridden
by hard dependency constraints — the right call.

## Issues / Debt Recorded

### P2 — MissionControlRequestSystem vestigial (DEBT-006)
The original `MissionControlRequestSystem` still exists in the codebase but is no longer wired.
It should be **deleted** in the next relevant batch to avoid confusion. This is a **P2** —
it should be cleaned up soon.

### P3 — `view as EntityRepository` cast is fragile (DEBT-007)
`MissionControlIngressTranslator` (and the pre-existing `EntityMissionIngressTranslator`) cast
`ISimulationView` to `EntityRepository` to call `repo.Bus.PublishManaged`. If the view is
wrapped in tests or replays, this silently becomes a no-op. `ISimulationView` should expose
a `Bus` accessor or `PublishManagedEvent<T>` method. P3 — deferred.

### P3 — EventId collision has no compile-time guard (DEBT-008)
Two events can claim the same `[EventId]` integer with no warning at design time — only fails
at runtime during static init. A test that enumerates all registered event types and asserts
uniqueness would catch this. P3 — deferred.

### P3 — IDescriptorTranslator.Dispose(long) contract undocumented (DEBT-009)
New event-bus translators implement `Dispose(long networkEntityId)` as a no-op (correct for
bus bridges). But the contract is undocumented: when is per-entity disposal needed vs. not?
P3 — deferred.

---

## Suggested Git Commit Messages

### Main repo
```
feat(packs-1): BATCH-03 — ACL: Entity handles in combat events + MissionControl split

PACK-P003: Replace long network IDs with Entity handles in DetonationNotification / WeaponFireIntent;
           move NetworkEntityMap to egress translators only; FDP.Toolkit.Physics/Combat are now
           fully NetworkEntityMap-free
PACK-P001: Split MissionControlRequestSystem into Ingress/Egress/Execution components;
           define MissionControlIntent + MissionControlAckEvent (EventId 6002);
           MissionControlExecutionSystem has zero DDS/JSON references

Tests: 421 + 52 + 25 passing; 0 new failures.
```

### FDP submodule
```
feat(packs-1): BATCH-03 — Entity handles replace long network IDs in combat events

- DetonationNotification: ShooterNetworkEntityId → Shooter:Entity, HitEntityNetworkId → Target:Entity
- WeaponFireIntent / WeaponFireNotification: long IDs → Entity handles
- HitResolutionSystem: remove NetworkEntityMap constructor overload; always emit with Entity handles
- AimAndFireExecutor: remove NetworkEntityMap from constructor
- FireProcessingSystem: remove NetworkEntityMap from constructor
- DamageCalculationSystem: use event.Target (Entity) directly
- Update all caller sites and tests
```
