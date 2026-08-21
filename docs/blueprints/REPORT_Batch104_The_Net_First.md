<!--STATUS
state: LIVE
updated: 2026-08-21
current-answer: this whole file — what Batch 104 measured, fixed and found.
stale-below: nothing.
known-rot: none.
known-conflict: ⚠ `DESIGN_Time_Architecture.md` §13 is now STALE IN TWO PLACES and the coordinator
  owns it, not this session — §7 names both edits.
-->
# ⭐⭐⭐ REPORT — Batch 104 · **the net works, and the first thing it caught was a node that never answered**

> **Scope frozen at** `34deca154` · **branch** `claude/time-system-refactor-batch-104-gp617x` ·
> **started marker** `chore: started batch 104 at 34deca154` *(rule 1b, pushed before any code)*
> ⭐ **Branched by `--ff-only` from the coordinator head `91b53840`** *(rule 7)*, which is `34deca154`
> + 5 commits. ⚠ **Stated rather than assumed:** those five are **docs only** — `CLAUDE.md`, the
> `DESIGN_Time_*` merge, `PLAN_Time_System_Refactor.md`, `RULINGS.md` and the dispatch stamp
> *(`git diff --stat 34deca154 91b53840` — 6 files, all under `docs/` or `.claude/`)*. ⇒ **no
> production file moved after the freeze sha**, so the frozen scope is intact.
> ⭐ This is the **TIME lane's first batch**, approved `2026-08-21`. ⭐ ids **`TM-`**, tracker
> **Area H only**.

| item | verdict | one line |
|---|---|---|
| **`104a`** | ✅ **done** | ⛔⛔ **hypothesis ②, and worse than stated: the CGF node could not ACK — it had no time translators at all.** Both halves fixed |
| **`104b`** | ✅ **done** *(measurement)* | ⛔ **`BP-378` has NOT rotted for the FULL run** — it still aborts, twice, differently. ⭐ **A class-at-a-time gate is real**: see §4 |
| **`104c`** | ✅ **done** | gate row established, **run twice**, no flake |
| **`104d`** | ⚠ **partial** | 2 of 3 gaps closed *(`SetTimeScale`, CGF participation)*; ⛔ **editor-composition and breakpoint-pause NOT added — reasons in §5**, not silence |

⭐⭐ **IDs I allocated:** `TM-001` · `TM-002` · `TM-003` · `TM-004` · `TM-005` — ⭐ **all in the
tracker's new `Area H — Time & clock`**, none anywhere else in that file.

---

## 1. ⛔⛔⛔ `104a` — **root-caused BEFORE it was fixed, and the answer changes the picture**

### ⭐ The handoff put two hypotheses on the table. **It is ②** — and ② was understated.

📐 **The probe** *(a throwaway rail, since removed — §5)*: sample `_pendingAcks`, `_expectedSlaves`
and each node's controller mode around every step.

```
after PAUSE   : mode=Stepping pendingAcks=[]    expectedSlaves=[1,400] simHost=Deterministic cgf=SlaveSyncController/Continuous
after STEP 1  : mode=Stepping pendingAcks=[400] expectedSlaves=[1,400] simHost=Deterministic cgf=SlaveSyncController/Continuous
  STEP 1: waited 5002 ms for ACKs; remaining=1
after STEP 2  : mode=Stepping pendingAcks=[400] …  masterSimTime=4.928   ⛔ unchanged
after STEP 3  : mode=Stepping pendingAcks=[400] …  masterSimTime=4.928   ⛔ unchanged
```

| ⭐ what the numbers say | |
|---|---|
| **node 1 (SimHost) ACKs** | it leaves `_pendingAcks` on the first `Update()` |
| ⛔⛔ **node 400 (CGF) NEVER ACKs** | 📐 **5 000 ms and thousands of pumped frames, three times** |
| ⛔⛔⛔ **and CGF never even left `Continuous`** | ⇒ **it never heard the pause.** ⚠ **This is not "the settle is too short" — there is nothing to wait for** |
| ⇒ | ⭐⭐⭐ **only the FIRST step of any session ever worked**, in every session, always |

⛔ **The handoff said "if it is ②, the defect is in the harness or the ACK wiring."** ⭐ **It is the
ACK wiring, and it is PRODUCTION** — 📌 not a harness artefact, see below.

### ⛔⛔ `TM-002` — **why CGF cannot answer: it composes past the code that wires it**

| ⭐ | |
|---|---|
| **the three translators** | `SwitchTimeModeDescriptorTranslator` *(hears the pause)* · `SlaveLockstepTranslator` *(`FrameOrder`→`AdvanceFrameIntent` in, `FrameStepCompletedEvent`→`FrameAck` out)* · `SlaveTimeSyncTranslator` |
| **who wires them** | ⭐ `SharedApplicationBootstrapper` **phase 6c** — ⇒ SimHost · IG · StrideMock |
| ⛔⛔ **who does NOT** | **`CgfSubsystem`**, which builds through `HrotNodeBuilder` **directly** and never runs that bootstrapper ⇒ the node holds a `SlaveSyncController` **with nothing connected to it** |
| ⛔⛔⛔ **and yet it is in the roster** | `OrchestratorSubsystem:303` **and** `ClusterMaster:327`, both `SubsystemName is "SimHost" or "IG" or "CGF"` ⇒ ⭐ **the master blocks every step on a node that is structurally unable to reply** |
| ⚠ **the cruel detail** | 📌 **`CgfApplication` DID wire them** *(`:118-119`)* — ⛔ it has **exactly one caller, a unit test.** ⇒ **the working copy is the dead one, and the live one is the broken one** |

⇒ ⭐⭐ **Fix: phase 6c extracted to `SlaveTimeTranslatorRegistration.RegisterOn(...)` and called from
BOTH sites.** ⛔ **Not copied into CGF** — 📌 the standing ruling is *"no keeping two implementations
for the same concept"*, and a second copy is precisely how the first one rotted.

### ⭐⭐⭐ `TM-001` — **and the silent discard is STILL a defect, so it was fixed too**

⚠⚠ **Fixing only `TM-002` would have turned the suite green and left the trap armed.** 📌 The plan says
so itself: *"`AS-14` gets WORSE under `T4`: intents can be published faster than ACKs return."*

| ⭐ the choice the handoff asked me to make and justify | |
|---|---|
| ⭐⭐⭐ **QUEUE, bounded — and REFUSE audibly past the bound.** ⛔ **Not one or the other** | |
| **why not refuse-only** | ⭐ the operator clicking Step three times means three steps; a refusal that is merely *logged* still loses the motion they asked for |
| **why not queue-only** | ⛔ **`TM-002` is exactly the case that breaks it** — a node that has stopped ACKing **forever** would accumulate an unbounded queue and then fire the whole burst if it ever returned. ⚠ **Unbounded queueing would have HIDDEN `TM-002`, not surfaced it** |
| **why the ACK guard stays** | 📌 the handoff's own constraint, and it is right: **removing it trades a lost step for a cluster desync** |
| ⭐ **the bound** | `TimeConfig.MaxQueuedSteps`, default **8** — a config knob, not a magic number in the controller |

| ⭐ the resulting contract | |
|---|---|
| ACKs outstanding, room in the queue | **deferred**, released by `UpdateStepping` the moment the ACK set clears — ⭐ **one per frame**, because the next one waits for this one's ACKs |
| queue full | **refused**, `Warn` naming the nodes that have not ACKed, `RefusedStepCount++` |
| not in `Stepping` mode | **refused**, `Warn`, `RefusedStepCount++` — ⛔ **this used to be a silent no-op too** |
| **Resume** | **drops** what was deferred, and the `RESUME` log line **says how many** — 📌 a step queued during one pause must not fire into the next |

⭐ **Observability without inventing vocabulary:** `QueuedStepCount` / `RefusedStepCount` are public
properties. ⛔ **No new event type** — 📌 a `StepDeferredEvent` for a toolbar affordance is **`W4`**,
explicitly not this batch.

---

## 2. ⭐⭐ WHAT MOVED — **7 production files, and 2 of them are the fix**

| file | what |
|---|---|
| ⭐⭐⭐ `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/MasterSyncController.cs` | `Step` splits into `Step` *(decide)* + `ExecuteStep` *(issue)*; queue + refusal counters; drain in `UpdateStepping`; clear on Resume/`SnapAndPause` |
| ⭐ `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/TimeConfig.cs` | `MaxQueuedSteps` |
| ⭐⭐ `Hrot/Engine/Hrot.Common/Infrastructure/SlaveTimeTranslatorRegistration.cs` | **new** — phase 6c, extracted |
| ⭐ `Hrot/Engine/Hrot.Common/Infrastructure/SharedApplicationBootstrapper.cs` | phase 6c now **calls** it *(−18 lines, behaviour identical)* |
| ⭐⭐⭐ `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | **calls it too** — the fix; plus `TestHook_TimeControllerType`/`Mode` |
| ⭐ `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs` | `TestHook_TimeScale` |
| — | ⛔ **no `GlobalTime` change · no new `SystemPhase` · no drain system · no refusal deletion · no bus move** *(handoff §5)* |

### ⚠⚠ SCOPE NOTE — **`Hrot.CGF` and `Hrot.Common` are NOT in the time lane's file list. Flagged, not hidden.**

📌 The lane table lists `Fdp.Toolkits/Time/` · `Hrot.Orchestrator` · `ModuleHostKernel` ·
`Hrot.ClusterRunner.Integration.Tests`. ⭐ **That list was derived as a MEASUREMENT of what the
refactor touches, before anyone knew where `AS-14` lived.**

| ⭐ why I proceeded rather than stopping | |
|---|---|
| **① the handoff anticipated it** | *"if this is it, the defect is in the harness or **the ACK wiring**"* ⇒ **this IS `104a`'s fix**, not scope creep |
| **② the cross-lane rule is not triggered** | ⛔ the rule protects the **UI/variable lane** — `AiShared` · `Blueprints/BTree/Hsm.Editor` · variables · working state. 📐 `Hrot.CGF` and `Hrot.Common.Infrastructure` are **neither lane's**, and are **different assemblies** from everything the UI lane holds ⇒ **no shared production file, no merge collision** |
| **③ `T0` is a blocker for the whole programme** | 📌 a STOP here stops `T1`…`W5` as well, and `R-106` says a blocked item stops **that item** — ⛔ but this item was not blocked, only *outside a list* |
| ⚠ **what would have made me stop** | a file the UI lane owns. ⭐ **There was none** |

⇒ ⭐ **The coordinator should widen the TIME lane's row to name `Hrot.CGF` + `Hrot.Common.Infrastructure`**, or say the edit should have been a stop. **Either is fine; the silence is not.**

---

## 6. ⭐⭐ GATES

⚠ **Environment: Linux cloud container, 4 cores / 16 GB, `dotnet 8.0.424`. ⛔ NO Xvfb** — nothing in
these suites needs a GL context.

---

## 7. ⛔⛔ FOR THE COORDINATOR — **two design-doc statements are now stale, and that file is yours**

📌 **Rule: the coordinator designs; the implementation session does not rewrite `DESIGN_*`.** ⭐ So
they are **named here rather than edited**:

| 📄 `DESIGN_Time_Architecture.md` | what is now wrong |
|---|---|
| ⛔ **§13 `AS-13` — *"`BP-378` HAS ROTTED — no OOM, no hang"*** | ⭐ **True of the FILTERED run only, and the doc says so** — ⚠ but the headline reads as a verdict on the suite. 📐 **The FULL run aborts**: §4 |
| ⛔⛔ **§13 `AS-14` — *"either the settle is too short or the slave never ACKs in the harness"*** | ⭐ **Neither.** 📐 **CGF is structurally unable to ACK, in PRODUCTION** — `TM-002`. ⚠ *"in the harness"* pointed at the wrong place; the harness is faithful |

⭐ **And one addition worth making:** `AS-14`'s *"it gets WORSE under `T4`"* is now **guarded** —
`TM-001`'s queue is what absorbs the faster intent publication `T3`/`T4` introduce.
