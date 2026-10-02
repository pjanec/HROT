<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-21
current-answer: dispatch pointer for the TIME lane — build the PreFrame drain (W1/W2). The designs are
  DESIGN_Time_Architecture.md §10 (the drain) and DESIGN_Staged_Live_Write.md §5/§6 (the seam + split).
-->
# HANDOFF — TIME lane · **W1 + W2: the PreFrame drain** *(after T1/T2)*

> 📌 **Dispatched at `68c1855d5`.** ⭐ **TIME lane** — branch `claude/time-system-refactor-batch-104-gp617x`,
> ids **`TM-`**, tracker **`Area H` ONLY**. ⭐⭐ **Rule 7 (two-lane form):** continue on your own branch,
> `git merge origin/claude/blueprint-authoring-status-gm0akp` for canon *(do NOT branch fresh)*.
> ⭐ **Rule 1b: push `chore: started TM W1/W2 at 68c1855d5` FIRST.** ⭐ **Rule 3: your own `TM-` ids.**
> ⚠ **This is the FIRST of the held W-tasks** — take it only **after T1/T2 land** *(the drain uses T1's
> `IsAdvancing`)*.

## 0. ⭐⭐⭐ THE DESIGNS — **build from them; this handoff does NOT redraw them**

| 📄 | what to read |
|---|---|
| [`DESIGN_Time_Architecture.md`](../DESIGN_Time_Architecture.md) **§10** | ⭐⭐⭐ **the drain** — its `classDiagram` *(`ResumeAndDrainSystem`, `PreFrame`, `IStagedWrites`)* and `sequenceDiagram` are THERE. ⛔ build §10, do not redraw |
| [`DESIGN_Staged_Live_Write.md`](../DESIGN_Staged_Live_Write.md) **§5, §6** | ⭐ the `IStagedWrites` seam contract + the W-task lane split *(you own the seam CONSUMER; the coordinator already DEFINED the interface)* |

⭐⭐ **The seam is already defined** — `IStagedWrites` lives in `FDP/Engine/Fdp.Core/Abstractions/IStagedWrites.cs`
*(`HasPending` · `IsRewound` · `DrainInto(ISimulationView)` · `TryGetPending(...)`)*. ⛔ **Do NOT change its
shape** — the UI lane builds `DataBreakpointManager` to it in parallel. You **consume** it.

## 1. ⛔⛔⛔ `W1` — **`SystemPhase.PreFrame` + the one kernel line**

⭐ Add a `PreFrame` phase that runs **before `Input`** *(~25 state-mutating systems — the drain must
precede them so a restored/edited repo is what Input sees)*. 📌 §10: *"the scheduler is phase-generic"* ⇒
`W1` is proven and small.

## 2. ⛔⛔⛔ `W2` — **`ResumeAndDrainSystem`: the PULL**

⭐⭐ Mirror `DebugSnapshotProvider`'s shape. Each `PreFrame`:

| ⭐ | |
|---|---|
| ⛔⛔ **gate on the `deltaTime` PARAMETER, not `GetMode()`** | 📌 `AS-10`/`AS-1b`: read the ADVANCING signal from **T1's `ISimClock.IsAdvancing`** over the live view — ⛔ never `GetCurrentState()` |
| ⭐ **skip while `IStagedWrites.IsRewound`** | 📌 `R-63`: a breakpoint holds the pre-tick snapshot; its own resume path drains |
| ⭐⭐ **when advancing and not rewound ⇒ `staged.DrainInto(view)`** | the staged bytes land via the view's command buffer, then the set is empty |

⇒ ⭐⭐⭐ **PULL, not a release event** *(`R-126`)* — the loop asks every advancing frame; no caller can forget.

## 3. ⭐⭐ HOW TO BUILD & RAIL IT WITHOUT THE UI LANE

⚠ **The production `IStagedWrites` implementer is `DataBreakpointManager` — UI lane, built in parallel
(`W4`).** ⭐⭐ **You do NOT wait for it:** build `W1`/`W2` against the **interface**, and rail with a
**fake `IStagedWrites`** *(a test double that reports N pending then confirms `DrainInto` was called
exactly on advancing, non-rewound frames)*. ⛔ **The real wiring** *(hand the kernel the
`DataBreakpointManager` as `IStagedWrites`)* is a **composition step for later — NOT this batch.**

| ⭐ rails | |
|---|---|
| ⭐⭐⭐ **drains on an advancing frame** | `dt > 0`, not rewound ⇒ `DrainInto` called once ⇒ the fake's pending set empties |
| ⭐⭐ **does NOT drain while halted** | `dt == 0` ⇒ `DrainInto` NOT called |
| ⭐⭐ **does NOT drain while rewound** | `IsRewound == true` ⇒ `DrainInto` NOT called *(`R-63`)* |
| ⭐ **runs before Input** | assert the phase ordering |

## 4. ⛔ NOT IN THIS BATCH

⛔ `W3`/`W4`/`W5` *(staging, the yellow display, the restore move — UI lane)* · the `DataBreakpointManager`
implementer · the composition wiring · ⛔ **`T3`+** *(resume those after the W-drain, or as the user
directs)*.

## 5. ⭐ GATES

⭐ Baseline = your last table. ⭐⭐ **The STANDING integration row** *(`TM-003`)*: `TimeControlIntegrationTests`
must stay **`9/0`**. ⭐ `Fdp.Toolkits.Tests ▸ ThePauseFlagOnTheClockIsFalseWhilePaused` **4/0**. ⚠
`DEBT-AIB-030` confirmed by filter. ⭐ `tracker-counts.py --check` + the **`TM-` ids you allocated**
*(Area H)*. ⭐ **Rule 4: merge the coordinator branch again before your final commit.**
