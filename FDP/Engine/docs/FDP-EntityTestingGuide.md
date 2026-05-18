# Entity Testing Guide

## Overview
This guide provides best practices for writing tests involving entities in the FastDataPlane ECS system. It covers common pitfalls, correct assumptions, and how to properly handle entity references in tests.

---

## Entity Creation Behavior

### Generation Numbers

**✅ CORRECT ASSUMPTION:**
- Newly created entities in a fresh repository start with **generation 1**
- Generation is a `ushort` field in the `EntityHeader` struct
- **Generation 0 is reserved** for uninitialized memory (safety feature)
- `CreateEntity()` bumps generation from 0 to 1 on first use of a slot

**🛡️ SAFETY FEATURE: Protection Against `default(Entity)`**

The system **intentionally skips generation 0** to prevent dangerous collisions with uninitialized Entity structs:

```csharp
// DANGEROUS if generation could be 0:
Entity[] neighbors = new Entity[5];
// neighbors[0] would be {Index: 0, Generation: 0}

var firstEntity = repo.CreateEntity();
// If this returned {Index: 0, Generation: 0}, it would match neighbors[0]!
// repo.IsAlive(neighbors[0]) would INCORRECTLY return true!

// SAFE with generation starting at 1:
// firstEntity is {Index: 0, Generation: 1}
// neighbors[0] is {Index: 0, Generation: 0}
// repo.IsAlive(neighbors[0]) returns FALSE (generation mismatch)
```

**Why This Matters:**
- Entity arrays/fields default to `{Index: 0, Generation: 0}`
- Optional Entity parameters default to `default(Entity)` = `{Index: 0, Generation: 0}`
- Uninitialized Entity structs in classes default to `{Index: 0, Generation: 0}`
- **Generation 1** ensures all of these are distinguishable from valid entities

**❌ COMMON MISTAKE:**
```csharp
// WRONG: Assuming generation starts at 0
var entity = repo.CreateEntity();
var restoredEntity = new Entity(0, 0); // Generation 0 is NEVER valid!
Assert.True(repo.IsAlive(restoredEntity)); // This will FAIL
```

**✅ CORRECT APPROACH:**
```csharp
// CORRECT: First entity has generation 1
var entity = repo.CreateEntity();
Assert.True(repo.IsAlive(entity)); // Use the actual entity handle

// Or if you must construct manually:
var restoredEntity = new Entity(0, 1); // Generation 1 for first creation
Assert.True(repo.IsAlive(restoredEntity));

// Best: let the system tell you what's valid
Assert.Equal(default(Entity), new Entity(0, 0)); // Both are "invalid/uninitialized"
Assert.False(repo.IsAlive(default(Entity))); // default is never alive
```

### When Generation Increments

Generation **ONLY** increments on **destroy + recreate** cycles:

```csharp
var entity1 = repo.CreateEntity();  // Entity(0, gen:0)
repo.DestroyEntity(entity1);

var entity2 = repo.CreateEntity();  // Entity(0, gen:1) - reuses slot 0, increments gen
```

Generation **DOES NOT** increment on:
- Adding/removing components
- Modifying component values
- Flight Recorder replay
- Tick() calls

---

## Entity Reference Best Practices

### ✅ DO: Use Returned Entity Handles

```csharp
// Record phase
var entity = recordRepo.CreateEntity();
recordRepo.AddComponent(entity, new Position { X = 10 });

// Replay phase - let queries find the entity
var query = replayRepo.Query().With<Position>().Build();
query.ForEach((Entity e) => {
    // Use 'e' - the query gives you the actual entity handle
    var pos = replayRepo.GetUnmanagedComponent<Position>(e);
    Assert.Equal(10f, pos.X);
});
```

### ✅ DO: Check EntityCount for Existence

```csharp
using (var reader = new RecordingReader(_testFilePath))
{
    reader.ReadNextFrame(replayRepo);
}

// Verify entity was restored
Assert.Equal(1, replayRepo.EntityCount);
```

### ⚠️ AVOID: Manually Constructing Entity Handles

Unless you have a specific reason (like testing entity ID/generation handling), avoid manually constructing Entity structs:

```csharp
// Fragile - depends on implementation details
var entity = new Entity(0, 0);

// Better - use what the system gives you
var entity = repo.CreateEntity();
```

### ❌ DON'T: Assume Sequential Entity IDs

```csharp
// WRONG: Assuming entities are created sequentially
var e1 = repo.CreateEntity();
var e2 = repo.CreateEntity();
var e3 = repo.CreateEntity();

// Don't assume e1.Index == 0, e2.Index == 1, etc.
// The free list may cause reuse of destroyed entity slots!
```

---

## ComponentMask Behavior

### Automatic Synchronization

The `EntityHeader.ComponentMask` is **automatically synchronized** when:

1. **Adding components** (both managed and unmanaged):
   ```csharp
   repo.AddUnmanagedComponent(entity, new Position { X = 1 });
   // ComponentMask bit for Position is SET
   
   repo.AddManagedComponent(entity, new Name { Value = "Test" });
   // ComponentMask bit for Name is SET
   ```

2. **Removing components**:
   ```csharp
   repo.RemoveUnmanagedComponent<Position>(entity);
   // ComponentMask bit for Position is CLEARED
   ```

3. **Flight Recorder replay**:
   - EntityIndex chunks (typeId == -1) are serialized and restored
   - EntityHeader contains ComponentMask, IsActive, Generation, Authority, etc.
   - All bits are preserved during record/replay

### Testing ComponentMask

```csharp
// Record
var entity = recordRepo.CreateEntity();
recordRepo.AddManagedComponent(entity, new TestComponent { Value = 42 });

// Replay
using (var reader = new RecordingReader(path))
{
    reader.ReadNextFrame(replayRepo);
}

// Verify ComponentMask (if needed for low-level tests)
ref var header = ref replayRepo.GetHeader(0);
var typeId = ManagedComponentType<TestComponent>.ID;
Assert.True(header.ComponentMask.IsSet(typeId));

// But prefer high-level verification via queries:
var query = replayRepo.Query().With<TestComponent>().Build();
int count = 0;
query.ForEach(e => count++);
Assert.Equal(1, count); // Queries rely on ComponentMask
```

---

## Flight Recorder Test Patterns

### Pattern 1: Basic Keyframe Replay

```csharp
[Fact]
public void BasicKeyframeReplay()
{
    // === RECORD ===
    using var recordRepo = new EntityRepository();
    recordRepo.RegisterUnmanagedComponent<Position>();
    
    var entity = recordRepo.CreateEntity();
    recordRepo.AddUnmanagedComponent(entity, new Position { X = 100 });
    
    using (var recorder = new AsyncRecorder(_testFilePath))
    {
        recordRepo.Tick();
        recorder.CaptureKeyframe(recordRepo);
    }
    
    // === REPLAY ===
    using var replayRepo = new EntityRepository();
    replayRepo.RegisterUnmanagedComponent<Position>();
    
    using (var reader = new RecordingReader(_testFilePath))
    {
        bool hasFrame = reader.ReadNextFrame(replayRepo);
        Assert.True(hasFrame);
    }
    
    // === VERIFY ===
    Assert.Equal(1, replayRepo.EntityCount);
    
    // Use query to find entities - don't assume IDs
    var query = replayRepo.Query().With<Position>().Build();
    bool found = false;
    query.ForEach((Entity e) => {
        var pos = replayRepo.GetUnmanagedComponentRO<Position>(e);
        Assert.Equal(100f, pos.X);
        found = true;
    });
    Assert.True(found, "Entity with Position should exist");
}
```

### Pattern 2: Delta Frame Replay

```csharp
[Fact]
public void DeltaFrameReplay()
{
    using var recordRepo = new EntityRepository();
    recordRepo.RegisterUnmanagedComponent<Position>();
    
    var entity = recordRepo.CreateEntity();
    recordRepo.AddUnmanagedComponent(entity, new Position { X = 10 });
    
    using (var recorder = new AsyncRecorder(_testFilePath))
    {
        recordRepo.Tick();
        recorder.CaptureKeyframe(recordRepo);
        
        // Modify in next frame
        recordRepo.Tick();
        recordRepo.GetUnmanagedComponentRW<Position>(entity).X = 20;
        
        recorder.CaptureFrame(recordRepo, recordRepo.GlobalVersion - 1, blocking: true);
    }
    
    using var replayRepo = new EntityRepository();
    replayRepo.RegisterUnmanagedComponent<Position>();
    
    using (var reader = new RecordingReader(_testFilePath))
    {
        reader.ReadNextFrame(replayRepo); // Keyframe
        reader.ReadNextFrame(replayRepo); // Delta
    }
    
    // Verify final state
    var query = replayRepo.Query().With<Position>().Build();
    query.ForEach((Entity e) => {
        var pos = replayRepo.GetUnmanagedComponentRO<Position>(e);
        Assert.Equal(20f, pos.X); // Delta frame value
    });
}
```

### Pattern 3: Managed Components

```csharp
[Fact]
public void ManagedComponentReplay()
{
    using var recordRepo = new EntityRepository();
    recordRepo.RegisterManagedComponent<TestManagedComponent>();
    
    var entity = recordRepo.CreateEntity();
    recordRepo.AddManagedComponent(entity, new TestManagedComponent 
    { 
        Name = "Test", 
        Value = 42 
    });
    
    using (var recorder = new AsyncRecorder(_testFilePath))
    {
        recordRepo.Tick();
        recorder.CaptureKeyframe(recordRepo);
    }
    
    using var replayRepo = new EntityRepository();
    replayRepo.RegisterManagedComponent<TestManagedComponent>();
    
    using (var reader = new RecordingReader(_testFilePath))
    {
        reader.ReadNextFrame(replayRepo);
    }
    
    // Managed components work the same as unmanaged - ComponentMask is synced
    Assert.Equal(1, replayRepo.EntityCount);
    
    // Note: Can't use Query().With<> for managed types (requires value types)
    // So verify via direct access or custom iteration
    ref var header = ref replayRepo.GetHeader(0);
    Assert.True(header.IsActive);
    
    var typeId = ManagedComponentType<TestManagedComponent>.ID;
    Assert.True(header.ComponentMask.IsSet(typeId));
    
    var comp = replayRepo.GetManagedComponent<TestManagedComponent>(new Entity(0, 0));
    Assert.NotNull(comp);
    Assert.Equal("Test", comp.Name);
}
```

---

## Common Pitfalls & Solutions

### Pitfall 1: Assuming Generation Values

**Problem:**
```csharp
var entity = repo.CreateEntity();
// Later in replay...
var restored = new Entity(0, 1); // Assumes gen 1
Assert.True(replayRepo.IsAlive(restored)); // FAILS!
```

**Solution:**
```csharp
// Option A: Use the actual entity handle
var entity = recordRepo.CreateEntity();
// Store entity.Index and entity.Generation if you need to reconstruct

// Option B: For first entity in fresh repo
var restored = new Entity(0, 0); // First creation = gen 0

// Option C: Best - use queries instead
var query = replayRepo.Query().With<SomeComponent>().Build();
query.ForEach((Entity e) => {
    // 'e' has correct index AND generation
});
```

### Pitfall 2: Not Registering Components Before Replay

**Problem:**
```csharp
// Record with components
recordRepo.RegisterUnmanagedComponent<Position>();
var e = recordRepo.CreateEntity();
recordRepo.AddComponent(e, new Position());

// Replay WITHOUT registration
using var replayRepo = new EntityRepository();
// Forgot to register!
reader.ReadNextFrame(replayRepo); // Throws exception or silently fails
```

**Solution:**
```csharp
// Always register in same order for both record and replay
using var replayRepo = new EntityRepository();
replayRepo.RegisterUnmanagedComponent<Position>(); // ✅ Register first
replayRepo.RegisterManagedComponent<Name>();       // ✅ Same types as recording

reader.ReadNextFrame(replayRepo);
```

### Pitfall 3: Checking IsAlive with Wrong Generation

**Problem:**
```csharp
var entity = new Entity(5, 3); // Wrong generation
if (repo.IsAlive(entity)) { ... } // False negative
```

**Solution:**
```csharp
// Check by index first, then validate generation
ref var header = ref repo.GetHeader(entityIndex);
if (header.IsActive)
{
    var correctEntity = new Entity(entityIndex, header.Generation);
    // Now use correctEntity
}
```

### Pitfall 4: Assuming EntityCount Stays Constant During Replay

**Problem:**
```csharp
int beforeCount = repo.EntityCount; // 10
reader.ReadNextFrame(repo); // Might destroy entities
Assert.Equal(beforeCount, repo.EntityCount); // Might fail
```

**Solution:**
```csharp
// Check destruction log if needed
var destroyed = repo.GetDestructionLog();

// Or verify expected final count
reader.ReadNextFrame(replayRepo);
Assert.Equal(expectedCount, replayRepo.EntityCount);
```

---

## Entity Lifecycle State Machine

```
┌─────────────────┐
│  Memory Zeroed  │ (Index never used)
│   Gen = 0       │ ← RESERVED (Invalid/Uninitialized)
│  IsActive = 0   │
└────────┬────────┘
         │
         │ CreateEntity() [First time]
         │ (Bumps gen: 0 → 1)
         ▼
┌─────────────────┐
│    ALIVE        │ Entity(index, 1)
│   Gen = 1       │
│  IsActive = 1   │
└────────┬────────┘
         │
         │ DestroyEntity()
         │ (Increments gen: 1 → 2)
         ▼
┌─────────────────┐
│     DEAD        │
│   Gen = 2       │
│  IsActive = 0   │
└────────┬────────┘
         │
         │ CreateEntity() [Reuse slot]
         │ (Gen preserved at 2)
         ▼
┌─────────────────┐
│    ALIVE        │ Entity(index, 2)
│   Gen = 2       │
│  IsActive = 1   │
└────────┬────────┘
         │
         │ DestroyEntity()
         │ (Increments gen: 2 → 3)
         ▼
┌─────────────────┐
│     DEAD        │
│   Gen = 3       │
│  IsActive = 0   │
└─────────────────┘
```

**Key Points:**
1. **Generation 0 is RESERVED** - it marks uninitialized memory (safety feature)
2. **First CreateEntity() bumps 0 → 1** - ensures first entity is distinguishable from default(Entity)
3. Generation increments on **destroy**, not on create
4. CreateEntity() **preserves** generation if already ≥ 1, then sets IsActive = true
5. Entity handle = (Index, Generation at time of IsActive = true)
6. **Entity.Null / default(Entity) = {0, 0}** is never a valid entity handle

---

## Quick Reference Checklist

When writing entity tests, verify:

- [ ] ✅ Understanding that generation starts at 1 (0 is reserved for safety)
- [ ] ✅ Not manually constructing Entity(0, 0) - it's always invalid
- [ ] ✅ Using returned Entity handles where possible
- [ ] ✅ Registered all component types before replay
- [ ] ✅ Using queries instead of manual entity construction
- [ ] ✅ Checking EntityCount for high-level validation
- [ ] ✅ Not assuming sequential entity IDs
- [ ] ✅ Understanding generation only changes on destroy/recreate
- [ ] ✅ Trusting ComponentMask is automatically synchronized
- [ ] ✅ Using `IsAlive()` with correct generation values
- [ ] ✅ Knowing that `default(Entity)` is never alive

---

## Advanced: Debugging Entity Issues

### Inspecting Entity State

```csharp
public void DebugEntityState(EntityRepository repo, int index)
{
    ref var header = ref repo.GetHeader(index);
    
    Console.WriteLine($"Entity[{index}]:");
    Console.WriteLine($"  IsActive: {header.IsActive}");
    Console.WriteLine($"  Generation: {header.Generation}");
    Console.WriteLine($"  ComponentMask: {header.ComponentMask}");
    Console.WriteLine($"  LastChangeTick: {header.LastChangeTick}");
}
```

### Finding All Active Entities

```csharp
public List<Entity> GetAllActiveEntities(EntityRepository repo)
{
    var entities = new List<Entity>();
    var entityIndex = repo.GetEntityIndex();
    int maxIndex = entityIndex.MaxIssuedIndex;
    
    for (int i = 0; i <= maxIndex; i++)
    {
        ref var header = ref entityIndex.GetHeader(i);
        if (header.IsActive)
        {
            entities.Add(new Entity(i, header.Generation));
        }
    }
    
    return entities;
}
```

### Verifying Flight Recorder Integrity

```csharp
[Fact]
public void VerifyRecorderIntegrity()
{
    // Record
    using var recordRepo = new EntityRepository();
    var originalEntities = new List<Entity>();
    
    for (int i = 0; i < 5; i++)
    {
        originalEntities.Add(recordRepo.CreateEntity());
    }
    
    using (var recorder = new AsyncRecorder(_testFilePath))
    {
        recordRepo.Tick();
        recorder.CaptureKeyframe(recordRepo);
    }
    
    // Replay
    using var replayRepo = new EntityRepository();
    using (var reader = new RecordingReader(_testFilePath))
    {
        reader.ReadNextFrame(replayRepo);
    }
    
    // Verify counts match
    Assert.Equal(recordRepo.EntityCount, replayRepo.EntityCount);
    
    // Verify active indices match
    var originalActive = GetAllActiveEntities(recordRepo);
    var replayedActive = GetAllActiveEntities(replayRepo);
    
    Assert.Equal(originalActive.Count, replayedActive.Count);
    
    // Indices should match (generations should also match for fresh repos)
    for (int i = 0; i < originalActive.Count; i++)
    {
        Assert.Equal(originalActive[i].Index, replayedActive[i].Index);
        Assert.Equal(originalActive[i].Generation, replayedActive[i].Generation);
    }
}
```

---

## Summary

**Golden Rules for Entity Testing:**

1. **Never assume generation values** - use what `CreateEntity()` returns
2. **Prefer queries over manual entity construction** - let the system tell you what exists
3. **Register components in both record and replay repos** - synchronization requires it
4. **Trust the ComponentMask** - it's automatically maintained
5. **Generation = lifecycle counter** - only changes on destroy/create cycles
6. **EntityCount is your friend** - use it for high-level validation
7. **When in doubt, iterate MaxIssuedIndex** - inspect actual entity headers

Following these patterns will make your tests robust, maintainable, and resistant to implementation changes.
