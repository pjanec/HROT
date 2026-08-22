<!--STATUS
state: LIVE
updated: 2026-08-22
current-answer: the phase-1 report for Batch HN-120 (the MCP system-test harness, tasks H1–H6).
  ⭐ The DESIGN carries the as-built truth and the diagrams: DESIGN_MCP_System_Test_Harness.md §9.
  This file is the ephemeral batch record — gates, ids, verdicts. It does NOT restate the design.
known-conflict: none.
-->
# BATCH HN-120 — phase 1: the harness *(and the crash it found on run one)*

> 📄 **Design + as-built:** [`DESIGN_MCP_System_Test_Harness.md`](../../DESIGN_MCP_System_Test_Harness.md)
> — **§3/§4** the contract, **§9** what was built and the four deviations *(obligation ⑤: folded into
> the design, not left in this report)*.
> 📌 **Dispatch:** `HANDOFF_MCP_Harness.md`, scope frozen at **`14b3f8867`**. Branched from the
> coordinator line *(rule 7)*; started-marker `c217a7c36` *(rule 1b)*.

## 1. ⭐ What landed

| | |
|---|---|
| **`Hrot/Runner/Hrot.SystemTests/`** | 7 files — `EditorProcessFixture` *(H1)* · `McpClient` + `ApiResult` *(H2)* · `SystemTestBase` *(H3)* · `CapabilitySmokeTests` *(H4)* · `ScenarioBehaviorTests` *(H5)* · `KnownDefectRails` · `SystemTestEnvironment` |
| **`scripts/run-system-tests.sh`** | the local lane *(H6)* — preflight, build, filtered run, editor-log dump on failure |
| **`.github/workflows/system-tests.yml`** | the CI lane *(H6)* — ⚠ **manual-trigger**, see §4 |
| **docs** | design §9 *(as-built)* · tracker **Area J** *(new)* |

⭐⭐ **Zero production files changed.** The diff is a new project, a solution entry, two scripts and
three documents — ⛔ nothing under `Hrot/Subsystems`, `FDP/`, the frozen variable model, or the UI
lane's files. *(Handoff §3: the harness consumes the API, it does not change it.)*

## 2. ⛔⛔ THE HEADLINE — **the harness found a process-killing crash on its first full run**

⭐⭐⭐ **`HN-001`: `POST /preview/exit` aborts the editor (SIGABRT, exit 134).** Three HTTP calls, no
test code: `scenario/load hill-attack` → `preview/enter` → `preview/exit` ⇒ dead. All three curated
scenarios. ⛔ **`/recording/stop` exits preview too**, so the record→replay round trip dies the same way.
⚠⚠ **Likely a regression:** `MCP_Integration.md` records that exact cycle working end to end on
**`2026-08-22`**. 📄 **Full mechanism, repro and blast radius: tracker `HN-001`.**

⛔ **NOT fixed here** — phase 1's surface is the harness *(handoff §3)*, and the fix is engine
snapshot/restore semantics that deserve their own measurement. ⭐ **Pinned by a skipped rail carrying
the repro**, so it cannot be forgotten and cannot silently regress once fixed.

## 3. ⭐ Ids allocated — **`HN-` in tracker Area J** *(rule 5)*

| id | |
|---|---|
| 🔴🔴 **`HN-001`** | `POST /preview/exit` aborts the editor *(above)* |
| **`HN-002`** | the dump emits `Position` as `[x,y,z]`, the patch parser wants `{X,Y,Z}` ⇒ **read-modify-write fails on the round trip** |
| **`HN-003`** | `POST /shutdown` is **inert** — `EditorSubsystem:1585` passes `() => { }` |
| **`HN-004`** | `EntityRepository.View.cs:64` prints `FATAL:` to stdout from `Fdp.Core` *(⛔ route it, don't delete it — it is what made `HN-001` diagnosable)* |
| **`HN-005`** | `H4`'s "watch read + write a **variable**" is **unbuildable**: 47 routes, none `/variables` ⇒ that is `MX1`, slice ② |

⚠ **No `BP-`/`TM-`/`ST-` id was touched.** Area I *(Stride)* is absent from this branch by design — the
port is parked at `claude/stride-port`.

## 4. GATES *(rule 8 contract — one row per gate, verbatim command, delta, `--no-build` column)*

| gate | command | result | builds? | vs base `14b3f8867` |
|---|---|---|---|---|
| ⭐⭐ **the harness, headless** *(Row 8: this IS the integration invariant)* | `dotnet test Hrot/Runner/Hrot.SystemTests/Hrot.SystemTests.csproj --nologo` | ⭐ **18 passed · 0 failed · 2 skipped**, ~17 s | ⭐ **builds** | **new suite** |
| same, via the shipped lane | `bash scripts/run-system-tests.sh` *(filter `Category=SystemSmoke`)* | ⭐ **18 / 0 / 2** — identical | builds | new |
| solution | `dotnet build IOS-IG-SimHost.sln` | ⭐ **0 errors**, 64 warnings *(pre-existing `BP3010` orphan-node warnings from `Hrot.AI.Behaviors`)* | builds | unchanged |
| tracker | `python3 scripts/tracker-counts.py --check` | **OK — open 90 / done 264** | — | unchanged ⚠ *(counts only `BP-` rows; `HN-` rows are invisible to it — stated, not discovered)* |
| ledger | `python3 scripts/rulings-check.py` | **22/22 verified** · ⚠ **1 staleness WARN on `.claude/CLAUDE.md`** | — | ⭐ **pre-existing** — arrived with a coordinator merge, not this batch |
| designs | `python3 scripts/design-digest.py --check` | **all 57 pass** — STATUS, INVENTORY, UML present | — | unchanged |
| mermaid | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs docs/DESIGN_MCP_System_Test_Harness.md` | **2/2 blocks parse** | — | — |

⭐ **No golden files moved** — the batch writes none.
⭐ **Working tree clean after every suite run** *(no test regenerated an artefact)*.
⭐ **Both skips are the SAME finding, not a fix** — `HN-001`'s rail and the record→replay round trip it
blocks. ⛔ No other suite gained a skip.
⛔⛔ **"Green in CI" is NOT claimed** — `.github/workflows/system-tests.yml` is **the repository's first
GitHub Actions workflow** and ships **manual-trigger**: arming it on every push would start CI across a
codebase with known pre-existing reds. The file names the one-line change. The lane is verified locally.

⚠ **Touched-project unit suites: none run, and that is correct** — no production project was modified,
so there is nothing whose behaviour this batch could have changed. The system suite is the gate.

## 5. ⚠ Two mistakes worth recording — **both mine, both caught by measuring**

| | |
|---|---|
| ⛔ **Nearly reported my own bug as a product defect.** Three cases failed on `Invalid patch value … expected Vector3` and a 404. ⭐ **Probing the API directly showed the write SUCCEEDS with `{X,Y,Z}`** — my array shape was simply wrong, and the 404 was my own reload churn, not an API incoherence. ⚠ *(The `2026-08-22` session already paid for one scoped-grep false negative; this is the same discipline.)* ⭐ The real asymmetry that survived became `HN-002` |
| 🔴 **My first teardown leaked a display server per run.** `xvfb-run` stops Xvfb from an **EXIT trap**; `Process.Kill` sends **SIGKILL** ⇒ the trap never runs. **4 orphaned `Xvfb` + 4 X-locks** accumulated before I checked. ⭐ **H1's "clean teardown" gate was FALSE until measured** — the fixture now owns the server. **0 orphans, 0 locks**, verified |

## 6. Obligation ③ — **diagrams checked**

⭐ The design carries **1 `classDiagram` + 1 `sequenceDiagram`**. Built to both; **four deviations**,
each forced by a measurement, each argued and **folded back into the design at §9** with the prior state
marked in the STATUS block's `known-rot`. ⇒ ⭐⭐ **the diagrams are true again**, rather than merely
reported as untrue here.
