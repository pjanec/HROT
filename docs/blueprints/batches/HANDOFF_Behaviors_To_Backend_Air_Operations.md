<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-14); scope frozen there.
updated: 2026-10-14
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/air-attack/DESIGN_Air_Operations.md — THE design (classes §3, sequences §4, module picture §2, rules §5–§9, decisions §10, slices + rails §11)
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Ui_Air_Operations.md — the ui half (the route editor; later the pilot station)
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Vehicle_Tactics_2.md — the previous handoff; its C5 (CE-3198) stays queued
-->

# HANDOFF — backend: air operations (routes, helicopter take-off and landing, the plane, the bomb)

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at the commit that adds this file**:
your scope is FROZEN there; documents that change after it are FYI. If a later document invalidates an item, **STOP that item and report
it**; do every item that is not blocked (R-106).

## 0. Why

🔒 User, `2026-10-14` — air operations A1–A9 and B1–B11 *"Approved"* (R-2015): aircraft follow a waypoint route, a helicopter takes off and
lands on its own, the AI flight may be faked to look believable, one order set for every aircraft, and three demo scenarios — a strike on a
point, a helicopter air assault, and a rescue round trip. ⭐ **Build against [DESIGN_Air_Operations.md](../../designs/air-attack/DESIGN_Air_Operations.md)**:
its §3 class diagram and §4 sequences are the contract. Report any deviation and fold it into the design (CLAUDE.md obligation ⑤).

## 1. Before anything

1. Merge `behaviors` at the dispatch commit or later. Push a `chore: started …` marker (rule 1b).
2. Ids: new findings from **your** block (`CE-3xxx`); progress on our rows (CE-2170, CE-2171, CE-2173) as appended dated lines.

## 2. The items — in this order

| # | item | design | acceptance |
|---|---|---|---|
| **E1** | **CE-2170** — the launcher's guided-flight hold reads `CombatTkb.MissileOf(ammo).SpeedMps` first, then the mount, then the state | Missiles §4a ⚠ row | rail in `AimAndFireExecutorTests`: a TOW at 2 km holds ≥ 7 s (red before the fix) |
| **E2** | **slice 1 — the route on the intent** (B1): `RouteWaypoint` + `Kind`, `HeightAgl`, `Hold`, `HoldSeconds`, `HoldUnit` (ECS, `RoutePlanTranslator` save, `MapRoute.Waypoint` APPENDED fields — report it, R-169) · `NavigationIntent.RouteEntityId` (appended) written by `FollowRoute`; the Muscle resolves its own pool id from it. ⭐ First **confirm CE-2171** on a `--mode all` cluster (runbook), then fix | §5, §1 | `RoutePlanTranslatorTests` + the `MapRoute` translator rails round-trip the fields; a ground `FollowRoute` arrives on a split cluster (live), red before |
| **E3** | **slice 2 — the helicopter** (A9, B2, B7): `AirRoute.Compile` (reuses `CornerRounding.Round`, navmesh null, heights above the terrain) + `AirPath` (Muscle-local) · `RotorcraftModel.Autonomous` follows the path with an acceleration limit, nose tilt and roll in `SimTransform.Rotation` · Ground waypoints: take-off, approach, hover, descend, touchdown on `BodyGeometry.RestingPose`, holds (Seconds timed here; Unload / Pickup wait for `Flags.ReleaseHold`) · `NavigationPhase` + `TakingOff`, `Landing`, `Landed`, `Unloading`, `Loading` (values only — the wire field is the existing byte) · `FlightParamsDto` + `HoverHeightM`, `TurnRadiusM` · the doors-off UH-60 (404) gets `Gen.Locomotion Rotorcraft` + `Gen.Flight` (it is data only today) | §5, §6, §4b | `RotorcraftModelTests.B7_ARouteFromTheGround_TakesOff_FliesAndLands_OnItsGear` · `B7_AGroundWaypoint_HoldsUntilReleased_ThenTakesOff` · `B2_TheSmoothedPath_PassesEveryWaypoint_WithinTheTurnRadius` · a NED rail for the 404 type |
| **E4** | **slice 4 — the plane and the bomb** (A1, A3, A4, A6, B6): `FixedWingModel.Autonomous` (kinematic `AirPath` follower at cruise ≥ stall, bank from curvature eased by a roll rate, pitch from the climb, overflies; `MotionModelTable` row) · an attack-plane TKB type (A-10 / Su-25 class, ~150 m/s, `Gen.Locomotion FixedWing`, a `Bomb` mount) · `FlightParamsDto` + `StallSpeedMps`, `CruiseSpeedMps`, `MaxBankDeg` · `FireProcessingSystem`: an **Air-domain** shooter adds its velocity to the launch (ground shooters unchanged) · the Mk-82-class munition (6022: `Indirect` + area warhead from the library's nearest entry) + `WeaponCategory.Bomb` · `BombBallistics` (impact / time to fall / cue, stepping `BallisticsSystem`'s gravity at the same dt) · the weapon brain's **Release** profile (grant when the cue says now and the cross-track miss ≤ 10 m) | §6, §8, §4a | `FixedWingModelTests` (follows a route, banks in a turn, never below stall) · `FireProcessingSystemTests.A4_AnAircraftsBomb_KeepsItsVelocity_AGroundShotDoesNot` · `BombBallisticsTests.A3_ThePredictedImpact_IsTheFlownImpact` · `AimAndFireExecutorTests.A6_TheReleaseProfile_WaitsForTheCue` |
| **E5** ⏸ | **slice 6, backend half** — `FixedWingModel.Manual` (mouse-aim: turn toward the look, auto-bank, auto-level, no stall; `ManualDrive` Yaw = look heading, Vertical = desired pitch, Forward = throttle) and `RotorcraftModel.Manual` hover hold. ⛔ **Start only after E3 and E4**; the station half is ui's | §6 | `FixedWingModelTests.B3_*` |

E3 and E4 are independent — run them in either order or side by side. ⏸ **CE-3198 (NLAW overfly, handoff 2's C5) stays queued**: the drone
attack it waited for is now AirAttack's Dive profile (slice 6, ours).

## 3. Fences

- ⛔ The behaviour lane owns `AirAttack`, `AirAttackProfiles`, the `Strike` order, the `AirPlatform*` behaviours, `SquadEmbark`, `LoadCasualty`,
  the squad SOP rows, the helicopter brain's hold release and the scenarios — a finding there is a REPORT, not an edit.
- ⛔ No ground combat number moves (A4: only Air-domain shooters change). Wire changes are exactly the appended `MapRoute.Waypoint` fields and
  `NavigationIntent.RouteEntityId` — report each (R-169); any other wire change is a STOP-and-report.
- ⛔ Planes do not land in v1 (B9): a Ground waypoint on a fixed-wing route is a warning, the plane orbits the last point.

## 4. Report back — the gate report contract (CLAUDE.md rule 8)

One row per gate: verbatim command · pass / fail / skip · delta vs base · `--no-build` column · every red confirmed pre-existing against the
base sha · golden movement as a diff shape · tree clean after each suite · `tracker-counts.py --check` · every id allocated · ⭐ for E2 the
INTEGRATION run that carries `FollowRoute` across nodes (the CE-2171 confirmation and the green after), or why it cannot gate. The design
folded to the as-built (classes, sequences, deviations). Then the sha of each item on `backend`.
