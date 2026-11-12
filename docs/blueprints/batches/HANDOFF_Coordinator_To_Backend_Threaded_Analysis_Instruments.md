<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Threaded_Simulation_Presentation.md — OWNS this: ⭐ §13 the shared-state analysis (13.1 world singletons, 13.2 side tables, 13.4–13.5 gizmos and the presentation inside the tick, 13.6 state outside the world, 13.7 the verdict and the unknowns this batch answers) · §12 (on exp/threaded-kernel) the experiment's shape
  - STATUS_Threaded_Kernel_Experiment.md (on exp/threaded-kernel) — the previous experiment; its PublishProbe and ThreadedKernelGate are the starting point
-->

# HANDOFF — coordinator → backend: an OVERNIGHT ANALYSIS — build the INSTRUMENTS that tell us the right threading solution (not the solution)

**For:** a backend session (any lane that runs it overnight). **From:** coordinator. **Dispatched at `25bce1e00`**, scope frozen there.
🔒 User, `2026-11-12`: *"let's try analyzing the rest of unknowns. So not building some solution, but building something that tells us what the
right solution is."* · *"It's no point trying to develop POC without knowing how."*

⛔⛔ **Autonomous — never stop for a question** (R-4156). Decide, record with a claim table in your status file, continue. A failed premise stops
THAT item only (R-106). ⛔ **Build NO part of the threaded solution** — every instrument is opt-in (an env switch, default OFF) and changes no
behaviour. The deliverable is EVIDENCE: tables with counts and `file:line`.

## Where to work

⭐ A NEW branch **`exp/threaded-analysis`** from `exp/threaded-kernel` (it carries `PublishProbe`, `ThreadedKernelGate`, the published gizmo
buffer), with `coordinator` merged in (it carries §13). Push as you go. On your lane push only `chore: started …` and the status file
`STATUS_Threaded_Analysis_Instruments.md` (with the branch sha). ⛔ Nothing merges into a lane without the user's word. The cloud VM is enough
(correctness of the analysis, not speed); the editor runs hidden (`HROT_HIDDEN_WINDOW=1`, `xvfb-run -a`).

## The situation — read §13 whole

The analysis (§13) classified every shared object found by reading code and found **no blocker** — but it rests on GREP over named folders, and
`check_index_coverage` is not in the CLI. ⇒ ⛔ **we do not know what it MISSED.** Its main finding: the editor's node frame already runs
presentation work (26 gizmo tools, tool activation, the camera, selection, four `afterKernel` objects — §13.5), so the first step of any
threaded build is to take it out. The unknowns (13.7): what is missed; what each panel / layer / 3-D view actually READS (the per-reader masks
the user's "copy only what the UI needs" idea requires); the per-tick costs of the treatments; four objects never traced
(`TimelineLaneCatalog`, `OrchestratorCore.ScenarioPanel`, `GizmoUiStateHub`, the mission service's async acks); the 170 static collections.

## The instruments — in this order, as far as the night goes

| # | instrument | what it must tell us | done when |
|---|---|---|---|
| **A-1** | ⭐⭐ **a RUNTIME ACCESS TRACER** (`HROT_ACCESS_TRACE=1`): every access to the world — component table read / write by type, singleton get / set by type, entity create / destroy, bus publish — tagged with ① the CONTEXT (inside the node frame / tick, or the presentation part of the frame: ImGui panels, map layers' `Draw`, the 3-D view, input, the editor logic) and ② the READER (a scope name per panel, layer, gizmo rule, tool, system — a cheap scope stack). Aggregated per run, written to a JSON file | ① **the conflict set**: everything the tick writes that the presentation reads or writes (and the reverse); ② **the per-reader read set** — per panel / layer / 3-D layer / gizmo: component types and singletons read (= the masks); ③ **the diff against §13**: anything the tracer sees that §13 does not list is a FINDING | the tracer runs on the 1 000-man rig through a SCRIPTED session (below) and its three tables are in the status file |
| **A-2** | ⭐⭐ **a STATIC reachability analysis** (Roslyn — `/opt/roslynmcp/RoslynMcp.dll` over stdio per CLAUDE.md ②a, or a small Roslyn console tool on the root solution): from the SIMULATION roots (kernel systems' `Execute`, the `afterKernel` hooks, load steps, the orchestrator core) and the PRESENTATION roots (panels' draw, layers' `Draw`, the 3-D view, input, ImGui windows), the fields (instance + static) each side WRITES and READS; interface dispatch over-approximated (all implementations) | the objects OUTSIDE the world written by one side and read by the other — complete where A-1 only sees what the script exercised; resolve the four untraced objects and the 170 statics; compare with §13.6 | the intersection table (object · field · sim writer · UI reader · `file:line`) in the status file; every row not in §13.6 named |
| **A-3** | **COSTS of the treatments** — extend `PublishProbe` at 1 000 men (and 3 000 if the rig allows): the masked copy with A-1's masks (map-wide whole tables + 3-D; panel rows for 1 / 10 selected units — the row copy may be estimated from per-row size if no row copy exists); `NetworkEntityMap.RebuildFromWorld` on a copy (full and incremental); building an immutable `ClusterUiCache` view; the traffic lists (if traffic runs on the rig, else n/a); `GizmoRuleTimings` per rule split into §13.4's overlays vs tools | ms, bytes, allocations per tick for each treatment | the cost table in the status file |
| **A-4** | **the "presentation out of the tick" dependencies** — for each §13.5 item (the 26 tool gizmos, the 6 mixed, tool activation, centre-on-entity, the selection systems, the four `afterKernel` objects): what it relies on by running INSIDE the frame (phase order, the same-frame gizmo buffer the pickers hit-test, CE-259r; a world write it makes; a bus it reads after the swap) | a row per item: the dependency, `file:line`, and whether moving it to the presentation side breaks it | the table in the status file |
| **A-5** | **the verdict** — §13's treatments confirmed or changed by the evidence; the per-reader mask table; the costs; every finding §13 missed; what is STILL unknown | a short section at the top of the status file; ⭐ and fold the confirmed facts into §13 of the design (as-built rule: the design must carry what was learned, marked with the date) | |

**The scripted session for A-1** (through the debug API / MCP or a small driver — your choice): load the 1 000-man rig, play 60 s, pause, step;
select one unit then ten; open each panel the editor offers for a unit (details, brain, journal, decision log, orders, ORBAT, watch …) and the
Entities / Scenario / Cluster windows; switch the 3-D view on; drag a unit, rotate one, place one, delete one, undo; open the right-click menu;
run a hot reload of a behaviour if the rig allows; save. ⚠ Say which panels the script could not reach — an unexercised panel is an UNKNOWN,
never "reads nothing".

## Rules

- ⛔ **Instruments only.** No threading, no copies, no commands built. The tracer's cost when OFF must be ~0 (a static bool check); when ON it
  may be slow.
- **Measure before claiming** (CLAUDE.md): every "nothing reads X" from the tracer AND the static analysis; say what each could not see.
- **0 B per steady frame with the switches OFF** (R-2084); run the touched projects' suites at the end; a red is a finding.
- Run anything slow in the background; never poll. ⛔ No screenshots or recordings in git.

**Done when:** A-5 is written — however far A-1…A-4 got.

---
_coordinator lane, 2026-11-12_
