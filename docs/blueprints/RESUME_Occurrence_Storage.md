<!--STATUS
state: LIVE
doc-type: LANE RESUMPTION for the `behaviors` lane — programme ②, OCCURRENCE-SCOPED STORAGE.
  ⚠ A STATE doc, not canon. Every "green"/"pushed"/"HEAD" line is a snapshot dated below.
  ⛔ VERIFY against git before acting ("THE LEDGER MAY NOT ASSERT WHAT THE CODE IS").
updated: 2026-09-20
build-state: n/a — a resumption snapshot, not a design.
current-answer: §3 — WRITE THE PLAN. That is the one job in flight. §1 is the state in one
  table; §4 is the five traps this session paid for, and §4 is the section most worth the
  two minutes, because three of them were MY errors that reached a pushed document.
stale-below: nothing — new document.
known-rot: nothing yet.
known-conflict: RESUME_Assets_And_Occurrences.md is the COORDINATOR snapshot (2026-09-19) and
  owns TWO programmes. ⛔ It predates everything below: it still says the occurrence design has
  no PLAN (true) but also treats §3.2's defect as "shipped" and the AI-entity-count as the first
  measurement to take (both superseded — see §2). Neither doc supersedes the other; this one is
  the LANE view and is newer.
related-designs:
  - DESIGN_Occurrence_Scoped_Storage.md — ⭐ THE OWNING DESIGN. Start at its §16.
  - Blueprint_Issues_Tracker.md — CE-295 (open), CE-296 (refuted). Area F.
  - Architect_Question_37_Unify_On_The_Allocator.md — the owning question.
  - RUNBOOK_Cluster_Debugging_Over_Http.md — how to run the golden test. §2.1 is load-bearing.
-->

# RESUMPTION — **occurrence-scoped storage**, the `behaviors` lane

RELEARN

> ⭐⭐⭐ **You are the `behaviors` lane on branch `behaviors`, and you OWN this design.**
> 🔒 **User, `2026-09-20`: *"you take it from here, you are the one owning the design now."***
> ⭐ **Nothing is in flight but the PLAN.** No code change is pending; the only engine edit this
> session made is a two-line logging fix (§5).

## 0. ⭐ FIRST MOVES

```bash
python3 scripts/session-design-brief.sh          # ledger · digest · probe · 3 random rulings
# then read docs/blueprints/RULINGS.md IN FULL   (RULE ZERO)
git fetch origin && git log --oneline -1 origin/behaviors    # snapshot: e3b04dc47 + the log fix
python3 scripts/rulings-check.py && python3 scripts/design-digest.py --check
```

⚠ **Snapshot `2026-09-20`:** `rulings-check` **37/37** · `design-digest --check` OK (67 docs) ·
⛔ `tracker-counts --check` says nothing about our rows — **it counts only `BP-` rows** (`CE-073`).

---

## 1. THE STATE, IN ONE TABLE

| | |
|---|---|
| **owning design** | [`DESIGN_Occurrence_Scoped_Storage.md`](DESIGN_Occurrence_Scoped_Storage.md) — `build-state: DESIGN`, **start at §16** |
| **design work** | ✅ **FINALIZED.** Settled / measured / open are all in §16 |
| **measurements** | ✅ **CLOSED** — bytes-per-AI-entity, slots-per-behaviour, AI entity count (§2) |
| ⭐ **golden test** | ✅ **GREEN** — `hill-attack-close`: both targets destroyed, all 4 back on baseline. Reproduced **3×**. It is the **GATE**: green before *and* after every `O`-item |
| ⛔ **the one job** | ⭐⭐⭐ **WRITE THE PLAN** — §3 |
| **defects filed** | `CE-295` open *(scenario live-reload is a one-shot — filed NOT fixed, user's call)* · `CE-296` **refuted** *(my error)* |
| **decisions** | `D1′` *(Kind = nibble in the header)* · `D2` *(guards served)* · `D3` *(AI-state route becomes a list)* — all in design §16.1 |

---

## 2. ⭐ WHAT IS MEASURED — ⛔ do not re-measure, do not re-derive

| # | measurement | result |
|---|---|---|
| ① | **bytes per AI entity** | **192 B** BTree root · **256 B** HSM root · ⛔ **no heavy-DTO credit** ⇒ 256 tier **free–1.33×**, 1024 tier **4–5.3×** ⇒ ⭐ **`O3b` is load-bearing, not an optimisation** |
| ② | **slots per behaviour** *(30 generated assets)* | 0 ×17 · 1 ×6 · 2 ×5 · 3 ×1 · 8 ×1 ⇒ **77 % fit a 256 tier**. Worst case `PlatoonHillAttack2` needs 9 ⇒ the `MaxSlots` ladder 4/8/16 must be **re-picked** in `O3a` |
| ③ | **AI entity count** | **single-digit everywhere** — 4 brained of 8 in `hill-attack(-close)`; `test-move` 1, `test-fire` 2. ⚠ **No exercise-scale scenario exists in the repo** — say so, do not extrapolate |
| ④ | `Blackboard1024` attachment | **zero entities, both nodes** ⇒ §3.2's defect is **LATENT**, not shipped |
| ⑤ | attributed `[HsmGuard]` | **8** in `.cs`, **zero in production** |
| ⑥ | promotion sites | **three**, all funnelling through `CopyToLargerTier` |

---

## 3. ⭐⭐⭐ THE ONE JOB — **write the PLAN**

⭐ Shape it like [`PLAN_Asset_Management_Build.md`](PLAN_Asset_Management_Build.md): **one task per
`O`-item**, a **success condition each**, an **under-specified register**, a **dispatch grouping**.
⛔ The design has the sequence; it does **not** have per-task success conditions.

| ⭐ the order — ⛔ `O0` is NOT first any more | |
|---|---|
| **`O3` → `O0` → `O1` → `O2` → `O3a` → `O3b` → `O4` → `O5` → `O6` → `O7` → `O8` → `O9`** | `O0` needs `D1′`'s declared `Kind`, which `O3` delivers |
| ⭐⭐ **`O4` is the STOP-OR-GO gate** | it proves the model with **zero** ExtDeps edits. ⛔ If it fails, stop before paying for `O6` |

| ⛔ four things the PLAN must carry, or a batch will rediscover them | |
|---|---|
| **`H1`** | `CopyToLargerTier` never copies `Reserved` ⇒ **every tier promotion zeroes the `Kind` nibble array**. One line, covers all three promotion sites. ⭐ **Red-first rail** — a zeroed array is indistinguishable from "all kind 0" |
| **`H2`** | `Kind == 0` is `Invalid`, never a valid kind; `TryDetach` must **clear** the vacated tail nibble, not only move it |
| **`O3a`** | re-pick the `MaxSlots` ladder **with a sizing rationale** — ⛔ not inherited constants |
| **`O4`** | the hosted child's **re-entry reset**, with its own rail |

⚠ **Verification constraint from `CE-295`:** `load_scenario_live` works **once per process** ⇒
⭐ **one scenario per process** when verifying, or the harness silently re-tests the first one.

---

## 4. ⛔⛔ THE FIVE TRAPS THIS SESSION PAID FOR

⭐ Three of these were **my own errors that reached a pushed document**. They are here because the
correction cost more than the finding was worth.

| # | trap | ⭐ the checkable habit |
|---|---|---|
| **①** | 🔴🔴 **"WHERE THEY STARTED" IS NOT "WHERE THEY BELONG."** I read the platoon's `t=0` **spawn** (`x≈446`) as the baseline, so the correct end state (`x≈523–531`) read as *"left on the firing line"* — I filed `CE-296`, blocked the programme on a red golden test, and had to refute it in the same session | ⛔ **resolve the AUTHORED value** (`behaviorParams.baselineStart/End`), ⭐ never a `t=0` reading. ⚠ And when a trace shows a thing moving **toward** your "failure" position (`1002` drove 587→525), that is a tank **arriving** |
| **②** | 🔴 **ONE DATA POINT IS NOT A DISCRIMINATOR.** A Windows run reported success; I concluded *"the defect is mode-specific"* and said so — **without running the other mode** | ⭐ **run the A/B on one box before naming a discriminator.** It cost one turn to do and would have cost nothing to wait |
| **③** | 🔴 **A GREP IS NOT A CENSUS.** `\[HsmGuard\]` with a literal `]` missed every `[HsmGuard(Name = …)]` ⇒ I called a correct census *"fabricated"*. 📐 5 lines vs the real **15** | ⛔ **anchor on the attribute NAME, not the bracket** — `\[HsmGuard` — ⭐ and open the lines before counting them |
| **④** | ⚠ **THE DISCONFIRMING EVIDENCE WAS ALREADY IN THE RUN.** I proposed attaching `Blackboard1024` to test a hypothesis the **succeeding** phase of the same run had already falsified — the assault worked, driven by the same AiPrimitives | 🔒 **when a hypothesis predicts a failure, check whether the SAME mechanism visibly SUCCEEDED elsewhere in the same run** |
| **⑤** | ⚠ **`ok:true` IS NOT A LOAD.** `load_scenario_live` answers `ok:true` while changing nothing after the first call | ⭐ **read `sawWorldChange`**, and verify with `GET /entities`. 📌 This is `CE-295`, found *because* the runbook says to |

⭐ **And two operational ones, both in the runbook already and both re-paid anyway:**
⛔ `127.0.0.1` 404s on **every** route — `HttpListener` binds the hostname, use `localhost` (§2.1) ·
⛔ `pkill -f '<pattern>'` matches **your own command line** and kills the shell (exit 144) — use a
PID loop, and ⛔ never pipe a long-running script through `tail` (it buffers everything to the end).

---

## 5. ⛔ DONE THIS SESSION — do not redo

| | |
|---|---|
| ✅ design finalized — `H1`/`H2`/`D3` folded, slots column, corrected C1 arithmetic, §15 live-run record, §16 checklist | `3ca68dbd2` |
| ✅ `§3.2` corrected from **shipped** to **LATENT** *(gated on `HeavyDtoType`, which production sets nowhere)* | `bfb0baf53` |
| ✅ `CE-295` filed *(not fixed)*, `CE-296` filed then **refuted** | `17feaf94a`, `e3b04dc47` |
| ✅ the debug-API log fix — editor line now goes through `FdpLog` and **matches the cluster's phrase** | §5's own commit |

⭐ **Why the log fix mattered:** the editor logged via `Console.WriteLine` with a *different* wording,
so a host capturing the log but not stdout had **no record**, and grepping the cluster's phrase found
nothing ⇒ *"the debug API is absent in `-m editor`"* was reported while it was running fine.
