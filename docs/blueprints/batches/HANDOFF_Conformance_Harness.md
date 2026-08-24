<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-24
current-answer: dispatch pointer for the cross-host conformance harness — steps 6+7 of
  DESIGN_Headless_Testability.md. Lift the read+drive API to the ClusterRunner host so --mode all answers
  MCP, add ack-gated deterministic cluster-wide stepping, build the editor-vs-(--mode all) conformance
  suite, and RE-PROVE the part-C editor goldens under the same stepping seam. ⛔ Carries no design: every
  item cites a section.
known-conflict: none in the harness lane. ⛔ CROSS-LANE BOUNDARY: the LookaheadWallTicks/tickSource seam in
  --mode all lives in OrchestratorSubsystem.cs + Fdp.Toolkits/Time (TIME lane, Area H) and is OUT of this
  batch — correctness comes from the ack-gate, not from zeroing the barrier (§6c).
-->
# HANDOFF — **the cross-host conformance harness** *(steps 6+7)*

> 📌 **Dispatched at `878cf022d`.** ⛔ **Scope FROZEN at that sha.** ⭐ Branch fresh from
> **`claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: push the started-marker BEFORE any code.**
> ⛔ **No PR.** ⭐ ids **`HN-`**/**`MX-`**, tracker **Area J** — 📐 the Area-J series stands at **`HN-025`** *(net)* /
> **`HN-122`** *(MCP-harness)* / **`MX-014`**. ⭐ **Rule 3: you allocate the ids; state them (rule 5).**

> 🔒 **User, `2026-08-24`, verbatim:** *"do not forget also about the goldens based tests (editor showing same
> stuff as it did before). both need to be proven to work. also lets make sure we are using the deterministic
> stepping for those tests, should tick the simulation cluster wide no matter if editor or distributed mode."*
> ⇒ ⭐⭐⭐ **THREE things must be true when this batch closes: (a) `--mode all` answers MCP and its panels/gizmos
> match the editor's; (b) the part-C editor goldens still pass; (c) BOTH are driven by the SAME deterministic
> cluster-wide step — never wall-clock free-running.**

## 0. ⛔⛔ THE DESIGN IS THE SOURCE — this file is a POINTER

📄 **[`DESIGN_Headless_Testability.md`](../../DESIGN_Headless_Testability.md)** — §**"Step 6"** *(6a the dependency
split + the four wiring points, 6b the one step seam, 6c the two determinism hazards, 6d the goldens must be
re-proven)* and §**"Cross-host conformance"** *(the two-mode diff)*. ⭐ Both are `READY-TO-BUILD` and carry the
`classDiagram` + `sequenceDiagram` you must check against *(obligation ③)*.
📄 The measured seam facts are in that doc's `INVENTORY` — ⛔ **do not re-derive them; confirm and cite.**
⭐ Report per obligation ③; ⭐⭐ **fold deviations back into the design** *(obligation ⑤)* — the design updates,
not just the report.

## 1. ⭐⭐⭐ THE ITEMS

| # | task | design | the one thing not to get wrong |
|---|---|---|---|
| 🔴🔴 **①** | ⭐⭐⭐ **Extract `IReadDriveApi`** — the dependency-free read+drive surface *(LoadScenario · SwitchPerspective · Step · GetPanel · GetGizmoFrame · GetSimState)*, made the editor `DebugApiService` implement it, and a new **`ClusterReadDriveService`** implementing it with **no editor-only deps** *(no `IPreviewController`/`IEditorLogic`/AI sessions)* | §6a | ⛔ `DebugApiService` **cannot be constructed in `--mode all`** *(9 throw-guarded editor deps)* — that is WHY this is a split, not a move. ⭐ `GetPanel` is a pure static `PanelSnapshot` read; `GetGizmoFrame` reads the injected `DebugPrimitiveBuffer`. ⭐⭐ **One definition of "what a panel shows", two hosts** — ⛔ do not fork the panel-dump logic |
| 🔴🔴 **②** | ⭐⭐⭐ **Lift the read+drive API to the `ClusterRunner` host** — the **four wiring points**, attaching the `ClusterReadDriveService` | §6a table | ⛔⛔ **all four, or a `RunMain` route hangs:** `PanelSnapshot.CaptureEnabled=true` · construct+`AttachService`+`Start` · per-frame `MainThreadJobQueue.DrainAll()` · per-frame `PanelSnapshot.ClearCaptured()` **after** the drain *(order — `HN-007`)*. ⭐ Gate it on the same `HROT_DEBUG_API_PORT` signal the editor uses |
| 🔴🔴 **③** | ⭐⭐⭐ **Ack-gated cluster-wide `Step()`** — `POST /sim/step` returns **only when the tick is acknowledged cluster-wide** *(`MasterSyncController.IsAwaitingStepAcks == false`)*, SAME return contract in editor *(empty roster, returns immediately)* and `--mode all` *(SimHost·IG·CGF ACK via `FrameStepCompletedEvent`)* | §6b, §6c | 🔴🔴 **THIS IS THE CORRECTNESS ONE.** ⛔ **No `Thread.Sleep`/fixed `Settle` as the sync** — a read between `Step()` and the last ACK captures a HALF-STEPPED cluster. ⭐ Reuse the `StepTimeIntent` seam *(do not invent a stepper)*. ⚠ `GET /sim/state` lacks `awaitingStepAcks` today — either gate INSIDE `Step()` *(preferred)* or add the field + poll, ⛔ not both |
| ⭐⭐ **④** | ⭐⭐⭐ **The conformance suite** — `ClusterRunnerFixture(mode)`, run scenario `S` in **editor** and in **`--mode all`**, switch to the perspective that shows `PanelKind K`, deterministic-step, dump `K` **and the gizmo frame**, **diff by `PanelKind`** | §Conformance, §6 | ⭐⭐ **No golden — the reference IS the other mode's live dump.** ⛔ perspective SETS are disjoint *(editor `{Editor,BTree,HSM,Blueprint}` vs `{IG,SimHost,ExCon,CGF,StrideMock}`)* ⇒ **discover per-mode, never assume a shared name** *(open Q1)*. ⭐ Start from the **shared-presentation panels both hosts draw** *(`Hrot.Presentation/Panels`, open Q1)*. ⭐ tolerance: exact by default, documented ignores only *(open Q3)* |
| 🔴🔴 **⑤** | ⭐⭐⭐ **PROVE the conformance diff FAILS** — inject a divergence *(make one mode's panel field differ)*, confirm the diff reddens naming the JSON path, revert | §Conformance · §8-style | ⛔⛔ **Report it as `N4`'s mutation table did** — a diff that has never been seen to go red is decoration. ⭐ **Rebuild before concluding** *(the stale-binary trap that nearly inverted `ST-019`)* |
| 🔴 **⑥** | ⭐⭐⭐ **RE-PROVE the part-C editor goldens** — run `PanelGoldenRails` / `GoldenCaptureFixture` on this tree, report GREEN, and state *(obligation ③)* that their stepping is the same deterministic seam `--mode all` uses | §6d | ⛔ *"both need to be proven to work"* — the goldens are the **editor half of the parity claim**. ⛔ **Do not re-bless them** to go green; a red is a finding. ⭐ They already drive via `SwitchPerspectiveAndSettleAsync` → `POST /sim/step` *(the §6b seam)* — confirm that, don't rewrite it |
| ⭐ **⑦** | ⭐⭐ **A lockstep rail** — after a cluster-wide step, assert all sim nodes agree on sim time *(the invariant `--mode all` stepping must hold)* | §6b | ⭐ reuse the shape of `SimTimeSyncIntegrationTests.AssertAllInSync` / `TestHook_CurrentSimTime` — ⛔ do not copy the `Thread.Sleep` sync; gate on ACKs *(item ③)* |

## 2. ⚠ WHAT WILL BITE

| ⚠ | |
|---|---|
| ⭐⭐⭐ **the step is asynchronous under the hood** | `POST /sim/step` publishes `StepTimeIntent`; the tick completes over several frames as slaves ACK. ⛔ Returning before `!IsAwaitingStepAcks` is the half-stepped-read bug *(item ③)* |
| ⚠ **the 200 ms enter-deterministic barrier** | `LookaheadWallTicks` *(`TimeConfig.cs:75`)* is crossed against the real clock ⇒ pump-until-paused once when entering deterministic mode. ⭐ **This is LATENCY, not non-determinism** — sim is frozen during the barrier *(§6c)*. ⛔ **Zeroing it edits TIME-lane files — OUT of scope, see §3** |
| ⚠ **`ExCon` never ACKs steps** | the roster is **SimHost·IG·CGF** *(`OrchestratorSubsystem.cs:303-317`)* — ⛔ do not expect an ExCon ACK or the gate never clears |
| ⚠ **capture is perspective-scoped** | a panel registers to `PanelSnapshot` only when its perspective draws ⇒ switch, **step**, then read *(§capture protocol, `HN-007`)*. ⛔ a same-frame read returns the empty prefix |
| ⚠ **authoring perspectives capture EMPTY** | no debug route opens an AI asset *(`MX-013`)* ⇒ conformance over BTree/HSM/Blueprint panels compares skeletons. ⭐ Say so; it is not coverage of authoring |

## 3. ⛔ LANE & SCOPE

⭐ **Yours** *(harness lane, Area J)*: `Hrot.SystemTests` *(the `ClusterRunnerFixture`, the conformance rails,
the lockstep rail, the mutation proof)* · the new **`IReadDriveApi`** + **`ClusterReadDriveService`** · the
**read+drive API wiring at the `ClusterRunner` host** · the **ack-gate inside `Step()`** *(and, if you must, a
new `awaitingStepAcks` read-only field on the debug API — additive)*.

⛔⛔ **NOT yours — CROSS-LANE, STOP-and-report if you think you need them:**
- ⛔ **`OrchestratorSubsystem.cs` / `Fdp.Toolkits/Time/**` production files** *(TIME lane, Area H, `TM-`)* — the
  `LookaheadWallTicks=0`/`tickSource` seam. ⭐ **You do not need it for correctness** *(the ack-gate carries
  determinism)*; if the 200 ms latency hurts, **report it** and it becomes a small TIME-lane follow-up.
- ⛔ the variable/blackboard/`AiShared` panels *(UI-lane freeze)* · anything CGF-side beyond reading its panels.

⚠ **A parallel batch may run** — ⭐ **Rule 4: pull the coordinator branch before your final commit** and read any
design/handoff that changed.

## 4. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · **delta vs `878cf022d`** ·
a `--no-build` column · every RED confirmed pre-existing **by name** · `tracker-counts.py --check` ·
`rulings-check.py` · `design-digest.py --check` · **the ids you allocated** *(rule 5, same commit)*.

⭐⭐ **Row 8 — this batch IS an integration gate** *(it stands up `--mode all`)*: report
`bash scripts/run-system-tests.sh` *(baseline `58 / 58` + your new conformance/lockstep cases)*, **item ⑤'s
mutation table**, and **item ⑥'s `PanelGoldenRails` result**. 📐 **`--mode all` boots five subsystems over DDS** —
⛔ if the DDS-allocator crash *(`ClusterRunner.Integration.Tests`)* makes a suite un-gateable, that is a
**reported finding with the base-sha proof**, not a silent skip *(rule 8 row 8)*.

⚠ **Known baseline quirks — not yours:** `tracker-counts.py --check` is blind to `HN-`/`MX-` rows ·
`Fdp.Presentation.Tests` ~18–20 pre-existing *(`BP-419`)* · `mermaid-check.mjs` needs `npm install` *(say if
skipped)* · 2 known `rulings-check` staleness WARNs.

## 5. ⭐ WHEN YOU ARE DONE

⭐⭐ **Fold the as-built into [`DESIGN_Headless_Testability.md`](../../DESIGN_Headless_Testability.md)** — §"Step 6"
made true *(the actual `IReadDriveApi` members, the `ClusterReadDriveService` deps, the ack-gate's final shape)*,
§Conformance's coverage answered *(open Q1/Q3)*, and the sequencing table's steps 6+7 marked **BUILT**. ⛔ Design
content in the design; the report points at it. ⭐ Mark the ids closed in the tracker.
