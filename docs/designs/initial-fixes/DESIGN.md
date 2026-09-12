# Initial Fixes Design

**Source Design Talk:** [ios-ig-simhost-initial-fixes.md](../design/ios-ig-simhost-initial-fixes.md)

## Context

The SimHost, IG, and IOS applications were implemented to spec but contained critical architectural deviations and bugs discovered through a thorough review against the FDP engine's golden examples:

- **`Fdp.Examples.NetworkDemo`** — gold standard for distributed ownership, translator patterns, and DDS topic publication
- **`Fdp.Examples.UrbanCombat`** — gold standard for behavior-driven behavior control and entity lifecycle

The IOS implementation was rated architecturally excellent; the bugs are concentrated in SimHost and IG. Additionally both IG and IOS have UI panel wiring issues that prevent any panels from appearing at startup.

---

## Phase 1: SimHost Architecture Fixes

**Goal:** Make SimHost a fully compliant FDP authority node — correct physics component assignment, proper behavior preemption signalling, and complete DDS topic publication.

### 1.1 Remove VehicleState Contamination

`DescriptorMapper.MapToComponents` unconditionally adds a `VehicleState` component to every entity that has a `WorldPos` descriptor. `VehicleState` is only valid for wheeled entities. Its presence on infantry or aircraft breaks `LinearKinematicsSystem` (which filters *out* entities bearing `VehicleState`) and causes stuck or crashing non-vehicle entities.

The TKB template already adds `VehicleState` only when appropriate; the extra line in `DescriptorMapper` must be deleted.

**Files:** `Hrot.SimHost/Util/DescriptorMapper.cs`

### 1.2 Fix Behavior Preemption

`MissionAdapterSystem` updates `ActiveBehaviorHash` when a new behavior arrives but never increments `BehaviorState.InstanceId`. `ChannelArbitrationSystem` (Behavior toolkit) uses `InstanceId` change detection to preempt stale locomotion and weapon channels — without the increment, old channels accumulate indefinitely.

Pattern from `UrbanCombat`'s `BehaviorIngressSystem`: always `unchecked { behavior.InstanceId++; }` alongside a hash change.

**Files:** `Hrot.SimHost/Systems/MissionAdapterSystem.cs`

### 1.3 Publish EntityMaster DDS Topic

SimHost is the owning authority for all entities, but it never publishes the `EntityMaster` DDS topic. IG and IOS subscribe to this topic to learn which entities exist. Without it, neither node will ever receive entity creation events and the battlefield remains empty.

`Hrot.NED.Descriptors.EntityMaster` lacks the `[FdpDescriptor]` attribute, so `ReplicationBootstrap` does not auto-generate a translator. An `AutoCycloneTranslator<EntityMaster>` must be created manually and inserted into the `translators` list before `CycloneNetworkModule` is built.

**Files:** `Hrot.SimHost/Program.cs`

---

## Phase 2: IG Architecture Fixes

**Goal:** Make IG a fully compliant FDP ghost-reader node — correct remote ownership tagging, dead-reckoning interpolation, and DDS-routed entity creation.

### 2.1 Fix Ghost Ownership Theft

`EntityMasterTranslator` sets `OwnerNodeId = IgNetworkConstants.LocalNodeId` when creating replicated entities. Because IG is a read-only ghost node, this makes the ECS tag every entity with `NetworkAuthority.HasAuthority = true`, causing `TransformSyncSystem` to skip dead-reckoning for all of them (it only interpolates *remote* entities).

Fix: use owner ID `0` to force remote ownership on all replicated entities.

**Files:** `Hrot.IG/Translators/EntityMasterTranslator.cs`

### 2.2 Register TransformSyncSystem

`TransformSyncSystem` is absent from `IgApplication`. `WorldPosTranslator` writes incoming network coordinates to `NetworkPosition`, but without the sync system those positions are never interpolated into the visual `SimTransform`. All entities appear frozen at their spawn coordinates.

Fix: call `_kernel.RegisterGlobalSystem(new TransformSyncSystem(driveFromNetwork: true))` in `IgApplication` before `_kernel.Initialize()`.

**Files:** `Hrot.IG/IgApplication.cs`

### 2.3 Fix Rogue Local Spawning

`CreationTool.HandleClick` publishes a local `SpawnEntityCommand` to `FdpEventBus`. This bypasses SimHost entirely — the entity is created in the IG's local ECS only, is never simulated, and is never visible to IOS. Task `IG.3.3` specifies that the tool must send a `CreateEntityRequest` over DDS via the `BdcCommandGateway`.

Fix: inject `IDdsWriter<CreateEntityRequest>` (or `BdcCommandGateway`) into `CreationTool` and write to DDS instead.

**Files:** `Hrot.IG/Tools/CreationTool.cs`

---

## Phase 3: UI Panel Activation

**Goal:** Make all ImGui panels visible and functional in both IOS and IG at application startup.

### 3.1 Uncomment IOS Draw Methods

The IOS `Draw()` implementations were left commented out as part of Phase P9 stubbing. The panels are otherwise complete. Fixes required:

- `IosMock.DrawUI()` — uncomment the full ImGui docking layout and all panel `Draw()` calls; add `using ImGuiNET;`
- All seven panel files in `Hrot.ExCon/Panels/` — uncomment the ImGui body of each `Draw(IIosLogic logic)` method; add `using ImGuiNET;` where missing

**Files:** `Hrot.ExCon/IosMock.cs`, `Hrot.ExCon/Panels/ConfigPanel.cs`, `DiagnosticsPanel.cs`, `InspectorPanel.cs`, `InteractionPanel.cs`, `MissionPanel.cs`, `OrbatPanel.cs`, `SpawnerPanel.cs`

### 3.2 Connect IG UI Panels to App Loop

The IG panel classes (`IgDebugPanel`, `EntityInspectorPanel`, `MiniIosPanel`, `PerformanceOverlay`) exist in `Hrot.IG/UI/` but are never instantiated or called. Required changes to `IgApplication.cs`:

1. Add `using ImGuiNET;` and `using Hrot.IG.UI;`
2. Add private fields for all four panel states and panels
3. Initialize them at the bottom of `InitializeEcs()`
4. In `Run()`: gate `HandleCameraInput` and canvas updates behind `!ImGui.GetIO().WantCaptureMouse`, update UI states each frame, and call `Draw()` on each panel between `rlImGui.Begin()` / `rlImGui.End()`
5. Add a `GetSelectedEntity()` helper used by `EntityInspectorState.Refresh()`

**Files:** `Hrot.IG/IgApplication.cs`
