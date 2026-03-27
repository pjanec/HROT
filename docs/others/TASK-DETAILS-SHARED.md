# Shared Components Implementation Tasks

**Version:** 1.0  
**Date:** 2026-02-13  
**Status:** Ready for Development

**Parent Documents**: [DESIGN-SHARED.md](./DESIGN-SHARED.md) | [TASK-TRACKER.md](./TASK-TRACKER.md)

## Overview

This document provides **detailed task breakdown** for implementing shared components. Each task includes acceptance criteria, estimated effort, and dependencies.

**Total Effort:** ~25 developer-days (~5 weeks for 1 developer, ~3.5 weeks for 2 developers)

---

## Phase 1: Infrastructure Validation (2 days)

### Task P1.1: Build Existing FDP Solution

**Goal:** Verify all existing FDP projects compile successfully.

**Steps:**
1. Open `FDP/FDP.sln` in Visual Studio 2022
2. Restore NuGet packages
3. Build solution (All configurations: Debug, Release)
4. Resolve any compilation errors

**Acceptance Criteria:**
- ✅ FDP.sln builds without errors
- ✅ All projects compile successfully
- ✅ Zero warnings related to missing dependencies

**Estimated Effort:** 0.5 days

**Dependencies:** None

---

### Task P1.2: Run Existing Infrastructure Tests

**Goal:** Validate all existing FDP toolkit tests pass.

**Test Suites to Run:**
1. **ID Allocation:**
   - `ModuleHost.Network.Cyclone.Tests/DdsIdAllocatorTests.cs`
   - Verify: Server allocates unique IDs, client buffers blocks

2. **TKB Database:**
   - `FDP.Toolkit.Tkb.Tests/TkbDatabaseTests.cs` (if exists)
   - Verify: Template registration, retrieval, requirement checks

3. **Geographic Transforms:**
   - `Fdp.Toolkit.Geographic.Tests/WGS84TransformTests.cs`
   - Verify: Lat/Lon ↔ Cartesian accuracy within 0.1m for 10km radius

4. **Network Entity Map:**
   - `FDP.Toolkit.Replication.Tests/NetworkEntityMapTests.cs`
   - Verify: ID mapping, graveyard cleanup

5. **Entity Lifecycle:**
   - `FDP.Toolkit.Lifecycle.Tests/EntityLifecycleModuleTests.cs`
   - Verify: Constructing→Active→TearDown transitions

**Steps:**
1. Open Test Explorer in Visual Studio
2. Run all tests in:
   - `ModuleHost.Network.Cyclone.Tests`
   - `FDP.Toolkit.Replication.Tests`
   - `Fdp.Toolkit.Geographic.Tests`
   - `FDP.Toolkit.Lifecycle.Tests`
   - `FDP.Toolkit.Time.Tests`
   - `FDP.Toolkit.Tkb.Tests`
3. Document any failures

**Acceptance Criteria:**
- ✅ All ID allocation tests pass
- ✅ All TKB tests pass
- ✅ All geographic transform tests pass
- ✅ All network entity map tests pass
- ✅ All lifecycle tests pass
- ✅ Test coverage report generated

**Estimated Effort:** 1 day (includes investigating failures if any)

**Dependencies:** P1.1

---

### Task P1.3: Document Existing API Patterns

**Goal:** Create quick reference guide for existing FDP components.

**Deliverables:**
Create `docs/design1_AG/FDP-API-REFERENCE.md` with:

1. **BlockIdManager API:**
   ```csharp
   var idManager = fdpWorld.GetModule<BlockIdManager>();
   int newId = await idManager.AllocateIdAsync();
   ```

2. **TkbDatabase API:**
   ```csharp
   var tkbDb = fdpWorld.GetModule<TkbDatabase>();
   var template = tkbDb.GetTemplate(tkbTypeId);
   var entity = world.NewEntity(template);
   ```

3. **WGS84Transform API:**
   ```csharp
   var origin = new GeoPosition { Latitude = 50.0, Longitude = 14.0, Altitude = 200 };
   var transform = new WGS84Transform(origin);
   var cartesian = transform.ToCartesian(geoPos);
   ```

4. **NetworkEntityMap API:**
   ```csharp
   var entityMap = new NetworkEntityMap(graveyardDurationFrames: 60);
   var entity = entityMap.GetOrCreateEntity(networkId);
   entityMap.MapEntity(networkId, entity);
   ```

5. **TimeController API:**
   ```csharp
   // Master
   var timeController = new MasterTimeController();
   world.AddModule(timeController);
   
   // Slave
   var timeController = new SlaveTimeController(ddsParticipant);
   world.AddModule(timeController);
   ```

**Acceptance Criteria:**
- ✅ FDP-API-REFERENCE.md created
- ✅ All 5 component APIs documented with examples
- ✅ Code examples compile and run

**Estimated Effort:** 0.5 days

**Dependencies:** P1.1, P1.2

---

## Phase 2: Data Model Assembly (3 days)

### Task P2.1: Create Bagira.DDS.DataModel Project

**Goal:** Create C# class library for BDC SST data model.

**Steps:**
1. Create new C# project:
   ```
   dotnet new classlib -n Bagira.DDS.DataModel -f net8.0
   ```
2. Add project to solution:
   ```
   Location: Bagira.DDS.DataModel/
   ```
3. Add NuGet packages:
   - `CycloneDDS.NET` (latest version)
4. Create project structure:
   ```
   Bagira.DDS.DataModel/
     Common/          (Core types)
     Descriptors/     (Entity descriptors)
     Messages/        (Request/Ack messages)
     Map/             (Map-specific types)
     Mission/         (Mission-specific types)
   ```

**Acceptance Criteria:**
- ✅ Project compiles successfully
- ✅ CycloneDDS.NET package restored
- ✅ Folder structure created

**Estimated Effort:** 0.25 days

**Dependencies:** P1.1

---

### Task P2.2: Import FcdCsharp Types

**Goal:** Copy corrected type definitions from `docs/FcdCsharp/` to new project.

**Steps:**
1. Copy `docs/FcdCsharp/Common.cs` → `Bagira.DDS.DataModel/Common/`
   - Types: GeoPosition, OrientationHPR, DAL3, NodeId
2. Copy `docs/FcdCsharp/GenericDescriptors.cs` → `Bagira.DDS.DataModel/Descriptors/`
   - Types: EntityMaster, EntityInfo
3. Copy `docs/FcdCsharp/SimDescriptors.cs` → `Bagira.DDS.DataModel/Descriptors/`
   - Types: GeoSpatial, GeoSpatialDR
4. Copy `docs/FcdCsharp/MapDescriptors.cs` → `Bagira.DDS.DataModel/Map/`
   - Types: MapEntitySymbol, MapVisualOverlay, MapRoute, MapInteractionConfig, MapConfigStatus
5. Copy `docs/FcdCsharp/MissionDescriptors.cs` → `Bagira.DDS.DataModel/Mission/`
   - Types: EntityMission, MissionPlan, MissionTask
6. Copy `docs/FcdCsharp/GenericMessages.cs` → `Bagira.DDS.DataModel/Messages/`
   - Types: CreateEntityRequest, CreateEntityAck, UpdateEntityDescriptorRequest, UpdateEntityDescriptorAck
7. Copy `docs/FcdCsharp/MissionMessages.cs` → `Bagira.DDS.DataModel/Messages/`
   - Types: MissionControlRequest, MissionControlAck
8. Copy `docs/FcdCsharp/MapMessages.cs` → `Bagira.DDS.DataModel/Map/`
   - Types: MapClickEvent, DragEvent, SelectionChangedEvent, ContextActionsUpdate

**Acceptance Criteria:**
- ✅ All 8 files copied and organized
- ✅ Namespaces updated to `Bagira.DDS.DataModel.*`
- ✅ Project compiles without errors

**Estimated Effort:** 0.5 days

**Dependencies:** P2.1

---

### Task P2.3: Add DDS Attributes

**Goal:** Ensure all types have correct `[DdsTopic]` and `[DdsKey]` attributes.

**Validation Checklist:**
Verify each topic type has:
1. `[DdsTopic("TopicName")]` attribute on struct/class
2. `[DdsKey]` attribute on key field (usually `EntityId`)
3. Correct key type (`int` NOT `long` for entities)

**Example:**
```csharp
[DdsTopic("EntityMaster")]
public partial struct EntityMaster
{
    [DdsKey] public int EntityId; // CORRECT: int
    public long TkbType;
    public ulong DisType;
    public ulong Flags;
}
```

**Acceptance Criteria:**
- ✅ All 20+ topic types have `[DdsTopic]` attribute
- ✅ All key fields have `[DdsKey]` attribute
- ✅ No compilation warnings about missing attributes

**Estimated Effort:** 0.5 days

**Dependencies:** P2.2

---

### Task P2.4: Create DDS Publisher/Subscriber Test

**Goal:** Verify data model compiles with CycloneDDS and can publish/subscribe.

**Test Implementation:**
Create `Bagira.DDS.DataModel.Tests/EntityMasterPubSubTests.cs`:

```csharp
[TestClass]
public class EntityMasterPubSubTests
{
    [TestMethod]
    public async Task CanPublishAndSubscribeEntityMaster()
    {
        // Arrange
        using var participant = new DomainParticipant();
        var publisher = participant.CreatePublisher();
        var subscriber = participant.CreateSubscriber();
        
        var writer = publisher.CreateDataWriter<EntityMaster>("EntityMaster");
        var reader = subscriber.CreateDataReader<EntityMaster>("EntityMaster");
        
        var sample = new EntityMaster
        {
            EntityId = 12345,
            TkbType = 100,
            DisType = 0,
            Flags = 0
        };
        
        // Act
        writer.Write(sample);
        await Task.Delay(100); // Wait for propagation
        
        var samples = reader.Take();
        
        // Assert
        Assert.AreEqual(1, samples.Count);
        Assert.AreEqual(12345, samples[0].Data.EntityId);
        Assert.AreEqual(100, samples[0].Data.TkbType);
    }
}
```

**Acceptance Criteria:**
- ✅ Test project created
- ✅ EntityMaster pub/sub test passes
- ✅ GeoSpatial pub/sub test passes
- ✅ CreateEntityRequest/Ack pub/sub test passes

**Estimated Effort:** 1 day

**Dependencies:** P2.3

---

### Task P2.5: Document Data Model Assembly

**Goal:** Create README for data model project.

**Deliverables:**
Create `Bagira.DDS.DataModel/README.md`:

```markdown
# Bagira.DDS.DataModel

BDC SST (Simulate, Stimulate, Track) data model for DDS.

## Namespaces

- `Bagira.DDS.DM` - Common types (GeoPosition, OrientationHPR, etc.)
- `Bagira.BDC.SSTD` - Descriptors (EntityMaster, GeoSpatial, etc.)
- `Bagira.BDC.SSTM` - Messages (CreateEntityRequest, etc.)

## Usage

### Publishing EntityMaster

```csharp
using Bagira.BDC.SSTD;

var sample = new EntityMaster
{
    EntityId = 1,
    TkbType = 100,
    DisType = 0,
    Flags = 0
};

writer.Write(sample);
```

### Subscribing to GeoSpatial

```csharp
var reader = subscriber.CreateDataReader<GeoSpatial>("GeoSpatial");
var samples = reader.Take();

foreach (var sample in samples)
{
    var pos = sample.Data.Pos;
    Console.WriteLine($"Position: {pos.Latitude}, {pos.Longitude}");
}
```

## See Also

- [DATA-MODEL-REFERENCE.md](../../docs/design1_AG/DATA-MODEL-REFERENCE.md)
- [DESIGN-SHARED.md](../../docs/design1_AG/DESIGN-SHARED.md)
```

**Acceptance Criteria:**
- ✅ README.md created
- ✅ Usage examples included
- ✅ Namespace structure documented

**Estimated Effort:** 0.25 days

**Dependencies:** P2.4

---

## Phase 3: FDP.Toolkit.DER Implementation (5 days)

### Task P3.1: Create FDP.Toolkit.DER Project

**Goal:** Create non-ECS entity repository library.

**Steps:**
1. Create new C# project:
   ```
   dotnet new classlib -n FDP.Toolkit.DER -f net8.0
   ```
2. Add project to solution:
   ```
   Location: FDP/Toolkits/FDP.Toolkit.DER/
   ```
3. Add project references:
   - (None - pure C# library, no dependencies)
4. Create project structure:
   ```
   FDP.Toolkit.DER/
     IDerRepo.cs
     IDerEntity.cs
     IDerDescriptor.cs
     DerRepo.cs
     DerEntity.cs
   ```

**Acceptance Criteria:**
- ✅ Project created and compiles
- ✅ Project structure in place

**Estimated Effort:** 0.25 days

**Dependencies:** None (can run in parallel with P2)

---

### Task P3.2: Implement IDerRepo Interface

**Goal:** Define repository interface for entity storage.

**Implementation:**
Create `FDP.Toolkit.DER/IDerRepo.cs`:

```csharp
namespace FDP.Toolkit.DER
{
    /// <summary>
    /// Non-ECS entity repository for IOS Mock.
    /// Thread-safe dictionary-based storage.
    /// </summary>
    public interface IDerRepo
    {
        /// <summary>
        /// Retrieve entity by ID. Returns null if not found.
        /// </summary>
        IDerEntity? GetEntity(long entityId);
        
        /// <summary>
        /// Get all entities currently in repository.
        /// </summary>
        IEnumerable<IDerEntity> GetAllEntities();
        
        /// <summary>
        /// Create new entity with specified ID and TKB type.
        /// Throws if entity ID already exists.
        /// </summary>
        IDerEntity CreateEntity(long entityId, long tkbType);
        
        /// <summary>
        /// Delete entity by ID. No-op if entity doesn't exist.
        /// </summary>
        void DeleteEntity(long entityId);
        
        /// <summary>
        /// Raised when new entity is created.
        /// </summary>
        event Action<IDerEntity> EntityCreated;
        
        /// <summary>
        /// Raised when entity is deleted.
        /// </summary>
        event Action<IDerEntity> EntityDeleted;
    }
}
```

**Acceptance Criteria:**
- ✅ Interface compiles
- ✅ XML documentation complete
- ✅ Method signatures match DESIGN-SHARED.md

**Estimated Effort:** 0.25 days

**Dependencies:** P3.1

---

### Task P3.3: Implement IDerEntity Interface

**Goal:** Define entity interface with descriptor storage.

**Implementation:**
Create `FDP.Toolkit.DER/IDerEntity.cs`:

```csharp
namespace FDP.Toolkit.DER
{
    /// <summary>
    /// DER entity with descriptor storage.
    /// </summary>
    public interface IDerEntity
    {
        /// <summary>
        /// Network entity ID (from EntityMaster).
        /// </summary>
        long EntityId { get; }
        
        /// <summary>
        /// TKB entity type ID (from EntityMaster).
        /// </summary>
        long TkbType { get; }
        
        /// <summary>
        /// Get descriptor of type T. Returns default if not present.
        /// </summary>
        T? GetDescriptor<T>(int partId = 0);
        
        /// <summary>
        /// Set descriptor of type T. Replaces existing if present.
        /// </summary>
        void SetDescriptor<T>(T descriptor, int partId = 0);
        
        /// <summary>
        /// Check if entity has descriptor of type T.
        /// </summary>
        bool HasDescriptor<T>(int partId = 0);
        
        /// <summary>
        /// Get types of all descriptors currently attached.
        /// </summary>
        IEnumerable<Type> GetAllDescriptorTypes();
    }
}
```

*(Note: `IDerDescriptor` interface has been removed to allow storing raw DDS structs without wrapper allocations.)*


**Acceptance Criteria:**
- ✅ Both interfaces compile
- ✅ XML documentation complete
- ✅ Generic constraints correct

**Estimated Effort:** 0.25 days

**Dependencies:** P3.2

---

### Task P3.4: Implement DerRepo Class

**Goal:** Implement thread-safe entity repository.

**Implementation:**
Create `FDP.Toolkit.DER/DerRepo.cs`:

```csharp
using System.Collections.Concurrent;

namespace FDP.Toolkit.DER
{
    public class DerRepo : IDerRepo
    {
        private readonly ConcurrentDictionary<long, DerEntity> _entities = new();
        
        public event Action<IDerEntity>? EntityCreated;
        public event Action<IDerEntity>? EntityDeleted;
        
        public IDerEntity? GetEntity(long entityId)
        {
            return _entities.TryGetValue(entityId, out var entity) ? entity : null;
        }
        
        public IEnumerable<IDerEntity> GetAllEntities()
        {
            return _entities.Values;
        }
        
        public IDerEntity CreateEntity(long entityId, long tkbType)
        {
            var entity = new DerEntity(entityId, tkbType);
            if (!_entities.TryAdd(entityId, entity))
            {
                throw new InvalidOperationException($"Entity {entityId} already exists");
            }
            
            EntityCreated?.Invoke(entity);
            return entity;
        }
        
        public void DeleteEntity(long entityId)
        {
            if (_entities.TryRemove(entityId, out var entity))
            {
                EntityDeleted?.Invoke(entity);
            }
        }
        
        public void Clear()
        {
            _entities.Clear();
        }
        
        public int Count => _entities.Count;
    }
}
```

**Acceptance Criteria:**
- ✅ Class compiles
- ✅ ConcurrentDictionary used for thread safety
- ✅ Events invoked correctly
- ✅ Helper methods (Clear, Count) added

**Estimated Effort:** 0.5 days

**Dependencies:** P3.3

---

### Task P3.5: Implement DerEntity Class

**Goal:** Implement entity with descriptor storage.

**Implementation:**
Create `FDP.Toolkit.DER/DerEntity.cs`:

```csharp
using System.Collections.Concurrent;

namespace FDP.Toolkit.DER
{
    public class DerEntity : IDerEntity
    {
        private readonly ConcurrentDictionary<Tuple<Type, int>, object> _descriptors = new();
        
        public long EntityId { get; }
        public long TkbType { get; }
        
        public DerEntity(long entityId, long tkbType)
        {
            EntityId = entityId;
            TkbType = tkbType;
        }
        
        public T? GetDescriptor<T>(int partId = 0)
        {
            return _descriptors.TryGetValue(Tuple.Create(typeof(T), partId), out var desc) ? (T)desc : default;
        }
        
        public void SetDescriptor<T>(T descriptor, int partId = 0)
        {
            _descriptors[Tuple.Create(typeof(T), partId)] = descriptor!;
        }
        
        public bool HasDescriptor<T>(int partId = 0)
        {
            return _descriptors.ContainsKey(Tuple.Create(typeof(T), partId));
        }
        
        public IEnumerable<Type> GetAllDescriptorTypes()
        {
            return _descriptors.Keys.Select(k => k.Item1).Distinct();
        }
    }
}
```

**Acceptance Criteria:**
- ✅ Class compiles
- ✅ Descriptor storage thread-safe

**Estimated Effort:** 0.5 days

**Dependencies:** P3.4

---

### Task P3.6: Write DER Unit Tests

**Goal:** Achieve 100% code coverage for DER library.

**Test Implementation:**
Create `FDP.Toolkit.DER.Tests/DerRepoTests.cs`:

```csharp
[TestClass]
public class DerRepoTests
{
    [TestMethod]
    public void CreateEntity_ShouldAddToRepository()
    {
        // Arrange
        var repo = new DerRepo();
        
        // Act
        var entity = repo.CreateEntity(1, 100);
        
        // Assert
        Assert.IsNotNull(entity);
        Assert.AreEqual(1, entity.EntityId);
        Assert.AreEqual(100, entity.TkbType);
        Assert.AreEqual(1, repo.Count);
    }
    
    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void CreateEntity_ShouldThrowIfDuplicate()
    {
        var repo = new DerRepo();
        repo.CreateEntity(1, 100);
        repo.CreateEntity(1, 100); // Should throw
    }
    
    [TestMethod]
    public void GetEntity_ShouldReturnNullIfNotFound()
    {
        var repo = new DerRepo();
        var entity = repo.GetEntity(999);
        Assert.IsNull(entity);
    }
    
    [TestMethod]
    public void DeleteEntity_ShouldRemoveFromRepository()
    {
        var repo = new DerRepo();
        repo.CreateEntity(1, 100);
        
        repo.DeleteEntity(1);
        
        Assert.AreEqual(0, repo.Count);
        Assert.IsNull(repo.GetEntity(1));
    }
    
    [TestMethod]
    public void EntityCreated_EventShouldFire()
    {
        var repo = new DerRepo();
        IDerEntity? capturedEntity = null;
        repo.EntityCreated += (e) => capturedEntity = e;
        
        var entity = repo.CreateEntity(1, 100);
        
        Assert.IsNotNull(capturedEntity);
        Assert.AreEqual(1, capturedEntity.EntityId);
    }
    
    [TestMethod]
    public void EntityDeleted_EventShouldFire()
    {
        var repo = new DerRepo();
        IDerEntity? capturedEntity = null;
        repo.EntityDeleted += (e) => capturedEntity = e;
        
        repo.CreateEntity(1, 100);
        repo.DeleteEntity(1);
        
        Assert.IsNotNull(capturedEntity);
        Assert.AreEqual(1, capturedEntity.EntityId);
    }
    
    [TestMethod]
    public void GetAllEntities_ShouldReturnAll()
    {
        var repo = new DerRepo();
        repo.CreateEntity(1, 100);
        repo.CreateEntity(2, 101);
        repo.CreateEntity(3, 102);
        
        var entities = repo.GetAllEntities().ToList();
        
        Assert.AreEqual(3, entities.Count);
    }
}
```

Create `FDP.Toolkit.DER.Tests/DerEntityTests.cs`:

```csharp
// Sample descriptor for testing
public class TestDescriptor : IDerDescriptor
{
    public long EntityId { get; set; }
    public int Version { get; set; }
    public string Data { get; set; }
}

[TestClass]
public class DerEntityTests
{
    [TestMethod]
    public void SetDescriptor_ShouldStoreDescriptor()
    {
        var entity = new DerEntity(1, 100);
        var descriptor = new TestDescriptor { Data = "test" };
        
        entity.SetDescriptor(descriptor);
        
        var retrieved = entity.GetDescriptor<TestDescriptor>();
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("test", retrieved.Data);
        Assert.AreEqual(1, retrieved.EntityId); // Auto-set
    }
    
    [TestMethod]
    public void GetDescriptor_ShouldReturnNullIfNotPresent()
    {
        var entity = new DerEntity(1, 100);
        var descriptor = entity.GetDescriptor<TestDescriptor>();
        Assert.IsNull(descriptor);
    }
    
    [TestMethod]
    public void HasDescriptor_ShouldReturnCorrectValue()
    {
        var entity = new DerEntity(1, 100);
        
        Assert.IsFalse(entity.HasDescriptor<TestDescriptor>());
        
        entity.SetDescriptor(new TestDescriptor());
        
        Assert.IsTrue(entity.HasDescriptor<TestDescriptor>());
    }
    
    [TestMethod]
    public void GetAllDescriptorTypes_ShouldReturnAll()
    {
        var entity = new DerEntity(1, 100);
        entity.SetDescriptor(new TestDescriptor());
        
        var types = entity.GetAllDescriptorTypes().ToList();
        
        Assert.AreEqual(1, types.Count);
        Assert.AreEqual(typeof(TestDescriptor), types[0]);
    }
}
```

Create `FDP.Toolkit.DER.Tests/ConcurrencyTests.cs`:

```csharp
[TestClass]
public class ConcurrencyTests
{
    [TestMethod]
    public void MultipleThreads_CanCreateEntitiesSafely()
    {
        var repo = new DerRepo();
        var tasks = new List<Task>();
        
        for (int i = 0; i < 100; i++)
        {
            int entityId = i;
            tasks.Add(Task.Run(() => repo.CreateEntity(entityId, 100)));
        }
        
        Task.WaitAll(tasks.ToArray());
        
        Assert.AreEqual(100, repo.Count);
    }
    
    [TestMethod]
    public void MultipleThreads_CanReadDescriptorsSafely()
    {
        var entity = new DerEntity(1, 100);
        entity.SetDescriptor(new TestDescriptor { Data = "test" });
        
        var tasks = new List<Task<string>>();
        
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => entity.GetDescriptor<TestDescriptor>()?.Data));
        }
        
        Task.WaitAll(tasks.ToArray());
        
        foreach (var task in tasks)
        {
            Assert.AreEqual("test", task.Result);
        }
    }
}
```

**Acceptance Criteria:**
- ✅ All 15+ unit tests pass
- ✅ Code coverage >95%
- ✅ Thread safety verified

**Estimated Effort:** 2 days

**Dependencies:** P3.5

---

### Task P3.7: Create DDS Translator Example

**Goal:** Demonstrate EntityMaster DDS ingress → DER pattern.

**Implementation:**
Create `FDP.Toolkit.DER.Examples/EntityMasterIngressExample.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Bagira.DDS.DataModel;
using CycloneDDS.Runtime;

public class EntityMasterIngressTranslator
{
    private readonly IDerRepo _repo;
    
    public EntityMasterIngressTranslator(IDerRepo repo)
    {
        _repo = repo;
    }
    
    public void OnEntityMasterReceived(EntityMaster sample, SampleInfo info)
    {
        if (info.InstanceState == InstanceState.Disposed)
        {
            _repo.DeleteEntity(sample.EntityId);
            Console.WriteLine($"Entity {sample.EntityId} disposed");
        }
        else
        {
            var entity = _repo.GetEntity(sample.EntityId) 
                         ?? _repo.CreateEntity(sample.EntityId, sample.TkbType);
            
            // Store the raw struct directly
            entity.SetDescriptor(sample);
            
            Console.WriteLine($"Entity {sample.EntityId} updated (TkbType={sample.TkbType})");
        }
    }
}

// Usage example
public class Program
{
    public static async Task Main()
    {
        var repo = new DerRepo();
        var translator = new EntityMasterIngressTranslator(repo);
        
        using var participant = new DomainParticipant();
        var reader = participant.CreateDataReader<EntityMaster>("EntityMaster");
        
        reader.DataAvailable += (samples) =>
        {
            foreach (var sample in samples)
            {
                if (sample.Info.ValidData)
                {
                    translator.OnEntityMasterReceived(sample.Data, sample.Info);
                }
            }
        };
        
        Console.WriteLine("DER listening for EntityMaster samples...");
        await Task.Delay(Timeout.Infinite);
    }
}
```

**Acceptance Criteria:**
- ✅ Example compiles and runs
- ✅ Successfully ingests EntityMaster samples
- ✅ Handles entity creation, update, and disposal

**Estimated Effort:** 1 day

**Dependencies:** P3.6, P2.4

---

### Task P3.8: Document DER Library

**Goal:** Create comprehensive README for DER library.

**Deliverables:**
Create `FDP/Toolkits/FDP.Toolkit.DER/README.md`:

```markdown
# FDP.Toolkit.DER (Dynamic Entity Repository)

Non-ECS entity repository for IOS Mock. Provides dictionary-based entity storage with descriptor pattern, designed for ImGui panels with DDS translators.

## Why DER?

IOS Mock doesn't use Flecs ECS (no `IWorld`, no `IEntity`). DER provides alternative entity storage with:
- Thread-safe concurrent dictionary
- Descriptor storage (like ECS components)
- Event notifications (EntityCreated, EntityDeleted)
- DDS integration via translators

## Usage

### Basic Repository Operations

```csharp
// Create repository
var repo = new DerRepo();

// Create entity
var entity = repo.CreateEntity(entityId: 1, tkbType: 100);

// Get entity
var entity = repo.GetEntity(1);

// Delete entity
repo.DeleteEntity(1);

// Get all entities
var allEntities = repo.GetAllEntities();
```

### Working with Descriptors

```csharp
// Define custom descriptor
public class MyDescriptor : IDerDescriptor
{
    public long EntityId { get; set; }
    public int Version { get; set; }
    public string Name { get; set; }
}

// Set descriptor
entity.SetDescriptor(new MyDescriptor { Name = "Tank #1" });

// Get descriptor
var desc = entity.GetDescriptor<MyDescriptor>();
if (desc != null)
{
    Console.WriteLine(desc.Name);
}

// Check if descriptor exists
if (entity.HasDescriptor<MyDescriptor>())
{
    // ...
}
```

### DDS Integration

```csharp
// EntityMaster ingress translator
public class EntityMasterIngressTranslator
{
    private readonly IDerRepo _repo;
    
    public void OnEntityMasterReceived(EntityMaster sample, SampleInfo info)
    {
        if (info.InstanceState == InstanceState.Disposed)
        {
            _repo.DeleteEntity(sample.EntityId);
        }
        else
        {
            var entity = _repo.GetEntity(sample.EntityId) 
                         ?? _repo.CreateEntity(sample.EntityId, sample.TkbType);
            entity.SetDescriptor(new DerEntityMaster { /* ... */ });
        }
    }
}
```

## See Also

- [DESIGN-SHARED.md](../../docs/design1_AG/DESIGN-SHARED.md#41-fdptoolkitder-dynamic-entity-repository)
- [IOS Design](../../docs/design1_AG/DESIGN-IOS.md)
```

**Acceptance Criteria:**
- ✅ README.md created
- ✅ Usage examples included
- ✅ API reference complete

**Estimated Effort:** 0.5 days

**Dependencies:** P3.7

---

## Phase 4: FDP.Toolkit.Commands Implementation (4 days)

### Task P4.1: Create FDP.Toolkit.Commands Project

**Goal:** Create RPC-over-DDS library.

**Steps:**
1. Create new C# project:
   ```
   dotnet new classlib -n FDP.Toolkit.Commands -f net8.0
   ```
2. Add project to solution:
   ```
   Location: FDP/Toolkits/FDP.Toolkit.Commands/
   ```
3. Add NuGet packages:
   - `CycloneDDS.NET`
4. Add project references:
   - None (generic library)
5. Create project structure:
   ```
   FDP.Toolkit.Commands/
     DdsCommandClient.cs
   ```

**Acceptance Criteria:**
- ✅ Project created and compiles
- ✅ Dependencies configured

**Estimated Effort:** 0.25 days

**Dependencies:** None (can run in parallel with P2 and P3)

---

### Task P4.2: Implement DdsCommandClient<TRequest, TAck>

**Goal:** Generic RPC client with correlation and timeout.

**Implementation:**
Create `FDP.Toolkit.Commands/DdsCommandClient.cs`:

```csharp
using System.Collections.Concurrent;
using CycloneDDS;

namespace FDP.Toolkit.Commands
{
    /// <summary>
    /// Generic DDS command client with correlation and timeout.
    /// </summary>
    public class DdsCommandClient<TRequest, TAck> where TRequest : struct where TAck : struct
    {
        private readonly DomainParticipant _participant;
        private readonly DataWriter<TRequest> _requestWriter;
        private readonly DataReader<TAck> _ackReader;
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<TAck>> _pending = new();
        private readonly CancellationTokenSource _cts = new();
        
        public DdsCommandClient(DomainParticipant participant, string requestTopic, string ackTopic)
        {
            _participant = participant;
            
            var publisher = participant.CreatePublisher();
            var subscriber = participant.CreateSubscriber();
            
            _requestWriter = publisher.CreateDataWriter<TRequest>(requestTopic);
            _ackReader = subscriber.CreateDataReader<TAck>(ackTopic);
            
            // Start ACK listener loop
            Task.Run(AckListenerLoop);
        }
        
        /// <summary>
        /// Send request and await ACK with timeout.
        /// </summary>
        public async Task<TAck> SendAsync(TRequest request, int timeoutMs = 5000)
        {
            var correlationId = ExtractCorrelationId(request);
            var tcs = new TaskCompletionSource<TAck>();
            _pending[correlationId] = tcs;
            
            // Publish request
            _requestWriter.Write(request);
            
            // Setup timeout
            using var cts = new CancellationTokenSource(timeoutMs);
            cts.Token.Register(() => tcs.TrySetCanceled());
            
            try
            {
                return await tcs.Task;
            }
            finally
            {
                _pending.TryRemove(correlationId, out _);
            }
        }
        
        private async Task AckListenerLoop()
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var samples = await _ackReader.TakeAsync(_cts.Token);
                    
                    foreach (var sample in samples)
                    {
                        if (sample.Info.ValidData)
                        {
                            var correlationId = ExtractCorrelationId(sample.Data);
                            if (_pending.TryRemove(correlationId, out var tcs))
                            {
                                tcs.SetResult(sample.Data);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
        }
        
        /// <summary>
        /// Extract correlation ID from message via reflection (assumes "RequestId" property of type Guid).
        /// </summary>
        private Guid ExtractCorrelationId(object message)
        {
            var prop = message.GetType().GetProperty("RequestId");
            if (prop == null)
                throw new InvalidOperationException("Message must have 'RequestId' property of type Guid");
            
            return (Guid)prop.GetValue(message)!;
        }
        
        public void Dispose()
        {
            _cts.Cancel();
            _requestWriter.Dispose();
            _ackReader.Dispose();
        }
    }
}
```

**Acceptance Criteria:**
- ✅ Class compiles
- ✅ Generic constraints correct
- ✅ TaskCompletionSource pattern used
- ✅ Timeout with CancellationTokenSource
- ✅ Reflection-based correlation ID extraction

**Estimated Effort:** 1.5 days

**Dependencies:** P4.1

---

### Task P4.3: Create BdcCommandGateway

**Goal:** BDC SST-specific command gateway facade.

**Implementation:**
Create `Bagira.Map.Common/Commands/BdcCommandGateway.cs`:

```csharp
using FDP.Toolkit.Commands;
using Bagira.DDS.DataModel;

namespace Bagira.Map.Common.Commands
{
    /// <summary>
    /// Convenience gateway for BDC SST commands.
    /// </summary>
    public class BdcCommandGateway : IDisposable
    {
        private readonly DdsCommandClient<CreateEntityRequest, CreateEntityAck> _createEntity;
        private readonly DdsCommandClient<UpdateEntityDescriptorRequest, UpdateEntityDescriptorAck> _updateDescriptor;
        private readonly DdsCommandClient<MissionControlRequest, MissionControlAck> _missionControl;
        
        public BdcCommandGateway(DomainParticipant participant)
        {
            _createEntity = new(participant, "CreateEntityRequest", "CreateEntityAck");
            _updateDescriptor = new(participant, "UpdateEntityDescriptorRequest", "UpdateEntityDescriptorAck");
            _missionControl = new(participant, "MissionControlRequest", "MissionControlAck");
        }
        
        /// <summary>
        /// Request entity creation.
        /// </summary>
        public async Task<CreateEntityAck> CreateEntityAsync(CreateEntityRequest request, int timeoutMs = 5000)
        {
            return await _createEntity.SendAsync(request, timeoutMs);
        }
        
        /// <summary>
        /// Request descriptor update.
        /// </summary>
        public async Task<UpdateEntityDescriptorAck> UpdateDescriptorAsync(UpdateEntityDescriptorRequest request, int timeoutMs = 5000)
        {
            return await _updateDescriptor.SendAsync(request, timeoutMs);
        }
        
        /// <summary>
        /// Send mission control command.
        /// </summary>
        public async Task<MissionControlAck> SendMissionCommandAsync(MissionControlRequest request, int timeoutMs = 5000)
        {
            return await _missionControl.SendAsync(request, timeoutMs);
        }
        
        public void Dispose()
        {
            _createEntity.Dispose();
            _updateDescriptor.Dispose();
            _missionControl.Dispose();
        }
    }
}
```

**Acceptance Criteria:**
- ✅ Class compiles
- ✅ All 3 command types supported
- ✅ IDisposable implemented

**Estimated Effort:** 0.5 days

**Dependencies:** P4.2, P2.4 (data model)

---

### Task P4.4: Write Commands Unit Tests

**Goal:** Test request/ack correlation and timeouts.

**Test Implementation:**
Create `FDP.Toolkit.Commands.Tests/DdsCommandClientTests.cs`:

```csharp
// Test message types
[DdsTopic("TestRequest")]
public struct TestRequest
{
    [DdsKey] public Guid RequestId;
    public string Payload;
}

[DdsTopic("TestAck")]
public struct TestAck
{
    [DdsKey] public Guid RequestId;
    public int ErrorCode;
    public string Result;
}

[TestClass]
public class DdsCommandClientTests
{
    [TestMethod]
    public async Task SendAsync_ShouldReceiveAck()
    {
        // Arrange
        using var participant = new DomainParticipant();
        var client = new DdsCommandClient<TestRequest, TestAck>(participant, "TestRequest", "TestAck");
        
        // Start mock server
        var server = new MockServer(participant);
        server.Start();
        
        var request = new TestRequest
        {
            RequestId = Guid.NewGuid(),
            Payload = "test"
        };
        
        // Act
        var ack = await client.SendAsync(request, timeoutMs: 1000);
        
        // Assert
        Assert.AreEqual(request.RequestId, ack.RequestId);
        Assert.AreEqual(0, ack.ErrorCode);
    }
    
    [TestMethod]
    [ExpectedException(typeof(TaskCanceledException))]
    public async Task SendAsync_ShouldTimeoutIfNoAck()
    {
        using var participant = new DomainParticipant();
        var client = new DdsCommandClient<TestRequest, TestAck>(participant, "TestRequest", "TestAck");
        
        // No server running - should timeout
        var request = new TestRequest { RequestId = Guid.NewGuid(), Payload = "test" };
        
        await client.SendAsync(request, timeoutMs: 500);
    }
    
    [TestMethod]
    public async Task SendAsync_ShouldHandleConcurrentRequests()
    {
        using var participant = new DomainParticipant();
        var client = new DdsCommandClient<TestRequest, TestAck>(participant, "TestRequest", "TestAck");
        
        var server = new MockServer(participant);
        server.Start();
        
        var tasks = new List<Task<TestAck>>();
        
        for (int i = 0; i < 10; i++)
        {
            var request = new TestRequest { RequestId = Guid.NewGuid(), Payload = $"test{i}" };
            tasks.Add(client.SendAsync(request, timeoutMs: 1000));
        }
        
        var acks = await Task.WhenAll(tasks);
        
        Assert.AreEqual(10, acks.Length);
        foreach (var ack in acks)
        {
            Assert.AreEqual(0, ack.ErrorCode);
        }
    }
}

// Mock server for testing
class MockServer
{
    private readonly DataReader<TestRequest> _requestReader;
    private readonly DataWriter<TestAck> _ackWriter;
    private CancellationTokenSource _cts;
    
    public MockServer(DomainParticipant participant)
    {
        var subscriber = participant.CreateSubscriber();
        var publisher = participant.CreatePublisher();
        
        _requestReader = subscriber.CreateDataReader<TestRequest>("TestRequest");
        _ackWriter = publisher.CreateDataWriter<TestAck>("TestAck");
    }
    
    public void Start()
    {
        _cts = new CancellationTokenSource();
        Task.Run(ProcessLoop);
    }
    
    private async Task ProcessLoop()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            var samples = await _requestReader.TakeAsync(_cts.Token);
            
            foreach (var sample in samples)
            {
                if (sample.Info.ValidData)
                {
                    // Echo back ACK
                    var ack = new TestAck
                    {
                        RequestId = sample.Data.RequestId,
                        ErrorCode = 0,
                        Result = $"Processed: {sample.Data.Payload}"
                    };
                    
                    _ackWriter.Write(ack);
                }
            }
            
            await Task.Delay(10); // Small delay
        }
    }
    
    public void Stop()
    {
        _cts?.Cancel();
    }
}
```

**Acceptance Criteria:**
- ✅ Successful request/ack test passes
- ✅ Timeout test passes
- ✅ Concurrent requests test passes
- ✅ Mock server works correctly

**Estimated Effort:** 1.5 days

**Dependencies:** P4.2

---

### Task P4.5: Document Commands Library

**Goal:** Create comprehensive README.

**Deliverables:**
Create `FDP/Toolkits/FDP.Toolkit.Commands/README.md`:

```markdown
# FDP.Toolkit.Commands

Generic RPC-over-DDS toolkit with async/await pattern, correlation, and timeout.

## Features

- Generic `DdsCommandClient<TRequest, TAck>` pattern
- Auto-correlation via `RequestId` property (Guid)
- Timeout support with `CancellationTokenSource`
- Concurrent request handling with `TaskCompletionSource`

## Usage

### Generic Pattern

```csharp
using var participant = new DomainParticipant();
var client = new DdsCommandClient<MyRequest, MyAck>(participant, "MyRequestTopic", "MyAckTopic");

var request = new MyRequest { RequestId = Guid.NewGuid(), Payload = "data" };

try
{
    var ack = await client.SendAsync(request, timeoutMs: 3000);
    Console.WriteLine($"ACK received: ErrorCode={ack.ErrorCode}");
}
catch (TaskCanceledException)
{
    Console.WriteLine("Request timed out");
}
```

### BDC SST Gateway

```csharp
using Bagira.Map.Common.Commands;

var gateway = new BdcCommandGateway(participant);

var request = new CreateEntityRequest
{
    RequestId = Guid.NewGuid(),
    Owner = new NodeId { AppDomainId = 1, AppInstanceId = 1 },
    Flags = 0,
    InitialDescriptors = new List<EntityDescriptorUnion> { /* ... */ }
};

var ack = await gateway.CreateEntityAsync(request);

if (ack.ErrorCode == 0)
{
    Console.WriteLine($"Entity created: ID={ack.NewEntityId}");
}
```

## Requirements

- Request and ACK types must have `RequestId` property of type `Guid`
- Request and ACK must be DDS topic types

## See Also

- [DESIGN-SHARED.md](../../docs/design1_AG/DESIGN-SHARED.md#42-fdptoolkitcommands-rpc-over-dds)
```

**Acceptance Criteria:**
- ✅ README.md created
- ✅ Usage examples included

**Estimated Effort:** 0.25 days

**Dependencies:** P4.4

---

## Phase 5: Bagira.Map.Definitions (TKB Extensions) (6 days)

### Task P5.1: Create Bagira.Map.Definitions Project

**Goal:** Create domain-specific TKB descriptor library.

**Steps:**
1. Create new C# project:
   ```
   dotnet new classlib -n Bagira.Map.Definitions -f net8.0
   ```
2. Add project to solution:
   ```
   Location: Bagira.Map.Definitions/
   ```
3. Add project references:
   - `FDP.Interfaces`
   - `FDP.Toolkit.Tkb`
4. Create project structure:
   ```
   Bagira.Map.Definitions/
     Tkb/
       IgVisualDef.cs
       SimVehicleDef.cs
       SimCombatDef.cs
       TkbCompositionDef.cs
       BdcTkbBuilder.cs
   ```

**Acceptance Criteria:**
- ✅ Project created and compiles
- ✅  Dependencies configured

**Estimated Effort:** 0.25 days

**Dependencies:** P1.2 (TKB validation)

---

### Task P5.2: Implement IG Visual Descriptor

**Goal:** Create visual properties descriptor for IG.

**Implementation:**
Create `Bagira.Map.Definitions/Tkb/IgVisualDef.cs`:

```csharp
namespace Bagira.Map.Definitions.Tkb
{
    /// <summary>
    /// IG visual properties (color, symbol, 3D model).
    /// </summary>
    public class IgVisualDef : IManagedComponent
    {
        /// <summary>
        /// MIL-STD-2525 symbol code (e.g., "SFGPUCIZ-------" for friendly ground tank).
        /// </summary>
        public string SymbolCode { get; set; } = "SFGPUCIZ-------";
        
        /// <summary>
        /// Path to 3D model file (relative to models directory).
        /// </summary>
        public string ModelPath { get; set; } = "models/default.obj";
        
        /// <summary>
        /// Base color in hex format (#RRGGBB).
        /// </summary>
        public string ColorHex { get; set; } = "#FFFFFF";
        
        /// <summary>
        /// Model scale factor (1.0 = original size).
        /// </summary>
        public float Scale { get; set; } = 1.0f;
        
        /// <summary>
        /// Whether to show text label above entity.
        /// </summary>
        public bool ShowLabel { get; set; } = true;
        
        /// <summary>
        /// Layer name for rendering ("units_ground", "units_air", etc.).
        /// </summary>
        public string LayerName { get; set; } = "units_ground";
    }
}
```

**Acceptance Criteria:**
- ✅ Class compiles
- ✅ IManagedComponent interface implemented
- ✅ XML documentation complete

**Estimated Effort:** 0.25 days

**Dependencies:** P5.1

---

### Task P5.3: Implement SimHost Vehicle Descriptor

**Goal:** Create physics properties descriptor for SimHost.

**Implementation:**
Create `Bagira.Map.Definitions/Tkb/SimVehicleDef.cs`:

```csharp
namespace Bagira.Map.Definitions.Tkb
{
    public enum TerrainMobility
    {
        Tracked,   // Tanks, heavy IFVs
        Wheeled,   // Trucks, light vehicles
        Infantry,  // Dismounted soldiers
        Air,       // Helicopters, fixed-wing
        Naval      // Ships, boats
    }
    
    /// <summary>
    /// SimHost physics properties (mass, dimensions, mobility).
    /// </summary>
    public class SimVehicleDef : IManagedComponent
    {
        /// <summary>
        /// Vehicle mass in kilograms.
        /// </summary>
        public float Mass { get; set; } // kg
        
        /// <summary>
        /// Vehicle length in meters.
        /// </summary>
        public float Length { get; set; } // meters
        
        /// <summary>
        /// Vehicle width in meters.
        /// </summary>
        public float Width { get; set; } // meters
        
        /// <summary>
        /// Vehicle height in meters.
        /// </summary>
        public float Height { get; set; } // meters
        
        /// <summary>
        /// Maximum speed in meters per second.
        /// </summary>
        public float MaxSpeed { get; set; } // m/s
        
        /// <summary>
        /// Acceleration in meters per second squared.
        /// </summary>
        public float Acceleration { get; set; } // m/s²
        
        /// <summary>
        /// Turn rate in degrees per second.
        /// </summary>
        public float TurnRate { get; set; } // deg/s
        
        /// <summary>
        /// Terrain mobility type.
        /// </summary>
        public TerrainMobility Mobility { get; set; }
        
        /// <summary>
        /// Fuel capacity in liters (0 = unlimited).
        /// </summary>
        public float FuelCapacity { get; set; } = 0;
        
        /// <summary>
        /// Fuel consumption rate in liters per hour at max speed.
        /// </summary>
        public float FuelConsumption { get; set; } = 0;
    }
}
```

**Acceptance Criteria:**
- ✅ Class compiles
- ✅ All physics properties included
- ✅ XML documentation complete

**Estimated Effort:** 0.25 days

**Dependencies:** P5.2

---

### Task P5.4: Implement SimHost Combat Descriptor

**Goal:** Create combat properties descriptor for future (stubbed for now).

**Implementation:**
Create `Bagira.Map.Definitions/Tkb/SimCombatDef.cs`:

```csharp
namespace Bagira.Map.Definitions.Tkb
{
    public struct WeaponMount
    {
        /// <summary>
        /// Weapon type identifier (e.g., "120mm_APFSDS", "7.62mm_MG").
        /// </summary>
        public string WeaponType { get; set; }
        
        /// <summary>
        /// Initial ammunition count.
        /// </summary>
        public int Ammunition { get; set; }
        
        /// <summary>
        /// Effective range in meters.
        /// </summary>
        public float Range { get; set; }
        
        /// <summary>
        /// Rate of fire in rounds per minute.
        /// </summary>
        public float RateOfFire { get; set; }
    }
    
    /// <summary>
    /// Combat properties (weapons, armor, sensors).
    /// NOTE: Stubbed for future combat module integration.
    /// </summary>
    public class SimCombatDef : IManagedComponent
    {
        /// <summary>
        /// Frontal armor thickness in mm RHA equivalent.
        /// </summary>
        public float ArmorFront { get; set; } // mm RHA
        
        /// <summary>
        /// Side armor thickness in mm RHA equivalent.
        /// </summary>
        public float ArmorSide { get; set; }
        
        /// <summary>
        /// Rear armor thickness in mm RHA equivalent.
        /// </summary>
        public float ArmorRear { get; set; }
        
        /// <summary>
        /// Weapon systems mounted on vehicle.
        /// </summary>
        public List<WeaponMount> Weapons { get; set; } = new();
        
        /// <summary>
        /// Sensor detection range in meters.
        /// </summary>
        public float SensorRange { get; set; } // meters
        
        /// <summary>
        /// Whether entity can engage threats autonomously.
        /// </summary>
        public bool AutonomousEngagement { get; set; } = false;
    }
}
```

**Acceptance Criteria:**
- ✅ Class compiles
- ✅ All combat properties stubbed
- ✅ XML documentation notes future usage

**Estimated Effort:** 0.25 days

**Dependencies:** P5.3

---

### Task P5.5: Implement TKB Composition Descriptor

**Goal:** Create composite unit (ORBAT) descriptor.

**Implementation:**
Create `Bagira.Map.Definitions/Tkb/TkbCompositionDef.cs`:

```csharp
namespace Bagira.Map.Definitions.Tkb
{
    public struct TkbChildSlot
    {
        /// <summary>
        /// Required child TKB type ID.
        /// </summary>
        public long TkbType { get; set; }
        
        /// <summary>
        /// Number of entities of this type (e.g., 4 tanks in a platoon).
        /// </summary>
        public int Count { get; set; }
        
        /// <summary>
        /// Role tag for identification ("Tank", "Infantry", "Artillery").
        /// </summary>
        public string RoleTag { get; set; }
    }
    
    /// <summary>
    /// Composite unit (ORBAT) definition with subordinate slots.
    /// </summary>
    public class TkbCompositionDef : IManagedComponent
    {
        /// <summary>
        /// Subordinate entity slots.
        /// Example: Tank Platoon has 4x Tank slots.
        /// </summary>
        public List<TkbChildSlot> Subordinates { get; set; } = new();
        
        /// <summary>
        /// Organizational echelon ("Platoon", "Company", "Battalion").
        /// </summary>
        public string Echelon { get; set; } = "Platoon";
        
        /// <summary>
        /// Whether children are automatically created with parent.
        /// </summary>
        public bool AutoCreateChildren { get; set; } = true;
    }
}
```

**Acceptance Criteria:**
- ✅ Class compiles
- ✅ Subordinate slot structure defined
- ✅ XML documentation complete

**Estimated Effort:** 0.25 days

**Dependencies:** P5.4

---

### Task P5.6: Implement BdcTkbBuilder Flu API

**Goal:** Fluent API for registering TKB templates.

**Implementation:**
Create `Bagira.Map.Definitions/Tkb/BdcTkbBuilder.cs`:

```csharp
using FDP.Interfaces;
using FDP.Toolkit.Tkb;

namespace Bagira.Map.Definitions.Tkb
{
    public class BdcTkbBuilder
    {
        private readonly TkbDatabase _db;
        
        public BdcTkbBuilder(TkbDatabase db)
        {
            _db = db;
        }
        
        /// <summary>
        /// Define new vehicle entity type.
        /// </summary>
        public BdcTkbBuilder DefineVehicle(long tkbId, string name)
        {
            var template = new TkbTemplate
            {
                TkbType = tkbId,
                Name = name,
                MandatoryDescriptors = new List<Type>
                {
                    // BDC SST required descriptors (mapped to ECS components)
                    // typeof(EntityMasterComponent),
                    // typeof(EntityInfoComponent),
                    // typeof(GeoSpatialComponent)
                }
            };
            
            _db.RegisterTemplate(template);
            return this;
        }
        
        /// <summary>
        /// Add visual properties (IG).
        /// </summary>
        public BdcTkbBuilder WithVisual(long tkbId, Action<IgVisualDef> configure)
        {
            var template = _db.GetTemplate(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");
            
            var visualDef = new IgVisualDef();
            configure(visualDef);
            template.AddManagedComponent(visualDef);
            return this;
        }
        
        /// <summary>
        /// Add physics properties (SimHost).
        /// </summary>
        public BdcTkbBuilder WithPhysics(long tkbId, Action<SimVehicleDef> configure)
        {
            var template = _db.GetTemplate(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");
            
            var physicsDef = new SimVehicleDef();
            configure(physicsDef);
            template.AddManagedComponent(physicsDef);
            return this;
        }
        
        ///summary>
        /// Add combat properties (future).
        /// </summary>
        public BdcTkbBuilder WithCombat(long tkbId, Action<SimCombatDef> configure)
        {
            var template = _db.GetTemplate(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");
            
            var combatDef = new SimCombatDef();
            configure(combatDef);
            template.AddManagedComponent(combatDef);
            return this;
        }
        
        /// <summary>
        /// Add composite (ORBAT) definition.
        /// </summary>
        public BdcTkbBuilder AsComposite(long tkbId, Action<TkbCompositionDef> configure)
        {
            var template = _db.GetTemplate(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");
            
            var compositionDef = new TkbCompositionDef();
            configure(compositionDef);
            template.AddManagedComponent(compositionDef);
            return this;
        }
    }
}
```

**Acceptance Criteria:**
- ✅ Fluent API compiles
- ✅ All descriptor types supported
- ✅ Method chaining works

**Estimated Effort:** 1 day

**Dependencies:** P5.5

---

### Task P5.7: Register Representative Entity Types

**Goal:** Register 10-15 example entity types using builder.

**Implementation:**
Create `Bagira.Map.Definitions/Tkb/BdcTkbCatalog.cs`:

```csharp
namespace Bagira.Map.Definitions.Tkb
{
    public static class TkbEntityTypes
    {
        // Ground Platforms
        public const long Tank_M1Abrams = 100;
        public const long IFV_Bradley = 101;
        public const long Truck_HMMWV = 102;
        public const long Tank_T72 = 103;
        
        // Lifeforms
        public const long Infantry_Rifleman = 200;
        public const long Infantry_Officer = 201;
        
        // Tactical Graphics
        public const long TacGraphic_FireLine = 8801;
        public const long TacGraphic_Route = 8802;
        public const long TacGraphic_Area = 8803;
        
        // Composite Units
        public const long Unit_TankPlatoon = 301;
        public const long Unit_InfantrySquad = 302;
    }
    
    public static class BdcTkbCatalog
    {
        public static void RegisterAll(TkbDatabase tkbDb)
        {
            var builder = new BdcTkbBuilder(tkbDb);
            
            // M1 Abrams
            builder
                .DefineVehicle(TkbEntityTypes.Tank_M1Abrams, "M1 Abrams")
                .WithVisual(TkbEntityTypes.Tank_M1Abrams, v =>
                {
                    v.SymbolCode = "SFGPUCIZ-------";
                    v.ModelPath = "models/m1_abrams.obj";
                    v.ColorHex = "#2E4057";
                    v.Scale = 1.2f;
                    v.ShowLabel = true;
                })
                .WithPhysics(TkbEntityTypes.Tank_M1Abrams, p =>
                {
                    p.Mass = 61_000; // kg
                    p.Length = 7.93f;
                    p.Width = 3.66f;
                    p.Height = 2.44f;
                    p.MaxSpeed = 20.0f; // m/s
                    p.Acceleration = 2.5f;
                    p.TurnRate = 15.0f;
                    p.Mobility = TerrainMobility.Tracked;
                })
                .WithCombat(TkbEntityTypes.Tank_M1Abrams, c =>
                {
                    c.ArmorFront = 600; // mm RHA
                    c.ArmorSide = 350;
                    c.ArmorRear = 200;
                    c.Weapons.Add(new WeaponMount
                    {
                        WeaponType = "120mm_M256",
                        Ammunition = 42,
                        Range = 3000,
                        RateOfFire = 6
                    });
                    c.SensorRange = 8000;
                });
            
            // Bradley IFV
            builder
                .DefineVehicle(TkbEntityTypes.IFV_Bradley, "M2 Bradley IFV")
                .WithVisual(TkbEntityTypes.IFV_Bradley, v =>
                {
                    v.SymbolCode = "SFGPUCI--------";
                    v.ModelPath = "models/bradley.obj";
                    v.ColorHex = "#2E4057";
                    v.Scale = 1.0f;
                })
                .WithPhysics(TkbEntityTypes.IFV_Bradley, p =>
                {
                    p.Mass = 27_000;
                    p.Length = 6.55f;
                    p.Width = 3.6f;
                    p.Height = 2.98f;
                    p.MaxSpeed = 18.0f;
                    p.Acceleration = 3.0f;
                    p.TurnRate = 20.0f;
                    p.Mobility = TerrainMobility.Tracked;
                })
                .WithCombat(TkbEntityTypes.IFV_Bradley, c =>
                {
                    c.ArmorFront = 100;
                    c.ArmorSide = 60;
                    c.ArmorRear = 40;
                    c.Weapons.Add(new WeaponMount { WeaponType = "25mm_M242", Ammunition = 300, Range = 2500, RateOfFire = 200 });
                    c.Weapons.Add(new WeaponMount { WeaponType = "TOW_ATGM", Ammunition = 7, Range = 3750, RateOfFire = 2 });
                    c.SensorRange = 5000;
                });
            
            // HMMWV
            builder
                .DefineVehicle(TkbEntityTypes.Truck_HMMWV, "HMMWV")
                .WithVisual(TkbEntityTypes.Truck_HMMWV, v =>
                {
                    v.SymbolCode = "SFGPUUS--------";
                    v.ModelPath = "models/hmmwv.obj";
                    v.ColorHex = "#3E5641";
                    v.Scale = 0.9f;
                })
                .WithPhysics(TkbEntityTypes.Truck_HMMWV, p =>
                {
                    p.Mass = 2_400;
                    p.Length = 4.57f;
                    p.Width = 2.16f;
                    p.Height = 1.83f;
                    p.MaxSpeed = 25.0f;
                    p.Acceleration = 4.0f;
                    p.TurnRate = 30.0f;
                    p.Mobility = TerrainMobility.Wheeled;
                });
            
            // T-72 (OPFOR)
            builder
                .DefineVehicle(TkbEntityTypes.Tank_T72, "T-72")
                .WithVisual(TkbEntityTypes.Tank_T72, v =>
                {
                    v.SymbolCode = "SHGPUCIZ-------"; // Hostile
                    v.ModelPath = "models/t72.obj";
                    v.ColorHex = "#8B0000";
                    v.Scale = 1.1f;
                })
                .WithPhysics(TkbEntityTypes.Tank_T72, p =>
                {
                    p.Mass = 41_000;
                    p.Length = 6.95f;
                    p.Width = 3.59f;
                    p.Height = 2.23f;
                    p.MaxSpeed = 17.0f;
                    p.Acceleration = 2.0f;
                    p.TurnRate = 12.0f;
                    p.Mobility = TerrainMobility.Tracked;
                })
                .WithCombat(TkbEntityTypes.Tank_T72, c =>
                {
                    c.ArmorFront = 500;
                    c.ArmorSide = 250;
                    c.ArmorRear = 150;
                    c.Weapons.Add(new WeaponMount { WeaponType = "125mm_2A46", Ammunition = 39, Range = 2800, RateOfFire = 8 });
                    c.SensorRange = 6000;
                });
            
            // Infantry Rifleman
            builder
                .DefineVehicle(TkbEntityTypes.Infantry_Rifleman, "Rifleman")
                .WithVisual(TkbEntityTypes.Infantry_Rifleman, v =>
                {
                    v.SymbolCode = "SFGPUCI--------";
                    v.ModelPath = "models/soldier.obj";
                    v.ColorHex = "#556B2F";
                    v.Scale = 0.6f;
                })
                .WithPhysics(TkbEntityTypes.Infantry_Rifleman, p =>
                {
                    p.Mass = 100;
                    p.Length = 0.6f;
                    p.Width = 0.4f;
                    p.Height = 1.75f;
                    p.MaxSpeed = 2.5f; // Walking
                    p.Acceleration = 1.0f;
                    p.TurnRate = 90.0f;
                    p.Mobility = TerrainMobility.Infantry;
                })
                .WithCombat(TkbEntityTypes.Infantry_Rifleman, c =>
                {
                    c.ArmorFront = 5; // Body armor
                    c.Weapons.Add(new WeaponMount { WeaponType = "M4_Carbine", Ammunition = 210, Range = 300, RateOfFire = 700 });
                    c.SensorRange = 500;
                });
            
            // Tank Platoon (Composite)
            builder
                .DefineVehicle(TkbEntityTypes.Unit_TankPlatoon, "Tank Platoon")
                .WithVisual(TkbEntityTypes.Unit_TankPlatoon, v =>
                {
                    v.SymbolCode = "SFGPUCIZ--H----"; // Platoon echelon
                    v.ColorHex = "#0000FF";
                    v.Scale = 1.5f;
                })
                .AsComposite(TkbEntityTypes.Unit_TankPlatoon, comp =>
                {
                    comp.Subordinates.Add(new TkbChildSlot { TkbType = TkbEntityTypes.Tank_M1Abrams, Count = 4, RoleTag = "Tank" });
                    comp.Echelon = "Platoon";
                    comp.AutoCreateChildren = false; // Manual creation
                });
            
            // Infantry Squad (Composite)
            builder
                .DefineVehicle(TkbEntityTypes.Unit_InfantrySquad, "Infantry Squad")
                .WithVisual(TkbEntityTypes.Unit_InfantrySquad, v =>
                {
                    v.SymbolCode = "SFGPUCI---H----"; // Squad echelon
                    v.ColorHex = "#0000FF";
                    v.Scale = 1.2f;
                })
                .AsComposite(TkbEntityTypes.Unit_InfantrySquad, comp =>
                {
                    comp.Subordinates.Add(new TkbChildSlot { TkbType = TkbEntityTypes.Infantry_Officer, Count = 1, RoleTag = "SquadLeader" });
                    comp.Subordinates.Add(new TkbChildSlot { TkbType = TkbEntityTypes.Infantry_Rifleman, Count = 9, RoleTag = "Rifleman" });
                    comp.Echelon = "Squad";
                    comp.AutoCreateChildren = false;
                });
        }
    }
}
```

**Acceptance Criteria:**
- ✅ 10+ entity types registered
- ✅ All descriptors populated with realistic values
- ✅ Composite units defined

**Estimated Effort:** 2 days

**Dependencies:** P5.6

---

### Task P5.8: Write TKB Extensions Tests

**Goal:** Validate template registration and descriptor retrieval.

**Test Implementation:**
Create `Bagira.Map.Definitions.Tests/TkbBuilderTests.cs`:

```csharp
[TestClass]
public class TkbBuilderTests
{
    [TestMethod]
    public void RegisterAll_ShouldCreateAllTemplates()
    {
        // Arrange
        var tkbDb = new TkbDatabase();
        
        // Act
        BdcTkbCatalog.RegisterAll(tkbDb);
        
        // Assert
        var template = tkbDb.GetTemplate(TkbEntityTypes.Tank_M1Abrams);
        Assert.IsNotNull(template);
        Assert.AreEqual("M1 Abrams", template.Name);
    }
    
    [TestMethod]
    public void M1Abrams_ShouldHaveAllDescriptors()
    {
        var tkbDb = new TkbDatabase();
        BdcTkbCatalog.RegisterAll(tkbDb);
        
        var template = tkbDb.GetTemplate(TkbEntityTypes.Tank_M1Abrams);
        
        // Check managed components
        var visualDef = template.GetManagedComponent<IgVisualDef>();
        Assert.IsNotNull(visualDef);
        Assert.AreEqual("#2E4057", visualDef.ColorHex);
        
        var physicsDef = template.GetManagedComponent<SimVehicleDef>();
        Assert.IsNotNull(physicsDef);
        Assert.AreEqual(61_000, physicsDef.Mass);
        
        var combatDef = template.GetManagedComponent<SimCombatDef>();
        Assert.IsNotNull(combatDef);
        Assert.AreEqual(600, combatDef.ArmorFront);
    }
    
    [TestMethod]
    public void TankPlatoon_ShouldHaveComposition()
    {
        var tkbDb = new TkbDatabase();
        BdcTkbCatalog.RegisterAll(tkbDb);
        
        var template = tkbDb.GetTemplate(TkbEntityTypes.Unit_TankPlatoon);
        
        var compositionDef = template.GetManagedComponent<TkbCompositionDef>();
        Assert.IsNotNull(compositionDef);
        Assert.AreEqual(1, compositionDef.Subordinates.Count);
        Assert.AreEqual(TkbEntityTypes.Tank_M1Abrams, compositionDef.Subordinates[0].TkbType);
        Assert.AreEqual(4, compositionDef.Subordinates[0].Count);
    }
}
```

**Acceptance Criteria:**
- ✅ All tests pass
- ✅ Template registration verified
- ✅ Descriptor retrieval works

**Estimated Effort:** 1 day

**Dependencies:** P5.7

---

### Task P5.9: Document TKB Extensions Library

**Goal:** Create README with usage examples.

**Deliverables:**
Create `Bagira.Map.Definitions/README.md`:

```markdown
# Bagira.Map.Definitions

Domain-specific TKB descriptors for BDC SST.

## Descriptors

- **IgVisualDef**: Visual properties for IG (symbol, model, color)
- **SimVehicleDef**: Physics properties for SimHost (mass, speed, mobility)
- **SimCombatDef**: Combat properties (armor, weapons, sensors)
- **TkbCompositionDef**: Composite unit (ORBAT) subordinates

## Usage

### Registering Entity Types

```csharp
using Bagira.Map.Definitions.Tkb;

var tkbDb = world.GetModule<TkbDatabase>();
BdcTkbCatalog.RegisterAll(tkbDb);
```

### Custom Entity Type

```csharp
var builder = new BdcTkbBuilder(tkbDb);

builder
    .DefineVehicle(999, "Custom Tank")
    .WithVisual(999, v =>
    {
        v.SymbolCode = "SFGPUCIZ-------";
        v.ModelPath = "models/custom_tank.obj";
        v.ColorHex = "#FF00FF";
    })
    .WithPhysics(999, p =>
    {
        p.Mass = 50_000;
        p.MaxSpeed = 25.0f;
        p.Mobility = TerrainMobility.Tracked;
    });
```

### Retrieving Descriptors from Template

```csharp
var template = tkbDb.GetTemplate(TkbEntityTypes.Tank_M1Abrams);

var visualDef = template.GetManagedComponent<IgVisualDef>();
Console.WriteLine($"Model: {visualDef.ModelPath}");

var physicsDef = template.GetManagedComponent<SimVehicleDef>();
Console.WriteLine($"Mass: {physicsDef.Mass} kg");
```

## See Also

- [DESIGN-SHARED.md](../../docs/design1_AG/DESIGN-SHARED.md#43-bagiramapdefinitions-tkb-extensions)
```

**Acceptance Criteria:**
- ✅ README created
- ✅ Usage examples included

**Estimated Effort:** 0.5 days

**Dependencies:** P5.8

---

## Phase 6: Bagira.Map.Common Assembly (2 days)

### Task P6.1: Create Bagira.Map.Common Project

**Goal:** Consolidate shared constants and utilities.

**Steps:**
1. Create new C# project:
   ```
   dotnet new classlib -n Bagira.Map.Common -f net8.0
   ```
2. Add project to solution:
   ```   Location: Bagira.Map.Common/
   ```
3. Add project references:
   - `Bagira.DDS.DataModel`
   - `FDP.Toolkit.Commands`
4. Move `BdcCommandGateway.cs` from P4.3 to this project

**Acceptance Criteria:**
- ✅ Project created and compiles
- ✅ BdcCommandGateway moved successfully

**Estimated Effort:** 0.25 days

**Dependencies:** P4.3

---

### Task P6.2: Add TKB Entity Types Constants

** Goal:** Centralize entity type ID constants.

**Implementation:**
Create `Bagira.Map.Common/TkbEntityTypes.cs`:

```csharp
namespace Bagira.Map.Common
{
    /// <summary>
    /// TKB entity type ID constants (matches Bagira.Map.Definitions).
    /// </summary>
    public static class TkbEntityTypes
    {
        // Ground Platforms
        public const long Tank_M1Abrams = 100;
        public const long IFV_Bradley = 101;
        public const long Truck_HMMWV = 102;
        public const long Tank_T72 = 103;
        
        // Lifeforms
        public const long Infantry_Rifleman = 200;
        public const long Infantry_Officer = 201;
        
        // Tactical Graphics
        public const long TacGraphic_FireLine = 8801;
        public const long TacGraphic_Route = 8802;
        public const long TacGraphic_Area = 8803;
        public const long TacGraphic_Annotation = 8804;
        
        // Composite Units
        public const long Unit_TankPlatoon = 301;
        public const long Unit_InfantrySquad = 302;
    }
}
```

**Acceptance Criteria:**
- ✅ File compiles
- ✅ Constants match Bagira.Map.Definitions

**Estimated Effort:** 0.25 days

**Dependencies:** P5.7

---

### Task P6.3: Add Map Configuration Constants

**Goal:** Centralize map and context constants.

**Implementation:**
Create `Bagira.Map.Common/MapConfig.cs`:

```csharp
namespace Bagira.Map.Common
{
    public static class MapConfig
    {
        /// <summary>
        /// Default map group ID (0 = global shared group).
        /// </summary>
        public const int DefaultMapGroupId = 0;
        
        /// <summary>
        /// Default map instance ID.
        /// </summary>
        public const int DefaultMapId = 1;
    }
    
    public static class ContextKeys
    {
        /// <summary>
        /// Context for placing tank entities on map.
        /// </summary>
        public const string PlaceTank = "place_tank";
        
        /// <summary>
        /// Context for drawing route waypoints.
        /// </summary>
        public const string DrawRoute = "draw_route";
        
        /// <summary>
        /// Context for drawing fire lines.
        /// </summary>
        public const string DrawFireLine = "draw_fire_line";
        
        /// <summary>
        /// Context for measuring distances.
        /// </summary>
        public const string Measure = "measure";
        
        /// <summary>
        /// Context for selecting entities.
        /// </summary>
        public const string Select = "select";
        
        /// <summary>
        /// Context for entity deletion.
        /// </summary>
        public const string Delete = "delete";
    }
}
```

**Acceptance Criteria:**
- ✅ File compiles
- ✅ All relevant constants included

**Estimated Effort:** 0.25 days

**Dependencies:** None

---

### Task P6.4: Create Bagira.Map.Common README

**Goal:** Document shared constants and command gateway.

**Deliverables:**
Create `Bagira.Map.Common/README.md`:

```markdown
# Bagira.Map.Common

Shared constants, utilities, and command gateway for BDC SST.

## Components

### BdcCommandGateway

Convenience facade for BDC SST commands (CreateEntity, UpdateDescriptor, MissionControl).

### TkbEntityTypes

Centralized TKB entity type ID constants.

### MapConfig

Map and context configuration constants.

## Usage

### Command Gateway

```csharp
using Bagira.Map.Common;
using Bagira.Map.Common.Commands;

var gateway = new BdcCommandGateway(participant);

var request = new CreateEntityRequest
{
    RequestId = Guid.NewGuid(),
    Owner = new NodeId { AppDomainId = 1, AppInstanceId = 1 }
};

var ack = await gateway.CreateEntityAsync(request);
```

### Constants

```csharp
long tankTkbId = TkbEntityTypes.Tank_M1Abrams;
int mapGroupId = MapConfig.DefaultMapGroupId;
string contextKey = ContextKeys.PlaceTank;
```

## See Also

- [DESIGN-SHARED.md](../../docs/design1_AG/DESIGN-SHARED.md)
- [FDP.Toolkit.Commands](../FDP.Toolkit.Commands/README.md)
```

**Acceptance Criteria:**
- ✅ README created
- ✅ All components documented

**Estimated Effort:** 0.25 days

**Dependencies:** P6.3

---

### Task P6.5: Build and Validate Bagira.Map.Common

**Goal:** Ensure clean compilation and no circular dependencies.

**Steps:**
1. Build `Bagira.Map.Common` project
2. Run dependency analysis:
   ```
   Bagira.Map.Common
   ├── Bagira.DDS.DataModel
   ├── FDP.Toolkit.Commands
   └── CycloneDDS.NET (transitive)
   ```
3. Verify no circular references

**Acceptance Criteria:**
- ✅ Project builds successfully
- ✅ No circular dependencies
- ✅ All namespaces resolve correctly

**Estimated Effort:** 0.25 days

**Dependencies:** P6.4

---

## Phase 7: Integration Testing (3 days)

### Task P7.1: Create Integration Test Project

**Goal:** Create end-to-end test harness.

**Steps:**
1. Create new C# project:
   ```
   dotnet new mstest -n Bagira.Map.Integration.Tests -f net8.0
   ```
2. Add project to solution
3. Add project references:
   - `Bagira.DDS.DataModel`
   - `Bagira.Map.Common`
   - `FDP.Toolkit.DER`
   - `FDP.Toolkit.Commands`
   - `Bagira.Map.Definitions`
   - `FDP.Toolkit.Tkb`

**Acceptance Criteria:**
- ✅ Test project created
- ✅ All dependencies configured

**Estimated Effort:** 0.25 days

**Dependencies:** P6.5

---

### Task P7.2: Implement End-to-End Entity Creation Test

**Goal:** Test full workflow: IOS creates entity → SimHost allocates ID → IOS ingests.

**Test Implementation:**
Create `Bagira.Map.Integration.Tests/EntityCreationE2ETests.cs`:

```csharp
[TestClass]
public class EntityCreationE2ETests
{
    [TestMethod]
    public async Task FullWorkflow_IOSCreateEntity_SimHostPublishes_IOSIngests()
    {
        // Setup DDS participant
        using var participant = new DomainParticipant();
        
        // IOS Mock components
        var derRepo = new DerRepo();
        var iosGateway = new BdcCommandGateway(participant);
        
        // SimHost Mock (mock server responds with ACK)
        var mockSimHost = new MockSimHost(participant);
        mockSimHost.Start();
        
        // IOS Mock: Send CreateEntityRequest
        var request = new CreateEntityRequest
        {
            RequestId = Guid.NewGuid(),
            Owner = new NodeId { AppDomainId = 1, AppInstanceId = 1 },
            Flags = 0,
            InitialDescriptors = new List<EntityDescriptorUnion>()
        };
        
        var ack = await iosGateway.CreateEntityAsync(request, timeoutMs: 3000);
        
        // Assert ACK received
        Assert.AreEqual(0, ack.ErrorCode);
        Assert.IsTrue(ack.NewEntityId > 0);
        
        // SimHost Mock: Publish EntityMaster
        var entityMaster = new EntityMaster
        {
            EntityId = ack.NewEntityId,
            TkbType = TkbEntityTypes.Tank_M1Abrams,
            DisType = 0,
            Flags = 0
        };
        
        mockSimHost.PublishEntityMaster(entityMaster);
        
        // IOS Mock: Ingress EntityMaster → DER
        await Task.Delay(100); // Wait for propagation
        
        // Manually trigger ingress (in real system this is automatic)
        var reader = participant.CreateDataReader<EntityMaster>("EntityMaster");
        var samples = reader.Take();
        
        foreach (var sample in samples)
        {
            if (sample.Info.ValidData)
            {
                var entity = derRepo.CreateEntity(sample.Data.EntityId, sample.Data.TkbType);
                entity.SetDescriptor(new DerEntityMaster
                {
                    TkbType = sample.Data.TkbType,
                    DisType = sample.Data.DisType
                });
            }
        }
        
        // Assert entity in DER
        var derEntity = derRepo.GetEntity(ack.NewEntityId);
        Assert.IsNotNull(derEntity);
        Assert.AreEqual(TkbEntityTypes.Tank_M1Abrams, derEntity.TkbType);
        
        mockSimHost.Stop();
    }
}

// Mock SimHost for testing
class MockSimHost
{
    private readonly BdcCommandGateway _gateway; // Actually needs DataReader/Writer
    private readonly DataWriter<EntityMaster> _entityMasterWriter;
    private readonly DataReader<CreateEntityRequest> _createRequestReader;
    private readonly DataWriter<CreateEntityAck> _createAckWriter;
    private CancellationTokenSource _cts;
    private int _nextEntityId = 1;
    
    public MockSimHost(DomainParticipant participant)
    {
        var publisher = participant.CreatePublisher();
        var subscriber = participant.CreateSubscriber();
        
        _entityMasterWriter = publisher.CreateDataWriter<EntityMaster>("EntityMaster");
        _createRequestReader = subscriber.CreateDataReader<CreateEntityRequest>("CreateEntityRequest");
        _createAckWriter = publisher.CreateDataWriter<CreateEntityAck>("CreateEntityAck");
    }
    
    public void Start()
    {
        _cts = new CancellationTokenSource();
        Task.Run(ProcessLoop);
    }
    
    private async Task ProcessLoop()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            var samples = await _createRequestReader.TakeAsync(_cts.Token);
            
            foreach (var sample in samples)
            {
                if (sample.Info.ValidData)
                {
                    // Respond with ACK
                    var ack = new CreateEntityAck
                    {
                        RequestId = sample.Data.RequestId,
                        NewEntityId = _nextEntityId++,
                        ErrorCode = 0
                    };
                    
                    _createAckWriter.Write(ack);
                }
            }
            
            await Task.Delay(10);
        }
    }
    
    public void PublishEntityMaster(EntityMaster entityMaster)
    {
        _entityMasterWriter.Write(entityMaster);
    }
    
    public void Stop()
    {
        _cts?.Cancel();
    }
}
```

**Acceptance Criteria:**
- ✅ Test passes
- ✅ Full workflow verified
- ✅ DER contains entity after ingress

**Estimated Effort:** 1.5 days

**Dependencies:** P7.1

---

### Task P7.3: Performance Tests

**Goal:** Verify performance within acceptable bounds.

**Test Implementation:**
Create `Bagira.Map.Integration.Tests/PerformanceTests.cs`:

```csharp
[TestClass]
public class PerformanceTests
{
    [TestMethod]
    public void DER_1000Entities_LookupPerformance()
    {
        var repo = new DerRepo();
        
        // Create 1000 entities
        for (int i = 0; i < 1000; i++)
        {
            repo.CreateEntity(i, TkbEntityTypes.Tank_M1Abrams);
        }
        
        var stopwatch = Stopwatch.StartNew();
        
        // 10,000 lookups
        for (int i = 0; i < 10_000; i++)
        {
            var entity = repo.GetEntity(i % 1000);
            Assert.IsNotNull(entity);
        }
        
        stopwatch.Stop();
        
        double avgLookupMs = stopwatch.Elapsed.TotalMilliseconds / 10_000;
        
        Console.WriteLine($"Average lookup time: {avgLookupMs:F4} ms");
        Assert.IsTrue(avgLookupMs < 0.01, "Lookup should be <10µs");
    }
    
    [TestMethod]
    public async Task Commands_100ConcurrentRequests_Latency()
    {
        using var participant = new DomainParticipant();
        var gateway = new BdcCommandGateway(participant);
        
        var mockServer = new MockSimHost(participant);
        mockServer.Start();
        
        var stopwatch = Stopwatch.StartNew();
        
        var tasks = new List<Task<CreateEntityAck>>();
        
        for (int i = 0; i < 100; i++)
        {
            var request = new CreateEntityRequest
            {
                RequestId = Guid.NewGuid(),
                Owner = new NodeId { AppDomainId = 1, AppInstanceId = 1 }
            };
            
            tasks.Add(gateway.CreateEntityAsync(request, timeoutMs: 3000));
        }
        
        var acks = await Task.WhenAll(tasks);
        
        stopwatch.Stop();
        
        double avgLatencyMs = stopwatch.Elapsed.TotalMilliseconds / 100;
        
        Console.WriteLine($"Average command RTT: {avgLatencyMs:F2} ms");
        Assert.IsTrue(avgLatencyMs < 100, "Average RTT should be <100ms");
        
        mockServer.Stop();
    }
}
```

**Acceptance Criteria:**
- ✅ DER lookup <10µs
- ✅ Command RTT <100ms
- ✅ Performance metrics logged

**Estimated Effort:** 1 day

**Dependencies:** P7.2

---

### Task P7.3: Create Integration Guide

**Goal:** Document integration patterns for subsystem developers.

**Deliverables:**
Create `docs/design1_AG/INTEGRATION-GUIDE-SHARED.md`:

```markdown
# Shared Components Integration Guide

## Overview

This guide shows how to integrate shared components into IOS, IG, and SimHost mocks.

## 1. Data Model Integration

### Add Dependency

```xml
<ProjectReference Include="..\..\Common\Bagira.DDS.DataModel\Bagira.DDS.DataModel.csproj" />
```

### Usage

```csharp
using Bagira.DDS.DataModel;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;

var entityMaster = new EntityMaster
{
    EntityId = 1,
    TkbType = TkbEntityTypes.Tank_M1Abrams
};
```

## 2. DER Integration (IOS Mock Only)

### Add Dependency

```xml
<ProjectReference Include="..\..\Common\FDP.Toolkit.DER\FDP.Toolkit.DER.csproj" />
```

### Setup

```csharp
using FDP.Toolkit.DER;

var derRepo = new DerRepo();

derRepo.EntityCreated += (entity) => Console.WriteLine($"Entity {entity.EntityId} created");
```

### DDS Ingress

```csharp
// EntityMaster subscription
var reader = participant.CreateDataReader<EntityMaster>("EntityMaster");

reader.DataAvailable += (samples) =>
{
    foreach (var sample in samples)
    {
        if (sample.Info.ValidData)
        {
            var entity = derRepo.GetEntity(sample.Data.EntityId) 
                         ?? derRepo.CreateEntity(sample.Data.EntityId, sample.Data.TkbType);
        }
        else if (sample.Info.InstanceState == InstanceState.Disposed)
        {
            derRepo.DeleteEntity(sample.Data.EntityId);
        }
    }
};
```

## 3. Commands Integration

### Add Dependency

```xml
<ProjectReference Include="..\..\Common\Bagira.Map.Common\Bagira.Map.Common.csproj" />
```

### Setup (IOS)

```csharp
using Bagira.Map.Common.Commands;

var gateway = new BdcCommandGateway(participant);

// Create entity
var request = new CreateEntityRequest
{
    RequestId = Guid.NewGuid(),
    Owner = new NodeId { AppDomainId = 1, AppInstanceId = 1 }
};

try
{
    var ack = await gateway.CreateEntityAsync(request, timeoutMs: 3000);
    Console.WriteLine($"Entity created: {ack.NewEntityId}");
}
catch (TaskCanceledException)
{
    Console.WriteLine("Request timed out");
}
```

### Setup (SimHost - Server Side)

```csharp
// Subscribe to requests
var requestReader = participant.CreateDataReader<CreateEntityRequest>("CreateEntityRequest");
var ackWriter = participant.CreateDataWriter<CreateEntityAck>("CreateEntityAck");

requestReader.DataAvailable += async (samples) =>
{
    foreach (var sample in samples)
    {
        if (sample.Info.ValidData)
        {
            // Allocate entity ID
            int newId = await idAllocator.AllocateIdAsync();
            
            // Create entity in ECS
            var entity = world.NewEntity();
            entityMap.MapEntity(newId, entity);
            
            // Send ACK
            var ack = new CreateEntityAck
            {
                RequestId = sample.Data.RequestId,
                NewEntityId = newId,
                ErrorCode = 0
            };
            
            ackWriter.Write(ack);
        }
    }
};
```

## 4. TKB Integration (IG & SimHost)

### Add Dependency

```xml
<ProjectReference Include="..\..\Common\Bagira.Map.Definitions\Bagira.Map.Definitions.csproj" />
```

### Registration

```csharp
using Bagira.Map.Definitions.Tkb;

var tkbDb = world.GetModule<TkbDatabase>();
BdcTkbCatalog.RegisterAll(tkbDb);
```

### Entity Creation from Template

```csharp
long tkbType = TkbEntityTypes.Tank_M1Abrams;
var template = tkbDb.GetTemplate(tkbType);

var entity = world.NewEntity(template); // Applies all descriptors

// Retrieve descriptor
var visualDef = template.GetManagedComponent<IgVisualDef>();
Console.WriteLine($"Model: {visualDef.ModelPath}");
```

## See Also

- [DESIGN-SHARED.md](./DESIGN-SHARED.md)
- [DESIGN-IOS.md](./DESIGN-IOS.md)
- [DESIGN-IG.md](./DESIGN-IG.md)
- [DESIGN-SIMHOST.md](./DESIGN-SIMHOST.md)
```

**Acceptance Criteria:**
- ✅ Guide created
- ✅ All integration patterns documented
- ✅ Examples for all three mocks

**Estimated Effort:** 0.5 days

**Dependencies:** P7.3

---

## Summary Tables

### Effort by Phase

| Phase | Focus | Days | Dependencies |
|-------|-------|------|--------------|
| P1 | Infrastructure Validation | 2 | None |
| P2 | Data Model Assembly | 3 | P1 |
| P3 | FDP.Toolkit.DER | 5 | None (parallel) |
| P4 | FDP.Toolkit.Commands | 4 | None (parallel) |
| P5 | Bagira.Map.Definitions | 6 | P1 |
| P6 | Bagira.Map.Common | 2 | P4 |
| P7 | Integration Testing | 3 | P2, P3, P4, P5, P6 |
| **TOTAL** | | **25** | |

### Critical Path

```
P1 (2d) → P2 (3d) → P7 (3d) = 8 days minimum
              ↓
            P3 (5d) parallel with P4 (4d) and P5 (6d)
```

### Parallelization (2 developers)

**Week 1:**
- Dev1: P1 + P2 (5 days)
- Dev2: P3 (5 days)

**Week 2:**
- Dev1: P4 (4 days) + P6 (2 days, partial)
- Dev2: P5 (6 days, continues into Week 3)

**Week 3:**
- Dev1: P6 (complete) + P7 (3 days, partial)
- Dev2: P5 (complete, 1 day) + P7 (3 days)

**Total: ~3.5 weeks with 2 developers**

---

## Navigation

- **[⬆ Back to DESIGN-SHARED.md](./DESIGN-SHARED.md)**
- **[➜ Task Tracker](./TASK-TRACKER.md)**
