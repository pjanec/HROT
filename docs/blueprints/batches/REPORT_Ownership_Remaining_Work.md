<!--STATUS
state: HISTORICAL
updated: 2026-10-02
current-answer: §1 outcome table; §2 gate table
stale-below: nothing
related-designs:
  - docs/DESIGN_Ownership_Groups_And_Grants.md — the owning design; §5.7.1 E4/E6/E8, §5.9 (CE-524), §5.10 (CE-513) were updated by this batch.
-->
# REPORT — Ownership remaining work (`HANDOFF_Ownership_Remaining_Work.md`, scope frozen at `c6aae29e5`)

Branch `backend`. Ids allocated: **`CE-3006`** only (backend block; next free `CE-3007`).

## 1. Outcome per item

| item | outcome | evidence | design updated |
|---|---|---|---|
| ② `CE-3003` debug writes ask the owner | ✅ built | rails `TheWriteRoutesAskTheOwnerTests` 6/6 (new, `Hrot.Editor.Tests`), `AttributeChangeRequestRoundTripTests` 4/4 (red-proven by inverse edit, dll timestamp checked); live E4 + E6 `route: requested`, rotation on all 3 nodes | §5.7 prereq 4, §5.7.1 E4/E6 |
| ③ E8 crash run | ✅ measured | one process per node (domain 7); `kill -9` SimHost → heartbeat not-alive **+9.92 s** (ddsmonitor), every MuscleGround/Perception record back to the creator by **+10.2 s** on both survivors, **0** `OwnershipUpdate` after the kill (R-167); one-truth PASS; a routed edit after reclaim reached both nodes | §5.7.1 E8, S7 row |
| ④ `CE-3004` mission ack timeout | ✅ fixed | root cause: `CgfSubsystem` never called `ScenarioMissionService.PollAcks()` (only `EditorSubsystem` did). Live `--mode all`: `/missions/{id}/task` + `/run` both `ok` (before: 15 s each, fail). ⚠ no rail: the service exists only on a non-headless CGF (`CgfSubsystem.cs:1623`) | `DESIGN_Cgf_Scenario_Windows_Slice.md` as-built note; `MCP_Integration.md` |
| ⑤ `CE-516` editor network factory | ✅ built | runner passes `OfflineNetworkFactory` (no participant); `EditorSubsystem` builds with the injected factory; Stride field deleted. `Hrot.Editor.Tests` 451/0/2, editor integration rails 41/0, `HrotStrideApp.Game` builds, live `--mode editor` allocates id 1000 | tracker row |
| ⑥ `CE-518` 11 unit reds | ✅ all stale tests, fixed against their designs (§ row) | `Hrot.SimHost.Tests` 1056/0/3, `Hrot.ClusterRunner.Tests` 281/0, `Hrot.Core.Tests` 180/0. ⚠ the integration suite's order-dependence part of the row stays open | tracker row |
| ⑦ `CE-524` solver writes NavState | ⛔ **stopped at design** | measured live: the write is DORMANT — no host composes `NavigationSolverModule`; filed **`CE-3006`** (path requests never answered, vehicles steer `Direct`) | §5.9 (module + class + sequence UML, lean) |
| ⑧ `CE-513 (backend)` channel split | ⛔ **stopped at design** | the cause is the generic `IActionExecutor<TChannel>` contract (behaviors lane), so a split must cover every channel | §5.10 (class UML, lean) |

## 2. Gates *(verbatim; every run after the first build is `--no-build`; the TEST project was built each time)*

| gate | result | note |
|---|---|---|
| `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/… --no-build` | 451 / 0 / 2 skip | includes the 6 new CE-3003 facts |
| `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/… --no-build` | 1056 / 0 / 3 skip | was red on CE-518 ①② at base |
| `dotnet test Hrot/Runner/Hrot.ClusterRunner.Tests/… --no-build` | 281 / 0 | was red on CE-518 ④⑤ at base |
| `dotnet test Hrot/Engine/Hrot.Core.Tests/… --no-build` | 180 / 0 | was red on CE-518 ③ at base |
| `dotnet test Hrot.Network.NED.Tests --filter TheAppliersBelongToTheWorld\|UpdateEntityAttribute\|Ownership\|DescriptorOwnership` | 31 / 0 | |
| `dotnet test Fdp.Toolkits.Tests --filter Replication` | 126 / 0 | |
| ⭐ integration (row 8) `dotnet test Hrot.ClusterRunner.Integration.Tests --filter AttributeChangeRequestRoundTripTests\|PartialOwnerReclaimTests` | 6 / 0 (4 + 2), post-merge | class filter only — the whole suite is order-dependent (`CE-518`) |
| `--filter BreakpointSubsystemWiringTests\|EqsAuthoringOnBothHostsTests\|EditorSubsystemBoot\|IdAllocatorDiscovery` | 41 / 0 | editor composition (CE-516) |
| `python3 scripts/tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · mermaid | OK · 68/68 · OK · 11/11 parse | |
| live `ClusterRunner --mode all` / one-process-per-node | E4 E6 E8 ✅, CE-3004 ✅, CE-3006 measured | |

Goldens: none touched. Working tree clean after every run. No new skips.

## 3. Post-merge (`origin/behaviors`, typed event nodes E2–E6, merged before the final commit)

Merge conflicted only on the tracker id-block table (kept their behaviors row, our backend row). After the merge, on the merged tree: builds of `Hrot.ClusterRunner.Integration.Tests`, `Hrot.Editor.Tests`, `Hrot.SimHost.Tests` succeeded; `Hrot.Editor.Tests` 451 / 0 / 2; integration `AttributeChangeRequestRoundTripTests` + `PartialOwnerReclaimTests` 6 / 0.

## 4. Open, for the user

- **`CE-3006`** — compose `NavigationSolverModule` (Composition B5)? New behaviour on every vehicle; unblocks `CE-524`.
- **`CE-513`** — settle the channel intent/status split once for every channel, with the behaviors lane (architect question).
- `E8` observation: on an IG creator the reclaimed MuscleGround/Perception descriptors cover components IG never instantiated — nothing publishes them until a MuscleGround node takes them again (re-grant on rejoin not tested).
- Side finding (CE-516): the runner still creates a DDS participant for every discovered subsystem type, requested or not.
- The handoff's out-of-scope table cites `R-157`, superseded by `R-162`; scope is unchanged (via `R-170`). Not amended (rule 1).
