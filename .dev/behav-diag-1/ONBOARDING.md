# Onboarding — Behavior Diagnostics (BTree / HSM Per-Entity Trace Buffers)

Welcome. This document is your starting point for the **Behavior Diagnostics** workstream.

## What we're building

We're adding per-entity, zero-allocation execution-trace ring buffers for the two cognitive engines used by AI in this repo: **FastBTree** (behavior trees) and **FastHSM** (hierarchical state machines). The trace buffers live as 1024-byte unmanaged ECS components, can be toggled per entity from the Editor UI, survive flight-recorder save/replay, and can optionally stream their records to the existing `BehaviorLog` NLog target.

The work also refactors `FastHSM`'s kernel to remove a process-static `HsmTraceBuffer` (which prevented concurrent per-entity tracing), and introduces a small generic `DebugState` transient component as a long-term home for future per-entity debug flags.

**Read these first, in order:**

1. **[DESIGN.md](./DESIGN.md)** — full architectural design (one document, ~500 lines).
2. **[TASK-DETAIL.md](./TASK-DETAIL.md)** — every task has an ID, scope, success conditions.
3. **[TASK-TRACKER.md](./TASK-TRACKER.md)** — checkbox status per task; your daily progress map.
4. **[DEBT-TRACKER.md](./DEBT-TRACKER.md)** — running list of deferred technical debt (start empty).
5. **[design-talk.md](./design-talk.md)** — the long-form Q&A that produced this design. Useful when context is missing.
6. **[../.guides/DEV-GUIDE.md](../.guides/DEV-GUIDE.md)** — the universal developer workflow guide for batch-based development in this repo. **You must read this before starting.**

---

## Repository overview

This is a multi-project C# / .NET 8 simulation engine ("FDP") with a domain layer ("Hrot") implementing a military simulation. The cognitive subsystem (AI) sits in:

- **`FDP/ExtDeps/FastBTree/`** — pure behavior-tree kernel + compiler + tests + examples. Its own sub-solution (`FastBTree.sln`), **not included** in the top-level solution.
- **`FDP/ExtDeps/FastHSM/`** — pure hierarchical-state-machine kernel + compiler + tests + examples. Its own sub-solution (`FastHSM.sln`), **not included** in the top-level solution.
- **`FDP/Toolkits/Fdp.Toolkits/Behavior/`** — application-layer glue: tick systems, contexts, registries, channels, scenario translators.
- **`Hrot/Subsystems/Hrot.AI.Behaviors/`** — the project-specific BTree/HSM behaviors used by the Hrot scenarios. Houses `AiBehaviorFactory` and `BehaviorLog`.
- **`Hrot/Subsystems/Hrot.SimHost/`** — bootstrappers, component registry (`CognitiveComponentRegistry`), scenario serializer factory (`HrotScenarioSerializerFactory`).
- **`Hrot/Engine/Hrot.Common/`** — shared types between SimHost / Editor / IG. The new `DebugState` and `PatchDebugStateCommand` live here.
- **`Hrot/Engine/Hrot.Presentation/`** — ImGui renderers for entity inspector (`BrainBlackboardRenderer`, `Blackboard1024Renderer`, etc.). The new trace renderers go here.

### Where the existing pieces live (quick map)

| What you'll touch | Path |
|---|---|
| FastBTree interpreter | [FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs](../../FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs) |
| FastBTree state | [FDP/ExtDeps/FastBTree/src/Fbt.Kernel/BehaviorTreeState.cs](../../FDP/ExtDeps/FastBTree/src/Fbt.Kernel/BehaviorTreeState.cs) |
| FastHSM kernel core | [FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmKernelCore.cs](../../FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmKernelCore.cs) |
| FastHSM trace records | [FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/TraceRecord.cs](../../FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/TraceRecord.cs) |
| FastHSM trace buffer (to be DELETED) | [FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmTraceBuffer.cs](../../FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmTraceBuffer.cs) |
| BTree tick system | [FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BTreeTickSystem.cs](../../FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BTreeTickSystem.cs) |
| HSM tick system | [FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs](../../FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs) |
| BTreeContext | [FDP/Toolkits/Fdp.Toolkits/Behavior/BTreeContext.cs](../../FDP/Toolkits/Fdp.Toolkits/Behavior/BTreeContext.cs) |
| BehaviorRegistry & BehaviorDefinition | [FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs](../../FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs) |
| Component-ID constants (FDP-level) | [FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorApplicationComponentIds.cs](../../FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorApplicationComponentIds.cs) |
| Component-ID constants (Hrot-level) | [Hrot/Engine/Hrot.Core/MapDefinitions/HrotComponentIds.cs](../../Hrot/Engine/Hrot.Core/MapDefinitions/HrotComponentIds.cs) |
| Component registry hook | [Hrot/Subsystems/Hrot.SimHost/CognitiveComponentRegistry.cs](../../Hrot/Subsystems/Hrot.SimHost/CognitiveComponentRegistry.cs) |
| GlobalDebugSettings (to be MOVED) | [Hrot/Subsystems/Hrot.IG/Gizmos/GlobalDebugSettings.cs](../../Hrot/Subsystems/Hrot.IG/Gizmos/GlobalDebugSettings.cs) → `Hrot.Common` |
| Behavior factory | [Hrot/Subsystems/Hrot.AI.Behaviors/AiBehaviorFactory.cs](../../Hrot/Subsystems/Hrot.AI.Behaviors/AiBehaviorFactory.cs) |
| BehaviorLog | [Hrot/Subsystems/Hrot.AI.Behaviors/Logging/BehaviorLog.cs](../../Hrot/Subsystems/Hrot.AI.Behaviors/Logging/BehaviorLog.cs) |
| Scenario serializer factory | [Hrot/Subsystems/Hrot.SimHost/Serializers/HrotScenarioSerializerFactory.cs](../../Hrot/Subsystems/Hrot.SimHost/Serializers/HrotScenarioSerializerFactory.cs) |
| Existing inspector renderers (pattern to copy) | [Hrot/Engine/Hrot.Presentation/Renderers/BrainBlackboardRenderer.cs](../../Hrot/Engine/Hrot.Presentation/Renderers/BrainBlackboardRenderer.cs), `Blackboard1024Renderer.cs` |
| Context-menu integration sites | search for `RegisterContextMenuHandler` (notably `EditorSubsystem.cs:870`, `CgfSubsystem.cs:514`, `SimHostVisualization.cs:166`) |
| Global action IDs | [Hrot/Engine/Hrot.Common/Constants/GlobalActionIds.cs](../../Hrot/Engine/Hrot.Common/Constants/GlobalActionIds.cs) |
| TKB translator composition | [Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs:133](../../Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs#L133) |

---

## Building & testing

The repository has **three** solutions you'll touch:

```powershell
# Top-level
dotnet build IOS-IG-SimHost.sln
dotnet test  IOS-IG-SimHost.sln

# FastBTree sub-solution (NOT in IOS-IG-SimHost.sln but YOU MUST KEEP IT GREEN — Phase 6)
dotnet build FDP\ExtDeps\FastBTree\FastBTree.sln
dotnet test  FDP\ExtDeps\FastBTree\FastBTree.sln

# FastHSM sub-solution (same — Phase 6)
dotnet build FDP\ExtDeps\FastHSM\FastHSM.sln
dotnet test  FDP\ExtDeps\FastHSM\FastHSM.sln
```

> ⚠️ **Critical:** changes to `Fbt.Kernel` and `Fhsm.Kernel` cascade into test/example projects that live in the sub-solutions but are **excluded from the top-level solution**. The top-level build can stay green while the sub-solutions are broken. Phase 6 tasks (T6.1–T6.4) explicitly cover this. **Do not mark the workstream done until both sub-solutions build and test cleanly.**

Project quick-start scripts at the repo root:
- `run_Editor.bat`, `run_SimHost.bat`, `run_IG.bat`, `run_IOS.bat`, `run_all_standalone.bat`
- `build_all_standalone.bat`

---

## Coding standards & house rules

Read [.dev/.guides/DEV-GUIDE.md](../.guides/DEV-GUIDE.md) for the full workflow guide. Key points relevant to this workstream:

- **Strict layer boundaries.** `Fbt.Kernel` and `Fhsm.Kernel` must not reference `Fdp.Toolkits` or any `Hrot.*` project. The new `ITreeTracer` interface (`Fbt.Kernel`) and `HsmTraceContext` unmanaged struct (`Fhsm.Kernel.Data`) exist precisely so the kernels stay pure.
- **Zero allocations in the simulation hot path.** Tracing is gated through `unsafe` pointer arithmetic on ECS chunk memory. The `EmitToLog` path is the only exception, and it must be guarded by `BehaviorLog.IsTraceEnabled` before any string interpolation.
- **256-ID component-type budget.** New ECS components are precious. We use three new IDs (two for trace buffers, one for `DebugState`). Confirm availability in `BehaviorApplicationComponentIds` and `HrotComponentIds` before assigning.
- **`MaxComponentSize = 1024 bytes`** — enforced by `EntityCommandBuffer`. The trace buffers are sized to exactly fill this ceiling.
- **No new `UpdateBefore`/`UpdateAfter` attributes.** They don't exist in this codebase. System ordering inside a phase is driven by registration order; see the comment at [HsmTickSystem.cs:50](../../FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs#L50).

---

## Smoke-testing a finished feature

After Phase 5 lands, the round-trip verification is:

1. Launch Editor or SimHost with a scenario containing AI entities.
2. Right-click an entity with a `BehaviorState` in the inspector.
3. Click "Toggle AI Trace Buffer". The entity gains a `BTreeTraceWorkingMemory1024` or `HsmTraceWorkingMemory1024` component.
4. Expand the component in the inspector — see the live ImGui table of trace records.
5. Save a flight recording, replay it, scrub the timeline backward — trace records appear for historical frames.
6. (Optional) Click "Toggle AI Trace Log" — verify trace lines now appear in the `AI.Behavior` NLog target / log file.

---

## Where to ask for help

- Architectural questions → reread [DESIGN.md](./DESIGN.md), then the original [design-talk.md](./design-talk.md).
- Task-specific questions → check the **Success conditions** of the task in [TASK-DETAIL.md](./TASK-DETAIL.md).
- Codebase questions → use the project's `codebase-memory-mcp` tools (the system reminder at the top of every Claude Code session explains this).
- Process questions → [.dev/.guides/DEV-GUIDE.md](../.guides/DEV-GUIDE.md).

Good luck. Keep the tracker updated as you complete tasks — the next batch of work will pick up directly from your checkboxes.
