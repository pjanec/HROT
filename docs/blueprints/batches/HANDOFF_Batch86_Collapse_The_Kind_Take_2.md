# HANDOFF — Batch 86: **the kind collapse, with the three blockers resolved**

> 📌 **Dispatched at `a4d6b790e`.** ⭐ **Branch from it** *(rule 7)*.
> ⛔⛔ **YOUR SCOPE IS FROZEN AT THIS SHA.** ⭐ Documents changing after it are **FYI ONLY**.
> ⚠ **If a later document INVALIDATES an item — STOP AND REPORT.** ⭐ **Rule 3: allocate your own ids.**
> ⭐ **Rule 1b: push `chore: started batch 86 at <sha>` FIRST.**
>
> ⭐⭐⭐ **READ [`REPORT_Batch85_Collapse_The_Kind.md`](REPORT_Batch85_Collapse_The_Kind.md) FIRST — it
> IS this batch's investigation.** ⛔ **Do not re-derive it.** ⭐ **Batch 85 stopped on three questions;
> the user has now answered all three, and this handoff carries the answers plus the two mechanisms
> Batch 85 built and reverted.**

---

## 1. ⭐⭐⭐ WHAT BATCH 85 ESTABLISHED — **carried, not re-measured**

| ✅ settled | |
|---|---|
| ⭐⭐ **the collapse is HASH-NEUTRAL** | **43/43 compiled assets byte-identical.** 📌 `R-24`'s hard reset **cannot fire** — layout has been kind-agnostic since Batch 56 |
| ⭐ **no asset declares both kinds** | **0 of 100 SOURCE assets** *(⚠ my "541" counted 441 `obj/`/`bin/` copies — corrected)* |
| ⭐ **references are by `Guid`** | `VariableRef` is compile-time IR, rebuilt every compile ⇒ **nothing persisted carries a kind-relative index** |
| ⭐ **`R-09` is not made worse** | `__phase`/`__waitUntilTime` keep their append position; shared state untouched |

### ⭐⭐ The real population — **use these numbers, not mine**

**100 source `.bp.json`** · **16 carry `WorkingState`** *(9 `Parameter+WorkingState`, 7 `WorkingState`)* ·
**43 actually compile** *(the generator's `AdditionalFiles` glob)*.

---

## 2. ✅ THE THREE ANSWERS *(user, `2026-08-18`)* — **all approved**

| | question Batch 85 stopped on | ⭐ answer |
|---|---|---|
| **a** | rewrite the assets, or keep the old tag readable-but-unwritable? | ✅ **REWRITE THEM, IN SCOPE.** 📐 **16 source files, one word per declaration.** ⭐⭐ **The tag is NOT in `StructureHash`** ⇒ **zero hash movement, zero emit-golden movement.** ⛔ My Batch-85 fear of *"458 files of golden movement"* was the inflated census — ⭐ **it is 16 files and none are goldens** |
| **b** | what becomes of the Working State **section**? | ✅ **RETIRE IT, IN SCOPE.** 📌 **`R-01`: one concept ⇒ one section.** ⛔ **The other two options are worse** — pointing it at `Variable` duplicates rows in the outline; leaving it sourceless gives a permanently-empty section whose `[+]` adds elsewhere. ⚠ **This batch is therefore NOT behaviour-neutral, and that is accepted** |
| **c** | `TheV2TagsAreExactlyDeclarationKindsMembersInOrder` | ✅ **RESTATE IT as *"every on-disk tag MAPS to a kind"***, ⛔ **not an equality.** ⭐ **The alias is deliberate** — an equality between enum members and on-disk tags is exactly what a read-alias breaks |

---

## 3. ⭐⭐ CARRY FORWARD — **two mechanisms Batch 85 built, measured and reverted**

> ⛔ **Do not re-derive these.** ⭐ Batch 85 hit both order problems and solved both; the code was
> reverted with everything else.

| 🔴 the problem | ⭐ the solution |
|---|---|
| **`GetOrdered` reorders by an order list and puts unlisted ids in a by-Id tail.** Feeding the merged run only `VariableOrder` would move every old-working-state field into that tail | ⭐⭐ **`ConcatOrder(WorkingStateOrder, VariableOrder)`** — the two lists concatenated in `KindOrder`'s old sequence. ⛔ **Do not feed one. Do not sort.** |
| **Both property setters now drive ONE run**, and the deserializer sets both *(v2 migrates DOWN to the three-list shape)*. Plain `ReplaceWith` means the second setter **wipes** the first | ⭐⭐ **`DeclarationView.ReplaceSegment`** — `WorkingState` owns the leading segment, `Variables` the trailing one; **order preserved for ANY setter order** |

⚠⚠ **And the defect Batch 85's own gate caught — do not repeat it:** replacing `AppendFields(sb,
asset.WorkingState)` with `StateDeclarations` **while leaving the following `AppendFields(sb,
asset.Variables)` in place** hashes the state fields **twice** ⇒ **24 of 43 hashes moved.**

---

## 4. 🛠 THE WORK — **one item, four parts, in this order**

### ⭐ 4a — the collapse *(as Batch 85 built it)*
`DeclarationKind` → `{ Parameter, Variable }` · `VariableKind` → `{ Unresolved=0, Variable, Parameter }`
*(⛔ `Unresolved = 0` stays, for its stated reason)* · the 22 production `Of(WorkingState)` sites
⭐ **read one at a time, ⛔ not sed'd** · `IrAsset.WorkingState` retired, `Variables` carries the tier ·
⭐ **keep BOTH order lists** and both `BlueprintAsset` properties *(`D4` deletes those, not this batch)*.

### ⭐ 4b — rewrite the 16 source assets
`"Kind": "WorkingState"` → `"Kind": "Variable"`. ⛔ **Source only** — `'/obj/' not in f and '/bin/' not in f`.
⭐ **Nothing else in the file changes.** ⚠ **Report the count and confirm no hash moved.**

### ⭐ 4c — retire the Working State **section**
`BlueprintMyBlueprintModel:330`'s `SectionWorkingState => BuildDeclarationItems(DeclarationKind.WorkingState, …)`
goes, with its section descriptor and its create command.
⭐ **Variables becomes the one state section.** ⚠ **Check the `[+]` still creates the right kind**, and
⭐ **that no section is left empty-but-present** *(📌 the design's own "a section that appears and
disappears reads as a broken feature" cuts both ways)*.

### ⭐⭐ 4d — restate the rails, ⛔ **do not delete them**
📌 Batch 85 measured **~37 model/section/view rails** asserting the three-kind model
*(`DeclarationSectionsTests`, `TaggedDeclarationTests`, `StoreFlipTests`,
`MyBlueprintModel_Sections_FixedOrder`, …)*.

> ⭐⭐⭐ **A rail that asserted three kinds must assert TWO — it must not be deleted.**
> ⛔ **Deleting a rail because the model changed removes the evidence that the change was intended.**
> ⚠ **Report the count restated vs the count deleted, and justify every deletion individually.**

---

## 5. 🔴🔴 GOLDEN MOVEMENT — **AUTHORISED, and only in this exact shape**

⚠⚠ **Batch 85's rule was *"a moved golden is a STOP."* ⭐ That would stop this batch for the wrong
reason.** ⇒ ⭐⭐ **Pre-authorised, with the shape stated:**

| ✅ authorised | shape |
|---|---|
| **12 × `Tier1_StructureAndDiagnostics_MatchBaseline`** | ⭐ **a LABEL move only — `WorkingState:` → `Variables:`.** ⛔⛔ **Offsets and the hash line MUST be IDENTICAL.** ⚠ **Diff each one and say so** |
| **the 16 source assets** *(4b)* | one word per declaration |

| ⛔⛔ **STILL A STOP** | |
|---|---|
| **any `StructureHash` change** | 📌 `R-24` |
| **any `persistence-shape.txt` movement** | |
| **any `Emit/*.cs.txt` movement** | ⭐ **the tag is not in the emitted C#** — if one moves, something else moved with it |
| **a Tier-1 diff that touches an offset or a hash line** | ⛔ **not the authorised shape** |

---

## 6. ⭐ Gates — **the rule-8 contract, plus the two this batch owns**

| # | report |
|---|---|
| **1–7** | the standard contract — verbatim commands · `--no-build` column · golden movement as a **diff shape** · every red confirmed pre-existing vs the base sha · clean tree · both quarantine counts · `tracker-counts.py --check` + `rulings-check.py` + ids |
| ⭐⭐ **8** | 🔴🔴 **`StructureHash` computed BEFORE and AFTER for all 43 compiled assets — byte-identical, as a count.** ⛔ **Not "the goldens didn't move"** — 📌 that is what caught Batch 85's double-hash defect |
| ⭐⭐ **9** | ⛔⛔ **NOT REACHED IN BATCH 85 — required here.** ⭐ **A constructed asset declaring BOTH kinds**, proving `ConcatOrder` + `ReplaceSegment` preserve order. 📌 **The corpus cannot exercise this path** *(0 of 100)*, which is exactly the blind spot `BP-244` hit |
| ⭐ **10** | **rails restated vs deleted**, with a justification per deletion *(4d)* |

⭐ **Baseline** *(Batch 84, the last green one)*: build **0 err** · AiShared **1397** · Blueprints
**3772/3782/10** · BTree.Editor **615** · Hsm.Editor **551** · Generators **270** · Breakpoints **143** ·
Persistence **136** · Hrot.Editor **194** · Scenarios **56/68 (12 skipped)** · UrbanCombat **29** ·
Toolkits **1964** · NodeEditor.Core **211** · NodeEditor.UI **135** · FastHSM **300** ·
tracker **open 70 / done 197** · rulings **44/44**.

⚠ **Batch 85's 68 reds are the map of this batch's work**: 15 byte-stability *(fixed by 4b)* · 12 Tier-1
*(authorised regeneration, §5)* · 3 round-trip *(same cause as byte-stability)* · ~37 model/section
rails *(restated, 4d)* · 1 V2-tags test *(restated, 2c)*. ⭐⭐ **If a red survives that is NOT in this
list, it is a finding — report it, do not absorb it.**

---

## 7. ⛔ OUT OF SCOPE

| ⛔ | |
|---|---|
| **`D4`'s deletion of `asset.WorkingState` / `asset.Variables`** | ⭐ keep both properties; they are one view now |
| **`R-09`'s undeclared synthesized fields and shared state** | ⭐ **note if the merge makes either worse — Batch 85 measured it does not** |
| **the watch-pinning design** | 📄 `DESIGN_Variable_Watch_Pinning.md` — its own batch |
| **`BP-327`** *(the dialog has no OK button)* · **`BP-330`** *(shared `AiWatchWindow` cannot edit)* | separate |

## 8. ⭐⭐ If you must stop again

⭐ **Stopping remains a good outcome.** ⛔ **But the three questions that stopped Batch 85 are answered**
— ⚠ **if a FOURTH appears, report it with the same rigour and leave the tree clean.**
⭐ **A half-collapsed enum is the one unacceptable end state.**
