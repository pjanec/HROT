<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-22
current-answer: dispatch pointer for the implementation session (the repurposed TIME/Stride lane, now idle
  after the parked Stride port) to BUILD the MCP-driven system-test harness. THE DESIGN is
  DESIGN_MCP_System_Test_Harness.md; build from its §3 classDiagram + §4 sequenceDiagram (obligation ①).
known-conflict: none.
-->
# HANDOFF — implementation session · **build the MCP-driven system-test harness**

> 📌 **Dispatched at `14b3f8867`** *(re-stamped from `619b90756` while unstarted, rule 1a — this head adds the
> finalized MCP-extensions design and the ScenarioMenuTests fix; neither changes the harness scope)*. ⭐ Branch
> **fresh from the coordinator branch** *(rule 7)*; **rule 1b: started-marker FIRST.** ⛔ **Scope FROZEN at this sha.**
> ⚠⚠ **NEW WORK AREA — not Stride, not time.** ids **`HN-`** *(new prefix)*, a **new tracker area**
> *(Area J — MCP harness)*; ⛔ NOT Area H (time), NOT Area I (Stride), NOT A–G (UI/variable).
> 🅿 **The Stride port is PARKED at `claude/stride-port` (`b9ab83b0e`)** for the user's Windows visual test.
> ⛔⛔ **DO NOT branch from it and do NOT build on it** — branch from the coordinator line `619b90756`,
> which does **not** contain the Stride commits. The harness targets the **editor**, not Stride.

## 0. ⛔⛔ READ THE DESIGN FIRST — it holds the class diagram, the sequence, the decisions and the tasks

📄 **[`DESIGN_MCP_System_Test_Harness.md`](../../DESIGN_MCP_System_Test_Harness.md)** — read it whole.
- ⭐⭐⭐ **§3 `classDiagram`** — the exact types to build: `EditorProcessFixture` · `McpClient` · `ApiResult` ·
  `SystemTestBase` · `CapabilitySmokeTests` · `ScenarioBehaviorTests`, with their members and ownership.
- ⭐⭐⭐ **§4 `sequenceDiagram`** — the run: fixture boots the editor headless, polls `/status`, tests share it,
  drive over the API, teardown kills the process tree + Xvfb + tempdir.
- ⭐ **§5 `D1`–`D11`** decisions *(all with leans — treat as settled unless a measurement contradicts one)*;
  **§6 `H1`–`H7`** tasks; **§7** risks; **§8** out-of-scope.
- ⭐ **Obligation ③:** check what you build against the two diagrams and report the match/deviation.
  **Obligation ⑤:** fold any deviation back into the design *(mark the prior state superseded)* before the batch closes.

## 1. ⭐ WHAT TO BUILD — a new `Hrot.SystemTests` project, additive

⭐ **`D1`: a NEW project `Hrot.SystemTests`** *(xUnit)* — ⛔ do NOT pollute the fast `*.Integration.Tests`.
It **references** `Hrot.ClusterRunner` *(so the editor binary is on disk to launch)*, and drives it over HTTP.
⭐⭐ **Almost entirely additive** — a new project + one CI job. ⛔ **No edit to the editor, the MCP wiring in
`EditorSubsystem`, or any frozen/UI-lane file** — the harness *consumes* the API, it does not change it.

| # | task | design ref | gate |
|---|---|---|---|
| **H1** | **`EditorProcessFixture`** *(IAsyncLifetime collection fixture)* — free-port alloc *(bind `:0`, release, pass via env)*, temp staging dir via `FDP_STAGING_ROOT`, launch headless *(Xvfb on Linux, direct on Windows — detect)*, `WaitForStatus` poll, robust teardown *(kill process tree + Xvfb + delete tempdir)* | §3, §6 `H1`, `D3`/`D4`/`D5` | boots + `/status` 200 + clean teardown, on Linux-Xvfb |
| **H2** | **`McpClient`** — typed method + DTO per endpoint group used *(lifecycle, scenario, sim, preview, entities, breakpoints, checkpoint/diff, recording, replay, trace)*; `ApiResult` envelope | §3, §6 `H2` | each method round-trips against a booted editor |
| **H3** | **`SystemTestBase`** + async assertion helpers *(`WaitUntilAsync`, `WaitForBreakpointHitAsync`, `LoadAndPreviewAsync`)* | §3, §6 `H3`, `D8` | helpers proven by H4 |
| **H4** | **capability smoke suite** — one case each: status · curated-scenario load · list/get entities · preview+play advances time · **breakpoint set → play → hit** · **watch read + write a variable** · **checkpoint → mutate → diff** · **record → replay 48 frames** · **fault injection (Group L)** · **trace observe** | §6 `H4` | all green **headless** |
| **H5** | **≥1 scenario-behaviour case** — e.g. load `hill-attack`, play N ticks, assert a squad entity reached a state; document the pattern so cases grow with the curated set | §6 `H5`, `D9` | ≥1 green, pattern documented |
| **H6** | **CI lane** — a separate job that installs Xvfb + a GL driver, builds, runs `--filter Category=SystemSmoke` *(`D10`)* | §6 `H6`, §7 | green in CI |

⛔ **`H7` (declarative scenario-script DSL) is OUT OF SCOPE** — its own future design *(§8)*.

## 2. ⭐⭐ THE PROVEN GROUND — reuse it, do not re-derive it

⭐ The manual drive this harness automates is **already proven end-to-end headless** and written up. Build H4
against these verified facts *(do not rediscover them the hard way)*:

📄 **[`docs/MCP_Integration.md`](../../MCP_Integration.md)** — the wired + verified API, the endpoint groups,
and the **record→replay 48-frame cycle** proven manually. ⚠ **Gotchas already paid for**, encode them in `McpClient`:
- ⭐ **enable the API** with env **`HROT_DEBUG_API_PORT=<port>`** on the launched process.
- ⭐ the replay-load endpoint takes **`fdpPath`**, ⛔ **not `path`** *(cost an iteration)*.
- ⭐ **`FDP_STAGING_ROOT`** must point at a writable temp dir *(the old `C:\FDP_Temp` default is Linux-invalid;
  `ResolveStagingRoot` fixed the default, the fixture still sets it per-run for isolation)*.
- ⚠ **do not enter preview twice** — a second `EnterPreview` on an already-previewing session errors; gate it.

📄 **[`docs/Editor_Headless_Xvfb.md`](../../Editor_Headless_Xvfb.md)** — the exact proven launch:
`xvfb-run … dotnet Hrot.ClusterRunner.dll --mode editor` with software GL *(`LIBGL_ALWAYS_SOFTWARE=1
GALLIUM_DRIVER=llvmpipe`)*. ⭐ **`H1` resolves the binary from the referenced project's output, ⛔ never a hard-coded path** *(§7)*.

📄 **Curated worlds** — `CuratedScenarios` seeds `hill-attack`/`test-fire`/`test-move` from git on start; the
layout defaults seed the curated UI. ⭐ These are the deterministic worlds H4/H5 assert against.

## 3. ⛔ LANE & NOT-THIS-BATCH

⛔ **Do not touch:** the UI/variable frozen area *(`Hrot.Editor.AiShared`, variables, blackboard, Details)*,
the UI lane's Details/menu files, the coordinator's MCP wiring in `EditorSubsystem`/`DebugApi/*`, or the
parked Stride tree. ⭐ **Your surface:** the new `Hrot.SystemTests` project + its CI job, and *(if strictly
needed for a stable smoke)* additive test-only helpers **inside that project only**.
⚠ **A cross-lane edit is a STOP-and-report** *(`R-128`)*. ⛔ **If a smoke case needs an API change, STOP and
report it** — that is the MCP-extensions batch *(next)*, not this one.

## 4. ⭐ NEXT BATCH (not now) — the MCP server extensions

⭐ Once the harness is green, the **MCP extensions** *(Groups O–R: full variable addressing/watch parity,
mission editing via the intent bus, behavior discovery with param-DTO schema, entity-state dump)* follow as a
**separate handoff**, built against **[`docs/MCP_Integration.md`](../../MCP_Integration.md)**'s extension design
*(`MX1`–`MX6`)*. ⛔ **Not this batch** — the harness first, so the extensions land with a smoke suite to prove them.

## 5. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · delta vs base · the
`--no-build` column · every RED confirmed pre-existing against the base sha `14b3f8867` · goldens as a diff
shape · `tracker-counts.py --check` · `rulings-check.py` · the **`HN-` ids you allocated** · `R-106` verdicts.
⭐⭐ **Row 8 — the integration invariant IS this suite:** the harness's own smoke run *(`H4`, headless under
Xvfb, green)* is the system-level proof; report it as the integration row, with the exact `--filter` and the
Xvfb launch used. ⭐ Rule 4/7: re-sync + pull the coordinator branch around the batch. ⭐ Rule 1b: push the
started-marker before writing code.
