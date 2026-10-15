<!--STATUS
state: ⛔ ON HOLD — NOT DISPATCHED (user, 2026-10-15: "do not start building yet"). Do NOT start; a later commit lifts the hold and stamps the dispatch sha.
updated: 2026-10-15
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/airborne/DESIGN_Airborne_Insertion.md — THE design (approved, R-1003): §6 behaviours, §7 items H-1…H-3, §8 the three demos
  - docs/blueprints/batches/HANDOFF_Ui_To_Backend_Airborne_Insertion.md — the twin handoff; the engine half you build on (B-1…B-4, B-8, B-9)
-->

# HANDOFF — behaviors: airborne insertion — the drills, the jumper, the pilot, and three demos

**For:** `behaviors`. **From:** `ui`. ⛔ **ON HOLD — not dispatched; do not start** (user: *"do not start building yet"*). When the hold is lifted, the dispatch sha is stamped here and the scope freezes there (R-106 applies). Ids from your
`CE-2xxx` block; the ui row tracking this is **CE-1118**.

🔒 **User, `2026-10-15`:** *"approved. i will need a demo scenario for a squad of paratrooper jumping out of helicopter and regrouping on the
ground; also similar demo with a squad sliding down to building roof using rope from a helicopter then needs to come and hover above the building
and fly away when all men are safely on the roof; and a demo of a catapulting pilot"*.

## Order of work
1. ⚠ **Depends on backend's B-1…B-4** (the `Descent` component, the models, the `Rope` / `Drop` holds, the door exit) and **B-8 / B-9** (eject).
   Merge `backend` once it pushes them; until then, design and author against the design's seams.
2. Step 1 is yours: write the drills' classes and sequences into [the design](../../designs/airborne/DESIGN_Airborne_Insertion.md) §6 (diagram
   first, the `SquadDismount` / `ISquadPlay` shape), then build, then fold the as-built.

## The items — design §6, §7 H-1…H-3, §8

| # | item | acceptance |
|---|---|---|
| **H-1** | SOP rows (carrier `Roping` ⇒ `SquadFastRope`; carrier `Dropping` ⇒ `SquadAirborneExit`), the two drills, `ParachuteDescent` (writes the landing point as the `NavigationIntent`; the model steers), `AssembleAfterDrop`, the release point | ★ 8 men down two ropes in ≤ 15 s, in a ring; ★ a stick of 8 from 400 m lands along the DZ and assembles |
| **H-3** | `Eject` (a mission task — after `TimerElapsed` it plays the malfunction) and `EvadeAndSignal` for the landed pilot | ★ the pilot reaches cover ≥ 100 m from the nearest known enemy and stays down |
| **H-2** | the three demos, §8: **`ua-airborne-drop`**, **`ua-fast-rope-roof`**, **`ua-eject`** — each with its PASS line from §8, run live | live PASS per demo, recorded in the design |

⭐ **The rooftop one, twice over:** the security spots must stay INSIDE the roof's outline (a man never steps off a ledge — CE-3145 — so a spot
past the edge leaves him standing at it), and the helicopter must not leave while a man is on a rope (B-3's release rule — check it in the demo).

## Report — the gate report contract (CLAUDE.md rule 8)
One row per gate with `--no-build`; reds proven pre-existing; the live demo results; every id allocated; the sha of each item.
