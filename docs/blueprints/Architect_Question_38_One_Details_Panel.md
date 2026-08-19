<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: the RECOMMENDED ANSWERS A-F section at the very bottom, plus the
  REVISION 2026-08-18 above it. The revision supersedes the 2026-08-17 inventory
  (8 surfaces; the graph finds 25) and corrects section 4's claim that the shell
  is missing. A-F now carry recommendations awaiting the user's approval.
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

---

# ⭐⭐⭐ RECOMMENDED ANSWERS `A`–`F` — *(`2026-08-18`; I analyse and SUGGEST, the user APPROVES)*

> ⛔ **Nothing here is scheduled.** 📌 **`R-27` still gates the BUILD** — see `R5`.
> ⭐ **Reply *"approved"*, or name the one you want changed.**

### ⭐⭐⭐ `Q38-A` — contextual, or a mode toolbar?

| ⭐⭐⭐ **RECOMMENDED: CONTEXTUAL is the ONLY switch. The toolbar button is a PIN, not a mode.** |
|---|

📌 **`R-95` already made FOCUS the authority** *(`FocusedSurface`, a latch, cross-host)*.
⛔⛔ **A mode toolbar competing with focus re-creates TWO AUTHORITIES over one panel — which is
precisely the `B8` defect** *(a snapshot and a live read disagreeing about who owns the panel)*.
⭐⭐ **The user's "local toolbar" instinct is real and is SERVED BY `F`**: the button freezes the
context instead of overriding it. ⇒ ⭐ **one authority, plus an explicit escape hatch.**
**Blast radius: NONE new** — the mechanism shipped in Batch 87.

### ⭐⭐ `Q38-B` — one panel across perspectives, or one per perspective?

| ⭐⭐⭐ **RECOMMENDED: ONE window CLASS · ONE INSTANCE PER PERSPECTIVE · FEEDS REGISTERED PER HOST.** |
|---|

📐 **That is already how this editor works** — every window is built by `PerspectiveWorkspaceRegistrar`
with an `owningPerspective` and a suffixed id *(`ai_my_blueprint_{suffix}`)*.
⛔ **A single global instance would fight the docking layout** and make "which perspective am I in"
invisible. ⭐ **And the FEED registry already exists** — `RuntimeInspectorWindow.RegisterPane`.
**Blast radius: MEDIUM** — ⚠ `BlueprintDetailsWindow` stops being a window and becomes a feed.

### ⭐⭐ `Q38-C` — what about views that are not "properties"?

| ⭐⭐⭐ **RECOMMENDED: a surface stays STANDALONE only if it answers a DIFFERENT QUESTION — never merely a different ASSET TYPE.** |
|---|

⭐ **The test, in one line:** *does it answer **"tell me about the thing I selected"**?*
⭐⭐ **Byte-budget / bin-packing answers *"will this layout FIT?"*** — ⛔ a question about the layout
being authored, not about a selection ⇒ **`BlackboardAuthoringWindow`'s layout view STAYS.**
⚠ **Everything split by asset TYPE folds** — that split is the defect, not a feature.
📌 Consistent with the existing ruling *("bin-packing… is a genuinely different job")* and with
*"no rush removals"*. **Blast radius: LOW** — ⭐ it is a criterion, so it needs no list to be right.

### ⭐⭐⭐ `Q38-D` — runtime vs authoring: one panel or two?

| ⭐⭐⭐ **RECOMMENDED: ONE. And the SHELL that survives is `RuntimeInspectorWindow`'s, not `BlueprintDetailsWindow`.** |
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

| ⭐⭐⭐ **RECOMMENDED: ANSWER NOW, BUILD AFTER the post-Batch-88 visual check passes** *(`R-27`)*. |
|---|

⭐ **Then in this order, each step independently revertible:**

| # | step | why here |
|---|---|---|
| **1** | **collapse the TWO watch windows into one** *(`R-72`, `BP-330`)* | ⭐ smallest, and it removes a duplicate **before** anything is folded onto it |
| **2** | **generalise the shell** — feeds keyed by asset kind **and** by question | ⛔ no feed moves yet |
| **3** | **move feeds ONE AT A TIME**, re-checking visually after each | 📌 *"do not merge surfaces nobody has seen"* — ⚠ **already paid for once** |
| **4** | **retire the duplicates LAST** *(`U-16`/row 60, the second `InspectorWindow`)* | ⛔ **never before its replacement is proven** — 📌 row 60's own rule |

### ⭐⭐ `Q38-F` — the pin *(NEW)*

| ⭐⭐⭐ **RECOMMENDED: a pinned inspector is the SAME CLASS with a FROZEN context source. Not a new window type.** |
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
