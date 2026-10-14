<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-14); scope frozen there.
updated: 2026-10-14
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/indirect-fire/DESIGN_Indirect_Fire.md — THE design: §1 measured facts, §3 UML, §4 IF-A…IF-G, §5 slice IF-1
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — the tubes and towed guns are weapon systems (Gunner seat, ControllerOf)
  - docs/DESIGN_Entity_Interactions.md — §7.5 the TwoMan carry the 81 / 120 mm tubes use
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Air_Weapons.md — finish that batch FIRST
-->

# HANDOFF — backend: indirect fire, slice IF-1 (the fire solution, the guns, the rounds)

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at the commit that adds this file**: your
scope is FROZEN there; documents that change after it are FYI. If a later document invalidates an item, **STOP that item and report it**; do
every item that is not blocked (R-106). ⏳ **Start after** the weapon-systems batch (WS-1…WS-4 and its two steers) **and** the air-weapons
handoff (F1–F4).

## 0. Why

🔒 User, `2026-10-14`: *"... new ammo, new high arc ballistic mode and automatic selection between low arc and high arc based on visibility
..."* · *"Artillery approved including demo acenario please write design"* (R-2018, CE-2176). ⭐ **The design is the contract:**
[DESIGN_Indirect_Fire.md](../../designs/indirect-fire/DESIGN_Indirect_Fire.md) — classes §3.2, the low/high flow §3.3, decisions §4.

## 1. Before anything

1. Merge `behaviors` at the dispatch commit or later. Push a `chore: started …` marker (rule 1b).
2. Ids from your block (`CE-3xxx`); progress on our row **CE-2176** as appended dated lines.

## 2. The items

| # | item | design | acceptance |
|---|---|---|---|
| **I1** | `FireSolution.For(world, from, to, mount, ammoProfile, ArcPreference) → Solution { Ok, Refusal (TooFar · TooClose · Masked), Charge, High, Launch, TimeOfFlight, Elevation }` beside `ThrowArc` — lowest charge that reaches; `Auto` = low when the low arc clears (`ThrowArc.Clears`, sampled densely enough for kilometres), else high within `MaxElevationDeg`, else refused | §3.3, IF-B, IF-C | a new `FireSolutionTests` next to `ThrowArcTests`: lowest charge; low in the open; high behind a wall and into a walled courtyard; too far, too close, masked |
| **I2** | `WeaponMountDto.Arc` (`IndirectArc` HighOnly · Both · LowOnly) + `MinElevationDeg` / `MaxElevationDeg`; `AmmoWeaponBallisticsDto.ChargeSpeedsMps` (vacuum-equivalent: `v = √(g · R_max)` per charge — say so in the doc comment) | IF-B, IF-C | the TKB DTO suites; a mount with no charges keeps today's single `MuzzleVelocity` |
| **I3** | `WeaponFireIntent.Charge` / `HighArc` — APPENDED (R-169, report it); `FireProcessingSystem` launches an indirect round at THAT charge and arc (no second decision on SimHost); 0 charges ⇒ today's path | §3.1 | `FireProcessingSystemTests`: the intent's charge and arc are flown |
| **I4** | arc dispersion: an indirect round gets the mount's spread too — azimuth θ (`HitModel.Deflection`) and a range error as a speed factor on a second ordinal stream. ⛔ today `FireProcessingSystem.cs:177` replaces the dispersed direction | IF-F | rounds from one tube scatter around the point, the same way on every run |
| **I5** | an arc round lives its time of flight + 10 s (today a flat 60 s, `ArcRoundLifetimeTicks`) | §1 | a 90 s high-angle round still lands |
| **I6** | `FireAtPointExecutor`: fires a CONTROLLED weapon as itself (WS-3 taught only `AimAndFireExecutor`; `MountEntity(owner, 32+k)` falls back to the owner) and holds until `FireAtPointParams.FireAt` (a `double`, sim seconds; 0 = now) with `Arc` (`ArcPreference`); params stay ≤ 32 bytes | FM-2, FM-4 | `FireAtPointExecutorTests`: a gunner fires the ground mortar he mans; nothing leaves before `FireAt` |
| **I7** | the rounds: 60 / 81 / 120 mm mortar HE (exists for 81 mm, 6002), 105 / 155 mm howitzer HE; `ammo.json` gains the howitzer rows; each round's charges per weapon | IF-A | the munition catalogue suites |
| **I8** | the guns: a 60 mm mortar (`OneMan`), 81 and 120 mm mortars (`TwoMan`) — class `Mortar` 1.1.*.10, a Gunner seat, `WeaponCategory.Mortar`, `Arc = HighOnly`; a 105 mm towed howitzer (`TowedArtillery` 1.1.*.5, an emplaced weapon system, `Arc = Both`); a 155 mm self-propelled howitzer (`SelfPropelledArtillery` 1.1.*.4, a vehicle with a turret, a preset crew — W-I); an `ArtilleryUnit` type (11.1.*.4) to be the battery. Report every number you had to invent | IF-G, FM-1 | the catalogue suites; the type-picker golden gains lines |

## 3. Fences

- ⛔ The behaviours lane owns the battery play, the Strike fields, the observer and the demo — a finding there is a REPORT.
- ⛔ No ground direct-fire number moves; `ThrowArc` keeps hand throws exactly as today.
- ⚠ Seats per type stay ≤ 8.

## 4. Report back — the gate report contract (CLAUDE.md rule 8)

One row per gate: verbatim command · pass / fail / skip · delta vs base · `--no-build` column · every red confirmed pre-existing against the
base sha · golden movement as a diff shape · tree clean after each suite · `tracker-counts.py --check` · every id allocated · the wire
appends. The design folded to the as-built (§3.2, §5 IF-1). Then the sha of each item on `backend`.
