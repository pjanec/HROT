# HANDOFF — Batch 61: ⭐⭐⭐ **the rest of Phase A — five items, one run**

> 📌 **Dispatched at `518c95fb2`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐⭐ **RUN BATCH 60 THEN THIS, BACK TO BACK. Do not return in between.**
> ⭐ **User ruling `2026-08-15`: bigger batches, longer autonomous runs.** ⇒ **five items here, ordered,
> each with its own gate and its own STOP condition.**
> ⭐ **Rule 7:** branch from this branch. ⭐ **Rule 4:** pull it again before your final commit.
> ⛔ **Rule 3: the coordinator allocates no ids.** **You** allocate diagnostics and tracker rows.
> ⭐ **One commit per item**, as you did across 56/58/57/59 — that is what made attribution possible.

---

## 0. ⭐⭐ How to run this — **the stop rule is the whole safety mechanism**

⭐ **Work the items in the order below.** ⛔ **If an item's STOP condition fires, finish the items already
done, commit them, and return** — ⚠ **do not push past a stop condition to "finish the batch."**
⭐ **Four merged items plus a question beats five items and a guess.**

| # | item | why it is here |
|---|---|---|
| **1** | **`BP-247`** — the `CS0664` float literal | smallest; **your** finding |
| **2** | **`W5`** — summed parameter budget | ⛔ **coordinator CORRECTED — see §2** |
| **3** | **`W6`** — guard read-only projection | independent |
| **4** | **`W7`** — concurrent-region rule | ⚠ **needs `W6`** |
| **5** | **`S2`** — struct size resolution | ⚠ **largest, and carries an OPEN placement decision (§5)** — last on purpose |

---

## 1. `BP-247` — the float literal

⭐ **Your Batch 57 finding:** `Stage5` assigns `DefaultValueCSharp` from `DefaultValueJson` **verbatim**,
so a fractional float default emits a **double** literal ⇒ `CS0664`, **naming a generated file**.

| | |
|---|---|
| ⭐⭐ **why it is not cosmetic** | ⛔ **It becomes user-visible the moment the Details panel ships** — ruling 5's stopped half writes the initial value to JSON, so a designer typing `0.5` gets a compiler error naming a file they have never seen |
| **scope** | the literal suffix for `float`, ⭐ **and check the neighbours** — `decimal`, `long`, `uint` and unsigned types have the same shape |
| **gate** | ⭐ **a fixture asset with a fractional float default that FAILS to compile today** · golden unchanged *(every shipped default is integral — so ⛔ **the corpus cannot witness this either**)* |
| 🛑 **STOP if** | fixing it moves `StructureHash` on any shipped asset — it must not |

---

## 2. `W5` — ⛔⛔ **the handoff's instruction is NOT BUILDABLE. Coordinator-measured**

`W5` says: *"fold in `BehaviorParameterSizeAnalyzer:26`'s duplicated constant."*
⛔⛔ **You cannot, and the duplication is deliberate and documented.** `BehaviorParameterSizeAnalyzer:23-26`:

> *"Mirrors `BehaviorConstants.MaxBehaviorParamByteSize`. **Intentionally inlined here because this
> analyzer targets netstandard2.0 and cannot reference the net8.0 `Fdp.Toolkits` runtime assembly.**"*

⇒ ⭐ **Same wall as `BP-235`.** ⭐⭐ **The right fix is not to remove the duplicate but to make it CHECKED:**
a test — **tests are `net8.0` and can reference both** — asserting the analyzer's constant equals
`BehaviorConstants.MaxBehaviorParamByteSize`. ⛔ **A silently drifting mirror is the defect; the mirror
itself is forced.**

| | |
|---|---|
| ⭐ **the real work** | **sum the budget over all bindings in an asset** (`Pack` already returns `totalBytes`) — ⛔ **today each binding is checked alone**, so N bindings can each pass and the asset still overflow |
| ⭐ **live today** | **BTree parallel composites** — ⚠ **not an HSM-only gap** |
| **gate** | an asset exceeding 100 B **across simultaneously-live bindings** is refused; ⭐ **plus the constant-agreement test, proven red by editing one side** |
| 🛑 **STOP if** | a **shipped** asset trips the summed budget ⇒ **that is a live defect, report it — do not raise the limit** |

---

## 3. `W6` — guard read-only projection

⭐ `GetComponent` not `GetComponentRW`; `in` / `ref readonly` at the thunk boundary.
⭐ **The invariant to state in the code:** *"a speculative evaluation may not be observable."*

| | |
|---|---|
| ⚠ **verify the cheapness before relying on it** | the design session measured **0 production `[SharedAiCondition]` usages** *(4 exist, all test fixtures)*. ⭐ **Coordinator re-grepped and found only the ATTRIBUTE and GENERATOR sites, which is consistent but does not confirm the usage count** ⇒ 📐 **re-measure and state the number** |
| **gate** | a guard cannot obtain a writable component reference; ⭐ **prove it by trying** — a fixture guard that writes must fail |
| 🛑 **STOP if** | the count is **not** ~0 — a real blast radius changes this from near-free to a migration |

---

## 4. `W7` — concurrent-region rule *(needs `W6`)*

⭐ **Error on concurrent WRITERS; permit concurrent READERS.**
⚠ **Undecidable without `W6`** — a guard must be *statically* a reader before "reader" means anything.

| | |
|---|---|
| **gate** | two regions writing one slot ⇒ refused; two regions reading it ⇒ allowed. ⭐ **Both fixtures, both red-first** |
| 🛑 **STOP if** | `W6` did not land cleanly — ⛔ **`W7` on top of an unproven reader classification is a rule that cannot be trusted** |

---

## 5. `S2` — struct size resolution ⚠ **carries an OPEN decision**

🔴 **Today an unregistered struct resolves at a GUESSED 4 bytes**, and `StaticTypeRegistry:75-81`
hardcodes **three** structs with sizes **computed by hand in a comment** — the file names its own gap:
*"a general curated-struct registration mechanism … is future work."*
⭐⭐ **`StructSizeResolver` is a fully general Roslyn-based size computer that ALREADY EXISTS** in
`Hrot.AiEditor.Generators`, and ⛔ **the blueprint compiler never calls it.**

### ⚠⚠ The open decision — **placement, and it is yours to make**

⭐ **Coordinator-measured, so you do not have to:**

| | |
|---|---|
| ✅ **there is NO project cycle** | `Hrot.AiEditor.Generators` references `Hrot.Blueprints.Schema` and `Hrot.AiEditor.Persistence` — ⛔ **not the compiler** ⇒ a Compiler→Generators edge is **not** `BP-235`'s cycle |
| ⛔ **but do not just add the reference** | ⚠ **it drags Roslyn into the compiler**, and the compiler is deliberately reflection-less. ⭐⭐ **There is already a seam for exactly this: `IClrSignatureResolver` on `CompileOptions`** (`U-7`/Batch 47) — ⭐ **and Batch 44 measured the in-process and semantic-model paths 42/42 byte-identical**, so the same oracle is known to work at both ends |
| ⚖️ **coordinator lean** | ⭐ **the existing seam, or move `StructSizeResolver` somewhere both can see** — ⛔ **not a new Compiler→Generators project reference.** ⚠ **Lean, not a ruling — you have the better view once you are in it** |
| 🛑 **STOP and report if** | ⭐ **the only workable placement is a new project reference from the compiler** — ⛔ **that is an architecture change and it is the coordinator's to take to the user, not yours to absorb** |

| | |
|---|---|
| **gate** | ⭐ **an asset with an UNREGISTERED user struct gets its REAL size** — proven by a fixture whose struct is not one of the hardcoded three · ⭐ **and `Marshal.OffsetOf` agrees** *(Batch 60's `W2` gate now covers this — reuse it, do not write a second one)* |
| ⛔ **NOT in this batch** | `S3` (the `MarshalFromBytes` struct arm) · `S4` · `S5` · `W8`–`W13` · any panel work |

---

## 6. Gates — **whole batch**

**Baseline: Batch 60's merged numbers**, not `bc79be664`'s.

| | |
|---|---|
| 🔴🔴 **`StructureHash` unchanged for all shipped assets** | ⛔ **none of these five items may move a shipped layout.** ⚠ **`S2` is the one that could — an unregistered struct getting its real size CHANGES that asset's layout.** 📐 **If a shipped asset uses an unregistered struct, STOP: that is a live wrong-layout defect and it outranks the batch** |
| **`persistence-shape.txt`** | ⛔ **UNCHANGED** |
| ⭐ **golden** | Tier 1 unchanged for existing assets; **declare any Tier 2 movement per item** |
| ⭐ **per-item revert-goes-red** | ⛔ **five items in one batch means attribution matters more, not less** |
| `tracker-counts.py --check` | clean |

---

## 7. ⚡ How to work

**Opus.** ⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```
⭐ **Delegate `BP-247` and `W5`'s constant-agreement test to Sonnet if useful** — ⛔ **keep `S2` and `W7`
on Opus**, they are judgment, not mirroring.

---

## 8. Reporting

**Per item:** what moved, its gate, and **that it was red first**. ⭐ **Then, whole-batch:**
🔴 **`StructureHash` unchanged, stated FIRST** · ⭐ **the `[SharedAiCondition]` usage count you measured**
· ⭐⭐ **`S2`'s placement decision and WHY** · ⭐ **any STOP condition that fired, and where you halted** ·
`persistence-shape.txt` · per-suite numbers **full and filtered** · `tracker-counts.py --check` ·
⭐ **every id you allocated** (rule 5).

⭐⭐⭐ **The question to carry:** ⛔ **`W5`'s constant is a mirror that exists because a project boundary
forces it, and nothing checked it.** 📐 **How many other constants in this repo are duplicated across the
netstandard2.0 / net8.0 boundary with only a comment holding them together?** ⚠ **That boundary has now
produced `BP-235`, `W5`, and `S2`'s placement question — three findings from one wall.**
