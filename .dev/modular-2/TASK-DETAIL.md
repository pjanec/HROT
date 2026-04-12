# Task Detail — Modular-2: BDC Network Plugin and Assembly Consolidation

**Reference:** See [DESIGN.md](./DESIGN.md) for architecture overview and rationale.

---

## TASK-P1-001: Create Fdp.Core

**Design Reference:** DESIGN.md — Phase 1.1

**Scope:**
- Create `FDP/Kernel/Fdp.Core/Fdp.Core.csproj` absorbing `Fdp.Kernel`, `FDP.Interfaces`,
  `ModuleHost.Core`
- Move all `.cs` source files from the three old projects into the new project (under
  subfolders matching their original project names if helpful, but namespaces unchanged)
- Update all `<ProjectReference>` entries across the solution that currently point to any
  of the three merged projects
- Delete the three old `.csproj` files and their project entries from the solution file

**NOT in scope:** Changing any source code logic; changing any namespace.

**Constraints:**
- All existing namespaces (`Fdp.Kernel`, `Fdp.Interfaces`, `ModuleHost.Core.*`) must be
  preserved exactly — no renaming.
- NuGet dependencies from all three merged projects must be present in the new csproj:
  `MessagePack`, `K4os.Compression.LZ4`, `NLog`.
- `InternalsVisibleTo` attributes that target test projects must be preserved.

**Success Conditions:**
- The solution builds with zero errors after the merge.
- `Fdp.Core.dll` exists in the build output and contains types from all three old assemblies
  (e.g. `EntityRepository` from Fdp.Kernel and `ModuleHostKernel` from ModuleHost.Core).
- No file from any of the three old projects remains as a standalone project.
- Running existing tests for any of the merged projects passes without modification.

---

## TASK-P1-002: Create Fdp.Engine

**Design Reference:** DESIGN.md — Phase 1.2

**Scope:**
- Create `FDP/Toolkits/Fdp.Engine/Fdp.Engine.csproj` absorbing all `FDP.Toolkit.*`
  projects and `FDP.Framework.Runner`
- Includes absorbing: `FDP.Toolkit.Behavior`, `FDP.Toolkit.Physics`, `FDP.Toolkit.Combat`,
  `FDP.Toolkit.Combat.Contracts`, `FDP.Toolkit.CarKinem`, `FDP.Toolkit.Navigation`,
  `FDP.Toolkit.Navigation.Contracts`, `FDP.Toolkit.Perception`, `Fdp.Toolkit.Geographic`,
  `FDP.Toolkit.Time`, `FDP.Toolkit.Tkb`, `FDP.Toolkit.Lifecycle`, `FDP.Toolkit.Replication`,
  `FDP.Toolkit.NetworkSpawning`, `FDP.Toolkit.Orchestration`, `FDP.Toolkit.Scenario`,
  `FDP.Toolkit.Replay`, `FDP.Toolkit.DER`, `FDP.Toolkit.Commands`, and `FDP.Framework.Runner`
- Move runner types (`ISubsystem`, `SubsystemOrchestrator`, `SubsystemConfig`, `RunnerOptions`,
  `IWindowRegistrar`, `IMapCameraProvider`, `RunnerConfiguration`) into namespace
  `Fdp.Engine.Runner` (i.e. preserve the source from `FDP.Framework.Runner` but under the
  new assembly and namespace prefix)
- Strip all Raylib/ImGui references from `SubsystemOrchestrator`: remove `using Raylib_cs`,
  `using rlImGui_cs`, `using ImGuiNET` and all calls such as `Raylib.InitWindow`,
  `rlImGui.Setup`, `Raylib.BeginDrawing`, `rlImGui.Begin`, etc.
- `SubsystemOrchestrator` becomes a pure iteration loop:
  `Initialize()` calls `subsystem.Initialize(cfg)` for each subsystem  
  `Run()` loops calling `Update(dt)` then (if not headless) calls `subsystem.DrawWorld()`
  and `subsystem.DrawUI()`  
  `Shutdown()` calls shutdown in reverse order — no window teardown here
- Delete `WaitingRoomCoordinator.cs`, `SubsystemStatusAnnounce.cs`, `SubsystemPeerInfo.cs`
  (they currently live in `FDP.Framework.Runner`; do not migrate them)
- Update all `<ProjectReference>` entries across the solution that point to any of the
  merged projects
- Delete old `.csproj` files

**NOT in scope:** Moving `FDP.Toolkit.Vis2D`, `FDP.Toolkit.ImGui`, `FDP.Framework.Raylib`
(those go to TASK-P1-003).

**Constraints:**
- All existing namespaces for the simulation domain code must be preserved
  (`FDP.Toolkit.Physics`, `FDP.Toolkit.Behavior`, etc.)
- The `Fdp.Engine.Runner` namespace must not use `using` directives pointing to
  `FDP.Toolkit.Physics`, `FDP.Toolkit.Behavior`, or any other simulation-domain namespace —
  even though they're in the same assembly.
- `Fdp.Engine.csproj` must have zero PackageReferences to Raylib-cs, rlImGui-cs, ImGui.NET,
  CycloneDDS.Runtime, CycloneDDS.Schema, or CycloneDDS.Core
- `InternalsVisibleTo` attributes from all merged projects must be consolidated in the new
  csproj (or as assembly attributes in a shared `AssemblyInfo.cs`)

**Success Conditions:**
- Solution builds with zero errors.
- `Fdp.Engine.dll` exists and contains `ISubsystem` (from former FDP.Framework.Runner)
  AND `LinearKinematicsSystem` (from former FDP.Toolkit.CarKinem) — proving consolidation.
- `typeof(ISubsystem).Assembly == typeof(LinearKinematicsSystem).Assembly` in a test.
- All existing toolkit tests pass.
- Build output does NOT contain any of the old individual toolkit DLLs.
- A CI validation step (using NetArchTest or a grep script) confirms that zero `.cs`
  files inside `Fdp.Engine/Runner/` contain `using FDP.Toolkit.` or
  `using Fdp.Engine.` directives pointing to simulation-domain namespaces
  (`Physics`, `Behavior`, `Combat`, `Navigation`, etc.).

---

## TASK-P1-003: Create Fdp.Presentation

**Design Reference:** DESIGN.md — Phase 1.4

**Scope:**
- Create `FDP/Framework/Fdp.Presentation/Fdp.Presentation.csproj` absorbing
  `FDP.Toolkit.Vis2D`, `FDP.Toolkit.ImGui`, and `FDP.Framework.Raylib`
- Move all `.cs` files from the three old projects into the new project
- Update solution references
- Delete old `.csproj` files

**Constraints:**
- Preserve all existing namespaces (`FDP.Toolkit.Vis2D`, `FDP.Toolkit.ImGui`,
  `FDP.Framework.Raylib`)
- NuGet dependencies: `Raylib-cs`, `rlImGui-cs`, `ImGui.NET`
- No CycloneDDS references

**Success Conditions:**
- Solution builds with zero errors.
- `Fdp.Presentation.dll` contains `MapCamera` (from former Vis2D),
  `WindowManager` (from former ImGui), and `FdpApplication` (from former Raylib).
- All existing ImGui and Vis2D tests pass.

---

## TASK-P1-004: Create Fdp.Network.Cyclone

**Design Reference:** DESIGN.md — Phase 1.5

**Scope:**
- Create `FDP/ModuleHost/Fdp.Network.Cyclone/Fdp.Network.Cyclone.csproj` absorbing
  `ModuleHost.Network.Cyclone`
- Move all `.cs` files; preserve namespace `ModuleHost.Network.Cyclone`
- Update all `<ProjectReference>` entries that reference the old project
- Delete old `.csproj` file

**NOT in scope:** HROT cluster orchestration DDS schemas (`NodeOpCommand`, `NodeHeartbeat`,
etc.) are application-level types and must NOT be placed in the FDP engine layer. They
move to `Hrot.Network.Orchestration` in TASK-P2-003.

**Constraints:**
- Preserve namespace `ModuleHost.Network.Cyclone` for existing types
- References: `Fdp.Engine`, `FastCycloneDds` (CycloneDDS.Runtime, CycloneDDS.Schema,
  CycloneDDS.Core), NLog
- `Fdp.Network.Cyclone` must have zero project references to any `Hrot.*` assembly

**Success Conditions:**
- Solution builds with zero errors.
- `Fdp.Network.Cyclone.dll` contains `CycloneNetworkModule`.
- All existing `ModuleHost.Network.Cyclone.Tests` pass.
- `Fdp.Network.Cyclone.csproj` has no `<ProjectReference>` to any `Hrot.*` project.

---

## TASK-P2-001: Create Hrot.Core

**Design Reference:** DESIGN.md — Phase 2.1

**Scope:**
- Create `Hrot.Core/Hrot.Core.csproj` absorbing `Hrot.Common`, `Hrot.Map.Common`,
  `Hrot.Map.Definitions`
- Move all `.cs` source files from the three old projects
- Preserve all existing namespaces: `Hrot.Common`, `Hrot.Common.Infrastructure`,
  `Hrot.Map.Common`, `Hrot.Map.Definitions`, etc.
- **Remove direct Hrot.NED simulation data references** — any source file that
  references NED simulation message types (from `Hrot.NED/Messages/`,
  `Hrot.NED/Descriptors/`) must be refactored or moved to `Hrot.Network.NED`
  (TASK-P3-002). The file `Hrot.Map.Common/Commands/NedCommandGateway.cs` is an
  example — it references `CreateEntityRequest` (NED) and must move to `Hrot.Network.NED`.
- **Refactor `HrotNodeBuilder`:** `Build()` must stop constructing `DdsParticipant`
  internally. The participant must be supplied from outside as a `HrotNodeConfig`
  parameter or constructor parameter. Callers (Composition Root and test harnesses)
  are responsible for creating the participant and passing it in. `HrotNodeBuilder` may
  still hold a reference of type `DdsParticipant` (CycloneDDS.Runtime type) — it just
  must not call `new DdsParticipant()` or `HrotEnvironment.CreateParticipant()`.
- **`NodeOpSlaveTranslator` is removed from `HrotNodeBuilder`:** The orchestration
  translator is now wired by the Composition Root using `Hrot.Network.Orchestration`
  (TASK-P2-003) and passed into `HrotNodeConfig` alongside the participant.
- `HrotNodeContext.NedReplication` must be renamed to `HrotNodeContext.Replication`
  with type changed from `INedReplicationModule?` to `IReplicationModule?` (TASK-P3-001).
- Update all `<ProjectReference>` entries in the solution
- Delete old `.csproj` files

**Constraints:**
- `Hrot.Core.csproj` must have **zero** ProjectReferences to `Hrot.NED` (old) or any
  NED/BDC network adapter assembly, or `Fdp.Network.Cyclone`.
- `CycloneDDS.Runtime` package reference is permitted as a pragmatic base technology
  (so that `DdsParticipant` can be accepted and stored as a field type).
- `InternalsVisibleTo` list from all three old projects must be preserved.
- `HrotNodeBuilder` must still produce a working `HrotNodeContext` — restructure
  constructor/parameters as needed but update callers accordingly.

**Dependencies:** Must run after TASK-P1-001, TASK-P1-002, TASK-P2-003
(because `HrotNodeBuilder` now accepts `NodeOpSlaveTranslator` from `Hrot.Network.Orchestration`).

**Success Conditions:**
- Solution builds with zero errors.
- `Hrot.Core.csproj` has no project references to `Hrot.NED`, `Hrot.Network.NED`,
  `Hrot.Network.BDC`, `Hrot.Network.Orchestration`, or `Fdp.Network.Cyclone`.
- `Hrot.Core.csproj` references only `Fdp.Core` and `Fdp.Engine` from the FDP layer
  (plus `CycloneDDS.Runtime` as a package reference).
- Grep confirms no call to `new DdsParticipant()` or `CreateParticipant()` in any
  source file under `Hrot.Core/`.
- All `Hrot.Common.Tests`, `Hrot.Map.Common.Tests` and related tests pass.
- `Hrot.Core.dll` contains `HrotNodeBuilder`, `HrotNodeContext`, `NodeRole`, `MapCamera`,
  `HrotComponentIds`.

---

## TASK-P2-003: Create Hrot.Network.Orchestration

**Design Reference:** DESIGN.md — Phase 2.3, Assembly Map (Hrot Layer)

**Scope:**
- Create `Hrot.Network.Orchestration/Hrot.Network.Orchestration.csproj`
- Absorb all files from `Hrot.NED/Orchestration/`:
  - `NodeOpCommand` (DDS schema struct + generated code)
  - `NodeOpStatus` (DDS schema struct + generated code)
  - `NodeHeartbeat` / `NodeHeartbeatAck` (DDS schema struct + generated code)
  - `ClusterOpRequest` and any other cluster management schema types
  - `NodeOpSlaveTranslator` (currently in `ModuleHost.Network.Cyclone` or
    `Hrot.NED/Orchestration/` — move here)
- These files must be deleted from `Hrot.NED/Orchestration/` (and from
  `ModuleHost.Network.Cyclone` if `NodeOpSlaveTranslator` is there)
- Update all `<ProjectReference>` entries that reference these types
- Wire `HrotNodeConfig` (in `Hrot.Core`) to hold the translator using a marker interface
  `IOrchestrationTranslator : IDisposable` defined in `Hrot.Core`. `HrotNodeConfig`
  exposes an `IOrchestrationTranslator?` property. The Composition Root creates a
  concrete `NodeOpSlaveTranslator`, casts it in place, and stores it. `Hrot.Core` never
  references `Hrot.Network.Orchestration` — compile-time isolation is **mandatory**.
  `NodeOpSlaveTranslator` in `Hrot.Network.Orchestration` implements `IOrchestrationTranslator`.

**Constraints:**
- `Hrot.Network.Orchestration` must contain ONLY cluster management schemas.
  No entity state replication, no mission control, no simulation data schemas.
- Dependencies: `Hrot.Core`, `Fdp.Network.Cyclone`
- No references to `Hrot.Network.NED` or `Hrot.Network.BDC`
- `Hrot.Core.csproj` must NOT reference `Hrot.Network.Orchestration`. Compile-time
  isolation is mandatory. `HrotNodeConfig.OrchestrationTranslator` is typed as
  `IOrchestrationTranslator?` (defined in `Hrot.Core`); `NodeOpSlaveTranslator`
  implements that interface. Storing `NodeOpSlaveTranslator` as `object` or any other
  weakly-typed reference is not acceptable — use the marker interface.

**Dependencies:** Must run after TASK-P1-004 (Fdp.Network.Cyclone must exist).

**Success Conditions:**
- Solution builds with zero errors.
- `Hrot.Network.Orchestration.dll` contains `NodeOpCommand`, `NodeHeartbeat`,
  `NodeOpSlaveTranslator`.
- `Hrot.NED/Orchestration/` directory is empty or deleted.
- `Hrot.ClusterRunner/Program.cs` (after TASK-P5-002) creates `DdsParticipant` once,
  passes it to `new NodeOpSlaveTranslator(participant, nodeId)` from this assembly,
  casts the result as `IOrchestrationTranslator`, and stores it in
  `HrotNodeConfig.OrchestrationTranslator`. No subsystem assembly calls
  `new DdsParticipant()` directly.
- Grep over all files in `Hrot.SimHost/`, `Hrot.CGF/`, `Hrot.IG/`, `Hrot.ExCon/`,
  `Hrot.Orchestrator/` confirms zero references to `NodeOpSlaveTranslator` or
  `NodeOpCommand` — those names only appear in `Hrot.Network.Orchestration` and
  `Hrot.ClusterRunner`.

---

## TASK-P2-002: Create Hrot.Presentation

**Design Reference:** DESIGN.md — Phase 2.2

**Scope:**
- Create `Hrot.Presentation/Hrot.Presentation.csproj` absorbing `Hrot.UI.Common`
  and `Hrot.ScenarioEditor`
- Preserve all existing namespaces (`Hrot.UI.Common`, `Hrot.ScenarioEditor`)
- Update all `<ProjectReference>` entries in the solution
- Delete old `.csproj` files

**Constraints:**
- `Hrot.Presentation` depends on `Fdp.Presentation` (for ImGui and Vis2D utilities)
  and `Hrot.Core` (for domain model types it renders)
- No CycloneDDS references, no `Hrot.Network.NED` or `Hrot.Network.BDC` references
- References to `Hrot.NED` in `Hrot.UI.Common.csproj` must be replaced with
  references to `Hrot.Core` types or neutral DTOs

**Success Conditions:**
- Solution builds with zero errors.
- `Hrot.Presentation.dll` contains `OrbatPanel` (former Hrot.UI.Common) and
  `ScenarioEditorModule` (former Hrot.ScenarioEditor).
- All existing `Hrot.ScenarioEditor.Tests` pass.
- `dotnet list Hrot.Presentation reference` (or inspection of the generated `.deps.json`)
  confirms zero direct or transitive package references to `CycloneDDS.Runtime`.
  All formerly DDS-typed UI data is expressed through `Hrot.Core` DTOs.

---

## TASK-P3-001: Define INetworkFactory and Neutral Interfaces in Hrot.Core

**Design Reference:** DESIGN.md — Phase 3.1

**Scope:**
- Add to `Hrot.Core`:
  - `INetworkFactory` interface (in namespace `Hrot.Core.Network`)
  - `IReplicationModule` interface (replaces `INedReplicationModule` from
    `Hrot.Common/Abstractions/INedReplicationModule.cs`; that file gets moved/renamed)
  - `ICommandGateway` interface (replaces `INedCommandGateway` from
    `Hrot.Map.Common/Commands/NedCommandGateway.cs` which references NED schema types;
    the neutral version expresses operations using domain DTOs)
  - `IExConEgressWriters` interface (neutral aggregate covering all ExCon-side
    DDS-written messages: create-entity, map-config, delete-entity, map-command, etc.;
    replaces individual `IDdsWriter<NedType>` fields currently injected into `ExConLogic`)
- Delete `Hrot.Common/Abstractions/INedReplicationModule.cs` (superseded by `IReplicationModule`)
- Note: `IIngressHandler` is **already neutral** in `FDP.Toolkit.DER` (future `Fdp.Engine`);
  no migration needed for this interface.
- Define neutral domain DTOs for operations expressed in `ICommandGateway` and
  `IExConEgressWriters` (e.g. `CreateEntityCommand`, `UpdateEntityDescriptorCommand`)

**Constraints:**
- `INetworkFactory` must not reference any NED or BDC-specific types — only types from
  `Hrot.Core` and `Fdp.Engine`.
- `INetworkFactory` does NOT need a factory method for `NodeOpSlaveTranslator`; that
  translator is protocol-agnostic and is created directly by the Composition Root via
  `Hrot.Network.Orchestration` (TASK-P2-003). The factory covers simulation-data channels only.
- `IReplicationModule` must expose at minimum the members that `INedReplicationModule`
  currently exposes (verify: `DriveFromNetwork`, `GhostCreationSystem`,
  `NetworkLifecycleGroup`, and the base `IEcsModule` members).
  **Ghost creation is part of the contract, not an implementation detail.**
  `GhostCreationSystem` (or its neutral equivalent `IGhostSystemFactory`) must be
  exposed through `IReplicationModule` so the Composition Root can wire it into the
  `NetworkLifecycleSystemGroup` for replay operations. Alternatively,
  `IReplicationModule.RegisterSystems(IEcsKernel kernel)` must internally register
  the ghost entity materialization pipeline when called — document which model is
  used and ensure `BdcReplicationModule` implements the same ghost ownership semantics
  as `NedReplicationModule`.
- `ICommandGateway` must cover at minimum: `CreateEntityAsync`, `SendUpdateDescriptor`,
  `SendMissionControlRequestAsync` — but using neutral domain types, not NED structs.
- The DTOs used by ICommandGateway (e.g., CreateEntityCommandDto, UpdateEntityCommandDto) MUST be completely agnostic to the ECS. They must NOT contain properties like SimTransform or List<object> InitialComponents. They must rely exclusively on basic primitives (e.g., TkbType, Latitude, Longitude) and atomic string payloads (e.g., InitialAttributesJson).

**Success Conditions:**
- `INetworkFactory`, `IReplicationModule`, `ICommandGateway`, `IExConEgressWriters`
  are in `Hrot.Core.dll`.
- Unit test: create a mock `INetworkFactory` implementation in a test project that
  references only `Hrot.Core` — no NED-specific types required. The mock compiles.
- Compilation fails (as expected) if a test project attempts to call
  `NedReplicationModule` directly through `IReplicationModule` without referencing
  `Hrot.Network.NED` — proving the interface is in the right layer.
- A headless integration test (no GPU, no real DDS loopback required; use an in-process
  DDS domain) proves the ghost pipeline end-to-end: calling
  `INetworkFactory.CreateReplicationModule().RegisterSystems(kernel)` on a real
  `NedReplicationModule` then injecting a synthetic incoming network packet results in
  a materialized ghost entity in the ECS kernel. This test verifies the interface
  practically supports the ghost creation pipeline, not just that a mock compiles.
- dotnet list Hrot.Core reference confirms that the DTOs do not drag Fdp.Kernel or any ECS-specific types into their public properties.
---

## TASK-P3-002: Create Hrot.Network.NED

**Design Reference:** DESIGN.md — Phase 3.2

**Scope:**
- Create `Hrot.Network.NED/Hrot.Network.NED.csproj` by merging the existing
  `Hrot.NED` (generated DDS schemas) and `Hrot.Network` (NedReplicationModule +
  translators + routing)
- Implement `NedNetworkFactory : INetworkFactory` that wires the existing
  `NedReplicationModule`, translators, and DDS writers/readers
- Move the NED-specific translators extracted from `ExConSubsystem` (wiring of
  `MissionControlEgressTranslator` and `MissionControlAckIngressTranslator`) into
  `NedNetworkFactory.CreateCommandGateway` and `NedNetworkFactory.CreateExConIngressHandlers`
- `NedReplicationModule` implements `IReplicationModule` (from `Hrot.Core`)
- `NedNetworkFactory` implements `INetworkFactory` (from `Hrot.Core`)
- Delete old `Hrot.NED` and `Hrot.Network` projects
- Update all `<ProjectReference>` entries

**Constraints:**
- `Hrot.Network.NED` is the ONLY assembly allowed to reference Hrot NED DDS schema types
- Dependencies: `Hrot.Core`, `Fdp.Engine`, `Fdp.Network.Cyclone`
- Preserve the namespace `Hrot.Network` for existing translator types where possible
  (avoid unnecessary namespace churn)
- The concrete NedCommandGateway implementation is strictly responsible for interpreting the atomic properties of CreateEntityCommandDto. It must manually instantiate the required network primitives (e.g., wrapping TkbType into dtEntityMaster and geographic primitives into dtWorldPos), while mapping the remaining generic payload to the InitialAttributesJson field of the CreateEntityRequest.


**Success Conditions:**
- Solution builds with zero errors.
- `NedNetworkFactory` instantiation succeeds in a full integration test.
- All existing `Hrot.NED.Tests` pass with the new project.
- All existing `Hrot.Network` tests (if any) pass.
- No file outside `Hrot.Network.NED` contains a `using` directive for
  `Hrot.NED.Messages` or `Hrot.NED.Descriptors`.

---

## TASK-P3-003: Create Hrot.Network.BDC

**Design Reference:** DESIGN.md — Phase 3.3

**Scope:**
- Create `Hrot.Network.BDC/Hrot.Network.BDC.csproj`
- Define BDC DDS schema files (`.idl` or equivalent for FastCycloneDds CodeGen)
  mirroring the NED message structure at minimum for:
  - Entity state replication (equivalent of NED `EntityMaster`, `EntityDescriptor`)
  - Mission control commands (equivalent of NED `MissionControlRequest` / `MissionControlAck`)
- Implement `BdcReplicationModule : IReplicationModule`
- Implement `BdcNetworkFactory : INetworkFactory`
- Implement BDC-specific `IDescriptorTranslator` implementations mapping BDC wire structs
  to the same ECS components (`SimTransform`, `NetworkIdentity`, etc.) as NED translators

**NOT in scope:**
- Full BDC feature parity on first implementation — a minimal set demonstrating
  protocol-swapability is sufficient. Cover: entity state replication + mission control.

**Constraints:**
- BDC DDS topic names must be distinct from NED topic names to allow coexistence on the
  same domain ID during testing (even though they must never run in parallel in production)
- `BdcNetworkFactory` must satisfy the same `INetworkFactory` contract as `NedNetworkFactory`
- Dependencies: `Hrot.Core`, `Fdp.Engine`, `Fdp.Network.Cyclone`

**Success Conditions:**
- Solution builds with zero errors.
- Integration test: spin up a headless SimHost configured with `BdcNetworkFactory`,
  spawn one entity, verify `SimTransform` is populated on the receiving IG instance.
- Swapping `NedNetworkFactory` for `BdcNetworkFactory` in `Hrot.ClusterRunner/Program.cs`
  (or test harness) requires zero changes to `Hrot.SimHost`, `Hrot.IGH`, `Hrot.ExCon`,
  or `Hrot.CGF` source code.
- `Hrot.ClusterRunner` can be started with `--network bdc` and the spawning integration
  test passes end-to-end.

---

## TASK-P4-001: Decouple ExCon from NED

**Design Reference:** DESIGN.md — Phase 4.1

**Scope:**
- Refactor `Hrot.ExCon` and related code to remove all direct NED and CycloneDDS
  references from ExCon-owned code
- `INedCommandGateway` (in `Hrot.Map.Common/Commands/NedCommandGateway.cs`) references
  NED schema types in its contract. Replace it with the neutral `ICommandGateway` from
  `Hrot.Core`. The concrete `NedCommandGateway` implementation moves to `Hrot.Network.NED`.
- `Hrot.IG` also uses `INedCommandGateway` (in `IgApplication._commandGatewayInterface`
  and `TestHook_SetCommandGateway`) — update those to use `ICommandGateway`.
- In `ExConLogic`, replace individual NED-typed constructor parameters
  (`IDdsWriter<CreateEntityRequest>`, `IDdsWriter<MapInteractionConfig>`,
  `IDdsWriter<DeleteEntityRequest>`, `IDdsWriter<MapCommandRequest>`) with a single
  `IExConEgressWriters` parameter injected via constructor.
- Replace `MissionControlEgressTranslator`/`MissionControlAckIngressTranslator` with
  calls to `ICommandGateway` methods and `IIngressHandler` instances provided by
  `INetworkFactory.CreateExConIngressHandlers`.
- Wire all concrete NED implementations via `INetworkFactory.CreateCommandGateway`,
  `CreateExConEgressWriters`, and `CreateExConIngressHandlers` in the subsystem adapter
  (TASK-P4-004).

**Constraints:**
- `Hrot.ExCon.csproj` must have zero references to `Hrot.Network.NED`, `Hrot.NED`,
  `CycloneDDS.Runtime`, or any BDC assembly
- `NedCommandGateway.cs` must not remain in `Hrot.Map.Common` / `Hrot.Core`; it belongs
  in `Hrot.Network.NED`
- Existing `ExConLogic` unit tests must remain valid; test projects substitute mocks
  for `ICommandGateway` and `IExConEgressWriters`
- Code review of Hrot.ExCon confirms that when ICommandGateway.CreateEntityAsync or UpdateDescriptor is called, the UI logic only populates primitive DTO fields and/or a JSON property bag. ExCon contains zero logic for assembling EntityDescriptorUnion lists or checking for specific network descriptor types.

**Success Conditions:**
- `Hrot.ExCon.csproj` has no NED/CycloneDDS/BDC project or package references.
- All `Hrot.ExCon.Tests` pass without modification to test logic.
- Integration test: `ExConSubsystem` initialized with `NedNetworkFactory` sends a mission
  control command that arrives at a simhost instance and produces an ACK.
- Same test passes when the factory is swapped to `BdcNetworkFactory`.

---

## TASK-P4-002: Decouple SimHost from NED

**Design Reference:** DESIGN.md — Phase 4.2

**Scope:**
- Refactor `Hrot.SimHost` to remove direct `Hrot.NED` and `Hrot.Network` references
- `SimHostApp` and `NodeBootstrapper` accept `INetworkFactory` (from `Hrot.Core`) rather
  than constructing `NedReplicationModule` directly
- `HrotNodeBuilderReplicationExtensions.WithReplication` (which currently lives in
  `Hrot.Network/Infrastructure`) must be updated or replaced:
  - Either move the extension to `Hrot.Network.NED` (where `NedReplicationModule` lives)
  - Or generalize it to accept `INetworkFactory` — rename to `WithReplicationModule()`
- Verify `Hrot.SimHost.csproj` no longer needs `<ProjectReference ... Hrot.Network ...>`
  or `<ProjectReference ... Hrot.NED ...>`

**Constraints:**
- `Hrot.SimHost.csproj` must have zero references to `Hrot.Network.NED` or old `Hrot.NED`
- All `Hrot.SimHost.Tests` and `Hrot.SimHost.Integration.Tests` must continue to pass
- `SimHostInstance` (used in SimHost unit tests) must be updatable to inject a mock
  `INetworkFactory` without DDS — verify `SimHostInstance` location and update its
  initialization if needed

**Success Conditions:**
- `Hrot.SimHost.csproj` has no NED/BDC project references.
- All `Hrot.SimHost.Tests` pass.
- Integration test: `SimHostSubsystem` initialized with `NedNetworkFactory` boots and
  publishes entity state on the NED replication topic.
- Same test passes when initialized with `BdcNetworkFactory`.

---

## TASK-P4-003: Decouple IG and CGF from NED

**Design Reference:** DESIGN.md — Phase 4.3

**Scope:**
- Apply the same `INetworkFactory` injection pattern to `Hrot.IG` and `Hrot.CGF`
- Both currently reference `Hrot.NED` and `Hrot.Network`; replace with `INetworkFactory`
  from `Hrot.Core`
- Verify `Hrot.Orchestrator.csproj`: it references `Hrot.NED` for NED orchestration
  DDS types (`NodeOpCommand`, etc.). Determine if these belong to `Fdp.Network.Cyclone`
  (FDP toolkit orchestration transport) or to `Hrot.Network.NED`. Move them accordingly.

**Constraints:**
- `Hrot.IG.csproj`, `Hrot.CGF.csproj` must have zero references to `Hrot.Network.NED`
  or old `Hrot.NED`
- `Hrot.Orchestrator.csproj` must reference `Hrot.Network.Orchestration` for cluster
  management DDS types — it must NOT reference `Hrot.Network.NED` or `Hrot.NED` after
  this task
- All existing tests for these projects must pass

**Success Conditions:**
- `dotnet list Hrot.IG reference` shows no NED adapter dependency.
- `dotnet list Hrot.CGF reference` shows no NED adapter dependency.
- `Hrot.Orchestrator.csproj` references `Hrot.Network.Orchestration` and NOT
  `Hrot.Network.NED`.
- All `Hrot.IG.Tests`, `Hrot.CGF.Tests` and related integration tests pass.

---

## TASK-P4-004: Move ISubsystem Adapters into Plugin Assemblies

**Design Reference:** DESIGN.md — Phase 4.4

**Scope:**
Move these files from `Hrot.ClusterRunner/Services/` to their plugin assemblies:
- `SimHostSubsystem.cs` → `Hrot.SimHost/`
- `IgSubsystem.cs` → `Hrot.IG/`
- `ExConSubsystem.cs` → `Hrot.ExCon/`
- `CgfSubsystem.cs` → `Hrot.CGF/`
- `OrchestratorSubsystem.cs` → `Hrot.Orchestrator/`
- `EditorSubsystem.cs` → `Hrot.Editor/`
- `CgfDebugVisualizerAdapter.cs` — move with `CgfSubsystem.cs` (if it only supports CGF)
  or with `OrchestratorSubsystem.cs` if it supports both
- `ClusterScenarioPanel.cs` — move with `OrchestratorSubsystem.cs`

Files that **stay** in `Hrot.ClusterRunner/Services/`:
- `PerspectiveUpdateSubsystem.cs` (runner-internal infrastructure; always prepended first)
- `EyesAndMuscleSubsystem.cs` (verify usage and decide)

`CiSubsystem.cs` is **not** a special-cased runner internal. Move it to
`Hrot.ClusterRunner/Scenarios/` and ensure it implements `ISubsystem` with
`Name => "ci"`. The reflection scanner will discover it naturally when
`--mode ci` is requested; no dedicated branch is needed.

After moving, update each plugin assembly `.csproj` to reference `Fdp.Engine`
(for `ISubsystem`). Also update `Hrot.ClusterRunner.csproj` to remove the source files
from the `Services/` folder (they are now provided by the plugin DLLs).

**Constraints:**
- Each moved subsystem file must compile in its new home without changes to logic
- `Hrot.ClusterRunner/Services/` must not contain any concrete subsystem beyond
  `PerspectiveUpdateSubsystem`

**Success Conditions:**
- `Hrot.ClusterRunner.dll` does not contain `SimHostSubsystem`, `IgSubsystem`,
  `ExConSubsystem`, `CgfSubsystem`, or `OrchestratorSubsystem` among its types.
- `Hrot.SimHost.dll` contains `SimHostSubsystem`, etc. (verifiable via reflection).
- `CiSubsystem` is discovered by the reflection scanner when `--mode ci` is passed;
  no dedicated `if (mode == "ci")` branch exists in `Program.cs`.
- A unit test exists for every moved subsystem (`SimHostSubsystemTests`,
  `IgSubsystemTests`, `ExConSubsystemTests`, `CgfSubsystemTests`,
  `OrchestratorSubsystemTests`) asserting that invoking
  `Initialize(new SubsystemConfig { Headless = true })` completes without throwing
  `DllNotFoundException`. This verifies that no native graphics library is loaded
  via JIT when running headless.
- All subsystem integration tests pass.

---

## TASK-P4-005: Implement OfflineNetworkFactory for Hrot.Editor

**Design Reference:** DESIGN.md — Phase 5.5

**Scope:**
- Implement `OfflineNetworkFactory : INetworkFactory` in `Hrot.Editor` (or
  in a shared test utilities assembly if also needed by unit test projects)
- `OfflineNetworkFactory.CreateReplicationModule` returns a no-op `IReplicationModule`
  that does not allocate any DDS readers/writers
- `OfflineNetworkFactory.CreateCommandGateway` returns a no-op `ICommandGateway`
- `OfflineNetworkFactory.CreateExConIngressHandlers` returns an empty collection
- Update `EditorSubsystem.Initialize` (after TASK-P4-004) to inject
  `OfflineNetworkFactory` into the subsystem bootstrap chain

**Constraints:**
- `Hrot.Editor.csproj` must have zero references to `Hrot.Network.NED` or `Hrot.Network.BDC`
- The editor must still boot correctly in offline mode with the no-op factory

**Success Conditions:**
- `Hrot.Editor` builds with no NED/BDC project references.
- `Hrot.Editor.Tests` (Hrot.EditorSubsystemBootTests) passes headless with the offline factory.
- `EditorHarness` integration tests continue to pass without touching DDS.

---

## TASK-P5-001: Delete RunMode Enum and Refactor CLI Parsing

**Design Reference:** DESIGN.md — Phase 5.1, 5.2

**Scope:**
- Delete `Hrot.ClusterRunner/Configuration/RunMode.cs`
- Update `HrotRunnerConfiguration.cs`:
  - Remove `ParsedMode` property (`RunMode` type)
  - Remove `ParseModeString()` method
  - Add `RequestedSubsystems` (`HashSet<string>`, case-insensitive) populated by `Validate()`
  - `Validate()` splits `ModeString` on commas, trims each entry, adds to
    `RequestedSubsystems`; reject empty result with `InvalidOperationException`
  - Retain `--mode` CLI option; change its help string to "Comma-separated subsystem
    names (e.g. simhost,ig,excon) or 'all'"
  - Remove the `--wait-for` and `--no-wait` CLI options (they were for
    `WaitingRoomCoordinator` which is deleted)

**Constraints:**
- `HrotRunnerConfiguration.Validate()` must reject unknown subsystem names with a clear
  error message

**Success Conditions:**
- `RunMode.cs` does not exist in the repository.
- `HrotRunnerConfiguration` has no property of type `RunMode`.
- Unit tests for configuration parsing: `"simhost"` → `RequestedSubsystems == {"simhost"}`;
  `"simhost,ig"` → `{"simhost", "ig"}`; `""` → `InvalidOperationException`;
  invalid names → `InvalidOperationException`.
- All existing `Hrot.ClusterRunner.Tests` pass or are updated to match the new API.

---

## TASK-P5-002: Implement In-Memory Reflection Scan in Program.cs

**Design Reference:** DESIGN.md — Phase 5.3, 5.4

**Scope:**
- Replace hardcoded `if (config.ParsedMode.HasFlag(...))` blocks in `Program.cs` with
  an `AppDomain` reflection scan for `ISubsystem` implementations
- Algorithm:
  1. Call `LoadReferencedAssemblies()` to eagerly load all statically-linked DLLs
  2. Scan `AppDomain.CurrentDomain.GetAssemblies()` for non-abstract, non-interface
     types implementing `ISubsystem`
  3. Skip `PerspectiveUpdateSubsystem` (prepended manually at index 0)
  4. Instantiate each found type, preferring a constructor that accepts `INetworkFactory`,
     falling back to a parameterless constructor
  5. Map `instance.Name` → `instance` in a `Dictionary<string, ISubsystem>`
  6. Select instances matching `config.RequestedSubsystems`
  7. Prepend `PerspectiveUpdateSubsystem` to the active list always
- Implement `LoadReferencedAssemblies()` helper that loads all referenced assemblies
  that are not yet loaded in the AppDomain
- Move Raylib window init/close into `Program.cs` (extracted from `SubsystemOrchestrator`):
  ```
  if (!config.Headless) { Raylib.InitWindow(...); rlImGui.Setup(true); }
  // ... run orchestrator ...
  if (!config.Headless) { rlImGui.Shutdown(); Raylib.CloseWindow(); }
  ```
- Remove Raylib/ImGui `using` directives from `SubsystemOrchestrator`

**Constraints:**
- `Program.cs` may still contain the `NodeIdResolver` offset switch (this is legitimate
  topology logic for the composition root)
- `Program.cs` must not reference any concrete subsystem class other than
  `PerspectiveUpdateSubsystem`

**Success Conditions:**
- `dotnet run -- --mode simhost` starts `SimHostSubsystem` without any hardcoded
  reference to `SimHostSubsystem` in `Program.cs`.
- `dotnet run -- --mode simhost,ig` starts both subsystems.
- `dotnet run -- --mode unknownname` exits with a user-friendly error message.
- `dotnet run -- --mode all` starts all discovered subsystems.
- `dotnet run -- --mode ci` starts `CiSubsystem` via the reflection scanner without
  any dedicated `if (mode == "ci")` branch in `Program.cs`.
- Adding a new `ISubsystem` implementation to any project referenced by
  `Hrot.ClusterRunner` requires only a new project reference — not a change to
  `Program.cs`.
- Integration test: startup with reflection scan produces the same subsystem set as
  the previous hardcoded approach.

---

## TASK-P5-003: Add --network CLI Flag

**Design Reference:** DESIGN.md — Phase 3, Phase 5

**Scope:**
- Add `--network` CLI option to `HrotRunnerConfiguration` with default `"ned"`
- Accepted values: `"ned"`, `"bdc"`
- After configuration validation, `Program.cs`:
  1. Calls `ResolveAppNodeId()` (existing switch) to determine `nodeId`
  2. Calls `new DdsParticipant(domainId)` (single call — only place in the application)
  3. Creates `new NodeOpSlaveTranslator(participant, nodeId)` from
     `Hrot.Network.Orchestration`
  4. Passes participant and translator into `HrotNodeConfig`
  5. Instantiates `new NedNetworkFactory(participant)` or `new BdcNetworkFactory(participant)`
     based on `--network` flag
  6. Passes the factory to discovered subsystem constructors (TASK-P5-002)
- Add help text: `"Network protocol: ned (default) or bdc"`

**Constraints:**
- Unknown `--network` value must fail fast with a clear error message at startup,
  before any DDS participant is created
- `DdsParticipant` must be created exactly once; verify no subsystem assembly
  calls `new DdsParticipant()` internally
- `Program.cs` references: `Hrot.Network.Orchestration` (for `NodeOpSlaveTranslator`),
  `Hrot.Network.NED` (for `NedNetworkFactory`), `Hrot.Network.BDC` (for
  `BdcNetworkFactory`), `Fdp.Network.Cyclone` (for `DdsParticipant`)

**Success Conditions:**
- `dotnet run -- --mode simhost --network ned` starts with NED protocol.
- `dotnet run -- --mode simhost --network bdc` starts with BDC protocol.
- `dotnet run -- --mode simhost --network unknown` exits with error code 1 and a
  descriptive message before creating any DDS participant.
- Grep over all `.csproj` files for `Hrot.SimHost`, `Hrot.CGF`, `Hrot.IG`,
  `Hrot.ExCon`, `Hrot.Orchestrator` confirms none reference `Fdp.Network.Cyclone`
  as a project dependency.
- Unit test: `HrotRunnerConfiguration` parsing of `--network bdc` sets a property
  that causes `BdcNetworkFactory` to be instantiated.

---

## TASK-P6-001: Update Integration Test Harnesses

**Design Reference:** DESIGN.md — Phase 5.5 (headless tests)

**Scope:**
- Update `HrotRunnerHarness` (used by `AllSubsystemsClusterTransitionTests` and similar)
  to inject either `NedNetworkFactory` or `MockNetworkFactory` depending on test requirements
- Update `EditorHarness` to use `OfflineNetworkFactory` (from TASK-P4-005)
- Add a `MockNetworkFactory` to the test utilities project that provides stub
  implementations of `IReplicationModule`, `ICommandGateway`, `IIngressHandler`
  for pure domain testing (no DDS required)
- Update existing integration tests that previously used `StubRequestSource` or
  `_networkEnabled = false` patterns to use `MockNetworkFactory` injection instead

**Constraints:**
- Test projects must not reference `Hrot.Network.NED` or `Hrot.Network.BDC` unless
  they specifically test the wire protocol (DDS loopback tests)
- Pure domain tests must compile and pass without CycloneDDS installed
- E2E loopback tests explicitly reference `Hrot.Network.NED` and require a NED DDS
  environment

**Success Conditions:**
- `Hrot.SimHost.Tests` passes without CycloneDDS (uses `MockNetworkFactory`).
- `Hrot.SimHost.Integration.Tests` passes with `NedNetworkFactory` on loopback domain.
- `Hrot.SimHost.Integration.Tests` passes with `BdcNetworkFactory` on loopback domain
  (same test suite, different factory injected via test parameter or harness config).
- `EditorHarness` integration tests pass with `OfflineNetworkFactory`.
- Code review (recorded in the batch report) confirms that `HrotRunnerHarness` and
  `CgfHarness` exclusively instantiate and `Dispose` the `DdsParticipant` for E2E
  loopback tests, passing it down into the concrete `INetworkFactory` constructor.
  No static or shared participant is used. Parallel test runs must not collide on
  DDS domain ports (each harness instance uses a distinct domain ID or participant
  lifecycle that is torn down in `[TearDown]` / `IDisposable.Dispose`).
