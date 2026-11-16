<!--STATUS
state: LIVE
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../designs/animals/DESIGN_Animals.md — §6.1 the shared contract this note fills in (component ids, layouts, enum values)
  - HANDOFF_Coordinator_To_Backend_Animals.md — the dispatch (this note is its first required commit)
-->

# NOTE — backend → ui: the animals' contract, bound (CE-4623)

Declared on `backend` (`FDP/Toolkits/Fdp.Toolkits/Animals/AnimalComponents.cs`, `GlobalComponentIds.cs`, `LocomotionDto.cs`, `ActionStatus.cs`).
⭐ Fixed — bind to these now; the values never change (ids measured free on backend, behaviors, ui and coordinator `2026-11-16`).

## Component ids (namespace `Fdp.Toolkit.Animals`)

| component | id | layout (unmanaged, sequential) | who writes · who reads |
|---|---|---|---|

⚠ **`2026-11-16`, AB-10: the component ids MOVED 500–507 → 130–137** — 500–511 is R-44's test-only range (a test's own id collided in one test process) and the terrain lane took 500 for `PlacementGroup`. ⭐ Code that uses `GlobalComponentIds.<Name>` needs nothing; a literal number does.

| `Fear` | **130** | (backend's fear system — AB-3; ui does not need it) | backend |
| ⭐ `AnimalState` | **131** | `AnimalMode Mode` (byte) · `byte FearBand` (0 calm · 1 uneasy · 2 frightened · 3 panic) · `AnimalSpecies Species` (byte) · `AnimalFlags Flags` (byte) — **4 B**, replicated | backend writes on the brain node · ui reads (pose, gait, the alarm look) |
| ⭐ `FlockState` | **132** | `ushort Count` · `ushort InitialCount` · `uint Seed` · `FlockMode Mode` (byte) · 3 B pad — **12 B**, replicated | backend · ui draws `Count` birds from `Seed` in closed form (drop the highest indices first when `Count` falls) |
| ⭐ `HerdMember` | **133** | `Entity Herd` — **8 B** | backend · ui does not need it |
| `AnimalHome` | **134** | `Vector3 Centre` · `float Radius` · `float TetherMetres` (0 = none) · `Entity Area` · `byte Enclosed` · 3 B pad | backend · ui may draw a tethered dog's chain from `Centre` |
| `HerdState` | **135** | `Vector3 Centre` · `Vector3 NextGrazing` · `Entity Lead` · `AnimalMode Mode` · `AnimalSpecies Species` · 2 B pad | backend |

## Enums

| enum | values |
|---|---|
| `AnimalSpecies : byte` | `Cattle 0` · `Sheep 1` · `HorseHerd 2` · `HorseRiding 3` · `HorseWar 4` · `Dog 5` · `Cat 6` · `BirdFlock 7` · `Bird 8` · `Hen 9` · `Pig 10` · `Goat 11` (= TKB 700…711, the sound block's `s`) |
| `AnimalMode : byte` | `Calm 0` · `Alert 1` · `Flee 2` · `Recover 3` · `Rest 4` (lying / roosting) · `Guard 5` (a dog barking / attacking) |
| `AnimalFlags : byte` (flags) | `Bolting 1` (a ridden horse bolting) · `Dangerous 2` (attacking now) · `Calling 4` (mouth open) |
| `FlockMode : byte` | `Flying 0` · `Landed 1` · `Perched 2` |
| `LocomotionClass` (Fdp.Toolkit.Tkb.Domain) | ⭐ **`Quadruped = 10`** (ground) · ⭐ **`Bird = 11`** (air, never an aircraft) |
| `ActionReason` (the HUD reason) | ⭐ **`Bolting`** (appended after `Overheated`), text *"the horse bolts"* — the ridden-horse bolt (AR-10) through the same `WeaponPostureOf` / possession reason channel |

## Already fixed by the design (§4a / §6.1), restated
TKB 700–711 (+ 712 `AnimalArea`), DIS `3.1.0.200–208` / `3.2.0.205`, classes `Cattle` `Sheep` `Horse` `Dog` `Cat` `Bird` `Poultry` `Pig` `Goat`,
parents `LandAnimal` = `3.1.*.200-209`, `AirAnimal` = `3.2.*.205`; horse seats (703: 0 rider — fires WHILE riding, 1 second rider, a firing seat
sides + rear; 704: 0); sounds 7800–7895 = `7800 + species·8 + line`.

⭐ **Update (AB-3 … AB-5, backend):**
- `AnimalState` / `FlockState` REPLICATE as `dtAnimalState` **189** / `dtFlockState` **190** (Brain group, sent on change) — every node has them.
- `AirAnimal` is `3.2.*.205-209` (a range — `Bird` is exactly `3.2.*.205`, and the class table refuses two classes on one mask).
- The HUD's bolt cue: read **`AnimalFlags.Bolting`** on the horse (replicated); the local `ActionStatus` locomotion row also says `Bolting`.
- `MeleeWeapon` gains **`MeleeBite = 3`** (appended): the guard dog's bite arrives as a `StrikeNotification` like a blow — draw a snap, not a swing.
- `AnimalMode.Guard` + `Calling` = the dog barking; `Guard` + `Dangerous` = attacking. A flock: `FlockState.Mode` `Perched` → `Flying` (flushed) → `Landed` → `Perched`.
- ⭐ **AB-8 — the calls:** `UtteranceKind` **`AnimalCall = 26` · `AnimalAlarm = 27` · `Bark = 28`** on the animal's replicated `Utterance` (the one voice
  path) — play the species' line for it (`AnimalState.Species`, your 7800 + species·8 + line block). `SoundSourceClass` gains **`Animal = 8`** (an alarm / a
  bark as heard). The calm-call SCHEDULE is closed form — `Fdp.Toolkit.Animals.AnimalCalls.CallDue(entityIndex, simTime, CallInterval, herdMembers)` (`AnimalTemperamentDto.CallInterval`, a `SimDuration` since `095c21673` — was `CallIntervalSeconds`)
  — but the sim already sends each call as an `Utterance`, so ui needs only to play what arrives.
- `HerdState`'s last 2 bytes are now `ushort Members` (was padding) — backend's; ui does not read it.
- ⭐ **AB-9 — the animal area:** TKB **712** is registered (defaults `AnimalAreaDto`: species 0, count 12). An area entity = the one area tool's
  `EditablePolyline` + **`AnimalArea` (506)**: `AnimalSpecies Species` · `byte Enclosed` · `ushort Count` · `uint Seed` — **8 B**, authored and saved;
  ui's Details form edits these four. `AnimalAreaPopulated` (507, `SimInstant At`) is backend's run-time mark — never shown, never saved.
