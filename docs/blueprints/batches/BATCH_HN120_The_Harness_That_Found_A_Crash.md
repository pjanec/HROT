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

---

# ═══ PHASE 2 — MCP extensions, SLICE ① ═══

> 📄 **Design + as-built:** [`MCP_Integration.md`](../../MCP_Integration.md) §"AS-BUILT — SLICE ①".
> ⭐ Built ON the phase-1 harness, exactly as the handoff intended: **every claim below is gated by a
> smoke case driving a real editor**, so a red in phase 2 is the endpoint, not the instrument.

## 7. ⭐ What landed

| # | |
|---|---|
| **`MX4a`** | **`GET /behaviors`** — `?tkbType=` · `?entityId=` · neither. Each row `{id, name, brainTier, paramSchema}` |
| **`MX7`** | **`GET /breakpoint-types`** — the 12-arm closed union, each with its param schema |
| ⭐ **shared** | **`DtoJsonSchemaExtractor`** — one property walk serving both, as the design required |
| **`MX8`** | **`Hint` on the envelope** + the central **`DebugApiHints`** map *(12 categories)*; **15 existing failures back-filled** |
| **`MX5`** | `list_behaviors` + `list_breakpoint_types` Node wrappers; `SKILL.md` regenerated. **49 → 51 tools** |
| **`MX6`** | **8 smoke cases**, including the loop the slice exists to close |

## 8. ⛔⛔ The design premise that was FALSE — **and measuring it produced a better seam**

⭐⭐⭐ **`BehaviorUiRegistry` cannot answer behaviourId→DTO.** `Register<TDto>(id)` compiles an ImGui
**draw delegate** and discards the type. ⇒ the design's *"look up each DTO type in the registry"* was
unbuildable as written.
⭐⭐ **The real seam — `BehaviorRegistry.BehaviorDefinition.ParamsDtoType`** — is the DTO the **runtime
itself parses params with** ⇒ ⭐ **one declaration** behind both the schema and the bytes, which is what
the design *wanted* and a UI registry could never have given.
📄 Corrected in the design's `known-rot` + AS-BUILT §; recorded as **`MX-001`**.

## 9. ⭐ Ids allocated in phase 2

| id | |
|---|---|
| ✅ **`MX-001`** | slice ① built; the `BehaviorUiRegistry` premise corrected to `ParamsDtoType` |
| ⚠ **`MX-002`** | **three** interfaces named `IMissionEditorService`; the UML cites the wrong one ⇒ **`MX4b` must name the namespace** |
| 🔴 ✅ **`MX-003`** | the Node `toolError` hint-key collision that would have silently defeated `MX8` |

## 10. GATES — phase 2 *(all `--no-build` runs are against a freshly built solution)*

| gate | command | result | vs base `14b3f8867` |
|---|---|---|---|
| ⭐⭐ **Row 8 — slice-1 smoke on the phase-1 harness** | `dotnet test …/Hrot.SystemTests.csproj` | ⭐ **27 passed · 0 failed · 2 skipped**, ~15 s | **+9** *(18 → 27; the 2 skips are still only `HN-001` and what it blocks)* |
| ⭐ **`Hrot.Editor.Tests`** *(production code changed here)* | `dotnet test …/Hrot.Editor.Tests.csproj --no-build` | ⭐ **209 passed · 0 failed** | ⭐⭐ **IMPROVED — baseline was 207/2.** The 2 `ScenarioMenuTests` reds were fixed by the **coordinator's** merge, not by this batch |
| ⭐⭐ **`Hrot.Blueprints.Tests` `~Editor`** *(THE `EditorSubsystem` gate)* | `--filter "FullyQualifiedName~Hrot.Blueprints.Tests.Editor" --no-build` | **1032 passed · 0 failed · 9 skipped** | **unchanged** |
| ⭐ **`ClusterRunner.Integration` `~TimeControlIntegrationTests`** *(cross-node invariant)* | `--filter "FullyQualifiedName~TimeControlIntegrationTests" --no-build` | **9 passed · 0 failed** | **unchanged** |
| solution | `dotnet build IOS-IG-SimHost.sln` | **0 errors** | unchanged |
| Node server | `node src/index.mjs` | ⭐ **starts clean, 51 tools** *(was 49)* | **+2** |
| Node `SKILL.md` | `node generate-skill.mjs` | **written, 337 lines** | regenerated |
| ⚠ Node `verify.mjs` | `node verify.mjs` | ⛔ **FAILS — `MCP error -32000: Connection closed`** | ⭐⭐ **PRE-EXISTING, proved by stash:** it fails identically at clean HEAD *(reporting 49 tools)*. ⚠ It also needed `npm install` — `node_modules` had never been installed in this tree *(and stays gitignored)* |
| designs · ledger · tracker · mermaid | as phase 1 | **all pass** | unchanged |

⭐ **No golden files moved · working tree clean after every run · no new skip** beyond phase 1's two.

## 11. ⚠ Two interference bugs the harness caught **in its own tests** — worth recording

| | |
|---|---|
| 🔴 **An armed breakpoint outlives the case that set it, and its EFFECT outlives the breakpoint.** My recovery case armed a `Lifecycle` breakpoint on `"Bradley"` — and hill-attack really contains an **M2 Bradley IFV**. It tripped during a LATER case's scenario load, paused the sim, and the cluster never reached `OperatingEdit` ⇒ a **504 on `/scenario/load`** in a case that had nothing to do with breakpoints. ⭐ Fixed twice over: the case now targets a name that matches nothing *(it tests ACCEPTANCE, not firing)*, and `ResetToIdleAsync` clears breakpoints |
| ⚠ **Reload churn.** Loading the scenario per case rebuilds entities with fresh network ids, so listing while another case's reload settles yields an id the map has already dropped. ⭐ The world is now loaded **once per editor** |

⭐⭐ **Both are the shared-editor cost of `D6`** *(one editor per collection)*, and both are cheap to
avoid once named — ⛔ neither is a product defect.

## 12. Obligation ③ — **diagrams checked, phase 2**

⭐ Built against §"UML"'s `classDiagram` + `sequenceDiagram`. **`DtoJsonSchemaExtractor`, `DebugApiHints`,
the `Hint` field and the new `DebugApiService` members all match the drawn contract.** ⛔ **One box does
not:** `DtoJsonSchemaExtractor ..> BehaviorUiRegistry : reads behaviour DTO type` — **that edge cannot
exist**, and the extractor reads `BehaviorRegistry.ParamsDtoType` instead. ⇒ ⭐ **folded back into the
design** *(`known-rot` + AS-BUILT §)*, not merely reported here.
