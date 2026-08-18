# HANDOFF — Batch 85: **collapse `WorkingState` and `Variable` into ONE kind**

> 📌 **Dispatched at `42b428d82`.** ⭐ **Branch from it** *(rule 7)*.
> ⛔⛔ **YOUR SCOPE IS FROZEN AT THIS SHA.** ⭐ Documents changing after it are **FYI ONLY**.
> ⚠ **If a later document INVALIDATES an item — STOP AND REPORT. ⛔ Do NOT adapt, do NOT revert.**
> ⭐ **Rule 3: allocate your own ids.** ⭐ **Rule 1b: push `chore: started batch 85 at <sha>` FIRST.**
>
> 🔴🔴 **THIS IS THE MOST DANGEROUS BATCH THIS PROGRAMME HAS RUN.** ⛔ **It can wipe every deployed
> blackboard** *(`R-24`)*. ⭐⭐ **ONE item. Red-first. A moved `StructureHash` is a STOP, not a
> regeneration.** ⚠ **Landing NOTHING and reporting why is a better outcome than landing it wrong.**

---

## 1. ⭐⭐⭐ THE INVESTIGATION — **run by the coordinator `2026-08-18`, before writing this**

> ⭐⭐ **New methodology** *(user ruling, `2026-08-18`)*: no batch without an enumeration of the code
> surface **and** the non-superseded design record, both with the queries shown. ⛔ **Do not re-run
> these to confirm them — ⭐ re-run any you intend to RELY on.**

### INVENTORY — the code surface

```
search_graph(name_pattern="^(DeclarationKind|VariableKind|VariableRef|DeclarationView|BlueprintDeclaration)$",
             include_connected=true)                                   → total 7
grep -rn "DeclarationKind.WorkingState" --include=*.cs Hrot/ | grep -v Tests   → 22 sites
```

| 📐 measured | |
|---|---|
| `DeclarationKind` | **in-degree 44** — `Parameter`, `WorkingState`, `Variable` |
| `VariableKind` *(IR)* | **in-degree 13** — `Unresolved=0`, `Variable`, `WorkingState`, `Parameter` |
| ⭐⭐ **`VariableRef`** | *"which list, and the position **within that list**"* ⇒ **a LIST-RELATIVE index** |
| the 22 sites | ⭐ **most immediately `.Concat` `WorkingState` with `Variables`** *(`Stage0:441`, `Stage2:2107`, `Stage5:4164/4181`)* ⇒ **the code already treats them as one set, verbosely** |

### ⭐⭐⭐ The finding that DE-RISKS this batch — **references are BY ID, not by index**

```
$ python3 -c "…"   # over a shipped .bp.json
  VariableRef: 0 occurrences   VariableKind: 0 occurrences   VariableId: 6 occurrences
```

⇒ ⭐⭐ **`VariableRef` is COMPILE-TIME IR ONLY.** Graphs reference declarations **by `Guid`**, and
Stage 5 re-resolves id → (kind, index) **on every compile.**
⛔ **So `BP-244`'s warning — *"collapsing them would invalidate every baked reference"* — is about the
IR, which is REBUILT.** ⭐ **Nothing persisted carries a kind-relative index.**

### ⚠ What is still genuinely dangerous

| 🔴 | |
|---|---|
| **`KindOrder` feeds the layout** | `DeclarationList.KindOrder` = `Parameter, WorkingState, Variable` — ⭐ **and `R-61`: that is also `StructureHashComputation`'s append order** ⇒ **reorder = moved offsets = `R-24`'s wipe** |
| **458 persisted assets say `"Kind": "WorkingState"`** | a read-compat or migration question |
| **`R-09` hazards** | synthesized fields *(`__phase`, `__waitUntilTime`)* are `(State, Asset)` and **never declared**; **shared state** has **61 refs / 8 assets** declared nowhere |

### ⭐⭐ The design record — non-superseded, checked

| source | says |
|---|---|
| **`R-01`** | *"`Variable` ≡ `WorkingState`. Two names, ONE concept."* Identical `(Role=State, Scope=Asset)`; only `Dispatch` differs |
| **`R-02`** *(user)* | *"it makes no sense to emit them differently"* |
| **`R-08`** | ⛔ **`Parameter` STAYS SEPARATE** — different shape, written once at behaviour assignment |
| **`R-24`** | 🔴🔴 **field order must be preserved or every deployed blackboard is wiped** |
| **`Variable_Model_Unification` §4** | `D3` *(consumers off the old views)* · `D4` *(rails restated, old views deleted)* |

---

## 2. 🔴🔴 ITEM 1 — **MEASURE FIRST, and the measurement may STOP the batch**

### `M-12` — **does ANY asset declare BOTH kinds?**

📌 **Batch 56 (`BP-244`) measured over all 458 shipped `.bp.json`:**
**193** `(Variable)` · **32** `(Parameter, WorkingState)` · **7** `(Parameter)` · **5** `(WorkingState)` ·
**221** none · ⭐⭐⭐ **0 with BOTH.**

> ⭐⭐ **RE-MEASURE IT. Do not trust the number.** ⛔ **It is two batches old, and this whole programme's
> worst failures came from trusting a recorded measurement.**

| outcome | ⭐ what it means |
|---|---|
| ✅ **still 0 with both** | ⭐⭐ **no shipped asset can have its field order changed by the merge** — the group is homogeneous, so concatenating cannot reorder anything. **Proceed** |
| 🔴 **any asset has both** | ⛔⛔ **STOP AND REPORT.** ⭐ The merge order for that asset decides whether its blackboard survives, and **that is a decision for the user, not this batch** |

⛔ **Report the census either way, with the command.**

---

## 3. 🛠 ITEM 1 — **the collapse** *(only if `M-12` is ✅)*

### ⭐ The target

**`DeclarationKind` becomes `{ Parameter, Variable }`.** ⭐ `WorkingState` is **retired as a kind**.
⛔ **`Parameter` is untouched** *(`R-08`)*.

### ⭐⭐⭐ The invariant that must hold, and how to prove it

> 🔴🔴 **`StructureHash` MUST NOT MOVE FOR ANY OF THE 458 ASSETS.**

⭐⭐ **Order preservation is the whole job:** where `KindOrder` walked `WorkingState` **then** `Variable`,
the merged kind must yield **exactly that sequence**. ⭐ **The `WorkingStateOrder` / `VariableOrder`
lists already persist per-group order** — ⛔ **use them; do not sort, do not re-derive.**

⭐ **Rail it directly:** compute `StructureHash` for every shipped asset **before and after** and assert
**byte-identical**. ⛔ **Not "the goldens didn't move" — the HASHES, computed, compared.**

### ⭐ Read compatibility

⭐⭐ **Accept `"Kind": "WorkingState"` on READ, forever** — ⛔ **it is not a migration, it is an alias.**
⇒ ⛔ **do NOT rewrite the 458 assets in this batch**: that is 458 files of golden movement for zero
behavioural gain, and it destroys the *"zero golden movement"* signal that makes this batch auditable.
⭐ Assets adopt the new spelling naturally when next saved.

### ⭐ What else must change

| | |
|---|---|
| **the 22 `Of(DeclarationKind.WorkingState)` sites** | ⭐ **most already `.Concat` with `Variables`** — those collapse to a single `Of(Variable)`. ⚠ **Read each; do not sed** |
| **`VariableKind` (IR)** | ⭐ collapse the same way. ⛔ **`Unresolved = 0` STAYS** — 📌 its own comment: had `Variable` been `0`, a forgotten assignment would silently mean `Variables[0]` |
| **`DeclarationView<T>`** | ⭐ `asset.WorkingState` and `asset.Variables` become **the same view**. ⚠ **Keep both properties** *(`D4` deletes them, not this batch)* — ⛔ but they must not double-count |
| **`BlueprintAsset.WorkingStateOrder` / `VariableOrder`** | ⛔ **KEEP BOTH.** They are the order evidence `R-24` depends on |

### ⛔ Out of scope

⛔ **the UI sections** *(My Blueprint still showing two)* — ⭐ **that follows from the kind, and it is a
SEPARATE batch**: this one must be provably behaviour-neutral.
⛔ **`D4`'s deletion of the old views** · ⛔ **`R-09`'s undeclared synthesized fields and shared state** —
⭐ **note whether the merge makes either worse, and STOP if it does.**

---

## 4. ⭐ Gates — **the rule-8 contract, all seven rows** · ⭐⭐ **plus two specific to this batch**

| # | report |
|---|---|
| **1** | verbatim command · pass/fail/skip · **Δ vs baseline** |
| **2** | ⭐⭐ the **`--no-build` column** — ⛔ `NodeEditor.Core`, `NodeEditor.UI`, `Fhsm.Tests` take **NO** `--no-build` |
| **3** | ⭐⭐⭐ **golden movement as a DIFF SHAPE** |
| **4** | every RED confirmed pre-existing against the base sha, named |
| **5** | working tree CLEAN after every suite run |
| **6** | both quarantine counts — ⛔ a new skip is a finding |
| **7** | `tracker-counts.py --check` · `rulings-check.py` · every id allocated |
| ⭐⭐ **8** | 🔴🔴 **`StructureHash` computed BEFORE and AFTER for all 458 assets — byte-identical, stated as a count** |
| ⭐⭐ **9** | **the `M-12` census, re-run, with its command** |

⭐ **Baseline** *(Batch 84)*: build **0 err** · AiShared **1397** · Blueprints **3772/3782/10** ·
BTree.Editor **615** · Hsm.Editor **551** · Generators **270** · Breakpoints **143** · Persistence
**136** · Hrot.Editor **194** · Scenarios **56/68 (12 skipped)** · UrbanCombat **29** · Toolkits
**1964** · NodeEditor.Core **211** · NodeEditor.UI **135** · FastHSM **300** · tracker **open 68 /
done 197** · rulings **44/44**.

⚠ **`persistence-shape.txt` and the 43 `Emit/*.cs.txt` goldens MUST NOT MOVE.** ⛔ **If one does, that
is the wipe hazard showing itself — STOP, report the diff, change nothing else.**

---

## 5. ⭐⭐ If you must stop

⭐ **Stopping is a good outcome here.** ⛔ **Report:** the `M-12` census · which of the 22 sites you had
converted · the `StructureHash` comparison · and **what you did NOT touch.**
⚠ **Do not leave a half-collapsed enum** — ⭐ **either the kind is merged and the hashes are identical,
or the tree is exactly as you found it.**
