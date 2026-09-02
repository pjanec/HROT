# Workstream: BTree AI Action/Condition Parameter Binding

> **Origin:** user (2026-06-15). Continuation of DEC-05 (`.dev/_DONE/ai-hsm-btree-vis-edit-2/DEC/`). Goal: make JSON-authored behavior trees bind **multiple** actions/conditions — each with its **own** parameter DTO at its **own** blackboard offset — via the visual editor, with runnable demos. Then plan the future (Slice 2: multiple *stateful* primitives per entity).
> **Execution model:** lead (opus) writes specs + hard-verifies; coding delegated to sonnet/zoo; commit per batch; token-constrained. Build-verify with `dotnet build-server shutdown` before codegen checks.
> **Status:** Slice 1 **and** Slice 2 **architect-approved** (2026-06-15). Slice 1 ready to implement; Slice 2 carries three architect-mandated fixes (SLICE2-DESIGN §10) to apply during its implementation. **Canonical design now also lives in `docs/blueprints/BTree_AiActionParameterBinding_Detailed_Design.md`** (this folder holds the working drafts + demo specs). No implementation started yet (awaiting user go). Foundations already landed under DEC-05; see SLICE1-DESIGN §6.

## Documents
- **Canonical design:** `docs/blueprints/BTree_AiActionParameterBinding_Detailed_Design.md` ("AIB-DD") — the architect-approved design of record (Slice 1 + Slice 2).
- **[SLICE1-DESIGN.md](SLICE1-DESIGN.md)** — working draft: bind multiple stateless actions/conditions (distinct DTOs, distinct offsets) to authored blackboard variables; editor authoring; Slice 1 demo specs.
- **[SLICE2-DESIGN.md](SLICE2-DESIGN.md)** — working draft: lift the "one stateful AiPrimitive per entity" constraint via Option β + the three mandated fixes; Slice 2 demo specs.
- **[TASK-TRACKER.md](TASK-TRACKER.md)** — task checklist (S1-0…S1-G, S2-1…S2-G) with dependencies + implementation order.
- **[TASK-DETAIL.md](TASK-DETAIL.md)** — per-task scope + explicit success conditions / named test specs (the acceptance criteria a coding agent must implement, not invent).
- **[DEBT-TRACKER.md](DEBT-TRACKER.md)** — prerequisites, deferrals, watch-items, resolved.
- **[ARCHITECT-REVIEW-BRIEF.md](ARCHITECT-REVIEW-BRIEF.md)** — the brief used for the architect review (both passes done; folded into the designs).

## One-paragraph orientation
Behavior parameters live as unmanaged bytes in the entity's `BrainBlackboard` (100 B inline) and, when bigger, `Blackboard1024` (heavy). A reusable action/condition takes its DTO as `ref` param 0; the source generator projects it via `Unsafe.As` + `Unsafe.AddByteOffset(BehaviorParameters, offset)` at a **baked, bin-packed byte offset** — so many distinct-DTO actions coexist without collision. Blueprints (AiPrimitives) are the primary *authoring* source of these actions/conditions and compile to the **same** projection model. Slice 1 supports any number of **stateless** actions/conditions (params only). Slice 2 lifts the one-per-entity limit on **stateful** primitives (those with local working state). This split — verified against code and confirmed by the architect — is the spine of the two design docs.

## Key verified facts (ground truth, used by both docs)
- `BrainBlackboard` = 128 B; `BehaviorParameters` = `fixed byte[100]` at offset 0; interrupt/tail registers at 120–127. `MaxBehaviorParamByteSize=100`. ([BehaviorComponents.cs](../../FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs), `BehaviorConstants.cs`)
- Baked-offset projection is **already emitted today**: `Unsafe.AddByteOffset(ref bb.BehaviorParameters, (nint)entry.Offset)` — [BTreeActionGenerator.cs:693](../../FDP/Toolkits/Fdp.Toolkits.Analyzers/BTreeActionGenerator.cs#L693) (SharedAi path); the legacy `paramIndex*sizeof` AiPrimitive form is superseded.
- `BlackboardBinPacker`: `MaxInlineBytes=100`, `MaxHeavyBytes=928`, sequential alignment-padded offsets. (`Hrot.Editor.AiShared/Blackboard/BlackboardBinPacker.cs`)
- `Blackboard1024` (1024 B) + `BehaviorDefinition.HeavyDtoType` + `BehaviorIngressSystem` provisioning + `[SharedAiHeavyAction]`. `FDP_001` hard-errors an oversize master DTO (`BehaviorParameterSizeAnalyzer`).
- Partition allocator `BlueprintBlackboardPartitions.{TryAttach,TryDetach,TryGetSlotOffset,CopyToLargerTier}` — Slice-1-proven (Instance dispatch). (`FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintBlackboardPartitions.cs`)
- PREREQ-A (JSON BTrees execute real bound actions/conditions) is **done** (`8eb45e0c`); demo nodes `DemoCounterNodes` exist (`33e09ec1`).
