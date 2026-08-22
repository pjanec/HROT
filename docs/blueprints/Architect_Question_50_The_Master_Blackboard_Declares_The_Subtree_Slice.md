<!--STATUS
state: LIVE
build-state: DESIGN (OPEN architect question — resolve with the user; not yet buildable)
updated: 2026-08-22
current-answer: the whole file — does the MASTER blackboard DECLARE the auto-allocated sub-tree slice
  `{SubtreeName}_{DtoTypeName}`, and who SIZES it? This is BP-342 gap (2), and it is the LAST thing
  blocking BP-399's S4 (details.parametersync) and therefore S5. Options A/B/C with a recommended lean.
design-basis: BP-342 gap (2) · Architect_Question_49 (gap (1), approved + built as option C 2026-08-22) ·
  R-99 ("promoting an inert panel is worse than leaving it buried") ·
  DESIGN_Details_Panel_View_Switching.md §7.6 ④ ⑤ · Q45 (the source generator owns the sidecar).
known-conflict: none.
-->
# Architect Question 50 — **does the master blackboard declare the sub-tree slice, and who sizes it?**

> 🔴 **In one line:** Approach-B's emitted body writes `ref master.{SubtreeName}_{DtoTypeName}` — and
> **no blackboard emitter declares that field**. ⇒ the orchestrator references a member of the master
> blackboard struct that does not exist.

## Why this is the last blocker

⭐ **`Q49` closed gap ①** *(the sub-tree IDENTITY is now recomputed at load — built `2026-08-22`,
option C)*. ⛔ **`BP-342` gap ② is untouched by it**, and `BP-342` says so in its own words:
*"Widening the DTO would NOT be sufficient while ② stands, which is why it was not attempted."*

⇒ **`S4`** *(promote `details.parametersync` to the Details panel)* stays deferred under **`R-99`** —
*"promoting an inert panel is worse than leaving it buried"* — and **`S5`** *(retire `InspectorWindow`)*
is blocked on `S4`. ⭐ **This question is the whole remainder of `BP-399`.**

## INVENTORY — measured 2026-08-22

| symbol | where | role |
|---|---|---|
| the emitted write | `BTreeOrchestratorEmitCore:165` — `ref var subDto = ref master.{sliceField};` | 🔴 **references the field that does not exist** |
| **`GetAutoAllocatedVariables()`** | `BehaviorTreeAsset:768` | produces `BlackboardVariableEntry($"{SubtreeName}_{DtoTypeName}", typeof(object))` — ⛔ carries its own `DEBT`: *"real type resolution requires catalog integration"* |
| its **only** consumer | `BlackboardAuthoringWindow:529` | ⛔ **DISPLAY-ONLY**, greyed as *"(size unknown until build)"* |
| ⛔ **the gap** | — | the entry **never enters `_blackboardVariables`**, never reaches `Blackboard.Variables`, and **no blackboard emitter declares it** |
| **`StructSizeResolver.Resolve(typeId, compilation)`** | `Hrot.AiEditor.Generators/StructSizeResolver.cs:101` | ⭐ **the generator CAN size a type from the compilation** — so *"size unknown until build"* is true only in the EDITOR |
| **`SubtreeSyncIdentity`** | `Hrot.AiEditor.Persistence/Emit/` *(new, `Q49`)* | ⭐ gives both arms the sub-asset's real DTO type name + namespace ⇒ **the `typeof(object)` placeholder is no longer forced** |

⭐⭐ **The load-bearing consequence of that last row:** `GetAutoAllocatedVariables`'s `DEBT` said the real
type *"requires catalog integration"* — ⭐ **`Q49` IS that catalog integration.** ⇒ half of gap ② is
already dissolved; what remains is genuinely a **layout** decision, not a resolution one.

## ⛔⛔ Why this cannot be quietly built

📐 If the slice is not declared and the generator emits Approach-B anyway, **the generated code does not
compile** the moment a designer creates a sync binding — 📌 exactly `BP-306`'s shape *(the BTree action
generator emitting non-compiling code)*. ⇒ ⭐ **`Q49`'s option D** *(the generator-side catalog)* is
deliberately **NOT wired** until this question is answered; building it now would arm that landmine.

## The options

| # | option | how | ⭐ pro | ⛔ con / blast radius |
|---|---|---|---|---|
| **A** | ⭐⭐⭐ **DECLARE it — the auto-allocated entry joins the real variable set** | `GetAutoAllocatedVariables()`'s entries flow into `_blackboardVariables` *(or are unioned at emit)*, typed from the sub-asset's real DTO type *(`Q49`'s catalog)*; the generator sizes it with `StructSizeResolver` | ⭐ the emitted `ref master.X` becomes TRUE; ⭐ the byte budget stops lying *("size unknown")*; ⭐ one concept, declared once | ⛔ **changes the master blackboard LAYOUT** ⇒ offsets, `StructureHash`, the byte budget, and possibly goldens move for any asset with a sync binding. ⚠ **Today: zero corpus assets have one**, so the measured blast radius is **nil now** and real later |
| **B** | **PROJECT instead of declare** — the orchestrator reads the slice out of an existing partition rather than a named field | no new field; the body changes shape | ⭐ no layout change | ⛔ **invents a second addressing scheme** beside Approach A's aliases ⇒ 📌 ruling 9; and the slice still needs a home and a size |
| **C** | ⛔ **REMOVE Approach B** — delete the copy-in/copy-out path and keep only Approach A *(whole-DTO aliasing)* | delete the emit arm, the panel, the bindings | ⭐ removes the whole inert surface, unblocks `S5` immediately | ⛔⛔ **deletes a designed capability** — 📌 the `2026-08-15` rule; the panel, the DTO field, the emit core and its 7 rails were all built deliberately. ⚠ And it does not answer *"how does a subtree get parameters"*, it abandons the question |

## ⭐ Recommended lean — **A**

⭐⭐ **The field the emitter writes should exist.** The alternative reading — *"the emitter is wrong to
reference it"* — is not supported by the record: `GetAutoAllocatedVariables` **computes exactly that
name**, deliberately, and its display-only consumer shows it to designers as a real thing. ⇒ ⛔ the
defect is that **nothing carries it the last step**, not that the step is wrong.

⭐⭐⭐ **And the two reasons it was deferred have both expired:**

| the old reason | ⭐ now |
|---|---|
| *"real type resolution requires catalog integration"* | ✅ **`Q49` delivered exactly that** — the sub-asset's real DTO type is available in both arms |
| *"size unknown until build"* | ✅ true in the EDITOR, ⛔ **false in the generator** — `StructSizeResolver.Resolve` sizes it from the compilation, which is where the struct is actually emitted |

⚠ **The honest cost, stated:** this **moves the master blackboard layout** for any asset that has a sync
binding. 📐 **Measured today: no corpus asset has one**, so ⭐ **the change is byte-identical on the
current corpus** and the risk is entirely about future assets — which is the cheapest possible moment to
make it.

⇒ ⭐ **Sequenced as:** `A` → `Q49`'s **option D** *(the generator catalog, now safe to wire)* → **`S4`**
*(promote `details.parametersync`; the panel is no longer inert)* → **`S5`** *(retire
`InspectorWindow`)* ⇒ **`BP-399` closes.**

## To resolve

⭐ **One decision from the user: approve A, or name B/C.**
⚠ **The one measurement the build session runs first if A is approved:** *does adding an
auto-allocated entry move `StructureHash` or any golden on the current corpus?* — expected **no**
*(no asset has a sync binding)*, and it is a gate row either way, ⛔ not an assumption.

📌 **Owner when resolved:** the UI / BTree-editor lane. Tracked against **`BP-342` gap ②**.
