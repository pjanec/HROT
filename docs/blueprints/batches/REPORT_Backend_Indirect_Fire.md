<!--STATUS
state: LIVE
updated: 2026-10-15
current-answer: the whole file. It is the report for HANDOFF_Behaviors_To_Backend_Indirect_Fire.md (IF-1, items I1–I8).
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Indirect_Fire.md — the handoff this report answers
  - docs/designs/indirect-fire/DESIGN_Indirect_Fire.md — §3.6 IF-1 as built (folded in the same commit)
  - docs/blueprints/batches/REPORT_Backend_Air_Weapons.md — the batch before this one (the 12-seat list the M109's crew uses)
-->

# Report — backend → behaviors: indirect fire, slice IF-1 (I1–I8)

Run overnight `2026-10-15`, autonomous (the user's scheduled instruction: *"leans on any decision go into the report for the user to approve
in the morning"*). Scope frozen at the dispatch commit; `behaviors@ee891028d` was already merged; the started marker is `a1b3f1d49` (base
`0fc70eaf9`). Branch **`backend`**. As-built: [`DESIGN_Indirect_Fire.md` §3.6](../../designs/indirect-fire/DESIGN_Indirect_Fire.md).

## Where each item is

| item | what | state |
|---|---|---|
| **I1** | `FireSolution` (`Fdp.Toolkits/Combat/FireSolution.cs`): the lowest charge that reaches, low arc first when the mount allows and it clears to the point, else high, else refused `TooFar` / `TooClose` / `Masked`; time of flight and elevation from the same closed form (`ArcLaunch`) | ✅ |
| **I2** | `WeaponMountDto.Arc` (`IndirectArc`: **`Unset`** = today's rule · `HighOnly` · `Both` · `LowOnly`) + `MinElevationDeg` / `MaxElevationDeg`; `AmmoWeaponBallisticsDto.ChargeSpeedsMps` | ✅ |
| **I3** | `WeaponFireIntent.Charge` (1-based, 0 = none) + `HighArc`; `FireProcessingSystem` flies that charge on that arc | ✅ |
| **I4** | arc dispersion: azimuth θ from `HitModel.Deflection`, range as a speed factor `1 + ½·σ·RangeSequence(k)` (a second Weyl stream, √2 − 1) — replayable, no random source | ✅ |
| **I5** | an arc round lives max(60 s, time of flight + 10 s) — `BallisticProjectile.ArcLifetimeTicks` | ✅ |
| **I6** | `FireAtPointExecutor` fires a CONTROLLED weapon as itself (32 + k, or mount 0 on a unit with no weapon of its own); holds Running + `AwaitingFireTime` until `FireAtPointParams.FireAt`; `Arc` (`ArcPreference`); refuses with `OutOfRange` / `TooClose` / `Masked`; 32 bytes | ✅ |
| **I7** | rounds 6026 60 mm · 6002 81 mm (charges added) · 6027 120 mm mortar HE · 6028 105 mm · 6029 155 mm howitzer HE; `ammo.json` + the two howitzer rows; five charges per mortar, seven / eight per howitzer | ✅ |
| **I8** | weapon systems 609 / 610 / 611 (60 / 81 / 120 mm mortars, `Mortar` 1.1.0.10.1–3) and 612 (105 mm towed, `TowedArtillery` 1.1.0.5.1); vehicle 107 M109 (`SelfPropelledArtillery` 1.1.225.4.1, a turret, crew of five, a brain); unit 304 Artillery Battery (11.1.225.4.5.1, three M109s) | ✅ |

⭐ **Fences kept:** no ground direct-fire number moved (every pre-IF-1 mount in `NedTkbCatalog` declares no arc — `IF1_TheMortars_TheHowitzers_AndTheBattery` walks every type; the LR-1 rail walks the whole host catalogue and still sees no warhead or penetration move); `ThrowArc`
flies hand throws exactly as before (12 samples; the new `samples` overload is used only by `FireSolution`); seats per type ≤ 8 (the M109
has five); the battery play, the Strike fields, the observer and the demo are untouched (IF-2…IF-4).

## ⭐ Leans for you to approve

| # | decision I took | lean | what would change it |
|---|---|---|---|
| **L1** | `IndirectArc` has an **`Unset` = 0** member: an existing mount (the M67 grenade, `MortarTeam` 2005's built-in tube) keeps `ThrowArc`, no solution | keep — it is what makes "no number moved" true by construction | if you want 2005 moved onto the solution now (it would then need charges and an elevation window) |
| **L2** | **`WeaponCategory.Howitzer = 10`** appended (per-shot brain, no fire on the move, the possession HUD says "HOWITZER") | keep — a howitzer is neither `Mortar` (HighOnly) nor `Cannon` (direct fire, fire on the move) | if the behaviours lane would rather key on `Arc` than on category |
| **L3** | charges live on the round's **generic** profile (`WeaponGuid = 0`) — each round has one gun today | keep; a weapon-specific profile already wins when one exists (`ChargesOf`) | a round shared by two guns with different charge tables |
| **L4** | the range error is a **speed factor ½·σ** on the second Weyl stream | keep for now; tune with the demo | the demo's bursts look too tight or too loose |
| **L5** | a **masked high arc tries the next charge** (a steeper arc may clear); a masked low arc stops the low search (a faster low arc is flatter) | keep — measured on the courtyard rail | — |
| **L6** | `FireAtPoint` on a station weapon also checks the **station arc against the point** (`OutOfArc`), as `AimAndFire` does against a target | keep — a door M134 cannot fire at a point behind the aircraft | — |
| **L7** | the battery is **three** M109s (a US battery has six) | keep for the demo; the roster is one line | a scenario that needs six |
| **L8** | ⚠ **cross-lane (ui):** `VisualFamilies.FamilyOfClass` maps class `SelfPropelledArtillery` (1.1.*.4) → the **Tank** kit, so the M109 draws as a tracked, turreted body. Two ui rails disagreed on it: `S1_EveryPlaceableBuiltInType_HasAFamily` needs a family for a type with a side; `CE2138_TheClassTable_KeepsEveryFamily_AcrossTheWholeDisSpace` + `S1_TheIconFallback_…` pin that no class moves. ⇒ the mapping stays and those two are **re-pinned on purpose** (their old-family oracles now say 1.1.*.4 → Tank) | keep until the ui lane gives artillery its own kit (the handoff's ui row; `Mortar` / `TowedArtillery` weapon systems stay `Unknown`, like 600–608) | the ui lane's own artillery kit |

## Numbers I had to invent (each with its source)

| number | value | source |
|---|---|---|
| top charge range | 60 mm 3.49 km · 81 mm 5.6 km · 120 mm 7.2 km · 105 mm 11.5 km · 155 mm 18.1 km | public maximum ranges (M224, M252, M120, M119 HE, M109A6 HE), rounded |
| intermediate charges | 60: .42 / 1.0 / 1.75 / 2.6 · 81: 1.0 / 2.2 / 3.4 / 4.5 · 120: 1.5 / 3.0 / 4.5 / 5.9 · 105: 3.5 / 4.5 / 5.5 / 6.8 / 8.0 / 9.8 · 155: 4.0 / 5.5 / 7.0 / 9.0 / 11.0 / 13.0 / 15.5 km | ⚠ **mine** — a plausible ladder, each charge's speed `√(g·R)` (IF-B) |
| elevation windows | mortars 45–85° · 105 mm −5–70° · M109 −3–75° | public figures, rounded |
| dispersion | 6 / 5 / 4 mils (60 / 81 / 120) · 3 mils (both howitzers) | ⚠ mine |
| reload (s) / rounds carried | 3 / 20 · 3 / 30 · 6 / 20 · 8 / 40 · 10 / 39 | ⚠ mine, except the M109's 39 rounds (public) |
| deploy / pack (s) and carry speed | 60 mm 20 / 15, 0.8 · 81 mm 60 / 45, 0.5 · 120 mm 90 / 60, 0.4 · the 105 mm is `Static` (emplaced, no towing yet) | ⚠ mine |
| library rows | `105mm howitzer HE`: 15 kg, 494 m/s, 2.1 kg TNT · `155mm howitzer HE`: 43.2 kg, 684 m/s, 6.6 kg TNT | public M1 / M107 figures; ⚠ NEW library entries (the Mk-82 precedent, CE-3201) |
| M109 | 27.5 t, 6.19 × 3.15 × 3.25 m, 15 m/s, armour 30 / 25 / 20 / 20 mm, 1200 hp, sees 2000 m, crew driver + gunner + commander (officer) + 2 loaders | public dimensions; armour, health and sensor mine |
| solver | clearance sampled every 25 m (12–400 samples); lifetime margin 10 s | mine (the margin is the design's) |

## Wire and recordings (R-169)

- `WeaponFireRequest` (NED) **APPENDS** `byte Charge; byte HighArc;` — egress and ingress map both. Old fields unchanged.
- ECS: `BallisticProjectile` grows (`uint ArcLifetimeTicks` after `FuzeRemaining`) and `WeaponFireIntent` gains two bytes — a recording made
  before IF-1 replays those with the old layout.
- TKB fields (`WeaponMountDto.Arc` / elevations, `AmmoWeaponBallisticsDto.ChargeSpeedsMps`) are built on every node from the catalogue.
- Enums appended: `WeaponCategory.Howitzer = 10`, `ActionReason.AwaitingFireTime / OutOfRange / Masked`. ⚠ `IndirectArc` is new; its
  zero member is `Unset`, not `Default` — `default` is an IDL keyword and the IDL generator scopes a descriptor file's enum members
  together (the first build failed on it).

## What the behaviours lane needs to know (IF-2)

- **The shared node is yours:** `FireAtPointNodeParams` and the behaviour DTO do NOT carry `fireAt` / `arc` yet — the node writes
  `Arc = Auto`, `FireAt = 0` (fire now), so every existing behaviour is unchanged. Add the two fields there (FM-2).
- **The member's mount:** a gunner names mount 0 (his primary — the tube he mans) or 32 + k; the executor fires the tube as itself.
- **Time on target:** `FireSolution.For(...).TimeOfFlight` is the per-gun flight the play subtracts from T (FM-4); the solver needs the
  gun's mount (`CombatTkb.MountOf`) and its charges (`FireSolution.ChargesOf(db, mount, ammo)`).
- **Why it does not fire:** the unit's `ActionStatus` weapon row says `AwaitingFireTime` (laid, waiting), `OutOfRange` / `TooClose` /
  `Masked` (no solution — the action FAILS), or the usual holds.
- `MortarTeam` 2005 is unchanged (a soldier with a built-in tube on `ThrowArc`); the new mortars are separate weapon systems.

## Gates

Base: the started marker `a1b3f1d49`. One full-solution build (`dotnet build HROT.sln --no-restore`, 156 projects: **0 errors**, 3 min 27 s),
then every suite `--no-build`.

| gate | command | result | delta vs base | `--no-build` |
|---|---|---|---|---|
| T-1 the features' own suites | `dotnet test Fdp.Toolkits.Tests --filter FireSolutionTests\|ThrowArcTests\|BallisticsSystemTests\|FireProcessingSystemTests` · `--filter AimAndFireExecutorTests` | 45 / 0 · 93 / 0 | +6 (`FireSolutionTests` ×4, the lifetime rail, the charge / arc / scatter rail) · +4 (the manned mortar, no-solution ×2, params ≤ 32) | ✅ |
| toolkits | `dotnet test Fdp.Toolkits.Tests` | 3364 / 0 / 1 skip | +10; ⚠ `CombatComponentTests.WeaponFireIntent_IsUnmanaged_AndHasCorrectSize` **re-pinned on purpose** 48 → 50 (the two new bytes) | ✅ |
| catalogue | `dotnet test Hrot.Core.Tests` | 249 / 0 | +2 (`NedTkbBuilderCombatTests.IF1_*`, `HrotEnvironmentTests.IF1_*`) | ✅ |
| presentation | `dotnet test Hrot.Presentation.Tests` | 598 / 0 / 12 skip | golden: **6 lines added** (107, 304, 609–612), nothing removed · `LR1` explosive set +6026 +6027 +6028 +6029 (new types on new mounts) · L8: two class-table oracles re-pinned (1.1.*.4 → Tank) | ✅ |
| NED | `dotnet test Hrot.Network.NED.Tests` | 142 / 0 | ⚠ `RoleGroupOwnershipStrategyTests` ×3 were RED before this batch: **WS-1** put `dtWeaponSystemState` (132) in the Brain group (Weapon Systems §5, line 309) and missed this rail — pinned here | ✅ |
| simhost | `dotnet test Hrot.SimHost.Tests` | 1359 / **30** / 6 | ⚠ the known order-dependent set (record/replay, `BlueprintStateTranslatorTests`, `ZoneEntityPersistenceTests`) — the same classes as the air-weapons gate; **31 / 0 run alone** | ✅ |
| docs | `tracker-counts.py --check` · `design-digest.py --check` · `rulings-check.py` · `mermaid-check.mjs DESIGN_Indirect_Fire.md` | OK (open 106 / done 386) · OK · 204 / 204 · 5 / 5 parse | — | — |
| tree | `git status` after every suite | clean of test output | — | — |

Not run: the E2E / cluster suites (T3) — no cross-node path changed except the two appended `WeaponFireRequest` bytes, which the egress /
ingress translators map and the full build compiles; the live demo is IF-4.

## Ids allocated

None — progress lines on `CE-2176`.
