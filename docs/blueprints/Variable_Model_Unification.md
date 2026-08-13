# Variable model unification — the vision, and how it maps to today

> **Coordinator, 2026-08-13**, at the user's request. ⭐ **The question is not *whether* to unify —
> that is ruled — but *how* to do it without breaking things.** Everything below is verified against
> code; the one speculative item is marked.
>
> 📌 **Input to Q28.** ⛔ **Not a Batch 38 change** — Batch 38 finishes `BP-57` and only needs the
> one instruction in §5.

![Blueprint variables — four bespoke lists, two orthogonal axes](diagrams/variable_model_unification.svg)

---

## 1. The whole idea in one line

⭐ **Four bespoke lists are two orthogonal tags: `Role` ∈ {`Input`, `State`} × `Scope` ∈ {`Asset`, `Graph`}.**

⚠ **The model unifies; the emission does not** — the memory genuinely lives in two places (the BTree
parameter blob vs. blackboard), so `Params` and `State`/`WorkingState` stay separate structs. What goes
away is the *model* pretending they are unrelated.

---

## 2. The mapping — today → unified

| today | Role | Scope | emits to | dispatch |
|---|---|---|---|---|
| `BlueprintAsset.Parameters` | `Input` | `Asset` | `struct Params` ← `bb.BehaviorParameters[paramIndex]` | AiPrimitive |
| `BlueprintAsset.WorkingState` | `State` | `Asset` | `struct WorkingState` @ `Blackboard1024`+8 · `__phase` | AiPrimitive |
| `BlueprintAsset.Variables` | `State` | `Asset` | `struct State` · `BlueprintLatentCursor Cursor` | Instance |
| `Graph.LocalVariables` | `State` | `Graph` | C# local · blackboard slot when it can suspend | any (⛔ not `Macro`) |
| *(`Graph.Inputs`)* | *`Input`* | *`Graph`* | *method parameters* | *any — optional, §6* |

⇒ ⭐ **`WorkingState` and `Variables` occupy the SAME cell.** Both are private, persistent, per-entity
storage carrying the suspension bookkeeping (`Cursor` / `__phase`) plus the designer's fields. **Two
names, one concept**, held apart only by a diagnostic.

### ⭐ `Role` is not a new abstraction — it already ships

```csharp
// Hrot.AiEditor.Persistence/BlackboardVariableEnums.cs — used by BTree/HSM today
public enum BlackboardVariableRole { Input = 0, State = 1 }
public enum WorkingStateScope      { Node = 0, Behavior = 1, Entity = 2 }
```

⇒ **This is not designing a model. It is Blueprints keeping a bespoke three-list shape while the
shared side already carries role + scope.**

---

## 3. What it does to the rails

| | fate |
|---|---|
| ⛔ **`BP1024`** *"AiPrimitive uses parameters and workingState, not variables"* | ⭐ **disappears** — it only ever said *"call it `WorkingState`"* |
| ⚠ **`BP1031`** *"Instance uses variables, not parameters/workingState"* | **splits**: the `WorkingState` half vanishes; the `Parameters` half becomes *"Instance has no `Input` channel"* |
| ✅ **`BP1011`** *"Library must not declare member variables"* | **survives, better stated** — a Library compiles to static methods with **no per-entity storage at all**. ⭐ A capability boundary, not a naming rule |

📌 **Library keeps locals.** `LibraryEmitter.EmitGraphBody` already emits them (`:127`), and `BP1101`
forbids latent nodes in a Library ⇒ it can never suspend ⇒ its locals are **always** plain C# locals.
**No exception, and no hole in `Q27-A3`.**

### ⭐ And `BP-226` dissolves instead of being patched

[The finding](FINDING_Variable_Index_Space.md) asked for `FindVariableIndex` to return a
`(storage-kind, index)` pair so the ambiguity becomes unexpressible. ⇒ **With the tags on the
declaration, the kind is what the declaration SAYS.** The pair falls out; it is not bolted on.

---

## 4. How to do it safely — four stages, risk ascending

⭐ **The ordering principle: everything that does not touch the asset format lands first**, so by the
time the migration runs, the machinery is already unified and test-locked and the migration is the
**only** variable.

| | stage | touches | revert |
|---|---|---|---|
| **A** | `Variables` becomes a third `IVariablesSchemaSource`; ⚠ **kill the `bool isParams`** for a three-way kind | editor only | ✅ trivial |
| **B** | My Blueprint's Variables section projects **that** source instead of its own | editor only | ✅ |
| **C** | `FindVariableIndex` → `(kind, index)`; `VarFieldName` switches on it — ⭐ **closes `BP-226`** | compiler only | ✅ type change, not logic |
| **D** | one declaration list with `Role`/`Scope`; `BP1024`/`BP1031` restated | model + ⚠ **JSON migration** | ⚠ the only risky stage |

### The machinery to reuse — it exists and is already multi-consumer

```
Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs
    interface IVariablesSchemaSource
        Variables · AddVariable · RemoveVariable(s) · RenameVariable · MoveVariable
        GetRefactorKey · CountNodesReferencingVariable
        UpdateVariableRole · UpdateVariableScope · aliasing
```

| | |
|---|---|
| ✅ **Already three implementations** | `BTreeHsmSchemaSource` (shared with BTree/HSM), `BlueprintVariableSchemaSource`, test mocks |
| ✅ **Blueprint already plugs in** | `Parameters` **and** `WorkingState` flow through it today |
| ⛔ **`Variables` does NOT** | it has a separate path via `BlueprintMyBlueprintModel` → `MyBlueprintItem`. ⭐ **That is the parallel implementation to remove** |
| ⚠ **`bool isParams`** | a **two-way flag for a three-way choice** — ⭐ **`BP-224`'s exact shape**, which already has a row. Fix it in stage A |
| ⭐ **`CountNodesReferencingVariable`** | **exactly** what Batch 38 §3.3's delete-while-referenced needs. **Reuse; do not write a second one** |

⇒ ⭐ **The direction is to make `Variables` a third schema source — NOT to teach My Blueprint about
`WorkingState`.** The shared control is the richer, already-shared machinery; the My Blueprint path is
the bespoke one.

---

## 5. 📌 The one thing Batch 38 must do differently

⚠ As dispatched, Batch 38's **Local Variables** section is a **third** implementation — precisely what
this document exists to prevent. ⇒ **One instruction:**

> ⭐ **Implement the locals source as an `IVariablesSchemaSource`**, and have the My Blueprint section
> project it. **Same UI as ruled** — a canvas-following section with `[+]` — but **stage B absorbs it
> for free** instead of stage B having to undo it.

---

## 6. 📐 Open, for the architect

| | |
|---|---|
| **Is `Graph.Inputs` in or out?** | Conceptually it is `(Input, Graph)` and the cell is already populated. ⚠ But it is a different decl type (`ParameterDecl`) and function parameters are genuinely passed-not-stored. ⚖️ **Lean: leave it out** — lower payoff, real risk |
| **Does `Scope` need more than two values?** | The BTree side has `Node`/`Behavior`/`Entity`. ⚖️ **Lean: `Asset`/`Graph` only** until something needs more |
| **Does `Instance` get an `Input` channel?** | ⚠ `IsExposedOnSpawn`/`IsEditable` exist on `VariableDecl` and have **no compiler-side reader** — editor-only flags today. Unification makes the gap explicit; **filling it is a separate feature** |
| **Where does stage D's migration run?** | every `.bp.json` round-trips through `BlueprintJsonServices`; a reader that accepts both shapes and a writer that emits the new one is the usual answer. **Not designed here** |
