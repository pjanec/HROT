Pls this as an addendum, a continuation  to the `tkb-design-ideas.md` which you need to read first.


The `tkb-design-ideas.md` document captures the core principles of the Transient Knowledge Base accurately, but there are several architectural flaws and missing integration constraints that violate high-performance and clean architecture mandates. 

Here are the specific gaps and flaws that must be corrected.

**1. Architectural Flaw: String Allocation on the Parsing Hot Path**
In Section 8.2, the document permits a "single substring split" when stripping the `#PartId` delimiter during JSON ingestion. This is an unacceptable memory leak in a high-performance ingestion pipeline. You must mandate zero allocations. 
Instead of splitting the string, the deserializer must use `ReadOnlySpan<char>` to locate `IndexOf('#')`. It can extract the integer span and parse the `PartId`, then use .NET's dictionary `AlternateLookup` (available for `ReadOnlySpan<char>`) to query the `TkbDescriptorRegistry` without allocating a single string on the Large Object Heap.

**2. Implementation Gap: ECS Singleton Registration**
Section 11.6 correctly mandates manual constructor injection and bypassing DI containers. However, it completely omits the requirement to register the `ITkbDatabase` instance into the ECS world itself. 
For the `GhostPromotionSystem` to evaluate readiness and apply blueprints asynchronously, `ITkbDatabase` must be explicitly registered as an ECS singleton during the application's domain component registration phase (`RegisterDomainComponents`). If you omit this, the promotion system has no way to resolve the 64-bit `TkbType` back to a `TkbTemplate` at runtime.

**3. Architectural Flaw: Leaky VFS Concurrency**
Section 5.4 correctly identifies the severe locking contention introduced by `ZipArchiveMode.Update`. However, the document states: *"The editor implementation must hold an exclusive lock on the ZIP file during save operations."*
This is a leaky abstraction. The consumer (the Editor shell or the `ProjectSaver`) should not know the concurrency physics of the underlying VFS medium. The `ITkbStorageStrategy` interface itself must internally enforce mutual exclusion, encapsulating the lock within the `ZipTkbProvider` so the application tier remains unaware of the transport tier's I/O constraints.

**4. Implementation Gap: Safe ECS Projection Guards**
Section 10.5 claims that if a target `EntityRepository` does not have a component type registered, the `AddComponent<T>` call "silently no-ops". 
Relying on silent failures inside core ECS memory allocators is an anti-pattern that hides catastrophic schema mismatches. The domain translators mapping TKB DTOs to ECS chunks must explicitly evaluate `IsComponentTypeRegistered<T>()` first. The local engine must intentionally skip the allocation for unrecognized components, ensuring the projection is safely bypassed rather than swallowed by a lower-level ECS failure.

**5. Implementation Gap: Scenario Header Contract**
Section 11.3 requires the orchestrator to extract the `TkbName` from the scenario files. To prevent the orchestrator from suffering severe memory fragmentation by fully parsing massive scenario DOMs just to read one string, the document must explicitly state that the `ScenarioHeaderDto` (the lightweight envelope) must be expanded to include the `TkbName` property. The orchestrator must peek this property using a forward-only `Utf8JsonReader` before routing the TKB differential push.

**6. Implementation Gap: The Ghost Shell Allocator**
Section 10.3 mentions that `GhostStateTracker` is stamped at ghost creation to track timeout frames. It fails to define *who* creates this shell. The design must clarify that the network ingress translators (e.g., `EntityMasterIngressTranslator`) or the local `NetworkSpawningSystem` are responsible for allocating the raw ECS entity shell and stamping it with the `TkbIdentity` and `GhostStateTracker` components, effectively handing the entity over to the `GhostPromotionSystem` for downstream evaluation.


----

I realized the prefetching of the TKB file is a nonsense. Is shoudl be made part of a wider config/data file synchronization solution. So I no longer need the prefetch as part of scenario loading. It was a mistake. I need just the unified loader (zip/realFS) and integration into the HROT/FDP

----


Removing the orchestrator prefetch is the correct architectural decision. Tightly coupling the orchestrator's state machine to the distribution of specific domain assets is an anti-pattern. Asset synchronization belongs in a dedicated, out-of-band delivery pipeline.

With the transport responsibility removed, the integration of the Transient Knowledge Base (TKB) into HROT and FDP simplifies to local Virtual File System (VFS) ingestion and strict cluster handler ordering.

**1. Node-Side Ingestion and State Management**
The simulation node assumes the required TKB artifact (either a ZIP file or a raw directory) is already present in its local staging area. The node must manage its own in-memory caching to prevent reloading the TKB across scenario restarts.

You must implement `TkbLoadClusterStateHandler` to intercept the `PrepareLive` and `PrepareEdit` operations. This handler evaluates the active TKB requirement, checks the local cache, and invokes the `TkbUnifiedLoader` to stream the data into memory.

```csharp
public sealed class TkbLoadClusterStateHandler : IClusterStateHandler
{
    private readonly ITkbDatabase _tkbDb;
    private readonly string _localTkbStagingRoot;

    private string? _lastLoadedTkbName;
    private DateTime _lastLoadedTimestamp;

    public TkbLoadClusterStateHandler(ITkbDatabase tkbDb, string localStagingRoot)
    {
        _tkbDb = tkbDb;
        _localTkbStagingRoot = Path.Combine(localStagingRoot, "TKB");
    }

    public bool CanHandle(NodeOpType operation) =>
        operation == NodeOpType.PrepareLive || operation == NodeOpType.PrepareEdit;

    public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
    {
        // Resolve requested TKB from scenario envelope or fallback to cluster default
        string requestedTkb = ExtractTkbNameFromIntent(intent) ?? GetFallbackTkbName();
        
        // The VFS abstraction allows this path to point to a ZIP or a Raw Folder
        string localPath = Path.Combine(_localTkbStagingRoot, $"{requestedTkb}.zip");

        // Evaluate differential cache
        DateTime currentFileTime = File.GetLastWriteTimeUtc(localPath);
        if (_lastLoadedTkbName == requestedTkb && _lastLoadedTimestamp == currentFileTime)
        {
            return Task.FromResult<object?>(null); // Cache hit, preserve memory
        }

        // VFS Ingestion via Unified Loader
        _tkbDb.Clear();
        using var loader = new TkbUnifiedLoader(localPath); // Abstracts ZIP vs Raw
        var deserializer = new TkbDeserializer();

        foreach (var entityFile in loader.EnumerateEntityFiles())
        {
            _tkbDb.Register(deserializer.ParseEntity(entityFile));
        }

        _lastLoadedTkbName = requestedTkb;
        _lastLoadedTimestamp = currentFileTime;

        return Task.FromResult<object?>(null);
    }

    public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) { }
    public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) { }
}
```

**2. HROT / FDP Startup Wiring**
FDP and HROT enforce explicit constructor injection, bypassing DI containers. The TKB database and the loader handler must be explicitly wired during the node bootstrap sequence.

First, instantiate the `ITkbDatabase` in `HrotNodeBuilder` (or your equivalent composition root) so it is available to the entire system.

```csharp
// Inside HrotNodeBuilder.Build()
var tkbDb = HrotEnvironment.CreateTkb(); // Create the global database instance
```

Second, register the `TkbLoadClusterStateHandler` on the `ClusterSlave`. **Execution order is critical.** The TKB handler must be registered *before* any scenario loaders (like `HrotScenarioLoadHandler` or `HrotEditLoadHandler`). This guarantees that by the time the scenario parser extracts entity creation requests, the `ITkbDatabase` is already fully populated with the necessary `TkbTemplate` blueprints.

```csharp
// Inside NodeBootstrapper.BuildOrchestration()

// 1. Register TKB loader FIRST.
var tkbLoader = new TkbLoadClusterStateHandler(context.TkbDb, localTempRoot);
clusterSlave.RegisterHandler(tkbLoader);

// 2. Register Scenario Loaders SECOND.
clusterSlave.RegisterHandler(
    new HrotScenarioLoadHandler(scenarioSerializer, scenarioLoader, zoneService,
    scenarioExtractor, scenarioSource, scenarioIdAllocator,
    world: world, controller: controller, storageDirectory: localTempRoot));
```

By removing the orchestrator prefetch, you decouple the cluster state machine from asset synchronization physics. The orchestrator merely dictates the phase (`PrepareLive` or `PrepareEdit`), and the node-local handler asserts that the correct TKB is loaded into memory via the VFS before scenario materialization begins.



Implementing the Transient Knowledge Base (TKB) file loading and integrating it into the FDP/HROT v195 architecture requires a strict sequence of decoupled phases. 

**1. Domain Attributes and Pure DTOs**
Define the `[TkbDescriptor]` attribute using a `HierarchicalName` to explicitly mark pure C# POCOs. You must convert existing TKB definitions into engine-agnostic Data Transfer Objects completely devoid of ECS logic or transport-layer coupling like MessagePack markers.

**2. Virtual File System (VFS) and Transport Tier**
Implement the `TkbEntityFile` record struct to yield disposable, memory-bounded streams. Build the `ITkbStorageStrategy` interface backed by `RawDirectoryTkbProvider` for raw file hierarchies and `ZipTkbProvider` for LZ4-compressed archives. Create the `TkbUnifiedLoader` factory to abstract the physical storage medium, ensuring consumers remain completely ignorant of the underlying I/O.

**3. In-Memory Registry Refactoring**
Refactor `TkbTemplate` to act solely as a pure data container mapping composite `(Type, PartId)` keys to boxed descriptors, discarding all legacy ECS applicator delegates. Implement the `ITkbDatabase` singleton to serve as the cluster-wide registry, providing O(1) lock-free retrieval mechanisms.

**4. Streaming Deserialization Pipeline**
Develop the `TkbDeserializer` to process single-file streams yielded by the VFS. It must allocate exactly one `System.Text.Json.JsonDocument` per entity file and dispose of it immediately after extracting properties to strictly bound memory overhead and prevent Large Object Heap (LOH) fragmentation.

**5. Zero-Reflection Source Generation**
Create a Roslyn source generator (`Tkb.SourceGen`) that scans assemblies for the `[TkbDescriptor]` attribute at compile time. The generator must emit a static dictionary in `TkbDescriptorRegistry` that maps hierarchical JSON keys to `TkbDescriptorParserThunk` delegates, guaranteeing an O(1) deserialization hot path.

**6. ECS Projection and Instantiation**
Update the `GhostPromotionSystem` to evaluate `MandatoryComponent` requirements via O(1) bitwise checks directly against the native `EntityHeader.ComponentMask`. Implement domain-specific applicators that project pure TKB DTOs into ECS memory chunks. These applicators must check `IsComponentTypeRegistered<T>()` and silently bypass unsupported components to maintain schema safety.

**7. Node-Side Orchestration and State Management**
Extend `ScenarioHeaderDto` and envelope parsing to extract the requested `TkbName`. Implement `TkbLoadClusterStateHandler` to intercept the `PrepareLive` and `PrepareEdit` phases. This handler is responsible for evaluating the active TKB requirement, checking the differential cache using file timestamps, and executing VFS ingestion only when the cache is invalid.

**8. Bootstrapper Wiring**
In `HrotNodeBuilder`, manually instantiate the global `ITkbDatabase` so it is available to the entire system. In `NodeBootstrapper`, inject the `TkbLoadClusterStateHandler` into the `ClusterSlave` strictly *before* the scenario loaders (like `HrotScenarioLoadHandler`). This execution order guarantees that the database is fully populated prior to scenario materialization.

----

For concrete implementation and integration, we need to define some concrete TKB descriptors. TkbMaster of course, and then some for example for vehicle parameters, for ammo ballistics, for weapon capabilities, pls suggest. With concrete sample TKB files

----

To implement the Transient Knowledge Base (TKB) pipeline, we must define pure C# Data Transfer Objects (DTOs) completely decoupled from the Entity Component System (ECS) or network transport serialization. These DTOs serve exclusively as the schema for the JSON DOM and the in-memory `TkbTemplate` registry. 

Below are the concrete definitions for the master descriptor, vehicle parameters, weapon capabilities, and ammo ballistics, followed by their physical JSON representations.

### 1. C# Domain Schema (Pure DTOs)

These structs rely entirely on semantic attributes. They must not inherit from any ECS base class or carry `[MessagePackObject]` attributes.

```csharp
using System.ComponentModel;
using Fdp.Toolkit.Tkb.Attributes; // Assumed namespace for TKB schema attributes

namespace Fdp.Toolkit.Tkb.Domain
{
    // The only descriptor without a domain prefix. Mandatory on all entities.
    [TkbDescriptor("TkbMaster")]
    public record TkbMasterDto
    {
        public string CustomName { get; init; } = string.Empty;
        
        [Description("SISO-REF-010-2015 DIS Entity Type (e.g. 1.1.225.1.1.1.0)")]
        public string DisType { get; init; } = string.Empty;
    }

    [TkbDescriptor("Gen.VehicleParameters")]
    public record VehicleParametersDto
    {
        [EditUnit("kg")]
        public float Mass { get; init; }
        
        [EditUnit("m")]
        public float Length { get; init; }
        
        [EditUnit("m")]
        public float Width { get; init; }
        
        [EditUnit("m/s")]
        public float MaxSpeedFwd { get; init; }
        
        [EditUnit("m/s")]
        public float MaxSpeedRev { get; init; }
        
        [EditUnit("m/s²")]
        public float MaxAccel { get; init; }
    }

    [TkbDescriptor("Gen.WeaponCapabilities")]
    public record WeaponCapabilitiesDto
    {
        [EditUnit("m")]
        public float EffectiveRange { get; init; }
        
        [EditUnit("rpm")]
        public float RateOfFire { get; init; }
        
        public int MagazineCapacity { get; init; }
    }

    // Demonstrates relation mapping and multi-instance readiness
    [TkbDescriptor("Gen.AmmoWeaponBallistics")]
    public record AmmoWeaponBallisticsDto
    {
        [WeaponRef]
        [Description("The Weapon TKB GUID this ballistic profile applies to. 0 = Generic.")]
        public ulong WeaponGuid { get; init; }
        
        [EditUnit("m/s")]
        public float MuzzleSpeed { get; init; }
        
        [Description("Base damage applied on hit.")]
        public float Damage { get; init; }
    }
}
```

### 2. Concrete Sample TKB JSON Files

The file system enforces a strict 1:1 mapping between a JSON file and a TKB entity. The file path structure is purely for user categorization and does not dictate schema.

#### File: `Sample/Platform/Vehicle/Military/MBT/M1_Abrams.json`
This defines a physical vehicle platform. It maps the standard `TkbMaster` and the `Gen.VehicleParameters`.

```json
{
  "$guid": 100,
  "TkbMaster": {
    "CustomName": "M1 Abrams",
    "DisType": "1.1.225.1.1.1.0"
  },
  "Gen.VehicleParameters": {
    "Mass": 61000.0,
    "Length": 7.93,
    "Width": 3.66,
    "MaxSpeedFwd": 20.0,
    "MaxSpeedRev": 12.0,
    "MaxAccel": 2.5
  },
  "_EditorMetadata": {
    "LastModifiedBy": "AuthoringTool",
    "LastModified": "2026-05-15T16:00:00Z"
  }
}
```
*Note: The `_EditorMetadata` block demonstrates schemaless extensibility. Because the key does not start with a letter and lacks a registered DTO, the streaming parser skips it with zero allocations at runtime.*

#### File: `Sample/Weapon/Cannon/120mm_M256.json`
This is a **logical entity**. It does not spawn physically in the world but is referenced by the platform's weapon suite.

```json
{
  "$guid": 2001,
  "TkbMaster": {
    "CustomName": "120mm M256 Smoothbore",
    "DisType": "2.2.225.2.1.0.0"
  },
  "Gen.WeaponCapabilities": {
    "EffectiveRange": 3000.0,
    "RateOfFire": 6.0,
    "MagazineCapacity": 42
  }
}
```

#### File: `Sample/Ammo/Tank/120mm_APFSDS.json`
This entity relies on **multi-instance descriptors** to define different ballistic properties depending on which weapon fires the ammunition. The `#1` and `#2` postfixes route to the exact same `AmmoWeaponBallisticsDto` schema during deserialization.

```json
{
  "$guid": 3001,
  "TkbMaster": {
    "CustomName": "120mm APFSDS",
    "DisType": "2.2.225.2.1.1.0"
  },
  "Gen.AmmoWeaponBallistics#1": {
    "WeaponGuid": 2001,
    "MuzzleSpeed": 1500.0,
    "Damage": 600.0
  },
  "Gen.AmmoWeaponBallistics#2": {
    "WeaponGuid": 2005, 
    "MuzzleSpeed": 1450.0,
    "Damage": 550.0
  }
}
```

### 3. Integration Mechanics

When `TkbUnifiedLoader` ingests these files, the source-generated `TkbDescriptorRegistry` provides O(1) routing from the string `"Gen.VehicleParameters"` directly to a pre-compiled deserialization thunk. The resulting DTOs are boxed into the `TkbTemplate` instance.

During entity creation (e.g., when the network pipeline spawns an M1 Abrams), a domain-specific translator (like a `SimHost` kinematics translator) queries `template.GetDescriptor<VehicleParametersDto>()`. It extracts the pure POCO data and maps it into the strict ECS chunk layouts required by `CarKinematicsSystem` (e.g., the `VehicleParams` ECS component).





To translate a pure TKB descriptor into an Entity Component System (ECS) chunk layout at spawn time, we rely on domain-specific translators. This enforces a clean architecture by keeping the declarative TKB data completely decoupled from high-performance runtime execution. 

When the network pipeline or logic layer spawns the tank entity, the translation executes through the following strict pipeline:

1. **DTO Retrieval:** The translator queries the in-memory `TkbTemplate` for the target descriptor POCO, such as calling `GetDescriptor<VehicleParametersDto>()`. 
2. **Safety and Schema Guards:** Before attempting to map any data, the translator explicitly checks if the target ECS component is recognized by the local application using `EntityRepository.IsComponentTypeRegistered<T>()`. If an Image Generator node is processing the spawn, it will lack the physics registry and silently skip the allocation, preventing memory pollution.
3. **Data Projection:** The translator extracts the values from the engine-agnostic DTO (like `Mass` or `MaxSpeedFwd`) and packs them into the concrete, unmanaged ECS struct (e.g., `VehicleParams`). This bridges the gap between the flexible offline JSON schema and the rigid memory requirements of the engine.
4. **ECS Allocation:** The translator calls `EntityRepository.AddComponent<T>(entity, component)`. The repository copies the unmanaged struct directly into the contiguous `NativeChunkTable<T>`, ensuring cache-friendly O(1) memory access for the downstream simulation systems.

By enforcing this boundary, the `TkbTemplate` remains a pure data container, and the ECS repository never inherits transport or schema-parsing concerns.



----
can you givo concrete example for the Gen.VehicleParameters? pls note that the TKB descripto to ECS component translation must follow same M:N mechanics as for the  scenario content translation into ECS

----


To enforce the M:N translation mechanics established by the scenario serialization pipeline (`IEntityScenarioTranslator`), the TKB-to-ECS projection must use domain-specific translators that consume a subset of TKB descriptors (N) and project them into one or more strict ECS memory chunks (M). This guarantees that the offline JSON schema never dictates the runtime ECS memory layout.

Here is the concrete implementation for `Gen.VehicleParameters`.

### 1. The Pure Domain DTO
The data transfer object remains a pure POCO completely decoupled from the ECS, carrying only semantic and UI projection attributes.

```csharp
namespace Fdp.Toolkit.Tkb.Domain
{
    [TkbDescriptor("Gen.VehicleParameters")]
    public record VehicleParametersDto
    {
        [EditUnit("kg")]
        public float Mass { get; init; }
        
        [EditUnit("m")]
        public float Length { get; init; }
        
        [EditUnit("m")]
        public float Width { get; init; }
        
        [EditUnit("m/s")]
        public float MaxSpeedFwd { get; init; }
        
        [EditUnit("m/s²")]
        public float MaxAccel { get; init; }
    }
}
```

### 2. The M:N Translator Contract
Mirroring `IEntityScenarioTranslator`, the TKB translator contract explicitly declares which descriptors it consumes. This allows the entity instantiation pipeline to track which parts of the blueprint have been processed.

```csharp
namespace Fdp.Interfaces
{
    /// <summary>
    /// Custom translator that handles N TKB descriptors → M ECS components.
    /// </summary>
    public interface ITkbEntityTranslator
    {
        /// <summary>
        /// Returns the types of TKB descriptors this translator consumes.
        /// </summary>
        IEnumerable<Type> GetConsumedDescriptors();

        /// <summary>
        /// Projects data from the TKB template into concrete ECS components on the entity.
        /// </summary>
        void Inject(EntityRepository repo, Entity entity, TkbTemplate template);
    }
}
```

### 3. The Concrete Translator Implementation
This translator consumes a single TKB descriptor (`VehicleParametersDto`) and projects it into four distinct ECS components (`VehicleParams`, `VehicleState`, `NavState`, and `PhysicsCollider`). This demonstrates a 1:4 (N:M) translation that satisfies the physics and navigation systems' strict chunk requirements.

```csharp
using CarKinem.Core;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Tkb.Domain;
using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.CarKinem.Translators
{
    public sealed class VehicleKinematicsTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors()
        {
            yield return typeof(VehicleParametersDto);
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            // 1. Extract pure DTO from the template
            var dto = template.GetDescriptor<VehicleParametersDto>();
            if (dto == null) return;

            // 2. Safety Guard: Ensure local node has the required ECS component registered
            if (!repo.IsComponentTypeRegistered<VehicleParams>()) return;

            // 3. Project to ECS Component 1: Static Parameters
            var vehicleParams = new VehicleParams
            {
                Length = dto.Length,
                Width = dto.Width,
                MaxSpeedFwd = dto.MaxSpeedFwd,
                MaxAccel = dto.MaxAccel,
                // Derive wheelbase from length as a domain rule, hiding this logic from the JSON
                WheelBase = dto.Length * 0.6f 
            };
            repo.AddComponent(entity, vehicleParams);

            // 4. Project to ECS Component 2: Dynamic State (Zero-initialized)
            repo.AddComponent(entity, new VehicleState 
            { 
                Speed = 0, 
                SteerAngle = 0, 
                Accel = 0 
            });

            // 5. Project to ECS Component 3: Navigation State
            repo.AddComponent(entity, new NavState 
            { 
                Mode = KinematicsMode.None,
                TargetSpeed = 0 
            });

            // 6. Project to ECS Component 4: Physics Collider
            repo.AddComponent(entity, new PhysicsCollider 
            { 
                // Dynamically compute broadphase bounds based on dimensions
                Radius = Math.Max(dto.Length, dto.Width) / 2f,
                CollisionLayer = 1 
            });
        }
    }
}
```

By decoupling the ingestion schema from the memory layout, you guarantee that if the physics engine later requires splitting `VehicleParams` into smaller, cache-friendly aerodynamic and ground-friction chunks, the TKB JSON files and the DTO schema remain completely untouched. The translator simply projects the same pure data into the new ECS memory topology.



