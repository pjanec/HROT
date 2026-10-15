<!--STATUS
state: LIVE
updated: 2026-10-15
current-answer: the whole file. It is the report for HANDOFF_Behaviors_To_Backend_Fragments_On_Hulls.md (AE5, CE-2177), and for the ui
  lane's STEER_Backend_P3_Every_Ingress_Restores_Attitude.md (its one open row, CE-1102).
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Fragments_On_Hulls.md — the handoff this report answers
  - docs/designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md — §7.7 AE5 design step + as built
  - docs/blueprints/batches/STEER_Backend_P3_Every_Ingress_Restores_Attitude.md — the ui steer whose row 7 is closed here
  - docs/DESIGN_Dead_Reckoning.md — §9 pitch and roll on the wire (row 7 folded)
-->

# Report — backend: fragments on hulls (AE5) and the steer's last row (heading-only patches keep the bank)

Branch **`backend`**. Before starting I merged `ui@ea2510d94` and `behaviors@2205078a4` (IF-2, IF-3, the AE5 handoff); the merge commit is
named in the tracker lines. Doc conflicts: the tracker (behaviors' rows already folded both lanes' lines — taken) and Indirect Fire, where
both lanes had written a `§3.7`: behaviors' IF-2 / IF-3 keep `§3.7` / `§3.8`, my laying-the-gun section moved to **`§3.9`** (only my own
files cited it; renumbered there).

## AE5 — a fragment on a hull is a hit ([design §7.7](../../designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md))

As the handoff leaned: **hits on the hull × one round's damage**. A hull (`HullHeight > 0`) is struck by
`FragmentCount · area / (2π r²) · (1 − r / reach)` fragments (a ground burst's hemisphere; the fade to 0 at the fragment reach is their lost
speed), each doing `FragmentHitDamage` — the kinetic law every bullet uses (`perSqrtJoule · √(½ m v₀²)`) — times the armour chance and the
exposure. Area = `2 · collider radius · hull height`. Both numbers are new derived fields on `WarheadDto`, computed by the reference library
from the inputs it already had (casing, fragment mass, Gurney speed). A person keeps the dose: **no person number moves**. `AmmoChoice`
reads the same rule (a burst at the hull's own radius).

### ⚠ Numbers that MOVED — every library warhead, a pickup (5 mm, 2.65 m × 1.9 m)

| warhead | fragments · one hit | 5 m | 15 m | 20 m | 120 m |
|---|---|---|---|---|---|
| **Mk-82** | 8 047 · 96 | 47 878 | 4 934 | **2 667** (was 41 638) — destroyed | **14** (was 1 253) — holed |
| **155 mm HE** | 6 058 · 33 | 3 776 | **372** (was ≈ 880) — still destroyed | 196 — mobility kill (< 60 %) | 0 |
| every other HE / frag in the library | — | 0 | 0 | 0 | 0 — their fragments do not pass 5 mm (unchanged: 0 before too) |

MBTs: 0 (13.6 mm fragments against 350 mm), unchanged.

### Decisions I took (leans for you)

| # | decision | lean | what would change it |
|---|---|---|---|
| **B1** | hits fade as `(1 − r/reach)` | keep — the person falloff's reach, so a hull and a man stop being hit at the same distance | if you want a pure 1/r² count to the reach |
| **B2** | a hand-written TKB `WarheadDto` with no `FragmentCount` keeps the person rule on hulls | keep — nothing to count with; no catalogue warhead is hand-written today (all come from the library) | — |
| **B3** | ⚠ **the M2 on the far technical still dies at 121 m**: a weapon system has no collider (no hull height), so it takes the person rule (its 60 hp against the dose) | flag — giving weapon systems a collider (a 0.5 m hull) would make them hulls here; it also changes sight and cover, so not in this item | your call |
| **B4** | the man at 121 m from a Mk-82 (the fence's question) | ⚠ not changed. A cheap answer: give a person the same count rule with a man's area (~0.7 m²) and a man-scale hit — 1 253 → ≈ 1 hit at 121 m. It would move every person number for every warhead, so it needs its own item | your call |

### Acceptance

| | measured |
|---|---|
| a pickup in the open 20 m from a Mk-82 is destroyed | ✅ `AreaEffectSystemTests.AE5_AMk82_DestroysAPickupAt20m_OnlyHolesOneAt120m_AndLeavesAnMbtUnhurt` |
| at 120 m it is damaged, not destroyed, mobility kept | ✅ same rail (0 < damage < 120 of 300) |
| an MBT unchanged (0) | ✅ same rail |
| grenade / 40 mm / RPG against people unchanged | ✅ person path untouched; `ReferenceLibraryTests` (unchanged, green) pin their numbers |
| `ua-air-strike` ④ live | ⏳ yours |

## The ui steer, row 7 — a heading-only patch keeps the bank

Rows 1–6 were already done by `b6ad14c83` (the steer's own STATUS says so). Row 7: `SimTransformBridgeSystem.WithHeadingDeg(current, h)` —
the current pitch and roll read with `RotationToPitchRollDeg`, rebuilt with `EulerDegToRotation` (no third formula; a never-set zero
rotation counts as level). Both heading-only patch paths call it: `SimTransformHeadingInstaller` (binary) and `AttributeCompilerFactory`'s
`"Heading"` (JSON). The test hook and the debug "set heading" stay heading-only, as the steer said.

| steer acceptance | measured |
|---|---|
| round trip h × p (±80°) × r (±60°) within 0.1° | ✅ `SimTransformBridgeSystemTests.P3_TheAttitude_RoundTrips_ThroughTheOneIngressConversion` (125 cases) |
| a heading-only patch on a banked body keeps its bank | ✅ `TheHeadingConversionIsSharedTests.AHeadingOnlyPatch_OnABankedBody_KeepsItsBank` (JSON and binary) · `SimTransformBridgeSystemTests.P3_AHeadingOnlyChange_KeepsPitchAndRoll` |
| per path, pitch 10° / roll −20° arrives | ✅ the NED ingress (`GeoSpatialIngressTranslatorTests.P3_…`) and the descriptor patch (`TheHeadingConversionIsSharedTests.TheDescriptorRoute_CarriesPitchAndRoll`). ⚠ The CGF initial-descriptor path and BDC call the same helper but have no rail of their own |

## Gates

Base: the merge of `ui@ea2510d94` + `behaviors@2205078a4` into `backend` (named in the tracker line). One full-solution build
(`dotnet build HROT.sln --no-restore`: **0 errors**, 233 s), then every suite `--no-build`.

| gate | command | result | delta | `--no-build` |
|---|---|---|---|---|
| T-1 the features' own suites | `Fdp.Toolkits.Tests --filter AreaEffectSystemTests\|ArmorModelTests\|ReferenceLibraryTests\|AmmoChoiceTests\|SimTransformBridgeSystemTests\|AimAndFireExecutorTests` · `Hrot.SimHost.Tests --filter TheHeadingConversionIsSharedTests\|AttributeCompilerFactoryTests\|SquadManeuverTests\|TacticalIntentResolutionSystemTests` | 274 / 0 · 127 / 0 | +1 AE5 rail, +126 bridge cases (125 round trip + 1), +3 heading rails | ✅ |
| red-proof (run) | AE5: `HullFragmentDamage` always null (the dose for every target) · steer: `WithHeadingDeg` = heading only · site 4: `DescriptorMapper` heading only | **5 / 5 FAILED**, as they must; sources restored, rebuilt, green again | — | built |
| merge check | behaviors' IF-2 / IF-3 rails (`SquadManeuverTests`, `TacticalIntentResolutionSystemTests`) on the merged tree, with my gun laying (P2) | 93 / 0 | — | ✅ |
| toolkits | `dotnet test Fdp.Toolkits.Tests` | 3499 / 0 / 1 skip | — | ✅ |
| catalogue | `dotnet test Hrot.Core.Tests` | 251 / 0 | 0 | ✅ |
| presentation | `dotnet test Hrot.Presentation.Tests` | 635 / 0 / 20 skip | 0 (the ui lane fixed its Map3D row) | ✅ |
| NED · BDC · IG dead reckoning (row 8 for the steer) | `Hrot.Network.NED.Tests` · `Hrot.Network.BDC.Tests` · `Hrot.IG.Tests --filter DeadReckoning` | 142 / 0 · 8 / 0 · 9 / 0 | 0 | ✅ |
| map common | `dotnet test Hrot.Map.Common.Tests` | 65 / 2 | the 2 reds (`MapVisualOverlayIngressTranslatorTests`, `MapRouteTranslatorTests`) **pass alone (17 / 0)** — order-dependent, untouched code | ✅ |
| simhost | `dotnet test Hrot.SimHost.Tests` | 1357 / 47 / 6 | ⚠ the known ORDER-DEPENDENT set (record / replay, egress shadow, descriptor update, blueprint state, zones, time sync, hill-attack baseline) — **those 11 classes alone: 102 / 1**. The 1 is **`SopParamsRoundTripTests.CE2079_EveryBehaviourReadsTheParamsAnSopOrderSends`: behaviors' IF-2 red, PRE-EXISTING at `behaviors@2205078a4`** — `StrikeOrder` gained the fire-mission fields and `AirPlatformStrike` (which by IF-D reads only `Location` / `Target`) is reported as ignoring them; the rail, `OrderDtos.cs`, `AirAttackBehaviors.cs` are byte-identical to the behaviors tip. ⇒ **behaviors: exempt the aircraft's fields in the rail** | ✅ |
| docs | `tracker-counts.py --check` · `design-digest.py --check` · `rulings-check.py` · `mermaid-check.mjs` | see the commit | — | — |
| tree | `git status` after every suite | clean of test output | — | — |

## The shas

The merge **`0c9905d94`**; AE5 and the steer's row 7 **`c313ba0d7`**, on `backend`.

## Ids allocated

None — progress on `CE-2177` (AE5) and `CE-1102` (the steer).
