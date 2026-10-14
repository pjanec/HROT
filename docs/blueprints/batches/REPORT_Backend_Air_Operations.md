<!--STATUS
state: LIVE
updated: 2026-10-14
current-answer: the whole file. It is the report for HANDOFF_Behaviors_To_Backend_Air_Operations.md (E1–E5).
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Air_Operations.md — the handoff this report answers
  - docs/designs/air-attack/DESIGN_Air_Operations.md — the as-built for all four items: §5a route on the intent (E2), §5b the helicopter (E3), §6a the plane, §8a the bomb (E4)
  - docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md — E1 (§4a ⚠ row), the Mk-82 note (E4)
  - docs/designs/motion-models/DESIGN_Motion_Domains.md — the Rotorcraft and FixedWing as-built notes
  - docs/designs/navig-2/Navigation_Design_v2_0.md — §7.1 RouteEntityId (E2), the C8 row (E3)
-->

# Report — backend → behaviors: air operations (E1–E5)

Scope frozen at the dispatch commit `65ff1d032`, merged into `backend` in `fbdbedac8`. The started marker is `4df8c1f88`. Branch: **`backend`**.
Each item's as-built is folded into its owning design. Deviations are listed below with their reasons.

## Where each item is

| item | what | sha on `backend` | design fold-back | state |
|---|---|---|---|---|
| E1 | CE-2170 — a guided missile's launcher hold reads `MissileOf(ammo).SpeedMps` first | `0f9f7ad6f` | Missiles §4a | ✅ |
| E2 | slice 1 — waypoint fields + `NavigationIntent.RouteEntityId`; CE-2171 confirmed and fixed | `c5b8b8e9f` (+ tracker `272eb90ca`) | Air Ops §5a, ROUTES1 §6, Nav v2 §7.1 | ✅ |
| E3 | slice 2 — `AirRoute`/`AirPath`, the `RotorcraftModel` route follower, take-off/landing/holds, 404 flight data | `dc45879ce` | Air Ops §5b, Nav v2 C8, FPV §6, Motion Domains | ✅ |
| E4 | slice 4 — `FixedWingModel`, A-10 (405), air launch velocity, Mk-82 (6022), `BombBallistics`, the Release profile | `992329a2e` | Air Ops §6a + §8a, Motion Domains, Missiles | ✅ |
| E5 | slice 6, backend half — `FixedWingModel.Manual` mouse-aim pilot · `RotorcraftModel.Manual` tilt + hover hold | `a71724e9c` | Air Ops §6b, Motion Domains | ✅ |
| CE-3198 ⏸ | NLAW overfly | — | — | stays queued, as the handoff says |

## E2 — CE-2171 (a FollowRoute order never moves the unit on a split cluster), confirmed then fixed

**Two causes**, both confirmed:
- ① The brain waited for a local trajectory-pool id that never crosses the wire.
- ② `MapRouteEgressTranslator` was registered only in the Muscle pack, while scenario routes are owned by CGF, so the route plan never left CGF.

**The fix:**
- `FollowRoute` writes `NavigationIntent.RouteEntityId`, the route's network id.
- The Muscle resolves its own pool id from that id through `IRouteSource` (`HrotRouteSource`).
- Both MapRoute translators moved into `SharedTranslatorPack`.

**Integration rail:** `EntityOrdersAcrossNodesTests.CE2171_AFollowRoute_OnASplitCluster_DrivesTheRoute`.
- ⛔ **red at `fbdbedac8`**: the tank's intent stays `None` and it never moves.
- ✅ **green after.**
- It runs in-process over DDS, with CGF, SimHost and the editor as separate nodes. That is the integration suite's split cluster, not a `--mode all` live run.

## Gates

Each TEST project is built once (`dotnet build <tests.csproj> --no-restore`); every run after that is `--no-build`.
- After every suite the only changes in the tree were my own edits; no test wrote a golden.
- Before the E4 commit, dll timestamps were checked against the edited sources.

| gate | verbatim command | E2 | E3 | E4 (final) | `--no-build` | delta / note |
|---|---|---|---|---|---|---|
| Fdp.Toolkits | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build` | 3310 / 0 / 1 | 3313 / 0 / 1 | 3321 / 0 / 1 | yes | E4 +8: `FixedWingModelTests` ×3, `BombBallisticsTests` ×3, `A4_…`, `A6_…`. `RotorcraftModelTests.CE2168_*` re-pinned: FixedWing is moved now; Surface still throws. The 1 skip pre-exists |
| Hrot.Core | `dotnet test Hrot/Engine/Hrot.Core.Tests --no-build` | — | 241 / 0 / 0 | 242 / 0 / 0 | yes | E3 `B7_TheDoorsOffBlackHawk_…`; E4 `A1_TheAttackPlane_IsAFixedWing_WithFourBombs` |
| Hrot.Presentation | `dotnet test Hrot/Engine/Hrot.Presentation.Tests --no-build` | — | 598 / 0 / 12 | 598 / 0 / 12 | yes | E4 re-pins, purely additive: the `CE2138` picker golden gains one line (`405 A-10C Thunderbolt II …`) with nothing removed; `LR1`'s explosive set gains `TkbMk82Bomb` |
| Hrot.SimHost | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --no-build` | FollowRoute 11 / 0 | 1364 / 0 / 6 | 1364 / 0 / 6 | yes | the 6 skips pre-exist |
| Hrot.Network.NED | `dotnet test Hrot/Network/Hrot.Network.NED.Tests --no-build` | 142 / 0 | 142 / 0 | 142 / 0 | yes | the appended wire fields round-trip |
| Hrot.Map.Common | `dotnet test Hrot/Engine/Hrot.Map.Common.Tests --no-build` | 64 / 0 | — | — | yes | route waypoint fields |
| Fdp.Core | `dotnet test FDP/Engine/Fdp.Core.Tests --no-build` | — | 1240 / 0 / 22 | 1240 / 0 / 22 | yes | E3: `OneSharedJsonOptionsTests`' ParamsDtoAsset exemption shrinks 2 → 1. The rail tells you to do exactly that when the count drops; it had been red since `da8598a84` |
| Blueprints | `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --no-build` | 4209 / 0 / 17 | — | — | yes | E2: `CatalogTests` — FollowRoute now projects 3 pins (behaviors' suite, cross-lane re-pin) |
| ⭐ integration, E2 | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests --no-build` (the full suite) | 34 / 1 / 1 | — | — | yes | the 1 red is `EqsDistributedTests.DangerAlongRoute_AcrossHosts` (CE-3138); it fails identically at `4df8c1f88` (line 520, the Brain's rating), so it pre-exists. The skip pre-exists. CE-2171 went red → green |
| ⭐ integration, E3/E4 (row 8: motion and fire) | `… --no-build --filter "EntityOrdersAcrossNodesTests\|DistributedBrainMuscleIntegrationTests\|SubEntityCascadeDestroyTests"` | — | 9 / 0 / 1 | 9 / 0 / 1 | yes | the skip (`CgfAiIntent_ReachesSimHost_ViaDds`) pre-exists |
| docs | `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · `mermaid-check.mjs` (touched designs) | ✅ | ✅ | ✅ open 106 / done 386 · 200/200 · ✅ · ✅ | — | — |

**E5 gates** (on `a71724e9c`, all `--no-build`, the TEST project built):

| gate | result | delta |
|---|---|---|
| `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build` | 3324 / 0 / 1 | +3 (the three `B3_*` rails); the skip pre-exists |
| ⭐ integration (the drone's cross-node possession, since `RotorcraftModel.Manual` changed): `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests --no-build --filter "FullyQualifiedName~EntityOrdersAcrossNodesTests\|FullyQualifiedName~DistributedBrainMuscleIntegrationTests"` | 8 / 0 / 1 | `M7_APossessedDrone_FollowsTheStation_AcrossNodes` still green; the skip pre-exists |
| `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --no-build` | 1364 / 0 / 6 | unchanged |

**Red-proofs** (each production change removed, the TEST project rebuilt successfully, the rail run):
- **E2:** CE-2171 red at the merge base.
- **E3:** the three `RotorcraftModelTests` rails go red with the route follower off.
- **E4:**
  - `A4_AnAircraftsBomb_KeepsItsVelocity_AGroundShotDoesNot` goes red with the air-launch branch removed.
  - `A6_TheReleaseProfile_WaitsForTheCue` goes red with the release gate removed. The build succeeded (dll 18:35:53), and both rails were green again after the restore.
  - ⚠ The first E4 attempt used `if (false)`. That raises CS0162, which this repo treats as an error, so the run used a stale binary. It was redone with a real edit.
- **E5:** with the mouse-aim pilot bypassed and the helicopter's tilt not written (the build succeeded, dll 18:53:19), all three `B3_*` rails were red. With the code restored (dll 18:53:49) they were green again: 12/0 across the `B3_` filter. ⚠ The first E5 attempt used an early `return`, which raised CS0162, so that run used a stale binary. It was redone with a break that only applies at runtime.

## Wire (R-169)

| change | item | shape |
|---|---|---|
| `MapRoute.Waypoint`: `Kind`, `HeightAgl`, `Hold`, `HoldSeconds`, `HoldUnit` | E2 | appended |
| `NavigationIntent.RouteEntityId` | E2 | appended |
| `NavigationPhase` values 5–9 (TakingOff, Landing, Landed, Unloading, Loading) · Flags bit 2 = `ReleaseHold` | E3 | ⭐ no shape change: new values in the existing byte and flags fields |
| `ActionReason.AwaitingRelease` | E4 | appended enum value, not a wire field |

No other wire change.

## Deviations (each folded into Air Ops)

- **E3** `AirRoute.Compile` rounds with `CornerRounding.Round(..., shiftOut: false)`. The ground's outward C3 shift put a kink in the air path that broke B2, so a new optional parameter was added; the ground default is unchanged.
- **E3** Curvature is measured as Menger curvature over densified 20 m legs.
- **E4** A Ground waypoint on a FixedWing route is removed from the path, with an `FdpLog` warning (B9: it is flown over, never landed on). Holds are removed too. The plane circles the last point.
- **E4** Fixed-wing turn radius is cruise² / (g·tan max bank); the stall speed is a hard floor.
- **E5** The plane's mouse-aim turn is coordinated: the bank drives the yaw, and the yaw never passes the look. The throttle is a level: 0 is cruise, +1 top speed, −1 the stall speed.
- **E5** `RotorcraftModel.Manual` keeps its direct axes. Its hover hold is the existing "no axis ⇒ stay put", now with the body levelling out. No acceleration limit was added, because `M7_ARotorcraft_Hovers_ClimbsAndYaws_UnderManualDrive` and the drone's cross-node possession rail pin the direct response.
- **E4** The Mk-82 takes its warhead from the library entry nearest to it, the 120 mm mortar HE, with `Indirect = true`.
  - Penetration 50 and damage 1200 are stated on the munition.
  - **CE-3201** is filed: the warhead library has no bomb.
- **E4** For a bomb, the Release profile skips the seen-now gate, the aim clock, BodyAim and the friendly-line check. The cue (`BombBallistics.ReleaseNow`: impact within ½ dt ahead, not more than 0.5 s behind, |cross| ≤ 10 m) is the only gate. `HoldOf` reports `AwaitingRelease` until it says now.

## Findings (id allocated: CE-3201)

- **CE-3201**: the warhead library has no bomb entry. Lean: add a Mk-82 entry to `ammo.json`.
- ⛔ **behaviour lane, report only:** `FireAtPointExecutor` has no release gate, so a Strike on a POINT with a bomb would drop at once. It is yours to decide in slice 5. `AimAndFireExecutor.ReleaseCue(world, unit, target)` is public, so you can reuse it.
- **ui:** `PossessionStation`'s WeaponCategory label has no `Bomb` case. The station half of slice 6 (`ControlScheme.FixedWing`, the camera following the attitude, the HUD) is still to build, and it can drive E5 as is: the camera's look heading goes in `ManualDrive.Yaw`, the climb in `Vertical`, the throttle in `Forward`.
- ⚠ **A manual plane flown into the terrain crashes through `DroneImpactSystem`, which bursts mount 0, the A-10's Mk-82 rack.** The design does not say what a plane crash should do. No id is filed; it is yours to decide, along with slice 5.
- **CE-3138**: still red, pre-existing (above). A progress line is added to its tracker row.

No ground combat number moved. The launch change runs only for an **Air-domain** shooter (`Locomotions.InAir`): a Bomb mount drops through `BombBallistics.Launch`, and any other round adds the carrier's velocity. A ground shooter never enters that branch, which `A4`'s ground half pins.
Behaviour-lane surfaces (`AirAttack`, `Strike`, `AirPlatform*`, `SquadEmbark`, `LoadCasualty`, SOP rows, scenarios) were not touched.
Cross-lane edits:
- behaviors' `CatalogTests` (E2);
- the presentation re-pins (E4);
- the `Hrot.Blueprints` FollowRoute node (`CgfNodes.cs`, E2): the brain's route wait. This is the CE-2171 fix, which the handoff assigned to me.
