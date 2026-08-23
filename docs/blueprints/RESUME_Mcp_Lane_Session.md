<!--STATUS
state: LIVE
updated: 2026-08-22
current-answer: the whole file. Written for a FRESH session after compaction; assumes no prior
  conversation. This lane began as TIME, was repurposed to STRIDE, and is now the MCP / headless-
  testability lane. Its next task is HANDOFF_Derisk_Engine_And_MX1.md.
superseded: RESUME_Time_Stride_Session.md — kept for the PARKED Stride branch's gate baselines only.
known-conflict: none.
-->
# RESUME — the **MCP / headless-testability** lane

> ⛔ **Self-contained. Assumes no prior conversation.** ⭐ You are an **implementation** session. A
> separate coordinator session owns the tracker's other areas and writes the handoffs you execute.

## 0. ⭐⭐⭐ WHO YOU ARE, AND WHERE

| | |
|---|---|
| **branch** | ⭐ `claude/time-system-refactor-batch-104-gp617x` — ⚠ **the NAME is historical**; this is now the MCP lane |
| **head at write time** | `2d95c419f` *(= the coordinator head; everything is merged and pushed)* |
| **coordinator branch** | `claude/blueprint-authoring-status-gm0akp` |
| **the other implementation lane** | UI / panels — currently building **`U-obs-1`** *(the `PanelSnapshot` view-model)*. ⛔ **You must not touch their files** |
| ⭐ **id prefixes YOU own** | **`HN-`** *(harness)* · **`MX-`** *(MCP extensions)*, tracker **Area J**. ⛔ Never `BP-` *(UI lane)*, never `TM-`/`ST-` |
| 🅿 **parked** | the **Stride port** at `claude/stride-port` (`b9ab83b0e`), awaiting the user's Windows visual test. ⛔ **Do not branch from it or build on it** |

## 1. ⭐⭐ THE VERY FIRST THING TO DO

```bash
bash scripts/cloud-bootstrap.sh          # only if `dotnet` is missing or the graph tools are absent
export PATH="$PATH:$HOME/.dotnet"        # ⛔ dotnet is NOT on PATH by default in this container
git fetch origin claude/blueprint-authoring-status-gm0akp
git merge origin/claude/blueprint-authoring-status-gm0akp --no-edit          # rule 7
git commit --allow-empty -m "chore: started <batch> at 5843055e7" && git push # rule 1b, BEFORE any code
```

## 2. ⭐⭐⭐ THE NEXT TASK — **fix what the harness caught, then MX1**

📄 **Dispatch: [`docs/blueprints/batches/HANDOFF_Derisk_Engine_And_MX1.md`](batches/HANDOFF_Derisk_Engine_And_MX1.md)**
— **dispatched at `5843055e7`; scope FROZEN there.** ids `HN-`/`MX-`, tracker **Area J**.

| # | task | ⭐ note |
|---|---|---|
| 🔴🔴 **`HN-001`** | **fix the preview-rewind crash** *(§3 below — read it before touching code)* | ⭐ **un-skip** `KnownDefectRails.Exiting_preview_does_not_abort_the_editor`; it also un-blocks record→replay |
| **`HN-002`** | `Vector3` read/write asymmetry — dump emits `[x,y,z]`, the StructEdit patch parser wants `{X,Y,Z}` | ⭐ **lean: accept the array form in the parser** *(the dump's array is the natural read)* |
| **`HN-003`** | `POST /shutdown` is inert — `EditorSubsystem` passes `() => { }` | pass the orchestrator's real stop |
| **`HN-004`** | `EntityRepository.View.cs:64` `Console.WriteLine`s `FATAL:` from `Fdp.Core` | ⛔ **ROUTE through the logger, do NOT delete** — it is what made `HN-001` diagnosable |
| ⭐⭐ **`MX1`** | **Group O — variable addressing**: `GET /entities/{id}/variables?asset=` · `GET …/variable?asset=&path=` *(value + pending)* · `POST …/variable` *(stage)* | ⭐ **REUSE `_blueprintSession`** *(already injected)*: `ResolveWorkingStateField` → `StageFieldMutation`/`TryGetPending`; values via `ScenarioSerializer`. ⛔ **This finishes `HN-005`**, the harness's owed watch case |

⭐ **`MX1` carries its companions if cheap:** `MX5` Node wrappers + `SKILL.md`, `MX6` smoke.
⛔ **`MX2`/`MX3` are NOT required.**

### ⛔⛔ THE WALL — **stop at Group T**

`GET /panels` *(Group T / `MX9`)* reads the **`PanelSnapshot`** singleton the **UI lane** is building in
`U-obs-1`. ⇒ ⛔ **do not build it in this batch.** ⚠ Finish early ⇒ **report and stop**, do not reach past.

### ⚠ `MX1` vs the variable-model FREEZE — **you CONSUME, you do not CHANGE**

⭐ `MX1` is allowed **because it only consumes the existing debug-session seam** and adds routes under
`DebugApi/`. ⛔ If it turns out to need a change to `IBlueprintDebugSession` / `DataBreakpointManager` /
anything in `Hrot.Editor.AiShared` or the variable model — ⭐⭐ **STOP and report** *(`R-128`)*.

## 3. 🔴🔴 `HN-001` IN FULL — **the crash you are fixing** *(do not re-derive this)*

⭐⭐ **Repro — three HTTP calls, no test code:**

```bash
POST /scenario/load {"name":"hill-attack","waitForReady":true}
POST /preview/enter {"startPaused":true}
POST /preview/exit          #  ⇒ the editor process ABORTS (SIGABRT, exit 134)
```

⭐ **Reproduced identically on all three curated scenarios** *(`hill-attack`, `test-fire`, `test-move`)*
⇒ **not scenario-specific.**

📐 **What the editor prints, in order:**
```
[Preview] UnloadingPreview: live repo rewound to snapshot.
FATAL: Entity Entity(1, v1) GetManagedComponentRO<InitialTargetsIntent> returned null, but Has=True. Idx=1
Unhandled exception. System.InvalidOperationException: Entity Entity(1, v1) missing component InitialTargetsIntent
   at Fdp.Core.EntityRepository.…GetManagedComponentRO[T]   (EntityRepository.View.cs:68)
   at Hrot.SimHost.Systems.GenesisMaterializationSystem.MaterializeTargets (…:211)
   at …ModuleHostKernel.Update → EditorSubsystem.Update:1841
```

⭐⭐⭐ **Mechanism:** the preview rewind restores the managed component's **PRESENCE** *(the query still
yields the entity; `HasManagedComponent` is true)* **without its managed PAYLOAD**
*(`GetManagedComponentRO` returns null)*. The next tick's genesis pass queries
`WithManaged<InitialTargetsIntent>()` and dereferences the null.

| ⚠ | |
|---|---|
| ⛔ **Blast radius is wider than preview** | `POST /recording/stop` → `FinishRecordingStop` → `ExitPreviewMode` ⇒ **the whole record→replay round trip dies the same way** |
| ⚠⚠ **Likely a REGRESSION — and the date is the lead** | `docs/MCP_Integration.md` records that exact cycle being driven end to end on **`2026-08-22`**, writing a 48-frame `.fdp` through `/recording/stop`. ⇒ **it broke AFTER that verification.** ⛔ Git history could not date it — the implicated files all trace to one squashed import commit (`877fc7c74`) |
| ⭐ **the handoff leaves the CHOICE open** | restore the payload in the rewind, **or** make the genesis pass null-safe — *"whichever the measurement says is correct"* |

## 4. ✅ WHAT THIS LANE ALREADY BUILT *(all merged into the coordinator line)*

| batch | what |
|---|---|
| **HN-120 phase 1** | **`Hrot/Runner/Hrot.SystemTests/`** — boots the REAL editor headless under Xvfb and drives it over the AI-debug API. `EditorProcessFixture` · `McpClient`+`ApiResult` · `SystemTestBase` · `CapabilitySmokeTests` · `ScenarioBehaviorTests` · `KnownDefectRails` · `SystemTestEnvironment`, + `scripts/run-system-tests.sh` + `.github/workflows/system-tests.yml` *(manual-trigger; the repo's first workflow)* |
| **HN-120 phase 2** | **MCP extensions slice ①** — `GET /behaviors` · `GET /breakpoint-types` · `DtoJsonSchemaExtractor` *(shared)* · `Hint` on the envelope + `DebugApiHints` *(15 failures back-filled)* · Node wrappers *(49 → 51 tools)* |

📄 Designs *(carry the AS-BUILT truth — read before quoting a seam)*:
`docs/DESIGN_MCP_System_Test_Harness.md` **§9** · `docs/MCP_Integration.md` **§"AS-BUILT — SLICE ①"**.
📄 Report: `docs/blueprints/batches/BATCH_HN120_The_Harness_That_Found_A_Crash.md`.

## 5. ⛔⛔ GATE BASELINES — **measured on the MERGED tree at `2d95c419f`; do NOT re-derive**

| suite | baseline | ⚠ |
|---|---|---|
| `IOS-IG-SimHost.sln` | **0 errors**, ~64 warnings | the warnings are pre-existing `BP3010` orphan-node ones from `Hrot.AI.Behaviors` |
| ⭐⭐ **`Hrot.SystemTests`** *(Row 8 — THE integration gate)* | **27 passed · 0 failed · 2 skipped**, ~13 s | ⭐ **both skips are `HN-001`** and what it blocks ⇒ **fixing it should take them to 0** |
| `Hrot.Editor.Tests` | **209 / 0** | ⭐ improved from an older 207/2 baseline — the coordinator fixed the 2 `ScenarioMenuTests` |
| `Hrot.Blueprints.Tests` `~Hrot.Blueprints.Tests.Editor` | **1032 / 0**, 9 skipped | ⭐ **THE gate for anything touching `EditorSubsystem`** |
| `ClusterRunner.Integration` `~TimeControlIntegrationTests` | **9 / 0** | the cross-node invariant |
| ⚠ `tools/ai-debug-mcp` `node verify.mjs` | ⛔ **FAILS** *(`MCP error -32000: Connection closed`)* | ⭐⭐ **PRE-EXISTING — proved by stash** *(fails identically at clean HEAD)*. ⚠ Needs `npm install` first; `node_modules` is gitignored and was never installed |
| `node src/index.mjs` | ⭐ **starts clean, 51 tools** | the usable Node gate |

⚠⚠ **`tracker-counts.py --check` counts only `**BP-` rows** ⇒ **your `HN-`/`MX-` rows are invisible to
it.** Its OK is **not** evidence about your rows.
⚠ `rulings-check.py` emits **1 staleness WARN on `.claude/CLAUDE.md`** — **pre-existing**, arrived with a
coordinator merge.

## 6. ⛔⛔⛔ THE TRAPS — **each cost real time in this lane**

| # | trap | the habit that fixes it |
|---|---|---|
| **①** | ⛔⛔ **`--no-build` runs a STALE dll and prints PASSED.** Hit twice in the Stride batch | ⭐ **build the SOLUTION before a gate run** |
| **②** | ⛔⛔ **Reporting your OWN bug as a product defect.** Three harness cases failed on `Invalid patch value … expected Vector3` + a 404; probing the API directly showed the write **succeeds** with `{X,Y,Z}` — my shape was wrong, and the 404 was my own reload churn | ⭐⭐ **probe the API by hand before filing a finding.** *(The surviving real asymmetry became `HN-002`.)* |
| **③** | 🔴 **`xvfb-run` LEAKS a display server per run** — it stops Xvfb from an **EXIT trap**, and `Process.Kill` sends **SIGKILL**, so the trap never runs | ⭐ the fixture now owns `Xvfb` directly. ⛔ **Do not "simplify" it back to `xvfb-run`** |
| **④** | 🔴 **An armed breakpoint outlives its case, and its EFFECT outlives the breakpoint.** A `Lifecycle` breakpoint on `"Bradley"` *(hill-attack really has an M2 Bradley IFV)* tripped during a LATER case's scenario load, paused the sim, and the cluster never reached `OperatingEdit` ⇒ a **504** in an unrelated case | ⭐ `ResetToIdleAsync` clears breakpoints; discovery cases target names that match nothing |
| **⑤** | ⚠ **The shared editor is loaded ONCE** — reloading rebuilds entities with fresh network ids, so listing during another case's reload yields a dropped id | ⭐ `LoadAndPreviewAsync` is idempotent on scenario name |
| **⑥** | ⚠ **A new field on a shared envelope COLLIDES with what the client already spreads.** The Node `toolError` overwrote the server's `hint` with its own static usage string | ⭐ server pointer = `hint`, catalog usage = `usage` |
| **⑦** | ⚠ **`--` inside an XML comment breaks msbuild**; `dotnet` is not on `PATH` | trivial, but each cost a cycle |

## 7. ⭐ HOW TO RUN THE HARNESS *(your own instrument)*

```bash
bash scripts/run-system-tests.sh                  # the whole SystemSmoke suite (~15 s + build)
bash scripts/run-system-tests.sh Playing_hill     # filter by name
```
⭐ On failure it dumps the editor's own log — **`/tmp/hrot-systemtests-editor-<port>.log`**, which is
where the answer usually is. ⭐ Xvfb + GL are present in this container, so the suite really runs here.

## 8. ⭐ STANDING RULES THAT BIND YOU

- ⭐⭐⭐ **Read `docs/blueprints/RULINGS.md` in full at session start** *(RULE ZERO)*, then
  `python3 scripts/design-digest.py` and `python3 scripts/rulings-check.py`.
- ⭐⭐ **Intent lives in the DESIGN docs, not the code** *(`R-129`)* — `docs/` first, then `.dev/`.
- ⭐⭐ **Enumerate with `search_graph` before any exhaustive claim**; grep only confirms. ⚠ **This lane
  has already been bitten twice by same-named types** — three `IMissionEditorService`, two
  `EditorAiTracerCoordinator`.
- ⭐⭐ **Fold as-built deviations back into the OWNING DESIGN before the batch closes** *(obligation ⑤)* —
  the batch report is ephemeral; the design is not.
- ⭐ **Diagrams live in DESIGNS, never in batches**; validate with
  `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs <file>`.
- ⭐ **Ask in plain prose, never the multiple-choice widget.** ⭐⭐ **Always give GitHub links** —
  `https://github.com/pjanec/HROT/blob/claude/time-system-refactor-batch-104-gp617x/<path>` — the user
  is often on mobile.
- ⭐ **Report gates as a table**: verbatim command · pass/fail/skip · delta vs base `5843055e7` · every
  red confirmed pre-existing **by name**.
