<!--STATUS
state: LIVE — coordinator → ui: films from recordings — the director in the Replay Browser (CE-1303 P1 + P2), the agent's routes (CE-4059), promote (CE-1304), polish (CE-1305 / CE-1306)
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Film_Production.md — THE intent (R-4091): the workflow, its principles F-1…F-8, who does what, the overnight documentation
  - ../../DESIGN_Director_Timeline_Editor.md — §11 the plan: INVENTORY, the class / module / sequence diagrams, leans CR-1…CR-12, phases P0–P6
  - NOTE_Coordinator_To_Backend_Films_From_Recordings.md — backend's P0 (the TKB in the RB, the recording's scenario, kept exercises), sent the same day
-->
# NOTE — coordinator → ui: films from recordings, the director in the Replay Browser

**Dispatched at** the commit that adds this file (`coordinator`). Your scope is frozen there; documents changed after it are FYI only. This is a
PLANNING note and a FRAME: put it in your plan in the order below, design the details in Director Timeline §11 as step 1 of each phase, fold
the as-built back (obligation ⑤).

## The goal, in the user's words

🔒 **User, `2026-10-27`:** *"… run those clips in the replay browser from the recording and adjust the camera timing, camera cuts, position
and viewing angles, subtitles and their timing, and stuff like that to my liking, and be able to save that as takes and reels for the
recording and to promote these to a live scenario … The adjusting phase in the replay browser is critical … in the replay browser I can simply
seek to any time and start the replay of the take from whatever time. So it's easy to iterate the change and see and change and see."*
⭐ Full statement and approval: [Film Production](../../DESIGN_Film_Production.md) (R-4091).

⭐ **The shift:** a recording is the film's SOURCE; a live run produces recordings. Your director (CE-1364: keys, cut / fly / dip, segments,
the Camera and Time tracks, K, undo) is the editing surface — it needs a SEEKABLE host and must be ONE director on both hosts.

## What I measured ([Film Production §6](../../DESIGN_Film_Production.md))
A recording replayed in the RB matches the live run in time (7/7 sim-time seeks exact), place and effects; the bodies are boxes until backend
loads the TKB there (CE-4058). The RB renders a film frame in 0.26 s (live editor 0.32 s) and plays at 25.8 fps at 1× under software GL.

## Your phases — Director Timeline §11f

| phase | row | builds | accepted when | after |
|---|---|---|---|---|
| **P1** — one director | CE-1303 | the film runner, `DirectorTakeSession`, the Director window (film tabs), the four adapters (`EditorCinema`, `EditorLayerOverride`, `FilmFrameSink`, `EditorDirectorSound` — they MOVE, renamed for what they are), K's pilot camera → `Hrot.Presentation`; **`DirectorPack.Attach(host)`**, the one registrar the editor calls (CR-2) | the 55 seeded takes play the same shots (`CE1364_T4_*`); every director rail green | ⛔ **my CE-4061 first** — I am cutting the verifier out of `DirectorRunner` behind one `IRunExtension` seam (TE-5's premise was false: six verify types, 19 verify members on `IDirectorHost`). I will tell you when it lands |
| **P2** ⭐ — adjust in the RB | CE-1303 | `ReplayDirectorHost` over `ReplayBrowserTransport` (CR-3: `CanSeek`, `Seek(sourceSim)`, rate) · `DirectorPack` in the RB, its Event Timeline carrying the tracks · **CR-4: the runner plays the session's IN-MEMORY take; `PlayFrom(film t)`; loop a shot** · CR-7: time segments PLAY on both hosts (speed ramps; a skip = a seek here, a fast-forward card live; a segment may go BACK = an instant / multi-angle replay) · **CR-11: a Subtitles track** (moved up from R2b) · **CR-6: keys tied to the nearest preceding index event + an offset**, shown on the key · *"update key from view"*, pilot ⇄ program camera · takes + reels beside the recording (TE-1; backend's kept exercise) | the person opens a recording, plays a take from any time, changes a cut / an angle / a subtitle and sees it on the next loop, saves it beside the recording | P1, CE-4058 ① (typed bodies) |
| **P3** — the agent's means | CE-4059 | a **position track** in the timeline index (a few samples a second per networked unit) · `/director/*` with `recording=` on the RB (`DebugApiHost.AttachDirector` from `DirectorPack`) · `POST /director/stills` (PNGs at given film times, a contact sheet) · render to MP4 in the RB · the MCP catalog (R-1030). ⭐ The **Drafter** (CR-8) is shared code: you give it its home and the *"Draft a shot sequence here"* menu item; I write its shot rules (PLAN_Feature_Films §1a) with you | I draft a feature's take from a recording, read its contact sheet, adjust and render it without the person | P2 |
| **P4** — promote and repeat | CE-1304 | promote = a COPY (keys are already tied to events) · CR-5: ONE anchor resolver — event cues from an `ITimelineSource` (the live index live, the recording's index on a recording), state cues observe the world; `AnchorBook`'s `ShotLog` / `DetonationLog` reads retire · the View track | a take adjusted on recording A plays its beats on a later recording B of the same scenario | P2 |
| **P6** — polish | CE-1305 / CE-1306 | Graphics track (TE-9) · audio at any rate (TE-11) · smooth slow motion only if P3 measures judder at 0.25× (CR-9) · the reel composer and render queue · polygon / line conditions (R-4072) | §6 R2b / R3 / R4 | P4 |

⭐ My side, for the order: **CE-4061** (the verifier split, now) and **CE-4060** (the overnight documentation, after P3).

## The rules that bind every phase
- **One director, every host (R-4068, CR-2):** no host-specific copy of a director part — if the RB needs it, it comes from `DirectorPack`.
- **One seek path (R-3059):** the director seeks through the RB's transport, the same one the panel's cursor drives.
- **The scout of a recording never steps the GUI's sandbox (TL-7).**
- **Diagrams in the design, not here** — extend §11's class / sequence / module diagrams as you design each phase.

## Gates you report (rule 8)
Per phase: the touched suites `--no-build`, every red with its base sha; the director rails (`DirectorTracksTests`, `FilmDirectorTests`,
`DirectorRunnerTests` — the last includes my verifier rails, which must stay green across P1), and for P2 a recording-based rail in
`Hrot.ReplayBrowser.Tests`. ⭐ P2 is also proven live: a screenshot pair of one adjusted key, before and after, from the RB.

_coordinator lane, 2026-10-27_
