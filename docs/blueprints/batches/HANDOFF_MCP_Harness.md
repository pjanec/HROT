<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-22
current-answer: dispatch pointer for the implementation session (the repurposed TIME/Stride lane, now idle
  after the parked Stride port). TWO PHASES in one continuous run: PHASE 1 build the MCP-driven system-test
  harness (DESIGN_MCP_System_Test_Harness.md), push it GREEN as a checkpoint, then PHASE 2 continue to MCP
  extensions SLICE 1 (MX4a+MX7+MX8, MCP_Integration.md §"UML"/§"Sequencing"). Build each from its diagrams (obligation ①).
known-conflict: none.
-->
# HANDOFF — implementation session · **harness (phase 1) → MCP extensions slice 1 (phase 2)**

> ⭐⭐⭐ **ONE run, TWO phases, with a CHECKPOINT between.** ⭐ **Phase 1 = the harness**; when its smoke suite is
> GREEN headless, **push a `chore: harness green at <sha>` checkpoint + a short status in the batch doc, then
> CONTINUE — do NOT idle waiting for the coordinator.** ⭐ **Phase 2 = MCP extensions slice 1** (MX4a+MX7+MX8),
> built ON the now-green harness. ⚠ **Why the checkpoint:** the harness is the *test instrument* for the
> extensions — a green foundation means a red in phase 2 is the endpoint, not the harness.

> 📌 **Dispatched at `14b3f8867`** *(re-stamped from `619b90756` while unstarted, rule 1a — this head adds the
> finalized MCP-extensions design and the ScenarioMenuTests fix; neither changes the harness scope)*. ⭐ Branch
> **fresh from the coordinator branch** *(rule 7)*; **rule 1b: started-marker FIRST.** ⛔ **Scope FROZEN at this sha.**
> ⚠⚠ **NEW WORK AREA — not Stride, not time.** a **new tracker area** *(Area J — MCP harness + extensions)*;
> ⛔ NOT Area H (time), NOT Area I (Stride), NOT A–G (UI/variable). ⭐ ids: **`HN-`** for phase-1 harness rows,
> **`MX-`** for phase-2 extension rows *(the design's `MX4a`/`MX7`/`MX8` task ids)*.
> 🅿 **The Stride port is PARKED at `claude/stride-port` (`b9ab83b0e`)** for the user's Windows visual test.
> ⛔⛔ **DO NOT branch from it and do NOT build on it** — branch from the coordinator line `14b3f8867`,
> which does **not** contain the Stride commits. Phase 1 targets the **editor** from outside; phase 2 extends the
> **MCP server** (see the phase-2 scope in §4).

# ═══ PHASE 1 — the harness ═══

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

### ⭐⭐ THE CHECKPOINT — push harness-green, then continue to phase 2

⭐ When `H1`–`H6` are green headless: **push `chore: harness green at <sha>`** and write a 1-paragraph status in
this batch doc *(the gates table for phase 1)*. ⛔ **Then CONTINUE to phase 2 in the same run — do NOT wait for a
coordinator review.** ⭐ The checkpoint is a clean commit the coordinator can glance at, not a barrier.

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

## 3. ⛔ PHASE-1 LANE — the harness edits NOTHING but its own project

⛔ **Phase 1 does not touch:** the UI/variable frozen area *(`Hrot.Editor.AiShared`, variables, blackboard,
Details)*, the UI lane's Details/menu files, the coordinator's MCP wiring in `EditorSubsystem`/`DebugApi/*`, or
the parked Stride tree. ⭐ **Phase-1 surface:** the new `Hrot.SystemTests` project + its CI job, and *(if strictly
needed for a stable smoke)* additive test-only helpers **inside that project only**.
⚠ **A cross-lane edit is a STOP-and-report** *(`R-128`)*. ⛔ **In phase 1, if a smoke case needs an API change,
DON'T patch it here — note it; phase 2 adds the endpoint** *(the harness's own missing-capability cases become
phase-2 work)*.

# ═══ PHASE 2 — MCP extensions, SLICE 1 (after the harness checkpoint) ═══

## 4. ⭐⭐ WHAT TO BUILD — the discovery + self-correction spine (MX4a · MX7 · MX8)

📄 **[`docs/MCP_Integration.md`](../../MCP_Integration.md)** — read **§"MCP EXTENSIONS"** through **§"Sequencing"**.
⭐⭐⭐ **§"UML — the build contract"** carries the **`classDiagram` + `sequenceDiagram`** — build to them
*(obligation ③: report the match/deviation; obligation ⑤: fold any deviation back into that doc, marking the
prior state superseded, before the batch closes)*. ⭐ **§"Sequencing"** puts these three in **slice ①**.

| # | task | design ref | gate |
|---|---|---|---|
| **MX4a** | **`GET /behaviors?tkbType=`** → `{id,name,paramSchema}[]`. ⭐⭐ **REUSE** `BehaviorUiRegistry`/`BehaviorSchemaDiscovery` (behaviourId→DTO, already TKB-filtered via `IMissionEditorService.GetAvailableBehaviors`); a **shared `DtoJsonSchemaExtractor`** emits the schema from the same property walk `BehaviorUiCompiler.Compile` does. `[ParamDoc]` OPTIONAL enrichment only | §"Group P/P.0", §UML | endpoint returns schemas for a known TKB type; MX6 smoke |
| **MX7** | **`GET /breakpoint-types`** → `{$type,paramSchema}[]` by reflecting the `SearchPredicateDto` `[JsonDerivedType]` **closed 12-arm union**. ⭐ **SAME `DtoJsonSchemaExtractor`** as MX4a; ⛔ **no new encoding** (conditions round-trip via the existing `SearchPredicateJsonOptions`). Set/list/remove/hits already exist (Group G) | §"Group S", §INVENTORY, §UML | endpoint lists the arms + schemas; MX6 smoke |
| **MX8** | **self-describing errors** — add `JsonNode? Hint` to the `ApiResponse`/`RouteResult` envelope; a central **`DebugApiHints`** category→endpoint map; **back-fill the existing prose hints**; attach the right hint to every schema-shaped validation failure *(condition→`/breakpoint-types`, behaviour→`/behaviors`)* | §"Self-describing errors", §UML | a bad condition's error carries `hint.seeEndpoint`; MX6 smoke |
| **MX5** | Node MCP-server tool wrappers + `SKILL.md` regen **for these three** *(behaviors, breakpoint-types; hint surfaced in tool errors)* | §"Sequencing ⤫" | the new tools are agent-callable |
| **MX6** | **harness smoke cases for slice 1** — `GET /behaviors` returns a schema · `GET /breakpoint-types` returns the arms · **POST a bad condition ⇒ error `hint.seeEndpoint == "GET /breakpoint-types"`** | §"Sequencing ⤫", H4 | all green headless on the phase-1 harness |

## 4b. ⛔ PHASE-2 LANE — extends the MCP server, still bans the frozen area

⭐ **Phase 2 MAY edit** *(additively)*: `Hrot.Editor/DebugApi/*` *(`DebugApiService`, `DebugApiHost`, the envelope)*,
and the **MCP wiring in `EditorSubsystem`** *(inject collaborators the new endpoints need — like the existing
`_blueprintSession`)*, plus `tools/ai-debug-mcp/` *(Node wrappers)*. ⭐ **REUSE the existing seams** named above
*(obligation ②: `BehaviorUiRegistry`, `SearchPredicateDto`, `SearchPredicateJsonOptions`)* — ⛔ do NOT build a
parallel registry or a second schema walk. ⛔ **Still DO NOT touch** the frozen variable model
*(`Hrot.Editor.AiShared`, variables, blackboard, Details)* or the UI lane's Details/menu files — ⚠ a cross-lane
edit is STOP-and-report *(`R-128`)*. ⛔ **NOT this batch:** extensions slices ② *(MX1/MX3/MX2)* and ③ *(MX4b)* —
a follow-up handoff.

## 5. GATES — report per phase

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · delta vs base · the
`--no-build` column · every RED confirmed pre-existing against the base sha `14b3f8867` · goldens as a diff
shape · `tracker-counts.py --check` · `rulings-check.py` · the **`HN-`/`MX-` ids you allocated** · `R-106` verdicts ·
`design-digest.py --check` · `mermaid-check.mjs` on any design you fold a deviation into *(obligation ⑤)*.
- ⭐⭐ **Phase 1 — Row 8 integration invariant IS the harness itself:** the `H4` smoke suite, headless under Xvfb,
  green — report it with the exact `--filter` and the Xvfb launch used. **This is what the checkpoint pushes.**
- ⭐⭐ **Phase 2 — Row 8:** the **slice-1 MX6 smoke cases** running green **on that same harness** *(behaviors +
  breakpoint-types return schemas; a bad condition's error carries the `hint`)* — the system-level proof the
  endpoints work end-to-end. Plus the touched-project unit suites for `Hrot.Editor`/`DebugApi`.
⭐ Rule 4/7: re-sync + pull the coordinator branch around the batch. ⭐ Rule 1b: push the started-marker before
writing code *(phase 1)*; ⭐ push the harness-green checkpoint before phase 2 *(§1's checkpoint rule)*.
