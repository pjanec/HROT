<!--STATUS
state: LIVE
build-state: READY-TO-BUILD
updated: 2026-08-21
current-answer: this whole file — the Batch 104 dispatch, REPLACING HANDOFF_Batch104_The_Write_That_Lands.md.
stale-below: nothing.
known-rot: none.
known-conflict: none. ⛔ This batch touches NO time-control production code except the one AS-14 fix.
replaces: HANDOFF_Batch104_The_Write_That_Lands.md (withdrawn under rule 1c, never started)
-->
# HANDOFF — Batch 104: **make the net work, before anything else**

> 📌 **Dispatched at `34deca154`.** ⭐ Branch from it *(rule 7)*. ⛔ **Scope FROZEN at this sha.**
> ⭐ **Rule 3: your own ids.** ⭐ **Rule 1b: push `chore: started batch 104 at 34deca154` FIRST.**
> ⭐⭐ **`R-106`: a blocked item stops THAT ITEM, never the batch. Four verdicts.**

> ## ⭐⭐⭐ WHY THIS BATCH EXISTS — **and why the previous 104 was withdrawn**
> 🔒 **User, `2026-08-21`, verbatim:** *"the integration tests are the most important thing we need to
> make working before we touch any time monitoring/control related code. lets put that as the beginning
> of all the tasks belonging to the time system unification/refactor."*
>
> ⛔ The previous dispatch *(`HANDOFF_Batch104_The_Write_That_Lands.md`)* started with `GlobalTime`, a new
> kernel phase and a drain system. ⇒ **all time-related production code, all before a working net.**
> ⭐ **It is WITHDRAWN, not deleted** — its items return as `W0`–`W4`.
>
> 📄 **[`PLAN_Time_System_Refactor.md`](PLAN_Time_System_Refactor.md)** — ⭐ **the whole task list.**
> **This batch is `T0`, and `T0` blocks every other task in it.**

---

## 1. ⛔⛔⛔ `104a` — **fix `AS-14`: a step must not vanish**

📌 **This is the blocker.** 📐 `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/MasterSyncController.cs:188-195`:

```csharp
public GlobalTime Step(float fixedDelta)
{
    if (_mode != MasterMode.Stepping) return GetCurrentState();
    if (_pendingAcks.Count > 0)       return GetCurrentState();   // ⛔ DISCARDED, not queued
```

⇒ ⭐⭐⭐ **N step requests produce ONE step's worth of sim time**, and **the caller is never told.**

### 📐 The measurement — **reproduce it FIRST, before changing anything**

```
dotnet build Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/... --no-restore     → 0 errors, ~88 s
dotnet test  --no-build --filter "FullyQualifiedName~TimeControlIntegrationTests"  → 4 passed / 2 FAILED
```

| ⛔ red | message |
|---|---|
| `PauseStepResume_SimTimeAdvancesByStepAmount` | *"should have advanced ~3s after 3 steps; actual delta=**1.000s**"* |
| `MixedSequence_PauseStepPauseStep_AllCorrect` | *"expected ~2s advance; got **1.000s**"* |

### ⚠⚠ ROOT-CAUSE IT BEFORE CHOOSING A FIX — **two candidates, and they need different fixes**

| ⚠ hypothesis | ⭐ how to tell |
|---|---|
| **①** the settle between steps is **too short**, so `_pendingAcks` has not cleared | ⭐ instrument `_pendingAcks.Count` at each `Step()` and report the sequence |
| **②** ⛔ **the slave never ACKs in the harness**, so only the FIRST step ever works | ⭐ count `FrameStepCompletedEvent` arrivals; ⚠ **if this is it, the defect is in the harness or the ACK wiring, not in `Step`** |

⛔ **Do not guess.** ⭐ **Report which one it is — that IS the deliverable of this item**, and the fix
follows from it.

### ⭐ The design constraint on whichever fix you choose

| ⭐ | |
|---|---|
| ⭐⭐⭐ **a step request must not vanish silently** | ⭐ **QUEUE it** *(accumulate into `_pendingStepDelta` and apply when ACKs clear)* — ⛔ **or REFUSE it audibly**, with a return value or an event the caller can see |
| ⛔ **do NOT simply remove the ACK guard** | ⚠ it exists so a lockstep cluster does not run ahead of its slaves — 📌 removing it trades a lost step for a desync |
| ⚠ **and say what you chose, and why** | ⭐ this is the one production time-stack change in the batch |

---

## 2. ⭐⭐ `104b` — **can the FULL suite run?** *(`BP-378`'s remaining half)*

⭐ **`BP-378` says the suite OOMs at `EntityRepository..ctor`, `MAX_ENTITIES = 1_000_000` per harness.**
📐 **The filtered run disproves the blanket claim** — ⛔ **the full run is still untested.**

| ⭐ | |
|---|---|
| **①** | **run the whole suite once**, with a generous timeout, and **report what actually happens** |
| **②** | ⚠ **if it OOMs: name the harness and the allocation** — ⛔ do not "fix" `MAX_ENTITIES` blind |
| **③** | ⭐ **a class-at-a-time gate is an ACCEPTABLE outcome** if the full run is not economic — ⭐ say so and list which classes run |
| **④** | ⭐⭐ **update `BP-378` with what you measured** *(rule 3: the tracker is yours)* |

---

## 3. ⭐⭐⭐ `104c` — **make it a STANDING GATE ROW**

⭐ From this batch on, **every batch in this programme reports**:

```
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/... --no-build \
            --filter "FullyQualifiedName~TimeControlIntegrationTests"
```

| ⭐ | |
|---|---|
| ⭐⭐ **baseline** | **4 passed / 2 failed** at the dispatch sha ⇒ ⭐ **after `104a` it must be 6/6** |
| ⛔ **a third red** | **a regression the batch caused** — ⛔ never "pre-existing" without a proof at the base |
| ⭐ **run it TWICE** | ⚠ it drives real DDS loopback and pumps frames; **a flake must be known as a flake** |

---

## 4. ⭐ `104d` — **the coverage the net is missing** — ⚠ **only if `104a`–`104c` land early**

📐 **Measured gaps** in `TimeControlIntegrationTests`:

| ⛔ missing | ⭐ why it matters to the refactor |
|---|---|
| **no `SetTimeScale` test** | `T1`'s `IsAdvancing` is `DeltaTime > 0`; ⚠ **`TimeScale = 0` is the OTHER way to halt** and nothing covers it |
| **no editor-composition test** | 📌 `T3` moves the editor's master to another bus — ⛔ **nothing would notice if the editor's toolbar stopped working** |
| **no breakpoint-pause test** | 📌 `W2`/`W5` turn on the rewind path |

⭐ **Add what you can cheaply; ⛔ do not build a second harness.** ⚠ **List what you did not add.**

---

## 5. ⛔⛔ NOT IN THIS BATCH

⛔ **Everything in `PLAN_Time_System_Refactor.md` §2.** ⚠ Specifically: **no `GlobalTime` change · no new
`SystemPhase` · no drain system · no refusal deletion · no bus move.**
⭐ **The one exception is `104a`'s fix**, which is what makes the net trustworthy in the first place.

---

## 6. ⭐ GATES

⭐ Baseline = **Batch 103's table**. ⚠ State the environment *(Xvfb or not)*.
⭐ **Extra rows:** `TimeControlIntegrationTests` **before and after** · the full-suite verdict *(`104b`)* ·
`Fdp.Toolkits.Tests` — ⭐ **`ThePauseFlagOnTheClockIsFalseWhilePausedTests` must stay 4/0**, and ⚠
`DEBT-AIB-030`'s rotating flakes must be confirmed by filter.
⛔ **`Hrot.ClusterRunner.Tests` carries 2 pre-existing reds** — `DataDrivenGizmoPredicateTests.D003_*`.
