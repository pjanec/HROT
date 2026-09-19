<!--STATUS
state: LIVE
doc-type: COORDINATOR RESUMPTION for TWO design programmes — asset management, and the blackboard ->
  occurrence-scoped storage conversion. ⚠ A STATE doc, not canon: every "ready"/"merged"/"HEAD" line is a
  snapshot dated below. ⛔ VERIFY against git before acting ("THE LEDGER MAY NOT ASSERT WHAT THE CODE IS").
updated: 2026-09-19
build-state: n/a — this is a resumption snapshot, not a design.
current-answer: §0 first moves · §1 the two programmes in one table · §2 ASSET MANAGEMENT (ready to
  dispatch, nothing blocked) · §3 OCCURRENCE STORAGE (design landed, NOT yet planned or dispatched) ·
  §4 what is DONE and must not be re-opened · §5 the traps this handover would otherwise repeat.
stale-below: nothing — new document.
known-rot: nothing yet. ⚠ §4's "done" claims are dated; re-derive with §0's commands.
known-conflict: docs/blueprints/COORDINATOR_RESUMPTION.md is the OLDER coordinator snapshot
  (2026-09-15) and owns a DIFFERENT programme (`cgf == editor`). It does not mention either programme
  here. ⛔ Neither supersedes the other — check which programme you were handed.
related-designs:
  - docs/DESIGN_Asset_Management.md — programme ① owning design (READY-TO-BUILD).
  - docs/blueprints/PLAN_Asset_Management_Build.md — programme ① task breakdown, 15 tasks.
  - docs/blueprints/Architect_Question_72_Asset_Lifecycle_And_Distribution.md — programme ① decisions.
  - docs/blueprints/DESIGN_Occurrence_Scoped_Storage.md — programme ② owning design (build-state DESIGN).
  - docs/blueprints/Architect_Question_37_Unify_On_The_Allocator.md — programme ② owning question.
  - docs/DESIGN_Artifact_Staging.md — BUILT; programme ① is its generalisation.
  - docs/DESIGN_Cluster_Load_Phase.md — BUILT; programme ① derives its needs tokens from it.
  - docs/DESIGN_Terrain_Zones_And_Assets.md — COMPLETE; the programme that produced ①.
-->

# RESUMPTION — **asset management** and **occurrence-scoped storage**

RELEARN

> ⭐⭐⭐ **You are the COORDINATOR** on `claude/blueprint-authoring-status-6sr5ld`. ⛔ You do NOT implement.
> You frame, dispatch, and **verify + merge** returned diffs. 🔒 **Rule 8: the REPORT substitutes for
> re-running the gates** — read the diff, spot-check a *surprising* claim, ⛔ do not re-run the suite.
>
> ⭐ **Nothing is in flight.** Both programmes are handed over at a clean boundary: no batch is
> dispatched, no session is mid-run, and no decision is waiting on you that the user has not seen.

## 0. ⭐ FIRST MOVES — re-derive the live state

```bash
python3 scripts/session-design-brief.sh      # ledger · 7-day digest · probe verdict · 3 random rulings
# then read docs/blueprints/RULINGS.md IN FULL  (RULE ZERO)
git fetch origin
git log --oneline -1 origin/claude/blueprint-authoring-status-6sr5ld   # coord   (snapshot: d3a0604ab)
git log --oneline -1 origin/claude/blueprint-macro-feature-sdmspn      # BACKEND (snapshot: e735e5e17)
git log --oneline -1 origin/claude/reset-working-branch-qd1qpv         # UI/CGF  (snapshot: 21eb374c9)
python3 scripts/rulings-check.py && python3 scripts/tracker-counts.py --check
```

⚠ **Snapshot `2026-09-19`:** coordinator `d3a0604ab` contains **everything** below, including the
backend lane merged in. `rulings-check` 36/36 · `design-digest --check` OK (66 docs) · tracker
**112 open / 379 done**.

---

## 1. THE TWO PROGRAMMES, IN ONE TABLE

| | ① **ASSET MANAGEMENT** | ② **OCCURRENCE-SCOPED STORAGE** |
|---|---|---|
| **what** | the NAS is the master; a node holds a copy of exactly what it will load | the **occurrence**, not the entity, owns memory ⇒ HSM + hosted BTree + blueprints run concurrently on one entity |
| **owning design** | [`DESIGN_Asset_Management.md`](../DESIGN_Asset_Management.md) — **READY-TO-BUILD** | [`DESIGN_Occurrence_Scoped_Storage.md`](DESIGN_Occurrence_Scoped_Storage.md) — **`build-state: DESIGN`** |
| **decision record** | [`AQ-72`](Architect_Question_72_Asset_Lifecycle_And_Distribution.md) — ✅ **fully resolved** | [`Q37`](Architect_Question_37_Unify_On_The_Allocator.md) *(+ `Q33`–`Q36`)* — ✅ reopened and answered |
| **plan** | ✅ [`PLAN_Asset_Management_Build.md`](PLAN_Asset_Management_Build.md) — **15 tasks, 3 batches** | ⛔ **NONE — this is your first job** *(§3)* |
| **who authored it** | this coordinator | the **backend lane**, merged at `d3a0604ab` |
| **next action** | ⭐ **dispatch batch ①** — nothing blocks it | ⭐ **write the PLAN**, then dispatch `O0`/`O1` |
| **decisions open** | ⛔ **none** | ⚠ one measurement `Q37` never took — see §3.2 |

---

## 2. ① ASSET MANAGEMENT — **ready to dispatch, nothing blocked**

### 2.1 The state, in one paragraph

`AQ-72` is **fully ruled** across three rounds with the user. The design is **READY-TO-BUILD** and has
survived **three review rounds by the backend lane** (`H - back`), which found **twelve** defects — all
folded in, each with the prior claim preserved in the design's `## ⛔ HISTORY` table so it cannot be
re-quoted. The plan is **15 tasks in 3 batches**. ⛔ **Nothing is dispatched.**

### 2.2 The three batches

| batch | tasks | state |
|---|---|---|
| **① A** | `A1`–`A3` — the recursive per-file manifest *(the enabler: recursion exists **nowhere** today)* | ✅ **dispatchable now.** Carries **no wire-contract change** *(that moved to `C1`)*, and is fully railable without a cluster |
| **② B** | `B1`–`B6` + `B4a` — needs-filtered sync, the transport partition, the storage seam | ✅ dispatchable — 🔴 **`B2` FIRST, not last** *(§2.4)* |
| **③ C** | `C1`–`C5` — publish, the probe, the warn/fail split, the explicit refresh | ✅ dispatchable, independent of B. ⚠ deferring it now has a cost *(§2.4)* |

### 2.3 The four rules that carry the whole design — ⛔ do not let a batch re-derive any of them

| | |
|---|---|
| ⭐⭐⭐ **ONE freshness predicate** | `(length, mtime)`, **BUILT and railed** in `DESIGN_Artifact_Staging` §6. 🔒 User ruled **no hash, no sidecar**. ⛔ A second freshness rule is a review finding |
| ⭐⭐⭐ **keyed on the AT-REST pair, never on the transport** | + 🔴 **the unpack MUST restore each member's mtime** — ZIP's **2-second DOS grid** vs an exact predicate, or the archived set re-transfers **forever** |
| ⭐⭐ **NAS form == node form**, shape AND structure | subfolders of **any depth** — 🔒 *"this is a way how **user organizes** the assets"* ⇒ authored content, not an optimisation |
| ⭐⭐ **the transport PARTITIONS** | big / pre-compressed travel **standalone**, the rest as one archive. 🔒 Not a CPU argument — a 100 GB member needs its size **twice** to unpack |

### 2.4 ⛔⛔ The two things a dispatching coordinator MUST carry into the handoff

**① `B2` is the safety task and it goes FIRST.** 📐 `CgfSubsystem.DefaultRole = NodeRole.Brain` and
`SimHostApp.DefaultRole` has **no Brain** ⇒ **every Brain host that exists is an authoring host**, reading
its AI assets from the **authoring** root. Without `B2`'s subtraction, increment B mirrors NAS **onto the
folder the author is editing** — and the probe then warns about the changes it just destroyed. The rule
has **three clauses and a bound**; all four are load-bearing, and the design's §7.3b says why each one
cannot be dropped. ⭐ Rail it as a **LOSS test**, not a filter test.

**② Deferring batch ③ now costs something it did not before.** Clause ③ makes the sync **add-only** for an
authored kind ⇒ until `C5` lands, an authoring station has **no route at all** to an update; `C3` can only
warn. That is a legitimate choice — ⛔ but make it knowingly.

### 2.5 What is DONE underneath it — ⛔ do not re-open

| | |
|---|---|
| ✅ [`DESIGN_Artifact_Staging.md`](../DESIGN_Artifact_Staging.md) | **BUILT** (S1–S4). NAS→node push of the named TKB + the staged header, with the `(length, mtime)` skip. §9 is the AS-BUILT |
| ✅ [`DESIGN_Cluster_Load_Phase.md`](../DESIGN_Cluster_Load_Phase.md) | **BUILT** (L1–L8). `RoleLoadRequirements` is the per-role table programme ① **derives** its needs tokens from. ⭐ `L8` parks the transition in `ClusterMaster` — **the sync must live INSIDE the prefetch saga** or the cluster hangs |
| ✅ [`DESIGN_Terrain_Zones_And_Assets.md`](../DESIGN_Terrain_Zones_And_Assets.md) | **COMPLETE** — 4 batches, 40 items, all 9 under-specified rows closed |

---

## 3. ② OCCURRENCE-SCOPED STORAGE — **design landed, PLAN is your first job**

### 3.1 The state

The backend lane authored [`DESIGN_Occurrence_Scoped_Storage.md`](DESIGN_Occurrence_Scoped_Storage.md)
(892 lines) and it merged at `d3a0604ab`. It is the **reopening of `Q37`**, which the user parked
themselves on `2026-08-17` *("I would certainly keep this open and return to it a bit later")*.
⭐ It carries an `INVENTORY`, a `classDiagram`, a `graph TD` and a `sequenceDiagram` (3/3 parse), a
**sequence `O0`–`O9`**, 9 rails and a 7-class blast-radius table. ⛔ **There is no PLAN document.**

### 3.2 ⭐⭐ What to do first, in order

1. ⭐⭐⭐ **Read §5a before anything else.** It is the two costs `Q37` measured and an earlier draft of
   that very document **omitted both**: a **~1 KB floor per AI entity** (8× today's 128 B) and
   **indirection moving from SOME actions to ALL**.
2. 🔴 **Settle the ONE open measurement:** *"whether the ~1 KB floor matters depends on the **AI entity
   count**, which was NOT measured."* ⛔ That is the number that prices the whole programme — ⭐ **measure
   it before framing anything**, and it is cheap.
3. ⭐ **Write the PLAN** in the same shape as `PLAN_Asset_Management_Build.md`: one task per `O` item, a
   success condition each, an under-specified register, a dispatch grouping. ⛔ The design has the
   sequence; it does not have per-task success conditions.
4. ⭐ **Dispatch `O0` + `O1` first** — both are **independent of the whole model** and are pure wins even
   if the rest is cancelled.

### 3.3 The four facts that decide how you frame it

| | |
|---|---|
| ⭐⭐⭐ **`O0`–`O5` need ZERO ExtDeps edits** | the kernel boundary is crossed **once**, at `O6`, and **only after `O4` has proved the model** on the paradigm needing no kernel change. ⇒ if `O4` fails you stop before paying for anything |
| 🔴 **it found a LIVE defect in shipped code** | BTree-hosts-BTree works, but the generated orchestrator ticks the child with **the MASTER's `BehaviorTreeState`** ⇒ host and child share `RunningNodeIndex`. It is the BTree twin of the HSM two-region collision. `O4` fixes it — **the fix and the feature are one piece of work** |
| ⚠ **one part is NOT additive** | adding the small tier makes `PromoteTier`'s dispatch grow **quadratically** (3 arms → 6) because per-tier branching is hand-rolled in ~10 places. ⇒ **`O3a` collapses it to a `TierSpec` table FIRST**; then the 4th tier is genuinely additive |
| ⚠ **two rails are VACUOUS unless authored** | the two-regions rail needs **a DTO-bound HSM action written as part of `O7`** (`BP-297` measured today's fixture cannot redden it), and the hosted-subtree rail **must be written to go RED before `O4`** |

### 3.4 The user's rulings already recorded in it — ⛔ do not re-ask

| ruling | where |
|---|---|
| ✅ **the small tier is IN SCOPE** — `Q37` option **B** | §5a |
| ✅ **"still just up to one assignable behavior per entity"** ⇒ `BehaviorState` stays singular; concurrency comes from **nesting**, never a second root | §8 `Q4` |
| ✅ the rename names agreed | §10 |
| ✅ **blueprint as an ASSIGNED ROOT is COMMITTED as `O9`, after `O8`** | §12 — ⛔ and **three of its four gaps are NOT storage**: registry resolution, a root tick path, and joining `BehaviorState.InstanceId` preemption |

---

## 4. ⛔ DONE — do not re-open, do not re-measure

| | |
|---|---|
| **terrain & zones** | COMPLETE. `BP-550` closed by artifact staging |
| **artifact staging** | BUILT, S1–S4 |
| **cluster load phase** | BUILT, L1–L8 |
| **`AQ-72`** | fully ruled — `Q72-A`..`M` |
| **`R-147`** | the asset-management canon row, with its probe |
| **`R-44`** | corrected: `MAX_COMPONENT_TYPES` is **512**, highest allocated **301** — ⚠ `BitMask256.cs` still exists beside `BitMask512`, so **the type name is not the capacity** |

---

## 5. ⛔⛔ THE TRAPS THIS HANDOVER WOULD OTHERWISE REPEAT

⭐ Every one of these was paid for **in this session**, by a wrong claim that a review caught.

| # | trap | the checkable habit |
|---|---|---|
| **①** | 🔴 **A GUARD IS NOT A DECLARATION.** I cited `if (role.HasFlag(NodeRole.Brain))` as evidence SimHost *carries* Brain. It does not — `SimHostApp.DefaultRole` has none, and that error **silently cancelled a whole feature** | ⛔ to prove a role/flag/capability is **composed**, find where it is **assigned**, never where it is **tested** |
| **②** | 🔴 **Two boxes on a diagram may be one population.** §6 drew "authoring host" and "brain nodes" separately; `DefaultRole = Brain` makes them the same hosts | ⭐ before drawing two boxes, ask **what makes a node one and not the other** — and measure the answer |
| **③** | ⚠ **A predicate proven on one transport is not proven on another.** `(length, mtime)` survives `File.Copy` and **not** a ZIP round-trip | ⭐ when a mechanism gains a second path, **re-measure the invariant on the new path** |
| **④** | ⚠ **A needs-filter that subtracts something the load chain REQUIRES fails loudly.** Twice: Map2D's knowledge base, and the editor's own scenario | 🔒 **no authorship or role claim may override a `LoadPart`** |
| **⑤** | ⚠ **"unbounded over a set" is where over-broad rules hide.** The author-subtraction looked fine until it was applied to `Scenario` | ⭐ state the **domain** of every rule, not just the rule |
| **⑥** | ⭐⭐ **The backend lane's reviews were worth more than my own re-reads.** Three rounds, twelve findings, two of them blockers I had shipped as READY-TO-BUILD | 🔒 **ask `H - back` to review a design before dispatching it.** It is cheap and it has never come back empty |

---

## 6. LANES — snapshot `2026-09-19`, ⛔ confirm by ancestry, never by name

| lane | branch | last seen doing |
|---|---|---|
| **coordinator** *(you)* | `claude/blueprint-authoring-status-6sr5ld` | both programmes above |
| **backend** — `H - back` | `claude/blueprint-macro-feature-sdmspn` | authored programme ②'s design; reviewed ① three times |
| **UI / CGF** | `claude/reset-working-branch-qd1qpv` | `CE-294`, reliable-init / wire-dispose work — ⛔ **unrelated to both programmes**; do not route either lane's work there |

🔒 **Two-session protocol still binds** — rules 1–8 in `.claude/CLAUDE.md`. The ones that bite here:
**rule 1** *(never amend a dispatched handoff)* · **rule 1b** *(the started-marker closes the ancestry
blind window)* · **rule 3** *(⛔ the coordinator allocates NO ids)* · **rule 8** *(the report substitutes
for the gate run)*.
