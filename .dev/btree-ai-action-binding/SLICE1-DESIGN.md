# Slice 1 — Detailed Design: BTree action/condition parameter binding (+ demos)

> **Status:** design approved-in-principle (user + architect, 2026-06-15); **not yet implemented** (this workstream's binding codegen). Foundations from DEC-05 already landed — see §6.
> **Scope:** authored JSON behavior trees bind **multiple stateless** actions/conditions, each with its own parameter DTO at its own bin-packed blackboard offset, via the visual editor; runnable demos. **Out of scope (Slice 2):** multiple *stateful* primitives per entity — see [SLICE2-DESIGN.md](SLICE2-DESIGN.md).

## 1. Goal & motivation
A real behavior composes many actions/conditions, each with a different parameter DTO (and sometimes the same action reused with different DTO instances). Binding all of them to **offset 0** is useless. Slice 1 makes the authored/JSON path support **distinct, non-zero, bin-packed offsets** per node binding — the realistic case — and surfaces it in the editor with runnable proof.

## 2. Verified architecture (ground truth)
All confirmed in code + by the architect (2026-06-15).

1. **Memory.** `BrainBlackboard` (128 B): `BehaviorParameters` = `fixed byte[100]` at offset 0; interrupt/tail registers at 120–127. Behavior input params + reusable-action DTOs live in the 100 B inline region; overflow → `Blackboard1024` (1024 B, ~928 usable). `MaxBehaviorParamByteSize=100`.
2. **Assignment.** A generated/hand-written `ParseParamsDelegate(json, byte*)` runs once at behavior assignment, writing defaults into `BehaviorParameters` ([BehaviorIngressSystem.cs:93](../../FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorIngressSystem.cs#L93)).
3. **Projection (the key mechanism).** A reusable action/condition is `static NodeStatus M(ref TDto p, ref BehaviorTreeState, ref BTreeContext)`. The generator emits a thunk projecting the DTO at a **baked byte offset**: `Unsafe.As<…>(ref Unsafe.AddByteOffset(ref bb.BehaviorParameters, (nint)offset))`. **Already emitted today** at [BTreeActionGenerator.cs:693](../../FDP/Toolkits/Fdp.Toolkits.Analyzers/BTreeActionGenerator.cs#L693) for `[SharedAiAction]` entries. The legacy AiPrimitive `paramIndex * sizeof(Params)` form (homogeneous arrays) is superseded; **`paramIndex` is ignored** in the baked-offset pipeline.
4. **Registry / runtime.** The blob is type-erased: `MethodNames[]` hold string keys `{Type}.{Method}@{offset}` (or bare name for 4-param). `Interpreter<BrainBlackboard,BTreeContext>.BindActions` resolves each key in the injected `ActionRegistry`; a miss installs a `=>Failure` fallback. The JSON bridge injects a populated registry (PREREQ-A, `8eb45e0c`). Tick site `BTreeTickSystem.cs:136` is hard-typed to `BrainBlackboard` — **we keep it that way** (no generic-runtime change).
5. **Bin-packer.** `BlackboardBinPacker` computes sequential, C#-alignment-padded offsets; `MaxInlineBytes=100`, `MaxHeavyBytes=928`. Editor offsets are *advisory* for the budget UI; the **authoritative** offset is the compiled struct layout (`Marshal.OffsetOf`).
6. **Blueprints = primary authoring source.** A blueprint **AiPrimitive** authors an action/condition as a function: typed **Parameters** → `BrainBlackboard` (generated `struct Params`), **WorkingState** (locals) → `Blackboard1024`, **return** `NodeStatus`/`bool`, body = graph. It compiles to `BTreeTick`/`BTreeEvaluate` thunks registered into `BehaviorRegistry` — the **same** projection/memory model as hardcoded `[SharedAiAction]` ([AiPrimitiveEmitter.cs](../../Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/AiPrimitiveEmitter.cs)). So authored-via-blueprint and hardcoded-C# actions are interchangeable at the binding layer.
7. **Aliasing.** Multiple nodes whose `ExpressionTargetField` points to the **same** variable → the bin-packer reserves the bytes once → the generator bakes the **same** offset into each node's thunk → zero-copy shared state. No per-node/orchestrator overhead.
8. **Overflow.** Master DTO > 100 B → `FDP_001` hard compiler error (`BehaviorParameterSizeAnalyzer`) + runtime guard. Overflow that *is* allowed spills to `Blackboard1024` (`HeavyDtoType` + `[SharedAiHeavyAction]`).
9. **Slice 1 constraint.** Exactly **one stateful** AiPrimitive working-state per entity (the `Blackboard1024` `Memory+8` / single `StructureHash` collision). Stateless actions/conditions (params only) have **no** such limit. → Slice 2 lifts it.

## 3. The Slice 1 design

### 3.1 Authoring model (whole-DTO binding)
- The asset has an **editor-managed (Category-2)** blackboard: a list of typed **variables**, each variable being the **whole parameter DTO** of some action/condition.
- A node binds its entire DTO param to exactly **one** variable via `ExpressionTargetField` (approved "whole-DTO binding", Addendum v3 §2.2).
- **"+ Promote to new variable"** creates an auto-managed (`IsAutoManaged`) variable of the action's DTO type for static-input-only actions (hidden from the main list; defaults baked into `ParseParams`).
- **Read-only reflection of hardcoded DTOs (Category-1):** when a node binds a hardcoded-C# action whose DTO is not an editor-managed variable, the Variables panel **reflects that DTO's fields read-only** (so the designer can see them). This is new (the panel today shows only editor-managed variables).

### 3.2 Codegen (the core new work)
For each managed BTree asset, `BTreeJsonGenerator` generates:
1. **Per-asset blackboard struct** from the authored variables (reuse `BlackboardDtoEmitter`), `[StructLayout(Sequential)]`, bin-packed to fit the 100 B inline region; **offsets authoritative from the compiled struct** (`Marshal.OffsetOf`).
2. **Topology over that struct** — each binding compiles to a blob key `{Type}.{Method}@{offset}` (the builder's expression overload computes the offset from the generated struct).
3. **Per-asset registrar** — for each bound (method, variable), register a `ref BrainBlackboard` thunk keyed identically: `Unsafe.As<…,TDto>(ref Unsafe.AddByteOffset(ref bb.BehaviorParameters, (nint)offset))` → call the method. Mirrors the existing `BTreeActionGenerator` emission. Registered into the registry the bridge already injects (PREREQ-A). Heavy variables emit a `[SharedAiHeavyAction]`-style thunk that fetches `Blackboard1024` via `ctx.World`/`ctx.Self` (deferred unless a demo needs >100 B).

Net: blob keys and registry keys both derive from the **same** generated struct's `Marshal.OffsetOf`, so they always match; the runtime stays `Interpreter<BrainBlackboard>`.

### 3.3 Validator
Unblock `ThreeParamReusable` at [BTreeMethodCompatibilityValidator.cs:149](../../Hrot/Subsystems/AI/Hrot.AiEditor.Generators/BTreeMethodCompatibilityValidator.cs#L149): accept it iff the method has the 3-param reusable shape (`ref TDto`, `ref BehaviorTreeState`, `ref TCtx`, returns `NodeStatus`) **and** `ExpressionTargetField` resolves to an authored variable whose declared type equals `TDto`. Otherwise emit a clear `BTREE0002` skip (never a build break). **Must land together with §3.2** — opening the gate without the per-asset struct would turn skips into build breaks.

### 3.4 Editor UX
- Node inspector **field-picker** (type-filtered `[BlackboardFieldPicker]`) sets `ExpressionTargetField` to a variable of the matching DTO type; "+ Promote to new variable".
- Variables panel: managed variables (editable) + read-only reflected hardcoded DTOs (§3.1).

## 4. Work breakdown (DO NOT implement yet)
| ID | Title | Notes |
|---|---|---|
| S1-1 | Variables-panel read-only reflection of hardcoded DTOs (Category-1) | Independent, additive; unblocks "see the DTO in the panel". |
| S1-2 | Per-asset struct + topology-over-struct codegen | `BTreeJsonGenerator` + `BlackboardDtoEmitter`; guarded `Managed==true`; byte-identity gate intact. |
| S1-3 | Per-asset baked-offset registrar codegen | Mirror `BTreeActionGenerator` emission; register into injected registry. |
| S1-4 | Validator unblock for `ThreeParamReusable` | Lands with S1-2/S1-3. |
| S1-5 | Node-inspector field-picker + promote-to-variable | Editor authoring. |
| S1-6 | Demo assets + runtime proof tests | §5. |

## 5. Slice 1 demo specifications
Demo nodes already exist: `DemoCounterNodes` (`33e09ec1`) — `DemoCounterParams { int Counter; int Threshold }`, `[BTreeCondition] Condition_CounterBelowThreshold`, `[BTreeAction] Action_IncrementCounter` (both **stateless**; `@0` bridges already generated).

- **Demo 1 — single action, single variable.** Managed blackboard with one `DemoCounterParams` variable; `Sequence[ Condition_CounterBelowThreshold, Action_IncrementCounter ]` bound to it; tick N frames; assert `Counter` climbs to `Threshold` then the condition fails. Proves the basic authored bind+run.
- **Demo 2 — multiple actions, multiple distinct DTOs, non-zero offsets (the headline).** Two (or more) variables of *different* DTO types at distinct bin-packed offsets, each bound to a different action/condition; include a `Repeater` decorator. Proves the real multi-action case the offset-0 model couldn't do. (Add a second tiny stateless DTO+action, e.g. `DemoFlagParams { bool Done } + Action_SetDone` / `Condition_IsDone`.)
- **Demo 3 (optional) — aliasing.** Two action nodes bind the *same* variable → assert they observe each other's writes (zero-copy shared state).
- **Observation (mirrors blueprint `CountingDemo`).** (a) Runtime proof test: attach behavior to an entity, tick, assert blackboard fields (pattern: `CountingDemo_ProofTests.cs`); (b) live: `BrainBlackboardRenderer` + `BTreeVisualizerRenderer` show the counter and active node in the entity inspector.
- **Authoring modes.** First demos may use **hardcoded** `DemoCounterNodes` with DTOs **reflected read-only** in the panel (S1-1). The same demo can later be re-expressed with **blueprint-authored AiPrimitives** — identical memory model, so no rework.

## 6. Already done (foundations, DEC-05)
- Load-flag round-trip fix + managed demo asset `T09` (`b2e09b29`); "Use editor-managed blackboard" toggle (`90c19d52`).
- **PREREQ-A** — JSON BTrees execute real bound actions/conditions; bridge injects a populated registry; live `CgfSubsystem` no longer discards JSON behavior defs (`8eb45e0c`).
- Demo nodes `DemoCounterNodes` (`33e09ec1`).
- FolderIcons test relaxed (`8ee40a33`).

## 7. Risks & verification discipline
- **Byte-identity gate** (`ByteIdenticalGateTests`, CombatShowcase/SampleScout): keep all emit changes behind `Managed==true`.
- **Incremental-generator caching:** `dotnet build-server shutdown` + `-t:Rebuild` for any codegen verification.
- **Validator/struct coupling:** S1-4 must not ship before S1-2/S1-3.
- Hard-verify every batch (diffs + real build + tests + a runtime tick proof); never trust agent reports; exclude editor autosave drift from commits.

## 8. Open decisions (for the user)
1. First demo authoring mode: hardcoded-DTO-reflected (faster) vs blueprint-AiPrimitive-authored (the end state). Recommended: hardcoded first, then re-author one as a blueprint.
2. Authored **heavy** (>100 B) variables in Slice 1, or keep heavy hand-written (`[SharedAiHeavyAction]`) until later? Recommended: keep heavy hand-written for Slice 1 demos.
