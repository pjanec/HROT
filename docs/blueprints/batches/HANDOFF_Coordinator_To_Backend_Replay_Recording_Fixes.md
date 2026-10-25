<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-10-25
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Replay_Census_H0.md — THE basis: the replay census (every component type asked "must replay read this from the recording?"), §2 findings
  - docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md — OWNS DataPolicy (§1j D7) and the restore classification (§6b); yours already
  - docs/DESIGN_System_State_Inventory.md — §7c-bis, where CE-3309 was measured
  - docs/designs/air-attack/DESIGN_Air_Operations.md — OWNS AirPath (§5, as-built §5b); item ② changes its shape
  - docs/blueprints/Architect_Question_93_Sumo_Traffic_Node.md — E5 / R-4027: the traffic mirror is gated in replay like ingress (item ④)
-->

# HANDOFF — coordinator → backend: what the recording must and must not hold (CE-4046, CE-3309, CE-4048)

**For:** the backend lane (`backend`). **From:** the coordinator lane. **Dispatched at `876f32057`**. ⭐ Scope FROZEN
there; later documents are FYI. A later document that invalidates an item ⇒ STOP that item, report it (R-106), keep the rest.
Questions ⇒ a NOTE to coordinator.

## Why

🔒 User, `2026-10-25`: *"I would like to make the replay working (not just in replay browser, in general)"* · on the harness: *"it relies
on that snapshots really save what should be saved … are we saving what needs to be saved to recording? aren't we saving what should
not be?"*. The coordinator is building a replay harness (CE-4045: the scenario verifier replays each run and compares it with the live
snapshots). Its step H0, the [census](../../DESIGN_Replay_Census_H0.md), found three things that are yours. Each is a measured finding,
spot-checked against the code.

## The items

| # | item | design basis | acceptance |
|---|---|---|---|
| **①** | ⭐⭐ **CE-4046 — make the 23 recorded managed components immutable.** A managed chunk is written only when its version moved (`RecorderSystem.cs:646`), so an object changed in place through `GetManagedComponentRO` never reaches a delta frame (measured: `SopState.cs:94`). 🔒 User: *"a managed component (besides singleton) should never be changed via read only accessor … The managed components was meant to be csharp records"*. ⭐ The rail is BUILT (coordinator, `45e0500c`): `RecordedManagedComponentsAreImmutableTests` + `FDP/TestShared/ManagedComponentImmutabilityProbe.cs`, with a SHRINK-ONLY list of the 23. **Step 1:** measure that the recorder's serializer writes `init`-only members and records without a parameterless constructor (CE-3305 needed settable members) — if it cannot, that is the first change. **Then per type**, by the census's grouping: `PositionGeodetic` (three doubles) → an unmanaged struct · the string-only ones (`BehaviorStartRecord`, `SensorConfigPayload`, `ContextMenuState`, `IgSymbolOverride`) → an id / fixed string, unmanaged · the variable-size ones → a `record` with `ImmutableArray`/`IReadOnly*` members, changed by `with` + Set. ⚠ `BinaryGhostStore` (`[Obsolete]`), `ChildMap` (no writer) and `BpManagedCollectionDemo` (a demo) look dead — search `docs/` then `.dev/` before deleting any (CLAUDE.md, "unreferenced is not unintentional") | Census §2; CE-4046 row; R-174 | each converted type leaves the shrink-only list; the rail stays green; the D1 round-trip rails (`SimStateKeyframeTests`) stay green |
| **②** | ⭐ **`AirPath` split** (part of ①, called out because it is the per-frame one). Its route geometry (`Points`, `S`, `Speed`, `Curvature`, `Stops` + the route key) changes only when the route does ⇒ an immutable record, still recorded (the compile depends on where the aircraft was). Its follower state (`Stage`, `Progress`, `SpeedNow`, `NextStop`, `HoldLeft`, `ReleaseArmed`, `Dropping`, `Pitch`, `Roll`, `OrbitCenter`, `OrbitAngle`) changes EVERY step ⇒ an **unmanaged struct** component — a record there would allocate per aircraft per frame. 📐 4 production files (~100 field accesses, almost all in `RotorcraftModel` / `FixedWingModel`), 2 test files | Air Operations §5 (fold the new shape back as-built there) | both models fly the same as before (their suites); a delta frame carries the follower state (the census's LOSSY finding gone) |
| **③** | **CE-3309 re-pointed** — ⛔ **no read-into-existing** (🔒 user: *"lets build that rail; no read-into-existing"*): with immutable records, replay creating a new object per change is intended. What stays is the chunk GRANULARITY — per-entity change tracking for managed tables, so one changed unit no longer rewrites (and replay no longer re-creates) all 16 384 | CE-3309 row (as re-pointed `2026-10-25`); System State Inventory §7c-bis | one changed unit writes one entity's value; a rail measures it |
| **④** | **CE-4048 — `TrafficMirrorSystem` is not replay-gated.** `TrafficModules.cs:63` does not implement `IReplayRestoreGated` (only `CycloneNetworkIngressSystem.cs:26` does; consumed at `EcsRecordReplayController.cs:278`), while AQ93 E5 / R-4027 says the mirror is gated like ingress ⇒ in replay it likely keeps writing LIVE SUMO into the world. **Settle by a run first** (record 60 s with the traffic node, replay, seek; watch cars and the replica count), then gate it | AQ93 E5, R-4027 | the run's before/after in the report; replay shows the recorded cars only |

## Not yours, FYI

The ui lane gets CE-4047 (`SquadDrillTrace` unrecorded under the Brain tab) and CE-4049 (selection becomes a UI singleton like
`UnitDebugSettings`; UI command events may re-fire in playback). The coordinator builds H1 (the replay comparison) — its first runs should
show ①'s flicker between keyframes directly, before your fix and after it.

## Gates (report these rows)

`RecordedManagedComponentsAreImmutableTests` + `EveryComponentTypeDeclaresItsDataPolicyTests` (filtered, `--no-build` after building the
TEST project) · the touched projects' suites · `SimStateKeyframeTests` · the air models' suites for ② · every red confirmed pre-existing
against the base sha · `tracker-counts.py --check` · the ids you allocate. ⚠ After merging, restore the test projects once (the third
stale-binary trap).

---
_coordinator lane, 2026-10-25_
