<!--STATUS
state: LIVE — in progress
updated: 2026-11-17
current-answer: the step table, the lane decisions, the stand-ins left for ui
related-designs:
  - ../../designs/person-looks/DESIGN_Person_Outfits.md — A (OF-1…OF-12)
  - ../../designs/weapons/DESIGN_Bows_And_Crossbows.md — B (BW-1…BW-13), E (§9 NA-1…NA-7)
  - ../../designs/animals/DESIGN_Animals.md — C (§4b AN-20), D (§4c AN-21)
-->
# STATUS — backend: OUTFITS (data), BOWS, BIRDS, THE RABBIT HUNT, THE NATIVE AMERICAN WARRIOR (CE-4648, CE-4649, CE-4650)

Handoff: [`HANDOFF_Coordinator_To_Backend_Bows_Birds_Outfits.md`](HANDOFF_Coordinator_To_Backend_Bows_Birds_Outfits.md), dispatched at `1bc4fb20e`.
Started at `c71fe5ede` (coordinator merged at `1b523513c`, a fast-forward).

## Steps

| # | step | state | result |
|---|---|---|---|
| A | outfits — data (CE-4648) | 🟡 building (worker) | — |
| B | bows (CE-4649) | ⏳ | — |
| C | birds (AN-20) | ⏳ | — |
| D | the rabbit hunt (AN-21) | ⏳ | — |
| E | the Native American warrior (CE-4650) | ⏳ | — |

## Ids

| what | id |
|---|---|
| Middle-East civilians | 1105–1110 (measured free on every lane) |
| Middle-East fighters | 2010–2012 (2012 only if a man-portable machine gun exists) |
| `WeaponCategory.Bow` · `MunitionShape.Arrow` · `HitMarkKind.Arrow` / `.Bolt` · munitions arrow / bolt | 14 · 9 · 5 / 6 · 6066 / 6067 — measured free on `backend`, `behaviors`, `ui`, `coordinator` |

## B — S0, measured `2026-11-17`

| question | answer | where |
|---|---|---|
| do animals have `HitMarks` parts? | ⛔ **no** — a round meets a body by its parts only for a PERSON (`BodyPartRay.Person`, `IsPerson` = `VehicleClass.Pedestrian`) or a ground VEHICLE (`VehicleBody.Of`); an animal (`Quadruped`) is a plain cylinder hit with `HasBodyPart = 0` | `RaycastSolverSystem.cs:246-300`, `PhysicsComponents.cs:82-92` |
| does a lying round keep its rotation on a viewer? | ✅ the `LightweightRound` replicates the world pose, rotation included; ⚠ the viewer draws a lying round FLAT (`ProjectileLayer.cs:157-163`) — the stuck draw is ui's (BW-3 / BW-10) | design §2 row 3 |
| does the weapon brain need more than numbers? | ✅ **numbers + switch arms only**: the category is read in `Armament` (armed look), `WeaponBrainProfiles` (profile, direct / indirect), `ArmorModel` (fire on the move), `BarrelHeat` — a `Bow` arm in each; no behaviors work | grep of `WeaponCategory.` outside catalogues |
| where does a shot's report come from? | per SHOOTER (`AcousticEmitter.FiringAudibleRange`), with two exceptions already — a countermeasure release and a hand throw (`SoundEmissionSystem.cs:85-100`) | ⇒ R-3100 |

## Lane decisions (backend block — for the user's review)

| id | decision | what would change it |
|---|---|---|
| R-3098 | the existing wardrobes (everyday, child, elderly, police, soldier) keep their EXACT draw sequence, reading every colour from `OutfitCatalog` data (no palette left in code); a type with an authored `OutfitDto` is dressed by the OF-6 fixed-order slot draw. ⭐ Why: today's dresser consumes its random stream conditionally (`Outfits.cs:156-177`: suit, sleeves, skirt/shorts rolls depend on earlier rolls), so one generic slot draw would reshuffle every existing crowd and break the design's own acceptance ("the existing frames pixel-identical", §6). The OF-2 sex fix still consumes the old 50 % roll, so nothing else shifts | the user accepting a one-time reshuffle of every crowd's clothes ⇒ the everyday set becomes ordinary slot data and one draw procedure remains |
| R-3099 | an arrow / bolt that hits an ANIMAL wounds it as any round does, and leaves NO stuck mark — animals have no part boxes in the hit test (S0); the round is torn down | an animal part kit in `BodyPartRay` (sim + ui) — filed as a follow-up |
| R-3100 | the bow's silence (BW-9) is a property of the MUNITION (`Silent` on the arrow / bolt rows), checked in `SoundEmissionSystem` beside the two existing exceptions — a man who carries a bow AND a rifle is still heard when he fires the rifle | the user wanting a quiet SHOOTER type instead |

## Stand-in looks left for ui

*(filled as each step lands)*
