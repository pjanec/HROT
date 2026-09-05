# HANDOFF — Batch 64: ⭐⭐⭐ **read the design record FIRST**, then `S2` · `W6` · `W7` · the race

> 📌 **Dispatched at `a431c429c`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ✅ **Batch 63 VERIFIED AND MERGED at `9edf13fdf`** — all eight gates coordinator-run, **green**;
> Tier 1 and `persistence-shape.txt` untouched; the 30-occurrence projection swap corroborated.
> ⭐ **Rule 7 / Rule 4** as always. ⛔ **Rule 3: the coordinator allocates no ids.**
> ⭐ **One commit per item · per-item STOP conditions.**

---

## 0. ⛔⛔⛔ **THE LESSON FROM BATCH 63 — now a binding rule in `.claude/CLAUDE.md`**

> ⭐⭐⭐ **User, verbatim:** *"what is not used does not mean it is existing without reason — a design
> doc gives answers."*

⛔ **My lean was DELETE. You routed instead, and you were right** — because you went and found the
design record I never looked for:

| | |
|---|---|
| **`.dev/_DONE/btree-ai-action-binding/SLICE1-DESIGN.md:82`** | ⭐⭐ **names the expression verbatim** — *"the BTree generator **ignores** the blueprint's standalone `BTreeTick` (with its `paramIndex*sizeof` math)"*, under the architect ruling *"BTree owns layout, blueprint provides `TickCore`"* |
| **`SLICE2-DESIGN.md` §6.2** | *"the blueprint's own `BTreeTick`/`Memory+8` path stays the **standalone** blueprint-as-behavior hosting"* |

⭐⭐ **And the distinction you drew is the part worth keeping:** `W3`'s stubs were **unreachable AND
harmful** *(last-writer-wins overwrite)* ⇒ delete. This was **dormant** *(a unique key overwriting
nothing)* ⇒ route. ⛔ **I collapsed two properties into one and applied the precedent to the wrong half.**
⭐ **Your `@0` insight closes it:** standalone hosting **is** the single-method case, so `@0` was true
for the case the thunk exists for — projecting at a literal `0` makes it **true by construction**, and
that is `W1`'s third rail seen from the other end.

---

## 1. ⭐⭐⭐ ITEM ONE — **the `.dev/` sweep. Do this before any code.**

🔴🔴 **There are ~2900 markdown files under `.dev/` and this programme has never read them.** Every
design decision in the remaining plan was derived **from code alone**. ⛔ **That is how Batch 63 nearly
deleted a documented capability.**

📐 **Sweep `.dev/` for design records covering the REMAINING plan** — 📄
[`PLAN_Remaining_Work.md`](PLAN_Remaining_Work.md) — and report **what it already rules**:

| track | items to look up |
|---|---|
| **B** | `S2` *(below)* · **`S3`** the `MarshalFromBytes` struct arm · **`S4`** fixed-list `Capacity` · **`S5`** one picker vs two |
| **C** | the Details/Watch panels, the value column, the write path — ⚠ **`.dev/ai-hsm-btree-vis-edit*/` and `Blackboard_Authoring_Detailed_Design.md` are already visible in a grep** |
| **D** | `W8`–`W12` — the reserved input variable, the initializer picker, the `Construction` initializer |

⭐⭐ **Report as a table: item → design record (or "none found") → does it CONFIRM, REFINE or CONTRADICT
what the plan says.** ⛔ **Do not silently rewrite the plan** — ⭐ **report, and the coordinator revises
it.** 🛑 **STOP and report immediately if a record CONTRADICTS a dispatched design** — that outranks
everything else in this batch.

⚠ **Timebox it.** ⛔ **Do not read 2900 files** — ⭐ **grep for the item's own vocabulary, read what
hits, and say what you did not cover.** 📐 **An honest "I swept for X, Y, Z and not for W" is the
deliverable; a claim of completeness is not.**

---

## 2. `S2` — struct size resolution ⭐ **and it has a design record too**

⭐ **Coordinator-grepped, so you start from the pointers rather than the search:**
`.dev/_DONE/btree-ai-action-binding/` — **`TASK-DETAIL.md`**, **`batches/BATCH-03-INSTRUCTIONS.md`**,
**`batches/BATCH-06-INSTRUCTIONS.md`**, **`reviews/BATCH-03-REVIEW.md`**, **`reports/BATCH-03-REPORT.md`**
all mention curated-struct registration / `StructSizeResolver` / `SizeReliable`.

⇒ ⛔ **Read those before choosing a placement.** ⭐ **`StaticTypeRegistry`'s own comment says *"a general
curated-struct registration mechanism … is future work"* — `.dev/` may already say what that mechanism
is meant to be.** ⚠ **If it does, that supersedes my lean.**

| | |
|---|---|
| 🔴 **the defect** | an unregistered struct resolves at a **guessed 4 bytes**; three are hardcoded with hand-computed sizes |
| ✅ **no project cycle** | Generators references Schema + Persistence, ⛔ **not the compiler** |
| ⚖️ **lean, now SUBORDINATE to the design record** | the existing `IClrSignatureResolver` seam, or move `StructSizeResolver` where both can see it — ⛔ **not a new Compiler→Generators reference** |
| **gate** | an unregistered user struct gets its **real** size · ⭐ **reuse Batch 60's `EmittedStateLayoutTests`** |
| 🛑 **STOP if** | a new project reference is the only workable placement · **or** a **shipped** asset uses an unregistered struct *(live wrong-layout defect)* |

---

## 3. `W6` → `W7` *(carried a fourth time — and these have records too)*

⭐ **Coordinator-grepped pointers:** `.dev/_DONE/blueprint-finalize/reports/AN3-REPORT.md` ·
`AN7-REPORT.md` · `.dev/_DONE/ai-hsm-btree-vis-edit-2/DECISIONS.md` · `.dev/_DONE/ai-hsm-btree-vis-edit/design-talk.md`.

| | |
|---|---|
| **`W6`** | guard read-only projection — `GetComponent` not `GetComponentRW`; `in`/`ref readonly` at the thunk boundary. ⭐ Invariant: *"a speculative evaluation may not be observable."* 📐 **Re-measure the `[SharedAiCondition]` usage count and state it** |
| **`W7`** | concurrent-region rule — error on concurrent **writers**, permit concurrent **readers**. ⚠ needs `W6` |
| 🛑 **STOP if** | the count is not ~0 · or `W6` did not land cleanly · ⭐ **or a `.dev/` record rules differently** |

---

## 4. ⚠ The `Fdp.Toolkits.Tests` race *(carried)*

`StatelessGizmoRegistryTests.SC_GZ022_2` — ⭐ **1 · 1 · 2 failures across three runs of an identical
binary**, passes in isolation. ⚠ **It came back 1942/1942 on your run and again on mine — ⛔ which per
my own warning is not evidence it is gone.** ⭐ **A race that hides is still a race.**
📐 **File it; fix it if the cause is a shared static registry.**
🛑 **STOP if** the cause is **production** static state — much larger finding.

---

## 5. Gates

**Baseline — coordinator-run at `9edf13fdf`:** build **0 errors / 69 warnings** · Blueprints
**3618 / 3608 / 0 / 10** · AiShared **1216** · BTree **612** · Breakpoints **130** · Generators **196** ·
Toolkits **1942** · NodeEdit **208 / 131**.

| | |
|---|---|
| 🔴🔴 **`StructureHash` unchanged for all 43** | ⚠ **`S2` is the item that could move it — if it does, STOP** |
| **`persistence-shape.txt`** | ⛔ **UNCHANGED** |
| ⭐ **golden Tier 1 unchanged** · Tier 2 movement **declared per item** | |
| ⭐ **per-item revert-goes-red** · `tracker-counts.py --check` clean | |

---

## 6. Reporting

⭐⭐⭐ **The `.dev/` sweep table — item → record → confirms/refines/contradicts — AND what you did not
cover** · ⭐⭐ **whether `.dev/` already rules `S2`'s placement** · ⭐ **the `[SharedAiCondition]` count** ·
⭐ **the race's cause, or that you could not localise it** · 🔴 **`StructureHash` unchanged** ·
per-suite numbers **full and filtered** · `tracker-counts.py --check` · ⭐ **every id you allocated**.

⭐⭐⭐ **The question to carry:** ⛔ **This programme has spent 60+ batches deriving from code what a
design corpus may already state.** 📐 **Now that you have read some of it — which of our hard-won
"findings" were already written down, and where should a session LOOK FIRST next time?**
⚠ **That answer belongs in `.claude/CLAUDE.md`, not in a batch report.**
