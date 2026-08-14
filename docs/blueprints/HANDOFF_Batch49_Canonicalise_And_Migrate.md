# HANDOFF — Batch 49: ⚠⚠ **`U-15` + `U-10` — the risky one. Canonicalise, then migrate**

> 📌 **Dispatched at `<STAMP>`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7:** branch from this branch, re-sync at the **start** of your run.
> ⭐ **Rule 4:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP1672+` is the next free diagnostic.
>
> 📄 [PLAN_Variable_Unification_Tasks.md](PLAN_Variable_Unification_Tasks.md) §2 · `U-15`, `U-10` — **D2**.
> ⚠⚠ **The plan calls `U-10` *"the risky one"* and it is the only batch in the programme whose
> ⛔ REVERT IS CODE IT SHIPS.** `git revert` does not undo a migration; **the down-migrator is the
> revert**, and it must ship and be tested **here**.

---

## 0. ⛔⛔ Two things that make this batch different from every one before it

| | |
|---|---|
| ⭐⭐ **`U-15` is the FIRST task that deliberately changes shipped files** | every batch since 44 has ended *"golden unchanged."* ⛔ **This one rewrites assets on purpose** — and ⭐ **the golden harness is precisely what proves the rewrite is a SEMANTIC NO-OP** |
| ⭐⭐ **`U-10` cannot be reverted by git** | ⛔ **a migrated file stays migrated.** ⇒ **the down-migrator IS the revert**, it ships in this batch, and it is tested here or it does not exist |

⚖️ **If the batch runs long, `U-15` alone is the clean stop** — ⛔ **never `U-10` without its
down-migrator.**

---

## 1. `U-15` — canonicalise the corpus

### 1.1 Why it exists — `V1`, measured at the plan review

⛔ **`U-10`'s original byte-identity gate was UNWRITABLE.** **0 of 58** shipped files survive even
`Deserialize → Serialize`: **41 of 42 are hand-authored 2-space-indented** and `BlueprintJsonServices`
sets `WriteIndented = false`.

⇒ ⭐ **Canonicalise first, and `v1 → v2 → v1` byte-identity becomes the strongest gate available.**

### 1.2 ⚠⚠ SCOPE — decide it explicitly, because the numbers disagree

**Coordinator-counted today, `.bp.json` outside `bin`/`obj`:**

| where | count | guarded by golden? |
|---|---|---|
| ⭐ `Hrot.AI.Behaviors/Assets/Blueprints` — **the corpus** | **42** | ✅ **yes, both tiers** |
| ⚠ `Hrot.AI.Behaviors/Recipes/Blueprints` | **16** | ⛔ **NO** — `Content`, never compiled by the generator |
| 📌 `Hrot.Blueprints.Tests/TestAssets` (+`Recipes`, `Invalid`) | **37** | ⛔ no — **fixtures** |
| 📌 `Comparison/Fixtures` (both suites) | **4** | ⛔ no — **fixtures** |

⭐ **`42 + 16 = 58`, which is the review's number.** 📐 **Rule the scope and say which:**

| | |
|---|---|
| ⚖️ **the lean: 42 + 16** | ⭐ **the corpus is proven a no-op by the golden harness; the 16 recipes are NOT.** ⇒ **if you canonicalise them, say what proves it** — a round-trip assertion per file is the minimum, and ⚠ **`BP-227`'s numeric `Dispatch` lives partly in there** |
| **42 only** | ⭐ safest, ⛔ **but `U-10` then migrates recipe files that were never canonicalised**, and V1's problem returns for exactly those |
| ⛔ **the 41 fixtures** | ⚠ **almost certainly NOT** — several are deliberately malformed (`Invalid/`), and a fixture's bytes are often the thing under test. **If you touch any, name it and why** |

### 1.3 Gates

| | |
|---|---|
| ⭐⭐ **Tier 1 UNCHANGED — `StructureHash` and every struct field** | ⛔ **this is the whole claim.** A canonicalisation that moves Tier 1 is not a canonicalisation |
| ⚠ **Tier 2 may move ONLY if the input JSON changed meaninglessly** | 📐 **and it should not move at all** — the compiler reads a model, not bytes. ⭐ **If Tier 2 moves, that is a finding, not a rebase** |
| ⭐ **`persistence-shape.txt` WILL move — deliberately** | ⭐⭐ **it is the baseline Batch 48 recorded on the pre-`U-9` tree.** ⇒ **regenerate it, and say in the commit that you did and why** — ⛔ **a silent regeneration of the gate that guards persistence is the one regeneration nobody can audit later** |
| ✅ **`BP-227` settles here** | the numeric `Dispatch` normalises to the string. 📌 **7 files** — 4 corpus + 3 recipes |

---

## 2. `U-10` — migrator pair + envelope `1 → 2` *(D2)*

| | |
|---|---|
| ⭐⭐ **Pass 1** | **`v1 → v2 → v1` is the IDENTITY, byte for byte** ⚠ **only meaningful AFTER `U-15`** |
| ✅ **Pass 2** | a **v1** file loads through the **v2** reader |
| 🔴🔴 **Pass 3** | ⭐⭐ **`StructureHash` unchanged for EVERY shipped asset.** ⛔ **This is the no-blackboard-wipe gate — a failure here resets every deployed entity's state** |
| ✅ **Pass 4** | ⭐ answered by `U-15` — asserted there |
| 🔴 **Revert** | ⛔ **`git revert` does not work. The down-migrator IS the revert** — it ships and is tested in this task |

### ⭐ What `U-9` left you, and it changes `U-10`'s shape

⚠⚠ **Batch 48 built the INVERSE of the plan:** the tagged declaration is a **view**; ⭐ **the three
lists are still the STORAGE**, and the serializer still writes v1 byte for byte.

⇒ 📐 **`U-10` is therefore the store flip AND the envelope, or just the envelope. Decide and say
which.** ⚖️ **The lean is: envelope + migrator only.** ⛔ **A store flip belongs with `U-12`, after
`U-11` has moved the ~34 consumers** — otherwise this batch carries both the riskiest gate in the
programme *and* a live rewrite of what every consumer reads.

⭐ **If you disagree, say so before building it** — this is the one place in the plan where the
sequencing was written before `U-9`'s direction was known.

---

## 3. Gates

**Baseline — coordinator-run on the merged Batch-48 tree (`c890620f`):**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| Blueprints | **3491 total / 3481 passed / 0 failed / 10 skipped** |
| ⭐ **AiShared 1216** · BTree **612** · Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** | ⛔ **none should move** |
| ⭐⭐ **Golden Tier 1** | ⛔ **UNCHANGED — non-negotiable** |
| ⭐ **Golden Tier 2 · `persistence-shape.txt`** | 📌 **may move; each regeneration must be stated and justified** |
| `tracker-counts.py --check` | clean **seventeen** batches running |

⭐ **Run the five `--no-build` suites in parallel; keep `\[FAIL\]` in the grep.**
⚠⚠ **The two NodeEdit gates take NO `--no-build`.**

---

## 4. ⚡ How to work

**You are on Opus, and ⛔ all of it stays there.** ⭐ **Persistence migration is the highest-blast-radius
work in the programme** — `Pass 3` failing in production wipes every deployed entity's blackboard.
🟢 **Sonnet fits only the mechanical re-serialisation sweep in `U-15`**, ⛔ **never `U-10`.**

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | the tracker is yours — ⭐ **`BP-227` closes with `U-15`** |
| ⚠⚠ **Stop point** | ⭐⭐ **`U-15` alone.** ⛔ **Never ship `U-10` without its down-migrator tested** |

---

## 5. Reporting

Per-suite numbers · ⭐⭐ **Tier 1 unchanged, stated explicitly** · ⭐⭐ **every baseline you regenerated
and why** · ⭐ **your §1.2 scope ruling and what proves the unguarded files are safe** ·
⭐⭐ **your §2 store-flip ruling** · `tracker-counts.py --check` · ⭐ **every id you allocated** ·
⭐ **where you stopped** · anything here **wrong against the code**.

⭐⭐ **Batch 48's best move was refusing a gate rather than a claim:** it showed by probe that my
round-trip test **could not see the failure it was written for**, and replaced it with a recorded
baseline. ⚠ **That is the exact risk in this batch, amplified:** `U-15` and `U-10` are both changes
whose *"nothing broke"* is easy to assert and hard to prove.

⛔ **So for each gate here, answer the same question Batch 48 answered: WHAT EDIT would redden this,
and did you run it?** ⭐ **A migration gate that has never failed is a migration gate nobody has
tested.**
