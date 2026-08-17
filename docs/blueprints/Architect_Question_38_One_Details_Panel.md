# Architect Question #38 — **should the inspect/detail windows merge into ONE mode-switching Details panel?**

> ⛔ **OPEN POINT — recorded `2026-08-17`, NOT scheduled.** ⭐ **A separate design task by user
> instruction**, banked so the idea is not lost and the next session does not re-derive the inventory.
>
> ⭐⭐ **User, verbatim:** *"we have too many specialized windows like Detail, Inspector,
> VariablePanelControl, Runtime Inspectors - i think they all should somehow merge into a single Detail
> panel which could switch its mode (using some local toolbar there or something) - this is a separate
> design task."*
>
> ⭐ **And a companion ruling the same day:** *"ad `VariablesPanelControl` - **keep for now**, but we need
> to rethink it later - find a way how to integrate it."* ⇒ ⛔ **Batch 79 is ADDITIVE. Nothing retires.**

---

## 1. 📐 The inventory — measured `2026-08-17`

⭐ **50 `ManagedWindow` subclasses in the editor.** ⚠ **The inspect/detail family alone:**

| window | shows | perspective |
|---|---|---|
| **`InspectorWindow`** | the selected node's facets · ⭐ **the `DEFAULT VALUE — {var}` section** | AI *(BTree/HSM)* |
| **`RuntimeInspectorWindow`** | live runtime state | AI |
| **`BlueprintDetailsWindow`** | the selected element's properties | Blueprint |
| **`BlackboardAuthoringWindow`** | ⭐ hosts **`VariablesPanelControl`** — bin-packing, byte budgets, sub-tree DTO warnings | AI |
| **`BlueprintVariablesManagedWindow`** | hosts `VariablesPanelControl` again, blueprint-side | Blueprint |
| **`GraphSignatureWindow`** | graph Inputs/Outputs · exec signature for Macros | Blueprint |
| **`FdpEntityInspectorWindow`** · **`IgEntityPropertiesWindow`** | ECS entity properties | sim/ExCon |
| ⛔ **and, unwired** | `VariableTableControl` — the Track C generic row list | *(nothing hosts it)* |

⇒ ⭐⭐ **At least SIX surfaces answer "tell me about the thing I selected"**, split by asset type and by
perspective rather than by what the user is doing.

---

## 2. ⭐⭐⭐ There is already a precedent row, and it is the same idea narrowed

> 📄 **`BP-128`** *(OPEN, `RW-M`)* — **"Fold `Graph Signature` into a context-sensitive `Details`."**
> ⭐ **User, then:** *"i do not understand why we are setting inputs and outputs in graph signature. Way
> more intuitive would be to set Detail on Event node (inputs) and Details on Return node (outputs)…
> The whole Graph Signature seems redundant."* ⭐⭐ **And: "this is exactly how Unreal works."**

⇒ ⭐⭐⭐ **`Q38` is `BP-128` generalised from one window to the family.** ⛔ **They must not be designed
separately** — `BP-128`'s answer *(selection drives the panel; the specialised window retires)* **is the
mechanism `Q38` would apply six more times.**

---

## 3. ⭐ The sub-questions, stated but NOT answered

| | question | ⚠ what makes it hard |
|---|---|---|
| **`Q38-A`** | **is the switch CONTEXTUAL (selection-driven) or a MODE (a local toolbar)?** | ⭐ `BP-128` and Unreal say **contextual**; ⚠ **the user's phrasing says a local toolbar** ⇒ **possibly both**: context picks the default, the toolbar overrides it |
| **`Q38-B`** | **does one panel span PERSPECTIVES**, or one per perspective with a shared control? | ⚠ `MyBlueprintPanel` is already perspective-agnostic *(`NodeEditor.UI` + `NodeEditor.Core`)* ⇒ **the precedent exists** |
| **`Q38-C`** | **what happens to the specialised VIEWS that are not "properties"?** | ⭐⭐ **`VariablesPanelControl`'s bin-packing/byte-budget view is a genuinely different job** — ⛔ *"no rush removals"*: it is a **duplicate surface only if the merged panel absorbs the LAYOUT view too** |
| **`Q38-D`** | **runtime vs authoring — one panel or two?** | ⚠ `RuntimeInspectorWindow` exists because run state changes what is shown. ⭐ **Track C already ruled that run state governs WRITABILITY, not WHICH surface** — 📄 `DESIGN_Variable_Details_And_Editing.md` §5 ⇒ **that ruling argues for one** |
| **`Q38-E`** | **sequencing** | ⛔ **after Track C is wired and visually checked** — ⚠ **merging surfaces before anyone has SEEN them is how the wiring gap happened** |

---

## 4. ⭐ What is already true, and makes this cheaper than it looks

| | |
|---|---|
| ⭐⭐ **the row list is already generic** | `VariableTableControl` renders `IReadOnlyList<VariableRow>` and **knows nothing about its source** — ⭐ `SectionSource` and `PinnedSource` are two feeds of one control |
| ⭐⭐ **the outline is already host-agnostic** | `MyBlueprintPanel` in `NodeEditor.UI`, `IMyBlueprintModel` in `NodeEditor.Core`, with **blueprint and BTree/HSM models** |
| ⭐⭐ **the dialog is already one implementation, two entry points** | `DefaultValueAuthoring.OpenSession`, pinned by `ExactlyOneCallSite_OpensAVariableEditSession` |
| ⭐ **run state is already a cross-cutting input**, not a surface selector | §5 |

⇒ ⭐⭐⭐ **Three of the four pieces a merged panel needs already exist and are already shared.**
⚠ **What is missing is the SHELL** — the thing that decides which feed the panel shows.

---

## 5. Status

| | |
|---|---|
| **raised** | `2026-08-17`, **by the user**, while ruling on `VariablesPanelControl` |
| **state** | ⛔ **OPEN POINT — recorded, not scheduled.** ⭐ **A separate design task** |
| ⭐ **absorbs** | **`BP-128`** *(open)* — ⛔ **do not resolve `BP-128` on its own**; it is this question with one window |
| ⚠ **prerequisite** | ⭐ **Track C wired (Batch 79) and visually checked** — ⛔ **do not merge surfaces nobody has seen** |
| ⛔ **not affected** | Batch 79 stays **purely additive**: the BTree/HSM outline joins the perspective, **`VariablesPanelControl` stays** |
