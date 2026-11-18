<!--STATUS
state: LIVE — IN PROGRESS (CE-1967 built; scenario running)
updated: 2026-11-18
current-answer: §0
stale-below: nothing
related-designs:
  - ../../designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md — OWNS RM-1 / K-1 (VehicleImpactSystem); §5b "K-1 run-over" is CE-1967's as-built
-->

# STATUS — behaviors: the user's editor observations (CE-1967)

Dispatched by the coordinator in `HANDOFF_Coordinator_To_Behaviors_User_Observations.md`; started at the merge of `coordinator@a401d7d22`.

## §0 Where it stands

| row | state | reproduction · cause · fix |
|---|---|---|
| **CE-1967** a bulldozer only knocks soldiers down | ✅ built — rails green; scenario `ua-dozer-runover` ⏳ | **Reproduced** in the feature's suite (`VehicleImpactSystemTests.CE1967_*`): a 49 t D9 driven over a standing man at its top speed 4.5 m/s left him at **84.5 / 100** damage, at 1 m/s at **0**. **Cause** (`VehicleImpactSystem.cs`): only the hull's FRONT strikes, scored by speed band × a mass factor CAPPED at ×2 — 4.5 m/s ⇒ at most 9 m/s, the "badly hurt" band (35–75 %, ±15 %); under 2 m/s nothing; the hull passing OVER him did nothing, and the 2 s repeat outlasts the 1.8 s the 8 m hull takes ⇒ knocked down 3 s, then up. **Fix** (R-2138, lane decision): a hull ≥ 3.5 t moving ≥ 0.3 m/s whose footprint covers a body the front did not reach this frame crushes it — 1.2 × Health, killed |

## §1 Ids allocated

R-2138 (lane decision, for the user's review: run over by ≥ 3.5 t ⇒ killed; cars and pickups unchanged).

## §2 Gates

| gate | result |
|---|---|
| T-1 the feature's own suite `VehicleImpactSystemTests` | **9 / 9** (2 new, red before the fix: 84.5 and 0 of 100) |
| with `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` (+ `DroneImpact`) | **16 / 16** |
| scenario `ua-dozer-runover` (new: the Victim dies, the Bystander beside the blade untouched) | ⏳ running |
