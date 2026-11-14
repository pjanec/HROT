<!--STATUS
state: LIVE
updated: 2026-10-15
current-answer: the whole file. It is the report for HANDOFF_Behaviors_To_Backend_Area_Effects_Vehicles.md (AE1–AE4).
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Area_Effects_Vehicles.md — the handoff this report answers
  - docs/designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md — §7.5 the classes and sequence (backend's design step), §7.6 as built
  - docs/DESIGN_Building_Interiors.md — §3k W-6′ / W-7′: the fragment, blast, barrier and shadow model this reuses unchanged
-->

# Report — backend → behaviors: area effects against vehicles (AE1–AE4)

Scope frozen at the dispatch commit `786c35f78` (merged into `backend`); the started marker is `258499aad`. Branch **`backend`**. Design
step and as-built: [`DESIGN_Armour_Hit_Model.md` §7.5 / §7.6](../../designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md).

## Where each item is

| item | what | state |
|---|---|---|
| **AE1** | a HE / fragmentation warhead's fragment mass grows with its casing — Mott scaling `m = 0.5 g · (casing / 1.5 kg)^0.78` above the anchor; the grenade, the 40 mm and the RPG stay at 0.5 g; a shaped charge never scales; a TKB `WarheadDto` still overrides | ✅ |
| **AE2** | blast reaches hulls × the type's **blast susceptibility**, through the same closed-barrier transmission and shadow a person gets; the weapon brain's ammo estimate (`AmmoChoice`) reads the same rule | ✅ |
| **AE3** | every catalogued platform with a combat record states its susceptibility (table below) | ✅ |
| **AE4** | no new code: blast is an ordinary damage event, so the existing `MobilityKillBelowFraction` (the technicals: 0.6) immobilises a light vehicle before it dies | ✅ (by the existing rule) |

## ⭐ Decisions I took (leans for you)

| # | decision | lean | what would change it |
|---|---|---|---|
| **A1** | **Mott scaling, exponent 0.78** (a scaled shell's mean fragment ∝ s^(7/3), its casing ∝ s³) rather than the frame's example `^⅓` | keep — with `^⅓` a Mk-82 fragment is 2.2 g / 5.0 mm and only *just* touches a pickup's 5 mm; with Mott it is 16.5 g / 13.6 mm, the realistic order | if you want heavy shells' fragments weaker against light armour |
| **A2** | **anchor 1.5 kg of casing** = the RPG's 1.47 kg, the heaviest the fence protects; so the **60 mm mortar (1.3 kg) and the 25 / 30 mm HE also stay** at 0.5 g | keep | — |
| **A3** | **shaped charges never scale** — their library "casing" is the whole rocket or missile (a TOW's motor) | keep | — |
| **A4** | susceptibility **−1 = derived** `1 − side armour / 40 mm` for a type that states none (a TKB file); a person, and a hull with no platform record (the HMMWV, the transport aircraft), take the full blast | keep | if you would rather an unstated hull be immune |
| **A5** | the distance to a hull stays **to its centre axis**, not its skin | keep — a hull's 2–3 m radius is small against a bomb's 9 / 27 m radii; changing it moves every fragment number on vehicles | close bursts against big hulls looking too weak |

## Numbers I had to invent — the susceptibilities (AE3)

| type | value | why |
|---|---|---|
| Toyota pickup 106, technicals 104 / 105 | 1.0 | 5 mm sheet |
| weapon systems 600–612 | 1.0 | a weapon on a tripod or mount — no hull |
| FPV drone 403 | 1.0 | plastic and foam |
| AH-64 406 | 0.8 | its armour covers the crew, not the rotors |
| A-10 405 | 0.5 | the titanium cockpit tub; wings and engines are soft |
| MilitaryAPC 2001 | 0.3 | 20 mm sides |
| M109 107 | 0.25 | aluminium hull, 25 mm sides |
| Bradley 101 | 0.1 | blast reaches optics and running gear only |
| M1 100, T-72 103 | 0 | an MBT shrugs off blast; fragments and direct hits still count |
| HMMWV 102, UH-60 400, F-16 401, C-130 402, civilian car | (1.0 by the rule) | no combat platform record; giving them one would also give them health and armour — not this batch |

## ⚠ Numbers that MOVED (the fence asks for every one)

| what | before → after |
|---|---|
| one fragment's penetration (mm RHA) | 81 mm 1.65 → **2.24** · 70 mm rocket 1.67 → **2.32** · 105 mm 1.27 → **2.94** · 120 mm mortar 1.63 → **3.49** · 125 mm HE-frag 1.31 → **3.56** · 155 mm 1.33 → **4.63** · Mk-82 2.37 → **13.6** |
| **unchanged** (the fence) | M67 3.03 · 40 mm HEDP 1.52 · PG-7V 2.06 · 60 mm 1.69 · 25 mm HEI 1.38 · 30 mm HE 1.26 · 120 mm HEAT 1.16 · TOW-2 1.29 |
| ⚠ **people** | fragment DAMAGE is unchanged (it is the explosive's, not the mass's). What moves is what a heavier fragment passes: **a thin wall** (`FragmentTransmission`) and **body armour**. The rifleman's 5 mm front plate (type 200) now meets the 155 mm's fragments with a 0.3 chance and the Mk-82's with 1 (both 0 before); the 81 / 120 mm still do not pass it |
| ⚠ **vehicles** | a soft hull now takes blast (it took none): the acceptance numbers below. A light armoured hull (APC, M109) takes a share; MBTs none |
| ⚠ **the AI** | `AmmoChoice` counts blast against a soft vehicle (× its susceptibility) — **HE becomes more attractive against technicals and pickups**; unchanged against people and MBTs |

## Acceptance (§7.4)

| | measured |
|---|---|
| ① Mk-82 within ~25 m of a pickup destroys it | ✅ at 20 m: fragments pass the 5 mm (chance 1), blast 0.4 × 150 = 60 — the pickup takes ≥ 300 of 300 (rail). At 25 m the fragments alone are ≈ 38 000 × chance 1 |
| 155 mm within ~15 m heavily damages it | ✅ by the numbers: no blast at 15 m (injury radius 11.3 m), fragments 3960 × 0.70 × chance 0.32 ≈ 880 ≥ 300 |
| 81 mm within ~5 m damages it | ✅ by the numbers: blast 0.23 × 150 ≈ 34 of 300 (the fragments do not pass 5 mm) |
| ② an MBT outside the lethal radius is unhurt | ✅ rail: 15 m from a Mk-82, 0 blast (susceptibility 0), 0 fragments (13.6 mm against 350 mm) |
| ③ behind a building, less | ✅ rail: the same pickup 20 m away behind the house — no fragments, blast through the walls < 10 % |
| ④ no person number moves for grenades, 40 mm, RPG | ✅ their warheads are bit-identical (rail asserts their penetration equals the one-mass formula) |
| ⑤ `ua-air-strike` ③ live | ⏳ yours (you said you re-run it after the merge) |

## D-M4 — where breakable buildings would plug in

The barrier and the shadow come from the world query's crossings (`TraceCrossing.Loss`, `.ClosedBarrier`, `.TopZ`). A damaged wall changes
its crossing — a hole is no barrier, a collapse is a new prism — and `AreaEffect.ClosedBarrierTransmission` / `TerrainShadow` need no change.
The precedent for a burst changing the world is `BreachDoors` (W-9). Nothing built.

## Wire

None. `CombatPlatformDefDto.BlastSusceptibility` is a TKB field (built on every node from the catalogue); `DetonationEffect` is a local log
record; the reference-library coefficients are embedded data.

## Gates

Base: the started marker `258499aad`. One full-solution build (`dotnet build HROT.sln --no-restore`: **0 errors**, 3 min 15 s), then every
suite `--no-build`.

| gate | command | result | delta vs base | `--no-build` |
|---|---|---|---|---|
| T-1 the features' own suites | `dotnet test Fdp.Toolkits.Tests --filter AreaEffectSystemTests\|ArmorModelTests\|ReferenceLibraryTests\|AmmoChoiceTests\|TkbDtos` | 44 / 0 | +3 rails (AE1 library, AE2 rule, AE2 burst); every existing area-effect, armour and ammo-choice rail green unchanged | ✅ |
| red-proof | AE2's rail with `susceptibility = hull > 0 ? 0 : 1` (blast back to people only) | **1 / 1 FAILED**, as it must; source restored | — | — |
| toolkits | `dotnet test Fdp.Toolkits.Tests` | 3368 / 0 / 1 skip | +3 | ✅ |
| catalogue | `dotnet test Hrot.Core.Tests` | 250 / 0 | +1 (`NedTkbBuilderCombatTests.AE3_EveryVehicle_…`) | ✅ |
| presentation | `dotnet test Hrot.Presentation.Tests` | 598 / 0 / 12 skip | 0 — the type-picker golden did NOT move (no new types), the LR-1 explosive set unchanged | ✅ |
| NED | `dotnet test Hrot.Network.NED.Tests` | 142 / 0 | 0 | ✅ |
| simhost | `dotnet test Hrot.SimHost.Tests` | 1372 / **17** / 6 (a first run: 30) | ⚠ the known ORDER-DEPENDENT set, rotating between runs (time sync, egress shadow, descriptor update, record / replay, blueprint state) — the same classes as the air-weapons and IF-1 gates; **those 8 classes run alone: 42 / 0** | ✅ |
| docs | `tracker-counts.py --check` · `design-digest.py --check` · `rulings-check.py` · `mermaid-check.mjs DESIGN_Armour_Hit_Model.md` | OK (open 106 / done 386) · OK · 205 / 205 · 7 / 7 parse | — | — |
| tree | `git status` after every suite | clean of test output | — | — |

Not run: the E2E / cluster suites (T3) — nothing cross-node changed (no wire); the live check is `ua-air-strike` ③ (yours).

## Ids allocated

None — progress on `CE-2177`.
