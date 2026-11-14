<!--STATUS
state: LIVE — backend → coordinator: wire separation (items 0–5) — the gate report
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - HANDOFF_Coordinator_To_Backend_Wire_Separation.md — the dispatch this answers
  - ../RULINGS.md — R-134 (no DDS type on the internal path)
  - ../DESIGN_Time_Architecture.md — §19 / §19.1, the time wire's move + the TimeSync split
  - ../../DESIGN_Semantic_Value_Types.md — §2.1, the CE-4209 rail
  - ../../reference/NED_DDS_Protocol.md — the id-width row, the CE-4207 / CE-4208 notes
-->
# REPORT — backend → coordinator: **wire separation done** (items 0–5)

**Started** at `848762755` (merged `coordinator`) · **head** `2d84e98f9` on `backend`.

| # | item | commit(s) | in one line |
|---|---|---|---|
| 0 | CE-4200's row | `927d901f6` | cites the commits that exist; the "69 casts" was a miscount (67 were `unchecked` hashes) — ONE boundary narrowing existed and S0 removed it |
| 1 | **CE-3356** | `40d5f6748` | the gateway create writes `0` (user-confirmed); rail red on `-1`; both `*SpawnMovingVehicleViaGateway*` rails green (red since before S0) |
| 2 | **CE-4207** | `686516858` | `EqsResultUpdateEvent` carries `EqsResult` (engine); `EqsResultEntry` is wire-only with an `int` id; the ingress translator is the one converter |
| 3 | **CE-4208** | `5ec250fda` (rail + audit) · `35b50b4eb` · `06acf0bed` + `5429a076d` | every DDS type declared on the network side, built from no engine type; the audit table is in the row |
| 4 | **CE-4209** | `3201965f2` | `SemanticValueTypesTests` reads the three by-name sources: every `"Fdp.Core.X"` resolves, the validator / pin-editor names are semantic, the registry has every semantic type |
| 5 | the last id narrowings | `2d84e98f9` (⚠ labelled CE-4209 — it is item 5) | `SearchResultPayload.Subject` `int` (`Other` stays `long`: it also holds enum / numeric values); `MultiInstanceLayout` has no 64-bit branch and throws on a 64-bit key. `checked((int)…)` left: only `JsonAttributeCompiler`'s byte length |

## Deviations — each in its design

- **CE-4208, the rail is the audit.** `StrictNetworkSeparationTests` §3 (the R-134 guard's own suite, not a new class) scans every `Fdp.*` / `Hrot.*` / `GizmoMap.*` assembly; red-proved by the 20 real offences of its first run, and once `Hrot.IG` was in reach it found one more — the ENGINE enum `PipelineTarget` on `IGCapabilitiesAnnounce` ⇒ `PipelineTargetWire` + `PipelineTargetWireTests` (red-proved by a renumber). `GizmoMap.Network` and `Hrot.Animation.Replication` are classified network assemblies.
- **The time layer**: first marked STOPPED, then built once the handoff's own gate list confirmed it in scope — `DESIGN_Time_Architecture.md` §19 (diagrams first), as-built §19.1: `TimeMessages.cs` SPLIT (its two bus events stay in the engine), `TimeSyncProbeEvent.ClientNodeId` is a `NodeId` (S0's rail), `HighResUtcClock` public.
- **Item 5**: `Other` kept `long` (above); namespaces kept on every move so DERIVED topic names do not change.
- ⚠ **A stale-binary report was caught and fixed**: `06acf0bed` left `Fdp.Network.Cyclone.Tests` uncompilable (a later `NodeId` retype); the worker found it and `5429a076d` repairs it. Cyclone tests 94/0 after.

## Gates *(Rule 8)*

| gate | result | vs base |
|---|---|---|
| T-1 rails: `StrictNetworkSeparationTests` (CE-4208, pending EMPTY) · `PipelineTargetWireTests` · `SemanticValueTypesTests` (CE-4209) · `TheNeutralLayerIsLocalEnuTests.CE3356_*` · `MultiInstanceLayoutTests` | green, each red-proved by a compiling mutation | — |
| `Hrot.SimHost.Tests` | 2086 / 9 fail / 6 skip | exactly the 9 base reds |
| `Hrot.IG.Tests` | 351 / 0 / 1 skip | green |
| `Hrot.Network.NED.Tests` | 177 / 0 | green (+2 rails) |
| `Fdp.Toolkits.Tests` | 4385 / 0 / 1 skip | green (43 time tests MOVED to Cyclone) |
| `Fdp.Network.Cyclone.Tests` | 94 / 0 | 51 before + 43 moved |
| `Hrot.Blueprints.Tests` | 3 fail | base reds |
| ⭐ `ClusterRunner.Integration` filtered (spawn / replication / ownership / split-authority / time-sync / EQS / danger-area / partial-owner) | one red `SplitAuthoritySpawnTests.CE3221_*`; the run hit the 28-min cap (the CE-4158 hang) — the class ALONE: **8 / 0** · EQS 156 / 0 · time 18 / 0 · gateway spawn 5 / 0 | flake (split-authority fails in batch at base too) |
| `Hrot.SimHost.Integration.Tests` | 47 / 1 (`EntityMission_MovesEntity`) | base red |
| ⭐ **wire capture** (ddsmonitor 1.2.20, live `hill-attack`, 120 s) | `TimeSyncRequest` ×419 · `TimeSyncResponse` ×418 (the probe split works end to end) · `EqsResult` ×4620 · `EntityMaster` ×9 · no `ReadError`; `IGCapabilitiesAnnounce` ×0 ⇒ **CE-3358**: its publisher is constructed nowhere in production (pre-existing, not this batch) | — |
| `tracker-counts.py --check` · `rulings-check.py` · `mermaid-check.mjs` (Time design 23/23) | ✅ | — |
| working tree | clean | — |
| ids allocated | **CE-3358** (the IG capabilities publisher is wired nowhere) | backend block |

_backend lane, 2026-11-02_
