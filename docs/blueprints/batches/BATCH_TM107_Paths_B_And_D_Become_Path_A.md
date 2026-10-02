<!--STATUS
state: LIVE
updated: 2026-08-21
current-answer: this file is a BATCH — scope, items, gates, verdicts. It carries NO design.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# ⭐⭐ BATCH TM-107 — **`T4` · `T4d` · `T5`'s first site**

> ⛔ **A batch, not a design** *(CLAUDE.md ①b)*. ⭐ **Design:**
> [`DESIGN_Time_Architecture.md`](../DESIGN_Time_Architecture.md) **§4** *(the four control paths)* and
> **§9** *(`ITimeCommands` — the three-way split)*.
> ⛔ **`W0`–`W5` are the coordinator's** *(user, `2026-08-21`)* — untouched.

| id | item | verdict |
|---|---|---|
| **`107a`** | **`T4`** — `ITimeCommands` + `IntentTimeCommands`; route **path B** *(toolbar)* | ✅ **done** |
| **`107b`** | **`T4d`** — **path D**: the tracer coordinator actually controls time | ✅ **done** |
| **`107c`** | **`T5` first site** — `ClockIsHalted()` → `SimClock` *(`TM-012`)* | ✅ **done** |
| — | **path C** *(debugger)* | ⛔ **left open, deliberately — see below** |

## `107b` — the one that was doing nothing

📐 `AiTracerCoordinator.RequestPause/RequestContinue/RequestStepOneTick` are `public virtual void … { }`
— **empty** — and `EditorSubsystem:790` constructed the **base class**.
⇒ ⛔⛔ **a BTree/HSM tracer asking the simulation to stop did nothing at all, silently.**

⭐⭐ **Subclassing is the PRESCRIBED mechanism**, found by reading the corpus first:
📄 `docs/projects/Hrot/Editor/Hrot.Editor.AiShared.md` §3 — *"Subsystem coordinators must override
AiTracerCoordinator… Pass the subsystem-specific coordinator to AiDebugSessionBase."*
⇒ ⭐⭐⭐ **and that is what kept the entire change out of the frozen `Hrot.Editor.AiShared`** *(`R-128`)*.
⚠ Had I not read it, the obvious move — edit the base class's no-ops — would have been a **cross-lane
edit into the freeze**.

⭐ The rail pins **both** halves: that the editor's coordinator forwards, **and that the base class is
silently inert** — 📌 because *"call `RequestPause()` and assert no exception"* passed for the whole
life of the defect.

## ⛔ WHY PATH C IS LEFT OPEN

⭐ `T4`'s goal is B, C and D become A. **B and D are done.** ⛔ **C is not, and that is a choice:**
📌 the debugger path is entangled with the **breakpoint rewind**, which `W2`/`W5` reshape — and the
`W`-tasks are the coordinator's. 🔒 `R-126` also rules that C is the one that must eventually go
**cluster-wide**, which is a design question, not a routing one.
⇒ ⚠ **Stated rather than quietly skipped**; `T4` is marked **PARTIAL** in the plan, not done.

## `107c` — and why the earlier "wait for MIN" was wrong

⚠⚠ **`TM-012` deferred this with *"MIN is in flight three lines away."*** ⛔ **That reason went stale
the moment the coordinator branch was imported** — MIN had already landed *(`REPORT_MIN` is in
`batches/`, and `EditorSubsystem`'s `isFrozen` is at its corrected final state)*. ⭐ **Deferring on a
stale premise is the same failure as acting on one.**
⭐ **Verified MIN's own rails after routing:** `Hrot.Diagnostics.Breakpoints.Tests` **151/0**, and the
`WriteLands`/`WritesWhileFrozen` set **31/0**.

## Gate results

| gate | baseline | after | Δ |
|---|---|---|---|
| solution build | 0 errors | ✅ **0 errors** | **0** |
| `~TimeControlIntegrationTests` ×2 | 9 / 0 | ✅ **9 / 0**, **9 / 0** | **0** — no flake |
| `EditorSubsystemBootTests` | 12 / 0 | ✅ **12 / 0** | **0** |
| `Fdp.Toolkit.Time.Tests` | 166 / 0 | ✅ **173 / 0** | **+7 rails** *(`IntentTimeCommandsTests`)* |
| `Hrot.Editor.Tests` | 206 / 0 | ✅ **209 / 0** | **+3 rails** *(`TheTracerCoordinatorActuallyControlsTime`)* |
| `Hrot.ClusterRunner.Tests` | 262 / 2 | ⚠ **262 / 2** | **0** — the documented `D003_*` pair |
| ⭐ `Hrot.Diagnostics.Breakpoints.Tests` *(MIN)* | — | ✅ **151 / 0** | run because `107c` touched MIN's predicate |
| ⭐ Blueprints `WriteLands`/`WritesWhileFrozen` *(MIN)* | — | ✅ **31 / 0** | same reason |
| working tree after every suite | clean | ✅ **clean** | — |
| goldens | — | ⛔ **none moved** | — |

⚠ **`Fdp.Toolkits.Tests` full is not quoted as a number** — 📌 `TM-015`: it carries a rate-characterised
flake *(1 fail / 4 runs, identical binary)*. ⭐ **The Time namespace is quoted instead**, filtered, per
`DEBT-AIB-030`'s own rule that a whole-suite count is not evidence.
