# HANDOFF — Batch 45: ⭐⭐ **`U-3` — `(kind, index)`. ONE task, and it closes `BP-226`**

> 📌 **Dispatched at `b22638e4`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7:** branch from this branch, re-sync at the **start** of your run.
> ⭐ **Rule 4:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP1671+` is the next free diagnostic.
>
> ⭐⭐ **You now have a net.** `U-3`'s Pass 1 is *"golden unchanged"* and Batch 44 made that a real
> assertion instead of a hope. ⭐ **This is the first task the net was built for.**
> 📄 [PLAN_Variable_Unification_Tasks.md](PLAN_Variable_Unification_Tasks.md) §2 · `U-3`.

---

## 0. ⭐ Why this one alone

⚖️ **Highest value of any single task in the programme, and deliberately unmixed** — it closes a
🔴 defect (`BP-226`) *and* it is stage **C**, which the Batch 38 review moved to **first** because it
**needs nothing from `D`**.

⛔ **Nothing else is in this batch.** No editor surface. ⚠ **The visual check is still unavailable and
this task does not need it.**

---

## 1. 🔴 The defect, coordinator-re-verified today — **both halves, verbatim**

### 1.1 `Stage5.FindVariableIndex` returns a **per-list** index and throws the list away

```csharp
// Stage5_Schedule.cs:4587-4589
for (int i = 0; i < variables.Count;  i++) if (variables[i].Id  == guid) return i;
for (int i = 0; i < workState.Count;  i++) if (workState[i].Id  == guid) return i;
for (int i = 0; i < parameters.Count; i++) if (parameters[i].Id == guid) return i;
```

⇒ ⛔ **`i` is an index into whichever list matched. Which list is NOT returned.**

### 1.2 `EmissionContext.VarFieldName` then guesses — and guesses **differently**

```csharp
// EmissionContext.cs:66-72
var fields = Asset.Variables;
if (index < fields.Count) return fields[index].Name;
var ws = Asset.WorkingState;
if (index < ws.Count)     return ws[index].Name;      // ⛔⛔ NOT ws[index - fields.Count]
return $"__var_{index}";
```

⭐⭐ **Two independent bugs in four lines, and the second is worse than `BP-226`'s row says:**

| | |
|---|---|
| 🔴 **`Variables` shadows everything** | a `WorkingState` reference at index `2` emits **`Variables[2]`** whenever `Variables.Count > 2` |
| 🔴🔴 **The `WorkingState` branch is not offset** | it tests `index < ws.Count` and reads `ws[index]` ⇒ ⛔ **even reaching that branch, the index is un-rebased.** *Coordinator finding — the tracker row does not say this* |
| 🔴 **`Parameters` is never consulted at all** | ⇒ a `Parameters`-targeting reference emits either the wrong `Variables` field **or** `__var_{index}`, ⛔ **which is not valid C# and has no BP diagnostic** |

⭐ **The three lists have three different meanings** — `struct Params`, `struct` working state at
offset 8, `struct State` at offset 16. ⇒ **an integer that does not say which one is a type error the
compiler is not making.**

---

## 2. ⭐ The fix, and the one thing that makes it safe

📐 **Shape is yours** — an `enum VariableKind` + a readonly record struct, or a discriminated pair.
⭐ **The requirement is only this: the kind travels with the index from Stage 5 to Stage 7, and
`VarFieldName` can no longer be reached with a bare `int`.**

⚠ **Make the wrong call unwritable, not merely unwritten.** ⛔ **If `VarFieldName(int)` still compiles
after this task, the next refactor re-introduces the defect.** ⭐ *`BP1670`'s throw is the precedent —
it turned a fall-through into an assertion.*

📌 **`IrOp_ReadVariable` / `IrOp_WriteVariable` carry the index** (`IrOperation.cs:17`) ⇒ **the IR
changes too.** ⚠ **That is expected, and it is why Pass 1 matters so much.**

---

## 3. Gates

| | |
|---|---|
| ⭐⭐ **Pass 1 — golden unchanged** | **42/42, both tiers.** ⛔ **This task declares NO golden change.** ⚠ **If Tier 1 moves, STOP** — it means the refactor is not behaviour-preserving, and Batch 44 built the harness precisely to tell you that on the day rather than three batches later |
| ⭐ **Pass 2 — 🔴 fails today** | an asset with **both** `Variables` and `WorkingState` populated (constructed **in memory**, driven past Stage 2) ⇒ a `WorkingState`-targeting read emits **the WorkingState field's name** |
| ⭐ **Pass 3 — 🔴 fails today** | a `Parameters`-targeting read emits **the parameter's name**, ⛔ never `__var_{index}` and never a `Variables` field |
| 🔴 **Revert-goes-red** | restore the bare `int` ⇒ **Pass 2 and Pass 3 fail** |
| ⚠ **`BP1670`'s throw SURVIVES** | `VarFieldName` throws on a negative index (Batch 39) — **the assertion that Stage 2's rail is complete.** ⛔ **The refactor must carry it across, not smooth it away.** 📐 In the new shape it may become *"no kind resolved"* rather than *"index < 0"* — **fine, say which** |

### ⭐⭐ Why Pass 2 and Pass 3 must live OUTSIDE the corpus — and why that is good news

⭐ **`BP1024`/`BP1031` mean no shipped asset has both lists populated** ⇒ ⛔ **`BP-226`'s wrong
resolution never fires inside the golden corpus.** ⇒ ⭐⭐ **the entrenchment worry is dead: no shipped
asset depends on the broken behaviour, so the fix cannot break one.**

⚠ **The corollary is the trap:** *"golden unchanged"* alone would pass **a refactor that fixed
nothing.* ⇒ **Pass 2 and Pass 3 are the whole proof.** ⭐ **Assert they are RED before your change** —
run them against the pre-change tree and say so in your report. *(Batch 44's lesson: a proof that has
never failed is not a proof.)*

**Baseline — coordinator-run on the merged Batch-44 tree (`ba337568`):**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| Blueprints | **3448 total / 3438 passed / 0 failed / 10 skipped** |
| ⭐ **AiShared 1213** · BTree **612** · Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** | ⛔ **none should move** |
| ⭐ **`tracker-counts.py --check`** | clean **thirteen** batches running |

⭐ **Run the five `--no-build` suites in parallel; keep `\[FAIL\]` in the grep.**
⚠⚠ **The two NodeEdit gates take NO `--no-build`.**

---

## 4. ⚡ How to work

**You are on Opus, and ⛔ this one stays there.** ⭐ **The `(kind, index)` threading touches Stage 5,
the IR and Stage 7 — it is exactly the novel compiler work `.claude/CLAUDE.md` says to do hands-on.**
🟢 **Sonnet is fine for the mechanical call-site sweep once the shape is fixed**, ⛔ **never for the
shape or the gates.**

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | the **tracker is yours** — ⭐ **`BP-226` closes here** |
| ⚠ **`BP-227`** | 📌 **not this batch.** The plan settles it in `U-15` |

---

## 5. Reporting

Per-suite numbers · ⭐⭐ **Tier 1 and Tier 2 both unchanged, stated explicitly** ·
`tracker-counts.py --check` · ⭐ **every id you allocated** · ⭐⭐ **that Pass 2 and Pass 3 were RED
before the change** · **your kind-carrier shape** · **what `BP1670`'s throw became** ·
⭐ **whether `VarFieldName(int)` is now unwritable, or only unwritten** · ⭐ **where you stopped** ·
anything here **wrong against the code**.

⭐ **Batch 44's best work was the two defects it found by DOING §1.4 rather than reasoning about it** —
baselines regenerating into `bin/`, and a bite test overwriting the baseline it was meant to prove.
⚠ **§3's *"assert Pass 2 and Pass 3 are red first"* is the same discipline. Do it in that order.**

📌 **And Batch 44 corrected this plan three times from measurement.** ⭐ **§1's second finding above is
mine and is not in `BP-226`'s row — if it is wrong against the code, say so.**
