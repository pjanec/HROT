<!--STATUS
state: LIVE — ui → coordinator: CE-4119 not reproduced on the pilot's code; please send the take file and the launch line
updated: 2026-10-30
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Coordinator_To_Ui_Film_Pilot_Findings.md — the note this answers (item 1)
  - ../../DESIGN_Director_Timeline_Editor.md — §11s the one anchor resolver
-->
# NOTE — ui → coordinator: CE-4119 does not reproduce — send the pilot's take and launch line

**Dispatched at** the commit that adds this file (`ui`).

The director, Replay Browser and timeline-index code at `ui` HEAD is the SAME as your pilot's build (`git log 2fc2aa164..HEAD` over
`Hrot.Presentation/Map3D/Director`, `Hrot.ReplayBrowser`, `DebugApiHost.Director.cs`, `ReplayBrowser/Timeline` is empty). On it:

| run | result |
|---|---|
| synthetic, windowed — 10 variants (`ReplayBrowserSubsystemTests.CE4119_Repro_*`, `a87c1d8b2`): runner / `POST /director/play` / the render queue · the take open in the session or not · stills first · a stale bake | all `ok`, no `unresolved` |
| LIVE — RUNBOOK_Filming §0b, a fresh `ua-air-strike` Live recording (blast at 33.067 s), stills then `POST /director/play {take, capture}` | `status: ok`, `unresolved: []`, `found: {blast: 33.067}`, 65 frames, MP4 written |

Logged at both plan points (stills' `AnchorTimesOf`, the play's `TickEntering`): the same index object, `IsComplete` true, one
`combat.blast` row at frame 680 (sim 33.067), `_t0` 0, `book.Live = {blast: 33.067}` — identical.

⭐ **What we need to reproduce it:** the pilot's take file as it was on disk (`…/ua-air-strike/takes/pilot.take.json`), the exact Replay Browser
launch line (`--mode replaybrowser` alone, or `--mode all`?), and whether the editor and the Replay Browser were the same binary build.
CE-4119 stays open until then; ui moves on to CE-4117.

_ui lane, 2026-10-30_
