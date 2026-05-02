# BATCH-04: Phase 3 — Domain Events (Embark, SeedTarget, ZoneObstacle, ZoneConfig + Registration)

**Batch Number:** BATCH-04  
**Tasks:** EDIT1-E001, EDIT1-E002, EDIT1-E003, EDIT1-E004  
**Phase:** Phase 3 — New Domain Events  
**Estimated Effort:** 5–7 hours  
**Priority:** HIGH — domain events are required before Phase 4 adapters and systems can be implemented  
**Dependencies:** BATCH-01 (project foundation) ✅

---

## 📋 Onboarding & Workflow

### Developer Instructions

You are defining five new domain event types and registering them in the appropriate ECS kernel  
initialisation registries.  This batch is **infrastructure** — no rendering, no UI, just new  
event structs/classes and a few registration lines in existing registry files.

After this batch, the Phase 4 adapters and systems will be able to publish these events.

Work task-by-task. Fix compile errors immediately. Do not stop and ask — work autonomously.

### Required Reading (IN ORDER)

1. **Workflow guide:** `.github/skills/developer/SKILL.md`
2. **Design:** `.dev/edit-1/DESIGN.md` §Phase 3 (§3.A, §3.B, §3.C, §3.D)
3. **Task specs:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-E001, §EDIT1-E002, §EDIT1-E003, §EDIT1-E004

### Source Code Locations

**FDP submodule (commit separately in FDP/)**:
| New File | Action |
|----------|--------|
| `FDP/Toolkits/FDP.Toolkit.Behavior/Events/EmbarkEntityCommand.cs` | Create |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Events/DisembarkEntityCommand.cs` | Create |
| `FDP/Toolkits/FDP.Toolkit.Perception/Events/SeedTargetCommand.cs` | Create (or add to `PerceptionEvents.cs`) |

**Hrot.Map.Common (top-level repo)**:
| New File | Action |
|----------|--------|
| `Hrot.Map.Common/Events/SpawnZoneObstacleCommand.cs` | Create |
| `Hrot.Map.Common/Events/UpdateZoneConfigCommand.cs` | Create |

**Existing registration files to modify**:
| File | Change |
|------|--------|
| `Hrot.SimHost/CognitiveComponentRegistry.cs` | Add `RegisterEvent<EmbarkEntityCommand>` + `RegisterEvent<DisembarkEntityCommand>` |
| `Hrot.SimHost/CombatComponentRegistry.cs` | Add `RegisterEvent<SeedTargetCommand>` |
| `Hrot.Map.Common/HrotSharedComponentRegistry.cs` | See note below about managed events |

### ⚠️ Critical Codebase Fact: Managed Events Need NO Registration

**The TASK-DETAIL mentions `world.RegisterManagedEvent<T>()`. This method does NOT exist** on  
`EntityRepository`. Managed events (classes, not structs) in the FDP kernel work via  
`Bus.PublishManaged<T>(evt)` / `Bus.ConsumeManaged<T>()` **without any pre-registration**.  
See `AssignBehaviorEvent` (a sealed class with no `[EventId]`, no registration) as the reference pattern.

**Therefore:**
- `SpawnZoneObstacleCommand` and `UpdateZoneConfigCommand` are **managed events** (classes with `string` fields)
- They do NOT get `[EventId]` attribute (only unmanaged structs use `[EventId]`)
- They do NOT need to be registered in `HrotSharedComponentRegistry`
- **EDIT1-E004**: Only register the **three unmanaged** events:
  - `EmbarkEntityCommand` and `DisembarkEntityCommand` → `Hrot.SimHost/CognitiveComponentRegistry.cs`
  - `SeedTargetCommand` → `Hrot.SimHost/CombatComponentRegistry.cs`

### Run tests with

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2

# Build
dotnet build IOS-IG-SimHost.sln 2>&1 | Select-String "error CS" | Select-Object -Last 5

# Tests
dotnet test Hrot.Map.Common.Tests --no-build
dotnet test Hrot.ClusterRunner.Tests --no-build
dotnet test Hrot.SimHost.Tests --no-build
```

### FDP Submodule Notes

`FDP/` is a git submodule on the `main` branch. After changing files inside `FDP/`,  
commit them in the submodule first, then the top-level repo picks up the pointer update.  
Do NOT commit from the dev lead instructions — just ensure the code is ready.

---

## Context

Phase 3 events are the "vocabulary" the Phase 4 adapters and systems will use.  
Creating them first keeps Phase 4 fast: the system/adapter developer can import  
these event types directly from their toolkit packages.

---

## 🎯 Batch Objectives

1. **EDIT1-E001** — `EmbarkEntityCommand` + `DisembarkEntityCommand` (FDP.Toolkit.Behavior)
2. **EDIT1-E002** — `SeedTargetCommand` (FDP.Toolkit.Perception)
3. **EDIT1-E003** — `SpawnZoneObstacleCommand` + `UpdateZoneConfigCommand` (Hrot.Map.Common, managed events)
4. **EDIT1-E004** — Register the three new **unmanaged** events in `SimHost` registries

---

## ✅ Tasks

### Task 1: EDIT1-E001 — `EmbarkEntityCommand` + `DisembarkEntityCommand`

**Full task spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-E001  
**Design reference:** `.dev/edit-1/DESIGN.md` §3.A

**EventId values** (Behavior block, after existing 3100–3102):
- `EmbarkEntityCommand` → `EventId = 3201`
- `DisembarkEntityCommand` → `EventId = 3202`

Add these constants to `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorConstants.cs`:
```csharp
public const int EventId_EmbarkEntity    = 3201;
public const int EventId_DisembarkEntity = 3202;
```

Create `FDP/Toolkits/FDP.Toolkit.Behavior/Events/EmbarkEntityCommand.cs`:
```csharp
using Fdp.Kernel;
namespace FDP.Toolkit.Behavior.Events;

[EventId(BehaviorConstants.EventId_EmbarkEntity)]
public struct EmbarkEntityCommand
{
    public Entity Passenger;
    public Entity Vehicle;
}
```

Create `FDP/Toolkits/FDP.Toolkit.Behavior/Events/DisembarkEntityCommand.cs`:
```csharp
using Fdp.Kernel;
namespace FDP.Toolkit.Behavior.Events;

[EventId(BehaviorConstants.EventId_DisembarkEntity)]
public struct DisembarkEntityCommand
{
    public Entity Passenger;
}
```

**Tests:** Write in `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/`:
1. Round-trip: `bus.Publish(new EmbarkEntityCommand { ... })` → `bus.Consume<EmbarkEntityCommand>()` returns the same value
2. `DisembarkEntityCommand` round-trip (similar)

---

### Task 2: EDIT1-E002 — `SeedTargetCommand`

**Full task spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-E002  
**Design reference:** `.dev/edit-1/DESIGN.md` §3.B

**EventId:** `4101` (Perception block, after existing 4004)

Add constant to `FDP/Toolkits/FDP.Toolkit.Perception/PerceptionConstants.cs`:
```csharp
public const int SeedTargetCommandId = 4101;
```

You can either:
- Create a new file `FDP/Toolkits/FDP.Toolkit.Perception/Events/SeedTargetCommand.cs`, OR
- Add the struct to the existing `FDP/Toolkits/FDP.Toolkit.Perception/Events/PerceptionEvents.cs`

Either way:
```csharp
[EventId(PerceptionConstants.SeedTargetCommandId)]
[StructLayout(LayoutKind.Sequential)]
public struct SeedTargetCommand
{
    public Entity Perceiver;
    public Entity Target;
    public float  ScoreBoost;
}
```

**Tests:** Round-trip publish/consume on a bare `FdpEventBus` (similar to EmbarkEntityCommand tests).

---

### Task 3: EDIT1-E003 — `SpawnZoneObstacleCommand` + `UpdateZoneConfigCommand`

**Full task spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-E003  
**Design reference:** `.dev/edit-1/DESIGN.md` §3.C

These are **managed events** (sealed classes, contain `string` fields).  
**No `[EventId]` attribute. No registration needed.**

Create `Hrot.Map.Common/Events/SpawnZoneObstacleCommand.cs`:
```csharp
using System.Numerics;
namespace Hrot.Map.Common.Events;

public sealed class SpawnZoneObstacleCommand
{
    public string  ZoneName { get; init; } = string.Empty;
    public Vector2 Position { get; init; }
    public float   Radius   { get; init; }
}
```

Create `Hrot.Map.Common/Events/UpdateZoneConfigCommand.cs`:
```csharp
namespace Hrot.Map.Common.Events;

public sealed class UpdateZoneConfigCommand
{
    public string  ZoneName        { get; init; } = string.Empty;
    public string? RoadNetworkPath { get; init; }
}
```

**Tests:** Write in `Hrot.Map.Common.Tests/`:
1. `new FdpEventBus(); bus.PublishManaged(new SpawnZoneObstacleCommand { ZoneName = "z1", Radius = 5f })` → `bus.ConsumeManaged<SpawnZoneObstacleCommand>()` returns the same command (check ZoneName + Radius)
2. `UpdateZoneConfigCommand` round-trip (check RoadNetworkPath)

---

### Task 4: EDIT1-E004 — Register Unmanaged Events in SimHost Registries

**Full task spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-E004  
**Design reference:** `.dev/edit-1/DESIGN.md` §3.D

**Important:** Only register the THREE unmanaged events; skip the two managed ones (no API exists).

In `Hrot.SimHost/CognitiveComponentRegistry.cs`, at the end of `RegisterAll(world)`:
```csharp
// Embarkation commands (edit-1/EDIT1-E001)
world.RegisterEvent<EmbarkEntityCommand>();
world.RegisterEvent<DisembarkEntityCommand>();
```
Add `using FDP.Toolkit.Behavior.Events;` to the using section.

In `Hrot.SimHost/CombatComponentRegistry.cs`, at the end of `RegisterAll(world)`:
```csharp
// Target seeding command (edit-1/EDIT1-E002)
world.RegisterEvent<SeedTargetCommand>();
```
Add `using FDP.Toolkit.Perception.Events;` to the using section.

**No changes to `HrotSharedComponentRegistry`** for this batch.

**Tests:** Write in `Hrot.SimHost.Tests/`:
1. Create a new `EntityRepository`; call `HrotSharedComponentRegistry.RegisterAll()`, then `CognitiveComponentRegistry.RegisterAll()`;  
   publish `EmbarkEntityCommand` on `world.Bus` → assert no exception (event is registered)
2. Same for `DisembarkEntityCommand`
3. Create repo; call `HrotSharedComponentRegistry.RegisterAll()` then `CombatComponentRegistry.RegisterAll()`;  
   publish `SeedTargetCommand` → assert no exception

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1 (E001):** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2 (E002):** Implement → Write tests → **ALL tests pass** ✅
3. **Task 3 (E003):** Implement → Write tests → **ALL tests pass** ✅
4. **Task 4 (E004):** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until all tests pass. Fix any compile errors before proceeding.  
Do NOT ask for permission to run tests or continue. Work autonomously until done.

---

## 🧪 Testing Requirements

- **Minimum 7 meaningful tests** across all 4 tasks
- Round-trip tests (publish → consume) are the primary quality check for event definitions
- Registration tests (no exception on publish after `RegisterAll()`) are required for EDIT1-E004
- Tests that only check `new Command() != null` are NOT acceptable

---

## ⚠️ Quality Standards

- All new event structs must be blittable/unmanaged (no managed fields for structs)
- `EmbarkEntityCommand` and `DisembarkEntityCommand` must have `sizeof(T)` evaluable at compile time
- XML `<summary>` comments on all new public types
- No compiler warnings introduced in any project

---

## 📊 Developer Insights (Required in Report)

**Q1:** What issues did you encounter creating the events or registering them?

**Q2:** Did you find any inconsistency between the TASK-DETAIL spec and the actual FDP kernel API  
(e.g. `RegisterManagedEvent` not existing, `ConsumeManagedSequence` vs `ConsumeManaged`)?  
Document exactly what exists vs what the spec claimed.

**Q3:** What design decisions did you make? (e.g. whether to add `SeedTargetCommand` to existing  
`PerceptionEvents.cs` or a new file, struct layout)

**Q4:** Are the `CGF` or `Editor` registries also missing these events? Will publishing these events  
in a CGF or Editor simulation context throw at runtime?  
(Hint: look at `CgfComponentRegistry.cs` to see what it currently registers.)

**Q5:** What is the highest-risk item for BATCH-05 (Editor adapters)?

---

## 🎯 Success Criteria

- [ ] `EmbarkEntityCommand` and `DisembarkEntityCommand` structs created with EventId 3201/3202
- [ ] `SeedTargetCommand` struct created with EventId 4101
- [ ] `SpawnZoneObstacleCommand` and `UpdateZoneConfigCommand` sealed classes created (no EventId, no registration)
- [ ] `CognitiveComponentRegistry` registers `EmbarkEntityCommand` and `DisembarkEntityCommand`
- [ ] `CombatComponentRegistry` registers `SeedTargetCommand`
- [ ] Minimum 7 unit tests written and passing
- [ ] No regressions in existing test suites
- [ ] Report submitted to `.dev/edit-1/reports/BATCH-04-REPORT.md`

---

## 📚 Reference Materials

- **Task specs:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-E001 through §EDIT1-E004
- **Design:** `.dev/edit-1/DESIGN.md` §Phase 3
- **Existing EventId constants:** `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorConstants.cs`
- **Existing Perception constants:** `FDP/Toolkits/FDP.Toolkit.Perception/PerceptionConstants.cs`
- **Existing unmanaged event pattern:** `FDP/Toolkits/FDP.Toolkit.Behavior/Events/AssignBehaviorHashEvent.cs`
- **Existing managed event pattern:** `FDP/Toolkits/FDP.Toolkit.Behavior/Events/AssignBehaviorEvent.cs` (no [EventId], no registration)
- **Registration pattern:** `Hrot.SimHost/CognitiveComponentRegistry.cs`, `Hrot.SimHost/CombatComponentRegistry.cs`
- **Test project:** `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/`, `Hrot.Map.Common.Tests/`, `Hrot.SimHost.Tests/`
