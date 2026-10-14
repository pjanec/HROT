<!--STATUS
state: LIVE — a STEER on HANDOFF_Behaviors_To_Backend_Weapon_Systems.md (dispatched at f71ce3a0b; started marker d453c45e4, no item built yet — the user: "they have not started implementing yet, waiting for us")
updated: 2026-10-14
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — THE design; W-I is folded in (§3 SeatDto / SeatPresets, §4d, §5, §6 the preset rule, §7 W-I, §8 WS-3 / WS-4)
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Weapon_Systems.md — the handoff this steers (items WS-1, WS-3, WS-4)
-->

# STEER — backend: weapon systems — presets seat their crew (W-I)

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ This changes three items of the
[weapon-systems handoff](HANDOFF_Behaviors_To_Backend_Weapon_Systems.md) before you build them. Read it together with the updated
[DESIGN_Weapon_Systems.md](../../designs/weapons/DESIGN_Weapon_Systems.md) — the design is the contract, as before.

## Why

🔒 User, `2026-10-14`: *"if there is machine gun mounted in toyota pickup, with its gunner seat occupied by a person, how can we instruct
the entity to attack? I would expect the toyota vehicle brain to control the weapon, because just that toyota is defined in the scenario,
the MG and gunner comes as preset so scenario does not care, the orders go to the toyota entity."* → the lean below → *"Agreed."*

📐 The gap it closes: the design let a preset fill only a host's **stations** (`StartWith` was a weapon type). But `CanFire` needs the
station's operating **Gunner** seat occupied (§6), so preset 104 would have arrived with an M2 and nobody to fire it — a gun that never
fires. The control rule itself was already right and is unchanged: the pickup's brain fires its station weapons (W-D, W-H).

## What changes

| item | was | now |
|---|---|---|
| **WS-1** `SeatDto.StartWith` | a weapon type id, on a `Station` | ★ a type id on **any** seat: a weapon type for a `Station`, a soldier type for a crew seat (`Gunner` / `Driver` / `Commander`) — design §3, §5 |
| **WS-4** `StationPresets` | fills a host's stations | ★ **`SeatPresets`** — fills every seat's `StartWith` ONCE on the creating node (R-138): a weapon boarded into its station, a soldier **of the host's side** boarded into his crew seat; then the host is marked filled (saved) so a load adds neither again — design §4d, §6 |
| **WS-4** presets | 104 = pickup + M2 · 105 = pickup + Kornet | ★ 104 = pickup + M2 **+ a gunner** · 105 = pickup + Kornet **+ a gunner** (2006 / 2007 unchanged) |
| **WS-3** rails | — | ★ add `WS_AStationWeapon_FallsSilent_WhenItsGunnerDies` (and fires again once a soldier boards the seat) |
| **WS-4** rails | — | ★ a placed 104 holds an M2 in its station and a soldier in its Gunner seat; a save / load adds neither again |

The seated soldier is an ordinary entity: drawn, hit, killed. Aboard, his own brain fires nothing (he only has to be in the seat).
Orders go to the pickup only; the scenario never names the M2 or the gunner.

## Unchanged

WS-2 (station boarding, the carry station, a station weapon placed at its seat) · the controller rule and the one firing path (WS-3) ·
the deploy state (WS-1) · the fences · the gate report contract. ⚠ Seats per type stay ≤ 8.

## Report

Name this steer in your report next to the items it touches. Rejected alternatives, for the record: `NeedsGunner = false` on preset
stations (fires from an empty truck) · an abstract vehicle-level gunner (nobody to kill).
