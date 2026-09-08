<!--STATUS
state: LIVE
updated: 2026-09-08
current-answer: §S (SESSION STATE, at the END of this file) - read it FIRST after a compaction.
  §2's verification is DONE and CE-221 is fixed; §3 (S4 / mode 2) has NOT started. §1 is standing context.
design-basis: docs/DESIGN_Stride_Node_Modes.md (the owning design; §13 is the slice table, §7.3b and
  §11.1a are the as-built for the two slices most at risk) · docs/blueprints/Blueprint_Issues_Tracker.md
  (CE-205/206/207/208/211/213/214/215/216/219) · docs/RUNBOOK_Cluster_Debugging_Over_Http.md.
stale-below: nothing yet.
known-rot: none yet — this document is new.
-->

# BOOTSTRAP — the Stride Windows session

> ⭐⭐⭐ **Paste this into a fresh Claude Code session on the Windows machine:**
>
> ```
> RELEARN. Then read docs/blueprints/BOOTSTRAP_Stride_Windows_Session.md in full and
> start at its §2. Branch: claude/reset-working-branch-qd1qpv.
> ```

---

## 0. WHY THIS DOCUMENT EXISTS — **five slices shipped, ZERO of them RUN**

📐 **Measured, and it is the whole reason for a Windows session.** Off Windows the only Stride gate is
`bash scripts/stride-check.sh` — a **compile** gate. Its own header states the boundary:

| | |
|---|---|
| restore + **compile** every Stride library / game / test project | ✅ works *(with `EnableWindowsTargeting=true`)* |
| build `HrotStrideApp.Windows` *(the launcher)* | ⛔ Stride's asset compiler wants **Direct3D11** — `MSB3073`, exit 150 |
| **RUN** any Stride test | ⛔ needs the `Microsoft.WindowsDesktop.App` **runtime** |
| **run the app** | ⛔ Windows + GPU |

⇒ ⭐⭐ **Everything below `S0` in §13's slice table is compiled and unverified.** The cloud session said
so in every commit; this session is where that debt is paid.

---

## 1. STANDING CONTEXT — read before touching anything

### 1.1 The obligations that are NOT optional

| # | |
|---|---|
| **①** | ⭐⭐⭐ **`RULE ZERO`: read [`docs/blueprints/RULINGS.md`](RULINGS.md) IN FULL first**, then run `python3 scripts/design-digest.py` and `python3 scripts/rulings-check.py`. ⛔ This is an **implementation** lane — do **not** write a `DESIGN BRIEF` *(coordinator-only)* |
| **②** | ⭐⭐ **`R-129`: the intent is in the design doc, not the code.** The owning design is 📄 [`docs/DESIGN_Stride_Node_Modes.md`](../DESIGN_Stride_Node_Modes.md). ⭐ **Read §13 (slices), §7.3a+§7.3b (the view bracket), §11.1+§11.1a (the physics delta), §4 (the composition) before editing** |
| **③** | ⭐⭐ **`R-142`: run the FEATURE'S OWN rails first.** For Stride those are `EditorStrideSubsystemTests` · `StrideVisualBindingSystemTests` · `StrideAnimationBridgeTests` · `ReverseSyncOrderingTests` · `StrideNodeBootstrapperTests` · `ModeStartupRails`, **plus the two new ones this work added** *(§2.2)* |
| **④** | ⭐⭐⭐ **Obligation ⑤: a deviation goes BACK INTO THE DESIGN before the batch closes**, prior state marked SUPERSEDED. A report is ephemeral; the design is not |
| **⑤** | ⭐⭐ **`R-139`: no lean without a claim table.** Each row cites `file:line` **and** a design basis, or is marked ⛔ assumed. **No assumed row may be load-bearing** |
| **⑥** | ⭐ **Rule 1b:** push an empty `chore: started <batch> at <sha>` commit immediately after syncing, before writing code |

### 1.2 Hard constraints

| | |
|---|---|
| **branch** | ⭐ **`claude/reset-working-branch-qd1qpv`** — push there, `-u origin <branch>`, retry 2/4/8/16 s on network failure. ⛔ **No PR unless the user asks** |
| **commit trailers** | `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>` and the `Claude-Session:` line for **your** session |
| ⛔⛔ **never** | put a **model identifier** in a commit message, PR body, code comment or any pushed artefact — chat replies only |
| 🔒 **never** | modify `scenarios/hill-attack/` — **it is the user's scenario** |
| ⭐ **the user is on mobile** | give **GitHub blob links** for every doc and task id, on this branch, and **gloss every id on first mention** — `CE-219 (Stride's own Bullet step was ungated)` |
| ⛔ | do **not** use the `AskUserQuestion` widget — ask in plain chat prose |
| ⭐ | **build the affected project (~8 s), never the solution (~115 s), in a fix loop** — then `--no-build` for every run after |

### 1.3 What was built in the cloud session, in order

| slice | task | what it did | owning as-built |
|---|---|---|---|
| **S0** | `CE-211` *(dead reckoning was gated on the `ImageGenerator` role)* | DR made role-independent; `SimStamp` added to `NetworkTransform` + `SimStampCodec`; NED's two `_roleHasIG` arms deleted; BDC's DR made real *(it was registered against components nothing wrote)* | 📄 [`DESIGN_Dead_Reckoning.md`](../DESIGN_Dead_Reckoning.md) §5.1/§5.2 |
| **S1** | `CE-205` + `CE-206` *(Stride's capability declaration; perception declared but never filled)* | `StrideCapabilities` — ⚠ **in `HrotStrideApp.Game`, not `Hrot.Stride.Core`** *(the design's home was corrected: Core does not reference what the capabilities need)* | §4.1 |
| **S2a** | part of `CE-208` | the **editor** resolves a `NodeCompositionPlan` — `EditorCapabilities`, role `Brain\|MuscleGround\|Perception\|NavigationSolver`, gated by a **differential** rail that proves the resolved system sequences equal the hand-written block | 📄 [`DESIGN_Subsystem_Composition_Unification.md`](../DESIGN_Subsystem_Composition_Unification.md) §4.1ac |
| **S2b** | `CE-208` | mode 1 composes from `StrideCapabilities`; `StrideNodeBootstrapper`'s four `IEcsModule?` ctor slots **deleted** | §4.1ac.2 |
| ⭐⭐ **S2c** | `CE-219` | **physics runs on the SIM delta and `simRunning` is derived from it**; `Simulation.DisableSimulation` follows it | §11.1a |
| ⭐⭐ **S3** | part of `CE-207` | the view tier extracted into **`StrideViewBracket`**; both tick paths drive it | §7.3b |

---

## 2. ⭐⭐⭐ FIRST — VERIFY. **Do not write S4 code until §2 is green.**

> ⭐⭐ **Why verification comes first and is not busywork.** Two of the six slices changed **live mode-1
> behaviour**, not just structure, and both are the kind of change that compiles perfectly and shows up
> as *"the editor is frozen"* or *"the selection box flickers"*:
>
> | slice | what could break, concretely |
> |---|---|
> | ⭐⭐⭐ **S2c** | `simRunning` used to be `timeMode == Continuous \|\| steppedThisFrame`; it is now `physicsDt > 0`, where `physicsDt` in `Continuous` is **the previous frame's `GlobalTime.DeltaTime`**. ⛔ **If that delta is 0 in mode 1 for any reason** — the editor's controller not advancing the way the cloud session inferred — then **`simRunning` is false forever and nothing moves.** 🔴 **This is the single highest-risk change in the six slices** |
> | ⭐⭐ **S3** | the post-kernel view order moved into a class, and the selection/marker emission became a **callback**. If the callback is dropped or misordered, the selection box vanishes or trails |
> | ⚠ **S2a/S2b** | the editor's and Stride's unit lists are now resolved from a plan. A missing unit is silent |

### 2.1 Sync and build

```powershell
git fetch origin claude/reset-working-branch-qd1qpv
git checkout claude/reset-working-branch-qd1qpv
git pull --ff-only origin claude/reset-working-branch-qd1qpv

dotnet build Stride\HrotStrideApp.sln          # expect 0 errors — INCLUDING HrotStrideApp.Windows,
                                               # which cannot even be built in the cloud
```

⛔ **If `HrotStrideApp.Windows` fails, STOP and report it** — the cloud session has never once compiled
that project, so it is the first genuinely new information this machine produces.

### 2.2 Run the Stride suites — **and BASELINE them**

⭐⭐⭐ **`R-142` + the baseline rule: a red is only a regression if it is red HERE and green at the base.**
The base commit is the last one this branch had before `S0`. Establish it:

```powershell
git log --oneline -20        # find the commit before the CE-211 / dead-reckoning work
```

Then run, on **both** sides:

```powershell
dotnet test Stride\Hrot.Stride.Core.Tests\Hrot.Stride.Core.Tests.csproj --no-build
dotnet test Stride\Hrot.Stride.Animation.Tests\Hrot.Stride.Animation.Tests.csproj --no-build
dotnet test Stride\HrotStrideApp.Game.Tests\HrotStrideApp.Game.Tests.csproj --no-build
```

⭐ **Two rails are NEW and have never executed anywhere:**

| rail | what it asserts | ⚠ if it is red |
|---|---|---|
| `StridePhysicsBracketPauseGateTests` *(`Hrot.Stride.Core.Tests`)* | the sim-advancing gate flips **both ways**, is asserted **every frame**, and a missing service is tolerated | a genuine defect in `S2c` — report it, do not adjust the expectation |
| `StrideViewBracketOrderTests` *(`HrotStrideApp.Game.Tests`)* | a gizmo emitted from the host callback reaches the sink in the **same frame** *(the "BATCH-S2-AG" one-tick-trail regression)*, plus its complement | a genuine defect in `S3` |

⛔ **Neither has ever run.** A red here is information, not noise.

### 2.3 ⭐⭐ The self-test — the closest thing to an end-to-end mode-1 check

```powershell
$env:STRIDE_SELFTEST=1
# launch HrotStrideApp.Windows; it self-exits
# then read logs\editor_stride.log
```

⭐ **Expect the single summary line:**

```
[SELFTEST] RESULT initialHold=PASS repos=PASS pausedFreeze=PASS drive=PASS
```

⛔⛔ **READ `pausedFreeze` NARROWLY — it does NOT verify `CE-219`.** 📐 Measured from
`StrideSelfTest.cs`:

| | |
|---|---|
| ✅ **it covers** | the **deterministic / edit-mode** arm. The app boots deterministic; only `Resume` calls `SwitchToContinuous`. That arm's behaviour `CE-219` did **not** change ⇒ a PASS is a **no-regression** check |
| ⛔ **it does NOT cover** | the arm `CE-219` fixes — **`Continuous` while the CLUSTER is paused.** ⭐ Mode 1 is **networkless**: there is no cluster to pause. ⇒ **that arm is first testable in mode 2 (`S4`)** |
| ⚠ **and it may be blind to gravity** | it measures displacement in FDP **X,Y** at 1 m tolerance. **Gravity is Z.** A body sinking through the floor would PASS |

⭐⭐ **`drive=PASS` is the S2c smoke test that matters most**: it needs `simRunning` to be **true** in
Continuous mode. 🔴 **If `drive=FAIL` with the vehicle not moving, suspect S2c's derived `simRunning`
first** — that is the predicted failure mode, and the fix is one line *(fall back to the wall `dt` in
`Continuous` and file the finding)*, not a redesign.

### 2.4 ⭐⭐⭐ THE VISUAL CHECK — **the one no suite replaces**

🔒 **`CE-203`'s standing constraint: no UI may disappear.** 📌 The tally in `CLAUDE.md` is blunt — across
batches 94–101 the **~8 000 regression tests caught nothing the user's own eyes did not**.

Launch the dual-window editor:

```powershell
$env:STRIDE_HOST_REAL_EDITOR=1
$env:STRIDE_EDITOR_WINDOW=1
# launch HrotStrideApp.Windows
```

| # | check | which slice it covers |
|---|---|---|
| **①** | the 2-D editor window and the 3-D Stride window both open; **every panel that was there is still there** | S2a/S2b |
| **②** | entities appear in 3-D and **move** when the sim runs | S2b/S2c |
| **③** | ⭐⭐ **select an entity and DRAG it fast** — the cyan selection box must **track without a trailing ghost** | 🔴 **S3** *(this is the exact defect `StrideViewBracketOrderTests` encodes)* |
| **④** | ⭐⭐ **pause, then watch a dynamic body for ~10 s** — it must **not** sink, drift or fall | 🔴 **S2c**, and the check `pausedFreeze` is too weak to make |
| **⑤** | issue a move order; the vehicle drives; animation plays | S1/S3 |
| **⑥** | step frame-by-frame — one step must advance the world **once**, not by however long you waited | S2c |

⭐ **Report each with a verdict and, where it helps, a screenshot.** ⛔ **A "looks fine" with no
enumeration is not a verification.**

### 2.5 Close the verification out

⭐⭐ **Write the results back into the DESIGN and the TRACKER**, not only into chat:

- 📄 [`DESIGN_Stride_Node_Modes.md`](../DESIGN_Stride_Node_Modes.md) — §13's slice table: turn each
  *"compiled here"* into *"verified on Windows `<date>`"* **with the evidence**, or record what failed.
- 📄 [`Blueprint_Issues_Tracker.md`](Blueprint_Issues_Tracker.md) — `CE-205` / `CE-206` / `CE-208` /
  `CE-211` / `CE-219` each get a **`VERIFIED ON WINDOWS`** line. ⭐ The `CE-146` row is the format to
  copy — it names the build result, the suite comparison and the self-test verdict.
- ⛔ **Anything that failed becomes a new tracker row with a measured cause**, not a note.

---

## 3. THEN BUILD — **`S4` / `CE-207`: mode 2**

> 📄 The design is [`DESIGN_Stride_Node_Modes.md`](../DESIGN_Stride_Node_Modes.md) §4, §7.3a/§7.3b, §11.1,
> §13 `S4`. ⛔ **Read it before designing anything** — and ⚠ **§7.3a's table was measurably wrong in four
> places** *(§7.3b records how)*, so **treat the design as evidence, not scripture: measure, then fold the
> correction back.**

### 3.1 What `S4` is

⭐⭐⭐ **`StrideNodeShell` is NOT a second composition root — it is a bracket DRIVER** 🔒 *(user,
`2026-09-07`: "stridenodeshell as bracket")*. `TickFrame(dt)` is a short, readable body:

```
physics bracket pre  →  Kernel.Update()  →  physics bracket post  →  view bracket
```

⛔ Nothing else. `Boot(config)` builds `StrideNodeBootstrapper` from `StrideCapabilities`. Both brackets
already exist *(`StridePhysicsBracket`, and `StrideViewBracket` from `S3`)*.

### 3.2 The four items

| # | item | notes |
|---|---|---|
| **①** | **`StrideNodeShell`** — `Boot` + `TickFrame` | ⭐ `SimHostApp` is the template: construct the bootstrapper, set `ApplicationSystemsRegistrar`, `BootstrapNode(config, role, networkFactory)`, tick `SlaveTranslator`. ⛔ The ctor's four `IEcsModule?` slots are **already gone** *(`S2b`)* — the capability plan replaces them |
| **②** | ⭐⭐ **the launch surface** 🔒 *(user: "cli args … approved")* | CLI args mirroring `ClusterRunner`'s: `--node-id` **defaulting to 700**, `--domain`, `--no-wait`, `--staging`, plus `--debug-port` *(from `CE-214`)*, and **`--mode editor\|node` defaulting to `editor`**. ⛔ **Not env vars** |
| **③** | ⭐⭐⭐ **the clock advance moves into the shell** *(`R-S12`, §11.1 ② **option A**)* | the shell calls the time controller **once** at the top of the frame, hands `GlobalTime.DeltaTime` to the physics bracket, and passes **the same instance** to a **new `Kernel.Update(in GlobalTime)`**. ⛔ **Never the obsolete `Update(float)`** — it *fabricates* a `GlobalTime` *(`ModuleHostKernel.cs:468`)*, which is why it desyncs. ⚠ A rail must assert `FrameNumber` increments **exactly once** per `TickFrame`. ⭐ **This is what retires mode 1's one-frame lag** *(§11.1a's deviation ①)* |
| **④** | **readiness handshake** | a `SubsystemStatusAnnounce` DDS topic already exists *(`Hrot.Network.NED`; `SubsystemStatusAnnounceTests` round-trips it)* — use it rather than inventing discovery |

### 3.3 Acceptance

⭐⭐ **`HrotStrideApp --mode node` joins a cluster beside a CGF node, and entities replicate.** ⇒ the
`--mode all`-equivalent smoke: launch CGF *(via `Hrot.ClusterRunner`)* and the Stride node, confirm
entities spawn on CGF and appear/move in the Stride 3-D window.

⭐⭐⭐ **AND THIS IS WHERE `CE-219` FINALLY GETS ITS REAL TEST:** with a cluster present, **pause it** and
confirm the Stride node's bodies **stop** — the arm mode 1 structurally cannot exercise *(§2.3)*.

📄 **`docs/RUNBOOK_Cluster_Debugging_Over_Http.md`** is the how-to for driving a live cluster: one node
per process over plain HTTP, `/diagnostics/architecture` for per-translator `sentSamples`/`receivedSamples`,
and the traps that each eat an hour — ⚠ **`127.0.0.1` 404s every route** *(the listener binds the
`localhost` hostname)* · **the cluster boots PAUSED** · **the module host SWALLOWS system exceptions**, so
a control plane can throw every frame while the API answers `ok:true`.

### 3.4 After `S4`

| slice | task | note |
|---|---|---|
| **S5** | `CE-215` *(gizmo ingress in 3-D + skip counters)* | remote gizmos visible; skipped shapes **counted, not silent** |
| **S6** | `CE-214` *(the day-1 operator surface)* | 🔒 *(user: "the map on day 1 approved")* — ⭐ **lands WITH `S4`, not after**; it is the full diagnostics bundle, ⛔ not "a 2-D map behind a flag" |
| **S7** | `CE-213` *(dead inspector view-model deleted; class renamed)* | ⭐ no Windows needed |
| **S8** | `CE-216` *(the animation duplicate)* | ⭐ reading only — `.dev/_DONE/anim-ctrl/DD-1` §15–16, decision recorded in the design |
| **S9** | `CE-209` *(retire self-contained mode)* | ⛔ **LAST.** ⚠ `STRIDE_SELFTEST` **forces** hosted mode, so it should survive — **measure, do not assume** |
| **S10** | `CE-212` *(`ImageGenerator` → `Map2D` rename)* | 🔴 **Roslyn, run TWICE and unioned** *(once in-solution, once at `Stride/HrotStrideApp.Game.csproj`)*. ⛔ **Never a text replace** |

---

## 4. KNOWN-OPEN, carried in

| id | what | where |
|---|---|---|
| **`CE-220`** | `EditorCapabilitiesTests` must move to its own assembly — its five tests make `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected` fail in the full suite by keeping `SimHostCoreLogicPack`s alive. ⛔ **Do NOT loosen the ALC assertion**; the `CE-152` precedent is a separate assembly | tracker |
| **`CE-151`** | the world-bootstrap seam — still homeless | tracker |
| ⚠ **`FixedTimeStep` / `MaxSubSteps`** | `CE-219` left these unset. It is a *determinism-of-step-size* concern, and it is **unmeasurable off Windows** — the code only ever **logged** `FixedTimeStep`. ⭐ **This machine can finally measure it** | §11.1a |
| ⚠ **§11.1's option A** | mode 1 shipped **option B** *(one frame of lag)*. `S4` item ③ is where A lands | §11.1a |

---

# ⭐⭐⭐ SESSION STATE — `2026-09-08` — **READ THIS FIRST AFTER A COMPACTION**

> ⭐ **§2's verification is DONE. `CE-221` is fixed and the host boots. The work since has been driven
> by the user's own visual checks, and it found five production defects the ~8 000 tests were green on.**

## S-1. The rulings this session produced — ⛔ they bind

| ruling | |
|---|---|
| ⭐⭐⭐ [`R-143`](RULINGS.md) | 🔒 *"no wall clock enywhere, whole sim driven by sim time ONLY. only use of wallclock us stamping the fdp recording"* — *"not in stirede, not in editor, not in cgf, not in simhost, never where simulation is related"*. ⛔ It RETIRED §7.3b's *"the view tier is free-running so `wallDt` is correct here"* |
| 🔒 **ELM / mode 2** | *"ELM and deferred ownership transfer is in play here and no editor specific shortcuts shall be made; it needs to work also for stride mode 2 where the brain who loads the scenario is on another node"* ⇒ ⛔ **any fix bounded only in the editor is wrong** |
| 🔒 **hill-attack is the gate** | *"you absolutely must check the system using the hill attack close scenario"* and *"the scenario needs to end up with killing both enemy tanks"* |
| ⛔ **MEASURE, do not offer a choice** | 🔒 *"always measure before decideind anything, i thought this is written clearly in calude.md"* — 📌 said after I offered an either/or instead of running the two commands that settled it |

## S-2. What is FIXED and verified on Windows

| id | what | proof |
|---|---|---|
| ✅ `CE-221` | the host **did not boot** — two `[SingleInstance]` systems registered twice. Fixed by hoisting `UnitHierarchySystem` + `EqsResultUpdateSystem` into **cross-role infrastructure capabilities** declared once per plan | `hill-attack-close`: **both hostiles killed** (`1006` t≈21 s, `1007` t≈46 s), 15 waves, 0 overshoot, 0 errors |
| ✅ `CE-225` | `spawn_entity` placed everything at the **origin** and answered `ok:true`. `SimTransform` uses **public fields** and `JsonSerializer` ignores fields unless `IncludeFields` is set ⇒ no payload could ever bind | live: `{"position":{"x":480,"y":450,"z":0.5}}` → reads back `[480,450,0.5]`; a non-binding payload is now **refused** |
| ✅ `CE-227` | ⭐⭐⭐ **the pause gate.** `GameTime.Factor = simDelta / wallDelta` ⇒ `WarpElapsed == simDelta` ⇒ Bullet integrates **sim seconds**; paused ⇒ `Simulate(0)` ⇒ its fixed-step loop never runs | paused 25 s bit-identical; resumed 30 s physics live |
| ✅ `CE-230` | the host tick, loop driver, test harness and view bracket now run on `CurrentSimDeltaSeconds` | paused: UI still renders, API responsive; resumed: physics integrates |

⛔⛔ **`Simulation.DisableSimulation` IS NOT A PAUSE — never use it as one.** It makes Stride's physics game
system `return` **before** `Simulate`, taking body readiness, contacts and events with it. That was
`CE-223`, and it made vehicles unable to move at all while looking like a fix.

⚠⚠ **RETRACTED this session:** I claimed Stride's physics was wall-dt dependent and that another engine
might be needed. **False.** `StepSimulation(FixedTimeStep, 0, FixedTimeStep)` is fixed-step; wall time only
decided how many steps ran, and **zero is a legal answer**.

## S-3. What is OPEN

| id | what |
|---|---|
| ✅ **the terrain floor** | **DONE — `CE-231`.** A 20 km × 20 km visible + collidable slab, top face at `Y=0`, added in `BootEditorSubsystem`. 📐 Verified: vehicles hold `z=0.5`, `simVel.z=0` — **the falling is gone**. ⛔ It did NOT make them move |
| ✅✅ **the X/Y collapse AND velocity-without-translation** | **DONE — `CE-232`, and they were ONE defect.** `MainScene`'s four walls are `StaticPlaneColliderShapeDesc` = **infinite half-spaces** at `X=±20`/`Z=±20`, so every scenario coordinate beyond them was the inside of a solid. Bodies at `x=446` were **426 m deep** and Bullet expelled them at up to **780 m/s**, then held them jammed inside the arena — which is why velocity was non-zero while position never moved. ⭐ Fixed by `NeutralizeInfinitePlaneColliders` in `BootEditorSubsystem`
| ⛔ HISTORY — the terrain floor | ⭐⭐ **the current blocker.** Scenarios sit at world coords ~450–670; the Stride ground is near the origin, so vehicles **fall** the moment physics integrates. 🔒 *(user: "best if we could extend the stride's terrain floor to be way larger so our scenarios can be modelled outside of the current (and extremely small) stride arena")* ⚠ **Their X/Y also collapse from `(446,420)` to `(18,19)`, which free fall does NOT explain** — ✅ **EXPLAINED `2026-09-08` by `CE-232`: the arena's infinite plane walls were ejecting them** |
| ⚠ **vehicle bodies** | ⭐⭐ **THE LIVE HOST NOW DRIVES — measured `2026-09-08` after `CE-232`.** All four tanks advanced from `(446,420)`-ish to their assigned waypoints and ARRIVED: `#1001 → (522,401)` for a destination of `(523,401)`, `#1002 → (525,450)` vs `(526,450)`, `#1003 → (528,498)` vs `(529,499)`. ⛔ **So *"vehicles have never worked in this tree"* is now FALSE for the live host.** ⚠ `FdpMoveOrderIntegrationTests` is still red at `69390758e`, the commit that introduced it, so THAT suite has never passed — ⛔ but it is no longer evidence that the feature is broken
| ⚠ `CE-222` | `repos`/`pausedFreeze` still red. ⛔ **The physics-gate lead is REFUTED** (still red after `CE-223`). 🔴 **And `drive=PASS` IS VACUOUS** — `endDrive` is the START point; it passes on the residual `B→A` offset of 13.34 m. ⇒ one defect corrupts **three of four** checks; only `initialHold` is independent |
| ⚠ `CE-226` | generated registrars still lack `ParamsDtoType` (34/40 empty) — the other session's lane |
| 🔴🔴 **THE NEXT THING — the scenario stops at the baseline** | ⭐⭐ **`hill-attack-close` no longer reaches "both enemy tanks killed", and `CE-221`'s claim that it DID is now suspect** — that kill happened in the collapsed world, with all 8 entities piled within ~2 m at the origin, so it proved the weapon chain at point-blank range and nothing about the approach. 📐 **Two independent causes, both measured, neither physics:** ① the platoon's `MissionPlanQueue` reads **`CurrentPhase: 1, PhaseCount: 1`** while `PlatoonHillAttack` carries **both** a `baselineStart/End` and a `firingLineStart/End` — only the baseline runs, the firing-line advance never happens *(task still `state: "TASK_PLANNED"`)*. ② at the baseline the tanks are **~143 m** from the hostiles and `PerceptionReceptor.VisionRange` is **`100`** ⇒ `SensorContactList.Count = 0`, `WeaponChannel.Status = "Failure"`, 42 rounds unfired. ⭐ **Searched `docs/`+`.dev/` for an owning design for the firing-line phase — none found.** ⛔ Behaviour/mission lane, not Stride |
| ⚠ **the navmesh still only covers the arena** | the bake reports `polys=49 (Vehicle) / 108 (Infantry)` from *"floor from arena colliders"* — ±20 m, nothing where scenarios live. ⭐ Vehicles are unaffected **today** because `VehicleNavigationIntentSystem` logs *"navmesh bypassed"* and steers direct — ⛔ **navmesh-driven infantry outside the arena cannot work until the bake covers scenario extents.** ⚠ The `CE-231` slab does not help: it sets `ColliderShape` directly, so it carries no `ColliderShapes` **description** and `StrideSceneGeometrySource` cannot see it |
| ⛔ **ANIMATION** | ⭐ **still never tested.** No assertion has been made about it in this session |

## S-4. How to drive the host — ⭐ the tool that found most of the above

⭐⭐ **The debug API needs ZERO code on Stride** — `EditorSubsystem` already builds a `DebugApiHost` behind
`HROT_DEBUG_API_PORT`, and the Stride host hosts a real `EditorSubsystem`. It was under-adopted, not missing.

```
set HROT_DEBUG_API_PORT=8131, STRIDE_HOST_REAL_EDITOR=1, STRIDE_EDITOR_WINDOW=1
run Stride\Bin\Windows\Debug\win-x64\HrotStrideApp.Windows.exe

B=http://localhost:8131          # localhost, NEVER 127.0.0.1 (it 404s every route)
POST $B/scenario/load/live       {"name":"hill-attack-close","waitForReady":true}
POST $B/sim/play                 {}
GET  $B/entities/1001            # READ THE ENTITY. Do not theorise from logs
GET  $B/capabilities             # every route with its tool name — do not guess paths
```

⚠ **The host boots PAUSED** — nothing moves until `/sim/play`, and that is not a defect. 📌 A whole
diagnostic loop went into *"nothing moves"* that was simply a paused world.

## S-5. ⛔ THE PROCESS LESSONS THIS SESSION PAID FOR

| | |
|---|---|
| ⛔⛔ **grep cannot settle a NEGATIVE claim** | 📌 I claimed *"`CgfCuratedBehaviorRegistrar.Register` has zero callers"*. It is `[BlueprintRegistrar]`-attributed and invoked **reflectively** — invisible to grep **by construction**. The other session corrected me. ⭐ codebase-memory MCP was down and **the CLI fallback exists**; I did not use it |
| ⛔⛔ **a green rail is not a working feature** | 📌 **three times**: `StridePhysicsBracketPauseGateTests` passes against its own fake · `DiscoveryAndHintTests` asserts the schema is *"an object"* and its own comment says *"possibly empty"* · `drive=PASS` measures displacement from a point the entity never reached |
| ⛔ **verify against the RIGHT baseline** | 📌 three sessions called the vehicle reds *"pre-existing"* against three different bases. That only ever proved *"not the last batch"*. The answer needed the commit that INTRODUCED the test |
| ⛔⛔ **NEVER RUN TWO SUITES AT ONCE — contention manufactures a PHANTOM REGRESSION** | 📌 `2026-09-08`: comparing `CE-232` against its base I ran both suites **and** the live Stride host concurrently, and got **5** reds against the baseline's 4 — the extra one in `EditorStrideSubsystemHostedModeTests`, green at the baseline. ⭐ **The tell: its IDENTITY ROTATED between runs** *(`HostedMode_BrainPathSpawn…` then `HostedMode_Initialize_AndTickThreeFrames…`)* and it passed **3/3 in isolation**. 📐 Run **sequentially with the host stopped**, the answer is stable: **4/243/247 three times, exactly the baseline's four.** ⚠ These tests boot a real subsystem and tick frames, so they are the first to fall over under load — ⛔ and the machine then killed every background task for low memory |
| ⛔ **do not present an inference as a measurement** | 📌 I said `DisableSimulation` blocks body creation. It was inferred from effect; the truth was a **latch of my own** in the wrapper's pre-`Inner` branch. The third configuration — gate inert, latch present — is what exposed it |
