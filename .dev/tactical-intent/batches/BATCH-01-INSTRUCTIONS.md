# BATCH-01: Core Contracts + Receiver-Side Resolution

**Batch Number:** BATCH-01  
**Tasks:** TASK-TI001, TASK-TI002, TASK-TI003  
**Phase:** Phase 1 (Core Contracts) + Phase 2 (Receiver-Side Resolution)  
**Estimated Effort:** 5-7 hours  
**Priority:** HIGH  
**Dependencies:** None — this is the foundation for all subsequent batches

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)
1. **Design Document:** `.dev/tactical-intent/DESIGN.md` — read completely, every section
2. **Task Definitions:** `.dev/tactical-intent/TASK-DETAIL.md` — tasks TASK-TI001, TASK-TI002, TASK-TI003 in detail
3. **Existing event pattern:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Events/AssignBehaviorEvent.cs` — model for TI001
4. **Existing system pattern:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BehaviorIngressSystem.cs` — model for TI003
5. **CgfLogicPack wiring:** `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs` — where TI003 system gets registered

### Source Code Locations
- **New contracts (TI001, TI002):** `FDP/Toolkits/Fdp.Toolkits/Behavior/` — no Hrot dependencies allowed here
- **New resolution system (TI003):** `Hrot/Subsystems/Hrot.CGF/Systems/`
- **Test project (TI001, TI002):** `FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj`
- **Test project (TI003):** `Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj`
- **TestWorldFactory:** `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/TestWorldFactory.cs`

### Report Submission
Submit report to: `.dev/tactical-intent/reports/BATCH-01-REPORT.md`  
Questions (if any): `.dev/tactical-intent/questions/BATCH-01-QUESTIONS.md`

---

## Context

This batch lays the entire foundation of the Tactical Intent Distribution System. The goal is to create the event and mapper interface types that all other phases depend on, and then add the receiver-side system that translates generic intents to concrete `AssignBehaviorEvent`s.

The **key architectural insight** is:
- Senders (`MissionAdapterSystem`, Commander AI) emit `AssignTacticalIntentEvent` with a string `IntentId` — they never know the recipient's unit type
- `TacticalIntentResolutionSystem` on the receiver node translates the intent via a mapper registry, or falls through to treat the `IntentId` as a direct behavior name
- `BehaviorIngressSystem` (untouched) handles the final `AssignBehaviorEvent` exactly as before

**Related Tasks:**
- [TASK-TI001](./../TASK-DETAIL.md#task-ti001---add-assigntacticalintentevent) — new managed event
- [TASK-TI002](./../TASK-DETAIL.md#task-ti002---add-itacticalordermapper-interface-and-tacticalintentmapperregistry) — mapper interface + registry
- [TASK-TI003](./../TASK-DETAIL.md#task-ti003---implement-tacticalintentresolutionsystem) — resolution system

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: Complete tasks in sequence with passing tests before moving on.**

1. **TASK-TI001:** Implement → Write tests → **ALL tests pass** ✅
2. **TASK-TI002:** Implement → Write tests → **ALL tests pass** ✅
3. **TASK-TI003:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until the current task's tests all pass.  
**DO NOT** stop and ask for permission to proceed — keep going until the full batch is done and the report is written.

---

## ✅ Tasks

### Task 1: Add AssignTacticalIntentEvent (TASK-TI001)

**File:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Events/AssignTacticalIntentEvent.cs` (NEW FILE)  
**Task Definition:** See [TASK-DETAIL.md §TASK-TI001](../TASK-DETAIL.md#task-ti001---add-assigntacticalintentevent)

Model this exactly on `AssignBehaviorEvent.cs` in the same folder. Key constraints:
- `sealed class` (not struct) — carries managed string fields
- Namespace: `Fdp.Toolkit.Behavior.Events`
- Fields: `Entity Entity`, `string IntentId = string.Empty`, `string JsonParams = string.Empty`
- No `IsRemote` flag (see DESIGN.md §5.2 and Architectural Decisions for the rationale)
- No Hrot-specific dependencies

**Tests:** Add to `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/` — new test class `AssignTacticalIntentEventTests.cs`
- SC-1: publish event → swap buffers → `ReadManaged<AssignTacticalIntentEvent>()` returns it with correct `IntentId`
- SC-2: default instance has non-null empty strings for `IntentId` and `JsonParams`

---

### Task 2: Add ITacticalOrderMapper + TacticalIntentMapperRegistry (TASK-TI002)

**Files (NEW):**
- `FDP/Toolkits/Fdp.Toolkits/Behavior/TacticalOrderMapper/ITacticalOrderMapper.cs`
- `FDP/Toolkits/Fdp.Toolkits/Behavior/TacticalOrderMapper/TacticalIntentMapperRegistry.cs`

**Task Definition:** See [TASK-DETAIL.md §TASK-TI002](../TASK-DETAIL.md#task-ti002---add-itacticalordermapper-interface-and-tacticalintentmapperregistry)

**ITacticalOrderMapper interface:**
```csharp
public interface ITacticalOrderMapper
{
    string TargetIntentId { get; }

    bool TryMap(Entity entity, EntityRepository repo, string jsonParams,
                out AssignBehaviorEvent assignment);
}
```

**TacticalIntentMapperRegistry:** dictionary-backed registry
- `void Register(ITacticalOrderMapper mapper)` — throws `InvalidOperationException` on duplicate `TargetIntentId`
- `bool TryGetMapper(string intentId, out ITacticalOrderMapper mapper)` — standard try-get

No Hrot-specific dependencies. Namespace: `Fdp.Toolkit.Behavior.TacticalOrderMapper`

**Tests:** Add `TacticalIntentMapperRegistryTests.cs` in `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/`
- SC-1: register two distinct mappers → `TryGetMapper` returns correct one for each ID
- SC-2: register same `TargetIntentId` twice → `InvalidOperationException`
- SC-3: `TryGetMapper("Unknown")` on empty registry → returns `false`, out param is `null`

---

### Task 3: Implement TacticalIntentResolutionSystem (TASK-TI003)

**File:** `Hrot/Subsystems/Hrot.CGF/Systems/TacticalIntentResolutionSystem.cs` (NEW FILE)  
**Wiring:** `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs` (MODIFY)  
**Task Definition:** See [TASK-DETAIL.md §TASK-TI003](../TASK-DETAIL.md#task-ti003---implement-tacticalintentresolutionsystem)

**System contract:**
- `[UpdateInPhase(SystemPhase.Simulation)]`
- Constructor: `TacticalIntentResolutionSystem(TacticalIntentMapperRegistry mapperRegistry)`
- Per-frame logic (critical — see DESIGN.md §2.1):
  1. Read all `AssignTacticalIntentEvent` from `repo.Bus.ReadManaged<AssignTacticalIntentEvent>()`
  2. For each event: **first** check `repo.HasAuthority<BehaviorState>(evt.Entity)` — if `false`, skip silently
  3. If entity doesn't exist (deleted), skip silently
  4. Look up `evt.IntentId` in `_mapperRegistry`
  5. If mapper found and `TryMap` returns `true` → publish the returned `AssignBehaviorEvent`
  6. Otherwise (no mapper, or `TryMap` returned `false`) → publish `new AssignBehaviorEvent { Entity = evt.Entity, BehaviorName = evt.IntentId, JsonParams = evt.JsonParams }`
- Must NOT mutate `BehaviorState`, `BrainBTreeState`, or `BrainBlackboard` directly

**CgfLogicPack wiring changes required:**
1. Add `TacticalIntentMapperRegistry mapperRegistry` parameter to `CgfLogicPack` constructor
2. Create `_tacticalIntentResolutionSystem = new TacticalIntentResolutionSystem(mapperRegistry)`
3. In the `simList` construction, insert `_tacticalIntentResolutionSystem` **immediately after** `_missionAdapterSystem`
4. Update the call site in `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` line 243 to pass a new `TacticalIntentMapperRegistry()` (empty registry for now — mappers registered in Phase 6)

**Tests in `Hrot/Subsystems/Hrot.SimHost.Tests/`:**

Add `TacticalIntentResolutionSystemTests.cs`:
- **SC-1:** Registry has mapper for "DefendArea"; entity has `BehaviorState` (authority = true); publish intent + execute system → `AssignBehaviorEvent` published with mapper-translated behavior name
- **SC-2:** Empty registry; entity has `BehaviorState`; publish intent with `IntentId="ConvoyEscort"` → `AssignBehaviorEvent` published with `BehaviorName == "ConvoyEscort"` (pass-through)  
- **SC-3:** Publish event for entity that does not exist → no exception, no `AssignBehaviorEvent` published
- **SC-4:** Mapper registered but `TryMap` returns `false`; entity has `BehaviorState` → fallback publishes `new AssignBehaviorEvent` with `BehaviorName == evt.IntentId`
- **SC-5:** Entity does NOT have `BehaviorState` (simulating remote-owned cognitive state) → no `AssignBehaviorEvent` published, no exception

For authority simulation in tests: **entity without `BehaviorState` component = no authority** (since `HasAuthority<BehaviorState>` returns false when the component is absent or not owned). Use `TestWorldFactory.Create()` for world setup. Check how existing tests in `Hrot/Subsystems/Hrot.SimHost.Tests/` set up CGF worlds — look at `CgfLogicPackTests.cs`.

---

## 🧪 Testing Requirements

- Minimum: 2 tests for TI001, 3 tests for TI002, 5 tests for TI003
- All tests must verify **actual behavior**, not just compilation
- SC-5 for TI003 (authority gate) is non-negotiable — it directly implements the CQRS boundary
- Run `dotnet build IOS-IG-SimHost.sln --no-restore -v quiet` and `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build --nologo` and `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build --nologo` after implementing each task

---

## ⚠️ Quality Standards

- No new Hrot dependencies in `FDP/Toolkits/Fdp.Toolkits/` (circular dep violation)
- `AssignBehaviorEvent` and `BehaviorIngressSystem` are NOT modified
- `CgfSubsystem.cs` construction site updated so `CgfLogicPack` receives the new `TacticalIntentMapperRegistry` parameter
- The authority gate (`HasAuthority<BehaviorState>`) must be implemented as a skip (not an exception)
- The fallback path must allocate a **new** `AssignBehaviorEvent` instance — do not pool or reuse

---

## 📊 Report Requirements

Submit `.dev/tactical-intent/reports/BATCH-01-REPORT.md` with:

1. **Files changed** — list each file with NEW/MODIFIED
2. **Test results** — paste the final `Passed!` / `Failed!` summary lines
3. **Issues encountered** — what problems did you hit? How did you resolve them?
4. **Design decisions made** — any choices beyond the spec?
5. **Weak points spotted** — anything in the existing codebase worth noting?
6. **Suggested commit message**

---

## 🎯 Success Criteria

- [ ] `AssignTacticalIntentEvent` class exists with correct fields and namespace
- [ ] `ITacticalOrderMapper` interface exists with correct `TryMap` signature
- [ ] `TacticalIntentMapperRegistry` exists with `Register` / `TryGetMapper` and duplicate-check
- [ ] `TacticalIntentResolutionSystem` exists, wired into `CgfLogicPack.SimulationSystems` after `MissionAdapterSystem`
- [ ] `CgfSubsystem.cs` passes `new TacticalIntentMapperRegistry()` to `CgfLogicPack` constructor
- [ ] All 10+ tests passing
- [ ] `dotnet build IOS-IG-SimHost.sln --no-restore -v quiet` reports no errors

---

## 📚 Reference Materials
- **Task Definitions:** `.dev/tactical-intent/TASK-DETAIL.md` — TASK-TI001, TASK-TI002, TASK-TI003
- **Design:** `.dev/tactical-intent/DESIGN.md` — §1.1, §1.2, §1.3, §2.1, §2.2, Architectural Decisions
- **Pattern reference:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Events/AssignBehaviorEvent.cs`
- **Pattern reference:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BehaviorIngressSystem.cs`
- **Pattern reference:** `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs`
- **Wiring target:** `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs`
- **Construction site:** `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` line 243
