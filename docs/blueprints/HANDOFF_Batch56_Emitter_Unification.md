# HANDOFF — Batch 56: ⭐⭐⭐ **one cell, one emit path** — retiring the `Variable`/`WorkingState` split

> 📌 **Dispatched at `STAMP`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7:** branch from this branch, re-sync at the **start** of your run.
> ⭐ **Rule 4:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP1674+` is the next free diagnostic.
>
> ⛔⛔ **THE VISUAL CHECK IS SUSPENDED BY USER RULING** until this and the Details panel land.
> ⇒ **`U-6`/`U-13`/`U-16` are no longer "blocked on the visual check" — the dependency inverted.**

---

## 0. Where this comes from

⭐ **The user, on reading `Q32`'s draft:** *"as the global vars and working state vars are the same
stuff, it makes no sense to emit them differently, this has to be unified… I need a clean solution,
no keeping two implementations for the same concept."*

📄 **Full ruling: [Architect_Question_32_…_ANSWERS.md](Architect_Question_32_Variable_Details_And_Values_ANSWERS.md).**
⛔ **Read §3 before starting — it is the safety argument for this batch.**

⚠ **A coordinator error is part of this batch's history and is recorded so you do not repeat it:** the
`Q32` draft asked whether `WorkingState` should have initial values *"since it is per-run scratch."*
⛔ **Wrong, and the user caught it.** ⭐ **`AiPrimitiveEmitter:128-133` has been emitting
`dst->{Name} = {DefaultValueCSharp}` from `asset.WorkingState` all along** — code identical to
`InstanceEmitter:178-183` over `asset.Variables`. **Reasoning from the NAME instead of the code.**

---

## 1. ⭐⭐ The defect this closes — and it is live, not theoretical

`U-12` made the mixture **legal at Stage 2**: `BP1024` **retired** ⇒ an AiPrimitive may declare a
`Variable`; `BP1031` **split** ⇒ an Instance may declare `WorkingState`. ⭐ **Correct — one cell,
`(State, Asset)`, as `Stage2_Validate:153` says in words.**

⛔⛔ **But nothing told the emitters:**

| | reads |
|---|---|
| `InstanceEmitter` `:104` `:110` `:164` `:178` `:188` | ⛔ **`asset.Variables` ONLY** |
| `AiPrimitiveEmitter` `:74` `:80` `:128` `:139` | ⛔ **`asset.WorkingState` ONLY** (+ `Parameters`) |
| ⚠ **`Stage5:4137` / `:4154`** | ⭐ **resolves across `Variable` CONCATENATED WITH `WorkingState`** |

⇒ **the halves disagree:**

| a wrong-side declaration that is… | today |
|---|---|
| **referenced** | Stage 5 binds it, the emitter never emits the field ⇒ ⛔ **a Roslyn error naming a field the designer never wrote** — `BP-228`'s shape, a diagnostic in the wrong language |
| **unreferenced** | 🔴🔴 **silently absent at runtime** — declared, initial value authored, **does not exist** |

📌 **Nothing caught it because nothing could:** `Stage2_Validate:172` — *"Measured: 0 of the 23 shipped
Instance assets carry either."* ⭐ **`BP-240`'s shape: a rail was relaxed and the code it was
protecting was not told.**

---

## 2. ⭐ Why this is safe to do in one batch — coordinator-measured

```
declaration-kind combinations, ALL 458 shipped .bp.json:
   193  (Variable)                 ← Instance
    32  (Parameter, WorkingState)  ← AiPrimitive
     7  (Parameter)
     5  (WorkingState)
   221  (none)
   ⭐   0  with BOTH
```

⇒ ⭐⭐ **`Variable ∪ WorkingState` == the single populated list, same order, for every shipped asset.**
⛔ **So `StructureHash` MUST be byte-identical and golden MUST NOT move.** ⭐ **That is exactly what
makes it a real gate: the union is a no-op today and will not be tomorrow.**

⚠ **If Tier 1 moves, the union is not order-preserving and you have found something** — 📐 **stop and
report rather than regenerating.**

---

## 3. Scope

| | |
|---|---|
| ⭐⭐ **Both emitters walk the union** | `Variable ∪ WorkingState`, in the asset's declared order |
| ⛔ **Do NOT rename the structs** | `State` (Instance) and `WorkingState`/`Params` (AiPrimitive) are **ABI**. ⭐ **Unify what they WALK, not what they are CALLED** — renaming is a larger change nobody asked for |
| ⭐ **"all access infrastructure"** — the user's words | 📐 **Sweep for every OTHER place that special-cases one of the two.** `Stage5:4137`/`:4154` already concatenate — ⛔ **find the ones that do not.** ⚠ **`VariableRef.cs:39` names the invariant; `Stage5:4591-4593` splits into three lists; `IrAsset` keeps three.** 📐 **Report the full list you found and which you unified** |
| ⚠ **The IR boundary is an ARCHITECT RULING — do not cross it casually** | `IrAsset`'s three lists are what keep `TickCore`'s signature and the blackboard allocation stable. ⭐ **If unifying the emit path is cleanest with a unified IR list, say so and STOP** — that is a design change, not this batch |
| ⭐ **`Parameter` stays separate** | `(Input, Asset)` — *"a genuinely different thing"*, `Stage2_Validate:168`. ⛔ **Ruling 8 unifies TWO kinds, not three** |

---

## 4. Gates

**Baseline — coordinator-run at `ee4d134ab`, ⭐ green:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| Blueprints | **3572 total / 3562 passed / 0 failed / 10 skipped** |
| AiShared **1216** · BTree **612** · Breakpoints **130** · Generators **193** · NodeEdit **208 / 131** | ⛔ **none should move** |
| 🔴🔴 **Golden Tier 1** | ⛔⛔ **BYTE-IDENTICAL. This is the gate** — §2 says why it must hold |
| ⭐ **Golden Tier 2** | ⛔ **unchanged** — the emitted source should not move either, since the union is a no-op on every shipped asset |
| ⭐⭐ **`persistence-shape.txt`** | ⛔ **UNCHANGED** — this batch touches emit, not persistence |
| `tracker-counts.py --check` | clean **twenty-four** batches running |

### ⭐⭐ The gate the corpus cannot give you — build it

⛔ **Every gate above is green on an unchanged corpus, which is exactly `BP-240`'s trap.**
📐 **Construct the asset the corpus does not contain:**

| fixture | must |
|---|---|
| an **Instance** declaring a `WorkingState`, **referenced** | ⭐ **compile and run**, the field present in `State` |
| an **AiPrimitive** declaring a `Variable`, **referenced** | ⭐ **compile and run** |
| the same, **unreferenced** | ⭐ **the field still exists and is still initialised from its default** — 🔴 **this is the silent case** |
| ⭐ **both kinds, same asset, initial values on both** | **both survive to the struct, in declared order** |
| **name collision across the two kinds** | `BP1673` already refuses — ⛔ **confirm it still does** |

⭐ **Prove each RED before the fix.** ⛔ **A fixture that is green both before and after is testing nothing.**

---

## 5. ⚡ How to work

**Opus, all of it.** ⭐ **The failure mode is a field that moves between structs** — the blackboard-wipe
class this programme has spent twelve batches keeping shut.

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | the tracker is yours — ⭐ **file the §1 defect as a row and close it here** |
| ⛔ **NOT in this batch** | the Details panel, the value column, StructEdit, either write path — ⚠ **and `Hrot.Editor.AiShared` is the CROSS-HOST session's territory (`ANSWERS` §5). Do not touch it** |

---

## 6. Reporting

🔴🔴 **`StructureHash` unchanged for all 42, stated FIRST** · **golden Tier 1 + Tier 2 unchanged** ·
⭐⭐ **the constructed fixtures, and which were RED before** · ⭐ **the full list of places that
special-cased one of the two kinds, and which you unified** · ⭐ **whether the IR boundary held, or
why it could not** · `persistence-shape.txt` unchanged · per-suite numbers **full and filtered** ·
`tracker-counts.py --check` · ⭐ **every id you allocated**.

⭐⭐⭐ **The question to carry, and it is the user's own standard:** *"no keeping two implementations
for the same concept."* 📐 **When you have unified the emitters, say plainly whether TWO implementations
are actually gone — or whether one now calls the other and the concept still has two homes.**
