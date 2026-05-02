# BATCH-04 Review

**Batch:** BATCH-04  
**Reviewer:** Dev Lead  
**Date:** 2026-05-02  
**Decision:** APPROVED

---

## Summary

BATCH-04 delivers the final phase of the Tactical Intent Distribution System: a reference Commander BTree action node and the first concrete mapper (`DefendAreaMapper`). All tasks complete, build clean, all new tests pass.

---

## Task Verification

| Task ID    | Status   | Code Quality | Tests | Notes |
|------------|----------|-------------|-------|-------|
| TASK-TI010 | APPROVED | Good | 2/2 | BTree action pattern correct; publishes event on ctx.World.Bus |
| TASK-TI011 | APPROVED | Good | 5/5 | Mapper logic correct; registered in CgfSubsystem |

---

## Code Review Notes

### CommanderNodes.cs
- `IssueTacticalIntentParams` correctly uses only value-type fields (long, int) for unmanaged blackboard storage.
- `[BTreeAction]` follows the CgfNodes.cs delegate signature pattern.
- `new Entity((ulong)p.SubordinatePacked)` — correct entity reconstruction.
- `ctx.World.Bus.PublishManaged(new AssignTacticalIntentEvent {...})` — correct managed event publication.
- TODO comments left for subordinate enumeration and IntentTypeOrdinal registry lookup — appropriate for reference impl.

### DefendAreaMapper.cs
- `ITacticalOrderMapper` interface correctly implemented.
- `TkbEntityTypes.MilitaryApc` / `InfantrySoldier` — correct constants via `Hrot.Map.Common` namespace.
- `HasComponent<TkbIdentity>` guard before reading component — correct defensive pattern.
- `assignment = null!` default with early returns — matches pattern; `AssignBehaviorEvent` is a class so `null!` is safe.
- `BehaviorName` field name used correctly (verified against event class).

### CgfSubsystem.cs
- `mapperRegistry.Register(new DefendAreaMapper())` added before passing to `CgfLogicPack` — correct composition root wiring.

### Project References
- `Hrot.AI.Behaviors.csproj` → `Hrot.Core`: required for `TkbEntityTypes` constants. No circular dependency.
- `Hrot.CGF.csproj` → `Hrot.AI.Behaviors`: appropriate since CGF is the Brain-tier bundle that owns mapper registration.

---

## Build & Test Results

| Suite | Result | Details |
|-------|--------|---------|
| Build | PASS | 0 errors |
| SimHost.Tests (targeted) | PASS | 7/7 new tests |
| SimHost.Tests (full) | PASS | 472/474 (2 pre-existing failures unchanged) |

---

## Decision

**APPROVED — no changes required.**

All 11 tasks of the Tactical Intent Distribution System are now implemented and tested. The full pipeline is complete:
1. `AssignTacticalIntentEvent` defined (TI001)
2. `ITacticalOrderMapper` + registry (TI002)
3. `TacticalIntentResolutionSystem` (TI003)
4. `MissionAdapterSystem` emits tactical intents (TI004)
5. `BehaviorCategory.Commander` flag (TI005)
6. `DefendAreaIntentDto` example (TI006)
7. `TacticalIntentRequest` DDS struct (TI007)
8. `TacticalIntentEgressTranslator` (TI008)
9. `TacticalIntentIngressTranslator` (TI009)
10. `CommanderNodes` BTree action (TI010)
11. `DefendAreaMapper` first concrete mapper (TI011)
