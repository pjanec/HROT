<!--STATUS
state: LIVE — DISPATCHED at `c7bcb4319` (2026-10-26); scope frozen there
updated: 2026-10-26
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Replay_Browser_Debug_Http.md — THE design this hands over (R-4080, R-4081 ruled; RB-1…RB-8 leans; RB-7 needs the user)
  - docs/DESIGN_Http_Control_Through_The_Cluster_Master.md — backend's route table the Replay Browser now answers too
-->

# HANDOFF — ui: the Replay Browser over the AI debug HTTP API, every feature

**For:** the ui lane (`ui`). **From:** the coordinator lane. ⭐ **Dispatched at `c7bcb4319`**; your scope is FROZEN there
and later documents are FYI. If one invalidates an item, STOP that item and report it (R-106).

🔒 **User, `2026-10-26`:** *"we of course still need the replay browser to be controllable via ai debug http … preferrably using the existing
api"* · *"Replay browser is always a standalone offline host. the export to json should be added as well, no replay browser feature should be
excluded from ai debug http api"* (R-4080, R-4081).

## 0. Why

Backend's R-3053…R-3055 (`a5b0a922f`) moved every replay route onto the cluster master and retired the debug service's private copy of the
replay engine. The Replay Browser app never had HTTP control, and its provider fills none of the members the routes now read ⇒ today an agent
can read a Replay Browser's world but not open, seek, step, search over or export a recording there.

## 1. Before anything

Merge `coordinator` at `c7bcb4319` or later; push a `chore: started …` marker (rule 1b); ids from your block. **Read
[`DESIGN_Replay_Browser_Debug_Http.md`](../../DESIGN_Replay_Browser_Debug_Http.md)** §0 (rulings, leans), §2 (every feature → its route), §3
(UML), §4 (claim table). The design is yours to finish and fold the as-built into (obligation ⑤).

## 2. The items — the design's §6

| # | item | design basis | first thing to do |
|---|---|---|---|
| **1** | the provider members: master control, transport, state, exercises, duration, recording path, ⭐ a serializer-backed extraction (it holds `_scenarioSerializer` and does not pass it — CE-171's reflection fallback) | RB-1 | T-1: `Hrot.ReplayBrowser.Tests` (the host's suite) and the debug provider rails (`ASubsystem…_PassesItToItsDebugProvider`) |
| **2** | the seek forms (`frame`, `frames`, `next` entity / lane, `cause`), resolved in the debug service for every host · lifecycle search · `/events` from the active provider | RB-2, RB-5 | T-1: the search suite (`Fdp.Toolkits.Tests --filter ~ReplayBrowser`) |
| **3** | `IReplayBrowserControl`: view, playback history, selection history, export jobs | RB-3, RB-4 | the export's file equals the panel's for the same options |
| **4** | timeline reads and user lanes · the map routes · `POST /selection` only if the user reopens it (RB-7) | RB-6, RB-8, RB-7 | regenerate the MCP catalog and skill (R-1030) |

## 3. Fences

- ⛔ No second route family and no second replay engine: the shared routes, the shared provider seam, ONE Replay Browser-only seam
  (R-4081, R-4078's "unify and share").
- ⛔ The export and any whole-file lookup run on their own context, never on the GUI's sandbox (replay-browser-2's isolation invariant).
- ⚠ RB-7 waits for the user's word.

## 4. Report back

The gate report contract (CLAUDE.md rule 8): one row per gate with the verbatim command, counts, delta vs the base, a `--no-build`
column, every red confirmed pre-existing, the tree clean, `tracker-counts.py --check`, every id allocated; ⭐ a LIVE check: a
`-m replaybrowser` host driven through every §2 row over HTTP, with its outputs; the as-built folded into the design.
