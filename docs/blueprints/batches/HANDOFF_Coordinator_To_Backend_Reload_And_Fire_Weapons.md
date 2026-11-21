<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - ../../designs/thermal/DESIGN_Thermal_Drivers.md — §3b / §3c (TD-16): `FireState` spots on three holders, ignition in the damage path, `BurnSystem` — the fire the weapons light
  - ../../designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md — §7 / §7a: `Hrot.Look`'s effect rules and categories — the flame and napalm look
  - NOTE_Behaviors_To_Coordinator_CE4665_Not_Reproduced.md — what behaviors measured on CE-4665
-->
# HANDOFF — coordinator → `backend`: the reload freeze (CE-4665), then the FLAMETHROWER + NAPALM design (CE-4635, CE-4637)

🔒 User, `2026-11-19`: *"the flamethrower (CE-4635) and napalm (CE-4637) design and the CE-4665 could be given to backend"*.

## Priority
After the row you are on (CE-3425), BEFORE CE-4395 and CE-3445.

| order | row | what | done when |
|---|---|---|---|
| 1 | **CE-4665** infantry stop moving after a scenario reload (⏸ parked) | the reporter's real-1 is not available. Behaviors REFUTED the crowd cause (production runs the no-op `EngineBackedDtCrowdProvider`) and did not reproduce on `pc-defend-house` (4 loads: 16 · 15 · 13 · 14 movers). ⭐ Try the user's ground: terrain's `pc-corridor-fight` (`wide-corridor`) and a ~30-infantry scenario: one editor process, `/scenario/load/live` (and `/edit`) ×4, count movers in 30 s each time; then behaviors' suspects — per-load state keyed by entity handle (SoftClear restarts generations, CE-3294), and anything scaling with the terrain (residency / navmesh per load) | REPRODUCED ⇒ root cause + fix + a rail (one process, N loads, every mover moves); or NOT REPRODUCED with what was tried, written into the row (and the row stays parked) |
| 2 | **CE-4635** flamethrower + **CE-4637** napalm — ⭐ **a DESIGN for the user's approval, NOTHING built** | ONE design for both (they share the flame): the flame AREA EFFECT (a cone from a man-portable or a vehicle-mounted thrower; napalm's burning ground area) as shared engine code; what it SETS ALIGHT — people, roofs, wagons, vehicles — through the thermal `FireState` spots and its ignition rule (TD-16), burning damage through `BurnSystem`; burning GROUND = a fire on a terrain cell record (the third holder, thermal §3c), a scorched `BurnedArea` mark when it goes out; the LOOK through `Hrot.Look`'s effect rules + categories (§7a — a flame category maps to DIS); fear / stress (a burning man, a flame seen); who uses it (a squad's flamethrower man, an aircraft's napalm drop — the air lane's bombing) and the ROE side (an "unhuman but military" weapon: a scenario flag?); TKB (range, fuel, burn time); the fun pack's dragon breath (CE-4634) is the same effect on another carrier | the design in `docs/designs/weapons/` with INVENTORY (graph + grep), the proven solution, decisions with leans, the three diagrams, slices with their scenarios; ⛔ no code until the user approves |

Gates (rule 8) for row 1. Fold findings into the owning designs (obligation ⑤).
