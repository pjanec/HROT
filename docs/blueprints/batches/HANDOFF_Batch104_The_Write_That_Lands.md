<!--STATUS
state: SUPERSEDED
build-state: DESIGN
updated: 2026-08-21
current-answer: NOTHING IN THIS FILE. It is replaced by HANDOFF_Batch104_The_Net_First.md.
stale-below: EVERYTHING. 104a-104f describe work that may not start until T0 is green.
superseded-by: HANDOFF_Batch104_The_Net_First.md
known-rot: none -- the items are still correct, they are just NOT FIRST.
-->
# ⛔⛔⛔ WITHDRAWN — HANDOFF Batch 104 (**the write that actually lands**)

> ## ⛔⛔⛔ THIS DISPATCH IS WITHDRAWN AND REPLACED — **`2026-08-21`, rule 1c**
> 🔒 **User, verbatim:** *"the integration tests are the most important thing we need to make working
> before we touch any time monitoring/control related code. lets put that as the beginning of all the
> tasks belonging to the time system unification/refactor."*
>
> ⛔ **`104b`, `104c` and `104d` all touch time-related production code** — `GlobalTime`, `SystemPhase`,
> `ModuleHostKernel`. ⇒ **they may not go first.**
>
> | ⭐ | |
> |---|---|
> | ⭐⭐⭐ **BUILD THIS INSTEAD** | 📄 **[`HANDOFF_Batch104_The_Net_First.md`](HANDOFF_Batch104_The_Net_First.md)** |
> | ⭐⭐ **the ORDER for everything after it** | 📄 **[`PLAN_Time_System_Refactor.md`](PLAN_Time_System_Refactor.md)** |
> | ⭐ **the items below are NOT WRONG** | ⚠ they are `W0`–`W4` in the roadmap and return **after `T0`** |
> | ⛔ **never started** | ✅ no `chore: started batch 104` marker; the user confirmed twice |
>
> ⭐ **Kept, not deleted** *(rule 1c ②)*: the item text is the detail the later batches reuse.

---

## 1. ⭐⭐⭐ `104a` — **the end-to-end rail, FIRST, and it will be RED**

📌 **Design basis:** `Q48-E` — *"one END-TO-END rail, and it is the acceptance criterion for the whole
slice."* ⛔ **Write it before any production change and report the failure map.**

> ⭐⭐ **The shape:** *pause by each supported means → edit a live variable → resume by the matching means
> → **the value is in the REPOSITORY**.*
> ⛔⛔ **A rail that asserts the QUEUE LENGTH proves nothing** — 📌 `2026-08-19`: *"is it connected?" is
> not "does anything flow?"*

| ⭐ cover both pause kinds — **they are different mechanisms** | |
|---|---|
| **① TIME pause** *(toolbar / deterministic stepping)* | ⭐ **no repo rewind.** This is the case the user hits, and the one `104c`+`104d` fix |
| **② BREAKPOINT pause** *(a data breakpoint hit)* | ⛔ **a repo REWIND** — `OnHit` rewinds the live repo to pre-tick; resume restores post-tick, drains, advances *(`AS-4`)* |

⚠ **Expect ① RED and ② GREEN** — ② already works through `RequestContinue`; ⭐ **if ② is also red, that is
a finding and it changes `104d`.**
⭐⭐ **Report the failure map as the deliverable of this item**, not just "red".

---

## 2. ⭐ `104b` — **`GlobalTime.IsAdvancing`** *(`RF-1`)*

📌 **Design basis:** `R-126` + `M-42`.

| ⭐ | |
|---|---|
| **①** | add `public bool IsAdvancing => DeltaTime > 0f;` beside the existing flag |
| **②** | ⚠ **mark `IsPaused` obsolete-with-a-reason** — ⛔ **do NOT delete it**: it is public API on an ECS component and `Fdp.Examples` may bind it. ⭐ An `[Obsolete]` with *"reads `TimeScale`, which a pause does not change — use `IsAdvancing`"* is the deliverable |
| **③** | ⚠ **if `[Obsolete]` breaks the build anywhere, that build break is a FINDING** — ⭐ report the sites; ⛔ do not silence it with a pragma without saying so |

---

## 3. ⭐⭐ `104c` — **`SystemPhase.PreFrame`** *(`RF-2`)*

📌 **Design basis:** probe `P1` — the drain must run **before `Input`**, because `Input` runs first and
holds **~25 state-mutating systems**. ⛔ `BeforeSync` is too late.

| ⭐ | |
|---|---|
| **①** | `SystemPhase.PreFrame = 0` |
| **②** | one `topology.Scheduler.ExecutePhase(SystemPhase.PreFrame, _liveWorld, deltaTime);` in `ModuleHostKernel.UpdateInternal`, **immediately after the `GlobalTime` push and before the `Input` line** *(`:493`)* |
| ⭐⭐ **why this is small** | 📐 `SystemScheduler` is **phase-generic** — `Dictionary<SystemPhase, List<IEcsModuleSystem>>`, built by iterating whatever was registered *(`:16`, `:46`)*. ⛔ No phase list to extend anywhere else |
| ⚠ **③** | ⛔ **A `PreFrame` system is OUTSIDE the four groups `ReferenceReplayLoadHandler.SetSystemsEnabled` toggles** — ⭐ **state that in your report**; 📌 `AS-10` |
| ⚠ **check** | `ModuleHostKernel:156` *"validate that the system's phase will actually be executed for global systems"* — ⭐ **make sure `PreFrame` counts as executed there**, or a `PreFrame` global system may be rejected at registration |

---

## 4. ⭐⭐⭐ `104d` — **the drain system** *(`RF-3`)*

📌 **Design basis:** `R-126` — *"applied from the sim tick loop… on the first next simulation tick."*
⭐ **Precedent to mirror, in the same assembly:** `DebugSnapshotProvider` — `[UpdateInPhase(...)]`,
`IEcsModuleSystem`, a `volatile int` gate, registered by **`EditorSubsystem:1085` and
`CgfSubsystem:566`.** ⛔ **Register the new system in BOTH, beside it.**

```
if (view is not EntityRepository repo) return;
if (deltaTime <= 0f) return;                   // halted: the edit waits
manager.DrainPendingMutations(repo);
```

> ### ⛔⛔⛔ GATE ON THE `deltaTime` **PARAMETER**, NOT ON THE SINGLETON — `AS-10`
> 📐 **Measured `2026-08-21`:** during `PrepareReplay`, `SuspendGlobalTimePush()` stops the kernel
> writing the singleton, so **it FREEZES AT ITS LAST VALUE — which may carry a NON-ZERO `DeltaTime`.**
> ⇒ ⛔⛔ **a singleton-based `IsAdvancing` answers TRUE while nothing is advancing.**
> ⚠ And `SetSystemsEnabled(false)` disables only **four named groups** *(input · sim · postSim ·
> lifecycle)* — ⭐ **a `PreFrame` system is in none of them**, so it keeps running.
> ⭐⭐ **`ExecutePhase(phase, _liveWorld, deltaTime)` hands every system the kernel's REAL per-frame
> delta**, and the suspension does not touch it. ⛔ **So use the parameter.**
>
> ⚠ **Residual, ACCEPTED and named** *(⛔ do not fix it in this batch)*: during replay preparation the
> parameter still reads *advancing*, so a staged edit can be drained into a world replay is about to
> overwrite. ⭐ **The edit is LOST, not corrupted**, and only if the designer starts a replay between
> editing and resuming. ⛔ A guard needs `_globalTimePushSuspended` exposed — a kernel API change this
> slice does not earn. ⭐ **Report it if you touch it; do not widen the batch for it.**

| ⛔⛔ **NOT in this item** | |
|---|---|
| ⛔ **Do NOT touch `RequestStep` / `RequestContinue`** | they keep restoring and draining exactly as today ⇒ **the breakpoint path is unchanged** |
| ⛔ **Do NOT restore the post-tick snapshot here** | ⚠ that is `RF-4`, and it is the one refactor still marked **LIKELY, not proven** *(§5)* |
| ⚠ **so guard it** | ⭐ **skip the drain while `manager.IsPaused`** — the repo is rewound and `RequestContinue` owns that case. ⛔ Draining into a rewound repo is `R-63` from the third side |

⭐⭐ **What this buys:** a staged write now lands on the **time-pause** path, which is `104a` item ① —
⛔ **and it costs nothing on the breakpoint path.**

---

## 5. ⭐⭐⭐ `104e` — **running is not a refusal** *(`RF-5` + `RF-6`)*

📌 **Design basis:** 🔒 `R-126`, the user verbatim: *"I do not understand how comes that something can be
unwritable… we should be able to write anything anywhere."*

| ⭐ | |
|---|---|
| **①** | ⛔ **delete `VariableEditCommit.Outcome.RefusedRunning`** — `TargetFor(Running)` stops being `Nowhere`; running ⇒ **stage** |
| **②** | ⛔ **delete `LiveWriteRefusal.NotFrozen`** and its message *(the sentence that cost three sessions)* |
| **③** | ⛔ **`BlueprintDebugSession.TryWriteWorkingStateField`: drop the `if (!_isPaused) return false;` gate** *(`:920`)* — ⚠ **keep `if (_dataBreakpointManager is null) return false;`** and keep the negative-offset throw |
| ⭐⭐ **KEEP these three refusals** | `NoSelectedEntity` · `FieldNotResolvable` · **`SizeMismatch`** — 📌 the last is `Q32` §2.1's **memory-corruption gate** and is not negotiable |
| ⚠ **rails to update, not delete** | `TheWriteWhilePausedTests` and `TheEditDialogReachesTheDesignerTests` both assert `RefusedRunning` — ⭐ **they become "it STAGES" rails.** ⛔ A deleted rail is a lost guarantee; re-point them |

---

## 6. ⭐⭐ `104f` — **"queued" is REAL, but only in TWO of three states** *(`RF-11`)* — ⚠ **only if `104a`–`104e` land early**

> ⚠⚠ **AMENDED at the re-stamp.** ⛔ The first edition of this item rested on a probe (`P6`) that
> **measured the wrong layer** and told you a paused edit can never be shown. ⭐ **That is false for the
> case the designer actually hits.** 📄 `DESIGN_…§4 P6′`.

📌 **Design basis — `P6′`, and it is TWO layers, only one of which ignores `dt`:**

| layer | guards on `dt`? |
|---|---|
| module **DISPATCH** | ⛔ **no** — `ShouldRunThisFrame` never reads `deltaTime` |
| ⭐⭐⭐ **the BEHAVIOUR tick systems** | ✅ **YES** — `BlueprintTickSystem:51` · `BTreeTickSystem:55` · `HsmTickSystem:103`, all `if (deltaTime <= 0f) return;` |

🔒 **And this is the user's own `2026-08-19` specification** — `Q46` rule `2b`: *"the brain (cgf) does
not tick ANY behavior when `dt=0`."*

### ⭐⭐ ⇒ The behaviour is PER RUN-STATE

| state | is the write overwritten? | what the surface shows |
|---|---|---|
| ⭐⭐ **time-paused** *(toolbar · stepping — **the case the user hits**)* | ⛔ **NO** — behaviours are not ticking | ⭐⭐⭐ **the value simply CHANGES.** ⛔ **No "queued" state — do not add one here** |
| ⛔ **breakpoint-paused** *(rewound)* | ⚠ **yes, by the RESTORE** *(`AS-4`)* | ⭐ **"queued"** |
| ⚠ **running** | ⚠ **yes, by the next behaviour tick** | ⭐ **"queued"**, and ⛔ an edit to a **computed** variable is inherently a **one-tick poke** |

### ⭐⭐⭐ The MECHANISM ALREADY EXISTS — ⛔ do not invent one

🔒 **`Q46` rule 5**, verbatim: *"A value the user typed is a **SEPARATE cache on the row**, distinct from
the value read through the accessor."* ⇒ ⭐ **that IS the queued state**, and it resolves on the next
`dt > 0` pulse *(rule 2)*.

| surface | ⭐ verdict |
|---|---|
| ⭐⭐ **`AiWatchWindow`** *(and `WatchPanelWindow`)* | ⭐ **the design's home for it** — rule 5 |
| ⭐⭐ **`VariableDetailsSection`** | ⭐ same row class ⇒ free. ⚠ **the surface the designer edits from — the one that must not lie** |
| ⛔ **`AiVariablesWindow`** | ⛔⛔ **DO NOT** — `U-16` / `R-54` retires it; the affordance would cement a duplicate |
| ⚠ **`VariableEditModal`** | ⭐ **one sentence on confirm**, ⛔ not a live state |
| ⛔ **`VariablePropertiesModal`** | ⛔ no — it edits the DECLARATION |

⚠ **Shape it minimally and report it**; ⭐ the full affordance is a UX design this batch does not own.

---

## 7. ⛔⛔ NOT IN THIS BATCH — **and `RF-4` is the important one**

| ⛔ | why |
|---|---|
| **`RF-4`** — move the RESTORE out of `RequestStep`/`RequestContinue` | ⚠ **the only refactor still LIKELY rather than PROVEN.** 📐 Measured: the DBM has **10 `_isPaused` sites, all inside its own protocol**, and every external consumer is display or per-frame ⇒ a one-frame deferral looks safe. ⛔ **UNMEASURED: whether `BlueprintDebugSession`'s own step machinery** *(temp breakpoints, `_nodePointer`, the recorder)* **tolerates the DBM resuming a frame later.** ⭐ **`104a`'s rail is what will settle it** |
| `RF-7` `RunStateSource` reads the clock through | ⭐ hygiene; ⚠ the arm is already **correct** as of `c0c066334` |
| `RF-8` cluster-wide debugger pause | 🔒 the user: *"not now"* — joins UX Ruling 62 |
| `RF-9` `ClusterUiCache` rename | ⭐ decided *(keep it — it observes the wire)*, not scheduled |
| `RF-10` BTree/HSM coordinator gets the real time controller | ⭐ **`AS-9`, a measured `R-67` instance** — its own item, later |
| the other ten pause notions | ⛔ **not one refactor, ten** |

---

## 8. ⭐ GATES — **ONCE, at the end** *(the contract, all seven rows)*

⭐ Baseline = **Batch 103's table**, base **`c0c066334`**. ⚠ State the environment *(Xvfb or not)*.

| ⭐ extra rows this batch needs | |
|---|---|
| **`Fdp.Toolkits.Tests`** | ⚠ `DEBT-AIB-030` — **seven tests whose identity ROTATES.** ⛔ Confirm by `--filter`/namespace and say so. ⭐ **`ThePauseFlagOnTheClockIsFalseWhilePausedTests` must stay 4/0** |
| **`Fdp.ModuleHost.Tests`** | ⭐ **new to the table** — `104c` touches the kernel and the scheduler |
| **`104a`'s failure map** | ⭐⭐ **before and after**, per pause kind |
| **the `[Obsolete]` fallout** | ⭐ every site, if any |

> ## ⭐⭐⭐ `Hrot.ClusterRunner.Integration.Tests` IS BACK IN — **as a GATE ROW** *(user, `2026-08-21`)*
> 🔒 *"these changes start to have very big blast radius… we should use these to verify if the time
> control during the refactoring still works as it used before."* ⭐⭐ **Correct, and it RUNS.**
>
> ⛔⛔ **`BP-378` has ROTTED.** 📐 Measured on the dispatch branch:
> ```
> dotnet build Hrot.ClusterRunner.Integration.Tests --no-restore   → 0 errors, 88 s
> dotnet test  --no-build --filter "FullyQualifiedName~TimeControlIntegrationTests"
>                                                                  → 4 passed / 2 FAILED, 38 s
> ```
> ⚠ **No OOM, no hang.** ⛔ **The FULL suite is still untested** — ⭐ **run it FILTERED**, and ⚠ **report
> `BP-378`'s rot as a finding** *(rule 3: it is the implementation session that renumbers/updates rows)*.
>
> | ⭐⭐ **THE BASELINE IS `4 / 2`** | ⛔ **a THIRD red is a regression THIS BATCH caused** |
> |---|---|
> | ⛔ `PauseStepResume_SimTimeAdvancesByStepAmount` | *"~3s after 3 steps; actual **1.000s**"* |
> | ⛔ `MixedSequence_PauseStepPauseStep_AllCorrect` | *"expected ~2s; got **1.000s**"* |
>
> ⭐ **Both are ONE pre-existing defect — `AS-14`.** 📐 `MasterSyncController.Step:188-195` returns early
> when `_pendingAcks.Count > 0` ⇒ **the step is DISCARDED, not queued, and the caller is not told.**
> ⭐ **PRE-EXISTING, provable by construction:** ⛔ no production file under `FDP/…/Time/`,
> `Hrot.Orchestrator`, `MasterSync*`, `SlaveSync*`, `ClusterMaster` or `ModuleHostKernel` was touched on
> the coordinator branch. ⚠ **Do NOT fix it in this batch** — ⭐ **it is `AS-14`'s own item**, and this
> batch changes nothing on that path.
>
> ⭐⭐ **Report it before AND after your changes.** 📌 `104c`/`104d` add a kernel phase and a system that
> runs on **every node**, so this suite is the only thing that will notice if a slave stops keeping time.

⛔ **The rest of `Hrot.ClusterRunner.Integration.Tests` stays out** *(`BP-378`, unverified for the full run)*.
⚠ **`Hrot.ClusterRunner.Tests` carries 2 pre-existing reds** — `DataDrivenGizmoPredicateTests.D003_*`,
proven against base in Batch 103 and reproduced again on `c0c066334`.
