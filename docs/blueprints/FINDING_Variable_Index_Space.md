# FINDING — the variable index space is not a latent defect. It is an **unenforced invariant**

> **Coordinator, 2026-08-11**, at the user's request to think it through before implementing.
>
> ⚠ **This corrects [Q27](Architect_Question_27_Local_Variables.md)'s ground-truth section and
> [Batch 37](batches/HANDOFF_Batch37_Local_Variables.md) §6**, both of which called it *"a latent defect,
> `BP-224`'s shape."* ⭐ **Batch 37 §6 has since been amended** — the user confirmed on `2026-08-12`
> that no implementation run had picked the handoff up, so rule 1 did not bite. Its instruction *"file
> it, do not fix it here"* is unchanged; the row's content and severity are what moved. This note is
> also the input to Batch 38's fix.

---

## The mechanism, restated

| | |
|---|---|
| `Stage5.FindVariableIndex` (`:4498`) | searches `Variables` → `WorkingState` → `Parameters`, returning the index **within whichever list matched** |
| `EmissionContext.VarFieldName` (`:55`) | reads that integer as a **priority-ordered union**: `Variables` first, then `WorkingState`, else `__var_{index}` |

⇒ They disagree about what the integer means. **On paper**, a `WorkingState` entry at index *j* resolves
to `Variables[j]` whenever `Variables.Count > j`, and a `Parameters` entry resolves to **neither list** —
`VarFieldName` does not know Parameters exist.

---

## ⭐ Why it cannot currently fire — two independent structural facts

### 1. The lists are disjoint **by dispatch kind**, not by luck

Measured across the shipped corpus (`Hrot.AI.Behaviors/Assets/Blueprints/*.bp.json`):

| Dispatch | `Variables` | `WorkingState` | `Parameters` | assets |
|---|---|---|---|---:|
| **Instance** | ✅ | ❌ never | ❌ never | 13 |
| **AiPrimitive** | ❌ never | ✅ sometimes | ✅ sometimes | 23 |
| **Library** | ❌ | ❌ | ❌ | 2 |
| ⚠ `Dispatch: 1` *(numeric, not a string)* | ❌ | ✅ sometimes | ✅ sometimes | 4 |

⇒ `Variables` and `WorkingState` are **never both populated**, because **they are the storage model of
different dispatch kinds** — Instance keeps state in `Variables`; AiPrimitive keeps it in
`WorkingState` + `Parameters`. ⭐ **Where `Variables.Count == 0`, `VarFieldName`'s first branch cannot
fire, so `WorkingState` resolves correctly.**

### 2. ⛔ The editor can only author a `Variables` target — ⚠ **THIS LEG IS WRONG, see below**

`BlueprintPickerSources` (`:148-152`) — the Get/SetVariable picker — queries **`_asset.Variables` and
nothing else**, so *today's picker* cannot aim a node at a `WorkingState` field or a `Parameter`.

⛔⛔ **But "the picker cannot author it" is not "none exist", and I wrote it as though it were.**
⭐ **Corrected `2026-08-12` by the implementation session's measurement, re-verified by me:**

| | 152 `VariableId` refs across 42 assets |
|---|---:|
| → `Parameters` | **0** ✅ *the one claim that held* |
| → `WorkingState` | ⚠⚠ **57** |
| → `Variables` | 32 |
| → neither *(mostly `"state"`, a different mechanism)* | 63 |

⇒ ⭐ **Leg 2 does not hold. Only leg 1 does.** Those 57 references resolve correctly **only** because
their AiPrimitive assets happen to have `Variables.Count == 0` — one `Variables` entry added to any of
them silently mis-resolves every `WorkingState` read in that asset. **The severity below is written
against the old, wrong picture; `BP-226` carries the corrected one.**

---

## ⚠ So the earlier framing was wrong, and the correction matters

**`BP-224`'s shape was:** *a discriminator that is correct only because one of its cases never occurs* —
wrong from the day it was written, harmless until collapse made macros real.

⭐ **This is not that.** Here the cases **cannot** occur given the storage model and the picker's scope.
The code is not accidentally right; it is right **under an invariant that happens to hold**.

### ⛔⛔ CORRECTION `2026-08-13` — **the invariant IS enforced. This note said it was not, and that was wrong**

> ⚠⚠ **Third correction to this note, and the biggest.** It claimed *"nothing asserts an Instance asset
> has no `WorkingState`, or an AiPrimitive none in `Variables`."* ⭐ **`Stage2_Validate` asserts exactly
> that, in both directions, and has all along:**
>
> | | |
> |---|---|
> | **`BP1024`** | *"AiPrimitive uses 'parameters' and 'workingState', **not 'variables'**."* |
> | **`BP1031`** | *"Instance uses 'variables', **not 'parameters'/'workingState'**."* |
> | **`BP1011`** | *"Library asset must not declare member variables."* |
>
> ⇒ ⭐ **`Variables` and `WorkingState` cannot both be populated in an authored asset — that is a rail,
> not a coincidence.** ⛔ **`BP-226`'s row repeats the wrong claim** (it was written from this note) and
> **needs the same correction.**

⇒ **What actually remains, now that the disjointness is accounted for:**

| | |
|---|---|
| 🔴 ⭐ **`Parameters` is the real hole, and it is NOT covered by those rails** | An AiPrimitive legitimately populates **`WorkingState` AND `Parameters` together** — `BP1024` only excludes `Variables`. ⚠ **That pair is not disjoint and never was**, and `Parameters` is **absent from `VarFieldName` entirely**, so that path has **no** correct answer. **This is the row's real content** |
| ⚠ ⭐ **The rails run at STAGE 2 — lowering runs after them** | `AiPrimitiveLowering` appends `__phase` at Stage 6, unchecked. ⇒ **anything that adds storage during lowering bypasses `BP1024`/`BP1031` entirely.** 📌 **This is precisely the Batch 38 trap**: parking graph locals in `WorkingState` during lowering would not exploit a gap, it would **go around a rail** |
| ⛔ Nothing stops a future picker from offering `WorkingState` or `Parameters` | it is one `Query` away — and `BP1024`/`BP1031` would **not** catch it, because the lists would still be legal for that dispatch kind |
| ⭐ **Someone has already been bitten by the shape — TWICE** | (1) `AiPrimitiveLowering:42-66` **appends** `__phase` rather than prepending, commented *"would shift every real field by +1, so `VarFieldName` would emit the WRONG field for every WorkingState access."* (2) ⭐ **`Stage5.FindParameterIndex` is a params-ONLY lookup**, existing because the combined index *"would silently emit the wrong field … whenever Variables/WorkingState are non-empty."* **Two independent authors routed around this. The workarounds are the evidence** |

---

## 📐 What to do about it — recommendation for Batch 38

⛔ **Not a rewrite of the index space.** Nothing is broken, and a refactor of a working resolution path
carries more risk than the invariant does.

⭐ **Make the invariant explicit and self-guarding**, in rough order of value:

| | |
|---|---|
| **1** | ⭐ **Return a `(storage-kind, index)` pair from `FindVariableIndex`** instead of a bare `int`, and have `VarFieldName` switch on the kind. **The ambiguity then cannot be expressed** — the cheapest permanent fix, and it is a type change, not a logic change |
| **2** | ⛔ ~~assert the disjointness where the asset is validated~~ — **`BP1024`/`BP1031`/`BP1011` already do.** ⭐ **Struck `2026-08-13`; the item was written from the wrong premise.** ⚠ What is missing is that the rails are **Stage 2** and lowering runs later, so a Stage 6 append is unchecked |
| **3** | 🔴 ⭐ **Give `Parameters` a correct branch in `VarFieldName`, or make that path throw** — ⭐ **now the FIRST item, not the third.** `WorkingState` + `Parameters` coexist legally in every AiPrimitive, so this is the one pair no rail separates. Today it silently returns `__var_{index}` — ⚠ a name that exists nowhere, i.e. a `CS0103` in generated code rather than a diagnostic |
| **4** | 📌 Note the four `Dispatch: 1` assets — a **numeric** dispatch where every other asset has a string. Unrelated to this, worth its own look |

⚠ **Locals do not touch any of this** (Q27-D: they get their own op), so this is **not** a prerequisite
for `BP-57`. ⭐ Batch 37's *"file it, do not fix it here"* stands — this note only changes what the row
should say.

---

## 📌 One instruction in Batch 37 §6 is now answered — and the handoff now says so

It asked: *"check whether `Parameters` already makes it live."* ⭐ **It does not** — the picker cannot
author such a node and there are zero in the corpus. ⭐ **§6 has been amended to state the answer rather
than pose the question**, and to ask for independent confirmation instead. ⚠ **If the implementation
session measures otherwise, their measurement wins over this note.**
