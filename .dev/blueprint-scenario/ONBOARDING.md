# Onboarding — Blueprint ↔ Entity Assignment & Scenario Persistence

## What we're building

A clean way to assign **Instance Blueprints** to entities and persist that to scenarios:
- **Static** — authored into the scenario file (multiple blueprints per entity).
- **Dynamic** — assign / remove / replace mid-simulation, via FDP events or a blueprint action node.

The core principle: scenarios store the **declarative assignment intent**, never the volatile blackboard bytes
(those belong to checkpoints / Flight-Recorder). AiPrimitive (behavior) assignment is a separate, existing path we
do **not** change.

## Read these first (in order)

1. **[BLUEPRINT-SCENARIO-DESIGN.md](./BLUEPRINT-SCENARIO-DESIGN.md)** — the design of record (architecture, rationale,
   verified code anchors). Authoritative; where a task and the design differ, the design wins.
2. **[TASK-DETAIL.md](./TASK-DETAIL.md)** — per-task descriptions + success conditions (the unit/integration tests).
3. **[TASK-TRACKER.md](./TASK-TRACKER.md)** — phases, task list, status, dependency order.
4. **[DEBT-TRACKER.md](./DEBT-TRACKER.md)** — technical debt + deferred-by-design items.
5. **`.dev/.guides/DEV-GUIDE.md`** — **how you must work** (build/test gates, no test-weakening, no snapshot
   regeneration, reporting). Read and follow it.
6. Background design conversation (context only): [design-talk.md](./design-talk.md), [ARCHITECT-BRIEF-01.md](./ARCHITECT-BRIEF-01.md).

## Where the components live

| Concern | Location |
|---|---|
| Blackboard components + partition allocator + tick/maintenance systems (core) | `FDP/Toolkits/Fdp.Toolkits/Blueprints/` (`Components/`, `Partitioning/BlueprintBlackboardPartitions.cs`, `Systems/`) |
| Unified attach/detach seam (new, core) | `FDP/Toolkits/Fdp.Toolkits/Blueprints/` |
| Editor-side attach service (→ becomes a forwarder) | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Runtime/BlueprintAttachService.cs` |
| Scenario serializer + translator interface | `FDP/Toolkits/Fdp.Toolkits/Scenario/` (`IEntityScenarioTranslator.cs`, `ScenarioSerializer*.cs`) |
| Existing translators to mirror (black-hole pattern) | `Hrot/Subsystems/Hrot.SimHost/Serializers/BrainBlackboardTranslator.cs`, `Blackboard1024Translator.cs` |
| Genesis intent components + registry | `Hrot/Engine/Hrot.Common/Serializers/GenesisIntentComponents.cs`; `GenesisIntentRegistry.RegisterAll` |
| Genesis materialization host (CGF) | `Hrot/Subsystems/Hrot.CGF/` (registers `GenesisMaterializationSystem`, `CgfScenarioLoadHandler`) |
| Blueprint compiler emit (action-node ABI) | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/` (`InlineActionLowering.cs`, `StatementEmitter.cs`) |
| Behavior event precedent (`BehaviorIngressSystem`, `AssignBehaviorEvent`) | `FDP/Toolkits/Fdp.Toolkits/Behavior/` |

## Key architecture facts (verified)

- **Layering:** the attach/detach seam goes in **core (`Fdp.Toolkits.Blueprints`)**, keyed by `int BlueprintId`.
  CGF/genesis must never depend on `Hrot.Blueprints.Editor`.
- **Host:** CGF owns scenario genesis (registers the materialization system; `CgfScenarioLoadHandler`).
- **One blueprint per node, entity-agnostic** breakpoint/assignment model; multiple Instance blueprints per entity
  via the partition slot table (tiers 1024/4096/16384 = 4/8/16 slots, 928/3936/16096 payload bytes).
- **Action-node → event:** use a `[SharedAiAction]` (receives `Entity self`, `EntityRepository world`) → `world.Bus`;
  a plain Library `FunctionCall` does **not** receive engine context.

## Build & test

- Build: `dotnet build IOS-IG-SimHost.sln -c Debug` → 0 errors (close the editor first — it locks DLLs).
- Test the touched projects, e.g. `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -c Debug`,
  `Hrot/Subsystems/Hrot.SimHost.Tests`, `FDP/Toolkits/Fdp.Toolkits.Tests` — **0 net-new failures**; report the full
  failing set by name.
- Use the **codebase-memory MCP** (`search_graph`, `get_code_snippet`, `trace_path`) to navigate; do not rely on
  `search_code`.
