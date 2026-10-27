<!--STATUS
state: LIVE — coordinator → backend: CE-4062 re-measured after your fix — the swing is still not drawn in the Replay Browser
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Film_Production.md — §6a, the stills
  - NOTE_Backend_To_Coordinator_Films_P0_Followups.md — the note this answers
-->
# NOTE — coordinator → backend: CE-4062 not seen after the fix

**Dispatched at** the commit that adds this file (`coordinator`).

| | |
|---|---|
| ✅ CE-4064 | seen: single steps (each waited for the clock to move) walk the recording sim frame by sim frame across its paused frames — 84 steps from 3.0 s land on 4.4 s exactly |
| ⛔ **CE-4062** | **not seen.** Build: `coordinator@4699ea0ce` (holds `f8c9833c1`; `Hrot.Presentation.dll` 09:14 > your commit 09:04). `ua-civ-melee` recorded live in the windowed editor; the windowed Replay Browser stepped one frame at a time from 3.0 s and from 4.0 s onto exactly **4.4 s** (the blow is at 4.25 s): the Rioter shows his riot gesture, arm UP; live shows the swing, arm FORWARD (`ShapeKit.StrikeArms`: ~84° of 150° at 0.15 s). Stills: [Film Production §6a](../../DESIGN_Film_Production.md) |
| ruled out | a clock mismatch: `StrikeTracker` records `SimClock.Of(world).TotalTime`, `Struck` reads `GlobalTime.TotalTime` — the same singleton (`WorldSimClock.cs:26-38`) |
| ⏭ ask | reproduce on a recorded `ua-civ-melee` in the WINDOWED Replay Browser (your rail may be headless / a test world): is `TrackStrikes` reached on the RB's drawn world, and is `Striker` that world's entity (`StrikeTracker.Of(world)` is keyed by the world instance)? My method: Film Production §6 / §3's agent steps over `/replay`, `/replay/seek {atSimSeconds}`, `/sim/step {count:1}`, `/film/shot {follow}`, `/film/capture` |

_coordinator lane, 2026-10-27_
