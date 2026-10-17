<!--STATUS
state: LIVE
updated: 2026-10-17
build-state: BUILT 2026-10-17 (steps 0–7); the as-built is Architect_Question_93_Sumo_Traffic_Node.md §3.0, with its deviations
  table. Live acceptance (§3) is the last row.
current-answer: §2 the steps, §3 acceptance, §4 gates.
stale-below: nothing.
known-conflict: none (AQ93 §6 was re-cut to match, 2026-10-17).
known-rot: step 2's "a `.net.xml` in `terrain.json` `roadNetworks[]`" — as built it is its own key, `sumoNet { file, anchor }`
  (AQ93 §3.0); step 5's "the civilian-car TKB type" — as built a new inert "SUMO car" type (8813); step 6's `NedPublishable()`
  and "18" — as built `DefaultRoutePublishable()` at 29 NED query sites.
related-designs:
  - docs/blueprints/Architect_Question_93_Sumo_Traffic_Node.md — THE DESIGN this plan builds: §3 class/sequence diagrams,
    §10 the embedded module diagram (EM-1…EM-5), §11 vendoring + V7/V8, §9 the summary. Every step cites its section; this
    plan draws no diagram of its own.
  - docs/DESIGN_Terrain_Zones_And_Assets.md — §2.1a/§2.1e: the road net is a terrain asset (R-4021).
  - docs/DESIGN_Terrain_World.md — terrain folder + TerrainCatalog (W12); §9 sparse terrain is NOT in this slice (CE-4012).
  - docs/DESIGN_Terrain_Height.md — §8 height fallback is NOT in this slice (CE-4011).
  - docs/DESIGN_Ownership_Groups_And_Grants.md — the send gate that EM-4's replication route sits beside.
  - docs/DESIGN_Map_3D_Mode.md — the 3-D map the road layer joins (as TerrainLayer3D's sibling).
-->

# PLAN — SUMO slice 1: **traffic embedded in the networkless editor** *(coordinator builds it)*

> 🔒 **User, `2026-10-17`:** *"yes, write the handoff for the first slice. Actually, it is not a handoff, because you are the
> session who is going to implement it."* · R-4047: *"the first thing to build later is the embedded sumo sharp module inside the
> editor"*, ready for a dedicated traffic node later.

## 1. What slice 1 delivers

Open the editor on a scenario whose terrain names a SUMO net and whose scenario carries the traffic settings entity: the 3-D
and 2-D map draw the roads, lane markings, crosswalks, junctions, the ambient ground outside our terrain and the live traffic
lights; SUMO cars drive, dead-reckoned on the editor's clock (pause freezes them); cars near any HROT entity are real entities
(raycasts, EQS, sensors and blasts see them). The same `TrafficPack` is ready to be composed into a dedicated traffic node.

⛔ **Not in slice 1:** pedestrians and panic (AQ93 §8 — SumoSharp S1/S2/S5), hits converting cars into civilians (§8.7 G1/G4 —
needs the civilian family, CIV-1), our entities as SUMO obstacles (E4), vehicle handover (R-4023), the dedicated networked
traffic node (§10 EM-1 composition only), zone-based replicas (Z2 needs `FidelityZones`, Terrain_Zones §11), the outer ground
and insets (CE-4011, CE-4012).

## 2. Steps

| # | step | design basis | done when |
|---|---|---|---|
| **0** | **Upstream SumoSharp changes** (attach `pjanec/sumosharp` with push access; work on branch `hrot/slice1`; the merge to `main` is the user's call): ① `NetworkParser` reads `<location>` (`netOffset`, `projParameter`, `convBoundary`) into `NetworkModel`; ② `Sim.Scene` — the CityLib builders moved into a packable library emitting SUMO-frame triangles with a kind (asphalt, junction, marking, crosswalk, ambient ground) + signal heads, and junction-polygon triangulation added; the Godot viewer keeps working by applying its axis swap at upload; ③ the in-memory/DDS sources can stamp samples with the frame's SUMO sim time instead of the arrival wall clock; ④ a host can name a spawned vehicle (or carry a numeric external id on the lifecycle record) | AQ93 E1, V7 (R-4049), E9 (R-4027), E11 (R-4029/R-4038) | SumoSharp's own suite green on the branch, run there (its parity bar), and the viewer still builds |
| **1** | **Vendor**: `scripts/sync-sumosharp.sh <ref> [--check \| --verify]` (scratch clone OUTSIDE the HROT tree); `FDP/ExtDeps/SumoSharp/` with the ten projects (`Sim.Core`, `Sim.Ingest`, `Sim.Replication`, `Sim.Replication.Dds`, `Sim.Pedestrians`, `Sim.LiveCity`, `Sim.Evac`, `Sim.Viewer.Motion`, `Sim.Host`, `Sim.Scene`) + `Shared/`; `UPSTREAM.md`; the projects in `HROT.sln` | §11 V1–V3 (R-4046), V4 dropped (R-4048) | the whole solution builds; `--verify` reports the copy equals upstream at the recorded commit |
| **2** | **The net rides the terrain**: a `.net.xml` in `terrain.json` `roadNetworks[]` is parsed on every node at terrain load into a `TrafficNetCache` singleton (the `NetworkModel`, a net hash, the UTM → WGS84 → local transform through the terrain origin; `+proj=utm` only) — beside, not inside, the CarKinem road graph (R-4026); the demo terrain **`demo-city`** (AQ93 §11.3, R-4052): the generator script (D3) + `Recipes/Terrain/demo-city/` built from SumoSharp's demo city (D1/D2, no procedural buildings), the non-georeferenced anchor (E1b); and the demo scenario (D4) | R-4021, E1 (R-4025), E2b (R-4030), R-4026 | a rail: the cache's local positions of known junctions land where the terrain origin says |
| **3** | **The road layer**: `Sim.Scene` geometry in local metres → a 3-D layer that is `TerrainLayer3D`'s sibling (built once per net identity, coloured by kind) + the same triangles flat in 2-D; ambient ground masked by our terrain's footprint with the blended seam; signal heads drawn per frame from the traffic-light states; a map toggle for the ambient ground | V7/V8 (R-4049), R-4024, H3 | the roads, junctions, markings and lights of the test net draw in both map modes |
| **4** | **The traffic scenario entity + the pack**: `NodeRole.Traffic` (`1 << 5`); a TKB type + component `TrafficDemand` (density, seed, step length); `Hrot.Traffic` with `TrafficPack` composed into the editor's capability plan (`EditorCapabilities`), idle unless the terrain has a net AND the scenario has the entity; a stepping module under `ExecutionPolicy.SlowBackground`, stepping SumoSharp's `Engine` to `GlobalTime` (random routable trips at the demand's density), publishing through `ReplicationPublisher` with `DrErrorPublishPolicy` into an `InMemoryReplicationBus` | EM-1, EM-3, EM-5 (R-4045), V5 (R-4047), R-4022 B | cars step on the editor's clock; pause stops SUMO; no traffic entity ⇒ zero SUMO work per frame |
| **5** | **The mirror**: `TrafficMirrorModule` (main thread) pumps the bus, resolves every car with `DrClock.ResolveAt` at `GlobalTime`, heights from SUMO; cars are drawn from the buffer; a car within R (300 m, hysteresis +50 m) of any HROT entity becomes a replica — network id from `INetworkIdAllocator`, `EntityInfo` Neutral, the civilian-car TKB type (collider), `ReplicationRoute { Sumo }`; the replica is dropped when it leaves the band or the car despawns | R-4020, R-4038, R-4041/R-4043 (vicinity half), G3 (R-4039) | a soldier placed by a road: cars near him appear in the entity list and a raycast hits one; far cars are drawn but are not entities |
| **6** | **The replication route (general)**: `ReplicationRoute` component; one `NedPublishable()` query helper (`.With<NetworkIdentity>().Without<ReplicationRoute>()`) adopted by the 18 NED egress translators that call `HasAuthority`; `HasAuthority` unchanged | EM-4 (R-4045) | a rail: an OWNED entity carrying a route + every published component makes every NED egress translator write zero samples |
| **7** | **Fold the as-built** into AQ93 (§3 diagrams to the as-built classes, §6 slices, deviations marked SUPERSEDED) | CLAUDE.md obligation ⑤ | the design matches the code |

## 3. Acceptance — run the real thing

The editor (`--mode editor`, headless over the HTTP debug API, `docs/RUNBOOK_Cluster_Debugging_Over_Http.md`) on the test
scenario: roads and lights render (a map screenshot); cars move and stop at red lights; pause holds every car still; a placed
soldier gets SUMO car replicas within 300 m and none beyond; a raycast from the soldier at a passing car reports that car's
entity; removing the traffic entity from the scenario leaves the roads drawn and SUMO idle.

## 4. Gates

T-1 first: the feature suites this touches (terrain load, map layers, egress translators). Build the TEST projects, never the
production ones alone. The new rails per step above. The doc gates (`rulings-check.py`, `tracker-counts.py --check`,
`design-digest.py --check`, `mermaid-check.mjs`). Long builds and the live run in the background.
