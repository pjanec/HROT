<!--STATUS
state: LIVE — a STEER on HANDOFF_Behaviors_To_Backend_Weapon_Systems.md (WS-1 built 5c3fe8974; WS-2 not started) — adds slice T-a of the team carry, best built with WS-2
updated: 2026-10-14
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Entity_Interactions.md — §7.5 THE team-carry design (classes, the stretcher sequence, T1–T7, slice T-a)
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — W-G now points at §7.5 (the TwoMan weapon)
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Weapon_Systems.md — the handoff this steers (WS-2)
  - docs/blueprints/batches/STEER_Behaviors_To_Backend_Weapon_Systems_Crew_Presets.md — the first steer (W-I), unchanged
-->

# STEER — backend: the team carry (a stretcher, a two-man weapon) — slice T-a, with WS-2

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ This ADDS one slice to the
[weapon-systems handoff](HANDOFF_Behaviors_To_Backend_Weapon_Systems.md); it changes no item you have built (WS-1, `5c3fe8974`). Build it
with **WS-2** (weapon boarding) — they touch the same `Embarkation` / `EmbarkedStateSystem` code. ⭐ The design is the contract:
[DESIGN_Entity_Interactions.md](../../DESIGN_Entity_Interactions.md) **§7.5** (classes, sequence, decisions T1–T7, rails).

## Why

🔒 User, `2026-10-14`: *"What about the weapon to be carried by 2 men, is that in the plan, including demo scenario?"* · *"... becase we
might need aomething like that for medics carrying stretcher with wounded soldier"* · *"Approwed write it into Entity Interactions §7"*
(R-2017). The weapons design had a two-man weapon as one line (W-G) with no mechanism; this is that mechanism, shared with the stretcher.

## Slice T-a — what to build

| piece | design |
|---|---|
| `CarryMode.Stretcher = 3` (free in the existing 2 bits a seat — no wire shape change) | §7.5.0, T2 |
| `PassengerBuffer.Assistant` · `EntityPassengers.Assistant` (network id) · `EmbarkEntityCommand.Assistant` — all APPENDED; report each (R-169) | T1 |
| `IsAssistingTag { Carrier }` — derived on every node from the list, like `IsEmbarkedTag` | T1 |
| the gate in `Embarkation.TryEmbark`: a `Stretcher` carry and a `Portable = TwoMan` weapon need a named assistant — alive, not incapacitated, carrying nothing, aboard nothing, the carrier's side, within 2 m; a `OneMan` load refuses one | T4 |
| `EmbarkedStateSystem`: the assistant placed at `PersonCarryDto.AssistantOffset` (the rear handle, ~2 m behind, turning with the carrier) where his position is owned; either man down ⇒ the load put down where it is (a casualty laid there, a weapon left `Stowed`), the link dropped | T3, T6 |
| `CarryLoad.Factor`: `Stretcher` × `PersonCarryDto.StretcherSpeedFactor` (0.6); a `TwoMan` weapon × its `CarrySpeedFactor` | T3 |
| both men lose `CanShoot` while carrying, get it back on the put-down | T5 |

**Rails** (the features' own suites): `EmbarkationTests` — a stretcher needs an assistant, a two-man weapon too, a one-man load refuses
one · `EmbarkedStateSystemTests` — the assistant at the rear handle as the carrier turns; either man down ⇒ the load put down, the link
gone · the speed × 0.6 · the passenger codec round-trips `Assistant`.

## Not yours

The nodes that use it (`TeamCarryNodes.Stretcher`, the casualty-care choice, the `CarryWeapon` team) and the demos are ours (T-c); the
drawing is ui's (T-b). A finding there is a REPORT.

## Report

Name this steer in your report next to WS-2, with the wire appends (R-169) and the gate rows as usual.
