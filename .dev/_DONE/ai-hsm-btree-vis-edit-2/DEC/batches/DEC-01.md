# DEC-01 — BTree demo assets (render showcase + trivial focused set)

**Workstream:** DEC ([../DEC-PLAN.md](../DEC-PLAN.md)). **Layer:** content only (`.btree.json` assets). **No C# changes.** **Size: medium.**

## Goal

Author new `.btree.json` demo assets that **(a) compile** (the BTree source generator emits valid C# and the project builds) and **(b) round-trip** through the editor persistence path. Two purposes:
1. **One rich render-showcase** exercising every renderable BTree feature (so we can visually validate the canvas, especially decorator **pills**).
2. **A set of trivial single-focus assets** (each isolates one feature) used to develop + test the decorator-authoring UX in later batches.

## Hard constraints (read first)

- **DO NOT modify** `CombatShowcase.btree.json` or `SampleScout.btree.json`. They are covered by a byte-identity gate (`ByteIdenticalGateTests`) — any edit breaks it.
- **DO NOT touch any C#.** Content only.
- **Binding rule (VE-DEBT-002 — critical):** these assets declare `"BlackboardTypeName": "Fdp.Toolkit.Behavior.Components.BrainBlackboard"`. On that blackboard you may ONLY bind **whole-blackboard (FourParamFull) actions**. The one proven-good action is:
  - `Hrot.AI.Behaviors.Brains.CgfNodes.Action_Wander` — `Action` block: `{ "MethodFqn": "Hrot.AI.Behaviors.Brains.CgfNodes.Action_Wander", "ExpressionTargetField": null, "DelegateShape": "FourParamFull" }` (exactly as `CombatShowcase.btree.json` does it).
  - **NO bound `[BTreeCondition]`** and **no DTO-param action** (e.g. `Action_FireAtTarget`) — they take a typed DTO slice that `BrainBlackboard` has no field for, so the generated `.Condition(...)`/`.Action(dto => dto.X, ...)` won't compile. If you need a "condition-ish" leaf, see the Condition note below.

## Reference: the format

Study `Hrot/Subsystems/Hrot.AI.Behaviors/Assets/BTrees/CombatShowcase.btree.json` and `SampleScout.btree.json` (already in the repo) for the exact schema: `$meta`, `AssetId` (unique GUID per asset), `Name`, `TargetNamespace` (`Hrot.AI.Behaviors.Trees`), `BlackboardTypeName`, `ContextTypeName` (`Fdp.Toolkit.Behavior.BTreeContext`), `Nodes[]` (each with `kind`, `VisualId`, `ChildVisualIds`, `DisplayLabel`, `EditorMetadata{X,Y,Comment,Collapsed,Color}`, and kind-specific blocks `Action`/`Wait`/`Subtree`), `Pills[]`, `Canvas`, `SubtreeSyncBindings`, `Suppressions`, `Blackboard`.

Node `kind` values: `Root`, `Sequence`, `Selector`, `Parallel`, `ObserverSelector`, `Action`, `Condition`, `Wait`, `Subtree`. Pill `DecoratorType` values: `Inverter`, `Repeater` (IntParam = count), `Cooldown` (FloatParam = seconds), `ForceSuccess`, `ForceFailure`, `UntilSuccess`, `UntilFailure`. Pills attach to a host node via `HostNodeVisualId` and carry `StackIndex` (0 = leftmost/innermost).

Use fresh, unique GUIDs for every `AssetId` and `VisualId` (do not reuse CombatShowcase/SampleScout ids). Discovery is automatic: the csproj globs `Assets\BTrees\**\*.btree.json` recursively, so a subfolder is fine.

## Assets to author

### 1. Rich render-showcase — `Assets/BTrees/BTreeRenderShowcase.btree.json`
One tree exercising the breadth of renderable features:
- `Root → Selector` with 3 children: a `Sequence`, a `Parallel`, and an `ObserverSelector`.
- At least one `Action` leaf bound to `Action_Wander`; at least one `Wait` leaf; one `Subtree` leaf referencing `SampleScout` (copy the `Subtree` block shape + `SubtreeAssetId`/`SubtreeName` from CombatShowcase).
- **All 7 decorator pill types** distributed across leaves via `Pills[]` (this is the main reason for the asset — it must visually exercise every pill). Stack at least 2 pills on one host to show a stack.
- Tasteful X/Y layout (mirror CombatShowcase spacing: composites ~130px vertical steps, siblings ~150–200px apart) so it's readable.
- **Condition note:** if a `Condition` leaf can be authored that still compiles (i.e. the generator tolerates a Condition node without a bound method), include one for completeness; if it forces a binding that won't compile on BrainBlackboard, **omit conditions** and leave a `Comment` on the showcase Root saying "Condition leaves deferred — VE-DEBT-002." Determine this empirically by building.

### 2. Trivial focused set — `Assets/BTrees/Authoring/`
Each is the **minimum** tree isolating one feature; each MUST compile + round-trip. Suggested set (adjust names only if a generator constraint forces it):
- `T01_Sequence.btree.json` — `Root → Sequence → [Wait(1s), Wait(2s)]`.
- `T02_Selector.btree.json` — `Root → Selector → [Wait(1s), Wait(1s)]`.
- `T03_Parallel.btree.json` — `Root → Parallel → [Wait(1s), Wait(1s)]`.
- `T04_DecoratorRepeater.btree.json` — `Root → Sequence → Action(Action_Wander)`, with a single `Repeater` pill (IntParam=3) on the Action.
- `T05_DecoratorStack.btree.json` — `Root → Sequence → Action(Action_Wander)`, with a 2-pill stack: `Inverter` (StackIndex 0) + `Cooldown` (FloatParam=2, StackIndex 1).
- `T06_ObserverSelector.btree.json` — `Root → ObserverSelector → [Action(Action_Wander), Wait(1s)]`.
- `T07_Subtree.btree.json` — `Root → Sequence → Subtree(SampleScout)`.
- `T08_ActionLeaf.btree.json` — `Root → Action(Action_Wander)` (bound-action smoke test).

(If the `Condition` leaf turns out to compile unbound, add `T09_Condition.btree.json` — `Root → Sequence → Condition`; otherwise skip it and note the deferral in DEC-PLAN's DEC-01 row.)

## Verification (run + paste RAW output)

1. **Compile gate:** `dotnet build` the `Hrot.AI.Behaviors` project (this runs `BTreeJsonGenerator` over every `.btree.json`; a build error = an asset that doesn't compile). Report 0 errors, and confirm the generator emitted code for each new asset (look for generated `*.g.cs` or build log mentions; if you can't see them, the clean build passing is sufficient).
2. **Round-trip + no regressions:** `dotnet test` for `Hrot.BTree.Editor.Tests` and `Hrot.AiEditor.Persistence.Tests`. Report pass/fail counts. **Watch for any test that enumerates/counts assets** (e.g. asserts "exactly two assets") — adding assets may trip it; report it explicitly, do NOT delete or weaken such a test (lead decides). Known pre-existing failures (NOT yours, do not chase): 2 pretty-print round-trip failures in Generators.Tests; 7 Blueprints failures.
3. List every file you created.

## Report back
For each asset: did it compile? Did conditions compile unbound (the T06/T09 question)? Any asset-count test that tripped? Diff/file list + raw build + raw test output. **Do NOT commit** — lead reviews & commits.
