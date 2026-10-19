<!--STATUS
state: LIVE — backend → behaviors: CE-3220 part 2 landed; five of your shipped scenarios were edited (cross-lane).
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7.2b: the as-built (what was retired, what each scenario became)
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §5d: CivilianHsm, the default a 1101 starts
-->

# NOTE — backend → behaviors: 1001 / 1003 are retired; your scenarios' stand-ins are 1101 + `Idle`, or the new 2008

Row: [`CE-3220`](../Blueprint_Issues_Tracker.md) part 2, which was unblocked by your `CE-2233` (CIV-0, the neutral-target gate). Design:
[Civilians §7.2b](../../DESIGN_Civilians_And_Crowd_Control.md).

**What changed for you.** The built-in TKB no longer has `CivilianPedestrian` (1001) or `CivilianChild` (1003). Five shipped scenarios
placed a 1001, and it played two different roles:

| scenario | entity (side) | now |
|---|---|---|
| `ua-civ-neutral-gate` | Civilian (Neutral) | **1101** + `Behavior: Idle / Superior` |
| `ua-threat-ranking` | Civilian Near (Neutral) | **1101** + `Idle` |
| `tt-posture` | Unarmed Hostile (Hostile) | **2008** UnarmedFighter |
| `ua-attack-approach` | Hidden Hostile (Hostile) | **2008** |
| `tt-heard-shot` | Decoy (Friend) | **2008** |

**Why two answers.** ① A 1101 starts `CivilianHsm` by default (your §5d); the old 1001 ran nothing. `Idle` at `Superior` outranks
the `Sop` default (CE-3034), the shape your `ua-civ-melee` uses. ② On a 1101, the three combatant stand-ins became **no target**: your
AW-21 (`Armament.IsCombatant`) judges a Civilian-class person on conduct, whatever his side. 📐 Measured: `CE2073`, `CE3084` and
`CE2106` all went red, with no round fired. ⇒ a new built-in **UnarmedFighter (2008)**: exactly the 1001's body (no weapon, no brain,
civilian senses) under the Infantry class, so he is a combatant by status, as the 1001 was by accident.

⭐ **Your call:** drop the `Idle` on the two neutral civilians if you want them to live their routine. The scenarios are yours.

**Editor list.** `BehaviorCatalog` now offers 1101–1104 `CivilianHsm`, `MoveToLocation`, `FollowRoute`, `Idle`, `WanderCivil`, `PanicFlee`.
The dead map key 501 is gone.

**New rail** (`RetiredCivilianTypesTests`, Hrot.Core.Tests). Every `TkbType` a shipped scenario places (under `scenarios/` and your
`Recipes/Scenarios/`) must resolve in the built-in TKB, so a scenario naming an unregistered type now reds at unit-test speed.

---
_backend lane, 2026-10-19_
