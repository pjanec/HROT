<!--STATUS
state: LIVE — for terrain (MS-T1 tuning); a finding, nothing changed in your files
updated: 2026-11-16
current-answer: the whole note
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — MF-2, MR-10 (the AT burst on the hull's bottom), MR-16
  - NOTE_Coordinator_To_Behaviors_Minefield_Scenarios.md — the feature scenarios (R-4277)
-->

# NOTE — behaviors → terrain: `mf-at-vs-vehicle` fails on the anti-tank mine's EFFECTS (measured)

⭐ The feature scenario `scenarios/demos/minefields/mf-at-vs-vehicle` (R-4277): an enemy AT field (110–150, 50–110, a mine every 4 m); a Friend man walks
across it at 1.5 m/s on y = 70; a Friend M1 (TKB 100) drives across at 6 m/s on y = 95. `scripts/scenario-regression.sh mf-at-vs-vehicle`, `2026-11-16`:

| check | result | what it shows |
|---|---|---|
| the man gets across | ✅ PASS (61 s) | ⭐ an AT mine does not go off under a man (the trigger's weight rule works) |
| a mine goes off | ✅ PASS (11 s) | the tank sets one off on entering the field |
| the tank reacts MineStrike | ✅ PASS (11 s) | behaviors' drill runs, the tank stops (it never crosses) |
| ⛔ **the tank is hurt** | ❌ FAIL | `Health.Current` never drops below its starting value |
| ⛔ **the man is never hurt** | ❌ FAIL (11 s) | he is hurt AT THE TANK'S BURST, ~35 m from it (he is at x ≈ 86, y = 70; the tank at x ≈ 110, y = 95) |

| lead (from the code — not measured beyond the run above) | where |
|---|---|
| ① the M1's TKB sets no `ArmorBottom`, so the Bottom facing falls back to `ArmorRear`; the burst's 120 mm (`AntiTankMinePenetrationMm`) does not get through | `ArmorModel.cs:83`, `MunitionTkbCatalog.cs:160` |
| ② the AT mine's munition is registered with NO warhead (`Register(tkb, MineAntiTankName, …, penetrationMm, damage)` — no `WarheadDto`), so its blast radii come from a default that reaches a man ~35 m off | `MunitionTkbCatalog.cs:328` |

⭐ The behaviour side (MS-B1) holds: the tank's drill fires once and stops it. ⛔ I did not tune your munition or the M1's armour — your MR-10 starter
values ("tune with the armour's `ArmorBottom`"). The scenario stays in the suite, failing, until they move; it passes as soon as an AT burst hurts
the tank from below and spares a man 35 m away.
