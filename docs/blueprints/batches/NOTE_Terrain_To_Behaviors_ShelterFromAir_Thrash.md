# Note: terrain → behaviors — `ShelterFromAir` thrashes between shelter points and the vehicle never moves (found by CE-4694)

| | |
|---|---|
| **From** | `terrain`, 2026-11-25 |
| **Found in** | `stechovice-battle` (the bridgehead, [HANDOFF_Coordinator_To_Terrain_Stechovice_Bridgehead.md](HANDOFF_Coordinator_To_Terrain_Stechovice_Bridgehead.md)), headless editor, live |
| **Yours** | the shelter-from-air reflex (`ShelterFromAir`, the `Flee` moves) — not fixed here |

## What was measured

`Riverbank Blue Tank 2` (an M1 at (945, 1618), ordered across the bridge — `POST /orders` move, or its authored `MoveToLocation`) with Red MiG-29s in the air
over the valley: from t ≈ 1 s to 14.5 s its journal is a `MoveStarted … (Flee)` every **2–3 ticks**, cycling between **(1002, 1618)**, **(971, 1588)**,
**(926, 1583)** and **(907, 1606)** — ~130 entries — and the tank **does not move at all** (position unchanged to 0.1 m), then `ShelterFromAir ended: Success`
and the order resumes. The same in two runs.

⇒ the shelter target is re-picked every tick or two and flips between candidates (no hysteresis / commitment), so the vehicle re-plans each time and never
starts. For ~14 s under an air threat a vehicle neither shelters nor obeys.

## Ask

A shelter choice that STICKS (commit to the chosen point until reached or invalid, or a hysteresis margin before switching). The scenario keeps the air war;
the bridgehead's tank-crossing row tolerates the delay (it keys on arrival, not on time).
