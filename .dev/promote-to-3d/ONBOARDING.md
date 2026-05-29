# ONBOARDING — 3D Cognitive Spatial Awareness Promotion

Welcome. This page gets a new developer productive on this workstream fast. Read it top to bottom, then
read [../.guides/DEV-GUIDE.md](../.guides/DEV-GUIDE.md) **before writing any code** — it defines how you
are expected to work (batching, reviews, reporting, test discipline).

---

## 1. What we are building

We are making **`SimTransform.Position.Z` the single authoritative physical altitude** — carried across
the network and read truthfully throughout the simulation — instead of a render-only offset. Today the
engine is deliberately flat: `Position.Z` is effectively unused, EQS results store only X/Y, perception
(`TargetMemory`) stores 2D contacts, path cost is computed in 2D, and a family of translators hardcode
altitude to `0f`. Multi-level urban content (bridges, overpasses, stacked decks) needs real altitude:
a position under a bridge and one on the deck above share X/Y and differ only in Z.

The transport (DDS `GeoPoint` with an altitude double) and the navmesh (DotRecast 3D nearest-polygon
search) are **already 3D**. This work connects authoritative altitude to the consumers that currently
discard it: EQS, perception/`TargetMemory`, path cost, and the position-carrying translators. It is the
committed **pre-step** to the Squad Coordination design.

Full rationale and the three-tier plan: read the design first —
[3D_Cognitive_Spatial_Awareness_Promotion_Design_v1_1.md](./3D_Cognitive_Spatial_Awareness_Promotion_Design_v1_1.md).

---

## 2. The documents (read in this order)

1. **Design** — [3D_Cognitive_Spatial_Awareness_Promotion_Design_v1_1.md](./3D_Cognitive_Spatial_Awareness_Promotion_Design_v1_1.md)
   — the *why* and the *what*, organized as three dependency-ordered tiers shipped in one atomic PR.
2. **Task detail** — [TASK-DETAIL.md](./TASK-DETAIL.md) — the *how*, one spec per task with success
   conditions. **Start at its §0** — it documents three load-bearing facts (two coordinate conventions,
   the pervasively-2D nav layer, and which generators actually exist) that you *will* get wrong if you
   skip it.
3. **Task tracker** — [TASK-TRACKER.md](./TASK-TRACKER.md) — binary progress checklist with links into
   the task detail.
4. **Debt tracker** — [DEBT-TRACKER.md](./DEBT-TRACKER.md) — record carried-over shortcuts here.
5. **Dev guide** — [../.guides/DEV-GUIDE.md](../.guides/DEV-GUIDE.md) — mandatory rules of engagement.

**Adjacent / dependent work (do not edit here):**
- `../group-maneuvers/Step_1_5_TargetMemory_3D_Reconciliation.md` — the Utility-AI `TargetMemory` reader
  reconciliation; runs *after* this PR. Utility readers are **out of scope** for this workstream.
- `../group-maneuvers/Squad_Coordination_Design_v1_1.md` — the dependent that consumes our 3D EQS cover
  query and 3D `TargetMemory`.

---

## 3. Where the components live (the folder map)

The change spans four areas. These are the real paths verified against the codebase:

**Tier 1 — authoritative altitude (`Fdp.Core` + `Fdp.Toolkits` + `Fdp.Examples.Common`)**
- `FDP/Toolkits/Fdp.Toolkits/Geographic/Components/GroundClampingState.cs` — slimmed to a terrain-clamp
  baseline.
- `FDP/Toolkits/Fdp.Toolkits/Geographic/Systems/TerrainQueryResolutionSystem.cs` — writes `HitZ` →
  `SimTransform.Position.Z`.
- `FDP/Examples/Fdp.Examples.Common/Systems/TransformSyncSystem.cs` — drops the visual-Z offset.
- `SimTransform` lives in `Fdp.Core` (its `Position` is already a `Vector3`; we stop zeroing Z).

**Tier 2 — EQS + perception (`Fdp.Toolkits`, `Hrot.Network.NED`, `Hrot.IG`)**
- EQS structs & buffer: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs` (`EqsResult`,
  `EqsResultArray`, `EqsCognitiveBuffer`).
- EQS DDS wire: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsDdsTopics.cs` (`EqsResultEntry`, IDL
  `hrot-eqs-msgs`).
- Generators: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/{EntitiesInRadiusGenerator,NavmeshSamplesGenerator,CoverPointsGenerator}.cs`.
- Cover sources: `.../Spatial/Eqs/{CoverPoint,ICoverProvider,ManualCoverProvider}.cs`.
- Scoring/filter tests: `.../Spatial/Eqs/{DistanceScoreTest,NavmeshReachableTest,PathCostScoreTest}.cs`.
- Perception: `FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs` (`TargetMemory`,
  `AddOrUpdateTarget`) and `ThreatEvaluationSystem`.
- EQS result translators: `Hrot/Network/Hrot.Network.NED/SimHost/EqsResultEventEgressTranslator.cs`,
  `Hrot/Network/Hrot.Network.NED/CGF/EqsResultIngressTranslator.cs`.
- Presentation: `Hrot/Subsystems/Hrot.IG/Gizmos/{EqsCognitiveBufferRenderer,EqsSensorGizmo}.cs`.

**Tier 3 — cost, destination, trajectory (`Fdp.Toolkits`, `Hrot.Network.NED`)**
- Navmesh stub: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/StubNavmeshProvider.cs` (match the 3D
  `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeNavmeshProvider.cs`).
- Destination chain: `.../Navigation/NavigationActions.cs` (`MoveToParams`),
  `.../Navigation/NavigationComponents.cs` (`NavigationIntent`), `EcsNavigationIntent`,
  `.../CarKinem/Core/NavState.cs`; executors under `.../Navigation/Executors/`;
  `.../Navigation/Systems/NavigationIntentBridgeSystem.cs`.
- Trajectory pool: `.../CarKinem/Trajectory/{TrajectoryPoolManager,CustomTrajectory,TrajectoryWaypoint}.cs`;
  consumer `.../CarKinem/Systems/CarKinematicsSystem.cs`; `Hrot.SimHost/Systems/Routing/RouteTrajectorySyncSystem.cs`.
- Nav translators: `Hrot/Network/Hrot.Network.NED/Replication/Map/{Egress,Ingress}/NavigationIntent*Translator.cs`,
  `Hrot/Network/Hrot.Network.NED/SimHost/PathfindingTranslators.cs` (path request/response).

**The two coordinate conventions** (TASK-DETAIL §0.1) cross all of the above: Sim/EQS is **Z-up**
(Z = altitude), Navmesh/Recast is **Y-up** (Y = altitude). Getting the axis mapping wrong is the #1 way
to silently reintroduce the flat-earth bug.

---

## 4. Building and testing

The repository is .NET. Two solutions matter:
- `IOS-IG-SimHost.sln` (repo root) — the **full** solution; build this to compile everything this PR
  touches (`Fdp.*`, `Hrot.*`).
- `FDP/FDP.sln` — the toolkit/engine subset (faster for `Fdp.Toolkits`/`Fdp.Core` iteration).

```powershell
# Full build (run from repo root)
dotnet build IOS-IG-SimHost.sln

# Toolkit-only iteration
dotnet build FDP\FDP.sln

# Run a focused test project (example)
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj
```

Every task in [TASK-DETAIL.md](./TASK-DETAIL.md) ends with `dotnet build IOS-IG-SimHost.sln succeeds` as a
success condition; most also specify the unit tests that must pass.

---

## 5. How this PR is shipped (read before you branch)

- **One atomic PR** (Design §7). The `EqsResult` struct, the DDS `EqsResultEntry`, the translators,
  `TargetMemory`, `PathCost`, the generators, and the Tier-1 transform inversion are coupled — staging
  them apart crashes Brain/Muscle deserialization and injects the exact `0f`-Z bugs we are removing.
- **The merge gate is three tests:** flat-terrain golden parity (P3D-403, Axis-1), the multi-level proof
  fixture (P3D-402, Axis-2), and the dead-reckoning-on-slopes probe (P3D-104, Axis-3). All three green
  before merge.
- **Flight Recorder break:** this PR invalidates existing recordings engine-wide (`EqsResult`,
  `TargetMemory`, and `SimTransform` semantics all change). It is a tell-everyone-first item (P3D-405).

Start with **Phase 0 (P3D-001)** — capture the flat-terrain golden baseline on the unchanged tree *before*
you touch anything, or you lose the safety net that proves you didn't regress existing behavior.
