<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-23
current-answer: dispatch pointer for the two runner loose ends — the Stride composition root's obsolete
  desyncing tick path (ST-018 re-graded), and an automated "every mode starts and ticks" rail to replace
  the manual gate row that was asked for, not delivered, and would have caught the --mode all crash.
  ⛔ Carries no design: see DESIGN_Stride_Port.md §7–§8.
known-conflict: none. Runs in parallel with the regression-net batch; no shared files (§3).
-->
# HANDOFF — **the runner's tick path and its mode rails**

> 📌 **Dispatched at `5963fffd4`.** ⛔ **Scope FROZEN at that sha.** ⭐ Branch fresh from
> **`claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: started-marker BEFORE any code.**
> ⛔ **No PR.** ⭐ ids **`ST-`**, tracker **Area I** — ⛔ never `HN-`/`MX-` *(the regression-net batch runs in
> parallel and owns those)*, never `BP-`. ⭐ **You allocate the ids** *(rule 3)* — `T1`/`T2` are placeholders.

## 0. ⭐ WHY THIS EXISTS — **two loose ends from the last round, and one is a correctness hazard**

📄 **Design basis: [`DESIGN_Stride_Port.md`](../../DESIGN_Stride_Port.md) §7** *(the as-built)* **and §8**
*(the coordinator's re-grade of `ST-018`)*. ⭐ Read §8 before touching `T1`.

⭐⭐ **Your own last batch filed both of these honestly rather than silently patching or silently keeping
them.** ⛔ That was right. ⭐ This batch closes them.

## 1. ⭐⭐⭐ THE ITEMS

| # | task | design basis | gate |
|---|---|---|---|
| 🔴🔴 **T1** | ⭐⭐⭐ **`StrideNodeBootstrapper.Tick` must stop using the DESYNCING tick path.** 📐 `Context.Kernel.Update(dt)` is `[Obsolete]` with the text *"Use Update() utilizing SteppingTimeController instead. **This legacy overload will cause deterministic desync**"* *(`ModuleHostKernel.cs:464-465`)*, and it is suppressed by a `#pragma warning disable CS0618` whose justification — *"a mock/test-only harness, not a live DDS-connected node"* — **died with the mock**. ⇒ ⭐ **move to the `SteppingTimeController` path and delete the `#pragma`.** ⚠ **If the stepping path needs state this bootstrapper does not have, STOP and report what is missing** — ⛔ do not invent a time source | **§8** · charter **D6** *(determinism is the net's foundation)* | ⭐ **the `#pragma` is GONE** *(that is the observable outcome — a suppression is the defect's fingerprint)*, and the time/integration suites are green: `Hrot.ClusterRunner.Integration.Tests ~TimeControlIntegrationTests` · `Fdp.ModuleHost.Tests` |
| ⭐⭐ **T2** | ⭐⭐⭐ **An AUTOMATED rail: every mode STARTS and TICKS.** ⛔ Today this is a manual gate row, and 📌 **the last batch was asked for it and did not deliver it — and the user then hit `--mode all` dying on frame one** *(a strict-mode violation: `ClusterSlave` published `NodeHeartbeatEvent` on a bus nobody had registered it on)*. ⇒ ⭐ **a rail per mode — `editor`, `all`, `simhost`, `cgf`, `ig`, `excon`, `orchestrator`, `replaybrowser` — that boots the real runner and ticks at least a few frames.** ⭐ **Reuse the harness's process fixture** *(`Hrot.SystemTests/EditorProcessFixture` already boots the real binary headless under Xvfb — ⛔ do not write a second launcher)* | 📌 the `--mode all` crash, `0defc1074` | ⭐⭐ **Prove it can FAIL: revert `0defc1074` in a scratch commit, confirm the `--mode all` rail reddens, restore.** ⛔ **A rail that has never failed is not evidence** — report the result |

⛔⛔ **`T1` before `T2`?** ⭐ **No — `T2` first.** 📌 A rail that catches a start-up crash is worth having
*before* you change a tick path, so that if `T1` breaks composition you find out from the rail rather than
from the next visual check.

## 2. ⚠ WHAT WILL BITE — measured, so you do not re-derive it

| ⚠ | |
|---|---|
| ⛔⛔ **You cannot build or run the `Stride/*` tree here** | `net8.0-windows`; the test host needs `Microsoft.WindowsDesktop.App`, which has no linux-x64 build; `HrotStrideApp.Windows` exits **150** *(`ST-006`, pre-existing)*. ⇒ ⭐ **`T1` edits `Hrot.NodeComposition`, which IS in the main solution and DOES build here** — ⚠ but its Stride-side consumer does not, so ⛔ **report `HrotStrideApp.Game` as REVIEWED, NOT COMPILED** and name it as **owed a Windows check** |
| ⭐ **`--mode all` = FIVE subsystems** | `orchestrator,simhost,ig,excon,cgf` — ⛔ not three. An unknown mode **throws** *(`HrotRunnerConfiguration.cs`)* |
| 🔴 **each subsystem gets its OWN isolated `FdpEventBus` under `--mode all`** | 📌 **that is the documented design, and it is why the crash happened**: `OrchestrationEventRegistry.RegisterAll` in one subsystem's bootstrap does nothing for another's. ⇒ ⭐ **a "mode starts" rail must tick far enough for each subsystem's FIRST publish** — ⛔ a rail that only checks the process launched would have stayed green through this crash |
| ⚠ **`stridemock` is gone** | ⛔ do not add it to the mode list |

## 3. ⛔ LANE & SCOPE

⭐ **Your surface:** `Hrot.NodeComposition` *(T1)* · `Hrot.SystemTests` **mode rails only** *(T2)* ·
`Hrot.ClusterRunner` if composition needs a fix the rail exposes.

⚠⚠ **A PARALLEL BATCH EXISTS** — 📄 `HANDOFF_Regression_Net.md` *(ids `HN-`, tracker **Area J**)* also touches
`Hrot.SystemTests`. ⭐⭐ **The split is by FILE: they own `Goldens/`, the golden store, the determinism rail and
the panel rails; you own ONLY the new mode-rail file.** ⛔ **Do not touch theirs, and do not add a golden.**
⭐ **Rule 4: pull the coordinator branch before your final commit** — they will have landed in the same project.

⛔ **Not this batch:** `ST-017` *(the lost `PanelSnapshot` rail)* — ⭐ it is coverage, so it belongs with the
net; the other batch carries it.

## 4. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · **delta vs the
dispatch sha** · a `--no-build` column · every RED confirmed pre-existing **by name** · `tracker-counts.py
--check` · `rulings-check.py` · `design-digest.py --check` · **the `ST-` ids you allocated** *(rule 5)*.

⭐⭐ **Row 8 — the integration invariant.** `T1` changes a **kernel tick path**, so the invariant is
*"cluster time still holds"*: `Hrot.ClusterRunner.Integration.Tests ~TimeControlIntegrationTests` and
`Fdp.ModuleHost.Tests`, **named and run**. ⭐ `T2` **is** an integration gate — report it as one.

⚠⚠ **AND A PROCESS NOTE, stated plainly and without blame:** 📌 **the last batch produced no `§Gates` table
at all** — the technical work and the design fold-back were good, but the contract's gate rows were not
delivered, and **the missing mode-matrix row is exactly what the user then found by hand.** ⇒ ⭐⭐ **this
batch's report must carry the full table.** ⛔ *"I ran it and it was fine"* is not a row.

⚠ **Known baseline quirks — do not re-derive:** `tracker-counts.py --check` counts only `**BP-` rows ⇒ **it is
blind to your `ST-` rows.** `Fdp.Presentation.Tests` crashes ~18–20 cases in *(`BP-419`, pre-existing,
`R-131`)*. `tools/ai-debug-mcp` `verify.mjs` fails pre-existing *(needs `npm install`)*.

## 5. ⭐ WHEN YOU ARE DONE

⭐⭐ **Fold the as-built into [`DESIGN_Stride_Port.md`](../../DESIGN_Stride_Port.md)** — §8 becomes RESOLVED
*(or records what blocked it)*, and the mode rails get a line saying where they live. ⛔ Design content goes
in the design, not the report.
