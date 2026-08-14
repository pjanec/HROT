# HANDOFF — Batch 46: ⭐⭐ **`U-4` + `U-5` — the editor's turn at the same defect**

> 📌 **Dispatched at `<STAMP>`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7:** branch from this branch, re-sync at the **start** of your run.
> ⭐ **Rule 4:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP1671+` is the next free diagnostic.
>
> 📄 [PLAN_Variable_Unification_Tasks.md](PLAN_Variable_Unification_Tasks.md) §2 · `U-4`, `U-5`.
> ⚠⚠ **The plan itself flags this: `U-4` + `U-5` is NOT one lane.** `U-5` reaches into
> **`Hrot.Editor.AiShared`** and **moves that gate (1213)**. ⭐ **Kept together anyway because `U-5` is
> what makes `U-6` honest** — ⛔ **and `U-6` is the batch after next, so shipping `U-4` alone would
> leave an editor that projects three lists through a stub.**

---

## 0. ⭐⭐ The framing, and it is not decoration

⭐ **`U-3` killed an untagged `int` in the compiler. `U-4` kills a two-valued `bool` over the same
three-list model in the editor.** ⛔ **Same defect shape, opposite end of the pipeline.**

```csharp
// BlueprintVariablesWindow.cs:33 — a BOOL over THREE lists
public BlueprintVariableSchemaSource(BlueprintAsset asset, bool isParams, Action onChanged)
```

⇒ ⛔ **`Variables` — the `State` struct at offset 16 — is not representable.** The flag has two values;
the model has three lists. ⭐ **Ten `if (_isParams)` branches ride it** (`:44 :53 :105 :124 :149 :157
:164` …).

⭐ **`U-3`'s answer is the precedent and it is one batch old:** `VariableKind` with
⭐⭐ **`Unresolved = 0` as the default**, so a forgotten assignment **throws** instead of silently
meaning the first list. 📐 **Mirror that instinct; the shape is yours.**

---

## 1. `U-4` — `Variables` as a third schema source; kill `bool isParams` *(stage A)*

| | |
|---|---|
| ✅ **Gate** | **all three kinds project the right list** — `Parameters` · `WorkingState` · `Variables` |
| ⭐ **Golden** | 42/42 both tiers, **unchanged** ⚠ *(this is editor-only, so a golden move means something reached the compiler that should not have)* |
| 🔴 **Revert-goes-red** | restore the `bool` ⇒ the `Variables` projection test fails |

📌 **`BlueprintLocalVariableSchemaSource` (Batch 41) is ALREADY an `IVariablesSchemaSource`** and was
built that way *so `U-4`…`U-6` could absorb it.* ⛔ **Do not fold it in yet** — that is `U-6`. ⭐ **But
read it first: it is the shape a fourth kind takes, and it is the newest and cleanest of the three.**

⚠ **One known gap it carries, recorded at Batch 43 and NOT yet fixed:**
⛔ **`AddVariable` appends unconditionally — it does not reject a duplicate name.** The guard currently
sits in the **window's confirm path**. 📐 **If `U-4`'s work makes the source the natural home, move it
and say so; otherwise leave it for `U-6`.** ⛔ **Do not leave it in two places.**

---

## 2. `U-5` — make the source honest (`BP-230`, `BP-231`)

### 2.1 🔴 `BP-230` — the stubs, and ⚠ **the interface is complicit**

```csharp
// VariablesPanelControl.cs:25-27 — DEFAULT INTERFACE BODIES
void UpdateVariableRole(string name, BlackboardVariableRole role) { }
void UpdateVariableScope(string name, WorkingStateScope scope) { }
```

⛔⛔ **A source that never implements these compiles silently and does nothing.** ⭐ **That is trap #5
built into the interface itself** — and `BlueprintVariableSchemaSource` takes exactly that offer, plus
`CountNodesReferencingVariable(name) => 0`.

⇒ ⭐⭐ **`Q-k` already ruled the semantics: `Role`/`Scope` are READ-ONLY for blueprints — a MOVE, not a
toggle.** ⛔ **So "honest" here does not mean "implement the setter."** 📐 **It means the surface must
say so rather than accepting the call and discarding it.** ⭐ **Decide the shape and say which:** drop
the default bodies and make each source declare its answer, a `CanEdit`-style capability, or an
explicit refusal. ⚠ **The plan's V2 note says this is the AiShared-touching half — expect 1213 to move.**

⭐ **`CountNodesReferencingVariable` has a working precedent one batch old:**
`BlueprintLocalVariableSchemaSource`'s real count (Batch 41) — ⭐ **counting by ID, not name.**
⛔ **Do not re-derive it; mirror it.**

### 2.2 `BP-231` — the order lists leak

`AddVariable` and `MoveVariable` maintain `ParameterOrder`/`WorkingStateOrder`; ⛔ **`RemoveVariable`
and `RenameVariable` do not** ⇒ a deleted variable's id stays in the order list forever.
✅ **Benign today** — `Stage5.GetOrdered` skips unknown ids and appends unlisted fields.
⚠ **`U-9` turns the order lists into projections of a tagged declaration** ⇒ ⭐ **benign now, not
benign then. Fix it while it is still cheap.**

**Gate:** the reference count is **real** · order lists **survive remove and rename** · ⭐ **and a test
that the count is not merely non-zero but CORRECT** — `BP-230`'s `=> 0` would pass *"returns an int."*

---

## 3. Gates

**Baseline — coordinator-run on the merged Batch-45 tree (`74526bf0`):**

| | |
|---|---|
| Solution build | **0 errors** · **BP diagnostics 10 distinct**, all `BP3010` |
| Blueprints | **3451 total / 3441 passed / 0 failed / 10 skipped** |
| ⚠⭐ **AiShared 1213** | 📌 **THIS BATCH MOVES IT** — the only batch since 38 that should. ⭐ **Say by how much and why** |
| BTree **612** · Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** | ⛔ **none should move** |
| ⭐⭐ **Golden 42/42, both tiers** | ⛔ **unchanged** |
| `tracker-counts.py --check` | clean **fourteen** batches running |

⭐ **Run the five `--no-build` suites in parallel; keep `\[FAIL\]` in the grep.**
⚠⚠ **The two NodeEdit gates take NO `--no-build`.**

⛔ **Say plainly what is NOT covered headlessly.** ⚠ **The visual check has not run for ELEVEN batches.**
⭐ **`U-4`/`U-5` are chosen to be headless-provable** — the projections and the counts are assertions
about **contents**, not appearance. ⛔ **`U-6`/`U-13`/`U-16` (Batch 47) are NOT, and will wait for it.**

---

## 4. 📌 One nit, and it has a demonstrated victim

`Stage5_Schedule.cs:4619` — `FindParameterIndex`'s doc comment still describes `FindVariableIndex` as
returning ⛔ **"a COMBINED index."**

⭐⭐ **Batch 45's own report identified this comment as the likely source of the coordinator's wrong
finding — then said it was "gone with the method." It is not:** `FindParameterIndex` survives, so the
comment survives, and it is **now doubly wrong** (the claim was always false; the return type it
describes no longer exists). ⇒ **One line. Fix it.**

---

## 5. ⚡ How to work

**You are on Opus.** 🟢 **Sonnet fits `U-4`'s ten-branch sweep once the carrier shape is fixed**, and
`BP-231`'s order-list maintenance. ⛔ **Opus keeps the `Q-k` refusal shape (§2.1) and every gate.**

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | the **tracker is yours** — ⭐ **`BP-230` and `BP-231` close here** |
| ⚠ **Stop point** | ⭐ **`U-4` alone is shippable.** If `U-5`'s AiShared reach turns out wider than the plan assumes, **stop and report the shape** — ⛔ **do not half-change a shared interface** |

---

## 6. Reporting

Per-suite numbers · ⭐ **AiShared's new number and what moved it** · ⭐⭐ **golden 42/42 both tiers,
stated explicitly** · `tracker-counts.py --check` · ⭐ **every id you allocated** · **your `isParams`
replacement shape** · **your `Q-k` refusal shape** · ⭐ **whether the duplicate-name guard moved** ·
⭐ **where you stopped** · anything here **wrong against the code**.

⭐⭐ **Batch 45 refused a coordinator finding and was right** — the index was list-relative and my
rebase would have broken every shipped AiPrimitive. ⚠ **§0's framing and §2.1's reading of the default
interface bodies are mine too. Same treatment if they are wrong against the code.**
