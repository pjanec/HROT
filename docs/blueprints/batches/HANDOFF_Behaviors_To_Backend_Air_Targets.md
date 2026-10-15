<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-15); scope frozen there.
updated: 2026-10-15
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/air-attack/DESIGN_Air_Operations.md — §7c OWNS the measurement and the approved decisions AT1–AT4 (R-2021); fold the as-built there
  - docs/designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md — OWNS what a round does to a hull (the aircraft's armour faces, AT4)
  - docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md — §8 the counter-drone fire that AT1 makes able to hit (its known-rot)
  - docs/DESIGN_Entity_Interactions.md — §7 riders die with their vehicle (I-2c), which AT3's crash relies on
-->

# HANDOFF — backend: aircraft can be hit, rounds lead them, a killed aircraft falls, the UH-60s get health (CE-2182…CE-2185)

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at the commit that adds this file**: your
scope is FROZEN there; documents that change after it are FYI. If a later document invalidates an item, **STOP and report it** (R-106) —
⭐ stop THAT item, never the batch.

⭐ **A FRAME** — the measurement, the approved decision, the acceptance. Step 1 is yours: read [Air Operations §7c](../../designs/air-attack/DESIGN_Air_Operations.md)
(the probe table, the findings with `file:line`, the decisions AT1–AT4, 🔒 R-2021), draw the classes / sequences you add into §7c, build,
fold the as-built there.

## 0. The measurement (`2026-10-15`) — in one line each; §7c has the table

- 🔴 **No bullet or unguided rocket can hit any aircraft**: a round's ray masks out `CombatConstants.BulletCollisionLayer` = 2
  (`BallisticsSystem.cs:215`); `CombatTkbTranslator` ORs its `CombatCollisionLayer` — also 2 — onto the collider (`:25,72`); a flyer's
  collider comes from `FlightTkbTranslator` with layer 0 (`Flight.cs:90`) ⇒ it ends on 2 alone ⇒ `(mask & layer) == 0`
  (`RaycastSolverSystem.cs:153`). FPV 403, A-10 405, AH-64 406 measured on layer 2; rifleman and M1 on 3.
- ⛔ No lead: a round is aimed where the target IS.
- ⛔ A killed aircraft hovers (`RotorcraftModel.cs:53`) or flies straight on (`FixedWingModel.cs:49`): death ends the move
  (`LocomotionDispatcherSystem.cs:56-71`) and nothing makes it fall.
- ⛔ UH-60 400 and 404 have no health (`DefineAircraft` adds no combat platform).
- ✅ Already right, keep: the 3-D height band of the hit test (M1), blast and fragments on aircraft (`AreaEffectSystem.cs:101-113`), soldiers
  seeing and targeting aircraft.

## 1. The items — all 🔒 approved by the user (R-2021)

| # | row | item | acceptance — rails in the features' own suites |
|---|---|---|---|
| **AT1** | **CE-2182** | a round gets its OWN collision bit (e.g. 4) so `CombatCollisionLayer` means "can be hit". ⚠ Check every other combat-only collider this makes hittable (weapon systems, M134 stations, mortars) and report the list | `RaycastSolverSystemTests` (or the ballistics suite): a round fired up at an aircraft built through `TkbTranslatorSet.Base()` (the AH-64 type) MEETS it; rounds still never meet each other; the rifleman / M1 rails unchanged. ✅ Red-proof: bit 2 restored ⇒ red |
| **AT2** | **CE-2183** | a direct-fire round whose target is in the **Air** domain (`Locomotions.InAir`) is aimed at the LED point; ground targets unchanged. The helper is **`TargetLead.Point(shooterPos, roundSpeed, targetPos, targetVel)`** (Fdp.Toolkits Combat, two-step intercept) — ⭐ **behaviors builds it as the first step of B13 and pushes it on `behaviors`; merge it before AT2** so there is one predictor. On the brain node the target's motion comes from `ReplicatedMotion` (Air Ops §9c — the brain's `SimVelocity` of another node's entity is zero) | `AimAndFireExecutorTests`: a rifleman at a helicopter crossing at 60 m/s, 300 m — the intent's aim point is ahead by velocity × flight time (± 1 m); a ground vehicle at the same speed is aimed where it is (unchanged). Live, with AT1: a crossing helicopter takes hits |
| **AT3** | **CE-2184** | a killed aircraft (`Health ≤ 0` on the node that moves it) FALLS: its model is left, it keeps its velocity, gravity pulls it (a simple drag is your call); `DroneImpactSystem` crashes it on the ground or a body (destroyed — no burst unless it is a munition, R-3007); its riders die with it (I-2c, already built) | `AirMotionSystem`'s / the flight model's suite: a helicopter at 60 m killed mid-route reaches the ground within the free-fall time (± 10 %), is destroyed there, and its riders are dead; a plane killed at 300 m falls along its track. ✅ Red-proof: without the fall it hovers ⇒ red |
| **AT4** | **CE-2185** | UH-60 400 and 404 get a health and armour entry (lean: like the AH-64 class's 800, light skin) — your numbers, stated with their source | the catalogue suite (`NedTkbBuilderCombatTests`): both types carry `Health` through the real translators |

⚠ **Also measured, NOT approved — report numbers, do not build:** the A-10 and the AH-64 have health but no armour faces, so once AT1 lands
every rifle round penetrates them. Propose per-face armour (the A-10's cockpit tub, the AH-64's 12.7 / 23 mm resistance) with sources; we
take them to the user.

## 2. Fences

- ⛔ **No behaviour change** — which soldier fires at which aircraft, the air attack and its evasion are ours (Air Ops §7b, CE-2179).
- ⛔ `TargetLead.Point` is written once (behaviors, B13); the aim path calls it — do not add a second predictor.
- ⛔ Ground combat numbers do not move: AT2 is Air-domain targets only; AT1 must leave every existing hit rail green.
- ⛔ The demos are ours: we run them live after your merge.

## 3. Report back — the gate report contract (CLAUDE.md rule 8)

One row per gate (verbatim command · counts · delta · `--no-build` · reds pre-existing at the base sha · golden diff shape · tree clean ·
`tracker-counts.py --check` · ids allocated) — **plus row 8**: AT1 changes what every round can meet, so name and run the combat
integration suite that exercises hits (or state with evidence why it cannot gate). Air Ops §7c folded to the as-built (classes / sequence
you added, prior state marked SUPERSEDED). Progress on **CE-2182…CE-2185** as appended dated lines.
