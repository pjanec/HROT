<!--STATUS
state: LIVE
updated: 2026-08-20
current-answer: this whole file — the Batch 101 return.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# REPORT — Batch 101: **the suite nobody runs, and why it still cannot be run**

> 📌 **Dispatched at `6106f7047`** · scope frozen there · base for every RED: **`6106f7047`**.
> ⭐ **Started-marker pushed first** *(rule 1b)*: `e7cd060`.
> ⭐ **Ids allocated by me** *(rule 3)*: **`BP-378`** · **`BP-379`** · **`BP-380`**.

---

## 1. ⭐⭐⭐ THE FOUR VERDICTS *(`R-106`)*

| item | verdict | |
|---|---|---|
| **`101a`** — the scalar row | ⛔ **NOT STARTED — withdrawn by the coordinator before I began.** ⭐ **Verified, not rebuilt**: 8/8 green, and I rendered it independently — §2 |
| **`101b`** — gate the integration suite | ✅ **DONE** | ⚠⚠ **and the finding is that it CANNOT be gated** — §3 |
| **`101c`** — triage the reds | ✅ **DONE** | ⭐ **direction established with evidence** for 8 of 11; ⛔ **3 left explicitly untriaged** — §4, §5 |
| gates · tracker · report | ✅ **DONE** | §6–§7 |

⛔ **Nothing blocked.** ⚠ `101a` is *not started* rather than *done* because it was not mine to do — ⭐ the honest verdict, and the handoff's own instruction.

---

## 2. ⭐ `101a` — **verified, not taken on trust**

⭐ The coordinator's work landed and is green: **8/8** under Xvfb *(7 pure + 1 frame)*.
⭐⭐ **And I rendered it independently** rather than reading the coordinator's own before/after image:

📄 **[`img/b101-scalar-row-real-modal.png`](img/b101-scalar-row-real-modal.png)** — **one row, labelled
`Count`, value `11`**, drawn by the real `ComponentEditDrawer` over a real StructEdit session through
the production `VariableEditModal.ScalarRowOrRoot`. ⛔ Not a mock-up, ⛔ not a replica.

⚠ The screenshot hook was **temporary and is reverted** — the rail file is byte-identical to the
coordinator's.

---

## 3. ⛔⛔⛔ `101b` — **THE SUITE CANNOT BE GATED, AND THAT IS THE FINDING** *(`BP-378`)*

📌 The handoff said *"expect it red — that is the point."* ⚠⚠ **"Red" understates it: it does not
finish.**

### 📐 Three full unfiltered runs, same commit, same machine

| run | configuration | reached | outcome |
|---|---|---|---|
| ① | default *(parallel)* | **89 / 174** | ⛔ **ABORTED** — `Test host process crashed : CycloneDDS … dds_take failed: -3 (BadParameter)` |
| ② | `parallelizeTestCollections=false` | **75 / 174** | ⛔ **ABORTED** — same DDS crash |
| ③ | default *(parallel)* | **117 / 174** | ⛔ **ABORTED** — `[ModuleHost][TIMEOUT] Module 'CognitiveSpatial' timed out after 100ms` |

⭐⭐⭐ **Three runs, three different truncation points, two different abort causes.** 📌 That is the
`BP-337` / `DEBT-AIB-030` signature verbatim — **neither a red nor a green from the whole suite is
evidence** — and it is why this suite has sat outside every gate table for ~40 batches: ⛔ **it could
not have been added, only ignored.**

### 📐 And underneath both aborts: **59–118 `OutOfMemoryException`s**

At `Fdp.Core.EntityIndex..ctor` → `EntityRepository..ctor` → one per harness.
📐 `FdpConfig.MAX_ENTITIES = 1_000_000`, and each harness builds a full repository.
⚠⚠ **Serialising made it WORSE (75 < 89)** ⇒ ⛔ **parallelism is not the cause** — memory is not
released between tests either way. ⭐ The box has **16 GB and no cgroup cap**, so this is accumulation,
⛔ not a small container.

### ⭐ What CAN be gated today

Individual classes in isolation run **clean and fast** — `BlueprintKernelRunTests` 5/5 in **918 ms**.
⇒ ⭐ the near-term shape is **per-class or per-chunk runs with a fresh host**, ⛔ not one process.

⛔ **NOT FIXED HERE**, per the handoff.

---

## 4. ⭐⭐⭐ `101c` — **THE DIRECTION, ESTABLISHED** *(`BP-379`)*

📌 The question: *"is the **counter** wrong, or the **expectation**?"*
⛔ Forbidden: *"changing `Expected: 10` to `9`."*

### 📐 THE MEASUREMENT — a temporary probe, pumping one frame at a time

```
after attach, before any pump: Count=0
after pump #1: Count=0   dt=0       frame=0     ← FROZEN
after pump #2: Count=1   dt=0.005   frame=1
after pump #3: Count=2   dt=0.005   frame=2
```

⭐ `BlueprintTickSystem:51` is **`if (deltaTime <= 0f) return;`** — **deliberate**, and that file
documents it as how FREEZE works *(«Frozen comes free»)*. ⇒ the first pumped frame is a frozen frame.

### ⭐⭐⭐ THE DECISIVE CONTROL — a second entity, attached into a WARM world

```
LATE entity, after attach: Count=0
LATE after pump #1: Count=1     ← NOTHING LOST
LATE after pump #2: Count=2
```

⛔ **So it is NOT "attach costs a frame"** — which is the reading that would have argued the expectation
was wrong. ⭐ **Attach is immediate.** It is the harness's **very first `Kernel.Update()`** that arrives
with `dt = 0`.

### ⭐⭐ THE ANSWER

| | |
|---|---|
| ⭐ **the counter** | **RIGHT** — it increments exactly once per non-frozen frame |
| ⭐ **the sim** | **RIGHT** — skipping `dt <= 0` is the freeze mechanism, not a bug |
| ⛔ **the expectation** | **wrong about the HARNESS, not about the blueprint**: `PumpFrames(N)` delivers **`N−1` live frames** from a cold kernel and **`N`** once warm |
| ⭐⭐⭐ **where the repair belongs** | **`EditorHarness` — its first pump must deliver a real `dt`.** ⛔ **NOT the expectations**: editing `Expected: 10` → `9` would bake a startup artefact into the contract for ever, in **eight** places |

⭐ **Scope: 8 of the 11 real assertion failures, across TWO classes** —
`BlueprintKernelRunTests` *(5)* and `BlueprintObserveTests.CaptureLiveState_AfterNFrames_CountEqualsN`
*(3)*. ⛔ A per-test "fix" would have been applied eight times.

⚠ **NOT fully traced, and I am saying so rather than rounding up:** *why* `MasterSyncController.Update()`
yields `dt = 0` on its first call, although `PumpFrames` calls `Step(0.005f)` **first**. 📐 The
controller **is** the kernel's own instance *(`EditorHarness:157 Kernel.SetTimeController(_timeController)`)*,
so the last hop is inside that controller — ⭐ **that is where a fixer starts.**

---

## 5. ⚠ THE OTHER THREE REDS — **named, and explicitly NOT triaged** *(`BP-380`)*

| test | message |
|---|---|
| `HsmBehaviorIntegrationTests.E1_CognitiveRuntimeModule_RegistersExactlySixSystemsInOrder` | **`Expected: 6, Actual: 7`** |
| `BlueprintScenarioIntegrationTests.Test5b_BackwardCompat_MixedOldAndNewKeys_OnlyAssignmentsApplied` | `Assert.True() Failure` |
| `BlueprintObserveTests.CaptureLiveState_WithoutDebugMap_ReturnsSnapshotWithEmptyFields` | `Assert.Empty() Failure: Collection was not empty` |

⭐ The first **smells like a genuinely drifted expectation** *(a seventh system added, the count never
updated)* — ⚠⚠ **but "smells like" is not a measurement, and I did not run it down.** 📌 `R-106`'s
acceptable outcome: *"could not establish the direction in this batch, here is what I ruled out."*

⭐ **What IS ruled out:** they are **not** `BP-379` *(different shapes, different classes)* and **not**
the OOM cascade *(they fail with real assertions in an isolated, clean run)*.

---

## 6. ⭐⭐ GATES

⭐ Base for every RED: **`6106f7047`**. Unfiltered unless a row says otherwise.
⚠⚠ **ENVIRONMENT STATED, per the handoff's portability note.**

| gate | `--no-build`? | result | Δ baseline |
|---|---|---|---|
| solution build | — | **0 errors** | — |
| **AiShared** *(Xvfb)* | ✅ | **1714 / 0 / 0** | **+8** *(1706)* — the `101a` rails |
| **Blueprints** *(Xvfb)* | ✅ | **3870 / 0 / 10 skip** | **0** |
| **Blueprints** *(NO display)* | ✅ | **3862 / 0 / 18 skip** | ⭐ the portability figure — **same tree, both true** |
| **BTree.Editor** | ✅ | **622 / 0 / 0** | **0** |
| **Hsm.Editor** | ✅ | **554 / 0 / 0** | **0** |
| **Hrot.Editor** | ✅ | **201 / 0 / 0** | **0** |
| **Breakpoints** | ✅ | **143 / 0 / 0** | **0** |
| **Generators** | ✅ | **277 / 0 / 0** | **0** |
| **Persistence** | ✅ | **143 / 0 / 0** | **0** |
| ⛔ **NodeEditor.Core** | ⛔ **NO** *(out of solution)* | **211 / 0 / 0** | **0** |
| ⛔ **NodeEditor.UI** | ⛔ **NO** | **135 / 0 / 0** | **0** |
| ⛔ **Fhsm** | ⛔ **NO** | **300 / 0 / 0** | **0** |
| ⛔ **StructEdit** | ⛔ **NO** | ⚠ **191 / 1 / 0** | **0** — `BP-363`, pre-existing |
| **Fdp.Presentation** | ✅ | **146 / 0 / 0** *(`BP-337` filter)* | **0** |
| **Fdp.Toolkits** | ✅ | **1964 / 0 / 0** | ⚠ `DEBT-AIB-030` — green this run, ⛔ not a clearance |
| ⭐⭐⭐ **`Hrot.ClusterRunner.Integration.Tests`** | ✅ | ⛔⛔ **CANNOT BE GATED — see below** | **first appearance** |
| `tracker-counts.py --check` | — | **OK — open 80 / done 235 (+1 refuted)** | open **+3** |
| `rulings-check.py` | — | **92 / 92 verified** | **0** ⚠ 1 staleness WARN *(`Hrot.ClusterRunner/Program.cs` changed after the ledger)* |
| `design-digest.py --check` | — | **OK** | — |

### ⭐⭐⭐ THE INTEGRATION SUITE'S GATE ROW — **the honest form**

| run | reached | verdict |
|---|---|---|
| ① parallel | **89 / 174** *(35 pass · 49 fail · 5 skip)* | ⛔ ABORTED — DDS native crash |
| ② serial | **75 / 174** *(28 · 42 · 5)* | ⛔ ABORTED — DDS native crash |
| ③ parallel | **117 / 174** *(31 · 81 · 5)* | ⛔ ABORTED — ModuleHost timeout |

⛔ **There is no single number to put in a gate table**, and inventing one would be the exact
manufactured confidence `R-124`'s batch was written against.
⭐ Of the failures that ARE real assertions: **11**, of which **8 are `BP-379`** and **3 are `BP-380`**.
⭐ The rest (~50, overwhelmingly `Eqs*`) are the **OOM cascade**.

### ⭐⭐ FRAME-RAIL COUNTS: **RAN / SKIPPED**

| environment | ran | skipped |
|---|---|---|
| ⭐ **under `xvfb-run`** | **9** *(8 Blueprints + 1 AiShared scalar)* | **0** |
| ⚠ `DISPLAY` unset | **0** | **9** — each printing *"no DISPLAY — run under `xvfb-run …`"* |

⭐ The 8 remaining AiShared scalar rails are **pure** and pass in both environments.

### ⭐ Golden movement · tree · quarantine

⛔ **ZERO golden files, ZERO asset `.json`, ZERO production source changed by me.**
⭐ **One new PNG** *(evidence)* + tracker + report. ⭐ Working tree **clean** after every suite run.
⭐ Quarantine: Blueprints **10 → 10** *(with a display)*; ⛔ no new skip.

---

## 7. ⭐ PROBES

⚠ **This batch changed no production code, so there is nothing to revert-probe** — ⛔ and inventing a
probe for an item I did not build would be theatre. 📌 `101a`'s probe was run by the coordinator
*(inverting the wrapper guard reddens 2 of 8)*; ⭐ I re-ran the rails themselves, green.

⭐ **The diagnostic probe for `101c` was temporary and is REMOVED** — `git status` shows only the new
PNG. ⛔ It was never committed.

---

## 8. ⭐ WHAT WAS **NOT** DONE — deliberately

⛔ No fix to the integration suite's reds *(`101c` is triage)* · no adjusted expectation · no change to
`StructEdit.Core` or `ComponentEditDrawer` · nothing from `DESIGN_Details_Panel_View_Switching.md` · no
revert of Batches 94–100 · ⛔ **and no `101a`**, which was withdrawn.

---

## 9. ⚠⚠ RULE 4 — **and a merge note the coordinator needs**

⭐ The coordinator branch was re-pulled before the final commit. ⭐ My rule-7 sync took **`8d66ba5`**,
which already included `51f743e` *(the `101a` fix)* and `caf9a38` *(the remote-desktop input fix)*.

⛔⛔ **THREE MORE COMMITS LANDED AFTER THAT SYNC**, while this batch was running:

| commit | |
|---|---|
| `8679a92` | `fix(edit): the dialog opens over what the row is showing, not the declaration default` |
| `aa33132` | `fix(watch): a double-click raises the gesture the host OFFERS` |
| `bc4a2c6` | `ledger: M-36 — the Instance live-write refusal is an unbuilt capability` |

⭐⭐ **I did NOT merge them, deliberately, and the reason is the gate table.** Every number in §6 was
measured against **`8d66ba5` + this batch's doc-only changes**. ⛔ Folding new production code in at the
last minute would have invalidated all of it, and reporting the old numbers over a new tree is exactly
the manufactured confidence this programme keeps filing. ⚠ **My scope was frozen at the dispatch sha;
these are FYI.**

⭐⭐⭐ **THE MERGE NOTE:** `aa33132` touches **`VariableTableControl.cs`**, which **Batch 100 (`100f`)
also changed** — it is the file where the `Gestures.OffersProperties` guard lives, and the commit
subject says it builds directly on that guard. ⇒ ⚠ **expect a merge to reconcile, and re-run the
`100f` rails after it** *(`TheWatchIsWiredLikeADetailsHostTests`, 9 tests)*. ⛔ Nothing in those three
commits invalidates a Batch 101 item.
