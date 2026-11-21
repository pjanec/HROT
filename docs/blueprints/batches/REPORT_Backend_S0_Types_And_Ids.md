<!--STATUS
state: LIVE — backend → coordinator: S0 (semantic types, authored references, 32-bit ids, content migration) — the gate report
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Rules.md — §9.8.11 is the as-built of S0 (every item's row, the deviations)
  - ../../DESIGN_Semantic_Value_Types.md — §2.1, CE-4200's as-built (ids, TkbTypeId, NodeId, the exclusions)
  - ../../reference/NED_DDS_Protocol.md — the id width on the wire
  - HANDOFF_Coordinator_To_Backend_S0_Types_And_Ids.md — the dispatch this answers
-->
# REPORT — backend → coordinator: **S0 done** (items 1–10)

**Base** `8769f1c4b` (pre-S0) · **head** `2688bad1a` on `backend` · as-built in [`DESIGN_Scenario_Rules.md`](../../DESIGN_Scenario_Rules.md) §9.8.11.

## What landed

| item | commit | in one line |
|---|---|---|
| 1–4 | earlier `backend` pushes | `WorldPoint` / `WorldRotation` / `Bearing` / `RefSpec`, the `*Link` types, `RefKey` + scenario-scope resolution, the one walker |
| 5 | `489774f21` | params / predicate DTOs use the authored types |
| 6 | `d298cd86d` | saved components use the semantic types through one seam (`SemanticValueTypes`) |
| 7 | `481e8a9a4` … `88a7201e4` | **CE-4200**: entity ids `int` on engine + wire, `TkbTypeId`, `NodeId`, one refusal guard in every allocator |
| 9 | `c361c139e` | `hrot-content-migrate`: 37 params blobs in 25 files; an old form is a LOUD load error |
| 8 | `69abe6816` | the component rail walks nested structs + every engine assembly; 2 places retyped; the compiler's semantic rungs (see deviations) |
| 10 | (in 7 / 9 / 8) | NED protocol id width, AQ107 E SUPERSEDED, the MCP skill's three JSON forms + "old forms fail the load" |

## Deviations — each now in the design

- ⚠ **Item 6 claimed compiler rungs it never added.** Found in item 8 when a `WorldPoint` pin failed `BP1501`. Added: `Vector3↔WorldPoint`, `Quaternion↔WorldRotation`, `Single↔Bearing`, `PointLink→WorldPoint`; `WorldRotation` registered. Design row corrected in place.
- **Retired keys are refused by declaration, not "unknown keys are errors".** The params parse ignores unknown keys by a pinned decision (`BTreeBridgeEmitCore.cs:1526`); `[RetiredJsonKeys]` on the two contracts that renamed keys, enforced only by the load-time applier (`RefResolve`).
- **Cross-lane (behaviors):** `assets/.../DangerCrossingBp.bp.json` re-baked (`RoutePoint` is a `WorldPoint`); `CgfSubsystemHeadlessTests` sent `targetLat`/`targetLon` (ignored since item 5) — now a local target.
- **CE-4200 exclusions** (with reasons, in `DESIGN_Semantic_Value_Types.md` §2.1): `EqsResultEntry.EntityId` stays `long` → now **CE-4207** in the wire-separation handoff.

## Gates *(Rule 8)*

| gate | result | vs base |
|---|---|---|
| T-1 walker / authored / component / id rails (`S0WalkerTests`, `S0AuthoredTypesRailTests`, `S0ComponentTypesRailTests`, `S0IdTypesRailTests`) | green; component rail red-proved by the 9 real places it found before the retype; id rails by compiling decoy fields | — |
| `CE4200_AnIdAboveInt32_IsRefusedAtTheAllocator` (replaces AQ107E) | green | — |
| `ContentMigrationTests` | 4/0; the content rail red before the migration, green after; a second run moves 0 | — |
| `Fdp.Toolkits.Tests` | 4424 / 0 / 1 skip | green |
| `Hrot.Blueprints.Tests` | 3 fail (`ChannelCommandPins_AimAndFire…`, `CE2083_SopOrderUrgency…`, `SubTickRestore…`) | base reds |
| `Hrot.SimHost.Tests` | 9 fail (composition ×2, load-phase ×2, map-parity ×2, UrbanCombat, LiveSimulationPlan, CE3345 flake); danger / tactics / manoeuvre 307/0 | base reds |
| `Hrot.Editor.Tests` | 714 / 0 | green |
| item-7 suites (IG 350, NED 174, Cyclone, Orchestrator, AnimReplication, …) | as the item-7 worker reported (all reds named there are red at base) | — |
| ⭐ `ClusterRunner.Integration` filtered (spawn / replication / ownership / split-authority / time-sync / partial-owner) | 43 / 5 fail: the two `*SpawnMovingVehicleViaGateway*` = **CE-3356** (red at base) · `AclBackdoor…` and `PauseStepResume…` pass alone (batch flakes) · `DistributedLoad_…RemappedMissionPlan` = **CE-3357** (the CE-4153 strict-save guard `d495c673c`, which predates base — ⚠ not proven by a base run: disk) | see row |
| `Hrot.SimHost.Integration.Tests` | 47 / 1 fail (`EntityMission_MovesEntity`) | base red |
| `DangerAreaNodesTests` (integration) | 7 / 0 | — |
| ⭐ **live run** — `--mode all`, `hill-attack` (a migrated scenario) | loaded; simTime 0.3 → 39.1 s; **5 of 9 entities moved ~360 m** (the tanks), 4 hold | — |
| ⭐ **wire capture** — ddsmonitor 1.2.20 (updated from 1.1.2, which ignores the duration / summary options) | 149 samples: `Hrot.NED.Descriptors.EntityMaster` ×10 with **`EntityId` 1000…1009 as `int`**; `IdRequest` ×6 / `IdResponse` ×7 (`Start` 1, `Count` 100 — `uint`); `EntityLifecycleStatusDescriptor` ×60; `OwnershipUpdate` ×66 | — |
| display rails (xvfb) | `FrameRail` 60/0 at item 6; items 7–9 touched no rendering code | — |
| golden movement | item 7: `hosting.txt` 9 numeric rows, `HsmFireActivity` 4 lines (EntityLink 48 → 40 B) · item 8: `DangerCrossingBp.cs.txt` one cast `(Vector3)` → `(WorldPoint)`, `persistence-shape.txt` its one line (−16 B) · item 9: 37 params lines in 25 scenario / recipe files | — |
| `tracker-counts.py --check` · `design-digest.py --check` · `rulings-check.py` (495/495) · `mermaid-check.mjs` (`DESIGN_Scenario_Rules.md` 22/22) | ✅ | — |
| working tree | clean after every run | — |
| ids allocated | **CE-3356** (a NED gateway create asks for id −1) · **CE-3357** (DistributedLoad's save refuses a GUID-less entity) | backend block |

## Open, named

- **CE-3356** — fixed next as item 1 of the wire-separation handoff (the gateway writes `0`, user-confirmed).
- **CE-3357** — decide fixture gap vs real defect.
- Component assemblies the rail still cannot see from `Fdp.Toolkits.Tests` (`Hrot.Traffic`, `Hrot.IG`, `Hrot.CGF`, `Hrot.MuscleCharacter.Fake`) — named in the design's item 8 row.
- Not run: the Stride test projects (Windows-only); `Fdp.Examples.Showcase` / `Hrot.NED.Tests` were already broken before S0 (stale namespaces).

_backend lane, 2026-11-02_
