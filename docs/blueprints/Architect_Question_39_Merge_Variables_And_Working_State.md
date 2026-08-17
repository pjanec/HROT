# Architect Question #39 — **should the outline show ONE section for `Variable` + `WorkingState`?**

> ⭐⭐⭐ **Raised by the user, `2026-08-17`, from the first visual check:** *"regarding the 'Variable' and
> 'Working state' sections, **the goal was to unify these two as they are the same thing**, or do they
> mean something else?"*
>
> ⭐⭐ **The user's recollection is CORRECT, and it is their own prior ruling.** ⛔ **This question is
> therefore NOT "are they the same" — that is settled. It is "how far does the unification reach."**
>
> ⭐ **Number taken across ALL active branches** *(rule 3a)*: `#33`–`#38` exist, ⇒ **`#39` is free**.

---

## 1. ⭐⭐⭐ What is already RULED and already BUILT

| | |
|---|---|
| ⭐⭐⭐ **`Q32` ruling 8 — the user, verbatim** | *"as the global vars and working state vars are **the same stuff**, it makes no sense to emit them differently"* ⇒ **`Q32-E`, decided: UNIFY** |
| ⭐⭐ **the measurement that made it safe** | 📐 **458 shipped assets: `0` with BOTH `Variable` and `WorkingState`.** ⇒ **`Variable ∪ WorkingState` = the single populated list, same order, for all 58 that declare anything** |
| ✅ **and it SHIPPED** | 📐 `IrAsset.cs:95-96` unions them *(`Variables` first, then `WorkingState`)* · `VariableRef.cs:38` documents the asset-level union with ⛔ **no `Parameters` arm** · `EmissionContext.cs:59` names the same order |
| ⛔ **the bound that was deliberately set** | ⭐ *"**Unify what the emitters WALK, not what they are CALLED**"* — `State` / `WorkingState` / `Params` are **ABI**, and renaming them was called *"a separate, larger change nobody asked for"* |

⇒ ⭐⭐⭐ **The compiler treats them as one concept TODAY. The authoring outline still shows two
sections.** ⚠ **That gap is not a decision anyone made — it is a side effect**, and §2 explains how.

---

## 2. 📌 How the two sections got there — ⛔ **not by disagreeing with ruling 8**

⭐ **The `C-sections` work was solving a DIFFERENT problem**, and its own comment says so:

> 📌 **`BlueprintMyBlueprintModel.cs:74-80`, verbatim:** *"🔴 **Parameters and working state were not
> shown in My Blueprint AT ALL**: `BuildVariableItems()` listed only `DeclarationKind.Variable`, so an
> AiPrimitive — **32 of the shipped assets are `(Parameter, WorkingState)`** — presented a designer with
> an **EMPTY Variables section** and no way to see, rename or delete anything it actually declares."*

⇒ ⭐ **It made the invisible visible, by appending one section per `DeclarationKind`.** ⛔ **Merging was
never considered** — the batch's job was visibility, and ruling 8 was about the emitters.
⚠ **So the outline encodes a distinction the compiler has already collapsed.**

---

## 3. ⛔ What is NOT the same — **`Inputs` must stay separate**

⚠ **Do not let "they are the same stuff" swallow the third kind.** 📐 **Measured:**

| | backing type | struct / offset | semantics |
|---|---|---|---|
| **Inputs** | ⛔⛔ **`ParameterDecl` — A DIFFERENT SHAPE**: lacks `IsEditable`, `IsExposedOnSpawn`, `Category` *(enumerated in `MembersAParameterDoesNotCarry`, asserted by reflection)* | `Params` **@ 0** | ⭐ **written ONCE at behavior assignment** |
| **Working State** | `VariableDecl` | `WorkingState` **@ 8** | freely mutable |
| **Variables** | ⭐ **`VariableDecl` — the SAME type** | `State` **@ 16** | freely mutable |

⇒ ⭐⭐ **`Variable` and `WorkingState` differ by a TAG and an offset. `Parameter` differs by TYPE and by
LIFECYCLE.** ⛔ **And the IR union already excludes it** — *"no `Parameters` arm"*.

---

## 4. ⭐ The sub-questions

| | question | ⭐ Claude's lean | ⚠ what makes it hard |
|---|---|---|---|
| **`Q39-A`** | **Does the outline show ONE section for `Variable` + `WorkingState`?** | ⭐⭐ **YES.** The compiler already does; showing two teaches a distinction that no longer exists, and the user has now hit it as confusion | ⚠ **`Inputs` stays** — three sections become two |
| ⭐⭐⭐ **`Q39-B`** | 🔴 **Which `DeclarationKind` does the merged `[+]` CREATE?** | ⭐⭐ **Follow the asset's DISPATCH KIND** — AiPrimitive ⇒ `WorkingState`, Instance ⇒ `Variable` | ⛔⛔ **THE DANGEROUS ONE — see §5** |
| **`Q39-C`** | **What is the merged section CALLED?** | ⭐ **`Variables`** — the name a designer already uses; ⛔ *"Working State"* is an implementation word | ⚠ the ABI struct names are untouched either way *(ruling 8's bound)* |
| **`Q39-D`** | **Does this reach BTree/HSM?** | ⛔ **NO — different classification entirely.** 📐 The AI outline sections are `Role`+`Scope` *(`Inputs`/`Working State`/`Asset Globals`)*, ⚠ **not `DeclarationKind`** — ⭐ **the shared NAME "Working State" across the two is a COINCIDENCE worth checking, not evidence** | ⭐ **flag it: two unrelated things wear one label** |
| **`Q39-E`** | **Does the STORED kind of existing declarations change?** | ⛔⛔ **NO. Presentation only.** ⭐ A migration would move `StructureHash` and the golden corpus | ⭐ **the gate ruling 8 set still applies** |

---

## 5. 🔴🔴 `Q39-B` is the one that can BREAK something

⭐⭐⭐ **Ruling 8's safety rests on a measured invariant: `0` of 458 assets populate BOTH lists.**

⇒ ⛔⛔ **A merged `[+]` that picks the wrong kind would create the FIRST asset that does** — and the
union is **order-sensitive** *(`Variables` first, then `WorkingState`)*:

> 📌 **`AiPrimitiveLowering.cs:61`:** *"…would emit the **WRONG field** for every `WorkingState` access
> (**off-by-one**) whenever a graph has **BOTH**."*

| ⇒ ⭐ therefore | |
|---|---|
| ⭐⭐ **the merged `[+]` MUST derive the kind from the asset**, never from a default or the section | |
| ⭐⭐⭐ **and a RAIL must pin the invariant itself** — ⛔ **not just the create path.** *"No shipped asset populates both lists"* is now a **load-bearing property**, and ⚠ **nothing asserts it today** | |
| ⚠ **if an asset legitimately needs both**, that is a **layout question**, not a UI one ⇒ **STOP and re-open `Q32-E`** | |

---

## 6. Status

| | |
|---|---|
| **raised** | `2026-08-17`, **by the user**, from visual-check finding **A2** |
| **state** | ⭐ **OPEN — decision-shaped, not scheduled.** ⛔ **Batch 81 is in flight and must not absorb this** |
| ⭐ **settled already** | **that they are the same stuff** *(`Q32` ruling 8)* · **that the emitters are unified** *(shipped)* · **that `Inputs` is different** *(measured, §3)* |
| ⭐ **still open** | **`Q39-A`–`E`** — ⛔ **above all `Q39-B`** |
| ⚠ **relates to** | **`Q38`** *(one mode-switching Details panel)* — ⭐ **but is INDEPENDENT**: this is one section list, not a window merge. ⛔ **Do not block it behind `Q38`** |
| 📌 **also answers** | the plan's open point *"the AiPrimitive-only sections show on EVERY blueprint"* — ⭐ **merging removes half of it by construction** |
