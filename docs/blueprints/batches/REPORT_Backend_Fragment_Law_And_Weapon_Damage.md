<!--STATUS
state: LIVE
updated: 2026-10-15
current-answer: the whole file. The report for CE-3203 (the one fragment law + breakable weapons) and the answer to the ui handoff
  HANDOFF_Ui_To_Backend_Heading_Patch_Keeps_Attitude.md (CE-1110).
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Building_Interiors.md — §3k.1 W-6″, the law (design + as built)
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — §5b, how each weapon type breaks
  - docs/designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md — §7.7 AE5, now part of the one law
  - docs/blueprints/batches/REPORT_Backend_Fragments_On_Hulls.md — the AE5 report whose B3 / B4 leans this answers
  - docs/blueprints/batches/HANDOFF_Ui_To_Backend_Heading_Patch_Keeps_Attitude.md — the ui handoff answered in §3
-->

# Report — backend: one fragment law (people, hulls, weapons) and breakable weapons (`CE-3203`); the ui heading handoff (`CE-1110`)

Branch **`backend`**. Before the commit I merged `ui@54088ca74` (its two handoffs) and `behaviors@b98e9914b` (air slice 6, IF-4 demo
assets). One tracker conflict (both lanes on the air rows): kept the ui lane's `[x]` / U1–U3 notes and added behaviors' slice-6 sentence.

## 1. The law ([Building Interiors §3k.1](../../DESIGN_Building_Interiors.md))

🔒 *"No unrealistic numbers for soldier hitting, if it breaks something, tests should check that."* A man used to take a DOSE that stayed
lethal far past a round's casualty radius. Now a man, a hull and a weapon share one count: **hits × what one hit does**, where hits =
`N · A / (2πr² + A)` and the fragment slows as `e^(−r/λ)`. They differ only in area, armour and the damage of one hit.

| a standing man in the open (100 = dead) | lethal r | casualty r | 2 × casualty r |
|---|---|---|---|
| M67 grenade (5 / 15 m) | 162 | 17 | 4 |
| 60 · 81 · 120 mm mortar | 77 · 48 · 37 | 20 · 20 · 9 | 4 · 4 · 2 |
| 105 · 155 mm | 61 · 42 | 17 · 9 | 3 · 2 |
| Mk-82 (60 / 150 m) | 117 | 16 | 3 |
| 70 mm rocket · 125 mm HE-frag | 149 · 120 | 14 · 22 | 3 · 5 |
| PG-7V HEAT (2 / 5 m) | 126 | 20 | 5 |

A man 121 m from a Mk-82: **25** (was 1 253). Prone ≈ 0.2 × standing.

⚠ **Found by the calibration probe, fixed in this item:** a shaped charge's whole body counted as casing, so a PG-7V gave a man **1 014 at
5 m** and reached 113 m. Now only `shapedChargeFragmentingShare` = 0.02 of the body fragments (the rest is motor, fins and the liner that
becomes the jet). With that, the PG-7V's reach is 21 m.

## 2. Weapons ([Weapon Systems §5b](../../designs/weapons/DESIGN_Weapon_Systems.md))

Each type states its area, casing, health and blast share. A broken weapon (health 0) cannot fire (`WeaponHold.Destroyed`), arms nobody,
and is never chosen. It stays in its station.

| measured | M2 (80 hp, 1.5 mm) | Javelin CLU (20 hp, no casing) |
|---|---|---|
| 81 mm bomb at 10 m | 30 — damaged | 25 — **broken** |
| grenade at 3 m | ≥ 80 — **broken** | — |
| Mk-82 at 121 m | 2 | — |

## 3. The ui handoff — CE-1110 (a heading-only patch keeps the bank)

**Already built in `c313ba0d7`** (my previous push, 22 minutes before the handoff; ui's branch did not contain it). Both sites the handoff
names call `SimTransformBridgeSystem.WithHeadingDeg(current, heading)`. Its acceptance is the existing rails
`TheHeadingConversionIsSharedTests.AHeadingOnlyPatch_OnABankedBody_KeepsItsBank` (binary + JSON) and
`SimTransformBridgeSystemTests.P3_AHeadingOnlyChange_KeepsPitchAndRoll`, re-run green on the merged tree (§5). ⇒ the ui row can close.

## 4. Decisions I took (leans for you) — ✅ ALL APPROVED `2026-10-15` (*"approved C1-C5"*, R-3008)

| # | decision | lean | what would change it |
|---|---|---|---|
| **C1** | body armour stops fragments only over **35 %** of the body (the plates) | keep — a vest does not cover the head and limbs | a per-type coverage, if you want it |
| **C2** | the weapon numbers in §2's design table (area / casing / health) | keep — starter values; the guided launchers and the sentry are the fragile ones | your own figures per type |
| **C3** | a shaped charge fragments **2 %** of its body | keep — puts the PG-7V at its public 2 / 5 m radii | a per-warhead casing mass in `ammo.json` |
| **C4** | a weapon **carried** by a soldier is struck only if its carry seat counts as exposed — **not measured** for the soldier types | measure it next, then decide; a carried weapon being broken with its carrier seems right | ✅ **measured `2026-10-15`: it IS struck** — the soldier types' carry station is open (`PersonCarrier()`), so the carried Javelin is exposed; rail `CE3203_C4_AJavelinCarriedOnASoldiersBack_IsStruckWithHim`; Weapon Systems §5b. Matches the lean ⇒ no decision needed |
| **C5** | 25 / 30 mm autocannon HE give a man ≈ 70 / 50 at 5 / 8 m (one hit there is a serious wound) | keep for now — I found no public radius to check them against, so they are not in the rail | a published figure |

## 5. Gates

Base: `backend` after the merges of `ui@54088ca74` and `behaviors@b98e9914b`. One full-solution build (`dotnet build HROT.sln
--no-restore`: **0 errors**, 2 min 59 s), then every suite `--no-build`.

| gate | command | result | delta | `--no-build` |
|---|---|---|---|---|
| T-1 the features' own suites | `Fdp.Toolkits.Tests --filter AreaEffectSystemTests\|ArmorModelTests\|ReferenceLibraryTests\|AmmoChoiceTests\|AimAndFireExecutorTests\|DroneImpact\|WarheadFlight\|BombBallistics` | 157 / 0 | +12 W6pp cases, +1 CE3203 weapon rail | ✅ |
| red-proof (run) | ① the stated area ignored (a weapon hit as a man) · ② `CanFire` without the health check · ③ a man on a bullet's per-hit damage · ④ a man on the old dose | ① the fragility rail red · ② the broken-weapon rail red · ③ 10 of 11 calibration rounds red · ④ all 12 W6pp cases red; sources restored, rebuilt, **12 / 12 green** | ⚠ removing the DRAG alone turned nothing red: it moves a man ≤ 40 % inside twice the casualty radius, inside the bands — the drag is not pinned on its own | built |
| re-pinned | `ReferenceLibraryTests`: `FragmentRadiusM` is now the law's hazard radius — M67 12–20 → **45–65 m** (54), 81 mm 30–50 → **110–150 m** (131), Mk-82 130–160 → **380–500 m** (442) | green | deliberate: the field's meaning changed (its one reader is the area effect) | ✅ |
| toolkits | `dotnet test Fdp.Toolkits.Tests` | 3512 / 0 / 1 skip | +13 | ✅ |
| catalogue | `dotnet test Hrot.Core.Tests` | 252 / 0 | +1 (`CE3203_EveryWeaponSystem_StatesHowItBreaks`) | ✅ |
| simhost: heading (CE-1110) · squads · air · detonation wire · creation | `Hrot.SimHost.Tests --filter TheHeadingConversionIsShared\|AttributeCompilerFactory\|SquadManeuver\|TacticalIntentResolution\|AirAttackNodes\|SimHostCoreLogicPack\|MunitionDetonation\|CreateEntityRequestSystem` | 181 / 0 | 0 — on the merged tree, behaviors' slice 6 included | ✅ |
| editor combat report (`fragmentHits`, `target`) | `Hrot.Editor.Tests --filter CombatReport` | 2 / 0 | 0 | ✅ |
| presentation | `dotnet test Hrot.Presentation.Tests` | 635 / 0 / 20 skip | 0 | ✅ |
| IG effects · dead reckoning | `Hrot.IG.Tests --filter EventToEffect\|AdvancedFeatures\|DeadReckoning` | 24 / 0 | 0 | ✅ |
| docs | `tracker-counts.py --check` · `design-digest.py --check` · `rulings-check.py` · `mermaid-check.mjs` (3 designs) | OK (open 106 / done 386) · OK · 206 / 206 (8 staleness warnings, as before) · 52 blocks parse | — | — |
| tree | `git status` after every suite | clean of test output | — | — |

⚠ Not run: the E2E / system suite (T3) and a live `ua-air-strike` / `ua-indirect-fire` run — the behaviours lane's demos will now show
smaller casualty counts at range (a man 121 m from the bomb is wounded, not killed).

## The shas

The merges `49da27177` (ui) and `8bdbca21a` (behaviors); CE-3203 and the CE-1110 answer **`84c27344e`**, on `backend`.

## Ids allocated

**`CE-3203`** (backend block). Answered: `CE-1110` (ui's row, a line added — cross-lane, said in the commit).
