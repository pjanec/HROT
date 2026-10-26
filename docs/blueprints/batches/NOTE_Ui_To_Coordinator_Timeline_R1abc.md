<!--STATUS
state: DRAFT (R0 ⑧ numbers and the gate table land in the next commit) — ui → coordinator: Timeline R1-a, R1-b, R1-c built (CE-1302); R0 ⑧ measured (CE-1301)
updated: 2026-10-26
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/DESIGN_Director_Timeline_Editor.md — §9 the plan, §9a the as-built of these three slices
-->

# NOTE — ui → coordinator: Timeline R1-a…c built ([HANDOFF_Coordinator_To_Ui_Director_Timeline](HANDOFF_Coordinator_To_Ui_Director_Timeline.md), `CE-1302`)

Built on `ui`: `eb767b13d` (the §9 plan), `8a06ef50b` (R1-a), `efc817237` (R1-b + R1-c), this note's commit (R0 ⑧ + gates). No item was
invalidated by a later document. **Where it sits in my plan:** R1-a…c done; next R1-d (the pinned unit's swimlane — TE-22's acceptance
on the `hill-attack` commander), then R1-e (live source, `ITimeTransportFacade.CanSeek / Seek`, the Trace Timeline hosting the panel).

| slice | what | as-built |
|---|---|---|
| **R1-a** | the recording's event index (`Fdp.Toolkits/ReplayBrowser/Timeline`): Life / Combat / Behaviour extractors, one pass on its own playback + repository, the `<recording>.timeline` sidecar, `RecordingTimelineSource` (sidecar or background build) | [§9a](../../DESIGN_Director_Timeline_Editor.md) |
| **R1-b** | `TimelineLayout` (pure: density vs glyphs, clusters, priority on top, zoom about the cursor, ruler) + `TimelinePanel` + `ITimelineCursor` | same |
| **R1-c** | the Replay Browser hosts it as **Event Timeline** (`rb_event_timeline`) over the primary node's recording; clicks seek through `FederatedReplayManager` (D1) | same |

⚠ **Deviations, argued in §9a:** the sidecar checks the recording's size + write time (not its last frame's ticks — that needs the file
opened); only the `events` table is built (`entities` waits for R1-d, `position track` for R2b; the `pyramid` replaced by shared bin
edges, measured under budget).

## R0 ⑧ — scan rate and index size on a real recording

⏳ running — a 216 MB recording from `2026-10-24`; numbers in the next commit.

## Gates

| gate | command (built the TEST project first, then `--no-build`) | result |
|---|---|---|
| ⏳ | filled in the next commit | |

Ids allocated: none (no new tracker rows — `CE-1301` / `CE-1302` carry the progress). No HTTP route changed (no catalog regeneration).

---
_ui lane, 2026-10-26_
