# HANDOFF — Batch 53: ⭐⭐ **the STORE FLIP — finish `U-12`. One item**

> 📌 **Dispatched at `<STAMP>`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7:** branch from this branch, re-sync at the **start** of your run.
> ⭐ **Rule 4:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP1674+` is the next free diagnostic
> *(coordinator-verified: `BP1673` was allocated last batch)*.
>
> 📄 [DESIGN_U12_Rails.md](DESIGN_U12_Rails.md) §4 — **your own statement of what this is.**

---

## 0. ⭐ One item, and your framing of it is the brief

⭐⭐ **Quoting you, because it is a better statement than the handoff's was:**

> *"`Pass 5` demands `persistence-shape.txt` unchanged, so the three properties must stop being
> **storage** while remaining **the serialized shape** — serialization-only projections over the
> tagged store. Different work, different revert story, and the one gate whose failure re-initialises
> every deployed entity's blackboard."*

⇒ ⛔ **Nothing else is in this batch.** ⚠ **`U-10`'s wiring is Batch 54** — that is where the on-disk
shape is allowed to change, and it must not creep forward.

---

## 1. What flips, and what deliberately does not

| | |
|---|---|
| ⭐⭐ **FLIPS** | `BlueprintAsset.Parameters` / `.WorkingState` / `.Variables` stop being the store and become **serialization-only projections** over the tagged declaration |
| 📌 **stays** | the three **`*Order`** lists — ⭐ **your Batch 51 scoping: display metadata that survive the flip** |
| 📌 **stays** | `IrAsset`'s same-named trio — ⛔ **the EMITTED fields; they set struct offsets and feed `StructureHash`** |
| 📌 **goes with the flip** | `BlueprintCompiler`'s **six-line storage copy** — ⭐ *"it builds an asset's storage, which is what does not move until `U-12` flips the store"* |
| ⚠ **decide and say** | whether the three properties survive **as public members at all** after the flip, or only as serializer surface. ⭐ **`ViewsAreUnreadTests` says nothing reads them** — 📐 **so deleting them is now possible. Is it right?** |

---

## 2. Gates

| | |
|---|---|
| 🔴🔴 **Pass 1 — `persistence-shape.txt` UNCHANGED** | ⛔ **the on-disk bytes must not move.** ⭐ **This is the whole constraint that makes the flip hard, and it is the gate whose failure wipes deployed blackboards** |
| ⭐⭐ **Pass 2 — golden 42/42, both tiers, unchanged** | the compiler reads a model; **the model's storage is not its meaning** |
| ⭐ **Pass 3 — the grep** | ⭐ **`ViewsAreUnreadTests` still holds**, and 📐 **extend it if the properties survive as serializer-only: a read outside the serializer is now the thing to refuse** |
| 🔴 **Revert-goes-red** | ⚠⚠ **and this one deserves care.** ⛔ **A flip that reverts cheaply probably did not flip anything.** ⭐ **Say what edit reddens Pass 1 specifically** — the golden set will not catch a persistence-only regression |
| ⭐⭐ **Both ways** | full suite **and** the isolated filters. ⚠ **Batch 52 exists because those two answers differed** |

**Baseline — coordinator-run on the merged Batch-52 tree (`003db0f2`), ⭐ green BOTH ways:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| Blueprints | **3532 total / 3522 passed / 0 failed / 10 skipped** |
| ⭐ **AiShared 1216** · BTree **612** · Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** | ⛔ **none should move** |
| ⭐⭐ **Golden Tier 1 + Tier 2 · `persistence-shape.txt`** | ⛔ **UNCHANGED** |
| `tracker-counts.py --check` | clean **twenty-one** batches running |

⭐ **Run the five `--no-build` suites in parallel; keep `\[FAIL\]` in the grep.**
⚠⚠ **The two NodeEdit gates take NO `--no-build`.**

---

## 3. 📌 Two things from your own last batch to carry in

### 3.1 ⭐⭐ `BP1673` is the precedent for what to look for here

⭐ **Retiring `BP1024`/`BP1031` UNCOVERED a defect they were silently holding shut** — the name
fallback in `Stage5.FindVariableRef`, which `U-3` and `U-14` both miss. ⭐⭐ **Nothing in the plan's
four passes would have caught it.**

⇒ 📐 **Ask the same question of the flip:** ⛔ **what is the three-lists-as-storage arrangement
silently holding shut?** ⚠ **Candidates worth checking rather than assuming:** anything that relies on
a list being **separately assignable**, on **reference identity** of a list, on a list being **null vs
empty**, or on **insertion order within a kind** surviving a round trip through the tagged store.

### 3.2 ⚠ Your own sweep named its own limit — that limit applies here

⛔ *"Class granularity UNDER-REPORTS: `Stage8Tests` passes per-class and fails per-test."*
⇒ ⭐ **The order-dependency sweep has not been run at test granularity.** 📐 **Not asked for as a
task — but if the flip reddens something intermittently, that is the first place to look**, and
⭐ **worth one line in the report saying whether you extended it.**

---

## 4. ⚡ How to work

**You are on Opus, and ⛔ all of it stays there.** ⭐ **This is the highest-blast-radius change left in
the programme.** 🟢 **Sonnet fits nothing here except possibly a mechanical call-site sweep once the
projection shape is fixed.**

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | the tracker is yours — ⭐ **`U-12` closes here** |
| ⚠⚠ **Stop point** | ⛔ **there is no clean mid-flip stop.** ⭐ **If it does not fit, stop BEFORE starting it and say so** — a half-flipped store is the worst state this programme can be left in |

---

## 5. Reporting

⭐⭐ **`persistence-shape.txt` unchanged, stated FIRST** · ⭐⭐ **what edit would redden Pass 1, and did
you run it** · **golden 42/42 both tiers** · **your §1 ruling on whether the three properties survive
as public members** · ⭐ **what the old arrangement was silently holding shut (§3.1) — even if the
answer is "nothing, checked"** · per-suite numbers, **full and filtered** ·
`tracker-counts.py --check` · ⭐ **every id you allocated** · anything here **wrong against the code**.

⭐⭐⭐ **Batch 52's `BP1673` is the best single finding of the programme:** removing a rail **created the
need for a different one**, and the four planned passes each missed it for a different reason —
`U-3` fixes emission not selection, `U-14` closes only the editor's auto-namer, and Stage 2 had no
duplicate-name rule at all.

⚠ **The flip is the same shape at higher stakes.** ⛔ **Its failure mode is not a red test — it is
`persistence-shape.txt` moving, which is every deployed entity's blackboard re-initialising.**
⭐ **So the question to carry: which gate would actually catch the mistake you are most likely to
make — and have you run it red?**
