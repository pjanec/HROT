# Hill Attack Group Behavior — Onboarding Guide

Welcome to the Hill Attack workstream. This document provides a concise orientation for a
developer picking up this feature for the first time.

---

## What Is Being Built

A platoon-level hill attack tactical doctrine for tank units. The feature consists of:

1. A **generic EQS (Environment Query System)** — a new batch-singleton pipeline that
   allows Brain-tier AI to asynchronously query which enemy entities lie inside a polygon
   area. It follows the existing `PathfindingBatchData` / `RaycastBatchData` patterns.

2. A **PlatoonHillAttack commander behavior** — a `FastBTree` behavior for the platoon
   commander that deploys tanks to a baseline staging area, then drives alternating attack
   waves until all enemies in the target area are eliminated.

3. A **HullDownAttackRun subordinate behavior** — a `FastBTree` behavior for individual
   tank entities that drives a single attack run: approach to a firing slot, creep forward
   until the assigned target is in sight, engage, then reverse to the assigned baseline
   slot.

Neither behavior violates the FDP CQRS boundary. All actuator writes go through
`LocomotionChannel` and `WeaponChannel`. All working memory fits in `BrainBlackboard`
(60-byte param region) and `Blackboard1024` (heavy mutable state via `Unsafe.As`).

---

## Planning Artifacts

| Document | Location | Purpose |
|---|---|---|
| Design talk (source) | `.dev/hill-attack/design-talk.md` | Raw conversation that produced this design |
| Design | `.dev/hill-attack/DESIGN.md` | Architecture, phases, decisions |
| Task Detail | `.dev/hill-attack/TASK-DETAIL.md` | Per-task specs with success conditions |
| Task Tracker | `.dev/hill-attack/TASK-TRACKER.md` | Checklist of all tasks |
| Debt Tracker | `.dev/hill-attack/DEBT-TRACKER.md` | Technical debt items found during implementation |

---

## Folder Layout

### New code lives in:

```
Hrot/Subsystems/Hrot.AI.Behaviors/
    Brains/
        HillAttackDtos.cs            -- DTOs: PlatoonHillAttackParams, HillAttackMutableState,
                                        HullDownAttackParams, blackboard wrappers
        HillAttackTankNodes.cs       -- Subordinate tank BTree nodes + BTree definition
        HillAttackCommanderNodes.cs  -- Commander BTree nodes + BTree definition
    Mappers/
        HullDownAttackMapper.cs      -- ITacticalOrderMapper for "HullDownAttack" intent
    AiBehaviorFactory.cs             -- Registration of both new behaviors (modified)

FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/
    AreaQueryBatchData.cs            -- AreaQueryRequest, AreaQueryResult, AreaQueryBatchData,
                                        EqsTargetPool
    AreaQueryBatchHelper.cs          -- Static helper (like PathfindingBatchHelper)

FDP/Engine/Fdp.Core/
    GlobalComponentIds.cs            -- Add AreaQueryBatchData = 202, EqsTargetPool = 203

Hrot/Subsystems/Hrot.SimHost/
    Modules/EqsModule.cs             -- SoD background module (ExecutionPolicy.SlowBackground(10))
    Systems/AreaQuerySolverSystem.cs -- EQS solver (reads SpatialGridData, polygon test)

Hrot/Subsystems/Hrot.CGF/
    Systems/AreaQueryResolutionSystem.cs  -- Stub system (supports future expansion)

Hrot/Network/Hrot.Network.NED/SimHost/
    AreaQueryBrainEgressTranslator.cs
    AreaQueryMuscleIngressTranslator.cs
    AreaQueryMuscleEgressTranslator.cs
    AreaQueryBrainIngressTranslator.cs
```

### Existing code consulted as reference / modified:

```
FDP/Toolkits/Fdp.Toolkits/Navigation/PathfindingBatchData.cs   -- EQS structural pattern
FDP/Toolkits/Fdp.Toolkits/Navigation/BTreeNodes/PathfindingActionNode.cs  -- Helper pattern
Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs           -- Existing behavior node patterns
Hrot/Subsystems/Hrot.AI.Behaviors/Mappers/DefendAreaMapper.cs  -- Mapper pattern to follow
Hrot/Subsystems/Hrot.SimHost/Modules/EyesAndMuscleModule.cs    -- SoD module pattern
Hrot/Network/Hrot.Network.NED/SimHost/TacticalIntentEgressTranslator.cs  -- Translator pattern
Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs                       -- Register mappers here
```

---

## Build and Run

Build the full solution from the workspace root:

```bat
dotnet build IOS-IG-SimHost.sln
```

Run all tests (no build step):

```bat
dotnet test IOS-IG-SimHost.sln --no-build
```

Run only hill-attack-relevant tests (once they exist):

```bat
dotnet test Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj --no-build
```

The `Fbt.SourceGen` Roslyn generator runs automatically during build and emits:
- `FbtTreeCatalog.g.cs` — one `Get<BehaviorName>()` method per `[BTreeDefinition]` method.
- `FbtActionRegistrar.g.cs` — registers all `[SharedAiAction]` / `[SharedAiHeavyAction]`
  / `[SharedAiCondition]` / `[SharedAiHeavyCondition]` delegates.

If the source generator produces errors, the behavior nodes will not compile. Check that:
1. Every `[SharedAiHeavyAction]` 5-argument form has matching struct types and field names.
2. Every `[BTreeDefinition]` method signature is `static Interpreter<BrainBlackboard, BTreeContext> Get...()`.

---

## Development Workflow

Read `.dev/.guides/DEV-GUIDE.md` to understand the batch-based development workflow used
in this project. Work is organized into batches; each batch produces a batch report.

Key rules:
- All new unmanaged structs must have `[StructLayout(LayoutKind.Sequential)]`.
- Byte-size constraints on parameter structs are hard: `sizeof(Params) <= 60`.
- Never add a new ECS component without a corresponding entry in `GlobalComponentIds`.
- The 256 component-type limit is hard. IDs 200-255 are reserved; IDs 202 and 203 are
  allocated by this workstream.
- When in doubt about an existing API, read `docs/AI_DEV_GUIDE.md`.
