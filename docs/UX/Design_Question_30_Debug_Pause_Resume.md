# Design question 30 — breakpoint pause / resume semantics on CGF

> **For [UXI-37](UX_Issues.md#uxi-37) · drafted 2026-08-14.** Inputs: [rulings 60, 61, 62](UX_RESUME_INTERACTION.md).
> ⚠ **No architect round — resolved in-house** (user, 2026-08-14: *"the architect is not available now… we should
> analyse this ourselves"*). Written primarily so the analysis **survives compaction**.
> **Status: ✅ ALL DECIDED — A-E closed by [rulings 62-64](UX_RESUME_INTERACTION.md). Ready to design.**

## 0. What is already settled

| | |
|---|---|
| 🔒 **Ruling 61** | debug from **Editor and CGF only** ⇒ the debugger always sits on the node owning the world; the single-process `IAiDebugSession` contract is honest |
| 🔒 **Ruling 62** | **a breakpoint hit on CGF freezes the WHOLE cluster** |
| 🔒 **Ruling 61** | exact on CGF; **latency accepted** elsewhere; data breakpoints cover only CGF-owned or CGF-replicated components |
| 🔒 **Ruling 61** | *"the world cannot jump back in time"* — no rewind of other nodes |

## 1. ⭐ The mechanism that already exists — verified, not proposed

| Piece | Where |
|---|---|
| Master pause | `MasterSyncController.SwitchToDeterministic(slaveRoster)`; `Step(dt)`; `SwitchToContinuous()` |
| Slave states | `SlaveSyncController`: `Continuous` · **`BarrierPending`** · **`Stepping`** |
| Barrier | a `SwitchTimeModeEvent` carries **`BarrierWallTicks`** — *"an absolute timeline anchor from the Master… we unconditionally synchronize to it"* (`:187-190`). Future barrier ⇒ `BarrierPending`, *"perfectly absorbs minor NTP jitter"* |
| ⭐ **Resume** | `ApplyResume(evt)` → **`ApplyTimeSnap(evt)`** → `_baselineSimTime = evt.SimTimeSnapshot` — the slave **snaps its sim-time baseline to the master's authoritative snapshot** |
| Wire | `TimeNetworkModule.CreateSlaveLockstepTranslator` · `SteppedSlaveController` · `SlaveTimeModeListener` |
| Debug seam | `IEngineDebugTimeController` in **`Hrot.Blueprints.Core`** (neutral); Editor adapter = `MasterSyncTimeControllerAdapter`; 🔴 **CGF = `CgfNoOpTimeController`, all three request methods empty** |

> ⇒ 🔒 **The gap-closing question is already answered by the engine, and it is the user's lean.**
> `ApplyTimeSnap` is a **zero-dt sim-time jump**: no re-execution, no ticks replayed, no dt charged to CGF.
> **Every slave already does this after every pause.** The debug case is not special — so the question
> becomes *"is the existing snap acceptable here?"*, not *"which of three schemes do we invent?"*

## A. 🔴 Does the debug halt stop the KERNEL tick or only the SIMULATION systems?

**This is the load-bearing question, and it is not a preference — one answer deadlocks.**

The barrier resolves inside `SlaveSyncController.Update()` → `DrainModeSwitchEvents()`, which runs **as part of
the tick**. And `SyncedWallTicks = _getTick() + _masterWallClockOffset` is a **wall-clock** anchor, not sim time.

| If the debug halt stops… | Result |
|---|---|
| **the whole kernel tick** | 🔴 **deadlock** — `Update()` never runs, the master's `Deterministic` event is never drained, `BarrierPending` never resolves, and the resume event is never seen either. **CGF freezes and cannot be resumed by the cluster** |
| ✅ **only the simulation/brain systems** | the kernel keeps ticking, time sync keeps running, the barrier resolves, resume arrives. The brain is frozen; the node is alive |

🔒 **Lean — decided: only the simulation systems.** ⭐ **And the shape already exists**: CGF composes its brain
into `TogglableInputGroup` / `TogglableSimulationGroup` (`CgfSubsystem.cs:330-334`) — **togglable groups are
precisely a "stop the sim systems, keep the kernel" switch.** ⚠ Verify the toggles gate the brain
systems *without* gating ingress-poll and time-sync, or A and C collide.

## B. How is the sim-time gap closed on resume? — ✅ answered by the engine

CGF halts at breakpoint tick **T**; the cluster halts at barrier **T+k**; on resume the master publishes an
authoritative `SimTimeSnapshot` and CGF **snaps** to it.

| Option | Verdict |
|---|---|
| ⭐ **A · zero-dt snap to master time** (the user's lean) | ✅ **already implemented** (`ApplyTimeSnap`). No re-execution, no big-dt hazard |
| **B · true-dt fast-forward of k ticks** | 🔴 **rejected — worse than it looks.** It re-executes brain logic k times, so **the breakpoint can immediately re-fire on resume**; and those k ticks would run against *current* (T+k) replicated state, not the state that existed at T…T+k — so it is **not a faithful replay either**. Neither cheap nor correct |
| **C · one catch-up tick with dt = k·Δt** | ⚠ rejected — a large dt is a classic source of integrator/nav breakage, for a k measured in **milliseconds** |

⚠ **The one cost of A, stated plainly:** a cooldown or timer started at T is instantly **k ticks older** when the
clock snaps. With k = freeze-convergence latency (tens of ms), this is negligible for a deliberative brain —
**but it is a real discontinuity, and it is the price of not rewinding the world.**

## C. ✅ DECIDED — no world-state ingress while frozen; the translators must be **categorized**

> **User, 2026-08-14:** *"No replication and simulation-state-changing ingress while frozen (this might need
> categorizing the ingress translators)."*

🔒 **Decided: freeze CGF's world whole.** The reasoning that survives:

| | |
|---|---|
| ⭐ **The window is bounded by k, not by the operator** | after the barrier the **whole cluster** is frozen ([question D](#d--decided--freeze-and-step-the-whole-cluster-in-deterministic-mode)), so nothing is being produced. *"Changes made during the pause"* can only occur in **T → T+k** |
| 🔒 **Coherence beats freshness** | brain state at **T** with replicated state at **T+k** means reading a decision made at T against data from T+k — the exact confusion a debugger exists to prevent |
| ✅ **No ECS-access violation** | ingress writes through a command buffer **inside the tick**; with the sim halted there is no legal place to apply it |
| ⚠ **Partly moot** | DDS **keep-last** overwrites intermediate samples anyway — not polling loses nothing that polling would have kept |

### 🔴 The categorization this forces — and it is REQUIRED, not a nicety

⚠ **Question A already proves control-plane ingress must keep running**: if the translators that deliver
`SwitchTimeModeEvent` stop, CGF never sees its own **resume**. So *"stop ingress"* cannot mean *all* ingress.

| Class | Examples | While frozen |
|---|---|:--:|
| **Control plane** | time-mode + **lockstep** translators (`TimeNetworkModule.CreateDescriptorTranslator` / `CreateSlaveLockstepTranslator`), orchestration/cluster commands | ✅ **keeps polling** — the resume arrives through here |
| **World state** | entity/component replication, descriptor ingress, mission/intent ingress | 🔒 **stops with the sim** |

⭐ **Prior art — the split already half-exists at the bus level.** `CgfApplication.cs:113-114` builds **two**
buses: `_eventBus` and `_orchestrationBus`, the latter commented ***"Control Plane bus (orchestration/cluster
management)"***. ⚠ **But the boundary is not the same one**: the **time** events ride `_eventBus`, so the
existing bus split does **not** by itself separate "keeps running" from "stops".

⇒ 🔒 **The new bit is small and belongs on the translator**: `IDescriptorTranslator` today carries `TopicName`,
`DescriptorOrdinal` and `Direction` (Ingress/Egress) — **add a category**, and have the freeze gate poll only
the world-state ones. ⚠ **Fail-safe default: a translator with no explicit category counts as WORLD STATE**
(it stops). A missed control-plane translator then shows up as *"resume does not work"* — loud and immediate —
whereas the opposite default leaks live world data into a frozen snapshot **silently**.

> ✅✅ **BUILT `2026-08-25`** (`CE-028`). `TranslatorClass {WorldState=0, ControlPlane=1}` sits beside
> `TranslatorDirection`, and `Category` is a **default interface member on `INetworkTranslator`** — so
> every existing implementation inherits the fail-safe answer without being edited. ⭐ The three time
> translators are marked `ControlPlane`; an enumeration rail asserts they are the ONLY overrides, because
> the default protects against a FORGOTTEN mark and nothing protected against a WRONG one.
>
> ⛔⛔ **Two corrections to this section, measured:** ① the gating target named above and in
> [UXI-37 §1a](UX_Feature_Cgf_Brain_Diagnostics.md) is **`CycloneIngressSystem`**, which has **zero
> production registrations** — the real class is **`CycloneNetworkIngressSystem`**; ② it is not one
> system but **12 production constructions across 9 files in 6 assemblies**, five of them on CGF, and
> **one of those five carries only the three time translators**. ⇒ ⭐⭐ the category is load-bearing for a
> sharper reason than stated here: the gate is applied to **every** ingress system on the node, so only
> `Category` keeps [question A](#a--does-the-debug-halt-stop-the-kernel-tick-or-only-the-simulation-systems)'s
> deadlock away. 📄 [`…Slice4_Debug_PauseStep.md` §10.3](../DESIGN_Cgf_Editor_Sharing_Slice4_Debug_PauseStep.md).
>
> ⚠ **Not audited translator-by-translator:** the NED **auxiliary** pack (combat, mission-control). If any
> of it is control plane it now stops with the sim — a follow-up, not a claim.

⚠ **Surface it in the UI**: while paused, CGF's remote-entity view is **stale by up to k ticks**. The paused
view should say so rather than imply live data.

## D. ✅ DECIDED — freeze and step **the whole cluster**, in deterministic mode

> **User, 2026-08-14:** *"Freeze (sim time) and step the whole cluster in deterministic mode."*

| Case | Mechanism | Cluster? |
|---|---|---|
| **Within a tick** (node → node) | walks the **node-granular recording**; clock paused, **no re-execution** (NGS-2.1) | ⭐ **no** — exact on CGF by construction, and free |
| **Crossing a tick boundary** | 🔒 **`MasterSyncController.Step(dt)` — every node advances one tick together** | ✅ **yes** |

| | |
|---|---|
| ⭐ **The protocol already exists** | `SwitchToDeterministic(roster)` + `Step(dt)` on the master; `SlaveMode.Stepping` + `SteppedSlaveController` + `_lastAcceptedStepFrameId` on the slave |
| 🔒 **Why not step CGF alone** | its brain would tick against a **frozen** world — every stepped tick sees an unchanging environment, misrepresenting the very behaviour under investigation |
| ⚠ **Cost, accepted** | each tick-crossing step is a network round trip, so cross-tick stepping is **slow**. Within-tick stepping is unaffected, and is the common case |
| 🔴 **Interaction with [A](#a--does-the-debug-halt-stop-the-kernel-tick-or-only-the-simulation-systems)** | CGF's sim group is toggled **off** while frozen ⇒ a granted step must **re-enable it for exactly one tick**, then toggle off again. The toggle is the step actuator, not just the halt |

## E. ⚠ "Master unreachable" — what the question actually is

⚠ **Asked by the user, 2026-08-14: *"what do you mean by 'master unreachable'?"*** — fair; the phrase was
doing too much work. Concretely:

**Every decision above assumes the freeze request reaches the master and comes back as a `SwitchTimeModeEvent`.**
CGF is a **slave** — it cannot freeze the cluster itself; it can only ask. The question is what happens when
that ask goes nowhere.

| When it can happen | |
|---|---|
| ✅ **A documented mode, not just a fault** | `CgfApplication.cs:107` — *"When null, the node operates **without DDS** (offline / pure-domain test path)"*, and `SlaveSyncController` is still installed unconditionally while the time-mode and lockstep translators are created **only if a participant exists** ⇒ a CGF with no DDS has a slave clock that **can never receive a mode switch** |
| ⚠ Orchestrator not running, crashed mid-exercise, or a transient DDS partition | same effect, transiently |

**Why it matters beyond tidiness:** it breaks the **bounded-k** assumption that [B](#b-how-is-the-sim-time-gap-closed-on-resume--answered-by-the-engine) and [C](#c--decided--no-world-state-ingress-while-frozen-the-translators-must-be-categorized) both rest on. If the freeze never
lands, SimHost keeps running while CGF sits halted, so **k grows without bound** — the timer discontinuity on
resume stops being negligible, and the *"stale by up to k ticks"* note becomes *"arbitrarily stale"*.

| Option | |
|--:|---|
| **a** | refuse to break — 🔴 no: a debugger that silently declines to stop is worse than one that stops partially |
| **b** | 🎯 **halt CGF locally anyway, and say plainly that the cluster is still running** | 
| **c** | halt locally and **auto-resume** after a timeout | ⚠ surprising: the operator's breakpoint would vanish while they read it |

🔒 **DECIDED — (b)** ([ruling 64](UX_RESUME_INTERACTION.md), user 2026-08-14: *"yes to E, halt locally and say
cluster still running"*). **With the offline case handled separately**: when CGF has **no participant at all**,
there is no cluster to freeze and the local halt **is** the complete and correct behaviour — not a degraded
one. ⚠ **Do not show a warning in that mode**; it is normal operation, and a permanent warning in a supported
mode is [ruling 49](UX_RESUME_INTERACTION.md)'s dead affordance in another costume.

## 2. Summary of decisions

| | Question | Lean | Firm? |
|--:|---|---|:--:|
| **A** | halt scope | **sim systems only** — kernel keeps ticking, else deadlock | 🔒 decided |
| **B** | gap closing | **zero-dt snap** — already implemented (`ApplyTimeSnap`) | 🔒 decided |
| **C** | ingress while paused | **no world-state ingress**; control-plane keeps polling ⇒ **categorize translators**, defaulting to world-state | 🔒 decided |
| **D** | step granularity | **freeze and step the whole cluster, deterministic mode**; within-tick stepping stays local and free | 🔒 decided |
| **E** | freeze request unanswered | **halt locally and say the cluster is still running**; in the documented **no-DDS** mode that is normal, not degraded — no warning | 🔒 decided |

## 3. Risks

| | |
|---|---|
| ⚠ **The toggle groups must not gate time sync or ingress-poll** | if `TogglableSimulationGroup` also stops the systems that drain `SwitchTimeModeEvent`, question A's deadlock returns through the back door. **Verify before building** |
| ⚠ **`IsPausedByDebugger` is already live on CGF** | the read half works today (`_bpManager?.IsPaused`) — so a half-built state is observable now; do not assume the flag means the clock stopped |
| ⚠ **k is expected small, still unmeasured** | 🔒 [Ruling 64](UX_RESUME_INTERACTION.md) — *"k is expected small"*, so [B](#b-how-is-the-sim-time-gap-closed-on-resume--answered-by-the-engine)'s timer discontinuity and [C](#c--decided--no-world-state-ingress-while-frozen-the-translators-must-be-categorized)'s stale window are both accepted on that basis. ⚠ **It is an expectation, not a measurement** — so **measure k once** during implementation and revisit B and C only if it is large. Do not treat "small" as verified |
| ⚠ **Data breakpoints see only CGF-owned or replicated components** ([ruling 61](UX_RESUME_INTERACTION.md)) | the breakpoint UI must not offer components CGF cannot observe — [ruling 49](UX_RESUME_INTERACTION.md): absent, not greyed |
