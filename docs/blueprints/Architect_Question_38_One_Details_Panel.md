<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: the REVISION 2026-08-18 section at the bottom - it supersedes the
  2026-08-17 inventory (8 surfaces; the graph finds 25) and corrects section 4's
  claim that the shell is missing. The sub-questions A-E stand; F is new.
stale-below: nothing, but section 1's inventory table and section 4's last line
  are SUPERSEDED by R1 and R2. Do not quote them.
known-rot: section 4 says "what is missing is the SHELL" - measured false,
  RuntimeInspectorWindow is that shell with three panes registered.
-->
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

---

# ⭐⭐⭐ REVISION `2026-08-18` — **the inventory was too small, and the SHELL already exists**

> ⭐⭐ **User, `2026-08-18`:** *"what are the other existing Variable panels doing, also the Inspector
> and Runtime inspector? i need to reduce and unify these as much as possible, best to integrate them
> into the focus/entity context chameleon detail panel. but some might still be useful if stays as
> standalone window (or even multi instance windows) shown independently, pinned to a concrete entity,
> graph or whatever denotes their content."*
>
> ⇒ ⭐ **Same question as `2026-08-17`, plus ONE NEW IDEA — the PIN.** See `Q38-F`.

## R1. 📐 INVENTORY — **re-run on the GRAPH, `2026-08-18`** *(`R-74`)*

```
search_graph(label="Class", name_pattern=".*(Inspector|Details|Variables|Watch|Blackboard).*(Window|Panel|Pane)$")
                                                                                     → total 25
```

⚠⚠ **`2026-08-17`'s table listed EIGHT.** ⛔ **The graph finds 25** — ⭐ **the same shape as `R-72`**
*(two watch windows… then four)*.

### ⭐ The AI/Blueprint editor family — **the ones this question is about**

| # | surface | lines | what it answers |
|---|---|---|---|
| 1 | **`InspectorWindow`** *(`AiShared`)* | **678** | node facets · **`DEFAULT VALUE — {var}`** · subtree param sync · utility considerations |
| 2 | ⚠ **`InspectorWindow`** *(`Hrot.Blueprints.Editor`)* | **70** | ⛔⛔ **A SECOND ONE.** 📌 `BP-317` named it; `2026-08-17`'s table did not |
| 3 | **`RuntimeInspectorWindow`** *(`AiShared`)* | **57** | ⭐⭐⭐ **a SHELL — see `R2`** |
| 4–6 | **`BTreeRuntimeInspectorPane`** · **`HsmRuntimeInspectorPane`** · **`BlueprintRuntimeInspectorPane`** | 68 · 93 · **190** | ⭐ **per-host FEEDS behind that shell** |
| 7 | **`BlueprintDetailsWindow`** | **304** | the chameleon — ⛔ Blueprint only, `sealed` *(`BP-317`)* |
| 8 | **`BlackboardAuthoringWindow`** | **462** | hosts `VariablesPanelControl` — ⭐ **bin-packing, byte budget, DTO warnings** |
| 9 | **`BlueprintVariablesManagedWindow`** | 33 | hosts `VariablesPanelControl` **again** |
| 10 | **`BlueprintVariablesWindow`** | 82 | ⚠ **a THIRD variables surface** — 📌 `U-16`/row 60 retires it |
| 11 | **`AiVariablesWindow`** | 120 | the standalone Variables table, AI side |
| 12–13 | **`AiWatchWindow`** · **`WatchPanelWindow`** | 107 · 130 | 📌 `R-72` — **two watch windows** |
| 14 | 🔴 **`LiveBlackboardPanel`** *(`BTree.Editor`)* | 120 | ⛔⛔ **in-degree 0 — NOTHING HOSTS IT.** ⭐ **Built deliberately** *(`.dev/_DONE/blueprints-2` `TASK-BT-S2-03/05`: "decode them to display actual runtime values")* ⇒ ⚠ **a surface that LOST its host**, not a stub |
| 15 | **`DetailsPanel`** *(`NodeEditor.UI`)* | 151 | the generic panel primitive |
| 16 | **`GraphSignatureWindow`** | — | 📌 `BP-128` folds it |

### ⛔ NOT this question — **engine / sim, different lifecycle**

`EntityInspectorPanel` ×2 *(`Fdp.Presentation`, `Hrot.IG`)* · `DerEntityInspectorPanel` ·
`EntityWatchPanel` · `FdpEntityInspectorWindow` ×2 · `FdpEntityWatchWindow` · `InspectorPanel`
*(`ExCon`)* · `FakeAnimBackendInspectorWindow` · `FakeNavigationInspectorWindow`.
⚠ **Named so a later sweep does not "discover" them and widen the scope.**

## R2. ⭐⭐⭐ THE CORRECTION — **§4 said *"what is missing is the SHELL."* It is NOT missing**

📐 **Measured, `RuntimeInspectorWindow`:**

```csharp
private readonly List<IRuntimeInspectorPane> _panes = new();
public void RegisterPane(IRuntimeInspectorPane pane) => _panes.Add(pane);
// "…the registered IRuntimeInspectorPane for the active asset kind."
```

⇒ ⭐⭐⭐ **A shell that picks a per-host feed by the active asset kind ALREADY SHIPS, with three feeds
registered.** ⛔ **It is scoped to the RUNTIME family — that is its only limitation.**

⇒ ⭐⭐ **`Q38` is therefore NOT "build a shell." It is *"generalise the shell that exists, and move the
other feeds onto it."*** ⚠ **That is a much smaller question than the `2026-08-17` framing implied**,
and it strengthens `Q38-D`: ⭐ **runtime-vs-authoring was never two ARCHITECTURES — it is two feeds.**

## R3. ⭐⭐ `Q38-F` *(NEW)* — **the PIN: one chameleon, N frozen inspectors**

⭐ **The user's model:** ⭐⭐ **Details = ONE, follows focus** · ⭐⭐ **Inspector = N, each FROZEN to a
context** *("pinned to a concrete entity, graph or whatever denotes their content")*.

⭐⭐⭐ **The pin payload already exists — Batch 87 built it yesterday:**
`SelectionOrigin` · `EditorSelectionStore.FocusedSurface` · `ActiveSubSelectionOrigin`, plus the
selection and the active asset. 📌 **`R-95`** made that tuple **cross-host by user ruling.**

⇒ ⭐⭐ **"Pin" = capture the tuple the Details panel is currently obeying, and hand it to a new window
that never updates it.** ⛔ **No new context model** — ⭐ the frozen window reads the SAME feeds.

| ⭐ why this resolves `Q38-A` too | |
|---|---|
| **contextual vs mode-toolbar** | ⭐⭐ **BOTH, and they stop competing:** the **live** panel is contextual *(focus decides)*; a **pinned** one is a mode *(the user froze it)* ⇒ ⛔ **the toolbar is not a mode switch, it is a PIN button** |

## R4. ⭐⭐⭐ RECOMMENDED DISPOSITION — **per surface** *(user approves; I do not build)*

| verdict | surfaces | why |
|---|---|---|
| ⭐⭐ **FOLD into the chameleon** | `BlueprintDetailsWindow` *(as the Blueprint feed)* · the three **RuntimeInspectorPanes** · `InspectorWindow` ×2 · `GraphSignatureWindow` *(`BP-128`)* | ⭐ all answer *"tell me about the selected thing"*; ⭐⭐ **the pane seam already exists** |
| ⭐⭐ **KEEP, but as a PINNABLE instance** | `AiWatchWindow` / `WatchPanelWindow` *(collapse to ONE first — `R-72`)* · a pinned **Details** | ⭐ **a watch is BY DEFINITION not focus-following** — ⛔ folding it would destroy its job |
| ⭐ **KEEP STANDALONE — a genuinely different job** | `BlackboardAuthoringWindow`'s **byte-budget / bin-pack** view | 📌 **`Q38-C`** already says so, and **`R-15`/"no rush removals"** binds |
| ⛔ **RETIRE** | `BlueprintVariablesWindow` *(row 60, `U-16`)* · `BlueprintVariablesManagedWindow` *(duplicate host)* · one of the two `InspectorWindow`s | 📌 **ruling 9** |
| ⚠ **DECIDE — do not assume** | 🔴 **`LiveBlackboardPanel`** | ⛔ **in-degree 0 and deliberately built.** ⭐ **Superseded by the Details Value column** ⇒ *probably* retire — ⛔ **but `CLAUDE.md`'s rule says say so explicitly, not silently** |

## R5. ⚠ SEQUENCING — **`R-27` still binds, and its condition is ALMOST met**

📌 **`R-27`: *"`Q38` must NOT be built until Track C is wired AND visually checked."***

| | |
|---|---|
| ✅ **wired** | Batch 87 — the binder reaches **all four** table hosts, the modal draws, selection renders, the panel obeys the surface |
| ⚠ **visually checked** | **ONE round done**, its three defects fixed — ⛔ **the RE-CHECK has not run**, and `88b` will add the BTree/HSM host that the check must cover |

⇒ ⭐⭐⭐ **RECOMMENDED: do NOT schedule `Q38` until the post-`88` visual check passes.** ⭐ **Answer it
now, build it after.** ⚠ **The `2026-08-17` warning stands and has now been PAID FOR ONCE:**
⛔ *"merging surfaces before anyone has SEEN them is how the wiring gap happened."*
