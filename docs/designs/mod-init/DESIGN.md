# Module Init — Modular Node Architecture

**Workstream:** `mod-init`  
**Parent workstream:** `eyes-and-muscle` (Phase 4 completion)  
**Status:** Design phase

---

## Background

The `eyes-and-muscle` workstream successfully delivered Phases 1–3:

- **Phase 1:** `HrotNodeBuilder` / `HrotNodeContext` DRY initialization infrastructure
- **Phase 2:** `NedReplicationModule` as an Anti-Corruption Layer (ACL) in `Hrot.ClusterRunner.Replication`
- **Phase 3:** `EyesAndMuscleSubsystem` tracer bullet proving Snapshot-on-Demand (SoD)

**Phase 4 is blocked by a circular dependency:**  
`NedReplicationModule` lives in `Hrot.ClusterRunner` because it references `Hrot.IG` (for `DeadReckoningSyncSystem`) and `Hrot.SimHost` (for translator packs). This prevents `SimHostApp` and `IgApplication` — which sit *below* `ClusterRunner` in the project dependency graph — from consuming the module without creating a cycle.

The tech debt is explicitly documented in `SimHostApp`:
```
// TODO (P2 debt): wire NedReplicationModule once it moves to Hrot.Common
```

---

## Goal

Resolve the inverted dependency graph so that:

1. `NedReplicationModule` lives in `Hrot.Network.Replication`, accessible to all application-layer projects.
2. `SimHostApp` and `IgApplication` are refactored to use `HrotNodeBuilder` + `NedReplicationModule`, eliminating ~300 lines of manual translator boilerplate each.
3. `CgfSubsystem` is updated to reference the module from its new home.
4. The three application classes (`SimHostApp`, `IgApplication`, `CgfApplication`) are structurally self-sufficient — they depend only on `Hrot.Common`, `Hrot.Map.Common`, and lower layers for bootstrapping and replication.
5. Future creation of standalone executables (`SimHost.exe`, `IG.exe`, `CGF.exe`) requires **zero** additional refactoring — only a thin `Program.cs` wrapper per target.

---

## Architectural Constraint: Clean Architecture

The dependency graph must flow strictly **downward**. The actual verified layer order (from `.csproj` ProjectReference inspection) is:

```
Hrot.ClusterRunner       (top — orchestrator)
    ↓ references
Hrot.SimHost  ·  Hrot.IG  ·  Hrot.CGF   (application layer)
    ↓ references
Hrot.Network             (NEW — NedReplicationModule, CognitiveTranslatorPack, .WithReplication() extension)
    ↓ references
Hrot.Common              (upper shared infrastructure — references Map.Common)
    ↓ references
Hrot.Map.Common          (lower shared infrastructure — does NOT reference Hrot.Common)
    ↓ references
Hrot.NED  ·  FDP toolkits                (domain contracts / toolkits)
```

**`Hrot.Network` project references:** `Hrot.Common`, `Hrot.Map.Common` (explicit), `FDP.Toolkit.Behavior`. It sits between the application layer and `Hrot.Common`, breaking all three circular-dependency pressure points:
- `NedReplicationModule` can use `NodeRole` (from `Hrot.Common`) without `Hrot.Map.Common` needing to reference `Hrot.Common`.
- `CognitiveTranslatorPack` can use `BehaviorRegistry` from `FDP.Toolkit.Behavior` directly, with no interface abstraction.
- `.WithReplication()` is architecturally *forced* to be an extension class: `Hrot.Common` does not reference `Hrot.Network`, so the method cannot live as a native instance method on `HrotNodeBuilder`.

**`INedReplicationModule` interface (in `Hrot.Common`):** `HrotNodeContext` is in `Hrot.Common` and cannot hold a `NedReplicationModule` concrete reference (that type is in `Hrot.Network`, which is above `Hrot.Common`). To keep `HrotNodeContext.NedReplication` strongly typed, a minimal `INedReplicationModule` interface is defined in `Hrot.Common`. The concrete `NedReplicationModule` (in `Hrot.Network`) implements it. This is the only abstraction this workstream introduces.

**Prohibited after this workstream:**
- Any application-layer project (`Hrot.SimHost`, `Hrot.IG`, `Hrot.CGF`) containing a `<ProjectReference>` to `Hrot.ClusterRunner`.
- `Hrot.Common` or `Hrot.Map.Common` gaining a `<ProjectReference>` to `Hrot.SimHost`, `Hrot.IG`, or `Hrot.Network`.
- `Hrot.Map.Common` gaining a `<ProjectReference>` to `Hrot.Common` (that would create a cycle since `Hrot.Common` → `Hrot.Map.Common` already exists).
- `Hrot.Map.Common` gaining a `<ProjectReference>` to `FDP.Toolkit.Behavior` (that dependency belongs in `Hrot.Network`, keeping combat/AI domain concepts out of map infrastructure).

---

## Implementation Stages

### Stage 1 — Push Down Architecturally Coupled Systems

**Goal:** Remove all application-layer dependencies from the components that `NedReplicationModule` requires, so the module can be relocated.

#### 1.1 Relocate `DeadReckoningSyncSystem`

Currently in `Hrot.IG/Systems/DeadReckoningSyncSystem.cs` (`Hrot.IG.Systems` namespace).

**Problem:** `NedReplicationModule` orchestrates dead reckoning for *all* roles (`AllInOne`, `ImageGenerator`), so having it reference `Hrot.IG` creates a hard bottleneck.

**Solution:** Move `DeadReckoningSyncSystem.cs` to `Hrot.Common` (new path: `Hrot.Common/Systems/DeadReckoningSyncSystem.cs`, namespace `Hrot.Common.Systems`).

**Rationale:** Dead reckoning reads `NetworkTransform` and `NetworkVelocity` (written by network translators) and writes `SimTransform`. This is pure ACL smoothing — no rendering or IG-specific concern. It belongs beside the network infrastructure it serves.

The system's existing `driveFromNetwork` flag logic must be preserved:
- `driveFromNetwork: true` — smooths all entities (IG node, no local authority)
- `driveFromNetwork: false` — adds `WithLifecycle(EntityLifecycle.Ghost)` filter to skip locally-owned entities (combined roles, prevents fighting local physics)

#### 1.2 Move Translator Packs to Hrot.Map.Common

All three packs already exist in `Hrot.SimHost/Network/`. `SharedTranslatorPack` and `KinematicTranslatorPack` move to `Hrot.Map.Common.Translators`; `CognitiveTranslatorPack` moves to `Hrot.Network.Translators` (see BehaviorRegistry note below).

**Why `Hrot.Map.Common` for Shared/Kinematic and not `Hrot.Common`:** These packs instantiate translators like `EntityMasterEgressTranslator` that physically live in `Hrot.Map.Common.Replication.Egress`. `Hrot.Map.Common` cannot reference `Hrot.Common` (that direction would create a cycle since `Hrot.Common` already references `Hrot.Map.Common`), so the packs must live in `Hrot.Map.Common` where all their translator dependencies already reside.

| File to move | Current location | Target location | Target namespace |
|---|---|---|---|
| `SharedTranslatorPack.cs` | `Hrot.SimHost/Network/` | `Hrot.Map.Common/Translators/` | `Hrot.Map.Common.Translators` |
| `KinematicTranslatorPack.cs` | `Hrot.SimHost/Network/` | `Hrot.Map.Common/Translators/` | `Hrot.Map.Common.Translators` |
| `CognitiveTranslatorPack.cs` | `Hrot.SimHost/Network/` | `Hrot.Network/Translators/` | `Hrot.Network.Translators` |

`EntityStatesIngressPack` is already in `Hrot.Map.Common.Translators` and does not move.

**Prerequisite for `KinematicTranslatorPack` and `CognitiveTranslatorPack` (Stage 1.4):** These packs reference `NavigationIntent*Translator` and `NavigationStatus*Translator` files that currently live in `Hrot.SimHost/Network/`. Those must be moved first (see Stage 1.4).

**BehaviorRegistry dependency (`CognitiveTranslatorPack`):**
`CognitiveTranslatorPack` takes a `BehaviorRegistry?` parameter from `FDP.Toolkit.Behavior`. `Hrot.Map.Common` does not (and must not) reference `FDP.Toolkit.Behavior` — that toolkit carries AI/combat domain concepts that have no place in map infrastructure. Therefore `CognitiveTranslatorPack` moves to **`Hrot.Network.Translators`** instead of `Hrot.Map.Common.Translators`. `Hrot.Network` holds a direct `<ProjectReference>` to `FDP.Toolkit.Behavior`, so `CognitiveTranslatorPack` can use the concrete `BehaviorRegistry?` type directly with no interface abstraction.

#### 1.3 Validate Layer Boundaries After Stage 1

- `Hrot.Common.csproj` must compile without any `<ProjectReference>` to `Hrot.SimHost` or `Hrot.IG`.
- `Hrot.Map.Common.csproj` — same constraint; also must not gain a new reference to `Hrot.Common` (it already doesn't have one).
- The `Hrot.ClusterRunner` tech debt comment referencing `Hrot.IG.Systems` for dead reckoning is resolved.

#### 1.4 Move Navigation Translators to Hrot.Map.Common (prerequisite for S103/S104)

Four translator files currently in `Hrot.SimHost/Network/` are not host-specific and must move to `Hrot.Map.Common/Replication/` before the packs that reference them can be relocated:

| File | Current path | Target path | Target namespace |
|---|---|---|---|
| `NavigationIntentIngressTranslator.cs` | `Hrot.SimHost/Network/` | `Hrot.Map.Common/Replication/Ingress/` | `Hrot.Map.Common.Replication.Ingress` |
| `NavigationIntentEgressTranslator.cs` | `Hrot.SimHost/Network/` | `Hrot.Map.Common/Replication/Egress/` | `Hrot.Map.Common.Replication.Egress` |
| `NavigationStatusIngressTranslator.cs` | `Hrot.SimHost/Network/` | `Hrot.Map.Common/Replication/Ingress/` | `Hrot.Map.Common.Replication.Ingress` |
| `NavigationStatusEgressTranslator.cs` | `Hrot.SimHost/Network/` | `Hrot.Map.Common/Replication/Egress/` | `Hrot.Map.Common.Replication.Egress` |

Rationale: These translators map NED navigation descriptors to ECS components with no SimHost-specific domain logic. They belong beside `GeoSpatialEgressTranslator` and `GeoSpatialIngressTranslator`, which are already correctly placed in `Hrot.Map.Common.Replication.Egress/Ingress`.



---

### Stage 2 — Relocate `NedReplicationModule`

**Goal:** Move the fully-depended-upon ACL module to the shared infrastructure layer.

#### 2.1 Physical Relocation

- Move `Hrot.ClusterRunner/Replication/NedReplicationModule.cs` → `Hrot.Network/Replication/NedReplicationModule.cs`
- Namespace: `Hrot.ClusterRunner.Replication` → `Hrot.Network.Replication`

#### 2.2 Purge Application-Layer `using` Directives

Remove all upward references from the file:

```csharp
// REMOVE:
using Hrot.IG.Systems;        // -> resolved from Hrot.Common.Systems (MODINIT-S101)
using Hrot.SimHost.Network;   // -> packs now in Hrot.Map.Common.Translators / Hrot.Network.Translators
```

Redirect to the newly shared namespaces established in Stage 1:
- `DeadReckoningSyncSystem` → `Hrot.Common.Systems` (MODINIT-S101)
- `SharedTranslatorPack`, `KinematicTranslatorPack` → `Hrot.Map.Common.Translators` (MODINIT-S102–S103)
- `CognitiveTranslatorPack` → `Hrot.Network.Translators` (MODINIT-S104; same assembly as the module)
- `EntityStatesIngressPack` → `Hrot.Map.Common.Translators` (already there, no change)
- `NavigationIntent*` and `NavigationStatus*` translators → `Hrot.Map.Common.Replication.Ingress/Egress` (MODINIT-S107)

#### 2.3 Preserve the Anti-Corruption Contract

The module's internal logic does not change:

- **Synchronous execution** (`ExecutionPolicy.Synchronous()`) must be maintained — CycloneDDS memory polling is not thread-safe.
- **Dead Reckoning guard:** `driveFromNetwork` flag logic for `DeadReckoningSyncSystem` must remain:
  - `NodeRole.AllInOne` / `NodeRole.MuscleGround` → `driveFromNetwork: false` (skip locally-owned entities)
  - `NodeRole.ImageGenerator` / `NodeRole.Brain` → `driveFromNetwork: true` (smooth all)
- **Role-based pack activation:** `SharedTranslatorPack` for all roles; `KinematicTranslatorPack` + `CognitiveTranslatorPack` only when role has egress authority.
- **Already-shared systems require no move:** `GhostCreationSystem` and `SmartEgressSystem` both live in `FDP.Toolkit.Replication.Systems` — a toolkit already referenced by `Hrot.Common` — so they are already correctly positioned and do not need to be relocated as part of this workstream.

#### 2.4 Wire `NedReplicationModule` into `HrotNodeContext` (mandatory)

`HrotNodeBuilder` must gain a mandatory `.WithReplication(NodeRole role)` fluent step. Because `HrotNodeBuilder` lives in `Hrot.Common` and `NedReplicationModule` lives in `Hrot.Network` — and `Hrot.Common` does **not** reference `Hrot.Network` — this step **must** be an **extension class** `HrotNodeBuilderReplicationExtensions` in `Hrot.Network`. This is the correct OCP-compliant design: `HrotNodeBuilder` is open for extension without modification.

`HrotNodeContext` gains a non-nullable `INedReplicationModule NedReplication` property. `INedReplicationModule` is a minimal interface defined in `Hrot.Common` (covering the surface the `SubsystemOrchestrator` and hot-swap logic requires). The concrete `NedReplicationModule` (in `Hrot.Network`) implements this interface. This is the only abstraction the workstream introduces, and it exists purely to avoid a `Hrot.Common` → `Hrot.Network` project reference cycle.

The builder stores replication configuration (the `NodeRole` value and a guard flag that `.WithReplication()` was called). The extension's `Build()` logic constructs `NedReplicationModule` using the `DdsParticipant`, `NetworkEntityMap`, `FdpEventBus`, `localNodeId`, `domainId`, and `IGeographicTransform` already assembled during `Build()` — no infrastructure is duplicated.

This is **not optional**. Splitting the module instance between the application's private field and the context creates two sources of truth for the running replication state. The `SubsystemOrchestrator` (and any hot-swap logic) must query `HrotNodeContext.NedReplication` — the framework, not application boilerplate, owns the hot-plug lifecycle.

`Build()` must throw `InvalidOperationException` if `.WithReplication()` was not called before `Build()`, mirroring the existing single-use guard pattern.

Consequence: `SimHostApp` and `IgApplication` must call `.WithReplication(role)` in their `HrotNodeBuilder` chain and access the module via `_context.NedReplication` rather than storing a private field.

---

### Stage 3 — Eradicate Legacy Boilerplate

**Goal:** Fulfill the DRY promise across `SimHostApp` and `IgApplication`.

#### 3.1 Refactor `SimHostApp.OnLoad`

Current state: role-based manual translator instantiation with a `// TODO (P2 debt)` comment on the `_nedReplicationModule` private field.

**Changes to `SimHostApp.OnLoad`:**
- Remove manual instantiation of individual translators (currently: `EntityMasterEgressTranslator`, `EntityInfoEgressTranslator`, `GeoSpatialEgressTranslator`, `NavigationStatusEgressTranslator`, and others built inline).
- Remove manual `GhostCreationSystem` instantiation that currently precedes the orchestration build.
- Add `.WithReplication(_role)` to the `HrotNodeBuilder` chain; access the module as `_context.NedReplication`.
- Remove the `_nedReplicationModule` private field entirely — the `SubsystemOrchestrator` retrieves the module via `HrotNodeContext.NedReplication`, not via application-layer state. The `// TODO (P2 debt)` comment is deleted along with the field.
- Pass the concrete `BehaviorRegistry` instance into `CognitiveTranslatorPack` directly at composition time; `SimHostApp` still owns the concrete registry.
- **Retain domain specifics:** `BehaviorRegistry`, `RoadNetworkBlob`, `CheckpointIOWorker`, scenario serializers, and physics/visualization modules remain explicitly registered by `SimHostApp`.

**Also update `NodeBootstrapper.BuildTranslators`:**
This method in `Hrot.SimHost/NodeBootstrapper.cs` already uses the three packs by name. After the packs move, `NodeBootstrapper.BuildTranslators` needs its `using` directives updated: `SharedTranslatorPack` and `KinematicTranslatorPack` from `Hrot.SimHost.Network` → `Hrot.Map.Common.Translators`; `CognitiveTranslatorPack` → `Hrot.Network.Translators`. No logic changes.

**Constraint:** This is a pure structural refactor — zero behavioral change. The `Hrot.SimHost.Integration.Tests` suite must remain green.

#### 3.2 Refactor `IgApplication.InitializeNetwork`

Current state: manual `customTranslators` list assembled individually plus `_kernel.RegisterGlobalSystem(new DeadReckoningSyncSystem())`.

**Changes:**
- Build `HrotNodeContext` from `HrotNodeBuilder` (already partially done for ECS).
- Add `.WithReplication(NodeRole.ImageGenerator)` to the `HrotNodeBuilder` chain; the module is accessed via `_context.NedReplication`.
- At `ImageGenerator` role, `NedReplicationModule` (now in `Hrot.Network.Replication`) bundles `EntityStatesIngressPack` (in `Hrot.Map.Common.Translators`) and registers `DeadReckoningSyncSystem` with `driveFromNetwork: true` automatically.
- Visualization modules, camera, and canvas initialization remain unchanged.

**Constraint:** Pure structural refactor. `IgApplication` test suite must remain green. Do not migrate `IgApplication` until `SimHostApp` passes 100%.

---

### Stage 4 — Decouple CGF and Prove Isolation

**Goal:** Update the CGF node to the new module home; verify no application project references `Hrot.ClusterRunner`; prove executable readiness.

#### 4.1 Update `CgfSubsystem`

`CgfSubsystem.cs` (in `Hrot.ClusterRunner`) currently references `NedReplicationModule` from `Hrot.ClusterRunner.Replication`. Update the `using` directive to `Hrot.Network.Replication`.

The role contract does not change: `NodeRole.Brain` (loads `CognitiveTranslatorPack`, isolates from ground kinematics and rendering).

#### 4.2 Update `CgfApplication` (if applicable)

`Hrot.CGF/CgfApplication.cs` may also hold a reference to the old namespace. Redirect to `Hrot.Network.Replication` as above.

#### 4.3 Sever Upward Project References

Verify the following project files contain **no** `<ProjectReference>` to `Hrot.ClusterRunner`:

- `Hrot.SimHost/Hrot.SimHost.csproj`
- `Hrot.IG/Hrot.IG.csproj`
- `Hrot.CGF/Hrot.CGF.csproj`

These must depend solely on `Hrot.Common`, `Hrot.Map.Common`, `Hrot.Network`, and FDP toolkits for bootstrapping and replication.

#### 4.4 Executable Isolation Proof

After Stage 4, each application class (`SimHostApp`, `IgApplication`, `CgfApplication`) is structurally capable of running standalone:

- Network bootstrapping: via `HrotNodeBuilder` (in `Hrot.Common`)
- Replication: via `NedReplicationModule` (now in `Hrot.Network`)
- Domain logic: injected by the app class itself

The existing `Hrot.ClusterRunner/Program.cs` acts as a monolithic switchboard that parses command-line arguments like `-m simhost` or `-m ig` to instantiate the respective subsystems. After this workstream, that switchboard is merely a **convenience router** rather than an architectural requirement — the application classes are fully self-sufficient independently of it.

**No new executables are created in this workstream.** The proof is structural (dependency graph audit) and behavioral (full integration test suite).

When the time comes to ship standalone executables, the only work needed is:
1. Create a new Console App project (e.g., `SimHost.Standalone.csproj`)
2. Reference `Hrot.SimHost.csproj`
3. Write a `static void Main()` calling `new SimHostApp(args).Run()`

No refactoring of domain logic or replication wiring is needed.

---

## Key Decisions

| Decision | Rationale |
|---|---|
| Introduce `Hrot.Network` assembly | Breaks three simultaneous dep pressure points: (1) `NedReplicationModule` needs `NodeRole` from above; (2) `CognitiveTranslatorPack` needs `FDP.Toolkit.Behavior`; (3) `.WithReplication()` must not modify `HrotNodeBuilder` (OCP). One new assembly eliminates all three hacks. |
| Move `DeadReckoningSyncSystem` to `Hrot.Common` (not `Hrot.Map.Common`) | It depends on ECS system abstractions already in `Hrot.Common`; pure ACL smoothing with no rendering concern |
| **Move** `SharedTranslatorPack` and `KinematicTranslatorPack` to `Hrot.Map.Common.Translators` | They are the correct peer layer for `EntityStatesIngressPack`; their only deps are already in `Hrot.Map.Common` |
| **Move** `CognitiveTranslatorPack` to `Hrot.Network.Translators` | It requires `BehaviorRegistry` from `FDP.Toolkit.Behavior`; `Hrot.Map.Common` does not (and must not) reference that toolkit |
| Move `NavigationIntent*` and `NavigationStatus*` translators before moving their packs | These 4 files in `Hrot.SimHost/Network/` are prerequisites for `KinematicTranslatorPack` and `CognitiveTranslatorPack` to compile in their new homes |
| Place `NedReplicationModule` in `Hrot.Network.Replication` | `Hrot.Network` references both `Hrot.Common` (for `NodeRole`) and `Hrot.Map.Common` (for translator packs) with no cycle |
| `INedReplicationModule` interface in `Hrot.Common` | `HrotNodeContext` (in `Hrot.Common`) cannot hold a concrete `Hrot.Network` type without a cycle; minimal interface keeps the property strongly typed |
| `.WithReplication()` is an **extension class** in `Hrot.Network` (OCP-compliant) | `Hrot.Common` does not reference `Hrot.Network`, so the method cannot be a native method; extension class is architecturally forced and correct |
| Remove `_nedReplicationModule` private field from `SimHostApp` | The field creates a second source of truth alongside `HrotNodeContext.NedReplication`; the `SubsystemOrchestrator` must use the context property for hot-swap |
| `.WithReplication()` is mandatory on `HrotNodeBuilder` (throws if absent) | Allows the framework, not application boilerplate, to own the replication lifecycle |
| `NodeBootstrapper.BuildTranslators` needs namespace updates only | It already correctly uses the three packs; after packs move, only `using` directives change — no logic change |
| Do NOT move `EntityStatesIngressPack` | Already correctly positioned in `Hrot.Map.Common.Translators` |
| Stage 3 is sequential: SimHostApp then IgApplication | Prevents simultaneous regressions; unambiguous test signal per migration |
| No standalone executables in this workstream | Out of scope; the goal is structural readiness, not delivery of new binaries |

---

## Success Criteria

This workstream is complete when:

1. `NedReplicationModule` compiles in `Hrot.Network.Replication` with no references to `Hrot.SimHost` or `Hrot.IG`.
2. `Hrot.SimHost.csproj`, `Hrot.IG.csproj`, `Hrot.CGF.csproj` contain no `<ProjectReference>` to `Hrot.ClusterRunner`.
3. `SimHostApp.OnLoad` and `IgApplication.InitializeNetwork` use `NedReplicationModule` (via `.WithReplication()` builder step) instead of manual translator lists.
4. The `// TODO (P2 debt)` comment **and** the `_nedReplicationModule` private field are removed from `SimHostApp`.
5. Full integration test suite (`Hrot.ClusterRunner.Integration.Tests`, `Hrot.SimHost.Integration.Tests`, `Hrot.IG.Tests`) passes green.
