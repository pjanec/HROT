<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-26); scope frozen there
updated: 2026-10-26
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: replaces HANDOFF_Coordinator_To_Ui_Replay_Browser_Http.md (withdrawn, never started) — R-4082
related-designs:
  - docs/DESIGN_Replay_Browser_Debug_Http.md — THE design this hands over (R-4080…R-4082 ruled; RB-1…RB-8 leans; RB-7 needs the user)
  - docs/DESIGN_Http_Control_Through_The_Cluster_Master.md — your route table and provider seam, which the Replay Browser now answers too
-->

# HANDOFF — backend: the Replay Browser over the AI debug HTTP API, every feature

**For:** the backend lane (`backend`). **From:** the coordinator lane. ⭐ **Dispatched at the commit that adds this file**; your scope is
FROZEN there and later documents are FYI. If one invalidates an item, STOP that item and report it (R-106).

🔒 **User, `2026-10-26`:** *"backed unified the replay control across hosts which likely broke the replay browser ai http control; we of course
still need the replay browser to be controllable via ai debug http … preferrably using the existing api"* · *"Replay browser is always a
standalone offline host. the export to json should be added as well, no replay browser feature should be excluded from ai debug http api"* ·
*"Better if all http stuff handle by a single lane."* · *"it is nit visual so lets give it to backend"* (R-4080, R-4081, R-4082).

## 0. Why you

Your R-3053…R-3055 (`a5b0a922f`, CE-3326) moved every replay route onto the master and retired the debug service's private replay copy, and
`8f657625b` made one ECS-node debug surface by construction. The Replay Browser app never had HTTP control and its provider fills none of the
members your routes read ⇒ today an agent can read a Replay Browser's world but not open, seek, step, search or export there. ⭐ The whole job
is yours, map and selection routes included — one lane for the HTTP surface (R-4082).

## 1. Before anything

Merge `coordinator` at the dispatch commit or later (it carries `ui` and your own `9c1297c37`); push a `chore: started …` marker (rule 1b);
ids from your block. **Read [`DESIGN_Replay_Browser_Debug_Http.md`](../../DESIGN_Replay_Browser_Debug_Http.md)** §0 (rulings, leans RB-1…RB-8),
§2 (every Replay Browser feature → its route), §3 (UML), §4 (claim table). The design is yours to finish and fold the as-built into
(obligation ⑤). ⚠ The Replay Browser host and its panels are the ui lane's code — a cross-lane edit is allowed and said in the commit.

## 2. The items — the design's §6

| # | item | design basis | first thing to do |
|---|---|---|---|
| **1** | the Replay Browser provider fills the members your routes read: an `IClusterMasterControl` over `FederatedReplayManager` (open / close / seek), `Drive` (play, pause, step, rate), state, exercises on disk, duration, recording path, ⭐ a serializer-backed `Extraction` (it holds `_scenarioSerializer` and does not pass it — CE-171's reflection fallback) | RB-1 | T-1: `Hrot.ReplayBrowser.Tests`, `TheDebugProvidersDoNotUnderReportTests` (`Hrot.SimHost.Tests` — add the Replay Browser's members to its forwarding rails) |
| **2** | the seek forms (`frame`, `frames`, `next` entity / lane, `cause`) resolved in the debug service for EVERY host · `/replay/search` lifecycle mode · `/events` from the active provider | RB-2, RB-5 | T-1: `Fdp.Toolkits.Tests --filter ~ReplayBrowser` (the search suite) |
| **3** | `IReplayBrowserControl`: view (node / merged), playback history, selection history, export jobs (`POST /replay/export`, `GET /replay/export/{job}`) | RB-3, RB-4 | the export's file equals the panel's for the same options |
| **4** | timeline reads and user lanes (`GET /timeline/lanes`, `/timeline/events`, `POST /timeline/lanes` over ui's `RecordingTimelineSource` / `TimelineLaneCatalog`) · the map routes on the Replay Browser (`/view`, `/view/follow`, `/entities/{id}/focus`, `/map/layers`, `/entities/{id}/debug`) · `POST /selection` ONLY if the user reopens it (RB-7) | RB-6, RB-8, RB-7 | regenerate the MCP catalog and skill in the same commit (R-1030) |

## 3. Fences

- ⛔ No second route family, no second replay engine: the shared routes, the shared provider seam, ONE Replay Browser-only seam (R-4081,
  R-4078's "unify and share").
- ⛔ The export and any whole-file lookup run on their own context, never on the GUI's sandbox (replay-browser-2's isolation invariant).
- ⭐ The Replay Browser is standalone (R-4080) — its provider is the only one in its process; no "any node" special case is needed.
- ⚠ RB-7 waits for the user's word.

## 4. Report back

The gate report contract (CLAUDE.md rule 8): one row per gate with the verbatim command, counts, delta vs the base, a `--no-build` column,
every red confirmed pre-existing, the tree clean, `tracker-counts.py --check`, every id allocated; ⭐ a LIVE check: a `-m replaybrowser` host
driven through EVERY §2 row over HTTP, with its outputs (runbook: `docs/RUNBOOK_Cluster_Debugging_Over_Http.md`); the as-built folded into the
design.
