# Unified variable editing UI — the vision

> **Coordinator, 2026-08-13.** Companion to
> [Variable_Model_Unification.md](Variable_Model_Unification.md), which covers the *model*. This one
> covers **what the designer sees**. ⭐ **The user's proposal — table as the details view of the
> Variables section, selection following the click — is endorsed**; this records it, the reasons, and
> what has to change.
>
> 📌 **Input to Q28.** ⛔ **Not an implementation task yet** — see the banner below.

> ✅ **REVIEWED — [Batch 38](REVIEW_Unified_Variable_Design.md), `2026-08-13`.** ⭐ **Updated to match.**
>
> ⛔⛔ **Two claims here were WRONG, and both were load-bearing for this document:**
> 🔴 **`BP-230`** — the shared table's `Role`/`Scope` editors and its reference counter are **stubs**
> on the Blueprint side ⇒ **stage B as written ships a picture, not an editor** ·
> 🔴 **`BP-228`** — a struct type id is **unvalidated pass-through**, so **§4's answer was wrong and
> `B′` is blocked.** ⚠ **Corrections are inline; §7 Q-h is rewritten.**

![Unified variable editing](diagrams/variable_editing_ui.svg)

---

## 1. The shape — one navigator, one editor, two ways in

| | role | what it is today |
|---|---|---|
| **My Blueprint** | ⭐ **navigate and create** | `BlueprintMyBlueprintModel` sections + `VariableCreateModal` |
| **Details** | ⭐ **review and tune** | ⛔ **not wired to variables at all** — the table lives in a separate window |

⛔ **Nothing is authored in two places.** The modal *creates*; the table *edits*. That division is what
keeps this from becoming a third parallel implementation.

---

## 2. Why both, rather than one

⚠ **They are not duplicates.** Each does something the other structurally cannot:

| only the **table** | only the **modal** |
|---|---|
| the whole set at once | one guided decision, nothing else on screen |
| **byte budget** per row and in total | name validated *before* the variable exists |
| reorder (`MoveVariable`) | **capacity / initial length** for collections |
| ⛔ ~~flip `Role` / `Scope` in place~~ — 🔴 **empty method bodies** (`BP-230`) | no chance to edit the wrong row |
| ⛔ ~~reference count before deleting~~ — 🔴 **`CountNodesReferencingVariable` returns a hardcoded `0`** (`BP-230`). **Must be implemented first** | |
| unused marking, alias bindings | |

⇒ ⭐ **Replacing the modal with the table would lose guided creation; replacing the table with the
modal would lose the byte budget and the whole-set view.** Keep both, on one model.

---

## 3. The selection flow — the user's proposal

```
click a variable in My Blueprint
        └─▶ Details panel shows the variables TABLE
                └─▶ with the selection moved to the clicked row
```

⭐ **Why this is the right shape:** it matches the convention the canvas already uses — `INodeModel.Kind`
is documented as *"used by catalog lookups and **Details panel routing**"*. ⇒ **Selection routes to
Details** is an established rule here, not a new one; My Blueprint is simply a second selection source.

⚠ **What it costs:** `IMyBlueprintModel` is a **read-only projection** — `Sections`, `GetItems`,
`Changed`, and **no selection or activation event**. ⇒ the click-to-details wiring lives in
`BlueprintMyBlueprintWindow`, not the model. 📐 **Whether the panel already surfaces selection to its
host is the one thing to check first** — if not, that is a small `NodeEditor.Core` addition and it
moves two gates (NodeEdit Core, UI).

⭐ **The section stays the navigator even so.** A designer scanning for *"where is `Threat` declared"*
wants the tree, not a table — and locals must stay grouped under the graph that owns them.

---

## 4. ⭐ Struct-typed variables — already discovered, only the projection is forked

**Requirement:** a hardcoded struct must be usable as a variable, exactly as in WorkingState.

✅ **The discovery rule is already shared.** One marker attribute, `[BlackboardDtoStruct]`, is scanned
by **both** sides:

| consumer | projects to | why that shape |
|---|---|---|
| `BlackboardTypeChoiceBuilder` *(AiShared)* | `VariableTypeChoice(Display, Type)` — **CLR `Type`** | the table needs it for **byte sizing** |
| `ISharedStructTypeProvider` *(Blueprints)* | **FQN `string`** | the compiler resolves types by `TypeId` |
| `BlackboardFieldClassifier` | the predicate itself | one definition of "is a shared struct" |

⇒ ⭐ **The unified choice record carries `(Display, TypeId, Type?)`.** Both projections are needed and
neither is wrong; today they are simply built twice from one rule.

### ⚠ But the two *pickers* were forked on purpose, and that is the hard part

| | |
|---|---|
| `BlackboardTypeHelper.DefaultKnownTypeNames` | shared by blueprints, BTree and HSM · primitives **+ structs** |
| `BlueprintTypeChoices` | blueprint-local · a projection of `StaticTypeRegistry.EditorOfferableTypeIds` · **17 types, no structs** |

⭐ **`BP-87` split them deliberately**, and the reason is recorded in the file: widening the shared list
to fix a blueprint problem *"would change the BTree and HSM blackboard pickers too."* ⚠ And `BP-87`'s
durable half is that the blueprint list is a projection of the **compiler's own registry**, so every
offered type is guaranteed resolvable — a property a merged list must not lose.

⇒ ⛔ **Do not merge the two lists.** ⭐ **Merge the *shape*:** one choice record, two contributors —
`StaticTypeRegistry.EditorOfferableTypeIds` for primitives, `[BlackboardDtoStruct]` discovery for
structs.

### ⛔⛔ CORRECTED `2026-08-13` — **`B′` IS BLOCKED. There is nothing to validate against**

> This section originally ended *"extend the resolvability lock to cover the struct half."*
> 🔴 **`BP-228`: that lock cannot be written, because the compiler validates nothing.**
>
> ```
> 'Hrot.AI.Behaviors.StructDemoData'   SUCCEEDED=True  DIAGS=[]
> 'Hrot.AI.Behaviors.NoSuchStructAtAll' SUCCEEDED=True DIAGS=[]
> 'Totally.Made.Up.Type'                SUCCEEDED=True DIAGS=[]  → public global::Totally.Made.Up.Type Threat;
> 'a.b'                                 SUCCEEDED=True DIAGS=[]  → public global::a.b Threat;
> ```
>
> ⭐ **The rule is purely syntactic: contains a dot ⇒ trusted verbatim; no dot ⇒ `BP1500`.**
> ⇒ ⛔ **`BP-87`'s *"every offered type is guaranteed resolvable"* has nothing to check against**, and
> the *"assert end-to-end compilation"* lock this document asked for **would pass on a fabricated
> type** — compilation succeeds. Only Roslyn catches it, as `CS0246` in generated code **naming no
> variable**: ⭐ **the `__var_-1` shape again.**
>
> ⇒ 📐 **`B′` needs a COMPILER-SIDE RAIL before it needs a picker.** *What validates a struct type id*
> — a registry entry, a generator-side Roslyn check, or an allow-list from `[BlackboardDtoStruct]`
> discovery — **is the first thing to decide** (review §7).

---

## 5. 📌 The one thing [Batch 39](HANDOFF_Batch39_Finish_Local_Variables.md) must do differently

⚠ As drafted, its Local Variables section is a **third** implementation. ⇒ **One instruction:**

> ⭐ **Implement the locals source as an `IVariablesSchemaSource`**, and have the My Blueprint section
> project it. **Same UI as ruled** — canvas-following section, always present, `[+]` where applicable
> — but it lands *inside* the unified path instead of beside it.

⛔⛔ **CORRECTED `2026-08-13`:** this originally said it *"gets `CountNodesReferencingVariable` for
free."* 🔴 **`BP-230` — it returns a hardcoded `0`**, so delete-while-referenced would report *"0
references"* for every variable **and delete anyway.** ⇒ **it must be implemented, not reused.**

---

## 6. Where this sits in the staged plan

⚠ **Re-ordered by the review — [see the model doc §4](Variable_Model_Unification.md).**

| stage | what the designer notices |
|---|---|
| **0** · **C** — compiler-only, `C` first | nothing |
| **A** — `Variables` becomes a third `IVariablesSchemaSource` | nothing yet |
| **B** — Details shows the table; My Blueprint routes selection into it | ⭐ **this document's picture** ⚠ **inert until `BP-230`** |
| **B′** — the type-choice union | ⛔ **BLOCKED on `BP-228`** |
| **D1…D4** — the model, the migration, the consumers, the rails | `Role`/`Scope` become authoritative |

⭐ **The UI still lands before the model change** — the table's columns exist, so the unified *view* can
ship while the model still has four lists behind an adapter. ⚠⚠ **But "the view" is the honest word:
until `BP-230` is fixed those columns edit nothing, and `R3` means a blueprint `Scope` may not be
expressible through the shared contract at all.** **Fix `BP-230` inside B, or ship B as read-only and
say so.**

---

## 7. ✅ ANSWERED — architect ruling, `2026-08-13`

> ⚠ **Provenance: Claude ruling, delegated by the user** — same standing as `Q27-D`.
> ⛔ **NotebookLM was not consulted.** ⭐ **Both measurable questions were measured.**

### Q-e · Does the Details panel host non-node content? → ⭐⭐ **It is already designed for exactly this**

`NodeEditor.Core/Interfaces/IDetailsViewProvider.cs` — `DetailsTarget` is an open hierarchy:

```csharp
SingleNode · MultipleNodes · Comment · Asset · Function · Macro · CustomEvent · EventDispatcher
Variable(string VariableId)
LocalVariable(string FunctionId, string LocalId)     // ⭐ already there
```

⭐⭐ **`LocalVariable` is keyed by `(FunctionId, LocalId)`** — the contract already models a local as
*belonging to a graph*, which is precisely the canvas-following section §3 proposes. **The routing key
does not need inventing; it was designed in.**

⚠ **What is actually missing:** ⛔ **no Blueprint type implements `IDetailsViewProvider` at all.** The
only implementations in the repo are NodeEdit's own `DemoNodeDetailsProvider`, `DetailsPanel` and
`DetailsViewRegistry`. ⇒ ⭐ **The work is "register a provider", not "extend the contract"** — and none
of it moves the two NodeEdit gates.

### Q-f · One table, or one per scope? → ⭐ **One**, with `Scope` as a column

And the reason is now stronger than taste: ⭐ **`DetailsTarget` already distinguishes `Variable` from
`LocalVariable`, so the *selection* is scope-aware before the table sees it.** The table does not need
splitting to know what was clicked — it needs a column and grouping. ⛔ A table per scope would
re-create exactly the panel sprawl this work exists to remove.

### Q-g · Does the modal gain `Role`/`Scope`? → ✅ **Yes, defaulted, and shown only where meaningful**

⭐ **The precedent already exists in the shared control:**
`VariableViewModel.ShowScopeSelector => Role == BlackboardVariableRole.State` — Scope is **hidden for
Inputs** because it has no meaning there. **Mirror that rule in the modal** rather than inventing a
second one. Creating an `Input` must not require creating a `State` and flipping it afterwards.

### Q-h · Does a struct-typed blueprint variable compile? → ⛔⛔ **RULING OVERTURNED `2026-08-13`**

> **The answer was "yes, and it already works." That was half right, and the wrong half is a blocker.**
> 🔴 **`BP-228`.** It compiles — but so does **`Totally.Made.Up.Type`**, and so does **`a.b`**, both
> with **zero diagnostics**. ⭐ **The rule is syntactic — contains a dot ⇒ emitted verbatim as
> `global::{whatever}`.** There is no resolution and therefore nothing to lock against.
>
> | what I said | what is true |
> |---|---|
> | *"resolved by fully-qualified name"* | ⛔ **not resolved at all** — `TryResolve` is `False` even for the FQN that works |
> | *"the fix is a list union and nothing else"* | ⛔ **the union offers types nothing validates** |
> | *"extend the lock to assert end-to-end compilation"* | ⛔ **that lock passes on a fabricated type** |
>
> ⇒ ⭐ **Revised ruling: `B′` is blocked until a compiler-side rail exists.** The union remains the
> right *shape*; it cannot ship without something to validate against. **What that validator is —
> registry entry · generator-side Roslyn check · allow-list from `[BlackboardDtoStruct]` discovery —
> is an open decision, and it is the first one** (review §7).

---

## 8. 📌 Standing gap — the visual check

⚠ The review could not establish whether the table's `Role`/`Scope` columns are **drawn-but-dead or
hidden** for a blueprint asset: *"it needs the UI on screen, and there is no ImGui in this container."*
⛔ **That check has now not been done for FIVE batches.** It is a programme-level gap, not one
batch's — and this document's central picture is the kind of thing it would catch.
