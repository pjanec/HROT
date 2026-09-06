<!--STATUS
state: LIVE
build-state: DESIGN
updated: 2026-09-06
current-answer: §3's four sub-questions, each with a recommended lean. §4 holds the relayed
  NotebookLM input once it arrives, and §5 the verification of every load-bearing claim in it.
design-basis: docs/DESIGN_Stride_Node_Modes.md §4, §7.3, §11, §13 (the owning design; CE-207 is its
  slice S4) · .dev/_DONE/stride-mock/DESIGN.md §5.6/§5.9/§5.10 and §6 (the mode-2 node design that
  predates the real Stride shell) · docs/DESIGN_Stride_Port.md §8, §9 (the as-built tick correction
  ST-021 and the mode rails).
known-rot: none yet.
known-conflict: none.
-->

# Q66 — **`CE-207`: Stride as a networked node replacing SimHost. What does the design record already decide?**

> ⭐ **Scope.** `CE-207` *(mode 2 — a Stride process joining a cluster beside CGF, providing
> Muscle · Perception · Navigation)*. 📄 Owning design:
> [`DESIGN_Stride_Node_Modes.md`](../DESIGN_Stride_Node_Modes.md), slice **S4**.
> ⛔ Not in scope here: `CE-205`/`CE-206` *(capabilities, perception)*, `CE-210` *(LOS)* — those have
> their own rows.

## 1. INVENTORY

⭐ `search_graph` + grep, `2026-09-06`, branch `claude/reset-working-branch-qd1qpv`.
⚠ `check_index_coverage` is not reachable from this session, so no count below is a completeness proof.

| query | total | what it settled |
|---|---|---|
| `search_graph(".*Stride.*", label="Class")` | **65** | the whole Stride surface |
| `grep "AttachBootstrapper"` | **1 declaration, 0 callers** | `StrideHrotGame.cs:266` — mode 2's entry point is unreachable |
| `grep "StridePhysicsBracket"` | class + **2 construction sites** in `EditorStrideSubsystem` | ⭐⭐ **the physics half of the view tier is ALREADY EXTRACTED** into `Hrot.Stride.Core` — see §3B |
| the bracket's own doc header | pre-kernel order **1–5**, post-kernel forward-sync | `PhysicsBodyLifecycle` → `VehicleNavIntentSystem` → `CharacterMotor` → `VehicleMotor` → `ReverseSyncGroup`, all **before** `Kernel.Update()` |
| `EditorStrideSubsystem.cs:367-376` | the documented frame order | `A` TimeController.Step · `B` RunPreKernelStep · `C` Kernel.Update · `E1` AnimationBridge · `E2` RunPostKernelStep · `E3` gizmos · `E4` selection |

## 2. THE PROBLEM IN ONE PARAGRAPH

Mode 2's node composition root (`StrideNodeBootstrapper`) is **written and dormant** — it implements all
seven `SharedApplicationBootstrapper` hooks, ticks `Kernel.Update()` with no `dt` as a time **slave**,
and has **zero callers**. What is missing is everything around it: who drives its frame, where the 3-D
view tier is composed when there is no editor, how the process joins a cluster, and what the view tier
owes replay. Each is a design call with a large blast radius, so each is a sub-question below.

## 3. SUB-QUESTIONS, EACH WITH A LEAN

### A — the tick contract: who owns `dt`?

Mode 2 is a **time slave** (mock design §5.6/§12.5: *"single authoritative time source … all nodes halt
on the exact same microsecond"*), but its frame is driven by Stride's **render loop**, which is
free-running. Mode 1 side-steps this: `EditorStrideSubsystem.TickHosted` runs `A` = a local
`TimeController.Step(dt)` before the bracket, because the editor **is** the time authority.

⭐ **Lean:** mode 2 keeps `Kernel.Update()` parameterless *(already as-built, `ST-021`; the `Update(float)`
overload's own attribute says it "will cause deterministic desync")*, **drops step `A` entirely** — the
master supplies time — and passes the render `wallDt` only to the view tier and the gizmo producer
buffer. ⚠ **The open half:** the bracket's `RunPreKernelStep(world, dt, simRunning)` takes a `dt`. On a
slave, is that the render delta or the sim delta the controller just applied? Bullet is stepped by
Stride's external loop, so the two genuinely differ.

### B — where is the 3-D view tier composed?

Mode 1's view tier lives **inside** `EditorStrideSubsystem` (steps `E1`–`E4`). `StrideCapabilities`
*(`CE-205`)* covers Muscle/Perception/Navigation only. The mock's equivalent
(`SyncFdpToStrideScript`, its §6) was deleted with the mock.

⭐⭐ **Lean — and this is a seam-law case:** `StridePhysicsBracket` is **already** the extraction for the
physics half — a cohesive, host-driven unit in `Hrot.Stride.Core` with a documented order, constructed
by `EditorStrideSubsystem` rather than owning it. ⇒ **mirror it for the view half** rather than inventing
a composition root: a sibling bracket owning `E1`–`E4` that both shells construct. ⛔ Do **not** re-create
`SyncFdpToStrideScript`.

### C — launch, identity and configuration

`Hrot.ClusterRunner` has **zero** Stride references and the `stridemock` token now throws (`ST-015`), so
mode 2 is a separate executable joining an existing cluster. The mock design's §9 answers this for a
runner mode that no longer exists.

⭐ **Lean:** CLI args mirroring `ClusterRunner`'s — `--node-id` *(default **700**, still free from mock
§9.2)*, `--domain`, `--staging`, and a `--no-wait` equivalent — plus one `--mode editor|node` switch,
default `editor`. ⛔ Not env vars: those are mode 1's debug switches and already sprawl.

### D — what does the view tier owe REPLAY?

Mock §5.9 puts mutative systems in `TogglableSimulationGroup` before orchestration is built, and §6
argues the ECS→visual sync must be a **differential 2-pass `IsAlive()` reconciler** because replay's
`PlaybackSystem` does *"raw ECS memory blits — lifecycle events are not fired."*

⭐ **Lean:** that argument survives its shell — the view bracket must reconcile differentially and be
suspended by the togglable groups during `LoadingReplay`, and `ReplaySeek` must drop stale visuals by
generation counter. ⚠ **Unverified:** whether mode 1's `StrideVisualBindingSystem` is already
differential in that sense, or relies on lifecycle events it happens to receive because the editor
never blits.

## 4. RELAYED INPUT — NotebookLM architect *(`2026-09-06`)*

⭐ Job **`20260906T160229Z-0d9ef4e3`**, notebook *"HROT - test 1"*, project `simhost`, corpus snapshot `_277`
*(21 sources)*. 176.9 s · 5 736 chars · 18 citations. ⛔ **An input, not a ruling** — the user decides.

> 🔴🔴 **READ THIS BEFORE THE ANSWER: THE SNAPSHOT CONTAINS OUR OWN DESIGN DOC.** The corpus includes
> [`DESIGN_Stride_Node_Modes.md`](../DESIGN_Stride_Node_Modes.md) — written **the same day** — and the
> architect cites it as *"Docs.All_277.01.txt"* for answers **1** and **2**. ⇒ ⭐⭐ **on those two
> sub-questions it is quoting US back at ourselves, not supplying independent judgement.** ⚠ Its value
> is highest exactly where it cites something we did **not** write recently — which is what happened in
> answer 3 *(`SubsystemStatusAnnounce`)* and answer 4.

⭐ **Its four answers in brief** *(verbatim text in the job result; verification in §5)*:

1. `Kernel.Update()` parameterless; `wallDt` goes only to the view tier and gizmo producer buffer;
   the bracket sits at the same point relative to the kernel step as in mode 1. ⭐ **Explicitly SILENT**
   on whether `RunPreKernelStep`'s `dt` is the render or the simulation delta.
2. `StrideNodeShell` owns the mode-2 view tier, built from `StrideVisualBindingSystem`,
   `StrideAnimationBridge`, `DebugPrimitiveRenderer3D`, `StridePhysicsBracket`. **Silent** on whether the
   bracket was meant as a pattern to mirror — it treats it as one of four extracted engine adapters.
3. Node id **700** survives; DDS join via a local `DdsParticipant` + `NedNetworkFactory`; an isolated
   temp root per node; readiness via a `SubsystemStatusAnnounce` topic *(Reliable + TransientLocal)* —
   *"the Waiting Room protocol"*; and *"the standalone process is `FakeStrideApp`"*.
4. The differential two-pass `IsAlive()` reconciler requirement **stands**, because replay's
   `PlaybackSystem` does raw memory blits and fires no lifecycle events — and
   **`StrideVisualBindingSystem` already satisfies it**, having absorbed `SyncFdpToStrideScript`'s job.

## 5. VERIFICATION — **every load-bearing claim, against source**

| # | claim | verdict |
|---|---|---|
| 1a | parameterless `Kernel.Update()`, `wallDt` to the view tier only | ✅ true — ⚠ **but this is our own §11 echoed back**; independent weight ≈ 0 |
| 1b | the record is **silent** on render-vs-sim `dt` for `RunPreKernelStep` | ✅ **correct, and useful** — it independently reaches the same open question §3A names |
| 2a | *"explicit intent"* for `StrideNodeShell` | ⚠⚠ **MISLEADING.** It is **our own `«new»` proposal** in a doc marked `build-state: DESIGN`. ⛔ Presenting a proposal as established intent is exactly the failure the relay rules warn about |
| 2b | silent on the bracket as a pattern to mirror | ⚠ defensible on *intent*, ⛔ but it missed that `StridePhysicsBracket`'s **own header** documents it as a cohesive host-driven extraction with a numbered order — which is the prior art §3B rests on |
| 3a | node id **700** still reserved | ✅ `.dev/_DONE/stride-mock/DESIGN.md` §9.2 |
| 3b | ⭐⭐ readiness via a **`SubsystemStatusAnnounce`** DDS topic | ✅✅ **REAL, and NEW to this design** — `Hrot/Network/Hrot.Network.NED.Tests/SubsystemStatusAnnounceTests.cs` exercises a `DdsWriter<SubsystemStatusAnnounce>` pub/sub round trip. ⭐ **This is the answer's most valuable contribution: §3C had no handshake story at all** |
| 3c | temp root `OrchestrationConstants.DefaultStagingDirectory` | 🔴 **FABRICATED.** No such member — `OrchestrationConstants` declares `ScenariosDirectoryName` · `ExercisesDirectoryName` · `EpisodesDirectoryName` · `SharedDirectoryName`. ⚠ The *shape* `<staging>/nodes/node-<id>` is right *(mock §5.8)*; the symbol is invented |
| 3d | *"the standalone process is `FakeStrideApp` (`Hrot.FakeStrideApp`)"* | 🔴🔴 **STALE — the project does not exist.** `git ls-files Hrot/Runner/Hrot.FakeStrideApp` is **empty**; only untracked `bin`/`obj` leftovers remain. It was deleted with the StrideMock *(`ST-015`)*. ⇒ the architect quoted the **dead** mock design §8 as current |
| 4a | the differential 2-pass reconciler requirement stands | ✅ mock §6 / §11 |
| 4b | ⭐⭐ **`StrideVisualBindingSystem` already satisfies it** | ✅✅ **CONFIRMED — and it CLOSES an open item.** Its own header: *"Implements the two-pass differential sync pattern from `SyncFdpToStrideScript` … Pass 1 (destructions): iterate the visual dictionary … Pass 2 (creations): query all entities with `SimTransform` + `TkbIdentity`; upsert"*. ⇒ §3D's *"unverified"* is resolved **in the architect's favour** |

📐 **Tally: 6 confirmed · 2 misleading · 2 false.** ⚠ Both falsehoods are the same shape — **a retired
artefact quoted as current**, because the corpus still holds the dead `stride-mock` programme. ⇒ ⭐ a
standing caution for every future question: **ask which document a claim comes from, and check whether
that document describes something already retired.**

## 6. DECISION

⏳ **The user's call, per sub-question.** ⭐ What the relay changed, on verification:

| sub-question | effect of the relayed input |
|---|---|
| **A** *(tick contract)* | ⭐ **unchanged, but corroborated** — and the render-vs-sim `dt` question is confirmed genuinely open, not merely unresearched |
| **B** *(view tier)* | ⭐ **unchanged.** The architect named the same four units; ⛔ its *"explicit intent"* framing is our own proposal reflected back |
| **C** *(launch/identity)* | ⭐⭐ **MATERIALLY IMPROVED** — `SubsystemStatusAnnounce` gives mode 2 a real readiness handshake the lean did not have. ⛔ Ignore the `FakeStrideApp` and `DefaultStagingDirectory` claims |
| **D** *(replay)* | ⭐⭐ **RESOLVED** — `StrideVisualBindingSystem` is already the differential reconciler ⇒ the view-tier extraction inherits replay-correctness rather than having to build it |
