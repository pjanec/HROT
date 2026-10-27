<!--STATUS
state: LIVE — coordinator → backend: the NED wire cutover (AQ107 B–I + CE-4073) is BUILT on `coordinator` and changed your code; merge it before your next NED work
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
supersedes: NOTE_Coordinator_To_Backend_NED_Wire_AQ107.md ("nothing to build yet" — the user then approved AQ107 in full and asked for it built)
related-designs:
  - ../Architect_Question_107_NED_Wire_Frame_And_Names.md — the design and the rulings (R-4092, R-4093, R-4094)
  - ../../reference/NED_DDS_Protocol.md — the wire as it is now
-->
# NOTE — coordinator → backend: the NED wire cutover is built (it touched your code)

**Dispatched at** the commit that adds this file (`coordinator`). FYI, with nothing to build. The user approved [AQ107](../Architect_Question_107_NED_Wire_Frame_And_Names.md)
in full (R-4093) and asked the coordinator to build it with Sonnet subagents, so it was built here instead of on `backend`.
⭐ **Merge `coordinator` before your next NED / orchestrator / BDC work, then `dotnet restore` the test projects you run** (the third stale-binary trap).

## What changed, by AQ107 item

| item | commit | what you will see |
|---|---|---|
| **E** ids `long` | `0a9030cb2` | every entity id on the NED/BDC wire is `long`. The non-wire `int` layer narrows through 19 checked casts tagged `CE-4069` |
| **B + D + F** local ENU metres, `WorldPos` quaternion | `c140c7f13` | `WorldPos` = {`long EntityId`, `double SimTime`, `Vec3d Position`, `Vec3f OrientationXyz` (quaternion, w implied ≥ 0, all zeros = identity), `Vec3f Velocity`, `Vec3f AngularVelocity` rad/s ENU}. One codec, [`QuaternionWireCodec`](../../../FDP/Toolkits/Fdp.Toolkits/Replication/Components/QuaternionWireCodec.cs), used through [`WorldPosCodec`](../../../Hrot/Network/Hrot.Network.NED/Replication/Map/Utils/WorldPosCodec.cs) / [`EnuWire`](../../../Hrot/Network/Hrot.Network.NED/Replication/Map/Utils/EnuWire.cs) (NED) and [`BdcWorldPosCodec`](../../../Hrot/Network/Hrot.Network.BDC/Replication/BdcWorldPosCodec.cs) (BDC). The Euler wire types are deleted. ⇒ **CE-4068 (vertical nose loses heading) is fixed by construction** |
| **C** `GeoReference` | `e7e6e1183` | the ORCHESTRATOR publishes it (retained, keyless): from the terrain header at a world-boundary load (one shared `TerrainCatalog.ReadDefinition` with the nodes), and from the recording metadata at a replay (through the nodes' `PrepareReplay` result). [`ClusterMaster.GeoReference.cs`](../../../Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.GeoReference.cs), [`NedWire.cs`](../../../Hrot/Network/Hrot.Network.Orchestration/Orchestration/NedWire.cs) |
| **I** SI units | `15633d91b` | angles in rad, temperatures in K, pressure in Pa, speeds in m·s⁻¹, barrel heat and CPU as 0–1 fractions, the clock's UTC offset in s. The ECS keeps its own units; translators convert through one [`WireUnits`](../../../Hrot/Network/Hrot.Network.Orchestration/Orchestration/WireUnits.cs) |
| **CE-4073** attribute route + ExCon/IG neutral layer | `7befce3ad` | `Position.X/Y/Z` (attribute ids 14–16) are local metres, copied straight into `SimTransform` on every host. ⚠ Ids 10–12 are retired as `RetiredGeo*` and **must never be reused**. `NeutralGeoBoundary`, `GeoPosition`, `GeoPoint` and the ignored `IGeographicTransform` parameters are deleted, so the NED network path has zero geo conversions. A drag writes X/Y; the owner keeps Z |
| 0.4 prep | `144483c46` · `1d061a7cd` · `ee00ad588` (user) | `ClusterOpNone` / `NodeOpNone` = 0 on both enum copies. `NavLayerMask.All` is the union of the defined layers. `BehaviorOrigin.Self` changed 255 → 127, and old recordings are deliberately not supported (the user) |
| **A** derived topic names | ⏳ not done | waits for CycloneDDS.NET 0.4 to be published. ⛔ Keep the topic-name strings until then (CE-3000) |

## Rows filed for your lane (all `2026-10-27`, in the [tracker](../Blueprint_Issues_Tracker.md))

- **CE-4069**: the non-wire layer still carries ids as `int`.
- **CE-4070**: `Hrot.NED.Tests` is an orphan project.
- **CE-4071**: `ClusterOpType` / `NodeOpType` exist twice.
- **CE-4072**: a replica's `SimVelocity` has two writers.
- **CE-4074**: a branch from a replay differs by one tick on the tank scenarios. It was pre-existing at `e7e6e1183`.
- **CE-4075**: the heartbeat mapping exists twice, and CPU is always 0.
- **CE-4076**: IG's personal route sends (x, 0, y).

⭐ Whenever you add, remove or rename a `[DdsTopic]` type, run `python3 scripts/ned-wire-inventory.py --check` and update [NED_DDS_Protocol.md](../../reference/NED_DDS_Protocol.md).

_coordinator lane, 2026-10-27_
