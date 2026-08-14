# HANDOFF — Batch 52: 🔴 **§1 the RED gate, then `U-12` — the rails and the store flip**

> 📌 **Dispatched at `<STAMP>`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7:** branch from this branch, re-sync at the **start** of your run.
> ⭐ **Rule 4:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP1672+` is the next free diagnostic.
>
> ⛔⛔ **THE BLUEPRINTS GATE IS RED. §1 comes first and `U-12` does not start until it is green** —
> a store flip cannot be verified against a suite with two known failures.

---

## 1. 🔴🔴 Two failing tests — and the compiler is complicit

```
PdbEmbeddedSourceTests.WithPdbOption_PdbIsNonNull       Assert.NotNull(result.PortablePdb) → null
PdbEmbeddedSourceTests.PdbContainsEmbeddedSourceSignature   same
```

### 1.1 ⭐ It is NOT yours — coordinator-bisected

✅ **Reproduced on the pre-Batch-51 tree (`2a8188dd9`, fresh worktree, full build): both fail there in
isolation** — ⛔ **while at Batch 50 that same tree ran the full suite 3505/3505 green.**

⇒ ⭐⭐ **It was already an order-dependent green at Batch 50, and the coordinator did not see it.**
`ViewsAreUnreadTests` changed the suite's composition enough to break the accident.
📌 **Exposed by you, not caused by you — the same sentence as `BP-236`, one batch later.**

### 1.2 The mechanism, coordinator-verified

```csharp
// BlueprintsCore.cs:14 — [ModuleInitializer]. Fires only when THIS ASSEMBLY is first loaded.
BlueprintCompiler.RoslynFinalizer = (source, virtualPath, assemblyName, sink) => …

// BlueprintCompiler.cs:116 — and the guard is SILENT
if (options.EmitPdbWithEmbeddedSource && RoslynFinalizer is not null)
```

⛔⛔ **`EmitPdbWithEmbeddedSource: true` with no finalizer loaded yields NO pdb, NO diagnostic, and
`Succeeded == true`.** ⭐ **That is trap #5 in the COMPILER, not merely in the test** — the caller asked
for a PDB, did not get one, and was told nothing.

### 1.3 📐 What to build — two decisions, both yours

| | |
|---|---|
| **(a) the test** | ⭐ `BP-236`'s fix is the precedent — **the one-line preload `GoldenCorpus` uses.** ⚠ **But that only makes THIS test order-independent** |
| **(b) ⭐⭐ the compiler** | ⛔ **the silent guard is the real defect.** 📐 **Decide:** a diagnostic when a PDB is asked for and cannot be produced, or a throw, or *"documented and deliberate"* with a reason. ⭐ **`BP1670`'s throw and `U-3`'s out-of-range throw are both precedents** |

⚖️ **Both is the lean.** ⛔ **(a) alone leaves a compiler that silently drops a requested artefact.**
⭐ **File it, and say what you chose.**

### 1.4 ⚠⚠ And treat it as a CLASS, not a third incident

**Three in three batches:** `BP-236` (a fallback directory holding 9 of 16 recipes) · this one (a
module initializer) · and the near-miss `ViewsAreUnreadTests` was written to prevent (*"a grep that
matches nothing looks exactly like a grep that is green"*).

⇒ 📐 **Worth one deliberate sweep, and say what you find:** ⭐ **which other tests depend on
`Hrot.Blueprints.Core` being loaded, or on another test having run first?** ⛔ **A green suite that
reports its own composition rather than the code is the failure mode this programme keeps meeting.**
📌 **If a cheap collection-level fixture makes the whole class impossible, that is worth more than
three point fixes.**

---

## 2. `U-12` — rails restated, views deleted, store flipped *(D4)*

⭐⭐ **Unblocked as a CHECKED FACT:** `ViewsAreUnreadTests` says nothing under `Hrot.Blueprints.Editor`
or the compiler stages reads a declaration list directly, **and it is proved to fail.**

| | |
|---|---|
| ✅ **Pass 1** | `BP1024` is **gone** — an AiPrimitive with `(State, Asset)` entries compiles |
| ✅ **Pass 2** | `BP1031` **split** — an Instance with an `Input` entry ⇒ diagnostic |
| ✅ **Pass 3** | `BP1011` **restated** — a Library with **any** `Asset`-scope entry ⇒ diagnostic |
| ✅ **Pass 4** | ⭐ **golden 42/42 both tiers unchanged** · **grep asserts the old views are gone** |
| ⭐ **Pass 5** | ⛔ **`persistence-shape.txt` unchanged** — ⚠ **the store flip must not reach the bytes**; that is `U-10`'s wiring, Batch 53 |

### 2.1 ⚠ The store flip — your own note names what moves

*"`BlueprintCompiler`'s six-line storage copy stays: it builds an asset's storage, which is what does
not move until `U-12` flips the store."*

⇒ ⭐ **This is where it moves.** 📌 **And two things you already ruled must survive it:**

| | |
|---|---|
| ⭐ **The `*Order` lists are DISPLAY METADATA that survive the flip** | (Batch 51's scoping note) — ⛔ **they are not part of the store being flipped** |
| ⭐ **`IrAsset`'s same-named lists are the EMITTED fields** | ⛔ **untouched — they set struct offsets and feed `StructureHash`** |
| ⚠ **The three `*Order` lists stay per-kind in v2** | (Batch 49) — *"merging them needs each id's kind to reconstruct, which only holds while no id is stale"* ⭐ **you said that belongs with `U-12`. It is here now** |

### 2.2 ⚖️ Stop points, in order

⭐ **§1 alone is a complete, valuable batch.** ⭐ **§1 + the rails, without the store flip, is another.**
⛔ **The store flip is the last thing in, never the only thing.**

---

## 3. Gates

**Baseline — coordinator-run on the merged Batch-51 tree (`d2cde7cd`):**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| 🔴 **Blueprints** | **3518 total / 3506 passed / ⛔ 2 FAILED / 10 skipped** — ⭐ **§1 must take this to 0 failed** |
| ⭐ **AiShared 1216** · BTree **612** · Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** | ⛔ **none should move** |
| ⭐⭐ **Golden Tier 1 + Tier 2 · `persistence-shape.txt`** | ⛔ **UNCHANGED** |
| `tracker-counts.py --check` | clean **twenty** batches running |

⭐ **Run the five `--no-build` suites in parallel; keep `\[FAIL\]` in the grep.**
⚠⚠ **The two NodeEdit gates take NO `--no-build`.**
⭐⭐ **And run the Blueprints suite BOTH ways this time** — full, and with a filter that isolates the
class you touched. ⚠ **§1 exists because those two answers differed and nobody compared them.**

---

## 4. ⚡ How to work

**You are on Opus, and ⛔ `U-12` stays there** — three rail changes and a store flip.
🟢 **Sonnet fits §1.4's sweep** *(finding order-dependent tests)*, ⛔ **not the rulings.**

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | the tracker is yours |
| ⚠ **Stop point** | ⭐ **after §1, or after the rails.** ⛔ **Never mid-flip** |

---

## 5. Reporting

⭐⭐ **Blueprints at 0 failed, stated first** · **your §1.3 (a)/(b) choices** · ⭐⭐ **§1.4's sweep — what
else is order-dependent** · per-suite numbers · ⭐ **golden + `persistence-shape` unchanged** ·
`tracker-counts.py --check` · ⭐ **every id and diagnostic code you allocated** · ⭐ **where you
stopped** · anything here **wrong against the code**.

⭐⭐ **Batch 51's best move was making the gate distrust itself:** `ViewsAreUnreadTests` asserts the
pattern **still matches a known read**, because a grep that matches nothing is indistinguishable from a
grep that is green.

⚠ **§1 is that same idea aimed one level up.** ⛔ **The PDB tests were green for two batches because of
what else happened to run.** ⭐ **The question worth carrying into `U-12`: for each gate you are about
to trust, what else has to be true for it to pass — and is that thing checked, or lucky?**
