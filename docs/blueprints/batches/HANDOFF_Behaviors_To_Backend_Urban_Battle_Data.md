<!--STATUS
state: LIVE — behaviors → backend: three DATA items the urban battle needs (BR-14…BR-16); small, independent of each other.
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md — OWNS the need: §4 ② (finding a mortar) and ⑧ (the data fixes). This frame only points at it.
  - docs/designs/indirect-fire/DESIGN_Indirect_Fire.md — (shared) the tubes and guns (IF-1, I8) whose launch must be heard; IF-6 counter-battery builds on BR-14.
  - docs/designs/vehicle-tactics/DESIGN_Urban_Armour_Tactics.md — (behaviors) the convoy's vehicles, which BR-15 / BR-16 make fightable.
-->

# HANDOFF — behaviors → backend: the urban battle's data *(BR-14, BR-15, BR-16)*

**Dispatched at:** the commit that adds this file, on `behaviors`. ⭐ Your scope is FROZEN at that commit; a later document that
invalidates an item stops that item only (R-106).

**What it is.** The user approved the urban battle's order (R-2060, `2026-10-19`): the platoon programme first, then the scene's own
reactions, then the headquarters. These three are backend data that nothing on the behaviors side can do, filed now so they never
block it. ⭐ Allocate your own tracker rows (your block); name them back in a note.

| # | item | 📐 measured today | ⭐ lean | acceptance |
|---|---|---|---|---|
| **BR-14** | **a launch is HEARD from the crewed tubes and guns** | the crewed weapon systems `WeaponSystem_Mortar60/81/120` and the towed howitzer (`BdcTkbCatalog.cs:344-360`, `WeaponSystemType`) carry no sound descriptor; the PERSON type `MortarTeam` 2005 does (`UrbanCombatTkbCatalog.cs:271`, `HeavyWeapon` 500 m, since `97b751b3a`); ⚠ the SP howitzer not checked | a firing sound on each tube and gun at a doctrinal range (a mortar ≈ 1.5–2 km, a howitzer more), `HeavyWeapon` class, from the weapon's position — so a soldier's hearing hears the launch as it hears a rifle | rail: a listener 1.2 km from an 81 mm tube hears its launch (a heard sound at the tube's position), one 3 km away does not |
| **BR-15** | **the HMMWV can be hit and killed** | `Truck_HMMWV` (`BdcTkbCatalog.cs:606-633`) has visual, physics, faction and carrier, and NO `WithCombat` — no health, no armour | a light-vehicle combat block like the APC's (health, thin armour, blast susceptibility) and, as a TKB preset, the roof-ring M2 (its ring is already modelled, ui P1) | rail: a HMMWV hit by a rifle round takes armour-scaled damage; an RPG kills it; a crewed roof gun fires |
| **BR-16** | **the generic APC is armed** | `MilitaryAPC` 2001 (`UrbanCombatTkbCatalog.cs:189-201`) has `CombatPlatformDef` and seats, and NO `WeaponSuite` | a 12.7 mm remote weapon station (the Bradley's turret pattern, smaller) as a TKB preset (R-1001: weapons in vehicles come from TKB presets) | rail: an APC with a gunner seated engages a target with its RWS |

**Fences.** ⛔ No behaviour changes here — what a soldier DOES with a heard launch (a SPOT, counter-battery) is behaviors' (Urban
Battle §4 ②, IF-6). ⛔ The convoy's halt drill is behaviors' (§4 ⑤).
