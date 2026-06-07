# Onboarding — EQS Sensor Lifecycle / BTree Hybrid Lifecycle Hook

Welcome to the `eqs-2` workstream. This guide gives a new developer enough context to start
contributing to the first batch.

---

## What is being built

FastBTree actions in this engine are pure stateless static delegates. This makes them
extremely fast but means there is no built-in callback when the execution pointer leaves a
node due to a branch switch or abort. Actions that write to actuator channels (weapon,
locomotion) or allocate sensor components currently have no guaranteed cleanup path for these
cases — leading to stale channel state and orphaned resource IDs.

This workstream adds a **BTree hybrid lifecycle hook** to FastBTree: a `[BTreeDeactivator]`
attribute that pairs a static cleanup method with any action. The BTree interpreter invokes
the deactivator automatically when the execution pointer leaves the action for any reason.
The mechanism is inspired by `UBTTaskNode.AbortTask` in Unreal Engine, adapted to remain
fully compatible with the engine's stateless-delegate and zero-allocation constraints.

---

## Planning artifacts

| Artifact | Location |
|---|---|
| Design (WHAT and WHY) | [DESIGN.md](./DESIGN.md) |
| Task specifications | [TASK-DETAIL.md](./TASK-DETAIL.md) |
| Progress checklist | [TASK-TRACKER.md](./TASK-TRACKER.md) |
| Technical debt | [DEBT-TRACKER.md](./DEBT-TRACKER.md) |

---

## Folder layout

The work touches three separate project areas:

| Area | Path | What changes |
|---|---|---|
| FastBTree library | `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/` | New types: `NodeDeactivatorDelegate`, `BTreeDeactivatorAttribute`. Extended types: `ActionRegistry`, `Interpreter`. |
| FastBTree library tests | `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/` | New test class `HybridLifecycleTests.cs`. |
| Roslyn source generator | `FDP/Toolkits/Fdp.Toolkits.Analyzers/BTreeActionGenerator.cs` | Detect and emit `RegisterDeactivator` calls. |
| Insurgent behavior (FDP example) | `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/InsurgentNodes.cs` | New `Deactivate_AimAndFire` method. |
| HillAttack tank nodes (Hrot) | `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackTankNodes.cs` | New `Deactivate_CreepToAndBeyondSlot` and `Deactivate_AimAndFireSpecific` methods. |
| HillAttack commander nodes (Hrot) | `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs` | New `Deactivate_RequestAreaQuery` method. |

Existing attributes live in `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Attributes/` — new
attribute file goes there.

---

## Key existing types to understand

**`NodeLogicDelegate<TBlackboard, TContext>`** (`Fbt.Kernel/NodeLogicDelegate.cs`)
The 4-param delegate that every BTree action and condition must conform to. The new
`NodeDeactivatorDelegate` has the same signature but returns `void`.

**`ActionRegistry<TBlackboard, TContext>`** (`Fbt.Kernel/Runtime/ActionRegistry.cs`)
Maps method name strings to `NodeLogicDelegate` instances. Phase 1 adds a parallel
deactivator dictionary.

**`Interpreter<TBlackboard, TContext>`** (`Fbt.Kernel/Runtime/Interpreter.cs`)
Executes a compiled `BehaviorTreeBlob` per tick. Phase 1 adds pre/post-tick delta tracking
and deactivator invocation. Read `ExecuteSelector` and `ExecuteAction` to understand the
current active-node tracking logic.

**`BehaviorTreeState`** (`Fbt.Kernel/BehaviorTreeState.cs`)
64-byte per-entity state struct. Key field: `RunningNodeIndex` (ushort) — index of the
currently running leaf node, 0 when idle.

**`BTreeActionGenerator`** (`FDP/Toolkits/Fdp.Toolkits.Analyzers/BTreeActionGenerator.cs`)
Roslyn incremental generator that scans for `[BTreeAction]`/`[BTreeCondition]` and emits
`FbtActionRegistrar.g.cs`. Phase 2 extends it to also scan for `[BTreeDeactivator]`.

**`BTreeNewFeaturesTests.cs`** (`Fbt.Tests/Unit/`)
Good reference for how to write isolated interpreter tests using `TestBlackboard`,
`MockContext`, and manually-constructed `BehaviorTreeBlob` objects.

---

## Build and run tests

```powershell
# Build and test the FastBTree library in isolation (Phase 1)
dotnet test FDP\ExtDeps\FastBTree\FastBTree.sln --no-restore

# Build and test the full FDP solution (Phase 2 + 3)
dotnet test FDP\FDP.sln --no-restore
```

From the solution root (`D:\Work\IOS-IG-SimHost-FDP-2`).

Note: `Fbt.Tests.csproj` references a `Fbt.SourceGen` project (an analyzer) that does not
exist on disk. The reference is declared with `ReferenceOutputAssembly="false"`, so the
test project builds without it. Do not attempt to create or restore this project.

---

## Workflow

Read `.dev-workstream/guides/DEV-GUIDE.md` to understand the batch-based development
workflow used in this project. In summary: work is assigned in batches; each batch has a
TASK-TRACKER entry; report back with a batch report when done.

Phase 1 is the natural first batch since it has no engine dependencies and is fully provable
in isolation via `Fbt.Tests`.
