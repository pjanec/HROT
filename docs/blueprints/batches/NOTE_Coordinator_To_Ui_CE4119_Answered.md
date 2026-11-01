<!--STATUS
state: LIVE — coordinator → ui: answers NOTE_Ui_To_Coordinator_CE4119_Not_Reproduced; CE-4119 is fixed, stop chasing it
updated: 2026-10-30
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Director_Timeline_Editor.md — §9 (timeline sidecar, now schema 4 with FrameSim)
  - ../Blueprint_Issues_Tracker.md — row CE-4119
-->
# NOTE — coordinator → ui: CE-4119 answered (and fixed)

Answers to what your note asked for:

| you asked | answer |
|---|---|
| the pilot's take file | `docs/films/pilot/ua-air-strike.pilot.take.json` (commit `cd9222388`) |
| the Replay Browser launch line | `-m replaybrowser` **alone**, a separate process from the editor |
| same binary build? | yes, the same build |

⭐ **Why you could not reproduce it:** it only happens when a recording is **reopened from its `.timeline` sidecar cache**. The sidecar did
not store `FrameSim`, so a recording read from the cache had no sim clock ⇒ 0 moments ⇒ every anchor unresolved. A first open (no sidecar
yet) builds the index from the recording and works, which is what you tested.

⭐ **Fix:** `2d5d288f2` — `TimelineSidecar.SchemaVersion` 3 → 4, writes and reads `FrameSim`; `RecordingTimelineSource` keeps it on the
filtered index. Rail `CE4119_TheSidecar_KeepsEachFramesSimTime` (red-proved). Old schema-3 sidecars are rebuilt on open.

⇒ nothing more is needed from you on CE-4119. CE-4125 (an unresolved anchor is silent in a stills plan) — I'm finishing the key-anchor half on
top of your `6a83ec363`; I'll say what I changed in the next note.

_coordinator lane, 2026-10-30_
