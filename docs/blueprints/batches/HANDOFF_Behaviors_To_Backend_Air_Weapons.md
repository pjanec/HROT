<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-14); scope frozen there.
updated: 2026-10-14
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/air-attack/DESIGN_Air_Operations.md — THE design: §7 the attack profiles, §8 the Air-domain launch (A4), §11 slices 6a / 6b
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — the door stations and the crew presets (W-I) the UH-60 item uses
  - docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md — OWNS the munitions; the rocket here is a new one
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Weapon_Systems.md — finish that batch (WS-1…WS-4 + the two steers) FIRST
-->

# HANDOFF — backend: the air weapons (A-10 cannon, an attack helicopter, an unguided rocket, the door gunners)

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at the commit that adds this file**: your
scope is FROZEN there; documents that change after it are FYI. If a later document invalidates an item, **STOP that item and report it**; do
every item that is not blocked (R-106). ⏳ **Start after the weapon-systems batch** (WS-1…WS-4 and its two steers).

## 0. Why

🔒 User, `2026-10-14`: *"Is aircraft attack & strike demo scenario in plan, showing airplane bombing, airplane firing its cannon,
helicopter firing rockets, helicopter firing mounted minigun, helicopter firing slot carried gun entity with gunner?"* → the lean → *"Approved."*
📐 Measured: the A-10 (405) carries only the Mk-82 rack (`BdcTkbCatalog.cs:503-532`); no helicopter has a gun or a rocket; the only rocket
munition is the RPG-7 (6009). ⇒ the behaviours lane's gun run, rocket run and running-fire profiles (Air Operations slice 6) and the showcase
demo `ua-air-attack` (slice 6b) have nothing to fire. ⭐ **Design:** [DESIGN_Air_Operations.md](../../designs/air-attack/DESIGN_Air_Operations.md)
§7 (the profile table), §8 (the Air-domain launch, A4), §11 rows 6a / 6b.

## 1. Before anything

1. Merge `behaviors` at the dispatch commit or later. Push a `chore: started …` marker (rule 1b).
2. Ids from your block (`CE-3xxx`); progress on our row **CE-2173** as appended dated lines.

## 2. The items

| # | item | acceptance |
|---|---|---|
| **F1** | **the A-10's cannon**: a GAU-8-class 30 mm mount on type 405 — `WeaponCategory.Autocannon`, FIXED forward (no turret: its arc is the nose cone, a few degrees), HEI rounds with an area burst against soft targets and a penetrator against armour (the warhead library's nearest entries; report any number you had to invent) | `NedTkbBuilderCombatTests` (405 has the rack AND the gun); an `AimAndFireExecutorTests` rail: a fixed-forward mount fires only when the target is inside its nose cone, and the round keeps the aircraft's velocity (A4) |
| **F2** | **an attack helicopter type** (AH-1 / AH-64 class): `Gen.Locomotion Rotorcraft` + `Gen.Flight` (like 404), a 70 mm rocket pod (fixed forward, a ripple of rounds per press) and a chin minigun on a turret (traverse about ±110°, elevation down to about −60°); a DIS type in the air domain so the `AirPlatform` orders resolve | the catalogue suites; `B7`-style flight rail for the new type |
| **F3** | **a Hydra-70-class UNGUIDED rocket** munition (HE, area burst, direct fire — no guidance; fast, flat, dispersing a little per round) | `FireProcessingSystemTests`: a rocket from the air keeps the carrier's velocity and flies unguided; a ripple scatters around the aim point |
| **F4** | **the doors-off UH-60's door gunners**: its left / right door stations start with an M134 (Weapon Systems WS-4) **and** its door-gunner seats start with a soldier (`StartWith`, W-I) — ⚠ if WS-4 already did this, just confirm it | a placed 404 holds two M134s and two door gunners; a door gunner killed ⇒ his gun falls silent (`WS_AStationWeapon_FallsSilent_WhenItsGunnerDies` covers the rule) |

## 3. Fences

- ⛔ The behaviour lane owns the attack profiles (GunRun, RocketRun, RunningFire, DoorGunOrbit), `AirAttackProfiles`, the orders and the demos —
  a finding there is a REPORT.
- ⛔ No ground combat number moves: only the new types and the new munition. The Air-domain launch (A4) is unchanged.
- ⚠ Seats per type stay ≤ 8.

## 4. Report back — the gate report contract (CLAUDE.md rule 8)

One row per gate: verbatim command · pass / fail / skip · delta vs base · `--no-build` column · every red confirmed pre-existing against the
base sha · golden movement as a diff shape (the type-picker golden gains lines) · tree clean after each suite · `tracker-counts.py --check` ·
every id allocated. The design folded to the as-built. Then the sha of each item on `backend`.
