<!--STATUS
state: LIVE — REPORTED (CE-1967 done)
updated: 2026-11-18
current-answer: §0
stale-below: nothing
related-designs:
  - ../../designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md — OWNS RM-1 / K-1 (VehicleImpactSystem); §5b "K-1 run-over" and "K-1 lethal" are CE-1967's as-built
-->

# STATUS — behaviors: the user's editor observations (CE-1967)

Dispatched by the coordinator in `HANDOFF_Coordinator_To_Behaviors_User_Observations.md`; started at the merge of `coordinator@a401d7d22`.

## §0 Where it stands

| row | state | reproduction · cause · fix |
|---|---|---|
| **CE-1967** a bulldozer only knocks soldiers down | ✅ done | **Reproduced live** (editor over HTTP, the new `ua-dozer-runover`): a 49 t dozer driven straight over a standing man left him at exactly **30.000002 / 100**, knocked down for 3 s. **Two causes, both in K-1:** ① only the hull's FRONT struck (speed band × a mass factor capped at ×2 ⇒ at most "badly hurt" at the dozer's 4.5 m/s); the hull passing OVER him did nothing ⇒ **a hull ≥ 3.5 t crushes what it drives over** (R-2138). ② ⭐ **every vehicle hit rode `HitEffect.Blunt`, which `HealthApplicationSystem.NonLethalFloor` floors at the incapacitated line (the baton rule, R-2085) ⇒ no vehicle could ever kill anyone** — against K-1's own design ⇒ **new `HitEffect.Impact`** (lethal, knocks down; appended, a byte on the wire) for every vehicle strike and run-over (R-2139) |

## §1 Ids allocated

R-2138 (run over by ≥ 3.5 t ⇒ killed; cars and pickups unchanged) · R-2139 (a vehicle's hit is lethal force: `HitEffect.Impact`) — both lane decisions for the user's review.
New scenario `ua-dozer-runover` (`scenarios/demos/platoons-and-vehicles`).

## §2 Findings

| finding | evidence | where |
|---|---|---|
| no vehicle impact could ever kill (the non-lethal floor) | live: Health 30.000002 after the dozer passed over; the first crush rail passed because it summed PUBLISHED damage | fixed: `HitEffect.Impact`; an end-to-end rail through the damage chain |
| an ordinary `MoveToLocation` steers a vehicle round a standing man (the hull passed 4 m beside him) | live, `ua-dozer-runover` with a move order | the scenario drives by `RamTarget` at a place (no giving way), as the user's possessed dozer did |
| `ClearObstacle` with nothing at its point fails at its first tick | `ClearObstacleBehavior.cs` ("Nothing there ⇒ the order fails") | scenario authoring only |
| ⚠ `Melee.Strike` still rides `Blunt` (floored, never kills) | `Melee.cs:79` | not changed, not measured — a knife or bayonet may need `Impact` or `Kinetic`; for the user / the melee owner |
| `ua-animal-herd-road` golden: herd positions differ 2–6 m at t = 40 s, no Health change | this run | not this change (it only changes damage); not re-captured |

## §3 Gates

| gate | result |
|---|---|
| T-1 `VehicleImpactSystemTests` + `HealthApplicationSystemTests` (+ Effects, Melee) | **67 / 67** |
| the same with `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` | **34 / 34** |
| mutants (each alone, restored by `git checkout`) | M1 `Impact` floored ⇒ 2 red · M2 the impact published as `Blunt` ⇒ the end-to-end rail red · M3 no crush ⇒ 3 red |
| scenarios | ✅ `ua-dozer-runover` (new) · ✅ `cp-ram-barrier`, `cp-vbied`, `cp-routine-car`, `cp-civilian-stops`, `cp-compliant-bomber`, `cp-moto-attack`, `ua-animal-herd-road`, `ua-moto-drive-by`, `ua-civ-police-line` — 10 / 10; goldens `same` except `cp-vbied` (CE-2448, differs run to run) and `ua-animal-herd-road` (positions only, above) |
