<!--STATUS
state: LIVE — ui → coordinator: Timeline R1-a, R1-b, R1-c built (CE-1302); R0 ⑧ measured (CE-1301)
updated: 2026-10-26
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/DESIGN_Director_Timeline_Editor.md — §9 the plan, §9a the as-built of these three slices
-->

# NOTE — ui → coordinator: Timeline R1-a…c built ([HANDOFF_Coordinator_To_Ui_Director_Timeline](HANDOFF_Coordinator_To_Ui_Director_Timeline.md), `CE-1302`)

Built on `ui`: `eb767b13d` (the §9 plan), `8a06ef50b` (R1-a), `efc817237` (R1-b + R1-c), `25bdc1ca0` (one event per new history entry, a failed build reported), `8e6fa8681`, this note's commit. No item was
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

A 216 MB recording (14 245 frames, 596 s), the editor's assemblies loaded: **one pass 46 s = 309 frames/s, 12.9× real time · sidecar 115 kB ·
reopen 1.1 ms** (budget 200 ms) · layout 0.59 ms per draw for an hour's synthetic index (budget 1 ms).

Two findings, both in [§9a](../../DESIGN_Director_Timeline_Editor.md):
- a recording holding a component type from an assembly the process never loaded stops the playback (`PlaybackSystem.cs:332`) — the
  browser's sandbox registers the same way; the index now stops with the reason shown, never an endless "Indexing…";
- that recording's frames 0–2 carry relative wall ticks and frame 3 absolute ones — the mixed clock `6052a75a1` fixed this morning; older
  recordings show a squashed span (the browser's scrubber too). No timeline workaround.

## Gates

| gate | command (built the TEST project first, then `--no-build`) | result |
|---|---|---|
| R1-a rails | `dotnet test Fdp.Toolkits.Tests --no-build --filter FullyQualifiedName~TimelineIndexTests` (+ `HROT_TIMELINE_RECORDING`, `HROT_TIMELINE_ASSEMBLY_DIR` for R0 ⑧) | 5 / 0 (new) |
| Replay Browser area (toolkit) | `dotnet test Fdp.Toolkits.Tests --no-build --filter FullyQualifiedName~ReplayBrowser` | 186 / 0 |
| Fdp.Presentation full (incl. `TimelineLayoutTests` 6 new) | `dotnet test Fdp.Presentation.Tests --no-build` | 606 / 0, 1 skipped (pre-existing) |
| Replay Browser host | `dotnet test Hrot.ReplayBrowser.Tests --no-build` (restored first — its assets file was missing) | 35 / 0 (incl. `CE1302_TheEventTimeline_*`; FND-T12 now counts 7 windows) |
| red-proofs | refused shot counted · death not reported · baseline order emitted · repeat ignored · only the newest new order · sidecar ignoring write time · sidecar never written · build error thrown not reported · density losing its last frame · cluster keeping its first lane · zoom anchored at the left edge | ✅ 11 / 12 red; ⚠ one first-round mutation stayed green because the rail then had no baseline order — the rail was strengthened and the same mutation is red in round two |
| tree clean after runs | `git status` | ✅ |
| `tracker-counts.py --check` | | OK |
| `design-digest.py --check` | | ⚠ flags `docs/DESIGN_Utility_Decision_View.md` (no INVENTORY) — another ui session's (CE-1325), not this work |

Ids allocated: none (no new tracker rows — `CE-1301` / `CE-1302` carry the progress). No HTTP route changed (no catalog regeneration).

---
_ui lane, 2026-10-26_
