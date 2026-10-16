<!--STATUS
state: LIVE
updated: 2026-10-15
current-answer: the whole file. It is the report for HANDOFF_Ui_To_Backend_Presets_Elevation_Attitude.md (P1–P4, CE-1102).
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_Ui_To_Backend_Presets_Elevation_Attitude.md — the handoff this report answers
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — §5a P1 (the presets), design step + as built
  - docs/designs/indirect-fire/DESIGN_Indirect_Fire.md — §3.10 P2 (laying the gun; §3.9 before the 2026-10-15 behaviors merge), design step + as built
  - docs/DESIGN_Dead_Reckoning.md — §9 P3 (pitch and roll on the wire), design step + as built
  - docs/DESIGN_Weapon_Layers.md — W-13e P4 (the active mount), design step + as built
-->

# Report — backend → ui: presets, the laid gun, pitch and roll, the active mount (P1–P4)

Scope frozen at the dispatch commit `64aaca945` (merged into `backend` at `4732af84c`); the started marker is `390d1313b`. Branch
**`backend`**. Tracker: [CE-1102](../Blueprint_Issues_Tracker.md) (presets, laid elevation, pitch/roll, active weapon).

## Where each item is

| item | what | state | design (step 1 + as built) |
|---|---|---|---|
| **P1** | the **HMMWV 102** carries an **M2 in a roof ring** (gunner seat 3 with a rifleman, exposed; ring station 4 with the M2, NeedsGunner) — its three passenger seats stay at 0–2; new **108 Technical (DShK)** and **109 Technical (minigun)** on the pickup body; 106 stays bare | ✅ | [Weapon Systems §5a](../../designs/weapons/DESIGN_Weapon_Systems.md) |
| **P2** | a mortar (609–611), the towed gun (612) and the M109 (107) are **laid before they fire**: `FireAtPoint` orders the gun's turret (Track at the point, the solution's arc and round), waits for the muscle's OnTarget, then fires; the pose is W-9's replicated `TurretPose` — no new field. The mortars and the towed gun get a **laying turret** (cheaper than a field: the turret part already replicates and the map already reads it) | ✅ | [Indirect Fire §3.9](../../designs/indirect-fire/DESIGN_Indirect_Fire.md) |
| **P3** | **pitch and roll** cross the wire: every ingress site rebuilds the rotation from heading + pitch + roll through ONE helper `SimTransformBridgeSystem.EulerDegToRotation` (the exact inverse of the egress conversion) — six sites, BDC included | ✅ | [Dead Reckoning §9](../../DESIGN_Dead_Reckoning.md) |
| **P4** | `WeaponPosture.ActiveMount` (APPENDED byte, also on `EntityWeaponPosture`): the mount the weapon brain fires — written by `AimAndFire` and `FireAtPoint` the moment they resolve it, kept when the weapon is lowered | ✅ | [Weapon Layers W-13e](../../DESIGN_Weapon_Layers.md) |

All four are in one commit, **`b6ad14c83`** — P2 and P4 share `FireAtPointExecutor` and the
weapon-brain suite.

## ⭐ Decisions I took (leans for you)

| # | decision | lean | what would change it |
|---|---|---|---|
| **L1** | a mortar / towed gun is laid by a **turret part**, not an elevation field | keep: the part is what W-9 replicates and `TurretPoseSource.PoseOf` already reads; a field would need a descriptor, a translator and a second reader | — |
| **L2** | the order carries the arc the brain's solution **chose** (Low / High), not the caller's preference | keep: the muscle never re-decides the arc | — |
| **L3** | the muscle solves **without the mask check** (per frame) | keep: identical to the brain's lay unless a mask made the brain step up a charge on the HIGH arc — then the drawn lay is off by that step while the round still flies the brain's charge | if you see a laid tube disagree with its shell's arc behind tall cover |
| **L4** | the solution aim is used **only for a laid order** (arc Low / High); an Engage order (Auto) keeps the one-speed arc | keep: Engage rounds carry no charge, so aim and flight stay matched; no existing number moves | — |
| **L5** | the HMMWV **itself** (102) carries the M2, no separate armed variant | as the handoff leaned | if scenarios need an unarmed HMMWV — then a bare variant |
| **L6** | the M109's turret keeps its 10 / 5 °/s: a worst-case lay (180°) takes 18 s, more than FM-4's 10 s lay time — **a time-on-target first round can be late** | ⚠ flag, not changed: the behaviors lane's play (FM-4) could start its clock after the guns report laid, or I raise the M109's traverse | your / behaviors' call |

## Numbers I had to invent

| what | value | why |
|---|---|---|
| the HMMWV ring gunner's seat | x −0.4 m, z 1.2 m, exposed | standing in the roof hatch, over the rear seats |
| the HMMWV ring | x −0.4 m, station z 2.0 m ⇒ the M2's pivot **2.3 m** | roof 1.83 m + the ring |
| the laying turrets | all round; mortars 45–85°, pivot 0.3 m; 105 mm −5–70°, pivot 1.0 m; **30 °/s traverse, 15 °/s elevation** | the mount's own elevation window; a full lay ≈ 6 s, inside FM-4's 10 s |
| 108 / 109 | the pickup body as 104 / 105; weapons 601 / 602 with their existing arcs (DShK −10…+70°, M134 −60…+30°) | — |

## The wire (all APPENDED, R-169)

| struct | field |
|---|---|
| `EntityWeaponTask` (`dtWeaponTask`) | `byte Arc` (the `ArcPreference`) |
| `EntityWeaponPosture` (`dtWeaponPosture`) | `byte ActiveMount` |
| `GeoSpatial` / BDC `Ori` | no change — `Pitch` and `Roll` were already sent; now they are READ |

## Acceptance

| | measured |
|---|---|
| P1 the catalogue | ✅ `NedTkbBuilderCombatTests.P1_TheHmmwv_CarriesAnM2_InItsRoofRing_ItsPassengerSeatsFirst`; `M9_TheTechnicals_…` extended with 108 / 109 |
| P1 a placed 102 holds an M2 + a gunner; save / load adds neither | ✅ `CreateEntityRequestSystemTests.P1_APlacedArmedVariant_HoldsItsWeaponAndGunner_AndALoadAddsNeither` (102 / 108 / 109 on the REAL catalogue: the weapon and the rifleman into the named seats, boarded, controlled by the vehicle, `CanFire`; the HMMWV's 3 passenger seats free; a request carrying `SeatPresetsFilled` spawns the vehicle only) |
| P1 the new technicals fire their station weapon | ✅ by the boarded `CanFire` above + the generic station rail `AimAndFireExecutorTests.WS_ThePickupFiresItsStationHmg_OnTheHmgsTurret` (the pickup body is the same `Pickup(...)`) |
| P2 laid before the first round, ± 0.5° | ✅ `AimAndFireExecutorTests.P2_AMortar_IsLaid_OnTheSolutionsElevation_BeforeItsFirstRound` — 10 s before `FireAt`, the tube's `TurretPose` is within 0.5° of the solution's elevation and at 90° ± 0.5° azimuth, OnTarget, nothing fired; the round then leaves at `FireAt` with the solution's charge and the high arc. ⭐ **On a receiving node**: this pose is the one `EntityTurretPoseTranslatorTests.WL1b_…` proves crosses the wire — composed, not one end-to-end rail |
| P2 the M109 turns before it fires | ✅ `P2_ASelfPropelledGun_TurnsItsTurret_BeforeItFires` (the M109's turret rates): no round while it says `Slewing`; the first at ≥ 8 s with the turret at 90° ± 0.5° |
| P2 the arc crosses | ✅ `EntityTurretPoseTranslatorTests.WL2b_TheBrainsOrder_ReachesTheMuscle_AndTheReportComesBack` (extended) |
| P3 roll / pitch within 1° | ✅ `GeoSpatialIngressTranslatorTests.P3_PitchAndRoll_CrossTheWire_AHeadingOnlySampleIsUnchanged` — (30, 10, −20), (250, −15, 45) round-trip to 0.01°; level ⇒ equal to the heading-only rotation, so ground numbers do not move |
| P4 the active mount | ✅ `P4_TheWeaponBrain_NamesTheMountItFires_OnThePosture` (the pickup's station M2 = 32, kept when lowered) · `P4_AGunner_FiringTheTubeHeMans_NamesIt_OnThePosture` (32) · it crosses: `WL2b_…` (extended) |

## Gates

Base: the started marker `390d1313b` (the ui merge `4732af84c` + the marker). One full-solution build (`dotnet build HROT.sln --no-restore`:
**0 errors**, 141 s), then every suite `--no-build`.

| gate | command | result | delta vs base | `--no-build` |
|---|---|---|---|---|
| T-1 the features' own suites | `Fdp.Toolkits.Tests --filter AimAndFireExecutorTests\|WeaponTaskDispatcherTests\|FireSolutionTests` · `Hrot.Core.Tests --filter NedTkbBuilderCombatTests` · `Hrot.Map.Common.Tests --filter GeoSpatialIngressTranslatorTests` | 110 / 0 · 19 / 0 (after the fix below) · 7 / 0 | +4 (P2 ×2, P4 ×2) · +1 (P1 HMMWV) and M9 +2 rows · +3 (P3 theory) | ✅ |
| red-proof (run) | P2 / P4: `FireAtPointExecutor` with the lay removed (`Laying.NoTurret`) + `SetActiveMount` writing nothing | **4 / 4 FAILED**, as they must; sources restored, rebuilt, green again | — | built |
| toolkits | `dotnet test Fdp.Toolkits.Tests` | 3372 / 0 / 1 skip | +4 | ✅ |
| catalogue | `dotnet test Hrot.Core.Tests` | 251 / 0 | +1 | ✅ |
| presentation | `dotnet test Hrot.Presentation.Tests` | 622 / **1** / 17 skip | the type-picker golden re-pinned on purpose: **+2 lines (108, 109), purely additive, no line changed or removed**. ⚠ The 1 red, `Map3DTests.Air_TheDisCategory_PicksTheKit(20, Helicopter)`, is **PRE-EXISTING at the base**: the ui lane's `f43f7f3c4` (CE-1103) made DIS category 20 draw as `AttackHelicopter` and left that test row; nothing here touches `Hrot.Presentation` (`git diff 390d1313b -- Hrot/Engine/Hrot.Presentation` is empty). ⇒ **ui: re-pin the row** | ✅ |
| NED | `dotnet test Hrot.Network.NED.Tests` | 142 / 0 | 0 | ✅ |
| BDC | `dotnet test Hrot.Network.BDC.Tests` | 8 / 0 | 0 | ✅ |
| map common | `dotnet test Hrot.Map.Common.Tests` | 66 / 1 | +3; the 1 red (`MapVisualOverlayIngressTranslatorTests.OnAReplica_TheOwnersOverlay_IsApplied`) **passes alone (2 / 0)** — order-dependent, untouched code | ✅ |
| simhost — the new rails | `Hrot.SimHost.Tests --filter EntityTurretPoseTranslatorTests\|CreateEntityRequestSystemTests\|GeoSpatialIntegrationTests` | 38 / 0 | +3 (P1 theory); `WL2b` extended | ✅ |
| simhost | `dotnet test Hrot.SimHost.Tests` | 1375 / **17** / 6 | ⚠ the known ORDER-DEPENDENT set (record / replay controller, branch pipeline, replay bootstrap, replay load) — the same classes as the IF-1 and AE gates; **those 4 classes alone: 17 / 0** | ✅ |
| **row 8 — the integration suite for P3** | `Hrot.IG.Tests --filter DeadReckoning` (the receiving node's dead reckoning over replicated transforms) + `GeoSpatialIntegrationTests` (in the simhost row) | 9 / 0 · green | 0 | ✅ |
| docs | `tracker-counts.py --check` · `design-digest.py --check` · `rulings-check.py` · `mermaid-check.mjs` on the four designs | OK (open 106 / done 386) · OK · 206 / 206 · 7 + 8 + 28 + 3 blocks parse | — | — |
| tree | `git status` after every suite | clean of test output | — | — |

⚠ One rail I had to fix on the way: `M9_TheTechnicals_…` asserted the weapon's own `Penetration` is 0 (the round's); the M134 (602) has always
stated 8 mm (the door gun's) — the table now carries the expected value per row, the existing rows unchanged.

Not run: the ClusterRunner E2E suites (T3). P3 is the one cross-node change; its invariant (ground numbers do not move) is pinned by the
level-sample case of the P3 rail and the IG dead-reckoning suite. A live banking-aircraft check on a cluster is the ui lane's after it
switches its drawings to the replicated values.

✅ **LIVE-VERIFIED `2026-10-16` (ui lane, CE-1123):** `ua-air-strike`, one process (`--mode all`), the A-10 ordered into a left turn over
HTTP: SimHost banks it to **−60°** with **11° nose-down** as it descends to 120 m; the **IG node reads the same roll and pitch** within a
frame, and both level the wings together after the turn. The drawing reads `SimTransform.Rotation`, so an IG map draws the bank.
🔴 Found on the way: the order itself answered 500 from the SimHost perspective — `AssignTacticalIntentEvent` registered only on the
Brain; fixed in `EntityOrders` (CE-1123, [Entity Control §S2 as built](../../DESIGN_Entity_Control.md)).

## The sha of each item

P1 · P2 · P3 · P4 — all **`b6ad14c83`** on `backend`.

## Ids allocated

None — progress on `CE-1102`.
