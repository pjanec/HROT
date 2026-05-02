# Task Detail: DDS-to-ECS Architectural Cleanup

**Design Reference:** See [DESIGN.md](./DESIGN.md) for the full architectural context, principles,
and target state for each phase.  
**Tracker:** See [TASK-TRACKER.md](./TASK-TRACKER.md) for progress status.

---

## Phase 1: Purify DDS Data Model

### DDS2ECS-S1T1 — Remove `[ComponentId]` from `EntityMaster`

**File:** `Hrot.NED/GenericDescriptors.cs`

**Context:** See [DESIGN.md §2.1](./DESIGN.md#21-violations-in-hrotddsdatamodel) and
[DESIGN.md §3.1](./DESIGN.md#31-dds-data-model--no-ecs-attributes).

**Change:**  
Delete the line `[ComponentId(GlobalComponentIds.EntityMaster)]` from the `EntityMaster` struct
declaration. Retain all DDS attributes (`[DdsTopic]`, `[DdsIdlFile]`, `[DdsQos]`).

**Success Conditions:**

1. **Unit test — reflection guard** (`Hrot.NED.Tests` or `Hrot.SimHost.Tests`):  
   `EntityMaster_HasNo_ComponentIdAttribute` — assert that
   `typeof(EntityMaster).GetCustomAttributes(typeof(ComponentIdAttribute), false)` is empty.

2. **Compilation gate:** After this change, `AutoCycloneTranslator<EntityMaster>` in
   `Hrot.SimHost/SimHostApp.cs` will fail to compile (the auto-translator requires a
   `[ComponentId]` on the type). This is expected; Phase 3 will fix SimHost. Both projects must
   compile cleanly after all phases are applied.

---

### DDS2ECS-S1T2 — Remove `[ComponentId]` from `EntityDamage`

**File:** `Hrot.NED/SimDescriptors.cs`

**Context:** See [DESIGN.md §2.1](./DESIGN.md#21-violations-in-hrotddsdatamodel).

**Change:**  
Delete the line `[ComponentId(GlobalComponentIds.EntityDamage)]` from the `EntityDamage` struct
declaration.

**Success Conditions:**

1. **Unit test — reflection guard:**  
   `EntityDamage_HasNo_ComponentIdAttribute` — assert
   `typeof(EntityDamage).GetCustomAttributes(typeof(ComponentIdAttribute), false)` is empty.

2. **No other code in `Hrot.*` (non-FDP) may reference `GlobalComponentIds.EntityDamage`**
   after this change. Verify with a solution-wide search.

---

## Phase 2: SimHost — Fix `DescriptorMapper`

### DDS2ECS-S2T1 — `dtEntityMaster` case produces nothing

**File:** `Hrot.SimHost/Util/DescriptorMapper.cs`

**Context:** See [DESIGN.md §3.2](./DESIGN.md#32-simhost--descriptormapper).

**Change:**  
Replace the `dtEntityMaster` case body with a `break` (or remove the case entirely, falling
through to the default `FdpLog.Warn` — whichever is clearer). No item must be added to `result`.

The `TkbType` is already threaded through `SpawnEntityCommand.TkbType` by the SimHost spawning
code that calls `DescriptorMapper.ExtractTkbType` separately. No additional ECS component is
needed.

**Success Conditions:**

1. **Unit test** `DescriptorMapperTests.MapToComponents_EntityMasterDescriptor_ReturnsEmptyList`:  
   Build a `List<EntityDescriptorUnion>` containing a single `dtEntityMaster` entry.  
   Call `DescriptorMapper.MapToComponents(descriptors, geoTransform: null)`.  
   Assert result is empty.

2. **Unit test** `DescriptorMapperTests.MapToComponents_EntityMasterDescriptor_NoEntityMasterType`:  
   Same setup. Assert result contains no instance of type `EntityMaster`.

---

### DDS2ECS-S2T2 — `dtEntityInfo` case produces nothing

**File:** `Hrot.SimHost/Util/DescriptorMapper.cs`

**Context:** See [DESIGN.md §3.2](./DESIGN.md#32-simhost--descriptormapper). SimHost does not
need `EntityInfo` at spawn time; the SimHost authority placed the name/affiliation itself.

**Change:**  
Replace the `dtEntityInfo` case body with a `break`. No item added to `result`.

**Success Conditions:**

1. **Unit test** `DescriptorMapperTests.MapToComponents_EntityInfoDescriptor_ReturnsEmptyList`:  
   Build a `List<EntityDescriptorUnion>` with a single `dtEntityInfo` entry.  
   Assert result is empty.

2. **Unit test** `DescriptorMapperTests.MapToComponents_EntityInfoDescriptor_NoEntityInfoType`:  
   Assert result contains no instance of type `EntityInfo`.

---

### DDS2ECS-S2T3 — `dtWorldPos` case: remove raw DTO, add `GeoTransform`

**File:** `Hrot.SimHost/Util/DescriptorMapper.cs`

**Context:** See [DESIGN.md §3.2](./DESIGN.md#32-simhost--descriptormapper).

**Change:**  
In the `dtWorldPos` case:
- **Remove** `result.Add(d.WorldPos)`.
- **Keep** the existing `SimTransform` generation (WGS84 → Cartesian conversion).
- **Add** a `GeoTransform` to `result` containing the raw geodetic coordinates:
  ```csharp
  result.Add(new GeoTransform
  {
      Latitude    = (float)d.WorldPos.Pos.Latitude,
      Longitude   = (float)d.WorldPos.Pos.Longitude,
      Altitude    = (float)d.WorldPos.Pos.Altitude,
      HeadingDeg  = d.WorldPos.Rot.Heading,
      PitchDeg    = d.WorldPos.Rot.Pitch,
      RollDeg     = d.WorldPos.Rot.Roll
  });
  ```
  *(Adjust field names to match the actual `GeoTransform` struct. If `GeoTransform` already covers
  these fields, use them directly.)*

**Success Conditions:**

1. **Unit test** `DescriptorMapperTests.MapToComponents_WorldPosDescriptor_ContainsSimTransform`:  
   Provide a `dtWorldPos` descriptor with known lat/lon/alt.  
   Assert result contains exactly one `SimTransform`.  
   Assert its `Position` matches the expected Cartesian output from the mock geo transform.

2. **Unit test** `DescriptorMapperTests.MapToComponents_WorldPosDescriptor_ContainsGeoTransform`:  
   Same setup. Assert result contains exactly one `GeoTransform`.  
   Assert `GeoTransform.Latitude` equals the input latitude (round-trip fidelity).

3. **Unit test** `DescriptorMapperTests.MapToComponents_WorldPosDescriptor_NoRawWorldPosType`:  
   Assert result contains no instance of type `WorldPos`.

4. **Existing test** `DescriptorMapperTests` (all passing before) still pass.

---

### DDS2ECS-S2T4 — `dtWorldPos` case: translate to `GeoVelocity`

**File:** `Hrot.SimHost/Util/DescriptorMapper.cs`

**Context:** See [DESIGN.md §3.2](./DESIGN.md#32-simhost--descriptormapper). The DR descriptor
carries velocity (polar vector: azimuth=heading, elevation=pitch, length=speed) and angular
velocity. The `GeoVelocity` ECS component holds this in the engine's internal representation.

**Change:**  
Replace `result.Add(d.WorldPos)` with a translation to `GeoVelocity`:
```csharp
result.Add(new GeoVelocity
{
    SpeedMs      = (float)d.WorldPos.Vel.Length,
    HeadingDeg   = d.WorldPos.Vel.Azim,
    PitchDeg     = d.WorldPos.Vel.Elev,
    HeadingRateDeg = d.WorldPos.RotVel.Heading
    // Populate remaining fields per GeoVelocity struct definition
});
```
*(Adjust to the actual `GeoVelocity` struct field names.)*

**Success Conditions:**

1. **Unit test** `DescriptorMapperTests.MapToComponents_WorldPosDescriptor_ContainsGeoVelocity`:  
   Provide a `dtWorldPos` descriptor with speed=15, heading=90.  
   Assert result contains exactly one `GeoVelocity`.  
   Assert `GeoVelocity.SpeedMs ≈ 15` and `GeoVelocity.HeadingDeg ≈ 90`.

2. **Unit test** `DescriptorMapperTests.MapToComponents_WorldPosDescriptor_NoRawWorldPosType`:  
   Assert result contains no instance of type `WorldPos`.

---

## Phase 3: SimHost — Replace `AutoCycloneTranslator<EntityMaster>`

### DDS2ECS-S3T1 — Create `EntityMasterEgressTranslator`

**File (new):** `Hrot.SimHost/Translators/EntityMasterEgressTranslator.cs`

**Context:** See [DESIGN.md §3.3](./DESIGN.md#33-simhost--entitymaster-egress).

**Behaviour:**  
- `PollIngress` — no-op (SimHost is the authority for EntityMaster).
- `ScanAndPublish` — queries entities with `NetworkIdentity` + `NetworkOwnership` +
  `NetworkSpawnRequest`. For each entity where `PrimaryOwnerId == LocalNodeId`, constructs a
  `EntityMaster` DDS DTO from FDP-internal components and writes it via `DdsWriter<EntityMaster>`.
- `ApplyToEntity` — no-op.
- `Dispose(networkEntityId)` — disposes (unregisters) the DDS instance for that entity via the
  `DdsWriter`.

**Constructor:** `EntityMasterEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap, long localNodeId)`.

**Success Conditions:**

1. **Unit test** `EntityMasterEgressTranslatorTests.ScanAndPublish_LocallyOwnedEntity_PublishesEntityMasterDTO`:  
   Create a fake `ISimulationView` with one entity having `NetworkIdentity { Value=42 }`,
   `NetworkOwnership { PrimaryOwnerId=1, LocalNodeId=1 }`, `NetworkSpawnRequest { TkbType=777 }`.  
   Assert `DdsWriter.Write` was called with `EntityMaster { EntityId=42, TkbType=777 }`.

2. **Unit test** `EntityMasterEgressTranslatorTests.ScanAndPublish_RemotelyOwnedEntity_DoesNotPublish`:  
   Same setup but `PrimaryOwnerId=2, LocalNodeId=1`.  
   Assert `DdsWriter.Write` was NOT called.

3. **Unit test** `EntityMasterEgressTranslatorTests.Dispose_CallsWriterDispose`:  
   Call `Dispose(42)`.  
   Assert the `DdsWriter` unregistered/disposed instance 42.

---

### DDS2ECS-S3T2 — SimHostApp: replace `AutoCycloneTranslator<EntityMaster>`

**File:** `Hrot.SimHost/SimHostApp.cs`

**Context:** See [DESIGN.md §3.3](./DESIGN.md#33-simhost--entitymaster-egress).

**Change:**  
In `OnLoad`, in the `translators` list setup:
- Remove: `new AutoCycloneTranslator<EntityMaster>(ddsParticipant, "EntityMaster", 0, entityMap)`
- Add: `new EntityMasterEgressTranslator(ddsParticipant, entityMap, SimHostNetworkConstants.LocalNodeId)`

**Success Conditions:**

1. **Compilation:** `AutoCycloneTranslator<EntityMaster>` no longer referenced in `SimHostApp.cs`.
2. **Integration test** `SpawningModuleIntegrationTests` (existing): still passes — entities are
   spawned and `EntityMaster` DDS samples are published during the test scenario.

---

### DDS2ECS-S3T3 — SimHostApp: remove `RegisterComponent<EntityMaster>`

**File:** `Hrot.SimHost/SimHostApp.cs`

**Context:** See [DESIGN.md §2.2](./DESIGN.md#22-violations-in-hrotsimhost).

**Change:**  
In `RegisterSimComponents`, remove the line `world.RegisterComponent<EntityMaster>()`.

**Success Conditions:**

1. **Unit test** (new) `SimHostComponentRegistrationTests.RegisterSimComponents_DoesNotRegisterEntityMaster`:  
   Call `RegisterSimComponents` on a fresh `EntityRepository`.  
   Assert that `world.IsRegistered<EntityMaster>()` (or equivalent API) returns `false`.

2. All existing `Hrot.SimHost.Tests` pass without modifications.

---

### DDS2ECS-S3T4 — SimHostApp: fix `onEntitySpawned` callback

**File:** `Hrot.SimHost/SimHostApp.cs`

**Context:** The existing callback reads:
```csharp
if (isLocalAuthority && world.HasComponent<EntityMaster>(entity))
    world.SetAuthority<EntityMaster>(entity, true);
```
After removing `EntityMaster` from the ECS, `HasComponent<EntityMaster>` will throw or always
return false. The intent was to mark the EntityMaster descriptor as locally owned.

**Change:**  
Remove both the `if` guard and the `SetAuthority<EntityMaster>` call inside it.  
Local authority is already conveyed via `NetworkAuthority` and `NetworkOwnership` components which
`NetworkSpawningSystem` sets based on `isLocalAuthority`. No additional authority call is needed
for a component that no longer exists in the ECS.

**Success Conditions:**

1. **Integration test** `SpawningModuleIntegrationTests.SpawnEntity_LocalAuthority_HasAuthority`:  
   Assert `NetworkAuthority.HasAuthority == true` on a locally-spawned entity. No change to the
   spawning flow expected; the test validates authority is set correctly by the spawning system.

2. **No compile error:** `world.HasComponent<EntityMaster>` and `world.SetAuthority<EntityMaster>`
   references are gone from `SimHostApp.cs`.

---

## Phase 4: IG — Fix `EntityMasterTranslator`

### DDS2ECS-S4T1 — Spawn path: empty `InitialComponents`

**File:** `Hrot.IG/Translators/EntityMasterTranslator.cs`

**Context:** See [DESIGN.md §3.4](./DESIGN.md#34-ig--entitymastertranslator).

**Change:**  
In `ProcessSample`, in the `else` (new entity) branch:
```csharp
// Before
InitialComponents = new List<object> { master }

// After
InitialComponents = new List<object>()
```

**Success Conditions:**

1. **Unit test (new)** `EntityMasterTranslatorTests.ProcessSample_NewEntity_SpawnCommandHasEmptyInitialComponents`:  
   Call `ProcessSample` with a fresh `EntityMaster` (not in `entityMap`).  
   Capture the published `SpawnEntityCommand` from the event bus.  
   Assert `SpawnEntityCommand.InitialComponents` is empty (count == 0).  
   Assert `SpawnEntityCommand.TkbType` equals `master.TkbType`.

2. **Existing test** `EntityMasterTranslatorTests` (all) still pass.

---

### DDS2ECS-S4T2 — Update path: remove `cmd.SetComponent(existing, master)`

**File:** `Hrot.IG/Translators/EntityMasterTranslator.cs`

**Context:** See [DESIGN.md §3.4](./DESIGN.md#34-ig--entitymastertranslator). The update path
must become a no-op for the ECS. There is no `EntityMaster` ECS component to update; the entity
already exists in the map and its FDP-internal state is managed by the geography and spawning
systems.

**Change:**  
In `ProcessSample`, the `if (_entityMap.TryGetEntity(netId, out var existing))` branch:  
Remove `cmd.SetComponent(existing, master)`. The branch may simply do nothing (or log at debug
level that an update was received for a known entity).

**Success Conditions:**

1. **Unit test (new)** `EntityMasterTranslatorTests.ProcessSample_KnownEntity_DoesNotCallSetComponent`:  
   Pre-register the entity in the `entityMap`.  
   Call `ProcessSample` with an `EntityMaster` for that entity.  
   Assert that the mock `IEntityCommandBuffer.SetComponent` was **not** called.  
   Assert that no `SpawnEntityCommand` or `UpdateEntityCommand` was published.

---

### DDS2ECS-S4T3 — `ApplyToEntity`: become a no-op

**File:** `Hrot.IG/Translators/EntityMasterTranslator.cs`

**Context:** `ApplyToEntity` is called during ghost promotion (late-join). Since `EntityMaster` is
no longer an ECS component, there is nothing to apply.

**Change:**  
Replace the `ApplyToEntity` body with an empty implementation:
```csharp
public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
```

**Success Conditions:**

1. **Unit test (new)** `EntityMasterTranslatorTests.ApplyToEntity_IsNoOp`:  
   Call `ApplyToEntity(entity, new EntityMaster { EntityId = 1 }, repo)`.  
   Assert no exception is thrown and `repo.SetComponent` was **not** called.

---

## Phase 5: IG — Create `IgEntityData` and Fix `EntityInfoTranslator`

### DDS2ECS-S5T1 — Create `IgEntityData` component

**File (new):** `Hrot.IG/Components/IgEntityData.cs`

**Context:** See [DESIGN.md §3.5](./DESIGN.md#35-ig--igentitydata-new-internal-component).

**Specification:**
```csharp
[ComponentId(GlobalComponentIds.IgEntityData)]  // Allocate a free ID; see §Note below
public class IgEntityData
{
    public string Name        { get; set; } = string.Empty;
    public ForceId ForceId    { get; set; } = ForceId.Unknown;
    public int CommanderId    { get; set; } = 0;
}
```

> **Note on ComponentId:** Allocate an ID in the `Hrot.*` reserved range in
> `FDP/Kernel/Fdp.Kernel/GlobalComponentIds.cs`. The nearest free byte above the `EntityDamage
> = 161` entry is the appropriate location. Coordinate with the lead to assign the value.

**Success Conditions:**

1. **Unit test** `IgEntityDataTests.DefaultValues_AreCorrect`:  
   `new IgEntityData()` → `Name == ""`, `ForceId == ForceId.Unknown`, `CommanderId == 0`.

2. **Attribute check:** `typeof(IgEntityData).GetCustomAttribute<ComponentIdAttribute>()` is not null.

---

### DDS2ECS-S5T2 — `EntityInfoTranslator`: translate to `IgEntityData`

**File:** `Hrot.IG/Translators/EntityInfoTranslator.cs`

**Context:** See [DESIGN.md §3.5](./DESIGN.md#35-ig--igentitydata-new-internal-component).

**Change in `PollIngress`:**  
Replace `ComponentsToUpdate = new List<object> { info }` with:
```csharp
var igData = new IgEntityData
{
    Name       = info.Name,
    ForceId    = (ForceId)(int)info.ForceIdentifier,
    CommanderId = info.CommanderId
};
_eventBus.PublishManaged(new UpdateEntityCommand
{
    NetworkId          = netId,
    ComponentsToUpdate = new List<object> { igData },
    RequestId          = Guid.Empty,
});
```

**Success Conditions:**

1. **Unit test (new)** `EntityInfoTranslatorTests.ProcessSample_PublishesUpdateWithIgEntityData`:  
   Produce an `EntityInfo` sample with `Name="Alpha-1"`, `ForceIdentifier=FORCE_FRIENDLY`.  
   Pre-register the entity in entityMap.  
   Capture the `UpdateEntityCommand` from the event bus.  
   Assert `ComponentsToUpdate` contains an `IgEntityData` with `Name == "Alpha-1"` and
   `ForceId == ForceId.Friend`.

2. **Unit test (new)** `EntityInfoTranslatorTests.ProcessSample_DoesNotIncludeRawEntityInfo`:  
   Same setup. Assert `ComponentsToUpdate` contains no instance of type `EntityInfo`.

---

### DDS2ECS-S5T3 — `EntityInfoTranslator.ApplyToEntity`: use `IgEntityData`

**File:** `Hrot.IG/Translators/EntityInfoTranslator.cs`

**Change:**  
Replace the `ApplyToEntity` body:
```csharp
public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
{
    if (data is EntityInfo info)
    {
        repo.SetManagedComponent(entity, new IgEntityData
        {
            Name        = info.Name,
            ForceId     = (ForceId)(int)info.ForceIdentifier,
            CommanderId  = info.CommanderId
        });
    }
}
```

**Success Conditions:**

1. **Unit test (new)** `EntityInfoTranslatorTests.ApplyToEntity_SetsIgEntityData`:  
   Call `ApplyToEntity(entity, new EntityInfo { Name="Bravo", ForceIdentifier=FORCE_OPPOSING }, repo)`.  
   Assert `repo.GetManagedComponent<IgEntityData>(entity).Name == "Bravo"`.  
   Assert `repo.GetManagedComponent<IgEntityData>(entity).ForceId == ForceId.Hostile`.

---

### DDS2ECS-S5T4 — `IgApplication`: register `IgEntityData`

**File:** `Hrot.IG/IgApplication.cs`

**Change:**  
In `InitializeEcs`, add:
```csharp
_world.RegisterManagedComponent<IgEntityData>();
```

**Success Conditions:**

1. **Unit test** `IgApplicationPanelTests.InitializeEcs_RegistersIgEntityData`:  
   Call `InitializeEcs` (or `InitializeEmbedded(headless: true)`).  
   Assert `_world.IsRegisteredManaged<IgEntityData>()` returns `true`.

---

## Phase 6: IG — Create `IgHealthState` and `EntityDamageTranslator`

### DDS2ECS-S6T1 — Create `IgHealthState` component

**File (new):** `Hrot.IG/Components/IgHealthState.cs`

**Context:** See [DESIGN.md §3.6](./DESIGN.md#36-ig--ighealthstate-new-internal-component).

**Specification:**
```csharp
[ComponentId(GlobalComponentIds.IgHealthState)]  // Allocate next free ID
public struct IgHealthState
{
    /// <summary>0 = healthy, 100 = fully destroyed.</summary>
    public float Damage;
}
```

**Success Conditions:**

1. **Unit test** `IgHealthStateTests.DefaultValue_IsZero`: `new IgHealthState().Damage == 0f`.
2. **Unit test** `IgHealthStateTests.HasComponentIdAttribute`.

---

### DDS2ECS-S6T2 — Create `EntityDamageTranslator`

**File (new):** `Hrot.IG/Translators/EntityDamageTranslator.cs`

**Context:** See [DESIGN.md §3.6](./DESIGN.md#36-ig--ighealthstate-new-internal-component).

**Specification:**  
Extend `CycloneTranslator<EntityDamage, EntityDamage>`.

```csharp
protected override void Decode(in EntityDamage data, IEntityCommandBuffer cmd, ISimulationView view)
{
    long netId = data.EntityId;
    if (!EntityMap.TryGetEntity(netId, out var entity))
        return;

    cmd.SetComponent(entity, new IgHealthState { Damage = data.Damage });
}

public override void ScanAndPublish(ISimulationView view) { }  // IG is ghost-only
```

**Success Conditions:**

1. **Unit test** `EntityDamageTranslatorTests.Decode_KnownEntity_SetsIgHealthState`:  
   Pre-register entity 42 in entityMap.  
   Call `Decode(new EntityDamage { EntityId=42, Damage=75f }, cmd, view)`.  
   Assert `cmd.SetComponent` was called with `IgHealthState { Damage ≈ 75f }`.

2. **Unit test** `EntityDamageTranslatorTests.Decode_UnknownEntity_IsSkipped`:  
   Entity 99 not in entityMap.  
   Call `Decode(new EntityDamage { EntityId=99 }, cmd, view)`.  
   Assert `cmd.SetComponent` was NOT called.

---

### DDS2ECS-S6T3 — `IgApplication`: register `EntityDamageTranslator`

**File:** `Hrot.IG/IgApplication.cs`

**Change:**  
In `InitializeNetwork`, in the `customTranslators` list:
```csharp
new EntityDamageTranslator(participant, _entityMap),
```

**Success Conditions:**

1. **Integration / headless init:** `InitializeEmbedded(headless: true)` completes without exception.
2. The translator appears in the CycloneNetworkModule's descriptor translator list.

---

### DDS2ECS-S6T4 — `IgApplication`: register `IgHealthState`

**File:** `Hrot.IG/IgApplication.cs`

**Change:**  
In `InitializeEcs`:
```csharp
_world.RegisterComponent<IgHealthState>();
```

**Success Conditions:**

1. **Unit test** `IgApplicationPanelTests.InitializeEcs_RegistersIgHealthState`:  
   Assert `_world.IsRegistered<IgHealthState>()` returns `true` after `InitializeEcs`.

---

## Phase 7: IG — Create `MapEntitySymbolTranslator`

### DDS2ECS-S7T1 — Create `MapEntitySymbolTranslator`

**File (new):** `Hrot.IG/Translators/MapEntitySymbolTranslator.cs`

**Context:** See [DESIGN.md §3.7](./DESIGN.md#37-ig--mapentitysymboltranslator). The
`IgSymbolOverride` component already exists in `Hrot.IG/Components/IgSymbolOverride.cs`.

**Specification:**  
Extend `CycloneTranslator<MapEntitySymbol, MapEntitySymbol>`.

Resolution priority per `MapEntitySymbol.MapGroupId`:
- `MapGroupId == 0` → global override, applies when no scoped override is present.
- `MapGroupId == IgNetworkConstants.MapGroupId` → scoped to this IG instance, highest priority.
- All other `MapGroupId` values → ignore.

```csharp
protected override void Decode(in MapEntitySymbol data, IEntityCommandBuffer cmd, ISimulationView view)
{
    // Filter: only accept global (0) or this IG's group
    if (data.MapGroupId != 0 && data.MapGroupId != _mapGroupId)
        return;

    long netId = data.EntityId;
    if (!EntityMap.TryGetEntity(netId, out var entity))
        return;

    cmd.SetManagedComponent(entity, new IgSymbolOverride
    {
        StyleSetId      = string.IsNullOrEmpty(data.StyleSetId) ? null : data.StyleSetId,
        TextureOverride = null  // JSON params parsing deferred to a future task
    });
}

public override void ScanAndPublish(ISimulationView view) { }  // IG is ghost-only
```

**Constructor:** `MapEntitySymbolTranslator(DdsParticipant participant, NetworkEntityMap entityMap, int mapGroupId)`.

**Success Conditions:**

1. **Unit test** `MapEntitySymbolTranslatorTests.Decode_GlobalOverride_SetsIgSymbolOverride`:  
   Entity 10 in entityMap. `MapEntitySymbol { EntityId=10, MapGroupId=0, StyleSetId="hostile" }`.  
   Assert `cmd.SetManagedComponent` called with `IgSymbolOverride { StyleSetId="hostile" }`.

2. **Unit test** `MapEntitySymbolTranslatorTests.Decode_ScopedOverrideMatchingGroup_SetsIgSymbolOverride`:  
   Translator constructed with `mapGroupId=5`. `MapEntitySymbol { EntityId=10, MapGroupId=5, StyleSetId="friendly" }`.  
   Assert `cmd.SetManagedComponent` called.

3. **Unit test** `MapEntitySymbolTranslatorTests.Decode_ScopedOverrideWrongGroup_IsIgnored`:  
   Translator `mapGroupId=5`. `MapEntitySymbol { EntityId=10, MapGroupId=7 }`.  
   Assert `cmd.SetManagedComponent` NOT called.

4. **Unit test** `MapEntitySymbolTranslatorTests.Decode_UnknownEntity_IsSkipped`:  
   Entity 99 not in entityMap. Assert `cmd.SetManagedComponent` NOT called.

---

### DDS2ECS-S7T2 — `IgApplication`: register `MapEntitySymbolTranslator`

**File:** `Hrot.IG/IgApplication.cs`

**Change:**  
In `InitializeNetwork`, in the `customTranslators` list:
```csharp
new MapEntitySymbolTranslator(participant, _entityMap, IgNetworkConstants.MapGroupId),
```
*(Add `MapGroupId` constant to `IgNetworkConstants` if not already present.)*

**Success Conditions:**

1. **Integration / headless init:** `InitializeEmbedded(headless: true)` completes without exception.
2. The translator appears in the network module's descriptor translator list.

---

## Phase 8: IG — Fix `IgApplication` Registrations and Queries

### DDS2ECS-S8T1 — Remove `RegisterComponent<EntityMaster>()` from `InitializeEcs`

**File:** `Hrot.IG/IgApplication.cs`

**Context:** See [DESIGN.md §2.3](./DESIGN.md#23-violations-in-hrotig).

**Change:**  
Remove the line `_world.RegisterComponent<EntityMaster>();` from `InitializeEcs`.

**Success Conditions:**

1. **Unit test (new)** `IgApplicationPanelTests.InitializeEcs_DoesNotRegisterEntityMaster`:  
   After `InitializeEmbedded(headless: true)`, assert `_world.IsRegistered<EntityMaster>()`
   returns `false`.

2. All existing `Hrot.IG.Tests` pass.

---

### DDS2ECS-S8T2 — Render query: replace `.With<EntityMaster>()` with `.With<NetworkIdentity>()`

**File:** `Hrot.IG/IgApplication.cs`

**Context:** See [DESIGN.md §3.8](./DESIGN.md#38-ig--igapplication-queries).

**Change:**  
In `InitializeNetwork`, where the entity render query is built:
```csharp
// Before
var query = _world.Query()
    .With<EntityMaster>()
    .With<SimTransform>()
    .Build();

// After
var query = _world.Query()
    .With<NetworkIdentity>()
    .With<SimTransform>()
    .Build();
```

**Success Conditions:**

1. **Unit test (new)** `IgApplicationPanelTests.EntityRenderQuery_MatchesEntityWithNetworkIdentityAndSimTransform`:  
   Create an entity with `NetworkIdentity` + `SimTransform`. Assert it is returned by the query.

2. **Unit test (new)** `IgApplicationPanelTests.EntityRenderQuery_DoesNotMatchEntityWithoutNetworkIdentity`:  
   Create an entity with only `SimTransform`. Assert it is NOT returned by the query.

---

### DDS2ECS-S8T3 — `DisTypeExtractor`: use `NetworkSpawnRequest` instead of `EntityMaster`

**File:** `Hrot.IG/IgApplication.cs`

**Context:** See [DESIGN.md §3.8](./DESIGN.md#38-ig--igapplication-queries).

**Change:**  
Replace the `DisTypeExtractor` lambda:
```csharp
// Before
DisTypeExtractor disExtractor = (object c, out ulong dis) =>
{
    if (c is EntityMaster m) { dis = m.DisType; return true; }
    dis = 0; return false;
};

// After
DisTypeExtractor disExtractor = (object c, out ulong dis) =>
{
    if (c is NetworkSpawnRequest req) { dis = req.DisType; return true; }
    dis = 0; return false;
};
```
*(Verify that `NetworkSpawnRequest` carries a `DisType` field. If not, consult the FDP lead for
the correct source of `DisType` — likely the `TkbDatabase` lookup by `TkbType`.)*

**Success Conditions:**

1. **Unit test (new)** `IgApplicationPanelTests.DisTypeExtractor_NetworkSpawnRequest_ReturnsDis`:  
   `disExtractor(new NetworkSpawnRequest { DisType = 0x0100_0000_0000_0001UL }, out var dis)`.  
   Assert returns `true` and `dis == expected`.

2. **Unit test (new)** `IgApplicationPanelTests.DisTypeExtractor_EntityMaster_ReturnsFalse`:  
   `disExtractor(new EntityMaster { DisType = 999 }, out var dis)`.  
   Assert returns `false` (EntityMaster is no longer a recognised input type).

---

## Phase 9: Network Cleanup System

See [DESIGN.md §6.1](./DESIGN.md#61-deviation-1--no-network-cleanup-zombie-entities).

### DDS2ECS-S9T1 — `SimHostApp`: register `CycloneNetworkCleanupSystem`

**File:** `Hrot.SimHost/SimHostApp.cs`

**Context:** Without this system, destroyed entities never send a DDS dispose sample, so IG
ghosts freeze as zombies.

**Change:**  
In `OnLoad`, after constructing `EntityMasterEgressTranslator` (Phase 3), register the cleanup
system:
```csharp
_kernel.RegisterGlobalSystem(new CycloneNetworkCleanupSystem(entityMasterEgressTranslator));
```

**Success Conditions:**

1. **Unit test (new)** `SimHostComponentRegistrationTests.OnLoad_RegistersCycloneNetworkCleanupSystem`:  
   After `InitializeHeadless()`, assert that the kernel's global system list contains an instance
   of `CycloneNetworkCleanupSystem`.

2. **Integration test** `EntityDestroyIntegrationTests.SimHost_DestroyEntity_IgGhostIsRemoved`
   (Phase 15): spawns an entity, destroys it in SimHost, asserts IG world no longer contains
   the ghost within 100 frames.

---

### DDS2ECS-S9T2 — `SimHostSubsystem`: same registration

**File:** `Hrot.ClusterRunner/Services/SimHostSubsystem.cs`

**Change:**  
Mirror the `CycloneNetworkCleanupSystem` registration from S9T1 in `SimHostSubsystem.Initialize`
so the Runner path behaves identically to the standalone app.

**Success Conditions:**

1. **Unit test (new)** `SimHostSubsystemTests.Initialize_RegistersCycloneNetworkCleanupSystem`:  
   After `subsystem.Initialize(headlessConfig)`, assert the kernel's global systems include
   `CycloneNetworkCleanupSystem`.

---

## Phase 10: Dead Reckoning

See [DESIGN.md §6.3](./DESIGN.md#63-deviation-3--hard-snapping-vs-dead-reckoning-stuttering-movement)
and [DESIGN.md §7](./DESIGN.md#7-geotransform-vs-networkposition--egressingress-architecture).

### DDS2ECS-S10T1 — Fix `WorldPosTranslator.Decode` (IG): write `NetworkPosition`

**File:** `Hrot.IG/Translators/WorldPosTranslator.cs`

**Change:**  
Replace the `cmd.SetComponent(entity, new SimTransform { ... })` call with:
```csharp
cmd.SetComponent(entity, new NetworkPosition { Value = new Vector3((float)cartesian.X, (float)cartesian.Y, (float)cartesian.Z) });
```
If the entity has no `SimTransform` yet (first update), also initialise it:
```csharp
if (!view.HasComponent<SimTransform>(entity))
    cmd.AddComponent(entity, new SimTransform { Position = new Vector3((float)cartesian.X, (float)cartesian.Y, (float)cartesian.Z) });
```
`ApplyToEntity` similarly sets `NetworkPosition` (not `SimTransform`) so ghost-promotion is
consistent.

**Success Conditions:**

1. **Unit test (new)** `WorldPosTranslatorTests.Decode_KnownEntity_SetsNetworkPosition`:  
   Provide `WorldPos { Pos.Latitude=51, Pos.Longitude=0, Pos.Altitude=0 }`.  
   Assert `cmd.SetComponent` was called with a `NetworkPosition` whose `Value` is the expected
   Cartesian vector.

2. **Unit test (new)** `WorldPosTranslatorTests.Decode_KnownEntity_DoesNotSetSimTransformDirectly`:  
   Assert `cmd.SetComponent` was NOT called with a `SimTransform`.

---

### DDS2ECS-S10T2 — Create `WorldPosTranslator` (IG)

**File (new):** `Hrot.IG/Translators/WorldPosTranslator.cs`

**Context:** The `WorldPos` topic carries velocity in polar form (`AngularVector`: azimuth, elevation,
length). Convert to a Cartesian `Vector3` for `NetworkVelocity`.

**Specification:**  
Extend `CycloneTranslator<WorldPos, WorldPos>`.

```csharp
protected override void Decode(in WorldPos data, IEntityCommandBuffer cmd, ISimulationView view)
{
    long netId = data.EntityId;
    if (!EntityMap.TryGetEntity(netId, out var entity)) return;

    // Convert AngularVector polar velocity to Cartesian
    float speedMs = (float)data.Vel.Length;
    float azimRad = (float)data.Vel.Azim * (MathF.PI / 180f);
    float elevRad  = (float)data.Vel.Elev * (MathF.PI / 180f);
    var cartVel = new Vector3(
        speedMs * MathF.Cos(elevRad) * MathF.Sin(azimRad),  // East
        speedMs * MathF.Cos(elevRad) * MathF.Cos(azimRad),  // North
        speedMs * MathF.Sin(elevRad)                         // Up
    );

    cmd.SetComponent(entity, new NetworkVelocity { Value = cartVel });
}

public override void ScanAndPublish(ISimulationView view) { }  // IG is ghost-only
```

**Success Conditions:**

1. **Unit test** `WorldPosTranslatorTests.Decode_KnownEntity_SetsNetworkVelocity`:  
   Provide `WorldPos { EntityId=1, Vel = { Azim=0, Elev=0, Length=10 } }` (moving North at 10 m/s).  
   Assert `NetworkVelocity.Value ≈ Vector3(0, 10, 0)`.

2. **Unit test** `WorldPosTranslatorTests.Decode_UnknownEntity_IsSkipped`.

---

### DDS2ECS-S10T3 — Create `DeadReckoningSyncSystem` (IG)

**File (new):** `Hrot.IG/Systems/DeadReckoningSyncSystem.cs`

**Context:** See [DESIGN.md §6.3](./DESIGN.md#63-deviation-3--hard-snapping-vs-dead-reckoning-stuttering-movement).
Uses a **"Project and Blend"** algorithm.

**Specification:**  
Query entities with `SimTransform` + `NetworkPosition` + `NetworkVelocity` + `NetworkAuthority`
where `HasAuthority == false`.

Per entity each frame:
1. **Project:** `projectedNetPos = NetworkPosition.Value + (NetworkVelocity.Value * deltaTime)`  
   Write back: `cmd.SetComponent(entity, new NetworkPosition { Value = projectedNetPos })`
2. **Blend:** `blendedPos = Vector3.Lerp(SimTransform.Position, projectedNetPos, deltaTime * SmoothingRate)`  
   Write: `cmd.SetComponent(entity, new SimTransform { Position = blendedPos, Rotation = simTf.Rotation })`
3. **Sync velocity:** `cmd.SetComponent(entity, new SimVelocity { Linear = NetworkVelocity.Value })`

`SmoothingRate` constant = `10.0f`.

Phase annotation: `[UpdateInPhase(SystemPhase.PostSimulation)]`.

**Success Conditions:**

1. **Unit test** `DeadReckoningSyncSystemTests.Execute_GhostEntity_ProjectsNetworkPosition`:  
   Entity with `NetworkPosition { Value = (0,0,0) }`, `NetworkVelocity { Value = (0,5,0) }`.  
   Run one tick with `dt=0.1f`. Assert new `NetworkPosition.Value ≈ (0, 0.5, 0)`.

2. **Unit test** `DeadReckoningSyncSystemTests.Execute_GhostEntity_BlendsSimTransform`:  
   Same setup with `SimTransform.Position = (0, 10, 0)`.  
   Assert `SimTransform.Position` moved toward `(0, 0.5, 0)` (not teleported).

3. **Unit test** `DeadReckoningSyncSystemTests.Execute_AuthorityEntity_IsSkipped`:  
   `NetworkAuthority.HasAuthority = true`. Assert no components modified.

---

### DDS2ECS-S10T4 — `IgApplication`: register new DR translator and system

**File:** `Hrot.IG/IgApplication.cs`

**Changes:**  
1. In `InitializeNetwork`, add `new WorldPosTranslator(participant, _entityMap, _geoTransform)` to `customTranslators`.
2. Register `DeadReckoningSyncSystem` as a global system:
   ```csharp
   _kernel.RegisterGlobalSystem(new DeadReckoningSyncSystem());
   ```
3. Remove the existing `_kernel.RegisterGlobalSystem(new TransformSyncSystem(...))` call for ghost
   entities, OR verify that `TransformSyncSystem` skips entities that have `NetworkVelocity`
   (to avoid double-interpolation). If `TransformSyncSystem` does not discriminate, remove it and
   rely solely on `DeadReckoningSyncSystem`.

**Success Conditions:**

1. **Integration test** `AdvancedFeaturesIntegrationTests.GhostEntity_MovesSmoothlybetweenPackets`
   (or a new equivalent): spawn a ghost entity, inject two `WorldPos` updates 10 frames apart,
   assert that during the gap the entity continues moving (does not freeze or snap).

2. **Existing test** `Hrot.IG.Tests` (all) still pass.

---

## Phase 11: Time Synchronisation Fix

See [DESIGN.md §6.4](./DESIGN.md#64-deviation-4--broken-distributed-time-synchronisation).

### DDS2ECS-S11T1 — Verify `TimePulseDescriptor` DDS topic registration

**File:** Wherever `TimePulseDescriptor` is defined (FDP toolkit).

**Check:**  
Confirm that `TimePulseDescriptor` has `[DdsTopic("TimePulse")]` (or equivalent attribute) so
CycloneDDS will create a topic for it. If the attribute is absent, add it.

**Success Conditions:**

1. **Reflection test** `TimePulseTranslatorTests.TimePulseDescriptor_HasDdsTopicAttribute`:  
   Assert `typeof(TimePulseDescriptor).GetCustomAttributes()` contains a `DdsTopicAttribute`
   with `Name == "TimePulse"`.

---

### DDS2ECS-S11T2 — `IgApplication`: enable `TimePulseTranslator`

**File:** `Hrot.IG/IgApplication.cs`

**Change:**  
Uncomment the `new TimePulseTranslator(participant, _eventBus)` line in the `customTranslators`
list inside `InitializeNetwork`.

**Success Conditions:**

1. **Unit test** `TimePulseTranslatorTests.PollIngress_Sample_PublishesTimePulseEvent`:  
   Provide a `TimePulseDescriptor` sample.  
   Assert `eventBus.Consume<TimePulseDescriptor>()` returns it.

2. **Integration / headless init:** `InitializeEmbedded(headless: true)` completes without
   exception (i.e., the earlier crash that caused the comment-out is resolved by S11T1).

---

### DDS2ECS-S11T3 — `SimHostApp` / `SimHostSubsystem`: register time-pulse egress

**Files:** `Hrot.SimHost/SimHostApp.cs`, `Hrot.ClusterRunner/Services/SimHostSubsystem.cs`

**Change:**  
Register an egress `TimePulseTranslator` (or equivalent) that broadcasts `TimePulseDescriptor`
samples to DDS on every simulation tick so IG's `SlaveTimeController` can drive its PLL.

*(Exact class name depends on the FDP egress time translator API — check
`FDP/ModuleHost/ModuleHost.Core/Time/` for the canonical master-clock egress translator.)*

**Success Conditions:**

1. **Unit test** `SimHostTimeSyncTests.SimHost_BroadcastsTimePulse_PerTick`:  
   After `InitializeHeadless()` + 1 tick, assert that a `TimePulseDescriptor` was written to
   the (mock) DDS writer.

2. **Integration test:** IG `SlaveTimeController.CurrentTime` advances in step with SimHost after
   10+ frames (validates S11T2 + S11T3 together).

---

## Phase 12: Transient Event Translators

See [DESIGN.md §6.2](./DESIGN.md#62-deviation-2--missing-transient-event-translators-invisible-combat).

### DDS2ECS-S12T1 — Create `FireInteractionEventTranslator`

**File (new):** `Hrot.IG/Translators/FireInteractionEventTranslator.cs` (Ingress side)  
**File (new or in SimHost):** `Hrot.SimHost/Translators/FireInteractionEventTranslator.cs` (Egress side)

*(Both can be the same class if placed in a shared assembly, or separate if they have app-specific
logic. The key distinction is which methods are active: SimHost uses `ScanAndPublish`; IG uses
`PollIngress`.)*

**Specification:**  
Inherit `CycloneNativeEventTranslator<FireInteractionEvent>` (or equivalent FDP base class).

- **SimHost Egress (`ScanAndPublish`):** Drain ECS `FireInteractionEvent` events from the event
  bus and write each to the DDS topic.
- **IG Ingress (`PollIngress`):** Read DDS samples and publish each as `FireInteractionEvent` onto
  `FdpEventBus` so `EventEffectModule` draws the explosion/tracer.

**Success Conditions:**

1. **Unit test** `FireInteractionEventTranslatorTests.SimHost_ScanAndPublish_WritesDdsOnEvent`:  
   Publish a `FireInteractionEvent` on the SimHost event bus. Call `ScanAndPublish`.  
   Assert `DdsWriter.Write` was called with matching data.

2. **Unit test** `FireInteractionEventTranslatorTests.IG_PollIngress_PublishesEventOnBus`:  
   Provide a `FireInteractionEvent` DDS sample.  
   Assert `FdpEventBus.Consume<FireInteractionEvent>()` returns the event.

---

### DDS2ECS-S12T2 — `SimHostApp` / `SimHostSubsystem`: register egress translator

**Files:** `Hrot.SimHost/SimHostApp.cs`, `Hrot.ClusterRunner/Services/SimHostSubsystem.cs`

**Success Conditions:**

1. **Compilation:** `translators` list includes `FireInteractionEventTranslator`.
2. **Unit test** `SimHostComponentRegistrationTests.OnLoad_RegistersFireInteractionEventTranslator`.

---

### DDS2ECS-S12T3 — `IgApplication`: register ingress translator

**File:** `Hrot.IG/IgApplication.cs`

**Success Conditions:**

1. **Compilation:** `customTranslators` list includes `FireInteractionEventTranslator`.
2. **Unit test** `IgApplicationPanelTests.InitializeNetwork_RegistersFireInteractionEventTranslator`.

---

## Phase 13: SimHost Mission Control Reception

See [DESIGN.md §8.4](./DESIGN.md#84-mission-plans).

### DDS2ECS-S13T1 — Create `MissionControlRequestSystem`

**File (new):** `Hrot.SimHost/Systems/MissionControlRequestSystem.cs`

**Behaviour:**  
On each tick, drain the DDS `MissionControlRequest` topic. For each request:
1. Look up the target entity by `NetworkId` from `NetworkEntityMap`.
2. Switch on `CommandType`:
   - `CMD_REPLACE_MISSION` → replace `EntityMissionHolder.Plan` with the new plan.
   - `CMD_JUMP_TO_TASK` → set `EntityMissionHolder.ActiveTaskId` to the requested task.
   - `CMD_ABORT_ALL` → clear the plan and set `ActiveTaskId` to null.
3. Write a `MissionControlAck` to DDS with `RequestId` echoed back and a `Success` flag.

**Success Conditions:**

1. **Unit test** `MissionControlRequestSystemTests.ProcessRequest_JumpToTask_UpdatesActiveTaskId`:  
   Pre-populate `EntityMissionHolder` with tasks `[A, B, C]`, `ActiveTaskId = "A"`.  
   Provide a `MissionControlRequest { CommandType = CMD_JUMP_TO_TASK, TargetTaskId = "C" }`.  
   Run tick. Assert `EntityMissionHolder.ActiveTaskId == "C"`.

2. **Unit test** `MissionControlRequestSystemTests.ProcessRequest_AbortAll_ClearsPlan`:  
   After `CMD_ABORT_ALL`, assert `EntityMissionHolder.Plan.Tasks` is empty.

3. **Unit test** `MissionControlRequestSystemTests.ProcessRequest_WritesAck`:  
   Assert `DdsWriter<MissionControlAck>.Write` called with matching `RequestId` and `Success=true`.

4. **Unit test** `MissionControlRequestSystemTests.ProcessRequest_UnknownEntity_WritesNack`:  
   Provide a request for a `NetworkId` not in the `NetworkEntityMap`.  
   Assert `MissionControlAck` written with `Success=false`.

---

### DDS2ECS-S13T2 — Register `MissionControlRequestSystem`

**Files:** `Hrot.SimHost/SimHostApp.cs`, `Hrot.ClusterRunner/Services/SimHostSubsystem.cs`

**Success Conditions:**

1. `SimHostApp.OnLoad` and `SimHostSubsystem.Initialize` both add `MissionControlRequestSystem`
   to the `_kernelGroup` or equivalent system group.

2. **Existing mission tests** still pass.

---

## Phase 14: IOS Mission Editor UI

See [DESIGN.md §8.5](./DESIGN.md#85-ios-mission-editor-ui--incomplete).

### DDS2ECS-S14T1 — Task-list editing: Add / Insert / Delete

**File:** `Hrot.ExCon/Panels/MissionPanel.cs`

**Change:**  
After the existing task list loop, add ImGui buttons:
- **"+ Add Task"** → appends a new default task (`BehaviorId = ""`, empty params) to the plan.
- **"↑ / ↓"** per-row arrows → reorder tasks.
- **"✕"** per-row delete → removes that task.

All mutations operate on a locally-held draft copy of `MissionPlan`; changes are not committed to
the network until the user clicks "Commit" (S14T3).

**Success Conditions:**

1. **Unit test** `MissionPanelTests.AddTask_AppendsToDraftPlan`:  
   Invoke `HandleAddTask()`. Assert draft plan has one more task.

2. **Unit test** `MissionPanelTests.DeleteTask_RemovesFromDraftPlan`:  
   Three tasks in draft; call `HandleDeleteTask(1)`. Assert two tasks remain.

3. **Unit test** `MissionPanelTests.ReorderTask_ChangesPosition`:  
   Task at index 0 → move down → assert it is now at index 1.

---

### DDS2ECS-S14T2 — `BehaviorId` dropdown and `BehaviorParams` JSON editor

**File:** `Hrot.ExCon/Panels/MissionPanel.cs`

**Change:**  
For each task in the draft list, display:
- An `ImGui.Combo` (dropdown) populated from the behavior registry for `BehaviorId`.
- An `ImGui.InputTextMultiline` for the raw `BehaviorParams` JSON string.

**Success Conditions:**

1. **Unit test** `MissionPanelTests.EditBehaviorId_UpdatesDraftTask`:  
   Set `BehaviorId = "MoveToLocation"` on task 0. Assert draft reflects the change.

2. **Unit test** `MissionPanelTests.EditBehaviorParams_UpdatesDraftTask`:  
   Set params JSON to `{"speed":15}`. Assert draft task `BehaviorParams == {"speed":15}`.

---

### DDS2ECS-S14T3 — "Commit" button wired to `CommitMissionAsync`

**File:** `Hrot.ExCon/Panels/MissionPanel.cs`

**Change:**  
Add an `ImGui.Button("Commit")` that calls:
```csharp
_ = logic.MissionEditorService.CommitMissionAsync(selectedEntityId, draftPlan);
```
Disable the button (grey out) while a commit is in-flight.

**Success Conditions:**

1. **Unit test** `MissionPanelTests.Commit_CallsMissionEditorService`:  
   Click commit. Assert `MissionEditorService.CommitMissionAsync` was called with the draft plan.

2. **Unit test** `MissionPanelTests.Commit_DisabledWhileInFlight`:  
   While a commit is pending, assert the button state is disabled.

---

## Phase 15: Integration Test Harness

See [DESIGN.md §9, Phase 15](./DESIGN.md#phase-15--integration-test-harness).

### DDS2ECS-S15T1 — Add `internal` test-hook properties/methods

**Files:**  
- `Hrot.ClusterRunner/Services/IgSubsystem.cs`
- `Hrot.ClusterRunner/Services/SimHostSubsystem.cs`
- `Hrot.ClusterRunner/Services/IosSubsystem.cs`
- `Hrot.IG/IgApplication.cs`

**Changes:**

```csharp
// IgSubsystem.cs
internal IgApplication App => _app ?? throw new InvalidOperationException("Not initialized");

// SimHostSubsystem.cs
internal EntityRepository World => _world ?? throw new InvalidOperationException("Not initialized");
internal ModuleHostKernel Kernel => _kernel ?? throw new InvalidOperationException("Not initialized");

// IosSubsystem.cs  — expose the IOS logic for test assertions
internal IosLogic Logic => _mock?.Logic ?? throw new InvalidOperationException("Not initialized");

// IgApplication.cs  — map click injection
internal void TestHook_SimulateMapClick(System.Numerics.Vector2 worldPos) =>
    OnCanvasClicked(worldPos, Raylib_cs.MouseButton.Left, false, false, Fdp.Kernel.Entity.Null);
```

**Success Conditions:**

1. All four `internal` members compile and are accessible from
   `Hrot.ClusterRunner.Integration.Tests` (same assembly visibility via `[InternalsVisibleTo]` in the
   respective project files, or `InternalsVisibleTo` attribute in `AssemblyInfo`).

---

### DDS2ECS-S15T2 — Create `HrotRunnerHarness`

**File (new):** `Hrot.ClusterRunner.Integration.Tests/HrotRunnerHarness.cs`

**Specification:**
- Static `Interlocked.Increment` counter assigns unique `DomainId` per instance (start at 100).
- Constructor: build `RunnerConfiguration { Headless=true, DomainId=..., ModeString="all" }`,
  create `SimHostSubsystem`, `IgSubsystem`, `IosSubsystem`, pass to `SubsystemOrchestrator`,
  call `Initialize()`, pump 10 frames (DDS discovery settle).
- `bool PumpUntil(Func<bool> condition, int timeoutFrames = 300)`:
  loop up to `timeoutFrames`, calling `Orchestrator.RunFrames(1)` + `Thread.Sleep(5)` each
  iteration; return `true` if condition satisfied, `false` on timeout.
- Implements `IDisposable`: calls `Orchestrator.Shutdown()`.

**Success Conditions:**

1. **Unit test** `HrotRunnerHarnessTests.Constructor_InitializesWithoutException`:  
   `new HrotRunnerHarness()` completes without throwing.

2. **Unit test** `HrotRunnerHarnessTests.PumpUntil_ConditionMet_ReturnsTrue`:  
   Condition always true → returns `true` immediately.

3. **Unit test** `HrotRunnerHarnessTests.PumpUntil_ConditionNeverMet_ReturnsFalse`:  
   Condition always false → returns `false` after `timeoutFrames`.

---

### DDS2ECS-S15T3 — Map Placement Integration Test

**File (new):** `Hrot.ClusterRunner.Integration.Tests/MapPlacementIntegrationTests.cs`

**Test:** `EndToEnd_PlacementFlow_SpawnsAndDistributesEntity`

Flow:
1. IOS activates placement tool for a known `TkbType`.
2. Pump 10 frames so `MapInteractionConfig` reaches IG.
3. IG simulates a map click at a fixed world position.
4. `PumpUntil` SimHost `World.EntityCount > 0` (max 100 frames).
5. Assert SimHost entity has correct `TkbType` (via `NetworkSpawnRequest` — NOT `EntityMaster`).
6. `PumpUntil` IG world has a `NetworkIdentity` + `ResolvedStyle` entity (max 100 frames).
7. `PumpUntil` IOS `DerRepo` contains an entity with matching `TkbType` (max 60 frames).

**Success Conditions:**

1. All three `PumpUntil` calls return `true` within timeout.
2. TkbType on SimHost entity matches the type activated in step 1.
3. IOS `DerRepo` entity count increased by exactly 1.

---

### DDS2ECS-S15T4 — Context Menu Push Integration Test

**File (new):** `Hrot.ClusterRunner.Integration.Tests/ContextMenuIntegrationTests.cs`

**Test:** `ContextMenu_SelectionEvent_PushesMenuToIG`

Flow:
1. Inject a `SelectionChangedEvent` DDS sample for a known `EntityId`.
2. `PumpUntil` IG `ContextMenuState` for that entity has `Actions.Count > 0` (max 100 frames).
3. Assert at least one action has `Label == "Properties..."` (Standard strategy).

**Success Conditions:**

1. `PumpUntil` returns `true`.
2. Menu contains expected Standard-strategy items.

---

### DDS2ECS-S15T5 — Entity Destroy Integration Test

**File (new):** `Hrot.ClusterRunner.Integration.Tests/EntityDestroyIntegrationTests.cs`

**Test:** `SimHost_DestroyEntity_IgGhostIsRemoved`

Flow:
1. Spawn an entity via the placement flow (reuse harness helper from S15T3).
2. `PumpUntil` IG has received the ghost (max 100 frames).
3. Destroy the entity in SimHost (directly call `EntityLifecycleModule.Destroy` or equivalent).
4. `PumpUntil` IG world no longer contains an entity with that `NetworkId` (max 100 frames).

**Success Conditions:**

1. Ghost is confirmed present after spawn.
2. Ghost is confirmed absent after destroy within 100 frames.

---

### DDS2ECS-S15T6 — Mission Control Integration Test

**File (new):** `Hrot.ClusterRunner.Integration.Tests/MissionControlIntegrationTests.cs`

**Test:** `IOS_SendsJumpCommand_SimHostAppliesIt`

Flow:
1. Spawn an entity with a multi-task mission plan (3 tasks).
2. `PumpUntil` SimHost's `EntityMissionHolder.ActiveTaskId == tasks[0].Id` (max 50 frames).
3. IOS sends `CMD_JUMP_TO_TASK` with `TargetTaskId = tasks[2].Id`.
4. `PumpUntil` SimHost `EntityMissionHolder.ActiveTaskId == tasks[2].Id` (max 100 frames).
5. `PumpUntil` IOS `RequestTransactionManager` has completed the request (ack received) (max 60 frames).

**Success Conditions:**

1. All `PumpUntil` calls return `true`.
2. SimHost `ActiveTaskId` changed from `tasks[0]` to `tasks[2]`.
3. IOS transaction completes with `Success = true`.

---

## Phase 16: SimHost Mission Pipeline (UrbanCombat Alignment)

*Source of truth: `FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs`
(`RegisterBehaviors()`, `RegisterSystems()`). See DESIGN.md §10 for full deviation analysis.*

---

### DDS2ECS-S16T1 — Delete EntityMissionHolder

**Files:**
- Delete: `Hrot.SimHost/Components/EntityMissionHolder.cs`
- Edit: `Hrot.SimHost/SimHostApp.cs`

**Changes:**
1. Delete `EntityMissionHolder.cs` entirely.
2. In `SimHostApp.cs`, find `world.RegisterManagedComponent<EntityMissionHolder>()` and replace
   with `world.RegisterComponent<MissionPlanQueue>()`.
3. Add using: `using FDP.Toolkit.Behavior.Components;` if not already present.
4. In `Hrot.SimHost.Tests/EntityMissionTranslatorTests.cs`, replace
   `world.RegisterManagedComponent<EntityMissionHolder>()` with
   `world.RegisterComponent<MissionPlanQueue>()`.

**Note:** `GlobalComponentIds.EntityMissionHolder = 162` is defined in
`FDP/Kernel/Fdp.Kernel/GlobalComponentIds.cs` (Hrot-reserved range). Do **not** delete that
entry; simply stop using it. Add an `[Obsolete]` comment on that line.

**Success Conditions:**
1. `dotnet build Hrot.SimHost/Hrot.SimHost.csproj` GREEN.
2. No remaining references to `EntityMissionHolder` in `Hrot.SimHost/`.
3. `world.HasComponent<MissionPlanQueue>` compiles without error.

---

### DDS2ECS-S16T2 — Rewrite EntityMissionTranslator to Write MissionPlanQueue

**File:** `Hrot.SimHost/Translators/EntityMissionTranslator.cs`
**Tests:** `Hrot.SimHost.Tests/EntityMissionTranslatorTests.cs`

**Constructor change:** add `BehaviorRegistry _behaviorRegistry` parameter (matching the pattern
used by `MissionAdapterSystem`; the registry is already available in `SimHostApp`).

**`PollIngress` / `ApplyToEntity` logic:**
1. Iterate `ddsMission.Plan.Tasks` — truncate silently at 8 (log warning if count > 8).
2. For each task, call `_behaviorRegistry.TryGetId(task.BehaviorId, out int behaviorId)`.
   If resolution fails, log a warning and use `behaviorId = 0` (Idle).
3. Map the first entry in `task.Triggers` list to `MissionTrigger` enum via the lookup table
   below. Unknown strings → `MissionTrigger.Immediate`.
4. Build `MissionPhase { BehaviorId = behaviorId, Trigger = trigger, TriggerParam = param }`.
5. Write `MissionPlanQueue { PhaseCount = n, CurrentPhase = 0 }` with `cmd.SetComponent(...)`.
   (Unmanaged — `SetComponent`, not `SetManagedComponent`.)
6. On `NOT_ALIVE_DISPOSED`: `cmd.RemoveComponent<MissionPlanQueue>(entity)`.

**Trigger lookup table:**
```
"TimerElapsed"        → MissionTrigger.TimerElapsed
"ReachedDestination"  → MissionTrigger.ReachedDestination
"HealthCritical"      → MissionTrigger.HealthCritical
"" / null / unknown   → MissionTrigger.Immediate
```

**Test rewrites required in `EntityMissionTranslatorTests.cs`:**
- `Ingress_ApplyToEntity_SetsMissionPlanQueue` — verify `PhaseCount`, `CurrentPhase == 0`, first
  `MissionPhase.BehaviorId` matches the resolved behavior, no `EntityMissionHolder` present.
- `Ingress_ComponentRemoval_ClearsMissionPlanQueue` — verify `MissionPlanQueue` is removed on
  `NOT_ALIVE_DISPOSED`.

**Success Conditions:**
1. All rewritten `EntityMissionTranslatorTests` pass.
2. `EntityMissionHolder` not referenced anywhere in this translator.
3. `MissionPlanQueue.PhaseCount` equals the number of tasks in the source `EntityMission`.

---

### DDS2ECS-S16T3 — Delete MissionAdapterSystem, Register MissionDirectorSystem

**Files:**
- Delete: `Hrot.SimHost/Systems/MissionAdapterSystem.cs`
- Edit: `Hrot.SimHost/Modules/SimulationLogicModule.cs`

**Changes in `SimulationLogicModule.RegisterSystems()`:**

Remove:
```csharp
// ── 1. MissionAdapterSystem ──────────────────────────────────────────
// Stub — full implementation in TASK-S4.3.
group.AddSystem(new MissionAdapterSystem(_behaviorRegistry, _entityMap));
```

Replace with:
```csharp
// ── 1. MissionDirectorSystem ─────────────────────────────────────────
// Evaluates MissionTrigger on MissionPlanQueue phases and advances
// CurrentPhase when a trigger condition is satisfied.
group.AddSystem(new MissionDirectorSystem());
```

Add using: `using FDP.Toolkit.Behavior.Systems;`

If `_entityMap` is now unused by any remaining system in this module, remove it from the
constructor parameter list and all associated plumbing.

**Success Conditions:**
1. `MissionAdapterSystem.cs` does not exist.
2. `dotnet build Hrot.SimHost/Hrot.SimHost.csproj` GREEN.
3. `SpawningModuleIntegrationTests` still pass (they use `SimulationLogicModule`).

---

### DDS2ECS-S16T4 — Compile Real BTree Interpreters for All Behaviors

**Files:**
- Edit: `Hrot.SimHost/SimHostApp.cs` (in `RegisterBehaviors()`)
- Create: `Hrot.SimHost/Brains/SimHostNodes.cs`

**Pattern** — mirror `UrbanCombat/HeadlessDemoApp.cs RegisterBehaviors()`:
```csharp
// MoveTo_BT
private const string MoveToJson = """
    {
        "TreeName": "MoveTo_BT",
        "Root": { "Type": "Action", "Action": "WriteMoveToChannel" }
    }
    """;

// In RegisterBehaviors():
var moveToReg = new ActionRegistry<BrainBlackboard, BTreeContext>();
moveToReg.Register("WriteMoveToChannel", SimHostNodes.Action_WriteMoveToChannel);
var moveToBlob = TreeCompiler.CompileFromJson(MoveToJson);
behaviorRegistry.Register(SimHostBehaviorIds.MoveTo_BT, "MoveToLocation",
    new BehaviorDefinition {
        Name             = "MoveToLocation",
        BrainTier        = BehaviorConstants.BrainTierBTree,
        BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(moveToBlob, moveToReg),
    });
```

Repeat for `FollowRoute_BT` (`Action_WriteFollowRouteChannel`) and `JoinFormation_BT`
(`Action_WriteJoinFormationChannel`).

**`Hrot.SimHost/Brains/SimHostNodes.cs`** — create following the pattern of
`UrbanCombat/Brains/InsurgentNodes.cs`; each action method reads params from
`BrainBlackboard.Memory` and writes the appropriate locomotion channel.

**Usings to add in `SimHostApp.cs`:**
```csharp
using Fbt.Runtime;
using Fbt.Serialization;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Executors;
```

**Success Conditions:**
1. `behaviorRegistry.TryGetDefinition(SimHostBehaviorIds.MoveTo_BT, out var def)` → `def.BTreeInterpreter != null`.
2. A single-frame simulation tick with an entity carrying `BehaviorState.ActiveBehaviorHash == MoveTo_BT` does not throw or produce a null-ref.
3. `dotnet build` GREEN.

---

### DDS2ECS-S16T5 — Wire ParseParams Delegates for Param-Carrying Behaviors

**Files:**
- Edit: `Hrot.SimHost/SimHostApp.cs` (add `ParseParams` to each `BehaviorDefinition` that carries params)
- Edit: `Hrot.SimHost/Brains/SimHostNodes.cs` (add param struct definitions)

**Goal:** When `EntityMissionTranslator` or `MissionDirectorSystem` activates a phase, the
`BehaviorDefinition.ParseParams` delegate is called with `(task.BehaviorParams, ptr)` to write
target coordinates into `BrainBlackboard.Memory` so `Action_WriteMoveToChannel` can read them.

**Param structs** (add to `SimHostNodes.cs`):
```csharp
[StructLayout(LayoutKind.Sequential)]
public struct MoveToLocationParams
{
    public float X;
    public float Y;
    public float Speed;
    public float ArrivalRadius;
}

[StructLayout(LayoutKind.Sequential)]
public struct FollowRouteParams
{
    // Waypoints serialised as flat X0,Y0,X1,Y1,... pairs (max 16 waypoints = 32 floats).
    public float Speed;
    public bool  Loop;
}
```

**`ParseParams` delegate pattern:**
```csharp
unsafe ParseParams = static (json, ptr) =>
{
    var p = JsonSerializer.Deserialize<MoveToLocationParams>(json);
    Unsafe.Write(ptr, p);
}
```

Wire into the `MoveTo_BT` and `FollowRoute_BT` `BehaviorDefinition` objects created in S16T4.

**Success Conditions:**
1. `SimHostNodes_ParseParams_WritesCorrectBytesToBlackboard` unit test: construct
   `BehaviorDefinition` for `MoveTo_BT`, call `def.ParseParams(json, ptr)`, read
   `MoveToLocationParams` back and assert `X`, `Y`, `Speed`, `ArrivalRadius` match.
2. No managed heap allocations inside the `ParseParams` delegate (use `Unsafe.Write`).
3. `dotnet test Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj` GREEN.

---

## Phase 17: SimHost Combat Readiness (UrbanCombat Alignment)

*Source of truth:*
- *`FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs` — `RegisterComponents()`, `RegisterSystems()`*
- *`FDP/Examples/Fdp.Examples.UrbanCombat/Setup/DemoTkbSetup.cs` — TKB template component patterns*

*See DESIGN.md §11 for the five-deviation analysis. All five tasks should be applied together —
applying a subset leaves the combat pipeline in an inconsistent state.*

---

### DDS2ECS-S17T1 — Add Perception and Combat Project References

**File:** `Hrot.SimHost/Hrot.SimHost.csproj`

Add three `<ProjectReference>` entries inside the existing `<ItemGroup>` that already contains
`FDP.Toolkit.Physics` (line 39):

```xml
<ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Perception\FDP.Toolkit.Perception.csproj" />
<ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Combat\FDP.Toolkit.Combat.csproj" />
<ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Combat.Contracts\FDP.Toolkit.Combat.Contracts.csproj" />
```

**Success Conditions:**
1. `dotnet build Hrot.SimHost/Hrot.SimHost.csproj` GREEN with the new references present.
2. `using FDP.Toolkit.Perception.Components;` and `using FDP.Toolkit.Combat.Components;` resolve
   in `SimHostApp.cs` without error.

---

### DDS2ECS-S17T2 — Register Perception, Combat, Physics, and HSM Components

**File:** `Hrot.SimHost/SimHostApp.cs` (inside `RegisterSimComponents()`)

After the existing `world.RegisterComponent<BrainBlackboard>();` line, add:

```csharp
// HSM brain tiers (for APC-style HSM behaviors)
world.RegisterComponent<FDP.Toolkit.Behavior.Components.BrainHsm64>();
world.RegisterComponent<FDP.Toolkit.Behavior.Components.BrainHsm128>();
world.RegisterComponent<FDP.Toolkit.Behavior.Components.PreviousCapabilities>();
world.RegisterComponent<FDP.Toolkit.Behavior.Components.PassengerBuffer>();
world.RegisterComponent<FDP.Toolkit.Behavior.Components.IsEmbarkedTag>();

// Perception
world.RegisterComponent<FDP.Toolkit.Perception.Components.Faction>();
world.RegisterComponent<FDP.Toolkit.Perception.Components.PerceptionReceptor>();
world.RegisterComponent<FDP.Toolkit.Perception.Components.TargetMemory>();

// Combat & Physics
world.RegisterComponent<FDP.Toolkit.Physics.Components.PhysicsCollider>();
world.RegisterComponent<FDP.Toolkit.Combat.Components.WeaponState>();
world.RegisterComponent<FDP.Toolkit.Combat.Components.Health>();
world.RegisterComponent<FDP.Toolkit.Combat.Components.BallisticProjectile>();
world.RegisterComponent<Fdp.Kernel.HealthData>();
```

**Success Conditions:**
1. `dotnet build` GREEN.
2. Unit test `SimHostComponents_AllCombatAndPerceptionComponentsRegistered`: create
   `EntityRepository`, call `RegisterSimComponents()`, assert `IsComponentRegistered<T>()` for
   `PerceptionReceptor`, `WeaponState`, `Health`, `Faction`, `PhysicsCollider`.

---

### DDS2ECS-S17T3 — Initialize PhysicsToolkitModule in SimHostApp.OnLoad()

**File:** `Hrot.SimHost/SimHostApp.cs` (inside `OnLoad()`)

Add before `_kernel.Initialize()` (line 213):

```csharp
// Allocates RaycastBatchData singleton required by RaycastSolverSystem.
var physicsModule = new FDP.Toolkit.Physics.PhysicsToolkitModule();
physicsModule.Initialize(_world);
```

In `OnUnload()` (or equivalent cleanup), mirror `HeadlessDemoApp.Dispose()`:

```csharp
if (_world.HasSingleton<FDP.Toolkit.Physics.Components.RaycastBatchData>())
{
    ref var batch = ref _world.GetSingleton<FDP.Toolkit.Physics.Components.RaycastBatchData>();
    if (batch.Requests.IsCreated) batch.Requests.Dispose();
    if (batch.Hits.IsCreated)     batch.Hits.Dispose();
}
```

**Success Conditions:**
1. `dotnet build` GREEN.
2. Unit test `PhysicsModule_Initialize_CreatesBatchDataSingleton`: assert
   `world.HasSingleton<RaycastBatchData>()` after `physicsModule.Initialize(world)`.
3. No `NativeArray` leak warning on shutdown.

---

### DDS2ECS-S17T4 — Expand SimulationLogicModule with Combat Systems

**File:** `Hrot.SimHost/Modules/SimulationLogicModule.cs`

Split `RegisterSystems(SystemGroup group)` into three group parameters (or create
`InputSystemGroup`, `SimulationSystemGroup`, `PostSimulationSystemGroup` internally, mirroring
`HeadlessDemoApp`). Update the call sites in `SimHostApp.cs` and `SimHostModule.cs` to run all
three groups per frame in order.

**Input phase additions:**
```csharp
inputGroup.AddSystem(new FireProcessingSystem());
inputGroup.AddSystem(new RaycastSolverSystem());
inputGroup.AddSystem(new HitResolutionSystem());
```

**Sim phase additions** (insert after `BTreeTickSystem`):
```csharp
var weaponSys = new WeaponDispatcherSystem();
weaponSys.RegisterExecutor(CombatConstants.ActionIdAimAndFire, new AimAndFireExecutor());
simGroup.AddSystem(weaponSys);

simGroup.AddSystem(new VisionBroadphaseSystem());
simGroup.AddSystem(new LosRequestBatchingSystem());
simGroup.AddSystem(new ThreatEvaluationSystem());
simGroup.AddSystem(new DamageSystem());
simGroup.AddSystem(new HsmDamageBridgeSystem());
simGroup.AddSystem(new HsmTickSystem<BrainHsm128>(_behaviorRegistry));
```

**PostSim phase:**
```csharp
postSimGroup.AddSystem(new BallisticsSystem());
```

Usings to add:
```csharp
using FDP.Toolkit.Combat.Systems;
using FDP.Toolkit.Combat.Executors;
using FDP.Toolkit.Perception.Systems;
using FDP.Toolkit.Physics.Systems;
```

**Success Conditions:**
1. `dotnet build Hrot.SimHost/Hrot.SimHost.csproj` GREEN.
2. `SpawningModuleIntegrationTests` still pass.
3. 10-frame pump with a `PerceptionReceptor` + `WeaponState` entity produces no exception.

---

### DDS2ECS-S17T5 — Rewrite BdcTkbBuilder.WithCombat() to Attach Real ECS Components

**File:** `Hrot.Map.Definitions/Tkb/BdcTkbBuilder.cs`

Rewrite `WithCombat()` to call `template.AddComponent()` for each real FDP ECS struct derived
from the `SimCombatDef` fields — while retaining the managed component for IG display:

```csharp
public BdcTkbBuilder WithCombat(long tkbId, Action<SimCombatDef> configure)
{
    var template = _db.GetByType(tkbId)
        ?? throw new InvalidOperationException($"Template {tkbId} not found");

    // Keep managed definition for IG inspector / ORBAT display.
    template.AddManagedComponent(() => {
        var def = new SimCombatDef();
        configure(def);
        return def;
    });

    // Translate to real FDP ECS unmanaged components.
    var combatDef = new SimCombatDef();
    configure(combatDef);

    if (combatDef.SensorRange > 0f)
    {
        template.AddComponent(new PerceptionReceptor {
            VisionRange    = combatDef.SensorRange,
            HearingRange   = combatDef.SensorRange * 0.5f,
            FieldOfViewCos = 0f  // 360° — override per entity if needed
        });
        template.AddComponent(new TargetMemory());
    }

    if (combatDef.Weapons.Count > 0)
    {
        var primary = combatDef.Weapons[0];
        template.AddComponent(new WeaponState {
            Ammo                   = primary.Ammunition,
            MuzzleVelocity         = primary.Range > 0f ? primary.Range : 800f,
            CooldownTicksRemaining = 0
        });
    }

    float maxHp = combatDef.ArmorFront > 400f ? 300f
                : combatDef.ArmorFront > 100f ? 150f
                : 100f;
    template.AddComponent(new Health { Current = maxHp, Max = maxHp });
    template.AddComponent(new HealthData { Current = maxHp, Max = maxHp });
    template.AddComponent(new PhysicsCollider {
        Radius         = 2.5f,
        CollisionLayer = PhysicsConstants.EntityCollisionLayer
    });

    return this;
}
```

Add a new `WithFaction(long tkbId, byte factionId)` fluent method and call it per entity type
in `BdcTkbCatalog.cs` (Blue = 1 for friendly, Red = 2 for threat — matching
`UrbanCombatConstants.FactionBlue/FactionRed`).

Required usings in `BdcTkbBuilder.cs`:
```csharp
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Physics;
using Fdp.Kernel;
```

**Success Conditions:**
1. `BdcTkbBuilder_WithCombat_AttachesWeaponState`: spawn M1 Abrams entity, assert
   `world.HasComponent<WeaponState>(entity)` and `Ammo == 42`.
2. `BdcTkbBuilder_WithCombat_AttachesPerceptionReceptor`: assert
   `world.HasComponent<PerceptionReceptor>(entity)` and `VisionRange == 8000f`.
3. `BdcTkbBuilder_WithCombat_AttachesHealth`: assert `world.HasComponent<Health>(entity)`.
4. `SimCombatDef` managed component still accessible on the template (IG display not broken).
5. `dotnet test Hrot.SimHost.Tests/ Hrot.Map.Common.Tests/` GREEN.
