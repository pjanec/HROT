<!--STATUS
state: LIVE
build-state: BUILT (option A approved by the user 2026-08-22 and shipped, BP-444..447) — WITH A
  LIMIT that RE-MEASUREMENT on 2026-08-22 showed is BIGGER and DIFFERENT than first recorded, and
  which the user has POSTPONED on this record. See "THE LIMIT — re-measured".
updated: 2026-08-22
stale-below: the "## ⛔ HISTORY" section at the foot of this file — it names the WRONG cause for the
  Category-2 limit (resolution, not the byte budget) and proposes a fix that would not have worked.
known-rot: DESIGN_Details_Panel_View_Switching.md §7.6 ④ said S4's bindings "reach the runtime";
  corrected 2026-08-22 — no binding the panel can AUTHOR is emittable yet (see THE REACH).
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

## ✅ BUILT — **option A shipped `2026-08-22`** *(`BP-444`–`BP-447`)*

🔒 **User, `2026-08-22`:** *"i hoped the editor automatically adds the subtree's data, which is likely
the option A."*

### ⭐⭐⭐ The refinement that made A and `Q49`'s D the SAME change

⛔ **The options table described A as *"the entries flow into `_blackboardVariables`"* — an EDITOR-side
change. 📐 Measured, that is the wrong home**, and the right one is smaller:

| ⭐ every input is already PERSISTED | where |
|---|---|
| which fields copy in/out | `BehaviorTreeAssetDto.SubtreeSyncBindings` *(`:354`)* |
| which subtree, and its name | `BTreeSubtreePayloadDto.SubtreeAssetId` + `SubtreeName` *(`:231`)* |
| the callee's blackboard type | ⭐ the **sibling `*.btree.json`** — `Q49` option D's catalog |

⇒ ⭐⭐ **the whole thing is a GENERATOR-side projection over a document**, so ⛔ there is **no editor
involvement and no ordering problem** — nothing must run *"after the catalog is populated."*
⭐ `SubtreeSyncProjection` does **one walk** producing **both** the groups and the slice fields they
require: 📌 ruling 9, and it makes *"a group without its field"* — the non-compiling state — unrepresentable.

### ⭐ What shipped

| | |
|---|---|
| **`SubtreeSyncProjection`** *(persistence, netstandard2.0)* | the one walk; **`SliceFieldName`** is the single composer, and `BTreeOrchestratorEmitCore` now calls it instead of spelling the name out |
| **`GeneratedBTreeSchemaCatalog`** *(`Q49` D)* | `AssetId → (Name, BlackboardTypeName)` from the `*.btree.json` AdditionalTexts the generator **already receives**. ⭐ Unlike the `*.bp.json` precedent it is **not a second parser** — it deserialises through `BTreeJsonServices`, so a schema change cannot desynchronise two readers |
| **`BTreeJsonGenerator`** | declares the slices **before** the blackboard is sized or packed, then passes the real groups to the emit core. ⛔ The *"a generator provably has no groups to pass"* note is gone — both of its reasons are closed |
| **rails** | 7 projection + 3 generator, incl. an **end-to-end two-sibling** rail asserting the declared field IS the field the orchestrator writes |

### ⛔⛔⛔ THE LIMIT — **re-measured `2026-08-22`, and the first description of it was WRONG**

> ⚠⚠ **The prior text of this section is SUPERSEDED and moved to [`## ⛔ HISTORY`](#-history).** ⛔ It named
> *"the callee's blackboard type cannot be resolved in the master's compilation"* as the cause. 📐 **That
> cause never fires on any real asset.** ⭐ The outcome it claimed *(a safe skip)* is right; ⛔ **the
> mechanism, the reach and the remaining work are all different.**

#### ① What the slice's type actually is

📐 `SubtreeSyncProjection:105` types the slice with the callee's **asset-level `BlackboardTypeName`** —
⛔ **not** the generated `{Name}_{Blackboard.TypeName}` struct, and ⛔ not `Blackboard.TypeName`.

| measured over **every** `*.btree.json` in the repo | |
|---|---|
| managed assets *(Category-2)* | **15** |
| distinct asset-level `BlackboardTypeName` among them | ⭐ **ONE** — `Fdp.Toolkit.Behavior.Components.BrainBlackboard` |
| is that type resolvable from the master's compilation? | ✅ **yes** — it is an ordinary referenced toolkit type |

⇒ ⛔⛔ **a Category-2 callee never hits the "cannot be resolved" skip.** ⭐ And it cannot: the callee's own
interpreter is built as `BTreeBuilder<BrainBlackboard, …>` *(`BTreeEmitCore:377`)*, so the **blob is the
blackboard the sub-tick wants** — the generated `{Name}_BrainBlackboard` struct is the *named view* of
what is packed inside it, not the interpreter's parameter type.

#### ② What it hits instead — ⭐⭐ **the byte budget, and this is architectural**

| | |
|---|---|
| `BrainBlackboard` | **128 bytes** *(`BehaviorConstants:19`)* |
| the master's inline budget | **100 bytes** *(`BTreeBlackboardPackHelper:20`)* |

⇒ ⭐⭐⭐ **embedding it as a field ALWAYS overflows** ⇒ the asset is skipped with the *budget* `BTREE0002`,
not the resolution one. ⛔⛔ **So option A's "the master DECLARES the slice as a field" can never hold a
Category-2 callee** — ⚠ **that is a property of the model, not a missing helper**, and ⛔ **the fix this
section previously recorded *(a size fallback mirroring `TryResolveParamsSize`)* would not change it.**

#### ③ ⛔⛔ THE REACH — **the authorable set and the emittable set are DISJOINT**

📐 `ParameterSyncSource.ModelFor:219` offers `subAsset.BlackboardVariables` and refuses with *"Sub-tree has
no blackboard variables."* when empty. 📐 And the corpus splits perfectly: **managed ⟺ has variables**
*(15 with, 11 without, no mixed case)*.

| | callee is **Category-2** *(managed, has variables)* | callee is **Category-1** *(hand-written struct)* |
|---|---|---|
| ⭐ the panel can author a binding | ✅ **yes** — it is the only case it can | ⛔ **no** — refused, no variables to list |
| ⭐ the generator can emit for it | ⛔ **no** — 128 > 100, skipped | ✅ yes *(if the bound names are real fields)* |

⇒ 🔴 **every binding the panel can author today ends in a skipped asset.** ⚠ **This is the honest
statement of `S4`'s status, and it is weaker than the one I wrote in `§7.6 ④`** — the panel authors real
persisted data through a real seam, ⛔ **but no authorable binding reaches the runtime yet.**

#### ④ ⛔ An UNGUARDED defect, independent of all the above — **`BP-451`**

📐 **Nothing validates that a binding's `FieldName` is a member of the callee's blackboard type.** A callee
whose type is resolvable **and** fits the budget **and** lacks the bound names emits
`subDto.{FieldName} = …` ⇒ **CS1061** — 📌 **`BP-306`'s exact shape.**

⚠⚠ **And this repository's own rail is that fixture:** `TheOrchestratorIsGeneratedTests:304` sets
`callee.BlackboardTypeName = "System.Guid"` and binds `Health`. ⭐ It passes because it asserts on generated
**text**, ⛔ never on compilation. ⇒ **unreachable through the UI** *(③ makes the authorable set skip first)*,
⛔ **but reachable by hand-edited JSON**, and a build break is the one outcome worse than a skip.

### ⭐⭐ THE OPEN DESIGN CALL — **where a Category-2 callee's blackboard LIVES during the sub-tick**

⛔ Its variables are packed **inside** the blob; `BrainBlackboard` exposes only `BehaviorParameters` plus
three interrupt bytes ⇒ ⭐ **the copy is a projection, not field assignment.**

| # | route | ⭐ pro | ⛔ con |
|---|---|---|---|
| **A′** | **project, don't embed** — tick the callee against the entity's own `BrainBlackboard` component, copy through `Unsafe.As<BrainBlackboard, {Callee}_{TypeName}>` *(the idiom already at `BTreeOrchestratorEmitCore:142`)* | ⭐ no layout change, no budget problem, existing idiom | ⚠ callee and master then **share one blackboard** ⇒ needs an aliasing / re-entrancy ruling |
| **B′** | **embed the NAMED struct** and cast up to `ref BrainBlackboard` for the Tick | ⭐ fits the budget; sub-state isolated per call site | ⛔⛔ **memory-unsafe** — the interpreter reads 128 bytes off a ~12-byte field. **Not recommended** |
| ⭐ **C′** | **declare the slice `Role = State`** — `Pack:140` and `WouldOverflow:188` **already** exclude State-role variables from the inline layout *and* the budget, sizing them at runtime in the partition tier | ⭐⭐ **reuses a seam built for exactly this class of thing** — a sub-tree's whole blackboard is per-behaviour state, not inline params | ⚠ a State-role variable is not a field of the emitted struct ⇒ `ref master.{slice}` **changes shape**; the emitted body must reach it through the partition accessor |

⭐ **Recommended lean: `C′`.** ⛔ It is not a code detail — it moves the emitted body — so it wants an
explicit nod before anything is built.

### ⭐ POSTPONED — **user, `2026-08-22`, on this record**

🔒 *"is that safely postponable, providing you record it thoroughly as such?"* ⇒ ⭐ **yes, and this section
is that record.** ⚠ **What makes it safe, stated so nobody has to re-derive it:**

| ⭐ | |
|---|---|
| **no silent wrongness** | every failure mode is a **build-time `BTREE0002` + a wholly skipped asset** — ⛔ never a partial emit, never a bad copy at runtime |
| **zero current exposure** | 📐 **no corpus asset has a sync binding** ⇒ nothing regresses by waiting |
| **the panel is not lying** | it writes real persisted data through a real seam; ⛔ what it cannot yet do is *reach the runtime* — ⭐ ③ above, not a UI defect |
| ⚠ **the one carve-out** | ④ / **`BP-451`** is a **defect, not a gap** — postponable only because the UI cannot reach it. ⛔ It should be closed in the next batch that touches this generator, and the rail's `System.Guid` fixture replaced |

### ⭐ What this unblocks

⇒ ⭐ **`S4`/`S5` shipped and stay shipped** — ⛔ but see ③: `S4`'s justification in
`DESIGN_Details_Panel_View_Switching.md` §7.6 ④ has been **corrected**, not withdrawn.

---

## To resolve

⭐ **A/B/C are SETTLED — A was approved and shipped `2026-08-22`.** ⇒ ⛔ **the open decision is no longer
A/B/C**; it is **`A′`/`B′`/`C′`** above *(where a Category-2 callee's blackboard lives during the
sub-tick)*, and the user has **POSTPONED** it on this record.

⚠ **The measurement that WAS owed here is done:** *"does declaring the slice move `StructureHash` or any
golden?"* — 📐 **no**, and now with a reason rather than a hope: **no corpus asset has a sync binding**,
and the one shape that would declare a slice is skipped by the budget before it can.

📌 **Owner when resumed:** the UI / BTree-editor lane. Tracked against **`BP-342` gap ②**, **`BP-451`**
*(the unguarded `FieldName` defect)* and **`BP-452`** *(the `A′`/`B′`/`C′` call)*.

---

## ⛔ HISTORY

⚠ **Superseded `2026-08-22` by *"THE LIMIT — re-measured"* above. ⛔ DO NOT QUOTE THIS AS CURRENT** — it
names a cause that never fires, and its *"the fix is a size fallback"* line points at work that would not
have fixed anything. ⭐ Kept because the *shape* of the reasoning is the thing the correction is against.

> ### ⛔⛔ THE LIMIT THE RAILS FOUND — **and it is not in the options table**
>
> 📐 The slice's type is the **CALLEE's blackboard**. ⇒ ⚠ **when that is a GENERATED (Category-2) struct
> it does not exist in the master's compilation** — 📌 the same wall `GeneratedBlueprintSchemaCatalog`
> exists for: *sibling generators cannot see each other's generated output within one pass.*
>
> ⭐⭐⭐ **The existing validator already handles it correctly, which is why this is safe to ship:** the
> asset is **SKIPPED** with an actionable `BTREE0002` *("managed blackboard variable 'X' has type 'Y'
> which cannot be resolved in the compilation")* — ⛔ **never emitted half-formed.**
>
> | ⭐ so, today | |
> |---|---|
> | callee blackboard **resolvable** *(Category-1, or any referenced type)* | ✅ **works end to end** — railed |
> | callee blackboard **generated** *(Category-2)* | ⚠ asset skipped, loudly. ⭐ The fix is to derive the callee's SHAPE from its JSON — **the blueprint *"Option A"* route**. ⛔ Not built |
> | **also required** | the MASTER blackboard must be **`Managed`** |

⭐⭐ **The one row of it that SURVIVES**, and it was never in doubt: **the master must be `Managed`** — a
Category-1 master is a hand-written struct that cannot gain a field ⇒ no managed master, no groups.

⭐ **Why it was wrong, in one line — 📌 worth keeping as a lesson:** ⛔ **I described a limit from the
rail's synthetic fixture rather than from the corpus.** ⚠ The fixture used an unresolvable type, so
*"unresolvable"* looked like the boundary; ⭐ **15 real assets say the boundary is the byte budget.**
📌 The same shape as `BP-450` *(an absence claimed from a pattern rather than measured)* two items earlier.
