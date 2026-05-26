# Onboarding — Blackboard Authoring for the BTree & HSM Editors

Welcome. This document orients a new developer joining the **visual blackboard authoring** work. Read it, then read the design and the dev guide before writing any code.

## 1. What we are building

The HROT AI editors (BTree and HSM) already let designers visually author tree topology and state machines, but the **blackboard DTO** — the C# struct holding an asset's parameters and runtime state — has stayed hand-written. Non-programmer designers can author logic but not the memory it operates on.

This project adds visual blackboard authoring:

- A **Blackboard Variables panel** in both editors: add / remove / rename / retype variables and write per-field comments without leaving the editor.
- **Recursive aggregation** of parameter requirements from nested behaviors (sub-BTrees, HSM-embedded BTrees) into the parent's variable list, with opt-in aliasing.
- **Two sharing models** — Approach A (whole-DTO aliasing, true pointer sharing) and Approach B (field-level copy-down/copy-up via auto-generated orchestrators).
- **Editor-owned C# DTO emission** that round-trips comments as `///` XML docs and preserves user-introduced exotic fields read-only and byte-for-byte.

C# stays the single source of truth — no sidecar files. The editor either owns a file (marker `HROT_EDITOR_GENERATED` present, regenerated on save) or the user owns it (no marker, never written).

**Out of scope** (see design §15): kernel-side DTO projection (owned by FastBTree/FastHSM), Blueprint's full C# ownership migration (Tier B, deferred), live-edit of running-entity values.

## 2. The documents (all in this folder: `.dev/ai-hsm-btree-vis-edit/`)

| Document | Purpose |
|----------|---------|
| [`Blackboard_Authoring_Detailed_Design.md`](./Blackboard_Authoring_Detailed_Design.md) | **The design.** Read it end to end — it is the authority on behavior. Sections referenced as "BB §x". |
| [`TASK-DETAIL.md`](./TASK-DETAIL.md) | Per-task descriptions with success conditions (test specs). Starts with **§0 Reconciliation** — read that first; it maps design-era names to the real committed names/paths. |
| [`TASK-TRACKER.md`](./TASK-TRACKER.md) | One line per task with status checkboxes and links into the detail doc. |
| [`DEBT-TRACKER.md`](./DEBT-TRACKER.md) | Deferred P2/P3 issues found during implementation. |
| `design-talk.md` | The brainstorm transcript that produced the design. **Background only — do not implement from it; the design supersedes it.** |

Related design specs (in `docs/blueprints/`) — all already implemented in the codebase:
`AI_Editor_Shared_Infrastructure.md`, `BTree_Editor_NodeEditor_Host_Design.md`, `HSM_Editor_NodeEditor_Host_Design.md`, `Blueprint_Subsystem_Editor_Detailed_Design.md`, and the NodeEditor extension docs.

## 3. Where the code lives

> ⚠️ The design header lists slightly different paths. The table below (and TASK-DETAIL §0) reflects the **actual committed layout** — use these.

**Shared editor infrastructure** — `Hrot/Editor/Hrot.Editor.AiShared/`
- `Emit/` — `FluentCSharpEmitterBase`, `IFluentCSharpEmitter`, `UsingDirectiveSet` (extend for DTO emit).
- `References/` — `SubElementKind` (already has `BlackboardField`; you add `BlackboardVariable`), reference catalog.
- `Refactor/` — `IRefactorService` (rename propagation).
- `Catalog/` — `IAssetCatalog` (+ `Changed` event), contributor pattern.
- `Selection/` — `EditorSelectionStore`, `IAssetSubSelection`.
- **New:** `Blackboard/` — schema exporter, aggregator, bin-packer, DTO emitter, source-text parser, authoring window, shared `VariablesPanelControl`.
- Tests: `Hrot/Editor/Hrot.Editor.AiShared.Tests/`.

**BTree editor** — `Hrot/Subsystems/AI/Hrot.BTree.Editor/`
- `Model/` — `BehaviorTreeAsset`, `BehaviorTreeAssetProjector`.
- `Blackboard/` — existing `BlackboardSchemaBuilder` (the read-only path we extend); **new** orchestrator templates + bin-packer integration.
- `Inspector/` — `BlackboardFieldPickerAttribute` (type-filter it).
- Tests: `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/`.

**HSM editor** — `Hrot/Subsystems/AI/Hrot.Hsm.Editor/`
- `Model/` — `HsmAsset`, `HsmAssetProjector`; `Validation/` — `HsmAssetValidator` (extend for cross-region conflicts).
- **New:** `Blackboard/` — HSM aggregator + orchestrator templates.
- Tests: `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/`.

**Kernel / runtime (Phase 0 additive work)**
- `FDP/Toolkits/Fdp.Toolkits/Behavior/` — `BrainBlackboard`, `Blackboard1024`, `BehaviorConstants` (`MaxBehaviorParamByteSize = 100`, `BrainBlackboardByteSize = 128`), `BehaviorRegistry`, `BehaviorIngressSystem`.
- `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/` — `[BTreeDefinition]`, `SharedAiAttributes`. Add `BlackboardManaged`, `HeavyDtoType`, `[BlackboardDtoStruct]`, `[BlackboardReadOnly/ReadWrite]`.
- `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/` — `HsmActionDispatcher` (static, generated), HSM definition attribute equivalents.

**Blueprint editor (Phase 1.5g)** — `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/` and the `AiPrimitiveEmitter`.

## 4. Building & testing

The solution is `IOS-IG-SimHost.sln` at the repo root. Standard .NET:

```powershell
dotnet build IOS-IG-SimHost.sln           # full build
dotnet test  IOS-IG-SimHost.sln           # all tests
```

Per-project (faster while iterating) — build/test only what you touch, e.g.:

```powershell
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj
dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj
```

Kernel changes touch the `FDP/ExtDeps/FastBTree` and `FastHSM` source generators — rebuild the full solution after Phase 0 so generated code regenerates.

## 5. How to work — read the DEV-GUIDE

**Before coding, read [`../.guides/DEV-GUIDE.md`](../.guides/DEV-GUIDE.md).** It defines the batch-based workflow expected of you: read the whole instruction set and referenced specs first, study existing patterns, follow TDD where practical, test as you go, document deviations with rationale, and submit a thorough report. Key habits for this project:

- **Follow existing patterns.** The shared infra, emitters, and pickers are tested and stable — match their style; reuse `FluentCSharpEmitterBase`, `IRefactorService`, `IAssetCatalog`, `EditorSelectionStore`. Do not rename or rework committed code unless it is a genuine bug.
- **Round-trip determinism is sacred.** Any emit change must keep RT-1 (no-edit save is byte-identical) and RT-2 (single edit = clean diff) green (BB §3.7, TASK-BB-1b-06).
- **Success conditions are the test specs** in each task's "Verifies" line; design §16 is the overall test strategy.
- Work the phases in order: **Phase 0 → 1.5a → … → 1.5g.** Each slice has an acceptance gate in BB §15.

## 6. Recommended first read order

1. This file.
2. `../.guides/DEV-GUIDE.md`.
3. `Blackboard_Authoring_Detailed_Design.md` (end to end).
4. `TASK-DETAIL.md` §0 (reconciliation), then your assigned phase.
5. The referenced existing code in the folders above, to see the patterns you'll extend.
