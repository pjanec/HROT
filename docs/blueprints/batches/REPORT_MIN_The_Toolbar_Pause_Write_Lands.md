<!--STATUS
state: LIVE
updated: 2026-08-21
current-answer: this whole file — what MIN built, measured and found.
stale-below: nothing.
known-rot: none.
known-conflict: the handoff's §5 says "breakpoint pause beyond staging: already works via
  the existing stage+drain". Re-measured (M-41): the drain has no production caller outside
  DataBreakpointManager, so the editor's own Continue does not reach it. §5 of this report.
  NOT adapted to — reported, per the SCOPE-IS-FROZEN rule.
-->
# ⭐⭐⭐ REPORT — `MIN` · **the toolbar-pause write lands**

> **Scope frozen at** `38deecc9a` *(the RE-DISPATCH)* · **branch** `claude/hrot-implementation-j1jvin`
> · **started at** `38deecc9`
> ⭐ **Re-pulled the coordinator branch before the final commit** *(rule 4)* — ⚠⚠ **and it had
> RE-DISPATCHED this batch mid-run. §0.**

| item | verdict | one line |
|---|---|---|
| **`3a`** | ✅ **done** | the gate is the **CLOCK**, three ways — refuse · stage · **write now** |
| **`3b`** | ✅ **done** | ⭐ measured BOTH mechanisms; ⭐⭐ **built the re-dispatch's `Playback`-now path**, which is better — §3 |
| **`3c`** | ✅ **done** | `NotFrozen` → **`SimulationAdvancing`**, and the sentence is true again |
| **`4`** | ✅ **done** | 8 rails over a **real kernel** + 4 session-half rails, all revert-probed |

⭐ **ID I allocated:** **`BP-389`** *(`RW-H`)*. ⛔ **No `TM-`, no Area `H`** — that is the time lane's
partition.

---

## 0. ⚠⚠⚠ THE BATCH WAS RE-DISPATCHED WHILE IT WAS RUNNING — **what I did, and the process finding**

📌 **`ae4e9514` — *"re-dispatch (rule 1a, unstarted) — the write-now path flushes itself"*.** It arrived
while I was running the gate table, with everything built and green.

### ⛔⛔ The process finding: **the run was NOT unstarted**

⭐ Rule 1b requires the implementation session to *"push an empty `chore: started batch N at <sha>`
commit IMMEDIATELY after the rule-7 merge, before writing any code."*
📐 **I did:** `25f4c18a` — `chore: started MIN at 38deecc9` — pushed **before the first line of code**,
and it was on `origin/claude/hrot-implementation-j1jvin` well before `ae4e9514` was authored.

⇒ ⚠⚠ **Rule 1a's precondition was not met**, and rule 1b's control — the one written precisely to close
that blind window — **was present and worked**; it simply was not read. ⭐ Rule 1b also says
*"ASK before re-dispatching rather than inferring from the remote."*

⛔ **No damage.** ⭐ Stated because the control existing and being ignored is a different failure from
the control being blind, and only the second one is fixed by the rule as written.

### ⭐⭐ What I did with it — **adopted, and here is why that is not "adapting"**

⭐ The SCOPE-IS-FROZEN rule says a later document that *invalidates* an item ⇒ **STOP and REPORT**, do
not adapt. ⚠ **This is not that shape**: it is a **re-dispatch of the very item in hand**, it *sharpens*
`3b` rather than contradicting it, and its own text says *"report the `Playback`-now path as what you
built, or report why it did not land."*

⭐⭐⭐ **And it is RIGHT.** 📐 `EntityCommandBuffer.Playback(repo)` is **synchronous**
*(`EntityCommandBuffer.cs:331`)*, so the write can flush its own scratch buffer and land **before
`WriteFieldNow` returns** — ⛔ **no kernel dependency at all**, which is the exact weakness the original
handoff flagged in the path I had built. ⇒ **~10 lines, re-probed, green.**

⚠ **The honest accounting:** I had already built and railed the other mechanism, and it worked. ⭐ I
changed it because the new one is **better on the axis the handoff itself named**, not because a
document told me to — ⛔ and the measurement of *both* is kept in §3, because a report that quietly
showed only the winner would hide the comparison that justifies it.

---

## 1. ⭐⭐⭐ WHAT WAS BROKEN — **two causes, and the second is why a naive fix is worse than none**

📌 **The live failure:** *edit a working-state variable while paused from the toolbar → the value does
not change.*

| step | before `MIN` |
|---|---|
| the run state | `Paused` ✅ |
| the `isFrozen` gate on the control | ✅ already fixed on my base *(`f0b1e141b`)* |
| `BlueprintDebugSession.TryWriteWorkingStateField` | 🔴 **`AS-3`** — `if (!_isPaused) return false;`, a **session-local** flag a toolbar pause never sets |
| and even had it staged | 🔴 **`AS-5`** — **nothing drains** the queue under a toolbar pause |

⇒ ⛔⛔ **Widening the gate ALONE would have turned *"refused with a wrong reason"* into *"accepted and
silently discarded"*** — 📌 `M-41` says exactly this, and §5 re-measures it. ⭐ **That is why the toolbar
arm WRITES rather than stages**; it is not an optimisation.

---

## 2. ⭐⭐ `3a` — **the three-way, and why the stage arm asks the MANAGER**

| the clock says | `MIN` does | why |
|---|---|---|
| **advancing** | ⛔ refuse | a direct write is overwritten next tick — `W1`/`W2`'s job |
| **halted AND `dbm.IsPaused`** | ⭐ **stage**, unchanged | 📌 `R-63`: resume restores `_liveRepo ← _postTickSnapshot` and drains **afterwards** |
| **halted AND NOT `dbm.IsPaused`** | ⭐⭐⭐ **`WriteFieldNow`** | `ActiveView` **is** `_liveRepo`; `P6′` — nothing recomputes at `dt = 0` |

### ⭐⭐ Keying the stage arm on the MANAGER is **more** correct, not a like-for-like swap

⛔ It would be easy to read this as *"swapped one pause flag for another"*. 📐 It is not:
**`R-63`'s hazard exists exactly when the manager has REWOUND**, and *rewound ⟺ `manager.IsPaused`* —
`OnHit` sets the flag and does the rewind in the same method. ⇒ ⭐ the session's flag was never the
thing that made staging necessary; it only correlated with it.

### ⚠ `IsClockHalted` — `AS-1b`, and the one deliberate default

⭐ Reads the **LIVE WORLD's** `GlobalTime.DeltaTime`. ⛔ **Never** the controller's `GetCurrentState()`,
which hard-codes its delta to `0` and would answer *"halted"* for ever — 📌 pinned from the other side
by `ThePauseFlagOnTheClockIsFalseWhilePausedTests`, which stayed **4/0**.

⚠ **No clock at all ⇒ HALTED.** ⭐ Deliberate and railed, not a fallback: no `GlobalTime` means no source
of ticks, so nothing can overwrite a direct write. ⛔ The other answer would refuse every edit in such a
world and blame the designer for a simulation that is not running.

### ⭐⭐ Both new interface members **THROW** by default

⛔ A stub answering *"not halted"* would refuse every live edit and blame the designer — 📌 the `M-36`
shape precisely, and the reason that confusion cost three handoffs. 📐 **It reddened 13 rails in the
test stubs, loudly, on the first run.** ⭐ That is the default working, not a cost.

---

## 3. ⭐⭐⭐ `3b` — **BOTH mechanisms measured; the RE-DISPATCH's is what shipped**

⭐ The original handoff asked *"which immediate path? MEASURE, do not guess"*; the re-dispatch replaced
the A/B choice with a specific one. ⭐⭐ **Both were measured, and the comparison is the deliverable.**

### 📐 What I measured FIRST — a temporary probe over a **real `ModuleHostKernel`** at `dt = 0`

```
before Update: Value=1
after  Update: Value=42            ⇒ the kernel DOES flush the per-thread ECB at dt = 0
live GlobalTime after a dt=0 frame: DeltaTime=0 TimeScale=0 Frame=1
afterFrame1=77, afterFrame3=77     ⇒ and the write STAYS across paused frames
```

⭐ `ModuleHostKernel.UpdateInternal` plays the per-thread buffer back in `BeforeSync`
**unconditionally** — ⚠ systems self-skip at `dt = 0` *(`BlueprintTickSystem:51`)*, the **frame does
not**. ⇒ **candidate A works.**

### ⭐⭐⭐ …and the re-dispatch's path is BETTER, for the reason the handoff itself gave

| mechanism | lands | depends on |
|---|---|---|
| ⚠ **the repository's PER-THREAD buffer** *(what I built first)* | ⛔ **next kernel frame** | 🔴 **the kernel still flushing at `dt = 0`** — measured true, but silently breakable |
| ✅✅ **a SCRATCH `EntityCommandBuffer` + `Playback(_liveRepo)`** *(shipped)* | ⭐⭐ **before the call returns** | ⭐ **nothing** — `Playback` is synchronous |

⭐ **Both use the identical `IEntityCommandBuffer.SetComponentFieldRaw` the breakpoint drain calls**
*(📌 `R-65`/ruling 9 — one implementation of "patch these bytes", not two)*.
⛔ **The `internal` `EntityRepository.SetComponentFieldRaw` fallback was NOT needed**, so no
`InternalsVisibleTo` on `Fdp.Core` and no second public surgical-write surface.

### ⭐⭐ The difference is RAILED, not asserted

⭐ `UnderAToolbarPause_TheWriteLands_AndStaysAcrossPausedFrames` reads the value back **before any
`Update()`**. 📐 **Probe ④ swapped the scratch buffer for the per-thread one and that single assertion
reddened** — ⇒ ⛔ the two mechanisms are distinguishable by the rail, not only by argument.

⚠ **The kernel-driven half of the rail is kept anyway**: it now proves the write **survives** paused
frames *(`P6′`)* rather than proving it arrives.

## 4. ⭐⭐ **DOES IT ACTUALLY FIX WHAT THE USER SAW?** — measured, because the answer was not obvious

⚠ The symptom was *"the value does not change"* — a **display** claim. ⇒ landing the write is necessary
and I could not assume it is sufficient.

📐 **`BehaviorFrame` does NOT advance at `dt = 0`** *(`BehaviorFrameSystem:31`, and that gate is its
entire contract — `Q46` rule 2b)*. ⇒ ⚠ if the panels sampled on that pulse, the edit would be invisible
until the sim resumed.

⭐⭐ **They do not.** `VariableRowSources.ToRow`'s object arm is **a camera, not a photograph**
*(Batch 94a)*: `readObject` re-invokes the provider on **every read**, and `Build()` runs every `Draw`.
⇒ ⭐ **the new value appears on the next frame.** ⛔ Only the **change highlight** stays quiet — and that
is correct: no behaviour ticked, so nothing changed *behaviourally*.

---

## 5. ⚠⚠ ONE PLACE THE HANDOFF AND THE LEDGER DISAGREE — **reported, NOT adapted to**

📌 **Handoff §5:** *"breakpoint pause beyond staging — ✅ already works via the existing stage+drain."*
📌 **`M-41`** *(a §M row — "MEASURE, DON'T MEMORISE")* says the drain has no production caller outside
the manager.

### 📐 I ran `M-41`'s own command rather than quoting it. **The ledger is right.**

| | |
|---|---|
| `DrainPendingMutations` call sites | **2**, both inside `DataBreakpointManager` *(`RequestStep:498`, `RequestContinue:517`)*, plus `OnHotReloadBegin:437` reaching it **through** `RequestContinue` |
| production callers of the manager's `RequestStep`/`RequestContinue` **outside the class** | ⛔ **none.** The other matches are different interfaces *(`ITimeControlGateway`, `AiTracerCoordinator`, `IExConLogic`)*, and `BlueprintDebugSession:1436` calls `_timeController.RequestStepOneTick()` — ⛔ not the manager |

⇒ ⭐⭐ **The staged path works when the MANAGER resumes; the editor's own Continue does not reach it.**
⛔ **`MIN` did not make this worse** — the breakpoint arm is byte-for-byte the behaviour it had — and
⭐⭐⭐ **it is exactly why the toolbar arm writes instead of staging.**

⛔ **Not fixed here** *(handoff §5 puts it in `Q48-C` / `W1`–`W5`)*, and ⛔ **not adapted to**, per the
SCOPE-IS-FROZEN rule. ⚠ **And my own rail says so in its doc comment**: it calls `RequestContinue()`
**directly**, so it proves the manager's path and **cannot** prove the designer's button reaches it
*(`M-29`)*.

---

## 6. ⚠ FOUR RAILS RE-EXPRESSED — **each is a finding, none is a weakening**

⭐ Their premise **was** the flag `MIN` removed. ⛔ Silently editing them would have hidden that.

| rail | was | is |
|---|---|---|
| `WhileFreeRunning_TheWriteIsRefused…` | `Session(paused: false)` | **`WhileTheClockAdvances_…`** — drives the clock |
| `WhileFrozen_TheWriteIsStaged` | `Session(paused: true)` | **`UnderABreakpoint_TheWriteIsStaged`**, ⭐ + asserts it did **not** take the immediate arm |
| `WhileNotFrozen_TheInstanceWriteStagesNothing` | `Attach(paused: false)` | **`WhileTheClockAdvances_…`**, ⭐ + asserts nothing was written |
| the refusal-sentence rail's 4th cause | *"session not frozen"* | *"simulation advancing"*, induced through the clock |

⭐ **Each now also asserts `WroteNow` is empty**, so a refusal that quietly took the new arm is caught.
⭐ **And three genuinely NEW rails state the half that changed**, including
`TheSessionsOwnPauseFlagIsNoLongerTheGate` — *the session is **not** paused and the write still lands*.

---

## 7. ⭐ REVERT PROBES — **each reddened, each un-applied by the inverse edit**

⛔ Never `git checkout --`.

| # | probe | result |
|---|---|---|
| **①** | restore `if (!_isPaused) return false;` | ⭐ **3 red** |
| **②** | `WriteFieldNow` stages instead of writing | ⭐ **2 red** — the two toolbar-arm rails |
| **③** | `IsClockHalted() => true` *(the `AS-1b` trap)* | ⭐ **1 red** |
| **④** | the scratch buffer swapped for the repository's **per-thread** one | ⭐⭐ **1 red** — ⭐ exactly the *"landed before any `Update()`"* assertion, ⛔ nothing else. **That is the two mechanisms told apart by a rail** |

⚠ Probe ② first failed to **compile** — `TreatWarningsAsErrors` caught the unreachable branch I used to
park the real body. ⭐ Rewritten as a straight replacement, then it reddened.

⭐⭐ **Probe ④ is the one worth keeping in mind**: it is the only evidence that adopting the
re-dispatch's mechanism *changed observable behaviour* rather than just tidying the source.

---

## 8. ⭐⭐ GATES — **run ONCE, at the end** *(`M-37`)*

⭐ Baseline = **Batch 103's table**. Base sha **`38deecc9`**. ⚠ Every row states its environment; every
suite **unfiltered** unless the row says otherwise.

⚠⚠ **Read the deltas with this in mind:** my merge brought **two coordinator code commits**
(`f0b1e14`, `eb310fd`) and their rails, so **some of the delta is theirs.** Split per row.

⭐ **Re-verified after the §0 mechanism change**, not carried over: solution build **0 errors**, and the
four suites the change can reach re-run **identical** — Breakpoints `151/0/0`, Blueprints `3881/0/10`
(Xvfb), `Hrot.Editor` `206/0/0`, Smoke `4/0/0`. ⛔ The untouched rows were **not** re-run; they are from
the single pass above, which is what `M-37` asks for.

| gate | env | result | Δ vs Batch 103 |
|---|---|---|---|
| **solution build** | — | ⭐ **0 errors** | — |
| `Hrot.Diagnostics.Breakpoints.Tests` | — | **151 / 0 / 0** | ⭐ **+8 — mine** *(the `MIN` rail)* |
| `Hrot.Blueprints.Tests` | **Xvfb** | **3881 / 0 / 10** | ⭐ **+4 — mine** *(3 session-half + 1 Instance toolbar)* |
| `Hrot.Editor.AiShared.Tests` | **Xvfb** | **1723 / 0 / 0** | **0** |
| `Hrot.Editor.Tests` | — | **206 / 0 / 0** | ⚠ **+5 — the COORDINATOR's** *(`ThePausedClockIsTheRunStateTests`, from `f0b1e14`)* |
| `Hrot.ClusterRunner.Tests` | — | ⚠ **260 / 2 / 0** | ⚠ **+6 passed — the COORDINATOR's** *(`TheLayoutResetCanActuallyBeTurnedOff`)*; the **2 reds are PRE-EXISTING** — §8.1 |
| `Fdp.Toolkits.Tests` | — | ⚠ **see §8.2** | ⛔ **`DEBT-AIB-030` — no number is evidence** |
| `Fdp.Presentation.Tests` | **Xvfb**, filtered *(`BP-337`)* | **155 / 0 / 0** | **0** |
| `Hrot.Smoke.Tests` | — | **4 / 0 / 0** | **0** |
| `Hrot.BTree.Editor.Tests` | — | **622 / 0 / 0** | **0** |
| `Hrot.Hsm.Editor.Tests` | — | **554 / 0 / 0** | **0** |
| `Hrot.AiEditor.Generators.Tests` | — | **277 / 0 / 0** | **0** |
| `Hrot.AiEditor.Persistence.Tests` | — | **143 / 0 / 0** | **0** |
| ⚠ `NodeEditor.Core.Tests` *(**out of solution — BUILT**)* | — | **211 / 0 / 0** | **0** |
| ⚠ `NodeEditor.UI.Tests` *(**out of solution — BUILT**)* | — | **135 / 0 / 0** | **0** |
| ⚠ `Fhsm.Tests` *(**out of solution — BUILT**)* | — | **300 / 0 / 0** | **0** |
| ⚠ `StructEdit.Tests` *(**out of solution — BUILT**)* | — | ⚠ **191 / 1 / 0** | **0** — `BP-363`, pre-existing |
| ⭐ **`ThePauseFlagOnTheClockIsFalseWhilePaused`** | — | ⭐ **4 / 0** | **0** — ⭐ `AS-1b` is load-bearing for `IsClockHalted`; the handoff required this row |
| **tracker** | — | ⭐ **OK — open 81 / done 243 (+1 refuted)** | **+1 done** — `BP-389` |
| **rulings** | — | ⭐ **92/92 verified** · ⚠ 3 staleness WARNs — §8.3 | — |
| **design digest** | — | ⭐ **61 docs OK** | — |
| **working tree** | — | ⭐ **CLEAN after every suite run** | — |

⛔ **`Hrot.ClusterRunner.Integration.Tests` stays out** *(`BP-378`)*.

### ⭐ The `--no-build` column *(gate-report contract row 2)*

| | |
|---|---|
| ⭐ **`--no-build`** | every project **in** the solution — the solution build above produced their binaries |
| ⛔⛔ **MUST BUILD** | `NodeEditor.Core` · `NodeEditor.UI` · `Fhsm` · `StructEdit` — ⚠ **out of solution**, so `--no-build` would report a **STALE BIN**. ⭐ They were built |

### ⚠ 8.1 — the two `ClusterRunner` reds

`DataDrivenGizmoPredicateTests.D003_Predicate_False_SkipsUpdateAndDraw_ForFilteredEntity` and
`D003_Predicate_True_AllowsUpdateAndDraw`. ⭐ **The identical pair Batch 103 reproduced in a worktree at
`d3c370ffb`** — an ancestor of this base — and nothing since touches gizmo-predicate code. ⇒ ⛔ **not
mine**, and no second worktree was built for a fact already established at the source.

### ⚠⚠ 8.2 — `Fdp.Toolkits.Tests` is `DEBT-AIB-030`, and I measured the rotation rather than reporting a number

📐 **Four consecutive full runs of the SAME binary:**

| run | result |
|---|---|
| 1 | ⚠ **1965 / 3 / 0** |
| 2 | ⭐ **0 failed** |
| 3 | ⚠ **1966 / 2 / 0** |
| 4 | ⭐ **0 failed** |

⭐ **The failing identities rotate too** — `StatelessGizmoRegistryTests.SC_GZ022_2_Register_UnregisteredType_Throws`
and `GizmoRegistryTests.SC_GZ004_2_Register_UnregisteredComponent_Throws`, in `Fdp.Toolkit.Diagnostics.Gizmos.Tests`.
📐 **Under `--filter`, in isolation: 8 / 0 / 0.**

⇒ ⛔ **Neither a red nor a green from the whole suite is evidence** *(`DEBT-AIB-030`, verbatim)*, and
⭐ **`MIN` touches no gizmo code at all.**

### ⚠ 8.3 — the three rulings staleness WARNs

```
Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs   ← MINE
.claude/CLAUDE.md                                                        ← the coordinator's
docs/blueprints/PLAN_Time_System_Refactor.md                             ← the coordinator's
```

⭐ **Mine checked:** the ruling citing that file is **`R-63`**, whose quote is
`_liveRepo.SyncFrom(_postTickSnapshot);` — **present and untouched**, and ⭐ **`MIN` actively honours it**
*(the breakpoint arm still stages)*. ⛔ Not silenced.

### ⭐ Quarantine counts

| | |
|---|---|
| `Hrot.Blueprints.Tests` skipped | **10** *(Xvfb)* — ⭐ unchanged |
| every other suite | **0 skipped** |
| ⛔ **new skips this batch** | ⭐ **none** |

### ⭐⭐ Golden movement, as a diff shape *(contract row 3)*

⭐⭐⭐ **ZERO goldens moved.** 📐 **8 files: 7 changed, 1 added** *(the new rail)*, **0 deleted**. ⛔ No
`.approved.` / golden / snapshot file appears in the diff, and the tree was clean after every run.

---

## 9. ⭐ LANE CHECK *(handoff §6)*

| file touched | assembly | lane |
|---|---|---|
| `BlueprintDebugSession.cs` | `Hrot.Blueprints.Editor` | ⭐ UI/variable |
| `DataBreakpointManager.cs` · `IDataBreakpointManager.cs` | `Hrot.Diagnostics.Breakpoints` | ⭐ UI/variable |
| `BlueprintLiveValueWriter.cs` | `Hrot.Editor` | ⭐ UI/variable |
| 4 test files | — | ⭐ UI/variable |

⛔ **Nothing under `Fdp.Toolkits/Time/`, `Hrot.Orchestrator`, `ModuleHostKernel` or the integration
tests was edited.** ⚠ `ModuleHostKernel` was **READ** for the `3b` probe and is **driven** by the rail —
⭐ neither is an edit, and both were necessary to answer the question the handoff asked.
⛔ **No `InternalsVisibleTo` was needed on `Fdp.Core`** — candidate A made that moot.
