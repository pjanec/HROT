<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-22
current-answer: dispatch pointer for the UI lane — L6, the entity-context views (Components + Mission
  plan) on the Scenario perspective, staged. THE DESIGN is DESIGN_Details_Panel_View_Switching.md §6 L6
  (re-staged 2026-08-22) + §5 + the L6 sequence; build from it. Diagrams live in the design, not here.
known-conflict: none.
-->
# HANDOFF — UI lane · **L6: the entity views (Components + Mission plan) on Scenario**

> 📌 **Dispatched at `<DISPATCH_SHA>`.** ⭐ Branch from it *(rule 7)*; **rule 1b: started-marker FIRST.**
> ⭐ Lane: UI / variable *(`claude/hrot-implementation-j1jvin`)* — ids **`BP-`**, tracker `A`–`G`. **Rule 3: your own ids.**
> ⛔ **Scope FROZEN at this sha.** Documents that change after it are FYI only.

## 0. ⛔⛔ READ THE DESIGN FIRST — **it holds the plan and the UML**

📄 [`DESIGN_Details_Panel_View_Switching.md`](../DESIGN_Details_Panel_View_Switching.md) — read **§6 `L6`**
*(the re-staged table, `2026-08-22`)*, **§5** *(what the registrar is + the as-built note)*, **§2** *(the
`classDiagram`)* and the **`L6` sequenceDiagram** in §6. ⭐ **Obligation ③:** check what you build against
those and report match/deviation; **obligation ⑤:** if you deviate, fold the as-built back into the design
before you close, marking the prior state superseded.

⚠⚠ **Three as-built facts the design now states — do not re-derive them the hard way** *(§6 L6 table)*:
**(a)** `DetailsContext.Entities` **already carries the selected entity** *(`L0.4`)* — you need descriptors
that READ it, not a new context arm. **(b)** the **Scenario perspective has NO details host at all** —
that is the real work. **(c)** **"brain-equipped" is not a real check** — write it fresh from
`GetMissionSnapshot`/`GetAvailableBehaviors` returning empty.

## 1. ⭐⭐⭐ THE ORDER — **build in stages; a STAGE GATE after the enabling refactor**

⭐ Each item is independently gated. ⛔ **Do not reorder;** the enabling refactor must be green before the
host, and the host before the views. 📄 Full detail per item is in **§6 `L6`** — this is the dispatch order.

| # | item | one line | gate |
|---|---|---|---|
| **1** | ⭐⭐ **`L6.1a` — extract `PerspectiveWorkspace`** | split the registrar's GENERIC half *(registry · `LiveContextSource` builder · entity source · `IDetailsViewSource` claim chain)* from its 21-param AI service bag *(§5)*. ⛔ **pure refactor of the 3 AI perspectives — no behaviour change** | ⛔⛔ **STAGE GATE: BTree/HSM/Blueprint each still host their SAME offer set** *(rail the offer sets unchanged)*. Do not start item 2 until green |
| **2** | ⭐⭐ **`L6.1c` — Scenario gets a host** | stand up a `PerspectiveWorkspace` + `DetailsWindow` on the Scenario perspective from **scenario** services *(not the AI bag)*; wire `WorldEntitySelectionSource` into its context builder so `ctx.Entities` flows there. ⛔ **do NOT rename the persisted key** *(that is `L6.1b`, deferred)* | Scenario shows a details panel; a selected entity yields a non-empty `ctx.Entities` |
| **3** | ⭐⭐ **`L6.5` — the predicate helper** *(before `L6.4`)* | `ctx.Entities is [{ }]` + the brain signal, so each entity view is a one-line predicate | the helper's predicates rail true/false over measured contexts |
| **4** | ⭐⭐⭐ **`L6.3` — Components view** | an **adapter in the composition root** *(`Hrot.Editor`/`Scenario/` — the only assembly seeing both `Fdp.Presentation`'s `EntityInspectorPanel` and `AiShared`'s `IDetailsViewSource`; §3's reference wall)* wrapping `EntityInspectorPanel`. ⚠ **it OWNS selection via a `HashSet`** — feed it `ctx.Entities`, DELETE the `HashSet`, re-point `EntityInspectorPanelMultiSelectTests` at the World | offer set on an entity context includes Components; it renders the selected entity's components |
| **5** | ⭐⭐⭐ **`L6.4` — Mission plan view** | adapter *(same comp-root rule)* wrapping `MissionPanel`; set its `SelectedEntityId` from `ctx.Entities[0]`; predicate = `L6.5`'s brain signal | offer set on a brain-equipped entity includes Mission; empty otherwise |

⛔⛔ **DEFERRED — NOT this batch:** `L6.1b` *(persisted-key rename `"Editor"`→`"Scenario"` + layout
migration — silently resets saved layouts; its own gated task)*. ⛔ **Also not this batch:** `BP-399`
*(L3's remaining AI-authoring views)* · `DerEntityInspectorPanel` *(IOS/ExCon)*.

## 2. ⭐ WHY THE STAGE GATE MATTERS

📌 `L6.1a` touches the registrar that **three** perspectives depend on. ⛔ Piling the Scenario host and two
new views on top of an unverified extraction is how a structural refactor's blast radius hides. ⭐ Gate the
extraction on the **unchanged** offer sets first — if BTree/HSM/Blueprint still host exactly what they did,
the generic half moved cleanly. ⚠ **If `L6.1a` proves larger than one batch, STOP and report** *(`R-106`:
stop THAT item, not the batch — items already green stay)*; we split it rather than rush it.

## 3. ⭐ LANE & GATES

⛔ No time-lane file. ⭐ Expect to touch: `Hrot.Editor.AiShared/Shell` + `Windows` *(the extraction)* ·
`Hrot.Editor`/`Scenario/` *(the Scenario host + the two comp-root adapters)* · `Fdp.Presentation`
*(`EntityInspectorPanel`'s `HashSet` deletion + its tests)* · `EditorSubsystem` *(the Scenario wiring)*.
⭐ Gate contract *(rule 8)*: one row per gate · command · pass/fail/skip · delta · goldens as a diff shape ·
`tracker-counts.py --check` · `rulings-check.py` · the **`BP-` ids you allocated** · `R-106` verdicts ·
⭐⭐ **the STAGE-GATE result** *(the three AI offer sets unchanged)*. ⭐ **The new rail:** *selecting an
entity on Scenario offers Components (and Mission when brain-equipped); the panels render the selected
entity.* ⭐ Rule 4/7: re-sync + pull the coordinator branch around the batch.
