<!--STATUS
state: LIVE
updated: 2026-10-14
current-answer: the whole file. It is the report for HANDOFF_Behaviors_To_Backend_Air_Weapons.md (F1–F4).
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Air_Weapons.md — the handoff this report answers
  - docs/designs/air-attack/DESIGN_Air_Operations.md — §7a the air weapons as built (AW-1…AW-5, F4 stopped)
  - docs/blueprints/batches/REPORT_Backend_Weapon_Systems.md — carries the 404 door-station decision F4 waits on
-->

# Report — backend → behaviors: the air weapons (F1–F4)

Scope frozen at the dispatch commit (merged into `backend` in `32264ce64`, behaviors@`ee891028d`); the started marker is `0885170f4`.
`behaviors` had not moved at the start of this item (fetched `2026-10-14`). Branch: **`backend`**. As-built:
[`DESIGN_Air_Operations.md` §7a](../../designs/air-attack/DESIGN_Air_Operations.md).

## Where each item is

| item | what | sha on `backend` | state |
|---|---|---|---|
| **F1** | the A-10's GAU-8 (405, mount 1): `Autocannon`, fixed forward, **`WeaponMountDto.FixedConeDeg` = 5°** — the AI's aimed, blind and at-point rounds are held `OutOfArc` outside the cone (3-D, the body's pitch included); HEI loaded (6023) + a penetrator (6024), `AmmoChoice` picks | `19fcb343a` | ✅ |
| **F2** | the attack helicopter **406** (`Heli_AH64`, DIS 1.2.225.20): Rotorcraft, mount 0 a chin M134 on turret 0 (±110°, −60…+11°), mount 1 the Hydra pod (`Rocket`, fixed forward, 8° cone, 38 rounds) | `19fcb343a` | ✅ |
| **F3** | **6025 Hydra-70 HE**: unguided `MissileDto` 700 m/s / 6 s, the warhead of the new library entry `70mm rocket HE`; `WeaponCategory.Rocket = 9` (per-shot; a held trigger keeps firing — the ripple) | `19fcb343a` | ✅ |
| **F4** | the doors-off UH-60's door gunners | — | ⏳ **unblocked by the user** (below) — next commit |

## F4 — stopped, then unblocked

⭐ **User, `2026-10-14`:** *"Why not to extend the wire capacity to 10 seats or so instead of hacking the seats? Isnt the descriptor sent
only on change? No problem extending."* — 📐 measured: `EntityPassengersEgressTranslator` publishes only on change
(`EntityPassengersTranslators.cs:48`). ⇒ **F4 is built by widening the passenger list to 12** (8 riders + 2 door stations + 2 door
gunners), appended on the wire — its own commit, after this one. The original stop, kept for the record:


404 already has **8 seats**, the wire's cap. Two door stations + two door-gunner seats need 4 of them. It is the same user decision the
[weapon-systems report](REPORT_Backend_Weapon_Systems.md) carries (row "404's door stations"), still unanswered. ⭐ **Lean (unchanged):**
per side, the forward seat becomes a door station and the next a door-gunner seat — 4 riders + 2 gunners + 2 stations; the air-assault demo
then loads 4, not a squad of 8. **Alternative:** keep 404 at 8 riders and add a door-gunner variant (a new type id) for the DoorGunOrbit
vignette. Either is an hour of backend work once decided.

## Numbers I had to choose (each with its source)

| number | value | source |
|---|---|---|
| GAU-8 HEI penetration / damage | the library pair of `30mm HE` (`0.2 · 30` = 6 mm · `600 · 0.05 kg` = 30) and its warhead | `ammo.json` (existing entry) |
| GAU-8 API penetration / damage | the library pair of `30mm APFSDS` (≈ 125 mm · ≈ 286) | ⚠ nearest entry — the real PGU-14 is a full-calibre DU penetrator, ~69 mm at 500 m; a `30mm API` library entry would bring it closer (lean: add it if the gun runs over-kill armour) |
| GAU-8 cone / dispersion / load | 5° · 5 mils · 1150 rounds (575 + 575) · 3900 rpm | public figures; the cone is mine ("a few degrees") |
| Hydra-70 HE | new library entry `70mm rocket HE`: he, 70 mm, 4.5 kg warhead, 1.04 kg explosive, 700 m/s ⇒ blast lethal ≈ 2 m, injury ≈ 6 m, fragments ≈ 42 m, damage 624 | public M151 / Mk66 figures; ⚠ a NEW library entry — the Mk-82 precedent (CE-3201) |
| Hydra flight / pod | 700 m/s, self-destruct 6 s (~4 km) · pod 38 rounds, 10 mils, 8° cone | public figures; cone and dispersion mine |
| 406 flight | 80 m/s top, climb 12, accel 4 m/s², hover 30 m, turn radius 50 m | public AH-64 figures, rounded; the UH-60's shape |
| 406 combat | 800 hp, sensors 6 km, chin M134 4000 rounds | mine (the UH-60 has no combat numbers to copy) |

## What the behaviour lane needs to know

- **The gun run must point the aircraft.** A level pass 300 m up never brings a ground target 1 km ahead into a 5° cone (it is 16.7° under
  the nose); the plane's pitch is clamped to ±25° (`FixedWingModel.MaxPitchDeg`). The hold reads `OutOfArc` while the nose is off.
- **The ripple is the action's round count** (`AimAndFireParams.Rounds` with a short `CooldownSeconds`); the pod is mount 1 of 406.
- **`AirAttackProfiles.Pick`** still returns null for a Rotorcraft and finds the A-10's rack before its gun (index 0) — LevelBomb is
  unchanged.

## Wire (R-169)

No wire struct changes. `WeaponCategory.Rocket = 9` is appended; the generated IDL declares the enum but no topic carries it.
`WeaponMountDto.FixedConeDeg` is a TKB field (built on every node from the catalogue). New TKB types 406, 6023–6025; new library entry.

## Gates

| gate | command (all after one build of each TEST project) | result | delta vs base `125acaaf2` | `--no-build` |
|---|---|---|---|---|
| T-1 the features' own suites | `dotnet test Fdp.Toolkits.Tests --filter AimAndFireExecutorTests\|FireProcessingSystemTests\|ReferenceLibraryTests\|RotorcraftModelTests\|FixedWingModelTests\|BombBallisticsTests` | 145 / 0 / 0 | +2 rails (F1 cone, F3 shell + rocket + ripple) | ✅ |
| catalogue | `dotnet test Hrot.Core.Tests` (incl. `NedTkbBuilderCombatTests`) | 247 / 0 / 0 | +3 rails (F1 405, F2 406, F2 B7 flight on 406's own numbers); A1 re-pinned: the rack is mount **0** of two | ✅ |
| presentation | `dotnet test Hrot.Presentation.Tests` | 598 / 0 / 12 skip | golden: **one line added** (406 after 405), nothing removed; `LR1` explosive set +6023 +6025 (new types on new mounts) | ✅ |
| toolkits | `dotnet test Fdp.Toolkits.Tests` | 3354 / 0 / 1 skip | +2 | ✅ |
| simhost | `dotnet test Hrot.SimHost.Tests` | 1357 / **30** / 6 | ⚠ the same 30 order-dependent reds as at the T-a gate (record/replay, `BlueprintStateTranslatorTests`, `ZoneEntityPersistenceTests`); **31 / 0 run alone** | ✅ |
| tree | `git status` after every suite | clean of test output | — | — |

## Ids allocated

None (progress lines on `CE-2173`).
