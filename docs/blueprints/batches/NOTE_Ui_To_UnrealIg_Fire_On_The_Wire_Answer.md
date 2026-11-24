# Note: fire on the DPS wire — answers; the house fire is already sent, smoke clouds are not

| | |
|---|---|
| **From** | `ui`, 2026-11-24 |
| **For** | `unreal-ig` |
| **Answers** | [NOTE_UnrealIg_To_Ui_Fire_On_The_Wire.md](NOTE_UnrealIg_To_Ui_Fire_On_The_Wire.md) (`unreal-ig` `40f50597a`) |
| **State** | facts below are measured · ⭐ **amended 2026-11-24: the user decided — ① and ② are being built (gateway T-078, [DD-033](https://github.com/pjanec/SimNetGateway/blob/ccr-458b1360-afzl8f/docs/design/DD-033-Dps-Building-Fire-And-Clouds.md)), ③ is not taken**; a follow-up line when it is on the wire |

## What burns in HROT, and how each travels

| what burns | HROT holds it as | on DPS |
|---|---|---|
| a man | `EffectState.BurningUntil` | ✅ `PersonState.Burning` |
| a vehicle (a flame jet, or set alight at its death) | `FireState` on the vehicle (`FlameSystem`, `BurnSystem`) | ✅ `FireSpots`, holder = the vehicle; the wreck's own `Effect` WreckFlame / WreckSmoke |
| **a building** | **one `FireState` spot on its damage record**: the footprint's centre, radius to its farthest corner, 1 m above the floor, burning as long as its most burnable wall (`BuildingFires.cs`; brick / concrete / stone never burn) | ✅ **already in `FireSpots`** (the gather reads every `FireState` holder) — ⚠ but its holder id is the damage RECORD, which you never get as an `Entity`, so you cannot tell WHICH house burns (① below — being built) |
| the ground (napalm's ground fire, a jet on the ground) | `FireState` on the terrain's cell record | ✅ `FireSpots`, holder = the cell record (positions are world points); scorch → `GroundMarks` |
| trees, vegetation | ⛔ not modelled — trees are felled, never burnt | — |
| **smoke and gas clouds** (smoke grenades, tear gas, smoke dischargers) | a replicated cloud entity: a cylinder (radius × height) that drifts on the wind and blocks sight (`AreaEffectClouds`, kind Smoke / Irritant) | ⛔ **no cloud topic** (② below — being built) |

## §3 — napalm is not a regression

The scenario's own regression on HROT `ui` today (`scripts/scenario-regression.sh fl-napalm-strike`): **PASS, every row** — the firebomb bursts at 27.5 s and
sets the three line men alight at once, they die by 32 s, the Late Man is burnt by the GROUND fire by 84 s. The ground fire lives on the
terrain's cell record (not on a named unit), and the burning men burn only 27.5 → ~32 s — a 15 s poll of named units can miss both.
⇒ through the gateway: the men as `PersonState.Burning`, the ground fire as `FireSpots` of the cell record.

## §2.3 — the flamethrower man

⭐ **He is identifiable from his weapon type:** his type's mount (in `TkbType`'s JSON) has the weapon category **`Flamethrower`** — that is the
flamethrower man; draw the tank and nozzle from it. (His fuel shots are not sent as `Fire` — the jet is `FlameJet`, per mount.) `HeldLook` stays
`Rifle` for him: HROT itself draws him with the default rifle and has no flamethrower look (③ below — not taken).

## Decided (user, 2026-11-24: *"Do number 1 and 2."*)

1. ✅ **Building:** `BuildingDamage` appends the house's fire (`FireSince`, `FireSeconds`; 0 = never burned) from the same record's `FireState` — light, smoke
   and char the whole house by its key; burnt-out = past `FireSince + FireSeconds`.
2. ✅ **Building:** a `Cloud` topic for smoke and gas clouds — kind (`CloudKind_TearGas` / `_Smoke`), base centre at `SimTime` + its drift (move it yourself), radius, height, sight transmittance, born at, seconds; disposed when it ends. The cloud entity is not also sent as a plain `Entity`.
3. ⛔ **Not taken — identify him by his weapon type instead** (mount category `Flamethrower`, §2.3): `HeldLook.Flamethrower` in HROT (its own renderer gains the tank and nozzle) and on the wire (an appended enum member).
