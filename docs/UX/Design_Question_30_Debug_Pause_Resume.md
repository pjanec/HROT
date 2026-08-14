# Design question 30 — breakpoint pause / resume semantics on CGF

> **For [UXI-37](UX_Issues.md#uxi-37) · drafted 2026-08-14.** Inputs: [rulings 60, 61, 62](UX_RESUME_INTERACTION.md).
> ⚠ **No architect round — resolved in-house** (user, 2026-08-14: *"the architect is not available now… we should
> analyse this ourselves"*). Written primarily so the analysis **survives compaction**.
> **Status: A-E have leans; C and D need a decision before implementation.**

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

## C. 🔴 Does ingress keep running while paused? — needs a decision

> **User:** *"maybe the ingress needs to work also in pause to keep replicating changes made during the pause…"*

⭐ **First, the window is smaller than it looks.** After the barrier the **whole cluster is frozen**, so nothing
is being produced. The only interval in which remote state changes is **T → T+k**, the freeze-convergence
window. *"Changes made during the pause"* is therefore **bounded by k**, not by how long the operator stares.

| | Argument |
|---|---|
| ✅ **for running ingress** | CGF's replicated view would otherwise be k ticks stale while the operator inspects it |
| 🔴 **against** | **it makes the snapshot incoherent**: brain state at **T**, replicated state at **T+k**. The operator would be reading a decision made at T against data from T+k — *the exact confusion a debugger exists to prevent* |
| 🔴 **against** | ingress writes ECS through a **command buffer inside the tick** (`PollIngress(IEntityCommandBuffer, ISimulationView)`). Applying it with the sim halted means either writing outside a tick — **forbidden** — or queueing, which is just deferral with extra state |
| ⚠ **moot in part** | DDS **keep-last** means intermediate samples are overwritten regardless. Not polling loses nothing that polling would have kept — it only **delays a jump that happens either way** |

🔒 **Lean: NO — freeze CGF's world whole.** Coherence beats freshness for a debugger, the lost interval is
bounded by k, and keep-last makes most of the "lost" data unrecoverable in either design.
⚠ **Consequence to accept and surface in the UI**: while paused, CGF's remote-entity view is **stale by up to
k ticks** — the paused view should say so rather than imply live data.

## D. 🔴 Stepping semantics — needs a decision on one case

| Case | Mechanism | Cluster involved? |
|---|---|---|
| **Within a tick** (node → node) | walks the **node-granular recording**; clock paused, **no re-execution** (NGS-2.1) | ⭐ **no** — exact on CGF by construction |
| **Crossing a tick boundary** | needs one real tick (NGS-2.3 tick-bridge; `RequestStepOneTick`) | 🔴 **decision** |

**The open case:** does *"step one tick"* step **CGF alone** or **the whole cluster**?

| | |
|---|---|
| **CGF alone** | CGF advances to T+1 while the cluster sits frozen at T+k. Cheap and instant. ⚠ But CGF then ticks its brain against **frozen** remote data — every stepped tick sees an unchanging world, which is a **lie** about how the brain would have run |
| ⭐ **whole cluster** (`MasterSyncController.Step(dt)`, which the lockstep protocol already supports) | every node advances one tick together; the world the brain sees evolves correctly ⚠ each step costs a network round trip, so stepping is **slow** |

🔒 **Lean: step the whole cluster.** The protocol exists, and the alternative silently changes what is being
debugged. ⚠ **But note the interaction with A**: stepping the cluster means CGF must accept a granted step
while its sim systems are toggled off — so the step must **re-enable the sim group for exactly one tick**.

## E. What if the master is unreachable when a breakpoint hits?

🔒 **Lean: CGF still halts locally, and the UI says the cluster could not be frozen.** ⚠ Never make the
breakpoint depend on the network — a debugger that silently fails to stop is worse than one that stops
partially. Mirrors [UXI-16/27 §2.0b](UX_Feature_Modal_Surfaces.md)'s *"fails toward telling the user"*.

## 2. Summary of decisions

| | Question | Lean | Firm? |
|--:|---|---|:--:|
| **A** | halt scope | **sim systems only** — kernel keeps ticking, else deadlock | 🔒 decided |
| **B** | gap closing | **zero-dt snap** — already implemented (`ApplyTimeSnap`) | 🔒 decided |
| **C** | ingress while paused | **no** — coherent snapshot; window bounded by k | ⚠ needs a nod |
| **D** | step granularity | **step the whole cluster** | ⚠ needs a nod |
| **E** | master unreachable | **halt locally, say so** | ⚠ needs a nod |

## 3. Risks

| | |
|---|---|
| ⚠ **The toggle groups must not gate time sync or ingress-poll** | if `TogglableSimulationGroup` also stops the systems that drain `SwitchTimeModeEvent`, question A's deadlock returns through the back door. **Verify before building** |
| ⚠ **`IsPausedByDebugger` is already live on CGF** | the read half works today (`_bpManager?.IsPaused`) — so a half-built state is observable now; do not assume the flag means the clock stopped |
| ⚠ **k is unmeasured** | every conclusion above treats the freeze-convergence window as "tens of ms". **Nobody has measured it.** If k turns out to be large, C and B both change character |
| ⚠ **Data breakpoints see only CGF-owned or replicated components** ([ruling 61](UX_RESUME_INTERACTION.md)) | the breakpoint UI must not offer components CGF cannot observe — [ruling 49](UX_RESUME_INTERACTION.md): absent, not greyed |
