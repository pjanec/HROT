# FINDING — the variable index space is not a latent defect. It is an **unenforced invariant**

> **Coordinator, 2026-08-11**, at the user's request to think it through before implementing.
>
> ⚠ **This corrects [Q27](Architect_Question_27_Local_Variables.md)'s ground-truth section and
> [Batch 37](HANDOFF_Batch37_Local_Variables.md) §6**, both of which call it *"a latent defect,
> `BP-224`'s shape."* ⛔ **Batch 37 is dispatched and frozen (rule 1) — it is NOT amended.** Its
> instruction *"file it, do not fix it here"* **remains correct**; only the row's content and severity
> change. This note is the input to Batch 38.

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

### 2. The editor can only author a `Variables` target

`BlueprintPickerSources` (`:148-152`) — the Get/SetVariable picker — queries **`_asset.Variables` and
nothing else**. ⇒ **A designer cannot produce a `GetVariable` aimed at a `WorkingState` field or a
`Parameter`.**

✅ **Confirmed empirically:** **zero** `Get`/`SetVariable` nodes in the corpus target a `Parameters` id.

---

## ⚠ So the earlier framing was wrong, and the correction matters

**`BP-224`'s shape was:** *a discriminator that is correct only because one of its cases never occurs* —
wrong from the day it was written, harmless until collapse made macros real.

⭐ **This is not that.** Here the cases **cannot** occur given the storage model and the picker's scope.
The code is not accidentally right; it is right **under an invariant that happens to hold**.

⇒ **The defect is that nothing enforces the invariant:**

| | |
|---|---|
| ⛔ Nothing asserts an **Instance** asset has no `WorkingState`, or an **AiPrimitive** none in `Variables` | the model permits both |
| ⛔ Nothing stops a future picker from offering `WorkingState` or `Parameters` | it is one `Query` away |
| ⚠ `Parameters` is **absent from `VarFieldName` entirely** | so that path has **no** correct answer, only unreachable ones |
| ⭐ **Someone has already been bitten by the shape** | `AiPrimitiveLowering:42-66` **appends** `__phase` rather than prepending, with a comment saying prepending *"would shift every real field by +1, so `VarFieldName` would emit the WRONG field for every WorkingState access."* **The workaround is the evidence** |

---

## 📐 What to do about it — recommendation for Batch 38

⛔ **Not a rewrite of the index space.** Nothing is broken, and a refactor of a working resolution path
carries more risk than the invariant does.

⭐ **Make the invariant explicit and self-guarding**, in rough order of value:

| | |
|---|---|
| **1** | ⭐ **Return a `(storage-kind, index)` pair from `FindVariableIndex`** instead of a bare `int`, and have `VarFieldName` switch on the kind. **The ambiguity then cannot be expressed** — the cheapest permanent fix, and it is a type change, not a logic change |
| **2** | If (1) is judged too broad: **assert the disjointness** where the asset is validated, so an asset with both populated fails loud instead of mis-resolving |
| **3** | ⭐ **Give `Parameters` a correct branch in `VarFieldName`, or make that path throw.** Today it silently returns `__var_{index}` — ⚠ a name that exists nowhere, i.e. a `CS0103` in generated code rather than a diagnostic |
| **4** | 📌 Note the four `Dispatch: 1` assets — a **numeric** dispatch where every other asset has a string. Unrelated to this, worth its own look |

⚠ **Locals do not touch any of this** (Q27-D: they get their own op), so this is **not** a prerequisite
for `BP-57`. ⭐ Batch 37's *"file it, do not fix it here"* stands — this note only changes what the row
should say.

---

## 📌 One instruction in Batch 37 §6 is now answered

It asks: *"check whether `Parameters` already makes it live."* ⭐ **It does not** — the picker cannot
author such a node and there are zero in the corpus. ⚠ **The handoff is frozen and stays as written**;
independent confirmation is worth having, and if the implementation session finds otherwise, **their
measurement wins over this note.**
