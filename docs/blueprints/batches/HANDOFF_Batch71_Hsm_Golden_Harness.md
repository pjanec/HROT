# HANDOFF — Batch 71: **give Track E a floor** — `E0` · backfill `E1`/`E2` · `E6`/`W9` · `E7b`

> 📌 **Dispatched at `aefb2f39f`.** Frozen per rule 1 *(rule 1a: re-dispatch only while this sha is NOT
> in your history)*. ✅ **Batch 70 MERGED at `0b2b55380`** — gates re-run by me, `StructureHash` and
> `persistence-shape` unchanged, and I verified the 17 moved snapshots are **purely additive**.
> ⭐ **Rule 7 / Rule 4.** ⛔ **Rule 3: the coordinator allocates no ids.**
> ⭐ **One commit per item · per-item STOP conditions.**
>
> ⭐⭐⭐ **The ORDER inside this batch is the point.** Item 1 builds the gate; item 3 is the first change
> that needs it. ⛔ **Do not reorder them** — `E6` landing before `E0` is exactly the hole `E0` exists
> to close.

---

## 0. ⭐⭐⭐ Batch 70 — **your `BP1031` call is ACCEPTED, and here is why it was the right shape**

| | |
|---|---|
| ⭐⭐⭐ **retiring `BP1031` was RIGHT, and reporting-then-deciding was right** | ⭐ **The decision was inside the item**: the alternative was never *"seam without retirement"*, it was *"no seam"*. ⭐⭐ **And you retired it in the shape that survives review** — kept **defined**, listed **`RETIRED`** in the ratchet, and the positive test **inverted** rather than deleted, asserting **no error of any code** *(stronger than the row it replaced)* |
| ⭐⭐ **`DEBT-AIB-021` was two defects and a third guard** | ⛔ **fixing (a) alone would have shipped nothing** for defaultless assets. 📌 **And a test had written defect (b) down as INTENT** — *"a test asserting the absence of a feature is indistinguishable from a test asserting a bug."* ⭐ **That is the `.dev/` lesson arriving from the opposite direction**, and it is now in the plan |
| ⭐ **"ask the artefact, not the thing that produced it" — third instance** | Batch 68 counted methods not call sites · Batch 69 scanned a signature not the object · Batch 70 read an expectation out of the field under test. ⭐⭐ **You found all three by PROBING, not by review** — keep the probes |
| ⚠ **`DEBT-AIB-030` widens** | 📐 **my run reddened `StatelessGizmoRegistryTests.SC_GZ022_2_…`** — green in isolation, counts **2 → 1 → 1** on an unchanged tree. ⇒ ⭐ **the cause is process-global registry state GENERALLY, not the AI registries.** Nothing to fix here; **expect it and confirm with `--filter`** |

---

## 1. 🔴🔴 `E0` — **the HSM golden harness.** ⭐ *The prerequisite the whole of Track E has been missing*

> 📄 **Plan §4B, "Track E has NO golden coverage"** — ⛔ **`persistence-shape.txt` is 43 assets, ALL
> `.bp.json`.** ⇒ **`E1`, `E3` and `E6` change emitted output and no golden gate would notice.**
> ⚠ **`BP-240`'s shape inverted: green because the corpus does not contain the thing.**

### ⭐ Measured for you — **the harness to mirror, and how little there is to seed it with**

| | measured `2026-08-17` |
|---|---|
| ⭐⭐ **the instrument to copy** | `Hrot.Blueprints.Tests/Golden/PersistenceShapeTests.cs` + `Snapshots/Golden/persistence-shape.txt` — **one line per asset: `name  SHA256(canonical)  length`** ⭐ **and read its own doc comment first**: it explains why a *recorded baseline* beats `Serialize(Deserialize(j)) == j` *(round-tripping is **closed under a leak**)* |
| **the emitted-source half** | `Snapshots/Golden/Emit/*.cs.txt` — **43 files, stored text not hashes**, on the rule *"a hash names the asset; a stored file names the LINE"* |
| 🔴 **the HSM corpus is TWO assets** | `HsmShowcase.hsm.json` · `SampleGuard.hsm.json` — ⛔ **that is the whole shipped set** |
| ⚠ **BTree has 26** | `Assets/BTrees/*.btree.json` — ⭐ **also ungated**, and the same harness shape would take them |

### What to build

| | |
|---|---|
| ⭐ **the shape baseline** | an HSM `persistence-shape`-equivalent over the shipped `.hsm.json` corpus, **same file format**, ⭐ **same reasoning recorded in the test's doc comment** *(do not restate it — cite it)* |
| ⭐⭐ **the emitted-output baseline** | ⛔ **this is the half that matters for `E6`** — the shape file cannot see a key change, only emitted text can. ⭐ **Stored text, mirroring `Snapshots/Golden/Emit`** |
| ⭐⭐⭐ **seed it past two assets** | ⛔ **two shipped assets cannot cover `E1`/`E2`'s features.** ⭐ **Add purpose-built corpus assets** covering: a `Role=State` variable · a `Role=Input` variable · **orthogonal regions** *(`E3`'s subject, so the gate exists before the fix)* · two same-simple-named actions in different types *(`E6`'s subject)*. ⚠ **Purpose-built corpus assets are corpus, not fixtures** — they live with the corpus and are regenerated with it |
| ⭐ **generic if it is free** | 📐 **measure whether the harness can be written over "asset kind" rather than "HSM"**, so BTree's 26 seed later without a rewrite. ⛔ **Do NOT seed BTree in this batch** — ⭐ **but say in one line whether the generalisation cost anything**, because that answer decides whether BTree coverage is a line item or a leftover |

### ⭐⭐ Backfill `E1` and `E2` into it — **this line is where that gets written down**

> 📄 Plan §4B: *"they shipped under unit tests only, and this line is where that is written down."*

⛔ **Not "add a test"** — ⭐ **the backfill is that their emitted output is now IN the baseline**, so a
future change to slot manifests or provisioning moves a golden file instead of passing quietly.

### 🔴 STOP conditions

| | |
|---|---|
| 🔴🔴 **the blueprint corpus MUST NOT MOVE** | ⭐ **43 assets, `persistence-shape` unchanged, the 43 `Emit/*.cs.txt` unchanged.** ⛔ **This item ADDS a corpus; it touches none of the existing one.** A move means you refactored shared harness code — **STOP and report** |
| ⚠ **if the HSM emitter is not deterministic** | *(dictionary order, timestamps, absolute paths)* ⛔ **a golden gate over non-deterministic output is worse than none** — it trains everyone to regenerate. ⭐ **If you find non-determinism, THAT is the finding**: report it and gate only what is stable |

**rails:** ⭐ **the gate must be able to FAIL** — mutate one HSM asset / one emitted key and show the
baseline reddens *(⛔ a green new gate proves nothing; this is the "ask the artefact" lesson applied to
a gate)* · the baseline regenerates deterministically twice in a row.

---

## 2. ⭐ `E6` / `W9` — **the simple-name hash.** ⛔ **AFTER item 1, in a later commit**

📐 **Measured:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/HsmActionGenerator.cs` — `ComputeHash(action.Name)`
at **`:517`** and **`:630`**, with the guard peers at `:528` / `:636`, plus compound forms at
`:642`/`:655`/`:660`. ⭐ **`MethodInfo` carries `FullName` as well as `Name`.**

| | |
|---|---|
| ⛔ **the defect** | two actions with the **same simple name in different types** collide on one id |
| ⚠ **TWO re-bake sites, not one** | blob key **and** thunk key — 📄 plan §4d: *"reconciled in lockstep via shared `ResolveStatefulSlotKey`"* ⇒ ⭐ **one shared resolver, called twice.** ⛔ **Two call sites each computing "the same" key is the duplication that produced this** |
| ⭐ **the rail is pre-written** | 📄 **plan §4B rails, `E6`**: *"two actions with the same simple name in different types get distinct ids, and **both re-bake sites agree**"* |

🔴 **This CHANGES emitted output** ⇒ ⭐⭐ **item 1's baseline will move, and that is the point.**
⛔ **Regenerate it deliberately, in this commit, and show the diff is only ids** — the same discipline
you applied to the 17 snapshots last batch.

---

## 3. `E7b` — **the OUTPUT binding.** ⭐ *Independent; it can land any time in this batch*

> ⛔ **`ExpressionTargetField` is an OUTPUT binding** — *"blackboard field that receives the expression
> **result** of `ActionFunction`"* — and **both hosts already have it** (BTree per node, HSM per
> transition). 📄 Plan §4d. ⚠ **`FIX-01-REPORT:43`'s "no per-node" meant per-NODE**, not "absent".

| | measured |
|---|---|
| 🔴 **the counting hole** | `Hrot.Hsm.Editor/Model/HsmAsset.cs:257` — *"Returns 0; HSM does not use `ExpressionTargetField` in this phase"*, `CountNodesReferencingVariable(name) => 0` |
| ⛔ **the consequence** | `BlueprintLocalVariableSchemaSource:135` computes `IsUnused: Count… == 0` ⇒ ⭐⭐ **a variable written through `ExpressionTargetField` reads as UNUSED.** ⚠ **Trap #5 again — a member reporting success while doing nothing**; the blueprint side's own doc comment already names it |
| ⭐ **it is authored and persisted today** | `HsmAssetMapper:114/135` round-trips it · `HsmCommandSink:249` maintains it · **`HsmValidator:394` already reads it as a writer style** ⇒ ⭐ **the authoring half is done; the runtime and the count are not** |

**rail — pre-written:** 📄 plan §4B, `E7b`: *"`CountNodesReferencingVariable` is **non-zero** for a field
bound through `ExpressionTargetField`."* ⭐ **Plus the runtime half: the named variable actually receives
the expression result** — assert the bytes, not the binding.

🔴 **STOP:** if wiring the runtime half needs `E3`'s occurrence key *(a transition's write racing across
regions)*, ⭐ **do the COUNT half and say so** — partial is fine here and the ordering is real.

---

## 4. ⛔ NOT in this batch

**`E3`** *(occurrence in the action key — after `E0`, its own item)* · **`E5`** *(needs `-028`(a):
`StateNode.SubtreeAssetId` is not persisted)* · **`E7a`** *(⛔ `IHostVariableAccess` stays declared-only;
it keeps receiving `null`)* · ⛔⛔ **blueprint multi-occurrence — BLOCKED on
📄 [`Architect_Question_34`](Architect_Question_34_Blueprint_Occurrence_Identity.md)**, which I owe the
user · the `InspectorWindow` "STATIC PARAMETERS" retirement · the Track C **visual check** ·
⛔ **seeding BTree into the new harness** *(measure the cost, do not do it)*.

---

## 5. Gates

**Baseline — coordinator-verified at `0b2b55380`:** build **0 / 69** · Blueprints **3690 / 3680 / 0 / 10** ·
AiShared **1280** · BTree.Editor **615** · Breakpoints **134** · Generators **208** · Hsm.Editor **531** ·
AiEditor.Persistence **136** · Toolkits **1964** · NodeEdit **208 / 131** · tracker **open 61 / done 153**.

| | |
|---|---|
| ⭐ **add any suite the diff reaches** | this batch reaches **Hsm.Editor**, **Generators**, **AiEditor.Persistence** and the analyzer's own suite if it has one |
| ⭐⭐ **`Fdp.Toolkits.Tests`** | ⛔ **a full-suite red is not signal by itself; a green is not evidence either** — `DEBT-AIB-030`, now **four** distinct tests. ⭐ **Confirm any red with `--filter` and say which test** |
| 🔴🔴 **the BLUEPRINT golden set MUST NOT MOVE** | `persistence-shape.txt` · the 43 `Emit/*.cs.txt` · `StructureHash`. ⭐ **The NEW HSM baseline is expected to appear in item 1 and MOVE in item 2** — ⛔ **say which files moved in which commit** |
| **per-item revert-goes-red** · `tracker-counts.py --check` · ⚠ **the two NodeEdit gates take NO `--no-build`** | |

---

## 6. Reporting

⭐⭐ **The gate table — one row per gate, verbatim command, result.**

**Per item:**
⭐⭐ **item 1** — ⭐ **can the new gate FAIL?** *(show the mutation that reddens it — a new green gate is
not evidence)* · **what you seeded beyond the two shipped assets, and why those features** ·
⭐ **did generalising over asset kind cost anything?** *(one line — it decides whether BTree's 26 are a
line item)* · **any HSM emitter non-determinism you found**.
⭐ **item 2** — **both re-bake sites reconciled through ONE resolver** · ⭐ **the baseline diff is only
ids**, shown.
⭐ **item 3** — whether the runtime half needed `E3`, and what you did about it.
**Always:** ⭐ **the blueprint golden set unchanged, stated FIRST** · **every id you allocated** ·
⭐ **which `DEBT-AIB` rows this batch touched** *(I expect `-029` to come up in `E7b`'s neighbourhood —
the direct-children-only walk)*.

⭐⭐⭐ **The standing ask, and it has now paid four batches running:** when a premise of mine fails,
**STOP and report it.** ⭐ **Batch 70 went further and DECIDED one** *(`BP1031`)* — that was right
because the decision was inside the item. ⚠ **The line stays where it is: decide what the item cannot
proceed without; escalate what changes the plan.**
