# Task Detail — Module Init

**Reference:** See [DESIGN.md](./DESIGN.md) for architecture context, rationale, and layer constraints.

---

## Stage 1 — Push Down Architecturally Coupled Systems

---

### MODINIT-S100 — Create Hrot.Network Assembly

**Design Reference:** [Architectural Constraint — Hrot.Network](./DESIGN.md#architectural-constraint-clean-architecture)

**Scope:**
- Create `Hrot.Network/Hrot.Network.csproj` targeting the same SDK and `<TargetFramework>` as `Hrot.Common`
- Add `<ProjectReference>` entries to: `Hrot.Common`, `Hrot.Map.Common`, `FDP.Toolkit.Behavior`
- Add `Hrot.Network` project reference to `Hrot.SimHost.csproj`, `Hrot.IG.csproj`, `Hrot.CGF.csproj`, and `Hrot.ClusterRunner.csproj`
- Create stub directory structure: `Replication/`, `Translators/`, `Infrastructure/`
- Add `Hrot.Network` to `IOS-IG-SimHost.sln`

**NOT in scope:**
- Adding any code files — directories and project files only
- Changing any existing project's logic

**Constraints:**
- `Hrot.Network.csproj` must NOT reference `Hrot.SimHost` or `Hrot.IG`
- `Hrot.Common.csproj` and `Hrot.Map.Common.csproj` must NOT gain a `<ProjectReference>` to `Hrot.Network`
- The reference direction is strictly: application layer → `Hrot.Network` → `Hrot.Common` / `Hrot.Map.Common`

**Success Conditions:**

1. **Compilation:** `dotnet build IOS-IG-SimHost.sln` succeeds with the new empty project in the graph.

2. **Project file valid:** `Hrot.Network/Hrot.Network.csproj` contains `<ProjectReference>` to `Hrot.Common`, `Hrot.Map.Common`, and `FDP.Toolkit.Behavior`; none to `Hrot.SimHost` or `Hrot.IG`.

3. **No reverse references:** `Select-String "<ProjectReference.*Hrot.Network" Hrot.Common/Hrot.Common.csproj, Hrot.Map.Common/Hrot.Map.Common.csproj` returns zero matches.

4. **Solution includes project:** `dotnet sln IOS-IG-SimHost.sln list` includes `Hrot.Network/Hrot.Network.csproj`.

---

### MODINIT-S101 — Move DeadReckoningSyncSystem to Hrot.Common

**Design Reference:** [Stage 1.1 — Relocate DeadReckoningSyncSystem](./DESIGN.md#11-relocate-deadreckoningsyncsystem)

**Scope:**
- Move `Hrot.IG/Systems/DeadReckoningSyncSystem.cs` to `Hrot.Common/Systems/DeadReckoningSyncSystem.cs`
- Update namespace from `Hrot.IG.Systems` to `Hrot.Common.Systems`
- Update all `using Hrot.IG.Systems;` references across the codebase that pull in `DeadReckoningSyncSystem`
- Remove `Hrot.IG.csproj` dependency if it was only needed for this system (verify)
- Add `Hrot.Common.csproj` project reference to any project that now needs it for this type (if not already present)

**NOT in scope:**
- Changing the logic or behavior of `DeadReckoningSyncSystem`
- Modifying `IgApplication.InitializeNetwork` translator list (Stage 3)
- Moving any other system currently in `Hrot.IG/Systems/`

**Constraints:**
- The `driveFromNetwork` flag behavior must be preserved exactly:
  - `new DeadReckoningSyncSystem()` (parameterless) → `driveFromNetwork = true`
  - `new DeadReckoningSyncSystem(driveFromNetwork: false)` → adds `WithLifecycle(EntityLifecycle.Ghost)` query filter
- The `[UpdateInPhase(SystemPhase.PostSimulation)]` attribute must be preserved
- `Hrot.Common` must not gain a `<ProjectReference>` to `Hrot.IG` or `Hrot.SimHost` at any point

**Success Conditions:**

1. **Compilation:** `dotnet build IOS-IG-SimHost.sln` succeeds with zero `error CS` entries after the move.

2. **Namespace resolution:** A search for `Hrot.IG.Systems.DeadReckoningSyncSystem` or `using Hrot.IG.Systems` (where the only purpose was `DeadReckoningSyncSystem`) yields zero results. All consumers now reference `Hrot.Common.Systems`.

3. **Parameterless constructor preserves drive-all behavior:**
   - Setup: create an `IgApplication`-equivalent world with 2 entities (1 local, 1 ghost)
   - Action: register `new DeadReckoningSyncSystem()`, tick once
   - Assert: both entities have their `SimTransform` updated (ghost and local)

4. **Flag-off constructor preserves ghost-only behavior:**
   - Setup: same world, 1 local entity, 1 ghost entity
   - Action: register `new DeadReckoningSyncSystem(driveFromNetwork: false)`, tick once
   - Assert: only the ghost entity's `SimTransform` is updated; local entity's `SimTransform` is untouched

5. **Existing tests pass:** `dotnet test Hrot.ClusterRunner.Integration.Tests --no-build` and `dotnet test Hrot.IG.Tests --no-build` remain green.

---

### MODINIT-S102 — Move SharedTranslatorPack to Hrot.Map.Common

**Design Reference:** [Stage 1.2 — Move Translator Packs to Hrot.Map.Common](./DESIGN.md#12-move-translator-pack-factories-to-hrotmapcommon)

**Scope:**
- Move `Hrot.SimHost/Network/SharedTranslatorPack.cs` → `Hrot.Map.Common/Translators/SharedTranslatorPack.cs`
- Namespace: `Hrot.SimHost.Network` → `Hrot.Map.Common.Translators`
- Update all callers (including `NedReplicationModule` after its move in Stage 2)
- Remove `using Hrot.SimHost.Network;` from any file that only needed this class from that namespace

**NOT in scope:**
- Moving `KinematicTranslatorPack` or `CognitiveTranslatorPack` (those are separate tasks)
- Changing the translator list yielded by the factory

**Constraints:**
- `Hrot.Map.Common.csproj` must not gain a `<ProjectReference>` to `Hrot.SimHost` or `Hrot.IG`
- `EntityMasterEgressTranslator`, `EntityMasterIngressTranslator`, `EntityInfoEgressTranslator` are already in `Hrot.Map.Common.Replication.Egress/Ingress` — so the moved file has all its dependencies in-project with no new references needed
- Factory signature must remain identical

**Success Conditions:**

1. **Compilation:** `dotnet build IOS-IG-SimHost.sln` succeeds after the move.

2. **File in correct location:** `Hrot.Map.Common/Translators/SharedTranslatorPack.cs` exists; no copy remains in `Hrot.SimHost/Network/`.

3. **Integration test:** `SharedTranslatorPack.Create(participant, entityMap, localNodeId, eventBus, ghostCreationSystem)` yields at least `EntityMasterEgressTranslator`, `EntityMasterIngressTranslator`, and `EntityInfoEgressTranslator` instances (verifiable via `.OfType<T>().Any()`).

4. **No old namespace references:** `grep -r "Hrot.SimHost.Network" --include="*.cs"` returns zero hits for `SharedTranslatorPack`.

5. **Existing tests remain green.**

---

### MODINIT-S103 — Move KinematicTranslatorPack to Hrot.Map.Common

**Design Reference:** [Stage 1.2 — Move Translator Packs to Hrot.Map.Common](./DESIGN.md#12-move-translator-pack-factories-to-hrotmapcommon)

**Prerequisite:** MODINIT-S107 (move `NavigationIntentIngressTranslator` and `NavigationStatusEgressTranslator` to `Hrot.Map.Common`) must be complete before this task, so the moved file can resolve its dependencies in-project.

**Scope:**
- Move `Hrot.SimHost/Network/KinematicTranslatorPack.cs` → `Hrot.Map.Common/Translators/KinematicTranslatorPack.cs`
- Namespace: `Hrot.SimHost.Network` → `Hrot.Map.Common.Translators`
- Update all callers

**NOT in scope:**
- `SharedTranslatorPack`, `CognitiveTranslatorPack` (separate tasks)

**Constraints:**
- `Hrot.Map.Common.csproj` must not gain a `<ProjectReference>` to `Hrot.SimHost` or `Hrot.IG`
- After MODINIT-S107: `GeoSpatialEgressTranslator`, `NavigationStatusEgressTranslator`, `NavigationIntentIngressTranslator` all live in `Hrot.Map.Common` — no external references needed

**Success Conditions:**

1. **Compilation:** `dotnet build IOS-IG-SimHost.sln` succeeds.

2. **Yields correct translators:** `KinematicTranslatorPack.Create(participant, entityMap, geoTransform)` returns an enumerable containing `GeoSpatialEgressTranslator` and `NavigationStatusEgressTranslator` instances.

3. **File in correct location:** `Hrot.Map.Common/Translators/KinematicTranslatorPack.cs` exists; none remains in `Hrot.SimHost/Network/`.

4. **No old namespace:** Zero callers reference `Hrot.SimHost.Network.KinematicTranslatorPack`.

5. **Existing tests remain green.**

---

### MODINIT-S104 — Move CognitiveTranslatorPack to Hrot.Network

**Design Reference:** [Stage 1.2 — Move Translator Packs](./DESIGN.md#12-move-translator-packs-to-hrotmapcommon)

**Prerequisite:** MODINIT-S100 (create `Hrot.Network` assembly) and MODINIT-S107 (move navigation translators) must be complete.

**Scope:**
- Move `Hrot.SimHost/Network/CognitiveTranslatorPack.cs` → `Hrot.Network/Translators/CognitiveTranslatorPack.cs`
- Namespace: `Hrot.SimHost.Network` → `Hrot.Network.Translators`
- Update all callers (including `NedReplicationModule` after its move in Stage 2)
- Remove `using Hrot.SimHost.Network;` from any file that only needed this class from that namespace

**NOT in scope:**
- `SharedTranslatorPack`, `KinematicTranslatorPack` (separate tasks targeting `Hrot.Map.Common`)
- Changing the translator list yielded by the factory
- Introducing any interface abstraction — `BehaviorRegistry?` is used as the concrete type directly

**Constraints:**
- `Hrot.Network.csproj` already references `FDP.Toolkit.Behavior` (MODINIT-S100); `BehaviorRegistry?` is used directly — no `IBehaviorRegistry` interface
- `Hrot.Network.csproj` must not gain a `<ProjectReference>` to `Hrot.SimHost` or `Hrot.IG`
- After MODINIT-S107, `NavigationIntentEgressTranslator` and `NavigationStatusIngressTranslator` live in `Hrot.Map.Common.Replication.Egress/Ingress` — accessible to `Hrot.Network` transitively

**Success Conditions:**

1. **Compilation:** `dotnet build IOS-IG-SimHost.sln` succeeds after the move.

2. **Yields correct translators:** `CognitiveTranslatorPack.Create(...)` returns an enumerable containing `NavigationIntentEgressTranslator`, `EntityMissionEgressTranslator`, `GeoSpatialIngressTranslator`, and `NavigationStatusIngressTranslator` instances.

3. **File in correct location:** `Hrot.Network/Translators/CognitiveTranslatorPack.cs` exists; none remains in `Hrot.SimHost/Network/`.

4. **No old namespace:** Zero callers reference `Hrot.SimHost.Network.CognitiveTranslatorPack`.

5. **No interface abstraction:** `grep "IBehaviorRegistry" Hrot.Network/Translators/CognitiveTranslatorPack.cs` returns zero results.

6. **Existing tests remain green.**

---

### MODINIT-S106 — Validate Stage 1 Layer Boundaries

**Design Reference:** [Stage 1.3 — Validate Layer Boundaries](./DESIGN.md#13-validate-layer-boundaries-after-stage-1)

**Scope:**
- Audit `Hrot.Common.csproj` and `Hrot.Map.Common.csproj` for `<ProjectReference>` elements pointing to `Hrot.SimHost` or `Hrot.IG`
- Confirm neither project holds such references
- If any remain, identify which type forces the reference and create a fix sub-task

**NOT in scope:**
- Any code changes (this is a verification task)

**Success Conditions:**

1. **Clean audit:** `Select-String "<ProjectReference.*Hrot\.(SimHost|IG)" Hrot.Common/Hrot.Common.csproj, Hrot.Map.Common/Hrot.Map.Common.csproj` returns zero matches.

2. **Builds in isolation:** `dotnet build Hrot.Common/Hrot.Common.csproj --no-restore` and `dotnet build Hrot.Map.Common/Hrot.Map.Common.csproj --no-restore` each succeed.

3. **Documentation:** A brief comment in this task's completion note lists each `<ProjectReference>` confirmed absent.

### MODINIT-S107 — Move Navigation Translators to Hrot.Map.Common

**Design Reference:** [Stage 1.4 — Move Navigation Translators](./DESIGN.md#14-move-navigation-translators-to-hrotmapcommon-prerequisite-for-s103s104)

**Scope:**
Move these four files from `Hrot.SimHost/Network/` to `Hrot.Map.Common/Replication/`:

| File | Source | Target | Target namespace |
|---|---|---|---|
| `NavigationIntentIngressTranslator.cs` | `Hrot.SimHost/Network/` | `Hrot.Map.Common/Replication/Ingress/` | `Hrot.Map.Common.Replication.Ingress` |
| `NavigationIntentEgressTranslator.cs` | `Hrot.SimHost/Network/` | `Hrot.Map.Common/Replication/Egress/` | `Hrot.Map.Common.Replication.Egress` |
| `NavigationStatusIngressTranslator.cs` | `Hrot.SimHost/Network/` | `Hrot.Map.Common/Replication/Ingress/` | `Hrot.Map.Common.Replication.Ingress` |
| `NavigationStatusEgressTranslator.cs` | `Hrot.SimHost/Network/` | `Hrot.Map.Common/Replication/Egress/` | `Hrot.Map.Common.Replication.Egress` |

Update all callers to use the new namespaces.

**NOT in scope:**
- Changing the translator logic or NED descriptor mappings
- Moving any other file from `Hrot.SimHost/Network/`

**Constraints:**
- `Hrot.Map.Common.csproj` must not gain a `<ProjectReference>` to `Hrot.SimHost` or `Hrot.IG`
- These translators map NED navigation descriptors to ECS components with no SimHost-specific domain logic. Their only dependencies are `Hrot.NED` and `FDP.Toolkit.*` toolkit types, all of which `Hrot.Map.Common` already references.

**Success Conditions:**

1. **Files relocated:** All four `.cs` files exist in their target paths; none remain in `Hrot.SimHost/Network/`.

2. **Compilation:** `dotnet build IOS-IG-SimHost.sln` succeeds.

3. **No old namespace references:** `grep -r "Hrot.SimHost.Network" --include="*.cs"` returns zero hits for any of the four translator names.

4. **Peer translators compile:** `GeoSpatialEgressTranslator` (already in `Hrot.Map.Common.Replication.Egress`) continues to compile — verifying namespace consistency.

5. **Existing tests remain green.**

---

## Stage 2 — Relocate NedReplicationModule

---

### MODINIT-S201 — Move NedReplicationModule to Hrot.Map.Common

**Design Reference:** [Stage 2.1 — Physical Relocation](./DESIGN.md#21-physical-relocation), [Stage 2.2 — Purge Application-Layer Dependencies](./DESIGN.md#22-purge-application-layer-using-directives)

**Scope:**
- Move `Hrot.ClusterRunner/Replication/NedReplicationModule.cs` → `Hrot.Network/Replication/NedReplicationModule.cs`
- Namespace: `Hrot.ClusterRunner.Replication` → `Hrot.Network.Replication`
- Remove `using Hrot.IG.Systems;`, `using Hrot.SimHost.Network;`
- Redirect to: `using Hrot.Common.Systems;` (DeadReckoningSyncSystem), `using Hrot.Map.Common.Translators;` (SharedTranslatorPack, KinematicTranslatorPack), `using Hrot.Network.Translators;` (CognitiveTranslatorPack), `using Hrot.Map.Common.Translators;` (EntityStatesIngressPack — same namespace)
- `NodeRole` (in `Hrot.Common`) is available to `Hrot.Network` directly via its `<ProjectReference>` to `Hrot.Common` — no parameter signature change needed

**NOT in scope:**
- Changing the module's constructor signature or internal role-based logic
- Wiring `NedReplicationModule` into `HrotNodeContext` (see MODINIT-S202)

**Constraints:**
- `ExecutionPolicy.Synchronous()` must remain enforced — CycloneDDS memory polling is not thread-safe
- `driveFromNetwork` flag logic must compile cleanly using `NodeRole` from `Hrot.Common` (accessible via `Hrot.Network`'s direct reference to `Hrot.Common`)
- `Hrot.Network.csproj` must NOT reference `Hrot.SimHost` or `Hrot.IG`

**Success Conditions:**

1. **Compilation:** `dotnet build IOS-IG-SimHost.sln` succeeds with zero `error CS`.

2. **Namespace clean:** `grep -r "Hrot.ClusterRunner.Replication.NedReplicationModule\|using Hrot.ClusterRunner.Replication" --include="*.cs"` returns zero results (except any legacy alias that will be cleaned in Stage 4).

3. **`Hrot.Network` boundary check:** `Hrot.Network.csproj` has no `<ProjectReference>` to `Hrot.SimHost` or `Hrot.IG`.

4. **Synchronous policy preserved:**
   - Unit test: instantiate `NedReplicationModule` with `NodeRole.AllInOne`, call `GetPolicy()` (or equivalent)
   - Assert: returned policy is synchronous (not background/async)

5. **Role guard: AllInOne uses ghost-only dead reckoning:**
   - Setup: `NedReplicationModule(role: NodeRole.AllInOne, ...)`
   - Assert: the internally created `DeadReckoningSyncSystem` has `DriveFromNetwork == false`

6. **Role guard: ImageGenerator uses drive-all dead reckoning:**
   - Setup: `NedReplicationModule(role: NodeRole.ImageGenerator, ...)`
   - Assert: the internally created `DeadReckoningSyncSystem` has `DriveFromNetwork == true`

7. **Existing integration tests pass:** `dotnet test Hrot.ClusterRunner.Integration.Tests --no-build` remains green.

---

### MODINIT-S202 — Wire NedReplicationModule into HrotNodeContext (mandatory)

**Design Reference:** [Stage 2.4 — Wire NedReplicationModule into HrotNodeContext](./DESIGN.md#24-wire-nedreplicationmodule-into-hrotnodecontext-mandatory)

**Scope:**
- Define `INedReplicationModule` interface in `Hrot.Common/Abstractions/INedReplicationModule.cs` with the minimal surface required by the `SubsystemOrchestrator` (e.g., `Start()`, `Stop()`)
- Extend `HrotNodeContext` with a **non-nullable** `INedReplicationModule NedReplication` property
- Add a guard flag to `HrotNodeBuilder` so `Build()` throws `InvalidOperationException` if `.WithReplication()` was not called (mirroring the existing single-use guard pattern)
- Create `Hrot.Network/Infrastructure/HrotNodeBuilderReplicationExtensions.cs` containing the static extension class `HrotNodeBuilderReplicationExtensions` with method: `public static HrotNodeBuilder WithReplication(this HrotNodeBuilder builder, NodeRole role)`
- The extension constructs `NedReplicationModule` using the `DdsParticipant`, `NetworkEntityMap`, `FdpEventBus`, `localNodeId`, `domainId`, and `IGeographicTransform` already assembled during `Build()` — no infrastructure duplication
- `NedReplicationModule` (in `Hrot.Network`) implements `INedReplicationModule`

**NOT in scope:**
- Changing any `SimHostApp` / `IgApplication` code (that is Stage 3)
- Making `.WithReplication()` optional — it is mandatory
- Implementing `.WithReplication()` as a native method on `HrotNodeBuilder` — that would require `Hrot.Common` to reference `Hrot.Network`, creating a cycle

**Constraints:**
- **Extension class is required:** `Hrot.Common` does NOT reference `Hrot.Network`; a native method on `HrotNodeBuilder` cannot reference `NedReplicationModule`. The extension class in `Hrot.Network` is the correct OCP-compliant approach.
- `INedReplicationModule` must not reference any `Hrot.Network` types — it must compile within `Hrot.Common` in isolation
- The `SubsystemOrchestrator` must use `HrotNodeContext.NedReplication` for any hot-swap logic — no private field in application classes

**Success Conditions:**

1. **API surface:** `new HrotNodeBuilder(config).WithRole(...).WithReplication(NodeRole.AllInOne).Build()` compiles (with `using Hrot.Network.Infrastructure;`) and returns an `HrotNodeContext` where `NedReplication` is non-null.

2. **Guard enforced:** `new HrotNodeBuilder(config).WithRole(...).Build()` (without `.WithReplication()`) throws `InvalidOperationException`.

3. **Role contract:** `.WithReplication(NodeRole.AllInOne)` → `context.NedReplication` references a module configured for ghost-only dead reckoning.

4. **No duplication:** The extension does not create a second `DdsParticipant` — it passes the one already built during `Build()` into the module.

5. **Interface in Hrot.Common:** `grep "INedReplicationModule" Hrot.Common/Abstractions/INedReplicationModule.cs` returns a result; the file contains no `Hrot.Network` type references.

---

## Stage 3 — Eradicate Legacy Boilerplate

---

### MODINIT-S301 — Refactor SimHostApp to Use NedReplicationModule

**Design Reference:** [Stage 3.1 — Gutting SimHostApp.OnLoad](./DESIGN.md#31-refactor-simhostapponload)

**Scope:**
- In `Hrot.SimHost/SimHostApp.cs`:
  - Remove `using Hrot.SimHost.Network;` directives that are now redundant (packs moved)
  - Remove manual instantiation of individual translators (built inline before the pack refactor)
  - Remove manual `GhostCreationSystem` instantiation that precedes the orchestration build
  - Remove manual `SimulationSystemGroup` and `NetworkLifecycleSystemGroup` instantiations that currently precede the orchestration build
  - Add `.WithReplication(_role)` to the `HrotNodeBuilder` chain; access the module as `_context.NedReplication`
  - **Delete** the `_nedReplicationModule` private field entirely — the `SubsystemOrchestrator` retrieves the module via `HrotNodeContext.NedReplication`, not via application-level state
  - Remove the `// TODO (P2 debt)` comment along with the field
  - Pass the concrete `BehaviorRegistry` instance directly into `CognitiveTranslatorPack` at composition time; `SimHostApp` still owns and provides the concrete registry
- In `Hrot.SimHost/NodeBootstrapper.cs` (`BuildTranslators` method):
  - Update `using Hrot.SimHost.Network;` for `SharedTranslatorPack` and `KinematicTranslatorPack` → `using Hrot.Map.Common.Translators;`
  - Update `CognitiveTranslatorPack` reference → `using Hrot.Network.Translators;`
  - No logic changes

**NOT in scope:**
- Touching domain-specific `SimHostApp` initializations: `BehaviorRegistry`, `RoadNetworkBlob`, `CheckpointIOWorker`, scenario serializers, physics modules, visualization
- Migrating `IgApplication` (done in MODINIT-S302 after this task is verified green)
- Any changes to test helpers or fixture code

**Constraints:**
- This is a **pure structural refactor** — zero behavioral change allowed
- The `_nedReplicationModule` private field must be **deleted** — not retained
- No new `<ProjectReference>` to `Hrot.ClusterRunner` in `Hrot.SimHost.csproj`

**Success Conditions:**

1. **`// TODO (P2 debt)` comment and `_nedReplicationModule` field removed:** `grep -r "P2 debt\|_nedReplicationModule" Hrot.SimHost/` returns zero results.

2. **No individual translator instantiation:** Searching `SimHostApp.cs` for `new EntityMasterEgressTranslator\|new GeoSpatialEgressTranslator\|new NavigationStatusEgressTranslator\|new NavigationIntentEgressTranslator\|new EntityMissionEgressTranslator` returns zero results.

3. **Module wired via builder:** `SimHostApp.cs` contains `.WithReplication(` in the `HrotNodeBuilder` chain; there is no standalone `new NedReplicationModule(` call.

4. **Compilation:** `dotnet build IOS-IG-SimHost.sln` succeeds.

5. **Behavioral preservation:** `dotnet test Hrot.SimHost.Integration.Tests --no-build` and `dotnet test Hrot.ClusterRunner.Integration.Tests --no-build` pass 100%.

6. **`NodeBootstrapper.BuildTranslators` updated:** `grep "Hrot.SimHost.Network" Hrot.SimHost/NodeBootstrapper.cs` returns zero results; `SharedTranslatorPack`/`KinematicTranslatorPack` now resolve from `Hrot.Map.Common.Translators`, `CognitiveTranslatorPack` from `Hrot.Network.Translators`.

7. **Spawn integration test:** A test that spawns an entity via `SimHostApp` and verifies the entity appears on the network (via `EntityMasterEgressTranslator` firing a descriptor) must pass — proving the module produces the same translator set as the manual list.

8. **Role guard (AllInOne):** If `SimHostApp` is initialized with `NodeRole.AllInOne`, the internally used `DeadReckoningSyncSystem` has `DriveFromNetwork == false` (ghost-only smoothing).

---

### MODINIT-S302 — Refactor IgApplication to Use NedReplicationModule

**Design Reference:** [Stage 3.2 — Gutting IgApplication.InitializeNetwork](./DESIGN.md#32-refactor-igapplicationinitializenetwork)

**Scope:**
- In `Hrot.IG/IgApplication.cs`:
  - Remove manual `customTranslators` list construction
  - Remove `_kernel.RegisterGlobalSystem(new DeadReckoningSyncSystem())` call
  - Build `HrotNodeContext` via `HrotNodeBuilder` if not already done for the ECS path
  - Add `.WithReplication(NodeRole.ImageGenerator)` to the `HrotNodeBuilder` chain; access the module as `_context.NedReplication`
  - The module (now in `Hrot.Network.Replication`) bundles `EntityStatesIngressPack` (in `Hrot.Map.Common.Translators`) and registers `DeadReckoningSyncSystem` with `driveFromNetwork: true` automatically

**NOT in scope:**
- Touching visualization modules, camera, canvas, overlay initialization
- Migrating any other application; `SimHostApp` must be green before this task starts (MODINIT-S301 prerequisite)

**Constraints:**
- Pure structural refactor — zero behavioral change
- `Hrot.IG.csproj` must not gain a `<ProjectReference>` to `Hrot.ClusterRunner`
- Dead reckoning must still run for **all** entities in IG (not just ghosts) — verified by `driveFromNetwork: true`

**Success Conditions:**

1. **No manual translator list:** Searching `IgApplication.cs` for `new EntityMasterIngressTranslator\|new GeoSpatialIngressTranslator\|new EntityInfoIngressTranslator\|new EntityDamageIngressTranslator\|new MapEntitySymbolIngressTranslator` returns zero results.

2. **No manual dead reckoning registration:** `IgApplication.cs` does not contain `RegisterGlobalSystem(new DeadReckoningSyncSystem`.

3. **Module wired via builder:** `IgApplication.cs` contains `.WithReplication(NodeRole.ImageGenerator)` in the `HrotNodeBuilder` chain.

4. **Compilation:** `dotnet build IOS-IG-SimHost.sln` succeeds.

5. **Behavioral preservation:** `dotnet test Hrot.IG.Tests --no-build` passes 100%.

6. **Drive-all dead reckoning:** A test that introduces a network-received ghost entity to an `IgApplication`-backed world verifies that after one tick, both a locally-spawned entity's `SimTransform` and the ghost entity's `SimTransform` are updated (no ghost-only filter).

---

## Stage 4 — Decouple CGF and Prove Isolation

---

### MODINIT-S401 — Update CgfSubsystem to Reference Hrot.Common

**Design Reference:** [Stage 4.1 — Update the CGF Boundary](./DESIGN.md#41-update-the-cgf-boundary-cgfsubsystem--cgfapplication)

**Scope:**
- In `Hrot.ClusterRunner/Services/CgfSubsystem.cs`: change `using Hrot.ClusterRunner.Replication;` to `using Hrot.Network.Replication;`
- In `Hrot.CGF/CgfApplication.cs`: same change if that file holds a direct reference to `NedReplicationModule`

**NOT in scope:**
- Changing `CgfSubsystem` role contracts (`NodeRole.Brain` initialization must remain)
- Any other `CgfSubsystem` behaviour

**Constraints:**
- `NodeRole.Brain` initialization must remain — ensures `CognitiveTranslatorPack` is loaded and ground kinematics is excluded
- `Hrot.CGF.csproj` must not gain a `<ProjectReference>` to `Hrot.ClusterRunner`

**Success Conditions:**

1. **No old namespace references:** `grep -rn "using Hrot.ClusterRunner.Replication" Hrot.ClusterRunner/Services/CgfSubsystem.cs Hrot.CGF/` returns zero results.

2. **Compilation:** `dotnet build IOS-IG-SimHost.sln` succeeds.

3. **Role contract preserved:** A test initializing `CgfSubsystem` confirms `NedReplicationModule` is registered with `NodeRole.Brain` (e.g., via `InstalledModuleNames` property if available, or by verifying `CognitiveTranslatorPack` is in the module's translators).

4. **Existing tests pass:** `dotnet test Hrot.ClusterRunner.Integration.Tests --no-build` remains green.

---

### MODINIT-S402 — Sever Upward Project References

**Design Reference:** [Stage 4.2 — Sever Upward Dependencies](./DESIGN.md#42-sever-upward-project-references-the-clean-architecture-guard), [Stage 4.3 — Prove Executable Isolation](./DESIGN.md#43-prove-executable-isolation-conceptual-validation)

**Scope:**
- Audit and verify that `Hrot.SimHost.csproj`, `Hrot.IG.csproj`, and `Hrot.CGF.csproj` contain **no** `<ProjectReference>` to `Hrot.ClusterRunner`
- If any such reference exists: identify the type(s) causing the dependency, fix by either moving the type or finding an alternative, and remove the reference
- Run full build and integration suite to confirm

**NOT in scope:**
- Adding new executables
- Changing `Hrot.ClusterRunner` internal structure

**Constraints:**
- Application projects must depend only on `Hrot.Common`, `Hrot.Map.Common`, `Hrot.NED`, and FDP toolkits for bootstrapping and replication
- `Hrot.ClusterRunner` may still reference the application projects (it is the composition root — that direction is correct)

**Success Conditions:**

1. **No upward references confirmed:**
   ```
   Select-String "<ProjectReference.*ClusterRunner" Hrot.SimHost/Hrot.SimHost.csproj
   Select-String "<ProjectReference.*ClusterRunner" Hrot.IG/Hrot.IG.csproj
   Select-String "<ProjectReference.*ClusterRunner" Hrot.CGF/Hrot.CGF.csproj
   ```
   All three return zero matches.

2. **`Hrot.SimHost` builds in isolation:** `dotnet build Hrot.SimHost/Hrot.SimHost.csproj` succeeds without `Hrot.ClusterRunner` in the build graph.

3. **`Hrot.IG` builds in isolation:** Same for `Hrot.IG.csproj`.

4. **`Hrot.CGF` builds in isolation:** Same for `Hrot.CGF.csproj`.

5. **Full suite:** `dotnet test IOS-IG-SimHost.sln --no-build` passes.

6. **Executable readiness documented:** A comment or note in the task completion confirms that each application class can be wrapped in a standalone `Program.cs` with no further refactoring needed.

---

## Appendix — Task Dependency Map

```
MODINIT-S100 (Create Hrot.Network assembly)
    ↓ prerequisite for
MODINIT-S104 (Move CognitiveTranslatorPack → Hrot.Network)
MODINIT-S201 (Move NedReplicationModule → Hrot.Network)
MODINIT-S202 (HrotNodeBuilderReplicationExtensions in Hrot.Network)

MODINIT-S107 (Move 4 navigation translators)
    ↓ prerequisite for
MODINIT-S103 (Move KinematicTranslatorPack)
MODINIT-S104 (Move CognitiveTranslatorPack)

MODINIT-S100 (Create Hrot.Network)
MODINIT-S101 (DeadReckoningSyncSystem)
MODINIT-S102 (Move SharedTranslatorPack)
MODINIT-S103 (Move KinematicTranslatorPack)
MODINIT-S104 (Move CognitiveTranslatorPack)
    ↓ all complete before
MODINIT-S106 (Validate Stage 1 boundaries)
    ↓
MODINIT-S201 (Move NedReplicationModule to Hrot.Network)
    ↓
MODINIT-S202 (Mandatory: wire NedReplicationModule into HrotNodeContext via INedReplicationModule + extension class)
    ↓
MODINIT-S301 (Refactor SimHostApp + NodeBootstrapper namespace update)
    ↓ must be green before
MODINIT-S302 (Refactor IgApplication)
    ↓
MODINIT-S401 (Update CgfSubsystem)
MODINIT-S402 (Sever upward references)
```
