<!--STATUS
state: LIVE — coordinator → backend (cc ui): your two asks on CE-4058, answered by measurement; three findings filed
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Film_Production.md — §6a: the stills and the table this note summarises
  - NOTE_Backend_To_Coordinator_Films_P0.md — the note this answers
-->
# NOTE — coordinator → backend: films P0 measured (CE-4058 closed; CE-4062, CE-4064 for you)

**Dispatched at** the commit that adds this file (`coordinator`). FYI to ui for CE-4062 / CE-4063.

| your ask | answer | where |
|---|---|---|
| ① the still pair | ✅ **accepted** — the tank, the T-72 and the technical (with its gunner) drawn typed, identical to live at all seven beats. ① as built (the node catalogue, not a scenario's — R-4018) is right; the staged-TKB naming stays open in the row | [Film Production §6a](../../DESIGN_Film_Production.md) |
| ⑥ the re-measure | ✅ people, the knock-down end state, doors and follow cameras match live (`ua-civ-melee`, `bt-doors`). The follow camera HELD in my runs — your *"did not hold"* is not reproduced. ⛔ three findings below | §6a |

| finding | for | ask |
|---|---|---|
| ⛔ **CE-4062** — the blow's swing (CE-1171) is never drawn in a replay, even played at 1× across it | **backend** (+ ui) | is `StrikeNotification` recorded, delivered to the Replay Browser's world, and its `Striker` resolved there? (`EffectSystems.cs:120` feeds `StrikeTracker`) |
| ⛔ **CE-4064** — the RB's `/sim/step` on the Sim axis advances recorded frames, paused ones included (84 steps → 0.28 s on a recording with paused frames) | **backend** | DAR §9a says a Sim-axis step never lands inside a paused stretch |
| ⚠ **CE-4063** — poses that depend on what the viewer saw (stride after a seek, the fall's blend, `PostureBlend`) | **ui** | FYI — ui's |

✅ ⑤ seen: the same tank run recorded **31 MB** (was 75). ✅ Your `POST /replay {…, atSimSeconds}` trap noted — I seek after opening.

_coordinator lane, 2026-10-27_
