<!--STATUS
state: LIVE
updated: 2026-08-23
current-answer: this WHOLE file. It is the self-contained entry point for the coordinator session
  continuing the MCP / observability / testing-harness programme. Point a fresh coordinator session here.
stale-below: nothing. For deep feature history (Batches 22-103, the variable-model programme) see
  RESUME_START_HERE.md — but do NOT quote its "next steps" as current; this file supersedes them for status.
supersedes-for-status: RESUME_START_HERE.md (older top block, ends 2026-08-18) · RESUME_Coordinator.md (historical log)
-->

# ⭐⭐⭐ RESUME — Coordinator, the TESTING-HARNESS / OBSERVABILITY / MCP programme

> ## ⭐⭐⭐ READ THE PROGRAMME CHARTER FIRST — [`PROGRAMME_Unification_And_Harness.md`](../PROGRAMME_Unification_And_Harness.md)
> ⭐⭐ **It says WHAT we are doing and in WHICH ORDER, in the user's own framing** *(`2026-08-23`)*, and it
> carries the decisions `D1`–`D5`. ⛔ **This file is the coordinator's operating detail; the charter is the
> goal.** ⚠ Where they disagree, **the charter wins** — and §3 of it supersedes §3 below.
>
> ## ⭐⭐⭐ THEN — `RELEARN` before you touch anything
> ⛔ **Ground yourself in the design canon before acting.** In order:
> 1. Read [`RULINGS.md`](RULINGS.md) **in full** *(it is short — lean on purpose)*.
> 2. Read [`.claude/CLAUDE.md`](../../.claude/CLAUDE.md) — the two-session protocol + process rules.
> 3. `bash scripts/session-design-brief.sh` · `python3 scripts/rulings-check.py` · `python3 scripts/design-digest.py`
> 4. ⭐⭐ **Your FIRST reply OPENS with the `DESIGN BRIEF` block, then answers what was asked, same reply.**

---

## 0. WHO YOU ARE · WHERE THINGS STAND

| | |
|---|---|
| **You are** | the **COORDINATOR** — you own design, handoffs, verification, merges. ⛔ **You do NOT write feature code** |
| **Your branch** *(push here, always)* | ⭐ **`claude/blueprint-authoring-status-6sr5ld`** *(user ruling `2026-08-23`: the coordinator's native branch. ⚠ `…-gm0akp` is an ANCESTOR of it — identical content, no divergence; it is history, not a second head)* |
| **HEAD** | ⭐ **`80a98f627`** — ⛔ **not `cb0d2dc2e`**, which this row claimed while §4 already contradicted it. 📐 Verified `2026-08-23`: solution builds **0 errors / 12 warnings** *(all NuGet `NU1902/1903` advisories on MessagePack 3.1.4 in `Fdp.Core.Tests`; **zero compiler warnings**)*, `rulings-check` 23/23, `design-digest --check` clean, `tracker-counts --check` OK *(open 99 / done 323)* |
| **Both implementation lanes** | ⭐ Verified idle by ANCESTRY at `80a98f627` *(⛔ not by claim — both lane heads are ancestors)*. ⚠ **Batch A is now DISPATCHED to the MCP lane** — see §3 |

### The two implementation lanes *(from `.claude/CLAUDE.md`, still binding)*

| Lane | Branch | id prefix · tracker area |
|---|---|---|
| ⭐ **UI / VARIABLE lane** *(the frozen variable-model area)* | `claude/hrot-implementation-j1jvin` | **`BP-`** · areas A–G |
| ⭐ **TIME lane** *(approved 2026-08-21)* | `claude/time-system-refactor-batch-104-gp617x` | **`TM-`/`ST-`/`HN-`/`MX-`** · **Area H only** |

⛔ **Locate their branch by ancestry, not by name** — they have moved before. Any `claude/*` branch whose
first commit's parent is one of yours is a live lane.

### Standing constraints — ⛔ do NOT re-derive, they have force

| ⛔ | |
|---|---|
| **No PR** unless the user explicitly asks | |
| **Never** put a model identifier in a commit message, PR body, code comment, or any pushed artefact | chat replies only |
| Push only to `claude/blueprint-authoring-status-gm0akp` via `git push -u origin <branch>`; **`git pull --rebase` first** | the user's own Windows visual-check session also pushes here *(not a lane violation)* |
| Ask in **plain prose**, never the multiple-choice widget | |
| ⭐ **GitHub links belong in CHAT, ⛔ almost never in a persisted document** *(user ruling `2026-08-23`)* | the user reads on mobile, so **give absolute links in the conversation** — `https://github.com/pjanec/HROT/blob/<branch>/<path>`, push first or it 404s. ⛔ **In docs use repo-relative paths**: a branch-encoded URL rots the moment the branch does |
| **Validate every Mermaid block before pushing** | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs <file>` |
| **Rule 8 — the coordinator does NOT re-run the impl session's gates** | the report substitutes; spot-verify only SURPRISING claims. ⭐ **BUT after a cross-branch merge, DO build the combined state** *(a state neither session tested)* |
| Commit footer | ⭐ use **your own session's** `Co-Authored-By:` + `Claude-Session:` trailers, exactly as your harness states them. ⛔ Do not copy a previous session's — the attribution would be false |

---

## 1. THE PROGRAMME — the pixel-free test/monitor stack

⭐⭐ **The goal:** a machine-readable "what the user sees" layer so we can verify cross-host behaviour
**without pixels**, because the UX programme unifies implementation across hosts *(editor · CGF · SimHost
· Orchestrator)* and much of what must stay identical is visual. `R-21` keeps human visual checks
SUSPENDED; this stack is the sanctioned substitute *(model-diff, not pixels)*.

**Three pieces, all built and merged:**

| piece | what | where |
|---|---|---|
| ⭐ **PanelSnapshot** *(approach C)* | every panel builds a whole view-model each frame, **renders ONLY from it** *(the load-bearing invariant)*, and `Register`s to a process-wide static singleton when capture is on. Tests read the singleton. No ImGui Test Engine, no pixels | `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Panels/` |
| ⭐ **MCP / AI-debug API** | a loopback `HttpListener` (`DebugApiHost`) wired in `EditorSubsystem`; a typed C# `McpClient` drives it over HTTP; the Node MCP server is the agent-facing driver | `Hrot/Subsystems/Hrot.Editor/DebugApi/` |
| ⭐ **The harness** | boots the **real process as a subprocess** *(headless, Xvfb on Linux)* and drives it over HTTP with the C# `McpClient` | `Hrot/Runner/Hrot.SystemTests/` · `Hrot/Runner/Hrot.Smoke.Tests/` |

### ⭐⭐ The one-binary fact — internalise it, it reshapes the conformance work

⛔ **There is ONE binary — `Hrot.ClusterRunner` — and `--mode` selects the subsystem(s).** The editor is
**not** a separate executable; it is the cluster runner hosting the **editor** subsystem.
- `--mode editor` → the editor subsystem *(fixture built, HN-120)*
- ⭐ **`--mode all`** → **`orchestrator,simhost,ig,excon,cgf` — FIVE subsystems in ONE process** *(⚠ CORRECTED
  `2026-08-23`: there is **no `--mode cluster`** — it throws)*; to snapshot a submodule's panels you
  **switch perspective** *(`PerspectiveCoordinatorSystem`)*.

⭐⭐ **Perspective-scoped capture:** a panel registers to `PanelSnapshot` only when its DRAW runs, and only
the ACTIVE perspective draws *(measured ~11 of 47 captured at once)*. Protocol: `POST /perspective {name}`
→ step ≥1 frame → `GET /panels/{id}`.

---

## 2. THE DESIGN CANON FOR THIS PROGRAMME — read before dispatching

| doc | holds | link |
|---|---|---|
| ⭐⭐⭐ **`DESIGN_Headless_Testability.md`** | the umbrella taxonomy + architecture *(one-binary/`--mode`, perspective-scoped capture, conformance = two modes diff by PanelKind)*. **Sequencing steps 5/6/7 are the pending work** | [`DESIGN_Headless_Testability.md`](../../docs/DESIGN_Headless_Testability.md) |
| ⭐⭐⭐ **`DESIGN_UI_Observability_Snapshot.md`** | the PanelSnapshot ADR *(approach C)*, `build-state: BUILT`. The `IPanelViewModel` / `PanelSnapshot` contract | [`DESIGN_UI_Observability_Snapshot.md`](../../docs/DESIGN_UI_Observability_Snapshot.md) |
| ⭐⭐⭐ **`TESTING_Harness_And_Goldens.md`** | ⭐ **the RUNBOOK handoffs CITE** — how the harness drives over HTTP, the smoke-test shape, the perspective protocol, golden creation/maintenance *(`PANEL_GOLDEN_CAPTURE`)*, conformance, and §6 the per-batch obligation | [`TESTING_Harness_And_Goldens.md`](../../docs/TESTING_Harness_And_Goldens.md) |
| ⭐⭐ **`MCP_Integration.md`** | the MCP extensions design — Groups O–T *(variable addressing · mission editing · hot-attach · entity-state · breakpoint-type discovery · panel-snapshot read)*, tasks MX1–MX9, the self-describing-error map. Carries AS-BUILT sections + `known-rot` | [`MCP_Integration.md`](../../docs/MCP_Integration.md) |
| ⭐ **`DESIGN_MCP_System_Test_Harness.md`** | the harness itself *(fixture, McpClient, capability ladder)* | [`DESIGN_MCP_System_Test_Harness.md`](../../docs/DESIGN_MCP_System_Test_Harness.md) |

---

## 3. ⛔ SUPERSEDED BY THE CHARTER'S ORDER — Batch A was WITHDRAWN

> ⛔⛔ **Batch A was WITHDRAWN before the lane started it** *(`2026-08-23`)* — its premise *(the two modes'
> perspectives are permanently disjoint)* was wrong, and its ordering put cross-host conformance before the
> editor-mode regression net. ⭐ **The running order is now the charter's §3: Stride → perspective naming +
> absent-capability tolerance → harness → baseline/goldens → decide what to port.**
> 📄 The withdrawn dispatch, with its ten still-valid measured findings:
> [`HANDOFF_Batch_A_Conformance_Prerequisites.md`](batches/HANDOFF_Batch_A_Conformance_Prerequisites.md).

### ⛔ HISTORY — the pre-charter framing

⭐ **User chose A first (`2026-08-23`).** 📄 **Dispatched at `80a98f627`:**
[`HANDOFF_Batch_A_Conformance_Prerequisites.md`](batches/HANDOFF_Batch_A_Conformance_Prerequisites.md)
— ⛔ **never amend it** *(rule 1)*; new findings go in the NEXT handoff.

⚠⚠ **The pre-dispatch review changed Batch A's shape — the table below is the ORIGINAL framing, kept for
the record. The handoff's §1 supersedes it**, on ten measured points. The three that matter most:
⛔ **`--mode cluster` does not exist** *(it is `--mode all`, five subsystems)*; ⛔ **item 2 is FOUR wiring
points, not one** *(and the missing per-frame `DrainAll` would hang every route)*; ⭐⭐ **item 4's frame
boundary is a PREREQUISITE of item 3, not a trailing extra** — without it the diff invents divergence.

### Batch A — CONFORMANCE *(the prerequisite chain)*

| # | item | design basis |
|---|---|---|
| **1** | `GET /perspectives` *(list)* + `POST /perspective {name}` *(switch)* on the DebugApi | `TESTING_Harness_And_Goldens.md` §3 · `DESIGN_Headless_Testability.md` step 5 |
| **2** | ⭐⭐ **Lift `DebugApiHost` one level up** — from `EditorSubsystem` to the `ClusterRunner` host *(mode-independent)* so `/panels` + `/perspective` answer in **`--mode all`** too. ⛔ **NOT a second editor-only endpoint** | `TESTING_Harness_And_Goldens.md` §1 *("wire the existing DebugApiHost one level up")* · step 6 |
| **3** | `ClusterRunnerFixture(mode)` + the differential conformance suite — same scenario, two modes, **diff by `PanelKind`** *(reference IS the other mode's live dump; no golden to maintain)* | `TESTING_Harness_And_Goldens.md` §5 · step 7 |
| **4** | ⭐ **`BP-487` — wire `ClearCaptured` + the gizmo publish across the FOUR gizmo hosts** *(IG · CGF · ReplayBrowser · SimHost)*, not just `EditorSubsystem`. ⚠ **Not a defect today** *(the debug API is Editor-only; a host that never clears keeps the latest-wins default)* — ⛔ **it becomes one the moment conformance drives two hosts**, so it belongs with whoever owns cross-host *(this lane)*. ⭐ **BP-485's address/kind split is what makes this wiring safe** | `MCP_Integration.md` *(Group T)* · filed by UI lane, BP-485 commit |

### Batch B — MIGRATION / CLEANUP

| # | item | design basis |
|---|---|---|
| **M1** | convert the 6 JSON `e2e_*.json` scripts to the MCP harness; retire the JSON `TestScript` engine's e2e role | user ruling 2026-08-23 *("convert/reimplement existing json driven e2e stuff to the new harness")* |
| **M2** | **fix or JUSTIFY-REMOVE** the crash-roots — `BP-378` *(`Hrot.ClusterRunner.Integration.Tests`, `MAX_ENTITIES=1M` OOM at `EntityRepository..ctor`)* and `BP-419` *(`Fdp.Presentation.Tests`)* — ⛔ **never a permanent filter-around** | ⭐ **`R-131`** |
| **M3** | retire `--mode ci` **once the harness is proven** — keep only if it is faster or brings something the harness cannot | user ruling 2026-08-23 |

⭐ **The harness's subprocess model structurally fixes the un-gateable integration suites** *(each scenario
is its own process, so BP-378's per-repo OOM no longer accumulates)* — that is the housekeeping insight
behind Batch B.

---

## 4. PARKED DECISIONS — no action pending from you unless the user reopens

| | |
|---|---|
| ⭐ **Stride port** | parked at `claude/stride-port` @ `b9ab83b0e`. ⭐ **User visually verified it, cleared to integrate "on your word."** Leave parked; do NOT merge unasked |
| **BP-399 tail** | Q49 *(subtree-sync identity — option **C**, recompute at load)* + Q50 *(master-blackboard slice — option **A**, declare)* both **resolved + built**. The `L0–L6` view-switching tail *(`DESIGN_Details_Panel_View_Switching.md`)* is the UI lane's, parked by the user |
| **Time-lane MX-011** | switch `GET /panels/_gizmo` to read the snapshot entry *(U-obs-3 now registers the gizmo buffer into PanelSnapshot)* — a small follow-up, not urgent |
| **Two open architect sub-questions** | Q49 missing-subtree-asset-at-load *(lean: diagnostic row)* · Q50 postponed Category-2 generated-callee limit — the user's call, no rush |

✅ **Closed since first draft** — the carried **Details-table** gap is GONE: `BP-484` *(UI lane, merged `b1082417c`)* now publishes the Details table's rows through the same `VariableTablePanelViewModel` as the Watch, so **T2-via-snapshot covers the Details table too**. Rail `BothPanelsAgreeThroughTheSnapshot`.

---

## 5. VERIFICATION HABITS — the gate contract, condensed

⭐ **On a returned batch:** `--ff-only` merge → **build the combined state** *(rule 8's one exception)* →
read the diff → spot-verify only surprising claims → doc gates *(below)*.

```bash
python3 scripts/tracker-counts.py --check     # tracker reconciles
python3 scripts/rulings-check.py              # every ruling quote still verbatim in its source
python3 scripts/design-digest.py --check      # buildable design carries classDiagram + sequenceDiagram; INVENTORY present
MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs <file>   # every mermaid block parses
```

⭐ The impl session's §Gates report **substitutes for re-running their gates** *(rule 8)* — it must carry:
one row per gate *(command · counts · delta)* · a `--no-build` column · golden movement as a **diff shape**
· every RED confirmed pre-existing against a named base sha · working tree clean after each suite · both
quarantine counts · `tracker-counts.py --check` + every id allocated · ⛔ **and for a cross-cutting change,
a row for the INTEGRATION suite that exercises its invariant** *(named, run, or justified un-gateable)*.

---

## 6. NEW RULING THIS PROGRAMME

**`R-131`** *(in `RULINGS.md`)* — ⛔ **A CRASHING / UN-GATEABLE TEST IS A DEFECT TO RESOLVE** — analyse,
fix, or JUSTIFY its removal; never a permanent filter-around. *(user, 2026-08-23.)* This binds Batch B/M2.
