# HANDOFF — Batch 40: ⭐ **review the unification TASK PLAN.** No feature code

> 📌 **Dispatched at `PENDING`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7:** branch from this branch, re-sync at the **start** of your run.
> ⭐ **Rule 4:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `U-n` are **plan labels, not tracker ids.**
>
> ⛔⛔ **NO FEATURE CODE.** The deliverable is an assessment.
> ⚠ **Runs AFTER [Batch 39](HANDOFF_Batch39_Finish_Local_Variables.md)** (finish `BP-57`).

---

## 0. What this is

📄 **[PLAN_Variable_Unification_Tasks.md](PLAN_Variable_Unification_Tasks.md)** — the unification broken
into **14 tasks with headless gates**, grouped into **batches 41–49**.

⭐ **You reviewed the design and it changed the design.** This reviews the *plan*: are the tasks the
right cut, are the gates real, and is the sequence survivable?

> **The user's framing:** *"each has its well defined success gates (headless unit tests) with clearly
> defined success conditions … usually such checks reveal weak spots to fill before breaking to
> actionable tasks."*

⚠ **The same standing instruction as Batch 38:** ⛔ **do not soften findings to be agreeable.** A plan
review that returns *"looks fine"* has cost a batch. **If a gate cannot be written, say so — that is
the single most valuable thing you can return.**

---

## 0a. ⚡ How to work

**Opus keeps the verdicts.** 🟢 Sonnet may take mechanical checks (does this API exist, how many call
sites). ⭐ **Throwaway probes expected and deleted; nothing temporary committed.**

⚠ **Gates:** no product change ⇒ **run build + Blueprints once at each end** and report both.
⚠ **The closing build will be INCREMENTAL and under-report warnings** — Batch 38 hit this and recorded
it honestly. **Do the same; do not print `69` from memory.**

**Baseline** — build **0 errors / 69 warnings** · Blueprints **3243** / 0 / 10 ⚠ *(**3259** if Batch 39
has merged the recovered work — say which tree you measured)*.

---

## 1. The deliverable

**`docs/blueprints/REVIEW_Unification_Plan.md`**, answering:

| | |
|---|---|
| **1** | ⭐⭐ **Is every gate WRITABLE as specified?** Task by task. **A gate you cannot write is a task that cannot be verified** |
| **2** | ⭐ **Is `U-1`'s golden harness actually achievable** — and is it the right invariant? |
| **3** | **Is the task cut right?** Anything too big to revert · too small to be its own · in the wrong order |
| **4** | ⭐ **What is MISSING** — §3 names where the coordinator did not look |
| **5** | **Is the 41–45 / 46–49 split real?** ⭐ *"stop after 45 and everything shipped is coherent"* — **is that true?** |
| **6** | 📐 **Verdict**: run it · run it with named changes · ⛔ or re-cut it |

---

## 2. ⚠ The gates most likely to be unwritable — start here

⭐ **These are the coordinator's own suspicions. Confirm or kill each.**

| | gate | the doubt |
|---|---|---|
| **U-1** | *"compile all 42 shipped assets in a test"* | ⚠ **Do the tests have access to them?** They live in `Hrot.AI.Behaviors/Assets/Blueprints`, and the compile path in production is the **generator** over `AdditionalTexts`. **Can `Hrot.Blueprints.Tests` reach and compile all 42 — and how long does it take?** ⛔ **If this is not writable, the whole plan loses its net** |
| **U-3** | *"an asset with BOTH `Variables` and `WorkingState`, constructed past Stage 2"* | ⚠ `BP1024`/`BP1031` refuse it **at Stage 2**. **Is there a seam to build one anyway** — call the scheduler directly, `InternalsVisibleTo`, a test-only flag? ⛔ **If not, `BP-226`'s fix cannot be shown to fix anything** |
| **U-6** | *"the provider handles `Variable` and `LocalVariable`"* | ⚠ **is `IDetailsViewProvider` registration reachable headlessly**, or does it only exist inside a window that needs ImGui? |
| **U-7** | *"with no oracle, compiles exactly as today"* | ⚠ **what is "exactly as today" for an asset whose type is bogus?** Today it emits `global::Totally.Made.Up.Type`. **Is preserving that the right fallback, or should the editor path warn?** |
| **U-10** | *"v1 → v2 → v1 is the identity, byte-for-byte"* | ⚠ **Is it?** `JsonStringEnumConverter`, no `DefaultIgnoreCondition`, property order from the type. **A round trip may normalise something that was hand-authored** — the four numeric `Dispatch: 1` assets are the obvious probe |
| **U-11** | *"golden unchanged at every sub-step"* | ⚠ **can the buckets actually be separated**, or does moving the compiler off the old views break the editor in the same commit? |

---

## 3. ⛔ Where the coordinator did not look

| | |
|---|---|
| **3.1** ⭐ | **How long does the golden harness take?** 42 assets × full compile, on every test run. ⚠ **If it is minutes, it is not a gate — it is a nightly.** **Measure it and say** |
| **3.2** ⭐ | **What does `U-1` record, exactly?** *"generated-source hash"* is brittle — a comment change breaks it. **Is the right invariant the SOURCE, or the `StructureHash` + struct layout + diagnostic set?** 📐 **Propose the invariant you would actually trust** |
| **3.3** | **Is `U-9` really golden-neutral?** A tagged decl with new members changes serialization ⇒ **`U-9`'s round-trip and `U-10`'s migration may not be separable.** ⚠ **If they collapse, say so — that changes the batch count** |
| **3.4** | **Does anything outside this repo read `.bp.json`?** Batch 38 searched **this repository only**, and said so |
| **3.5** | ⭐ **Is there a task missing between `U-5` and `U-6`?** The capability flag `U-5` adds has to be **consumed** by the shared table — **is that a `VariablesPanelControl` change, i.e. an `Hrot.Editor.AiShared` change that moves the AiShared gate?** |
| **3.6** | **`U-2` and `U-3` are both compiler-only and both in flight before `U-9`.** ⚠ **Do they conflict with Batch 39's merged locals work**, which touched `FieldLayout`, `StructureHash` and `MacroLatency`? |

---

## 4. 📐 Two rulings to pressure-test

| | the claim | test it |
|---|---|---|
| ⭐ **`U-1` first** | *"without the harness the programme is unfalsifiable"* | is a golden harness the right net, or does it **entrench** current behaviour including its bugs? ⚠ **`BP-226`'s wrong resolution is IN the golden set** — `U-3` must therefore declare a change. **Does that undermine the invariant?** |
| ⭐ **stop-after-45** | *"everything shipped is still coherent"* | ⚠ **is a half-unified editor coherent**, or is it two ways to edit a variable — the exact defect this programme exists to remove? |

---

## 5. Reporting

Gates at both ends · ⭐ **per-task: gate writable / not writable / writable-with-changes** ·
⭐ **your §3.1 timing measurement and §3.2 invariant proposal** · **every id you allocated** ·
⭐ **your §1.6 verdict** · **anything in the plan wrong against the code.**

⚠ ⭐ **Say what you could not establish.** Batch 38 did, and it was the most useful paragraph in it.
