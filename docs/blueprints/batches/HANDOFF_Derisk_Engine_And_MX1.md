<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-22
current-answer: dispatch pointer for the TIME lane — the engine/API de-risk work that needs NO U-obs-1:
  fix HN-001 (the preview-rewind crash the harness caught) + the cheap wiring fixes + MX1 (Group O variable
  addressing, which finishes the harness's owed watch case). STOP before Group T (that needs U-obs-1).
known-conflict: none.
-->
# HANDOFF — TIME lane · **HN-001 + wiring fixes + MX1 (up to the `U-obs-1` wall)**

> 📌 **Dispatched at `5843055e7`** *(the joined commit — your HN-120 harness + slice-1 extensions are now
> merged into the coordinator line; solution builds 0 errors)*. ⭐ Branch **fresh from the coordinator branch**
> *(rule 7)*; **rule 1b: started-marker FIRST.** ⛔ **Scope FROZEN at this sha.** ⭐ ids **`HN-`/`MX-`**, tracker
> **Area J** *(your existing area)*. ⭐ **Run freely to the `U-obs-1` wall** *(§4)* — everything here is
> independent of the UI lane's panel work.

## 0. ⭐ CONTEXT — you built the harness; now fix what it caught, and finish its owed case

📄 The findings are your own tracker rows *(Area J)*: **`HN-001`…`HN-005`**, **`MX-001`…`MX-003`**. The
umbrella **[`DESIGN_Headless_Testability.md`](../../DESIGN_Headless_Testability.md)** is the map; **MX1** is
**[`MCP_Integration.md`](../../MCP_Integration.md) Group O** *(slice ②)*.

## 1. ⭐⭐ THE TASKS

| # | task | ref | gate |
|---|---|---|---|
| **HN-001** | 🔴🔴 **fix the preview-rewind crash** — `POST /preview/exit` aborts the editor: the rewind restores a managed component's **presence bit without its payload**, and `GenesisMaterializationSystem.MaterializeTargets` dereferences the null on the next tick *(SIGABRT)*. ⭐ Engine snapshot/restore semantics — restore the payload, or make the genesis pass null-safe, whichever the measurement says is correct. ⛔ **Also fixes record→replay** *(`/recording/stop` → `ExitPreviewMode` dies the same way)*. ⭐ **Un-skip `KnownDefectRails.Exiting_preview_does_not_abort_the_editor`** | tracker `HN-001` | the rail goes green; the harness's 2 skips → 0 for this cause |
| **HN-002** | ⚠ **`Vector3` read/write shape asymmetry** — the dump emits `[x,y,z]`, the StructEdit patch parser wants `{X,Y,Z}` ⇒ read-modify-write round-trips fail. ⭐ Pick one shape and make both agree *(lean: accept the array form in the patch parser, since the dump's array is the natural read)* | tracker `HN-002` | a harness round-trip: read a component, write it back unchanged, succeeds |
| **HN-003** | ⚠ **`POST /shutdown` is inert** — `EditorSubsystem:1600`-area passes `() => { }`. ⭐ Pass the orchestrator's real stop | tracker `HN-003` | `/shutdown` actually stops the process |
| **HN-004** | ⚠ **route the `FATAL:` stdout leak** — `EntityRepository.View.cs:64` `Console.WriteLine`s from `Fdp.Core`. ⛔ **Route through the logger, do NOT delete** *(it is what made HN-001 diagnosable)* | tracker `HN-004` | no raw `FATAL:` on stdout; the message survives via the logger |
| **MX1** | ⭐⭐ **Group O — variable addressing** *(slice ②)*: `GET /entities/{id}/variables?asset=` · `GET /entities/{id}/variable?asset=&path=` *(value + pending)* · `POST /entities/{id}/variable` `{asset,path,value}` *(stage)*. ⭐ **REUSE the staged-write seam DebugApiService already holds** — `_blueprintSession` *(`IBlueprintDebugSession.ResolveWorkingStateField`)* → `StageFieldMutation`/`TryGetPending`; value encoding via `ScenarioSerializer` *(decision 3)*. ⛔ **This finishes `HN-005`** — the harness's owed "watch read + write a variable" case | `MCP_Integration.md` Group O; tracker `MX-001` shape | +MX6 smoke: stage a variable, read the pending, advance, read the applied value; the owed H4 case lands |

⭐ **`MX1` also carries its slice-② companions if cheap and in-scope:** `MX5` Node wrappers + `SKILL.md` for the
new variable tools, `MX6` the smoke case above. ⛔ **`MX2`/`MX3` (Q/R) are NOT required here** — take them only
if `MX1` leaves budget; otherwise a follow-up.

## 2. ⚠ MX1 & THE VARIABLE-MODEL FREEZE — you CONSUME, you do not CHANGE

⛔ **The variable model + its UI are the UI lane's frozen area.** ⭐ **MX1 is fine because it only CONSUMES the
existing debug-session seam** *(`_blueprintSession`, already injected into `DebugApiService`)* and adds HTTP
routes in `DebugApi/` — ⛔ **it does NOT touch the variable model, the panels, or `Hrot.Editor.AiShared`.**
⚠ **If MX1 turns out to need a change to `IBlueprintDebugSession`/`DataBreakpointManager` or any AiShared/
variable file — STOP and report** *(that is a cross-lane edit, `R-128`)*; it should not.

## 3. ⛔ LANE & NOT-THIS-BATCH

⭐ **Your surface:** `Hrot.Editor/DebugApi/*`, the MCP wiring in `EditorSubsystem`, the engine/kernel for HN-001
*(`GenesisMaterializationSystem`, `EntityRepository`, the preview snapshot/restore)*, `tools/ai-debug-mcp/`,
the `Hrot.SystemTests` harness *(un-skip rails, add MX6 cases)*.
⛔ **Do NOT touch:** the UI lane's panels / `PanelSnapshot` / `Hrot.Editor.AiShared` / the variable model, the
parked Stride tree.

## 4. ⛔⛔ THE WALL — STOP at Group T; it needs `U-obs-1`

⛔ **`GET /panels` (Group T / `MX9`) reads the `PanelSnapshot` singleton, which the UI lane is building in
`U-obs-1`.** ⇒ ⛔ **do NOT build Group T in this batch.** ⭐ When the UI lane pushes its `U-obs-1` checkpoint and
it merges, a **follow-up handoff** adds Group T + the CGF/SimHost read-API + the cross-host conformance suite.
⚠ If you finish HN-001+MX1 with budget to spare, **report and stop** rather than reaching past the wall.

## 5. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · delta vs base
`5843055e7` · `--no-build` column · every RED confirmed pre-existing · goldens as a diff shape ·
`tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · the **`HN-`/`MX-` ids you
allocated** · `R-106` verdicts. ⭐⭐ **Row 8 — the integration invariant:** the **harness smoke suite** *(now
including the un-skipped HN-001 rail + the MX1 watch case)*, headless under Xvfb, green — report it with the
`--filter` and Xvfb launch. ⭐ Rule 4/7: re-sync + pull the coordinator branch around the batch. ⭐ Rule 1b:
started-marker before code.
