<!--STATUS
state: LIVE
updated: 2026-08-20
current-answer: THE LIVE ANSWER, the block immediately below the title. Everything under
  "WORKING HISTORY" is the record of how it was reached and must NOT be quoted as the answer.
  The CONTEXT -> VIEWS TABLE below it is also LIVE and supersedes the bottom table's A/B/C
  lists; the bottom table's D/E/F sections (what stays out, what retires, the count) remain LIVE.
stale-below: every RECOMMENDED ANSWER that carries a struck-through line, the 2026-08-17
  inventory in section 1 (8 surfaces; the graph finds 25), and section 4's claim that the
  shell is missing. All superseded and marked in place.
known-rot: none as of 2026-08-20 - the six sub-questions are all ruled and each carries
  its ruling id.
known-conflict: none. R-112 corrects my own Q38-C test; R-113/R-114 settle the two
  surfaces that were left to decide.
-->
# Architect Question #38 — **should the inspect/detail windows merge into ONE mode-switching Details panel?**

# ✅✅✅ THE LIVE ANSWER — **all six sub-questions RULED** *(`2026-08-20`)*

> ⭐⭐ **Read only this block and the INTEGRATION TABLE at the bottom.** ⛔ Everything between them is
> **WORKING HISTORY** — how the answer was reached, including recommendations that were overruled.

## ⭐ The six

| | question | ⭐ the ruling | id |
|---|---|---|---|
| **A** | contextual, or a mode toolbar? | ⭐⭐ **The toolbar is a PANEL SWITCH, two stages.** The **context** decides which views are OFFERED and which is DEFAULT; the **user** picks among them with radio-style toggles. ⛔ It never changes what the panel is ABOUT, only which VIEW of one context is drawn. ⭐ **First goal is FEWER WINDOWS, not merged content** | **`R-98`** |
| **B** | one panel across perspectives, or one per? | ⭐ **In ALL perspectives, content PLUGGABLE.** The offer set is a function of **`(selection, perspective)`**. ⛔ **One shared instance is NOT required** — read-only views may be instanced freely; ⭐ **sharing is preferred for EDITING views**. Feeds registered at the composition root | **`R-110`** |
| **C** | what about views that are not "properties"? | ⭐⭐⭐ **The test is: IS IT ABOUT THE CURRENT SELECTION?** ⭐ **YES ⇒ a VIEW inside Details** on a toolbar toggle — ⛔ a different question earns its own **view**, not its own **window**. ⭐ **NO — a curated list kept open ACROSS selections ⇒ STANDALONE** *(Watch · Breakpoints)*. ⛔ A different **asset type** is a FEED difference and never justifies a surface | **`R-112`** |
| **D** | runtime vs authoring: one panel or two? | ⭐⭐ **The MODE is part of the CONTEXT** — it joins `(selection, perspective)` in deciding the offer set. ⭐⭐⭐ **A view is implemented ONCE, supporting multiple modes** — ⛔ never an authoring view plus a runtime twin | **`R-111`** |
| **E** | sequencing | ⭐ **ANSWER NOW, BUILD AFTER the visual check passes** — 📌 `R-27`, and *"merging surfaces before anyone has SEEN them is how the wiring gap happened"* | ✅ approved |
| **F** | the pin | ⭐⭐ **ONE WINDOW INSTANCE PER PIN, titled by its context.** Id keyed on `(view, asset, selection)`; an exact duplicate **focuses** rather than spawning; ⭐ **pins are VOLATILE.** Mechanically: the same class with a **frozen context source** | **`R-100`** |

## ⭐⭐ The shell — **it already exists**

📐 **`RuntimeInspectorWindow`** renders entity-lifecycle status, mode controls and a scrub bar, then
**delegates to the registered `IRuntimeInspectorPane` for the active asset kind.**
⇒ ⭐⭐⭐ **It IS the shell, and the pane registry the toolbar needs is already there.** ⛔ **Do not write
a third shell, and do not keep it as a second window beside Details.**
⚠ Its runtime chrome becomes **mode-conditional content** *(`R-111`)*.

## ⭐ Per surface — **the disposition**

| verdict | surfaces |
|---|---|
| ⭐⭐ **BECOME VIEWS** *(toolbar toggles in the right context)* | `InspectorWindow` *(AiShared — facets · default value · param sync · utility)* · the three **`RuntimeInspectorPane`s** *(one view, three feeds)* · `BlueprintDetailsWindow` · `AiVariablesWindow` *(the default view)* · **`BlackboardAuthoringWindow`'s byte-budget / bin-pack** *(`R-112` — ⛔ no longer standalone)* · `GraphSignatureWindow` *(`BP-128`)* |
| ⭐ **STAY STANDALONE** | ⭐⭐ **Watch** — `AiWatchWindow` survives *(`R-113`)*, variables-only, **persistable** · ⭐⭐ **Breakpoints** *(`Q44`)* · `DetailsPanel` *(NodeEditor.UI — the **primitive**, not a surface)* |
| ⛔ **RETIRE** | `InspectorWindow` *(Blueprints — the second class of that name)* · `BlueprintVariablesWindow` · `BlueprintVariablesManagedWindow` · **`WatchPanelWindow`** *(`R-113`)* · **`LiveBlackboardPanel`** *(`R-114` — ⭐ no feature the variable table lacks)* |

⚠⚠ **ONE CONSEQUENCE OF `R-98` + `R-113` MEETING:** `AiWatchWindow` draws **two** lists today —
breakpoint watches **and** pinned variables. ⭐ `R-98` says the Watch stays **variables-only** ⇒
⛔ **the breakpoint-watch list moves to the BREAKPOINTS window.**

## ⛔ What is NOT settled

| | |
|---|---|
| ⚠ **`R-27` still gates the BUILD** | the visual check must pass first — ⭐ **this is the only thing standing between here and a batch** |
| ⚠ **the `PARAMETER SYNCHRONIZATION` toggle** | ⭐ ruled a Details toggle in the NODE context *(`R-98`/`R-99`)*, ⛔ **sequenced AFTER the orchestrator wiring** — *"promoting an inert panel is worse than leaving it buried"* |

---

# ⭐⭐⭐ THE CONTEXT → VIEWS TABLE — **what each selection offers** *(`2026-08-20`)*

> ⭐⭐ **This supersedes the A/B/C lists in the INTEGRATION TABLE at the bottom** — same content,
> completed against the **measured** selection kinds and with the perspective/mode axes `R-110`/`R-111`
> add. ⛔ **The bottom table's D/E/F sections (what stays out · what retires · the count) are still live.**

## 📐 The selection kinds — **measured, not invented** *(`R-74`)*

`Selection/SubSelectionRecords.cs` — `BlueprintNodeSelection(GraphId, NodeId)` ·
`BTreeNodeSelection(VisualId)` · `BTreePillSelection(PillVisualId)` · `HsmStateSelection(StableId)` ·
`HsmTransitionSelection(VisualId)` · `HsmRegionSelection(StableId, RegionIndex)` ·
`UtilityConsiderationSelection(OptionIndex, ConsiderationIndex)`
`Hsm.Editor/Inspector/HsmSubSelections.cs` — `HsmEventSelection(EventId)` ·
`HsmGlobalTransitionSelection(VisualId)`
⭐ plus **`VariableOutlineSelection`** *(the My Blueprint outline's variable/section pick — a
different axis from the canvas sub-selection)* and ⭐ **`ActiveAsset`** with **no** sub-selection.

## ⭐⭐⭐ The table

⭐ **DEFAULT** = the view shown when the context becomes active. ⭐ **Availability may be narrowed by
MODE** *(`R-111`)* and by **perspective** *(`R-110`)* — the two right-hand columns say how.

| # | context *(what you clicked)* | views offered | ⭐ perspective | ⚠ mode |
|---|---|---|---|---|
| **1** | ⭐⭐ **FOCUS on a surface with NO sub-selection** *(a fresh document, or after an empty-canvas click — ✅ which CLEARS, `R-115`)* — 📌 **`R-115`: focus and selection are TWO axes** | ⭐ **the FOCUSED SURFACE'S DEFAULT view** — for the canvas/asset that is **Asset views** *(row 7)* | all | — |
| **1b** | ⚠ **a MULTI-selection** *(marquee, two or more nodes)* — ⭐ **a REAL selection** *(`R-115`)*, ⛔ **not "nothing"** | ⚠ **no multi-node view exists** ⇒ ⭐ the interim is *"N nodes selected"* + the focus default — ⛔ **never a silent empty** | all | — |
| **2** | ⭐⭐ **a VARIABLE or a variable SECTION** *(`VariableOutlineSelection`)* | ⭐ **Variables `(DEFAULT)`** · **Layout / byte budget** *(the bin-pack view — `R-112`)* | all three | ⭐ the table itself switches **initial ⇄ live** arm by mode *(`Q32` ruling 3)* — ⛔ not a different view |
| **3** | ⭐⭐ **a NODE** — `BlueprintNodeSelection` · `BTreeNodeSelection` · `HsmStateSelection` | ⭐ **Properties `(DEFAULT)`** *(the facet editor, and it CARRIES the node's two bindings — `ExpressionTargetField` / `WorkingStateTargetField`)* · **Default value** *(`DEFAULT VALUE — {var}`, the node-scoped default of the variable this node WRITES)* · **Runtime** | all three *(different feed per host)* | **Runtime** view offered **only** when a debug session is attached |
| **3a** | ⚠ **a SUBTREE node** *(a `BTreeNodeSelection` whose node is a subtree)* | row 3 **plus** ⭐ **Parameter sync** *(`PARAMETER SYNCHRONIZATION` — Approach B's copy-in/copy-out table)* | ⭐ **BTree** only — 📌 `M-24`: HSM cannot produce a subtree sync binding at all | ⛔ **sequenced AFTER the orchestrator wiring** *(`R-99`)* — *"promoting an inert panel is worse than leaving it buried"* |
| **3b** | ⚠ **a UTILITY node** *(or `UtilityConsiderationSelection`)* | row 3 **plus** ⭐ **Utility** *(`UTILITY CONSIDERATION`)* | BTree · HSM | — |
| **4** | **an HSM TRANSITION** *(`HsmTransitionSelection`, `HsmGlobalTransitionSelection`)* | ⭐ **Properties `(DEFAULT)`** — trigger/guard/priority | HSM only | — |
| **5** | **an HSM REGION** *(`HsmRegionSelection`)* · **an HSM EVENT** *(`HsmEventSelection`)* | ⭐ **Properties `(DEFAULT)`** | HSM only | — |
| **6** | **a BTREE PILL** *(`BTreePillSelection`)* | ⭐ **Properties `(DEFAULT)`** | BTree only | — |
| **7** | ⭐⭐ **the ASSET** — 📐 **`EditorSelectionStore.ActiveAsset`, whose own doc says *"the asset whose editor canvas has FOCUS"*, set by window-focus handlers.** ⛔ **Not** something picked in a browser; ⭐ **it is the open document you are looking at** | ⭐ **Asset settings `(DEFAULT)`** · **Layout / byte budget** · **Diagnostics** — ⭐ defined below | all three | — |
| **8** | **a GRAPH** *(a function / macro graph in the outline)* | ⭐ **Graph signature `(DEFAULT)`** *(`GraphSignatureWindow` — 📌 `BP-128`)* · **Variables** *(that graph's Local Variables)* | Blueprint only | — |

## ⭐⭐ WHAT THE "ASSET VIEWS" ARE — **concretely** *(row 7)*

| view | ⭐ what it shows | where it lives today |
|---|---|---|
| ⭐ **Asset settings** *(default)* | the asset-scoped switches — today the only measured one is **`Use editor-managed blackboard`** | `BlackboardAuthoringWindow` — ⛔ **genuinely mis-homed**: an asset-scoped switch inside a variables window |
| ⭐⭐ **Layout / byte budget** | ⭐ **does this blackboard FIT its tier, and how are its fields packed?** — the bin-pack picture, field offsets/sizes, and **DTO warnings** | `BlackboardAuthoringWindow`'s bin-pack view |
| ⭐ **Diagnostics** | **sub-tree allocations** *(`GetAutoAllocatedVariables` — ⚠ display-only today, `M-23`)* + **unbound requirements** *(`UnboundRequirementViewModel`)* | `VariablesPanelControl`'s host |

⇒ ⭐ **All three are about the ASSET AS A WHOLE**, ⛔ not about anything you clicked inside it — which is
exactly why they belong to the asset context and not to a node or variable context.

## ✅✅✅ CONTEXT = **FOCUS + SELECTION**, and they change INDEPENDENTLY *(user, `2026-08-20` — `R-115`)*

> ⭐⭐ **User, verbatim:** *"of course graph pan clicks should not change node selection or detail panels
> view. but it can change focus to the graph so context changes. marquee changes selection (if not
> cancelled). clicking empty space might change focus (i.e. context) so it is legitimate if it switches
> the detail to default view for the clicked UI. just switching perspective or switching document never
> changes sub-selection, just it changes the focus part of the context. same with perspective switch."*

⭐⭐⭐ **This resolves the fork — and it does it by splitting the thing I had conflated.** ⛔ *"Nothing
selected"* was never one state: **FOCUS** and **SELECTION** are two axes, and each has its own triggers.

| axis | ⭐ what it decides | ⭐ what CHANGES it | ⛔ what does NOT |
|---|---|---|---|
| ⭐⭐ **FOCUS** *(which UI surface you are in)* | **the OFFER SET and the DEFAULT view** | a click **into** a surface — **including empty canvas** · switching **document** · switching **perspective** | — |
| ⭐⭐ **SELECTION** *(the per-asset sub-selection)* | **which THING the views are about** | a real selection gesture — **click a node** · **marquee** *(unless cancelled)* | ⛔⛔ **PAN** · ⛔⛔ **switching DOCUMENT** · ⛔⛔ **switching PERSPECTIVE** — ⭐ those move **only the focus part** |

⇒ ⭐⭐ **A pan must leave both the selection AND the drawn view alone.**
⇒ ⭐ **A click on empty canvas legitimately moves FOCUS** ⇒ the panel may switch to **that surface's
default view** — ⛔ that is not "losing" the node, it is the focus part of the context moving.
⇒ ⭐⭐⭐ **Switching document or perspective NEVER touches the sub-selection** — ⭐ and the store already
supports this: `_subSelectionsByAsset` is keyed **by `AssetId`**, so each asset keeps its own pick.

### ⛔⛔ TWO MEASURED CONFLICTS — **today's code does not implement this**

📐 `BuildAfterDrawAction` assigns `MapSelection(ctx.View.Selection, asset)` to `ActiveSubSelection`
**every frame**, and `MapSelection` returns `null` when `selection.Count != 1`.

| ⛔ conflict | |
|---|---|
| **① a PAN can clear the selection** | ⭐ if a pan ends with the canvas reporting no single node, the sub-selection is **overwritten with `null` that frame** ⇒ ⛔ **the panel loses the node on a gesture the user says must not touch it** |
| **② a MULTI-selection is DISCARDED, not represented** | ⭐ marquee two nodes ⇒ `Count != 1` ⇒ `null` ⇒ **the same as nothing.** ⛔ But the ruling says *"marquee changes selection"* ⇒ a multi-pick is a **real selection with no view yet**, ⛔ not an empty one |

⚠ **`②` leaves a genuine gap: there is no multi-node view.** ⭐ **Filed, not invented here** — the honest
interim is *"N nodes selected"* with the **asset/focus default** views offered, ⛔ never a silent
"nothing".

### ✅ The residual — **RULED `2026-08-20`**

⭐⭐ **User: *"yes, empty canvas click clears the selection."*** ⇒ ⛔ **an empty-canvas click BOTH clears
the sub-selection AND moves focus to that surface** — ⭐ the panel then shows that surface's default
view. ⚠ **A PAN still does neither** *(`R-115`)*; the distinction is **click vs drag**, ⛔ not
empty-vs-node.

## ⭐⭐ THE ENTITY CONTEXT — **`Q47`, a deliberate SCOPE EXTENSION**

⚠ **This table covers the three AI perspectives.** ⭐⭐ **The user has extended the panel to the
SCENARIO perspective, where clicking an ENTITY is a context** — 📄
**[`Architect_Question_47_The_Entity_Context.md`](Architect_Question_47_The_Entity_Context.md)**.
⛔ **`Q38`'s fence on the engine/sim inspectors is NOT deleted** — ⭐ it still holds for the three AI
perspectives, where an entity is a **value source**; ⭐⭐ in the scenario perspective the entity is **the
authored thing**.
⚠⚠ **One item from `Q47` belongs in THIS design from the start:** ⭐⭐⭐ **the view registry should take a
PREDICATE, not an asset kind** — an entity has no single kind, it has components. ⛔ Retrofitting that
later means touching every context.

## ⛔ NOT in this table — **and why**

| | |
|---|---|
| **Watch** · **Breakpoints** | ⭐ **curated lists kept open ACROSS selections** ⇒ standalone *(`R-112`/`R-113`)* |
| **a PINNED instance** | ⭐ it is **this same panel with a FROZEN context** *(`R-100`)* — ⛔ not a context of its own |
| **engine / sim inspectors** | ⛔ different lifecycle, not the AI editor |

## ✅ THE TERM I COULD NOT MAP — **resolved, and it is TWO DIFFERENT ROWS**

📌 The user's *"param-to-working state mapper"* mapped to two measured candidates and I refused to
guess. ⭐ **Both now have a home, and they are not the same row:**

| the thing | ⭐ where it shows |
|---|---|
| ⭐ **`PARAMETER SYNCHRONIZATION`** — subtree param ⇄ sub-asset field, copy-in/copy-out *(Approach B)* | ⭐⭐ **its OWN toggle, row `3a`** — subtree nodes, BTree only, ⛔ after the orchestrator wiring |
| ⭐ **the node's two BINDINGS** — `ExpressionTargetField` *(params)* + `WorkingStateTargetField` *(working state)* | ⭐⭐ **FIELDS INSIDE the Properties view, row 3** — ⛔ **not a toggle of their own.** 📐 They are node facet members and `InspectorWindow` already draws them there |

⇒ ⭐ **That is what "closed" means:** ⛔ not *"the question went away"* — **each candidate was given a
row in this table.**

---

# ⛔ WORKING HISTORY — **how the answer was reached. Do NOT quote as the answer**


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

---

# ⭐⭐ WHAT EACH SURFACE ACTUALLY IS — *(written `2026-08-20` at the user's request, to decide keep/retire)*

> ⭐⭐ **User:** *"i forgot what each is about, which helps me to decide what to retire or keep."*

| # | surface | ⭐ what it is, in one line | verdict |
|---|---|---|---|
| **1** | **`InspectorWindow`** *(AiShared, 678)* | the **NODE** inspector: node facets · **`DEFAULT VALUE — {var}`** · the subtree **param-sync** table · utility considerations | ⭐ **FOLD** — it is a view of *"the selected node"* |
| **2** | **`InspectorWindow`** *(Blueprints.Editor, 70)* | ⛔⛔ **a SECOND class with the same name** *(`BP-317`)*, a thin Blueprint-side one | ⛔ **RETIRE** — ruling 9 |
| **3** | **`RuntimeInspectorWindow`** *(57)* | ⭐⭐ **a SHELL** — it holds no content; it hosts whichever per-host pane matches | ⭐⭐⭐ **IT *IS* THE SHELL — reuse it, ⛔ do NOT keep a second window beside Details.** 📐 It renders entity-lifecycle status, mode controls and a scrub bar, then **delegates to the registered `IRuntimeInspectorPane` for the active asset kind** ⇒ ⭐ **the pane registry the toolbar needs already exists here.** ⚠ Its runtime chrome becomes **mode-conditional content** *(`R-111`)*, ⛔ not a separate window |
| **4–6** | **`BTree`/`Hsm`/`BlueprintRuntimeInspectorPane`** | ⭐ the three **FEEDS** behind that shell — *"what is this asset's runtime state right now?"*, read from three different stores | ⭐ **FOLD to ONE VIEW, three feeds** — same question |
| **7** | **`BlueprintDetailsWindow`** *(304)* | the current chameleon — ⛔ **Blueprint only, and `sealed`** so nothing can extend it | ⭐ **FOLD** *(its Blueprint content becomes a feed)* |
| **8** | **`BlackboardAuthoringWindow`** *(462)* | ⭐⭐ **the BYTE BUDGET / BIN-PACK view** — *"does this whole blackboard FIT its tier, and how is it packed?"* plus DTO warnings | ⭐⭐ **BECOMES A VIEW in the ASSET context** *(`R-112`)* — ⛔ not standalone |
| **9** | **`BlueprintVariablesManagedWindow`** *(33)* | hosts `VariablesPanelControl` **again** — a second host of the same control | ⛔ **RETIRE** — duplicate host |
| **10** | **`BlueprintVariablesWindow`** *(82)* | ⚠ **a THIRD variables surface** — the editor's projection of ONE of the asset's three declaration lists | ⛔ **RETIRE** *(`U-16`/row 60)* |
| **11** | **`AiVariablesWindow`** *(120)* | the standalone **variables TABLE**, one per perspective, fed by an `IVariableRowSource` | ⭐ **FOLD as a VIEW** — it becomes the default view in the variable context |
| **12–13** | **`AiWatchWindow`** · **`WatchPanelWindow`** | ⛔ **two watch windows** *(`R-72`)* — a curated list of pinned variables | ⭐⭐ **`AiWatchWindow` SURVIVES · `WatchPanelWindow` RETIRES** *(`R-113`)* — ⛔ **standalone, never a Details view.** ⚠ **And its BREAKPOINT-WATCH list moves to the Breakpoints window** — `R-98`: *the Watch stays variables-only* |
| **14** | 🔴 **`LiveBlackboardPanel`** *(BTree.Editor, 120)* | 📐 its own doc: *"renders a **read-only** blackboard panel inside an existing ImGui window … in Slice 2 field values are **live-read** from the ECS blackboard component."* ⛔⛔ **in-degree 0 — NOTHING HOSTS IT** | ⛔ **RETIRE** — ✅ ruled `2026-08-20` *(`R-114`)*: no feature the variable table lacks |
| **15** | **`DetailsPanel`** *(NodeEditor.UI, 151)* | the generic **panel primitive** — not a surface, the thing surfaces are built from | ⭐ **KEEP — infrastructure** |
| **16** | **`GraphSignatureWindow`** | the graph's **signature** — its inputs/outputs as a callable | ⭐ **FOLD** *(`BP-128`)* — a view of *"the selected graph"* |

### ⚠ `LiveBlackboardPanel` — **the one that needs a decision, stated out loud**

⭐ **What it does:** a read-only live blackboard dump for **BTree**, live-read from the ECS component.
⭐ **Why it exists:** built deliberately — `.dev/_DONE/blueprints-2` `TASK-BT-S2-03/05`, *"decode them to
display actual runtime values."*
⛔ **Why it is a problem:** **nothing hosts it** — it lost its host rather than never having one.
⭐⭐ **What supersedes it:** the **Details variable table's live Value column** *(Batch 90/94/95)* now does
the same job on **all three** hosts, with change highlighting and editing.
⇒ ⭐ **Recommended: RETIRE** — ⛔ but 📌 `R-13` requires saying which of the three it is: this is
**duplicate CODE superseded by a working surface**, not a dormant capability. ⚠ **The user decides.**

---

# ⭐⭐⭐ RECOMMENDED ANSWERS `A`–`F` — *(`2026-08-18`; I analyse and SUGGEST, the user APPROVES)*

> ⛔ **Nothing here is scheduled.** 📌 **`R-27` still gates the BUILD** — see `R5`.
> ⭐ **Reply *"approved"*, or name the one you want changed.**

### ⭐⭐⭐ `Q38-A` — contextual, or a mode toolbar?

| ✅✅ **RULED `2026-08-18` BY THE USER — `R-98`, and it OVERRULES the recommendation below.** |
|---|
| ⭐⭐⭐ **THE DETAILS TOOLBAR IS A PANEL SWITCH — TWO STAGES.** ⭐ **The CONTEXT decides which panels are OFFERED and which is DEFAULT; the USER picks among them with radio-style toggles.** 📌 *"for variables the default is the variable table, but using toolbar (radio-button like toggles) it should be possible to switch it into another already existing panels"*. ⛔ **Not the `B8` two-authorities bug** — the toolbar never changes what the panel is ABOUT, only which VIEW of one context is drawn. ⭐⭐ **First goal is FEWER WINDOWS, not merged content.** ⭐ **Pinning captures the context AND the active view.** ⭐ **The Watch stays variables-only and MUST remain persistable/reloadable** |

| ⛔ ~~RECOMMENDED *(SUPERSEDED — do NOT quote)*: CONTEXTUAL is the ONLY switch. The toolbar button is a PIN, not a mode.~~ |
|---|

📌 **`R-95` already made FOCUS the authority** *(`FocusedSurface`, a latch, cross-host)*.
⛔⛔ **A mode toolbar competing with focus re-creates TWO AUTHORITIES over one panel — which is
precisely the `B8` defect** *(a snapshot and a live read disagreeing about who owns the panel)*.
⭐⭐ **The user's "local toolbar" instinct is real and is SERVED BY `F`**: the button freezes the
context instead of overriding it. ⇒ ⭐ **one authority, plus an explicit escape hatch.**
**Blast radius: NONE new** — the mechanism shipped in Batch 87.

### ⭐⭐ `Q38-B` — one panel across perspectives, or one per perspective?

| ✅✅ **RULED `2026-08-20` BY THE USER — `R-110`.** |
|---|
| ⭐ **The Details panel is in ALL perspectives and its content is PLUGGABLE.** ⭐⭐ **Which sub-panels (views) are available depends on the CLICKED THING and the CURRENT PERSPECTIVE** ⇒ the offer set is a function of `(selection, perspective)` — 📌 `R-98`, now with perspective named. ⛔⛔ **One shared instance is NOT required:** *"some views do not change with perspective but that does not necessarily mean we have to share the single instance … if multiple ones showing same data are possible"* ⇒ ⭐ **read-only views may be instanced freely; SHARING is preferred for EDITING views.** ⭐ **Feeds registered by whoever needs to show contextual info, at the composition root** *("host" = the initial composition of all the sw components)* |

| ⭐ ~~RECOMMENDED *(kept as the mechanism, ⛔ but "one instance" is NOT a requirement — see the ruling)*: ONE window CLASS · ONE INSTANCE PER PERSPECTIVE · FEEDS REGISTERED PER HOST.~~ |
|---|

📐 **That is already how this editor works** — every window is built by `PerspectiveWorkspaceRegistrar`
with an `owningPerspective` and a suffixed id *(`ai_my_blueprint_{suffix}`)*.
⛔ **A single global instance would fight the docking layout** and make "which perspective am I in"
invisible. ⭐ **And the FEED registry already exists** — `RuntimeInspectorWindow.RegisterPane`.
**Blast radius: MEDIUM** — ⚠ `BlueprintDetailsWindow` stops being a window and becomes a feed.

### ⭐⭐ `Q38-C` — what about views that are not "properties"?

| ✅✅ **RULED `2026-08-20` BY THE USER — `R-112`, and it CORRECTS the recommendation below.** |
|---|
| ⭐⭐⭐ **THE TEST IS: IS IT ABOUT THE CURRENT SELECTION?** ⭐ **YES ⇒ a VIEW inside Details**, reachable by a toolbar toggle — ⛔ **a different question earns its own VIEW, not its own WINDOW**, once `R-98` exists. ⇒ **`BlackboardAuthoringWindow`'s byte-budget / bin-pack becomes a view in the ASSET context.** ⭐ **NO — a CURATED LIST kept open ACROSS selections ⇒ STANDALONE**: the **Watch** and the **Breakpoints** windows *(`R-113`)* |

| ⭐ **The old half that SURVIVES**: ⛔ a surface never stays separate **merely because it is a different ASSET TYPE** — that is a FEED difference. ⛔ ~~*"…stays STANDALONE only if it answers a DIFFERENT QUESTION"* — too coarse; see the ruling.~~ |
|---|

⭐ **The test, in one line:** *does it answer **"tell me about the thing I selected"**?*
⭐⭐ **Byte-budget / bin-packing answers *"will this layout FIT?"*** — ⛔ a question about the layout
being authored, not about a selection ⇒ **`BlackboardAuthoringWindow`'s layout view STAYS.**
⚠ **Everything split by asset TYPE folds** — that split is the defect, not a feature.
📌 Consistent with the existing ruling *("bin-packing… is a genuinely different job")* and with
*"no rush removals"*. **Blast radius: LOW** — ⭐ it is a criterion, so it needs no list to be right.

### ⭐⭐⭐ `Q38-D` — runtime vs authoring: one panel or two?

| ✅✅ **RULED `2026-08-20` BY THE USER — `R-111`.** |
|---|
| ⭐⭐ **Runtime vs authoring is part of the CONTEXT definition, not a second panel** — *"authoring mode can provide different set of available views than runtime"* ⇒ **the MODE joins `(selection, perspective)` in deciding the offer set.** ⭐⭐⭐ **And a view is implemented ONCE, supporting multiple modes** — ⛔ not an authoring view plus a runtime twin *(ruling 9)*. 📌 The variable table already does this: the INITIAL arm while planning, the LIVE arm while running *(`Q32` ruling 3)* |

| ⭐ **The recommendation's SHELL half still holds**: ~~ONE.~~ ⭐ the shell that survives is `RuntimeInspectorWindow`'s, ⛔ not `BlueprintDetailsWindow`'s |
|---|

📐 **Measured (`R2`):** the runtime family is **already** shell + per-host feeds. 📌 And Track C ruled
run state governs **WRITABILITY, not WHICH SURFACE** *(`DESIGN_Variable_Details_And_Editing.md` §5)*.
⛔⛔ **Two panels would force the designer to know which window to look at based on whether the sim is
running** — ⚠ the same *"two doors to one room"* the designer quickstart already apologises for.
⭐⭐⭐ **Which half survives matters:** ⭐ **keep the GENERAL one** *(the shell with the registration
seam)* and **fold the SPECIFIC one** *(`BlueprintDetailsWindow`, `sealed`, blueprint-shaped)*.
⛔ **Ruling 9 applied the other way round would delete the seam and keep the special case.**
**Blast radius: MEDIUM-HIGH** — ⚠ the shell needs a name that is no longer "Runtime".

### ⭐⭐ `Q38-E` — sequencing

| ✅ **APPROVED `2026-08-20` by the user — *"Q-E yes"*.** ⭐ **ANSWER NOW, BUILD AFTER the visual check passes** *(`R-27`)*. |
|---|

⭐ **Then in this order, each step independently revertible:**

| # | step | why here |
|---|---|---|
| **1** | **collapse the TWO watch windows into one** *(`R-72`, `BP-330`)* | ⭐ smallest, and it removes a duplicate **before** anything is folded onto it |
| **2** | **generalise the shell** — feeds keyed by asset kind **and** by question | ⛔ no feed moves yet |
| **3** | **move feeds ONE AT A TIME**, re-checking visually after each | 📌 *"do not merge surfaces nobody has seen"* — ⚠ **already paid for once** |
| **4** | **retire the duplicates LAST** *(`U-16`/row 60, the second `InspectorWindow`)* | ⛔ **never before its replacement is proven** — 📌 row 60's own rule |

### ⭐⭐ `Q38-F` — the pin *(NEW)*

| ✅ **RULED `2026-08-19` BY THE USER — `R-100`, which EXTENDS this.** |
|---|
| ⭐⭐ **ONE WINDOW INSTANCE PER PIN, TITLED BY ITS CONTEXT** — ⛔ **not a toggle that re-points one reusable pinned window.** ⭐ Id keyed on `(view, asset, selection)`, an exact duplicate FOCUSES rather than spawning; ⭐ **pins are VOLATILE — they do not survive a restart** *(⛔ unlike the Watch, which is persistable)* |

| ⭐ **The recommendation below still holds as the MECHANISM**: a pinned inspector is the SAME CLASS with a FROZEN context source. Not a new window type. |
|---|

| ⭐ | |
|---|---|
| **what is pinned** | ⭐⭐ the Batch-87 tuple — `SelectionOrigin` · `FocusedSurface` · `ActiveSubSelectionOrigin` · the selection · the active asset. ⛔ **No new context model** |
| **what a pinned instance does** | ⭐ reads the **same feeds**, ⛔ **never updates its context.** ⚠ **Live VALUES still tick** — 📌 frozen **context**, not frozen **data** |
| **how many** | ⭐ **N, unlimited.** ids suffixed like every other multi-instance window |
| **when the target dies** | ⭐⭐ **the window STAYS and says so** — ⛔ **never silently blank.** 📌 `F3`'s rule: *"same information value, no false expectations"* |
| ⚠ **what it is NOT** | ⛔ **not a second selection authority** — a pinned window **cannot** drive the live panel, or `A` collapses |

**Blast radius: LOW** — ⭐ **the payload already exists**; this is a capture, a window id, and a
"do not update" flag.

## ⭐⭐⭐ `LiveBlackboardPanel` — **MEASURED against the variable table, `2026-08-18`**

> ⭐ **User:** *"check if live blackboard panel is feature wise superseded by the variable table."*

| feature | `LiveBlackboardPanel` | the variable table |
|---|---|---|
| **three columns, Field/Type/Value** | ✅ | ✅ *(`Type` is the one toggle — Details on, Watch off)* |
| **live read from `BrainBlackboard`** | ✅ direct pointer at `FieldOffset` | ✅ via `ILiveBlackboardValueProvider` |
| **14 primitives + `Vector2/3/4`** | ✅ **a hand-rolled `typeof` switch** | ✅ **GENERIC `Marshal.PtrToStructure`** — ⭐ strictly wider |
| 🔴 **enums** | ⛔⛔ **falls through to `"?"`** | ✅ decoded **and printed by NAME** *(`RawValueDecoder:58`)* |
| 🔴 **any other blittable struct** | ⛔ `"?"` | ✅ generic |
| **`bool` 1-byte packing** | ✅ by pointer cast | ✅ **explicitly, with the reason recorded** *(`Marshal.SizeOf(bool)` is 4)* |
| **INITIAL value when not running** | ⛔⛔ **none — live only** | ✅ the `Initial` arm *(row 58)* |
| **states** | `offline` · `--` | ⭐⭐ **`(pending)` · `<unreadable>` · stale-greyed** — three DISTINCT meanings |
| **multi-line struct tooltip** · **change highlight** · **selection highlight** · **edit gestures** · **grouping / multi-asset** | ⛔ none | ✅ all |
| ⚠ **fixed-list summary** — `List<T>[N] Count=k {…}` | ✅ **`FixedListFormatter`** *(`FC-3c` / `Q#21-D3`: "instead of the composite-blind `?`")* | 🔴 **NO — falls to generic struct rendering** |

### ⇒ ⭐⭐⭐ VERDICT: **superseded on every axis BUT ONE**

⛔ **The one exception is the FIXED-LIST SUMMARY**, and ⭐⭐ **it is not a reason to keep the panel — it
is a ONE-ARM GAP in the shared formatter.** 📐 `FixedListFormatter` is **already referenced from
`AiShared`** *(`FixedListBufferViewProvider:108`)*, so ⛔ **no new dependency** — the table's
`OneLine`/`MultiLine` simply never tries it before falling back to generic struct rendering.

| ⭐⭐⭐ **RECOMMENDED** | |
|---|---|
| **1** | ⭐ **Give `VariableValueFormatter` the fixed-list arm** — try `FixedListFormatter` first, generic struct after |
| **2** | ⭐⭐ **THEN retire `LiveBlackboardPanel`** |

⚠⚠ **The order is the whole point.** 📌 *"No rush removals"* — ⛔ **retiring it first would silently
lose a rendering the design deliberately built**, which is exactly the mistake `BP-295` was.
⭐ **The capability transfers, THEN the surface goes.**

---

# ⭐⭐ TWO SURFACES MEASURED IN DETAIL — `2026-08-18`

## W. ⭐⭐⭐ `AiWatchWindow` vs `WatchPanelWindow` — **NOT duplicates. Different FEEDS, duplicated WINDOW**

> ⭐ **User:** *"what is the difference… Better to have just one."*

| | `AiWatchWindow` *(`AiShared`, all 3 perspectives)* | `WatchPanelWindow` *(`Blueprints.Editor`, blueprint only)* |
|---|---|---|
| **feeds** | ⭐⭐ **TWO** — ① **breakpoint watches** *(`IDataBreakpointManager`, `IsWatch`)* ② **pinned variables** *(`PinnedVariableRowSource`)* | ⭐ **ONE** — **blueprint PIN watches** *(`IBlueprintDebugSession` → `WatchRowBridge`)* |
| **what a row IS** | ① a **CONDITION** — predicate · `Enabled` · `HitCount` · identity `Guid` ② an **OBSERVED IDENTITY** — `(AssetId, Entity, Section, VariablePath)` | a **PIN** — `WatchEntry { AssetId, GraphId, PinId }` |
| **renders through** | ✅ shared `VariableTableControl` | ✅ shared `VariableTableControl` *(row 59b killed its hand-rolled `BeginTable` — "the fourth variable table")* |
| **`IVariableTableHost`** | ✅ *(Batch 87)* | ✅ |

📌 **`AiWatchWindow`'s own doc names the count:** *"There are in fact **three** watch-shaped things in
the codebase, not two — the blueprint PIN watch is the third."* ⇒ ⭐⭐ **`WatchPanelWindow` IS that
third**, and it is **not** a copy of the other two.

### ⇒ ⭐⭐⭐ VERDICT: **yes, ONE window — but it must host THREE FEEDS**

⛔⛔ **The duplication is the WINDOW, not the content.** ⚠ **Merging by deleting one would DELETE A
FEED** — the same mistake shape as `BP-295`.

⭐⭐ **And the merge is unusually cheap, because the normalisation already happened:** `WatchRowBridge`
already converts pin watches into **`VariableRow`s** ⇒ ⭐ **the third feed is just another row source
beside `PinnedVariableRowSource`.**

| ⭐⭐⭐ **RECOMMENDED** | |
|---|---|
| **1** | ⭐ **Move the pin feed into `AiWatchWindow` as a third section** *(row source, not a new table)* |
| **2** | ⭐⭐ **THEN retire `WatchPanelWindow`** |
| ⛔ **do NOT** | merge the **breakpoint** list into the variable lists — 📌 `AiWatchWindow` measured that they are different entities, *"and merging them silently would have been wrong"*; **persistence already stores them as two lists** |

⭐ **This is `Q38-E` step 1**, and it is the smallest step for a reason: ⛔ **nothing should be folded
onto a surface that is itself still duplicated.**

## B. ⭐⭐ `BlackboardAuthoringWindow` — **what it adds on top of the variable list**

> ⭐ **User:** *"what the blackboard authoring window is doing on top of the variable list window…
> maybe via the toolbar option in Details, maybe it could be made part of the Properties."*
> 📌 **The prior discussion is `Q38-C` + the user's `2026-08-17` companion ruling:** *"ad
> `VariablesPanelControl` — **keep for now**, but we need to rethink it later — find a way how to
> integrate it."*

📐 **Measured — SEVEN things, and they answer FOUR DIFFERENT QUESTIONS:**

| # | what it adds | ⭐ which question |
|---|---|---|
| 1 | **byte size** per variable | *"tell me about this variable"* |
| 2 | **`AliasedBy`** — which assets/elements alias it | *"tell me about this variable"* |
| 3 | **unused** diagnostic | *"tell me about this variable"* |
| 4 | ⭐⭐ **the byte BUDGET / bin-pack picture** | ⛔ *"will this layout FIT?"* |
| 5 | **`SUB-TREE ALLOCATIONS (auto-managed)`**, incl. *"(size unknown until build)"* | *"what is allocated that I did not author?"* |
| 6 | **unbound sub-tree DTO requirements** ⚠ *(with the dead "Promote" item — visual check `A5`)* | *"what is missing?"* — **a validation question** |
| 7 | **`Use editor-managed blackboard`** toggle · **lossy-save guard** | ⛔ **asset-level SETTINGS, not variables at all** |

### ⇒ ⭐⭐⭐ VERDICT: **it is not ONE surface to move — it is FOUR, and they go to FOUR places**

⭐ **Applying `Q38-C`'s criterion** *(standalone only if it answers a different QUESTION)*:

| rows | ⭐⭐⭐ **RECOMMENDED destination** | why |
|---|---|---|
| **1 · 2 · 3** | ⭐⭐ **the PROPERTIES dialog** *(the user's second guess — correct)* | ⛔⛔ **NOT table columns** — 📌 `Q32`: *"Bytes, Role and Scope go"*; putting them back as columns reverses a shipped ruling |
| **4** | ⭐⭐ **STAYS STANDALONE** *(or becomes a toolbar-selectable FEED)* | 📌 `Q38-C`: a layout question, not a selection question. ⭐ **This is the half that justifies the window's continued existence** |
| **5 · 6** | ⭐ **a DIAGNOSTICS feed** *(beside the validation surfaces)* | ⚠ *"what is missing"* is not *"tell me about the selection"* |
| **7** | ⭐⭐ **ASSET settings** — ⭐ a legitimate Details feed **when the ASSET is the selection** | ⛔ it is not a variable at all, and it is the one row that is genuinely mis-homed today |

⚠⚠ **So "integrate `BlackboardAuthoringWindow`" is the WRONG UNIT OF WORK.** ⭐ **Split it by question
first; then only row 4 remains, and it is small enough to keep or to make a feed.**
⛔ **A single move would drag an asset-level toggle and a validation list into a variable panel.**

## W2. ⭐⭐⭐ "BREAKPOINT WATCHES" — **measured `2026-08-18`**

> ⭐ **User:** *"what are breakpoint watches? how does it relate to the breakpoints? does it make sense
> to integrate it to the breakpoint window?"*

### ⭐⭐ What one IS — **a Breakpoint with a FLAG. Not a sibling concept**

📐 `BreakpointTypes.cs:93` — **`IsWatch` is a `bool` on the `Breakpoint` record itself:**
*"True when this breakpoint is a 'watch' entry — persisted to `watches.json` and shown in the Watch
panel."* ⭐ Same record, same `DataBreakpointManager` store, same `Guid`, same
`Condition` · `Enabled` · `HitCount` · `IsBroken`.

📐 **And it is set POST-HOC**, never at creation — `DataBreakpointManager:393`:
*"Mark as watch (`AddBreakpoint` doesn't set `IsWatch`)"* ⇒ `AddBreakpoint(...)` then
`bp with { IsWatch = true }`.

⇒ ⭐⭐⭐ **A "breakpoint watch" is a TAGGED SUBSET of the breakpoints, not a kind of variable.**
📌 Which is exactly why `AiWatchWindow`'s doc insists it and a pinned variable *"are not the same
entity"* — ⭐ **a watch entry has a CONDITION THAT FIRES; a pinned row cannot fire.**

### 🔴🔴 THE FINDING — **`IsWatch` is a PRESENTATION flag, and NOTHING in the fire path reads it**

📐 **Every non-test use of `IsWatch`, enumerated:** the record declaration · `WatchPersistence` *(save
filter)* · `DataBreakpointManager` `:351/:386/:395` *(set)* · `DebugSessionPersistence` `:43/:130`
*(DTO)* · `EditorSubsystem:4099` *(restore)* · `AiWatchWindow` *(read)*.
⛔⛔ **NOT ONE of them is in an evaluation or hit path.**

⇒ ⚠⚠ **A "watch" still BREAKS.** ⭐ It is a breakpoint that is *also listed in the Watch panel* —
⛔ **not a non-breaking observer**, which is what the word "watch" promises in every debugger a
designer has used. ⭐⭐ **Flag this before any merge: the NAME is making a behavioural promise the flag
does not keep.**

### ⭐⭐ Where the breakpoint LIST actually lives today — **inverted**

| surface | what it renders |
|---|---|
| **`DataBreakpointManagerWindow`** *(`Hrot.Presentation`, "Data Breakpoints")* | ⭐ **full management** |
| 🔴 **`AiBreakpointsWindow`** *(`AiShared`, per-perspective, "Breakpoints")* | ⛔⛔ **A COUNT BANNER.** 📐 `DrawClientArea` renders one line — *"N active breakpoint(s). Open the global Data Breakpoints window for full management."* ⭐ Its own comment: *"A future iteration can render a full breakpoint grid"* |
| **`AiWatchWindow`** | ⭐⭐ **an actual LIST of the `IsWatch` entries** |

⇒ ⭐⭐⭐ **The breakpoint LIST is in the WATCH window, and the BREAKPOINT window is a signpost.**

### ⇒ ⭐⭐⭐ VERDICT: **YES, integrate — and it makes the watch merge CLEANER, not harder**

| ⭐⭐⭐ **RECOMMENDED** | |
|---|---|
| **1** | ⭐⭐ **Move the `IsWatch` list to the breakpoint surface** — ⭐ it is breakpoint vocabulary *(condition · enabled · hit count · broken)*, and that window has **no list at all** today |
| **2** | ⭐⭐⭐ **The Watch window then holds ONE ROW TYPE — variables only** *(pinned variables + blueprint pin watches, ⭐ both already `VariableRow`s)* ⇒ **`W`'s three-feed merge becomes a TWO-feed merge, homogeneous** |
| **3** | ⚠ **Decide what `IsWatch` MEANS** — ⭐ *"a breakpoint I am also monitoring"* **or** ⛔ *"a non-breaking observer"*. 📌 **Today it is the first and reads as the second** |
| ⛔ **do NOT** | resolve the **`AiBreakpointsWindow` vs `DataBreakpointManagerWindow`** duplication in the same step — ⚠ **that is a THIRD surface pair**, and 📌 `Q38-E` step 1 exists precisely so one merge does not drag in another |

> ✅✅ **SUPERSEDED IN PART, `2026-08-18` — the breakpoint half moved to its own question.**
> 📌 **User:** *"the breakpoint UI unification is a new area, new architect question."*
> ⇒ 📄 **[`Architect_Question_44_Breakpoint_UI_Unification.md`](Architect_Question_44_Breakpoint_UI_Unification.md)** *(`R-96`)*.
> ⭐⭐ **And it REORDERS `Q38-E`:** `Q44-B` *(send the breakpoint rows home)* now runs **BEFORE**
> `Q38-E` step 1 — ⛔ otherwise step 1 merges a **heterogeneous** surface. ⭐ After `Q44-B` the Watch
> window is **variables only**, and the merge is trivial.

⭐⭐ **Net effect on `Q38`:** ⛔ **the watch family is not "two windows to make one"** — ⭐ it is
**THREE row types across THREE windows**, and the right first move is to **send the breakpoint rows
home**, which leaves a genuinely single-typed Watch to merge.

---

# ✅✅✅ RULED `2026-08-18` — **the toolbar IS a panel switch. My `A` was wrong**

> ⭐⭐⭐ **User, verbatim:** *"the toolbar in the detail window should switch different panels that are
> related to the selected/focused stuff. for variables the default is the variable table, but using
> toolbar (radio-button like toggles) it should be possible to switch it into another already existing
> panels like param-to-working state mapper or inspector or runtime inspector or other variable related
> existing panels; whether to merge these toggleable panels into something more generic is a question
> for later once i see what is all available, the first goal is to get rid of too many separate
> contextual windows and integrate them into one single contextual (and toolbar switchable) details
> panel."*

⚠⚠ **My `Q38-A` recommendation — *"contextual is the ONLY switch; the toolbar is a PIN"* — is
OVERRULED.** ⭐ **And the original `Q38-A` text already anticipated the right answer:** *"possibly
both: context picks the default, the toolbar overrides it."* ⛔ **I narrowed it and lost that.**

## ✅ `Q38-A` — **RULED: TWO STAGES, and they do not compete**

| stage | who decides |
|---|---|
| ⭐⭐ **① which panels are OFFERED, and which is DEFAULT** | ⭐ **the CONTEXT** *(focus + selection — `R-95`)* |
| ⭐⭐ **② which of them is SHOWN** | ⭐ **the USER**, via radio-style toggles |

⭐⭐⭐ **Why this does NOT reintroduce the `B8` two-authorities bug:** ⛔ the toolbar never changes
**what the panel is about** — ⭐ **only which VIEW of the same context is drawn.** ⇒ **one authority
over the CONTEXT; a user choice over the VIEW.**
⚠ **Rule to keep:** ⭐ **a context change re-offers the set; a toggle within it STICKS** *(per context
kind, so returning to a variable returns to the view you chose for variables)*.

## ✅ `Q38-F` — **RULED, and sharper than my version**

⭐⭐⭐ **Pinning opens a new window of the SAME PANEL TYPE that was active when the pin button was
pressed, pinned to the context at that moment.**
⇒ ⭐ **the pin captures TWO things — the context tuple AND the selected view.** ⛔ Not just the context.

### ⭐⭐⭐ EXTENDED `2026-08-19` *(user)* — **one window INSTANCE per pin, titled by its context**

> ⭐⭐ **User, verbatim:** *"each pin operation is supposed to open a new window instance, with its
> title describing the pinned context"*

⭐ **Two things this settles that the `2026-08-18` ruling left open:** ⛔ **a pin is NOT a toggle** that
re-points one reusable pinned window — ⭐ **it SPAWNS**; and ⭐⭐ **the title is the identifier the user
navigates by**, ⛔ **not a generic *"Details (pinned)"*** — with N pinned windows docked as tabs, a
repeated title makes the whole feature unusable.

#### ⭐⭐ INVENTORY — **the spawn mechanism ALREADY EXISTS** *(`R-74`, `2026-08-19`)*

```
grep -rn "IsVolatile" --include=*.cs .   → 7 hits, 2 of them producers
```

| 📐 measured | |
|---|---|
| ⭐⭐⭐ **`ManagedWindow.IsVolatile`** | ⭐ `WindowManager:538` — *"Iterate a copy to allow safe removal of closed volatile windows"* ⇒ **a volatile window is REMOVED from the registry when closed.** ⛔ **Exactly the lifecycle a pin needs, and it ships** |
| ⭐⭐⭐ **`ComponentEditWindow` is the working PRECEDENT** | **spawned at runtime**, `IsVolatile = true` · `ShowInMenu = false` · `IsOpen = true`, registered from `ComponentReflector:366` |
| ⭐⭐ **and it already does BOTH halves of this ruling** | **content-keyed id** `cedit_{e.Index}_{e.Generation}_{type.FullName}` · **context-describing title** `$"Edit {type.Name} [{e.Index}]"` |
| ⚠ **`RegisterWindow` OVERWRITES on a duplicate id** | `_windows[window.Id] = window` ⇒ ⛔ **N instances REQUIRE N distinct ids** — the id scheme is not cosmetic, it is what makes the feature work |
| ⚠ **volatile ⇒ NOT persisted, NOT in the window menu** | `:323` skips them for the menu, `:376` skips them in `SaveSettings` |

⇒ ⭐⭐⭐ **This is ROUTING, not construction** *(ruling 9)*. ⛔ **Do not build a second spawned-window
mechanism** — ⭐ mirror `ComponentEditWindow`.

#### ✅✅ Two sub-choices — **BOTH APPROVED `2026-08-19`** *(user: "both recommendations approved")*

| | ✅ **RULED as recommended** |
|---|---|
| ✅ **pinning the SAME (context, view) twice** | ⭐⭐ **Key the id on `(view, asset, selection)` and FOCUS the existing window** — the `ComponentEditWindow` precedent exactly. ⭐ **Every pin that differs in context OR view still spawns**, so the user's rule holds everywhere it is observable; only a literal duplicate collapses. ⛔ **A monotonic `pin_7` counter is the alternative** and it does obey the letter — ⚠ **but two byte-identical windows are noise, and the user cannot tell them apart because their TITLES are identical too** |
| ✅ **do pins survive a restart?** | ⭐ **NO — volatile, as the precedent is.** ⛔ **Do NOT confuse this with the Watch**, which is ruled **persistable** — ⭐ a watch is a curated list the user built; a pin is a scratch view. ⚠ **If pins should persist, `IsVolatile` is the wrong base** and that is a bigger change |

#### ⭐ The title — **compose it from the pinned tuple, do not invent a scheme**

⭐⭐ `{view} · {asset} · {selection}` — e.g. **`Details · OrcGuard_BT · MoveTo_Advance`**,
**`Sync · OrcGuard_BT · Shoot_BT`**, **`Runtime · Guard_01`**.
⚠ **`ManagedWindow` takes `id` and `title` SEPARATELY** *(`Title` is `protected set`)* ⇒ ⭐ **a title
collision is harmless; an ID collision destroys a window.** ⛔ **Never derive one from the other.**

## ✅ The Watch window — **RULED**

⭐⭐ **Variables only** *(after `Q44-B` sends the breakpoint rows home)*, and ⭐⭐⭐ **it MUST remain
persistable to a file and reloadable.** ⚠ **That is a constraint on the merge:** ⛔ collapsing the two
watch windows must not lose `DebugSessionPersistence`'s watch list — 📌 it already persists
`WatchEntry { AssetId, GraphId, PinId }` beside the breakpoints.

## ✅ `Q38-B`, `Q38-D` — **as recommended.** ✅ `LiveBlackboardPanel` — **formatter arm FIRST, then retire**

---

# ⭐⭐⭐ THE INTEGRATION TABLE — **what becomes a Details toggle, and what does not**

> ⭐ **Goal, in the user's words:** *"the first goal is to get rid of too many separate contextual
> windows."* ⇒ ⛔⛔ **This step does NOT merge the panels' CONTENT.** ⭐ **They keep their current
> implementations and become TOGGLES inside one window.** 📌 *"whether to merge these toggleable panels
> into something more generic is a question for later."*

> ⛔⛔ **A/B/C BELOW ARE SUPERSEDED** by the **CONTEXT → VIEWS TABLE** above — same content, completed
> against the measured selection kinds. ⭐ **D/E/F remain LIVE.** ⛔ Do not quote A/B/C as the answer.

## ⛔ A. ~~Toggles offered when the context is a **VARIABLE / a variable SECTION**~~ *(superseded)*

| toggle | today's surface | notes |
|---|---|---|
| ⭐⭐ **Variables** *(DEFAULT)* | `VariableDetailsSection` | ⭐ already the Details content |
| **Layout / byte budget** | `BlackboardAuthoringWindow`'s bin-pack view | ⚠ **answers *"will it fit?"*** — ⭐ as a TOGGLE it is reachable without being a window |
| **Live values** | ⛔ **none — retire `LiveBlackboardPanel`** | 📌 superseded once the formatter gains the fixed-list arm |

## ⛔ B. ~~Toggles offered when the context is a **NODE**~~ *(superseded)*

| toggle | today's surface |
|---|---|
| ⭐⭐ **Properties** *(DEFAULT)* | `InspectorWindow`'s **facet editor** |
| **Default value** | `InspectorWindow`'s **`DEFAULT VALUE — {var}`** — ⭐ the node-scoped default of the variable this node writes |
| ⚠ **Parameter sync** | `InspectorWindow`'s **`PARAMETER SYNCHRONIZATION`** *(`DrawSyncBindingsTable`)* — **subtree nodes only** |
| **Utility** | `InspectorWindow`'s **`UTILITY CONSIDERATION`** — utility nodes only |
| ⭐ **Runtime** | the per-host **`RuntimeInspectorPane`** *(BTree · HSM · Blueprint)* |

> ✅ **RESOLVED `2026-08-19`** — the user asked for BOTH to be explained and then ruled *"approved,
> should be wired, add both to the plan"* *(`R-99`)*. ⭐ **Approach A (whole-DTO aliasing) and Approach B
> (field-level sync) are two mechanisms, and the toggle shows Approach B's table.** ⚠ The original
> ambiguity is kept below for the record.
>
> ⚠⚠ ~~ONE TERM I COULD NOT MAP — please confirm.~~ ⭐ You said *"param-to-working state mapper"*.
> 📐 **The two measured candidates are DIFFERENT things:**
> ⭐ **`PARAMETER SYNCHRONIZATION`** — subtree param ⇄ sub-asset field copy-in/copy-out *(Approach B)*
> ⭐ **the node's two BINDINGS** — `ExpressionTargetField` *(params)* and `WorkingStateTargetField`
> *(working state)*, the pair `ComposeAiPrimitiveAction` creates
> ⛔ **I am not guessing which you meant** — the second is closer to the words, the first is closer to
> the word *"mapper"*.

## ⛔ C. ~~Toggles offered when the context is the **ASSET** or a **GRAPH**~~ *(superseded)*

| toggle | today's surface |
|---|---|
| **Asset settings** | `BlackboardAuthoringWindow`'s **`Use editor-managed blackboard`** — ⛔ genuinely mis-homed today |
| **Graph signature** | `GraphSignatureWindow` — 📌 **`BP-128`**, which `Q38` absorbs |
| **Diagnostics** | **sub-tree allocations** + **unbound requirements** |

## ⛔ D. NOT Details content — **and why each**

| surface | why it stays out |
|---|---|
| ⭐⭐ **Watch** | ⛔ **by definition NOT focus-following** — folding it destroys its job. ⭐ **Own window, variables only, persistable** |
| ⭐⭐ **Breakpoints** | 📄 **`Q44`** — its own family, its own window |
| **the gutter + context menus** | *"set a breakpoint here"* is a **canvas gesture** |
| **`DataBreakpointManagerPanel`** | `Q44-A`'s base |
| **engine / sim inspectors** *(`EntityInspectorPanel` ×2, `DerEntityInspectorPanel`, `FdpEntity*`, ExCon `InspectorPanel`, the `Fake*` windows)* | ⛔ **different lifecycle, not the AI editor** |
| **`DetailsPanel`** *(`NodeEditor.UI`)* | ⭐ the **primitive** the shell is built from, not a feed |

## ⭐⭐ E. RETIRED by this work — **not toggles, duplicates**

| surface | replaced by |
|---|---|
| `BlueprintVariablesWindow` · `BlueprintVariablesManagedWindow` | the Variables toggle *(`U-16`, row 60)* |
| **one of the two `InspectorWindow`s** | the Properties toggle |
| `AiVariablesWindow` | the Variables toggle ⚠ *(unless it is wanted as a PINNED instance)* |
| `LiveBlackboardPanel` | ⭐ the Variables toggle's Value column — ✅ **RETIRE ruled `2026-08-20` (`R-114`)**, ⛔ no longer conditional on the formatter arm |
| `WatchPanelWindow` | ⭐ **`AiWatchWindow`**, the survivor *(`R-113`)* |
| `AiBreakpointsWindow`'s banner | `Q44-A` |

## ⭐ F. The count

| | |
|---|---|
| **windows today** *(editor family)* | ⭐ **16** |
| **after** | ⭐⭐ **Details · Watch · Breakpoints · My Blueprint · Canvas** + ⭐ **N pinned instances** |
| ⚠ **and nothing is DELETED that is not replaced** | 📌 *"no rush removals"* — ⭐ every row in `E` names its replacement |
