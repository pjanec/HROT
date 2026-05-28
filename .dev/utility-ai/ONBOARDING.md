# Utility AI — Onboarding Guide

Welcome to the Utility AI workstream. This document orients a developer picking up the feature for the first time.

---

## What Is Being Built

A **decision-scoring layer** for the HROT/FDP engine: a Brain-resident library that scores competing options (which target to eliminate, which weapon to fire, whether to take cover / flee / advance / suppress / hold, how a squad leader allocates fire across members) and selects the best one. It sits beside the three existing AI authoring systems (FastBTree, FastHSM, Blueprint) and is **consumed by them**, not replacing any of them.

The workstream ships in three coordinated tracks:

1. **Utility AI runtime + authoring** — the scoring core, response curves, input catalog, source generator, analyzer, starter-pack decisions, visual editor.
2. **Runtime tuning console** — live-edit AI knobs (perception ranges, utility weights and curves, squad focus-fire caps) in a running cluster, with the changes recorded into the Flight Recorder for deterministic replay.
3. **AI debug overlays** — in-world rendering of perception cones, EQS scored candidates, target memory, utility decision breakdowns, and squad assignment lines. Triggered per-entity by `DebugState.Flags`.

The three tracks share four artifacts (trace buffer, source generator, curve widget, GizmoMap integration) and are built interleaved per the build-order plan.

---

## Planning Artifacts

| Document | Location | Purpose |
|---|---|---|
| Architecture (v1.2) | [`.dev/utility-ai/Utility_AI_Design_v1_1.md`](./Utility_AI_Design_v1_1.md) | Scoring core, curves, inputs, storage, group fire, integration with BTree/HSM/Blueprint |
| Source generator (v1.1) | [`.dev/utility-ai/Utility_AI_SourceGenerator_Design_v1_1.md`](./Utility_AI_SourceGenerator_Design_v1_1.md) | `In.*` accessors, registrars, `UT####` diagnostics |
| Editor (v1.2) | [`.dev/utility-ai/Utility_AI_Editor_Design_v1_2.md`](./Utility_AI_Editor_Design_v1_2.md) | Card-table window, curve editor, comparison integration |
| Editor wireframes | [`.dev/utility-ai/Utility_AI_Editor_Wireframes.md`](./Utility_AI_Editor_Wireframes.md) | ASCII mockups of every editor surface |
| Starter pack (v1.2) | [`.dev/utility-ai/Utility_AI_StarterPack_Examples_v1_1.md`](./Utility_AI_StarterPack_Examples_v1_1.md) | Four canonical decisions + integration tests + scaffolding |
| Tuning console & overlays (v1.0) | [`.dev/utility-ai/Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md`](./Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md) | Tuning registry, `TuningConsoleGizmo`, five overlays |
| Curve widget in StructEdit (v1.1) | [`.dev/utility-ai/Curve_Editor_in_StructEdit_Guide_v1_1.md`](./Curve_Editor_in_StructEdit_Guide_v1_1.md) | Wrap pattern: editor's curve widget → tuning console drawer |
| Build order (v1.0) | [`.dev/utility-ai/Build_Order_UtilityAI_Tuning_Overlays_v1_0.md`](./Build_Order_UtilityAI_Tuning_Overlays_v1_0.md) | Six-phase interleaved plan |
| Phase-0 bundle | [`.dev/utility-ai/PREREQ_Phase0_Bundle.md`](./PREREQ_Phase0_Bundle.md) | Six prereq codebase changes (P0.1–P0.6) |
| Task detail | [`.dev/utility-ai/TASK-DETAIL.md`](./TASK-DETAIL.md) | Per-task specs with success conditions |
| Task tracker | [`.dev/utility-ai/TASK-TRACKER.md`](./TASK-TRACKER.md) | Checklist linking to task detail |
| Debt tracker | [`.dev/utility-ai/DEBT-TRACKER.md`](./DEBT-TRACKER.md) | Technical-debt log discovered during implementation |

---

## Folder Layout

### New code lives in

```
Fdp.Toolkits/Utility/                 (Phase 1+)
├── Core/                              -- scorer, aggregator, curves, structs (§4, §5, §8 of Architecture)
├── Inputs/                            -- StandardInputs.cs catalog + [UtilityInput] reader contract
├── Components/                        -- UtilityResultBuffer, UtilityDebugFlags, ThreatMatrixAssignmentState
├── Diagnostics/                       -- UtilityTraceWorkingMemory1024 (sibling to BTree/HSM trace buffers)
├── Group/                             -- ThreatMatrixAssignmentSystem (leader greedy assignment)
├── Authoring/                         -- [UtilityDecision], IUtilityDecisionDefinition, fluent builder
└── Integration/                       -- BTree UtilitySelectorNode, HSM arbiter, Blueprint nodes

Fdp.Toolkits.Analyzers/                (Phase 2 — extend existing assembly)
├── UtilityInputGenerator.cs           -- IIncrementalGenerator, mirrors BTreeActionGenerator
├── UtilityDecisionGenerator.cs        -- IIncrementalGenerator, mirrors BTreeDefinitionGenerator
└── UtilityAuthoringAnalyzer.cs        -- DiagnosticAnalyzer, mirrors EqsTemplatePurityAnalyzer for UT0130

Hrot.Utility.Editor/                   (Phase 5)
├── Windows/                           -- ManagedWindow card-table host
├── Curve/                             -- references the standalone CurveWidget (Phase 3)
├── Emit/                              -- UtilityFluentEmitter
└── Comparison/                        -- UtilityComparisonSanitizer + tuning-diff fast lane

Hrot.Diagnostics/Hrot.Diagnostics.Tuning/    (Phase 4 Slice 1, Phase 6 Slice 2)
├── TuningRegistry.cs, Tunable.cs, TuningChangeEvent.cs
├── UtilityTuningBinder.cs             -- auto-registers UtilityDecisionDef weights/curves as tunables
└── Gizmos/TuningConsoleGizmo.cs       -- IStatefulGizmo, StructInspector-backed

Hrot.Diagnostics/Hrot.Diagnostics.Overlays/  (Phase 4)
├── AiOverlayFlags.cs
├── PerceptionOverlaySource.cs, TargetMemoryOverlaySource.cs, EqsOverlaySource.cs,
│   UtilityDecisionOverlaySource.cs, SquadAssignmentOverlaySource.cs
└── OverlayBudgetArbiter.cs

Hrot.AI.Tests/Utility/                 (Phase 0+)
└── UtilityTestWorld.cs                -- shared Brain-only scaffolding (P0.6)
```

### Existing code consulted / modified

```
FDP/Toolkits/Fdp.Toolkits/Combat/Components/CombatComponents.cs        -- WeaponState (P0.1 adds MaxAmmo)
FDP/Toolkits/Fdp.Toolkits/Combat/Translators/CombatTkbTranslator.cs    -- spawn site (P0.1 + P0.2)
FDP/Toolkits/Fdp.Toolkits/Perception/PerceptionConstants.cs            -- MaxTrackedTargets (P0.3 raises to 16)
FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs -- TargetMemory, SensorContactList
FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs                 -- EqsSensor, EqsCognitiveBuffer
FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs    -- Blackboard1024 (P0.5 adds Project<T>)
FDP/Toolkits/Fdp.Toolkits/Behavior/Diagnostics/                        -- BTreeTrace/HsmTrace family (pattern for Utility trace)
FDP/Engine/Fdp.Core/CommandHierarchy/UnitRoster.cs                     -- P0.4 adds Add/IndexOf
FDP/Engine/Fdp.Core/CommandHierarchy/UnitSubordinate.cs                -- read-only consumer
FDP/Toolkits/Fdp.Toolkits.Analyzers/                                   -- existing generators/analyzers (Phase-2 extends)
FDP/ExtDeps/StructEdit/src/StructEdit.Core/Plugins/ICustomFieldEditor.cs  -- curve widget wrap (Phase 6)
FDP/Engine/Fdp.Presentation/ImGui/Editing/IImGuiFieldDrawer.cs          -- curve widget wrap (Phase 6)
FDP/ExtDeps/GizmoMap/                                                  -- substrate for tuning console + overlays
Hrot/Editor/Hrot.Editor.AiShared/                                      -- shared editor infra (4 small touches in §11 of Editor DD)
Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs   -- precedent for Unsafe.As Blackboard1024 projection
```

---

## Build and Run

Build the full solution from the workspace root:

```bat
dotnet build IOS-IG-SimHost.sln
```

Run all tests (no rebuild):

```bat
dotnet test IOS-IG-SimHost.sln --no-build
```

Run only the Utility-AI tests (once they exist):

```bat
dotnet test Hrot\Subsystems\Hrot.AI.Tests\Hrot.AI.Tests.csproj --no-build
```

(Substitute the actual test-project path created in Phase 0.)

Source generators in `Fdp.Toolkits.Analyzers` run automatically during build. After Phase 2, watch for:

- `UtilityInputRegistrar.g.cs` — one `Register(...)` call per `[UtilityInput]` reader.
- `UtilityInputAccessors.g.cs` — one `In.<Name>` accessor per reader.
- `UtilityDecisionCatalog.g.cs` — one entry per `[UtilityDecision]` plus per-decision `.Id` constants.
- `UT####` diagnostics on authoring mistakes.

---

## Where to Start

1. **Read** the design docs in roughly this order: Architecture v1.2 → Build Order → Phase-0 Bundle → Source Generator → Editor → Tuning Console & Overlays → Curve Editor in StructEdit → Starter Pack. The Architecture doc is the load-bearing reference; everything else extends or implements it.

2. **Then** open [`TASK-TRACKER.md`](./TASK-TRACKER.md) and start at Phase 0 — the prerequisite bundle blocks all Phase-1 work. Each task has explicit success conditions in [`TASK-DETAIL.md`](./TASK-DETAIL.md).

3. **Key invariants** to internalize before writing code:
   - The `[InlineArray]` defensive-copy trap (architecture §8.2): writes through the direct indexer are silently lost. Always cast to `Span<T>` via the type's `GetSpanRW()`/`GetSpanRO()` helpers (mirrored from `EqsCognitiveBuffer.GetSpanRW()`).
   - The cap invariant: `PerceptionConstants.MaxTrackedTargets <= UtilityConstants.TopN`. P0.3 sets both to 16.
   - Hash formula: 32-bit FNV-1a (basis `2166136261`, prime `16777619`), truncated to 16 bits for `InputId`. Identical to `BTreeActionGenerator.ComputeHash`. Any divergence silently breaks dispatch.
   - `Build` purity: a `[UtilityDecision]`'s `static void Build(...)` must be deterministic and not read live state. `UT0130` enforces.

---

## Development Workflow

Read [`.dev/.guides/DEV-GUIDE.md`](../.guides/DEV-GUIDE.md) for the batch-based workflow used in this project. Work is organized into batches; each batch produces a batch report. Key rules:

- All new unmanaged structs must have `[StructLayout(LayoutKind.Sequential)]`.
- Byte-size constraints on parameter structs are hard (e.g. BTree action params ≤ 60 bytes).
- Never add a new ECS component without a corresponding entry in `GlobalComponentIds`.
- The 256 component-type limit is hard.
- When in doubt about an existing API, prefer reading the code over assuming; the codebase-memory graph is the fastest way to navigate.

---

## Track Status

Track progress in [TASK-TRACKER.md](./TASK-TRACKER.md). Log discovered technical debt in [DEBT-TRACKER.md](./DEBT-TRACKER.md) with priority and target batch.
