<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-23); scope frozen there. A PLANNING handoff: fold it into the ui lane's plan.
updated: 2026-10-23
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Director_Timeline_Editor.md — THE design this hands over (approved, R-4071…R-4074); TE-21 still waits for the user's nod
  - docs/DESIGN_Behaviour_Debugging.md — the ui lane's own debugging design (CE-1270); §4 below says where the two meet
  - docs/blueprints/AI_Editor_Shared_Infrastructure.md — §15, the Trace Timeline this timeline becomes one panel with (TE-21)
-->

# HANDOFF — ui: the shared Timeline and the director's timeline editor

**For:** the ui lane (`ui`). **From:** the coordinator lane. ⭐ **Dispatched at the commit that adds this file**; your scope is FROZEN
there, and documents that change after it are FYI only. If a later document invalidates an item, STOP that item and report it (R-106).

⭐ **This is a PLANNING handoff, not a batch to start now.** 🔒 The user: *"they are designong behavior debuggoing with lots of ui tasks
so that they can include this one in ther plans"*. Put it in your plan beside CE-1270, in the order you judge best. The one ordering
the design asks for: **R1 (the timeline) before the director parts**, because the timeline is useful on its own.

## 0. The whole idea, in one picture

🔒 **User, 2026-10-23:** *"edit director's cuts … in the replay browser where the exercise is recorded and its time line is known … jump
to desired time and position a camera … by flying it in 3d map and then record it as new camera keyframe … skipping unimportant part of
the exercise, adding subtitles … switching 2d and 3d maps and some tactical map graphics, composing reels from takes"* · *"timeline is
usefull regardless of director cut window and i guess it should become one of the most important central pieces of the replay browser
as separate panel allowing for navigation"*.

| what is being solved | how — the design section |
|---|---|
| **Films cut from ONE recording** instead of scouting live runs: exact leads, slow motion and skips become seeks | R-4067; [design](../../DESIGN_Director_Timeline_Editor.md) §0, §3e of [`DESIGN_Film_Director.md`](../../DESIGN_Film_Director.md) |
| **A Timeline panel** — the Replay Browser's central navigation; every host shows the same panel | R-4073; design §2a |
| **An event index** read once per recording, cached, drawn by pixels so thousands of events stay readable at any zoom | design §2b |
| **A director editor** on that timeline: fly the camera, press K, keys with cut / fly / dip, tracks for time, subtitles, 2-D/3-D, film graphics, audio; promote a recording take to a scenario take | R-4068, R-4070; design §2, §3, §5 |
| **Anchors are the EXISTING conditions** (`SearchPredicateDto`, the existing StructEdit editor) — nothing new invented; a polygon area, a line crossing and drawing them are EXTENSIONS | R-4072, R-4074; design TE-3, TE-3b |
| **One player, one TKB, one transport** on every host | R-4068, R-4069; design TE-5, TE-12, TE-19 |

![Director workspace](../../img/director-timeline-workspace.svg)

## 1. Before anything

1. Merge `coordinator` at the dispatch commit or later into `ui` (it already holds `ui@39aab2378`). When you START building, push a
   `chore: started …` marker (rule 1b). Ids come from your block (`CE-1xxx`, `R-1xxx`); you create and number the tracker rows.
2. **Read, in this order:** [`DESIGN_Director_Timeline_Editor.md`](../../DESIGN_Director_Timeline_Editor.md) §0 (rulings and the TE leans), §2a, §2b,
   §6 (phases), §7 (claim table: what is measured and what is not). Then the neighbours it names in its STATUS block — above all
   [`replay-browser-2/DESIGN.md`](../../designs/replay-browser-2/DESIGN.md) (the sandbox, `RecordingSearchService`, isolation invariants),
   [`Predicate-Infrastructure-Capabilities.md`](../../Predicate-Infrastructure-Capabilities.md) and
   [`AI_Editor_Shared_Infrastructure.md`](../AI_Editor_Shared_Infrastructure.md) §15.
3. **The design is yours to finish.** It names the classes, sequences and modules; refine them in the DESIGN doc as you build (obligation
   ⑤), never in a batch file.

## 2. The items — the design's phases (§6), each with its acceptance there

| # | item | design basis | first thing to do |
|---|---|---|---|
| **R0** | eight measurements: playback fidelity · which events a recording holds · the TKB from the recording's scenario · sound sources in replay · is an editor run archived · which class draws tactical graphics · each anchor shorthand as a predicate · scan rate and index size on a real hour | §6 R0, §7's ⛔ rows | each answer goes into §7 with its file:line. ⚠ R0 ② decides where the Combat lanes come from |
| **R1** ⭐ | **the Timeline**: `TimelineIndexBuilder` + extractors + the sidecar · `ITimelineSource` (recording, live) · `TimelinePanel` with §2b's drawing rules · `ITimeTransportFacade.CanSeek / Seek` + a Replay Browser adapter · the Event Browser on the index · the TKB in the Replay Browser (R-4069) | §2a, §2b, TE-4, TE-17…TE-20, TE-12 | T-1: the Replay Browser's own suite (`Hrot.ReplayBrowser.Tests`) and `RBF_P5T2_TimelinePanelTests`, then the search suite |
| **R2** | the director on the Timeline: format v2 (`shots[]` read as keys) · `ProgramAt` · move `DirectorRunner` + `DirectorWindow` to `Hrot.Presentation` · `ReplayDirectorHost` · Time and Camera tracks, K, cut / fly / dip · undo · anchors as predicates in the existing editor | TE-2, TE-3, TE-3b, TE-5…TE-8, TE-14 | T-1: `DirectorRunnerTests`, `FilmDirectorTests`, `DirectorIdentityRails` (the gate that filmed = scouted) |
| **R2b** | Subtitles and View tracks · promote · the predicate extensions (polygon area, line crossing, drawing them on the map) | TE-1, TE-3 | T-1: the search suite + `ScenarioChecksTests` (checks.json shares the anchors) |
| **R3** | Graphics track (film-only) · shot templates · audio at any rate · still keys captured by Verify | TE-9, TE-10, TE-11 | T-1: `AudioTests`, the Verify rails |
| **R4** | reel composer · render queue · HTTP (`recording=` on `/director/*`, `POST /director/promote`) + the MCP catalog | TE-15, TE-16; R-1029, R-1030 | regenerate the catalog in the same commit |

## 3. Decided, and still open

| | |
|---|---|
| ✅ **ruled** | R-4067 (a take names its source; a film from one recording) · R-4068 (one panel, one runner) · R-4069 (the Replay Browser loads the TKB) · R-4070 (film graphics are film-only) · R-4071 (approved) · R-4072 (anchors ARE existing conditions) · R-4073 (the Timeline is its own panel, over a cached index) · R-4074 (the nine anchor shorthands lower to predicates) |
| ⚠ **open — TE-21** | **the Trace Timeline and this Timeline are ONE panel**, `ITraceLaneProvider` generalised as its row seam. The coordinator's lean; the user has not nodded yet. ⭐ You own both the panel and CE-1270, so you are best placed to settle its shape — propose it in your design pass and put it to the user |
| ⭐ **leans you may refine** | every other TE row — argue a change in the design doc, as obligation ③ asks |

## 4. Where this meets your Behaviour Debugging work (CE-1270)

| your design | this one | the joint |
|---|---|---|
| the Trace Timeline (§15 of the AI shared infrastructure), a shell today; BV-30's Brain lane deferred | the shared Timeline (R-4073) | ⭐ **one panel** (TE-21): a pinned unit's swimlane shows its trace lanes; the Brain lane is the index's Behaviour lane over the recorded journal — BV-30 gets built by R1 |
| *Debug this unit* / *Observe all* | "focus, not filter" and the pinned-unit swimlane (§2b) | the debugged unit is the focused unit |
| the breakpoint list (DBG-6, Q44) over `SearchPredicateDto` | anchors are `SearchPredicateDto` (R-4072) | ⭐ **one condition editor** for breakpoints, search and anchors; the polygon / line extensions serve all three |
| the journal break (DBG-8), *Recent changes* (DBG-10) | the Behaviour lane, next / previous event | a journal entry is a timeline event; a click seeks to it in a recording |
| HTTP for every view (DBG-11, R-1029) | `/replay/*` + `recording=` on `/director/*` (TE-15) | one route family per question |

## 5. Fences

- ⛔ **No second timeline, scrubber, condition vocabulary, runner, TKB loader or transport** (R-4068, R-4072, TE-17, TE-19, TE-12). A
  missing piece is an extension of the existing one.
- ⛔ **The live editor never rewinds** (R-3034); a skip on a scenario source still takes one step per frame (R-3038).
- ⭐ **A background pass is allowed only on its own isolated context** (replay-browser-2's isolation invariant); the GUI's sandbox and
  the live world are touched from the main thread only.
- ⛔ **A film never writes to the user's layer settings, the recording or the scenario** (R-4060, R-4070).

## 6. Report back

The gate report contract (CLAUDE.md rule 8) per built phase: one row per gate with the verbatim command, counts, delta vs the base, a
`--no-build` column, every red confirmed pre-existing, the tree clean, `tracker-counts.py --check`, every id allocated; the R0 answers and
any deviation folded into [`DESIGN_Director_Timeline_Editor.md`](../../DESIGN_Director_Timeline_Editor.md) (obligation ⑤); the sha on `ui`.
For now, only one thing comes back: **where it sits in your plan.**
