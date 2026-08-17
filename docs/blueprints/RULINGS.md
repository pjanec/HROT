# ⭐⭐⭐ RULINGS — **the canon. READ THIS FIRST, EVERY SESSION.**

> ⛔⛔ **This file exists because the coordinator kept re-deriving settled decisions from CODE after
> compaction, and steering development on a wrong base.** *(user, `2026-08-17`: "we start over and over
> after compaction, you forget all the design decisions and then steer the development on wrong base
> and act as if you never seen any of that.")*
>
> ⭐⭐ **Code answers *"how it IS."* ⛔ It can NEVER answer *"how it was MEANT to be."*** ⇒ **the second
> question has exactly one source, and it is the design corpus indexed here.**
>
> ⭐ **This is an INDEX, not the truth.** The cited documents are the truth. ⭐⭐ **Every quote below is
> verified verbatim against its source by `scripts/rulings-check.py`** — ⛔ **a rotted quote fails the
> gate**, so this file cannot silently drift.

---

## 0. ⭐⭐⭐ How to use this file

| when | do |
|---|---|
| ⭐⭐⭐ **session start / after compaction** | **READ THIS WHOLE FILE.** It is short on purpose |
| ⭐⭐ **before answering ANY design question** | find the row. ⛔ **If there is no row, SEARCH the corpus before answering** — §4 says where |
| ⭐⭐ **before writing a handoff item or architect question** | ⛔ **cite the design basis PER ITEM**, or write *"searched `<where>`, no design record found"* |
| ⭐ **when you FIND a ruling in the corpus** | ⭐⭐ **ADD A ROW IMMEDIATELY.** ⛔ Every row below was learned the expensive way |

---

## 1. 🔴 THE VARIABLE MODEL — **most-violated area; four wrong turns in one day**

| id | ⭐ the ruling | source |
|---|---|---|
| **R-01** | ⭐⭐⭐ **`Variable` ≡ `WorkingState`. Two names, ONE concept.** Identical `(Role=State, Scope=Asset)`; only `Dispatch` differs, and the tag carries no information `Dispatch` did not already carry | `Variable_Model_Unification.md` §2 |
| **R-02** | ⭐⭐ **User's own words:** *"as the global vars and working state vars are the same stuff, it makes no sense to emit them differently"* ⇒ `Q32-E`: **UNIFY** | `Architect_Question_32_…_ANSWERS.md` |
| **R-03** | ⛔⛔ **The unification is UNFINISHED INFRASTRUCTURE, not a UI question.** Stages **A** ✅ and **C** ✅ shipped; ⛔ **`B` and `D` did NOT** | `Variable_Model_Unification.md` |
| **R-04** | ⭐⭐⭐ **`U-9` was built INVERSE of the plan — the tagged type is the VIEW, the three lists are still the STORAGE.** ⇒ **that is WHY every storage-reading surface still sees three concepts** | `BOOTSTRAP_Cross_Host_Variable_Model.md` |
| **R-05** | ⭐⭐ **Stage `B`: `Variables` does NOT flow through `IVariablesSchemaSource`** — it has a separate path via `BlueprintMyBlueprintModel`. ⭐ **"the parallel implementation to remove"** | `Variable_Model_Unification.md` |
| **R-06** | ⭐ **`Role` IS cross-host** — `BlackboardVariableRole { Input, State }` already ships and is the unified model. ⇒ **BTree/HSM working state ≡ blueprint working state** | `Variable_Model_Unification.md` |
| **R-07** | ⛔⛔ **`Scope` is NOT cross-host** — blueprint `{Asset, Graph}` = **visibility**; AI `{Node, Behavior, Entity}` = **blackboard slot sharing**. `Q-b`: *"No. `Asset` and `Graph`, and stop there"* | `Variable_Model_Unification.md` |
| **R-08** | ⚠ **`Inputs`/`Parameter` IS genuinely different** — `ParameterDecl` is a different shape, written once at behavior assignment, and the IR union has **no `Parameters` arm** | `BlueprintDeclaration.cs`, `VariableRef.cs` |
| **R-09** | ⚠⚠ **Stage `D` hazards:** synthesized fields (`__phase`, `__waitUntilTime`) are `(State, Asset)` but **never declared** ⇒ **they surface in the authoring UI without a marker**; **shared state** has **61 refs / 8 assets** declared nowhere | `Variable_Model_Unification.md` |

## 2. ⭐⭐ SURFACES AND DUPLICATION

| id | ⭐ the ruling | source |
|---|---|---|
| **R-10** | ⭐⭐⭐ **Ruling 9 — the standing constraint over everything:** *"no keeping two implementations for the same concept."* ⭐ **`U-16` is not optional cleanup; it is the acceptance criterion** | `Architect_Question_32_…_ANSWERS.md` |
| **R-11** | ⚠ **Ruling 9's target is BIGGER than `U-16` assumed** — **three** variable surfaces, plus `InspectorWindow` in **two** assemblies | `Architect_Question_32_…_ANSWERS.md` |
| **R-12** | ⭐ **User `2026-08-17`: `VariablesPanelControl` KEEPS drawing for now**; the merge is `Q38`. ⇒ **duplicate SURFACE ≠ duplicate CODE** | `Architect_Question_38_One_Details_Panel.md` |
| **R-13** | ⛔ **"No rush removals"** — say which it is: **duplicate CODE** *(route)* · **duplicate SURFACE** *(usually keep)* · **genuinely dead** *(design record agrees)* | `.claude/CLAUDE.md` |

## 3. ⭐ AUTHORING UI BEHAVIOUR

| id | ⭐ the ruling | source |
|---|---|---|
| **R-14** | ⭐⭐ **A variable's classification is WHERE IT WAS CREATED.** ⛔ **NO `Role`/`Scope` dropdown anywhere — the SECTION is the control** | `DESIGN_Variable_Details_And_Editing.md` §1c |
| **R-15** | ⭐ **An empty section STAYS PRESENT** — *"a section that appears and disappears reads as a broken feature"* | `BlueprintMyBlueprintModel.cs` |
| **R-16** | ⭐ **`Q26-B2`: a refusable `[+]` STAYS and refuses out loud, naming the reason** — ⛔ it does not vanish. ⭐⭐ **`2026-08-17` user refinement: GREY it with a tooltip — greying is not vanishing, and it removes the false expectation** | `BlueprintMyBlueprintModel.cs` + user |
| **R-17** | ⛔⛔ **OVERRULED `2026-08-17`:** the *"quick-add, not a modal"* choice for Inputs/Working State. ⭐ **User: every section's `[+]` opens the SAME dialog** | user, `2026-08-17` |
| **R-18** | ⭐ **Rename lives in the OUTLINE, not the table row menu** — a row is an observation with no asset handle | `Q32` / plan §4C |
| **R-19** | ⭐ **Details is authoring+runtime; Watch is runtime-only.** ⛔ **Do NOT "fix" that into consistency** — ruling 9 forbids two implementations of one concept, not two behaviours of two concepts | `Architect_Question_32_…_ANSWERS.md` |
| **R-20** | ⭐ **Run state governs WRITABILITY, not WHICH surface is shown** | `DESIGN_Variable_Details_And_Editing.md` §5 |

## 4. ⭐⭐ WHERE TO LOOK when there is no row

> ⭐⭐⭐ **USER CORRECTION, `2026-08-17`, verbatim:** *"most designs are in the **docs** folder. in the
> `.dev` those named like 'design' or 'detailed design' describe **what was implemented**."*
> ⇒ ⛔⛔ **`.dev/` is AS-BUILT, not INTENT.** ⚠ **I previously listed `.dev/*-DESIGN.md` as an intent
> source — that was WRONG**, and it is the same error as reading code: it tells you *how it is*.

| # | look | it tells you |
|---|---|---|
| ① | ⭐⭐⭐ **`docs/**` — `Architect_Question_*_ANSWERS.md`** | ⭐ **THE RULINGS.** ⛔ the non-`ANSWERS` files carry only options |
| ② | ⭐⭐ **their §"Sequencing" tables** | ⛔ **a finding with a planned batch is NOT a new finding** |
| ③ | ⭐⭐ **`docs/` — `DESIGN_*.md`, `*_Unification.md`, `BOOTSTRAP_*.md`, `PLAN_*.md`** | ⭐ **THE INTENT — the model as it is MEANT to be** |
| ④ | ⚠ **`.dev/<programme>/*-DESIGN.md`, `*_Detailed_Design.md`** | ⛔⛔ **AS-BUILT — what WAS IMPLEMENTED.** ⭐ Useful for *"why is it like this"*, ⛔ **never for *"what should it be"*** |
| ⑤ | `.dev/**/reports/*-REPORT.md` tails · `TASK-DETAIL.md` | **the DEBT** *(`DEBT-*` ids are filed here and nowhere else)* · the authorising user decision |
| ⛔ | `batches/*-INSTRUCTIONS.md`, `reviews/*` | **least useful — they restate the design** |

⚠⚠ **The trap this correction closes:** ⛔ **an as-built document AGREES WITH THE CODE by
construction.** ⭐ **Citing one to justify a design position proves nothing** — it is code-reasoning
wearing a design document's name.

## 5. ⛔ MY OWN CORRECTIONS — **do not repeat these**

| ⛔ what I claimed | ✅ the truth |
|---|---|
| *"Working State `[+]` opening no dialog is not a defect — it is deliberate"* | ⛔ **overruled.** Its premise *("renamable in place")* was false, and consistency outranks the saving |
| *"the BTree/HSM `Working State` name is a COINCIDENCE"* | ⛔ **wrong. `Role` is genuinely shared** — only `Scope` differs |
| *"`Q39` is: should the outline merge two sections?"* | ⛔ **wrong framing** — it is **infrastructure**, stages `B`+`D` |
| *"rename the three `Variables` windows"* | ⚠ **incomplete** — the design says **retire** *(`U-16`)*; the rename is an **interim** the user authorised |
| *"`E3` is a signature widening" / "the dangerous case" / "`E5`'s dependency is stale"* | ⛔ **wrong 4×** — the **params BASE** is what collides |

---

<!-- MACHINE-CHECKABLE PROBES — id | file | verbatim substring that MUST exist in that file -->
```probes
R-01 | docs/blueprints/Variable_Model_Unification.md | occupy the SAME cell
R-01b | docs/blueprints/Variable_Model_Unification.md | names, one concept
R-02 | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | it makes no sense to emit them differently
R-04 | docs/blueprints/BOOTSTRAP_Cross_Host_Variable_Model.md | the tagged type is the VIEW, the three lists are still the STORAGE
R-05 | docs/blueprints/Variable_Model_Unification.md | That is the parallel implementation to remove
R-06 | docs/blueprints/Variable_Model_Unification.md | BlackboardVariableRole
R-07 | docs/blueprints/Variable_Model_Unification.md | and stop there
R-09 | docs/blueprints/Variable_Model_Unification.md | they surface in the authoring UI
R-10 | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | no keeping two implementations for the same concept
R-10b | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | not optional cleanup; it is the acceptance criterion
R-11 | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | THREE surfaces that show
R-15 | Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Windows/BlueprintMyBlueprintModel.cs | reads as a broken feature
R-16 | Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Windows/BlueprintMyBlueprintModel.cs | REFUSES OUT LOUD
R-19 | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | not two behaviours of two different concepts
```
