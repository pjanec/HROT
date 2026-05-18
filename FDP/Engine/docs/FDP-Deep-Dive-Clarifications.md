# **FDP Deep Dive - Pre-Implementation Clarifications**

**Critical Design Decisions, Edge Cases, and Implementation Guidance**  
**Read this BEFORE writing any code!**

---

## **Table of Contents**

1. [Memory Model Deep Dive](#1-memory-model-deep-dive)
2. [Entity Lifecycle Edge Cases](#2-entity-lifecycle-edge-cases)
3. [Component Type System](#3-component-type-system)
4. [Multi-Part Descriptors - Critical Details](#4-multi-part-descriptors---critical-details)
5. [Query System & Iteration](#5-query-system--iteration)
6. [Threading Model](#6-threading-model)
7. [Network Authority Model](#7-network-authority-model)
8. [Phase System Implementation](#8-phase-system-implementation)
9. [TKB & DIS Integration](#9-tkb--dis-integration)
10. [Performance Critical Paths](#10-performance-critical-paths)
11. [Testing Strategy Details](#11-testing-strategy-details)
12. [Common Pitfalls & Solutions](#12-common-pitfalls--solutions)
13. [Entity Command Buffers (ECB)](#13-entity-command-buffers-ecb---stage-17)

---

# **1. Memory Model Deep Dive**

## **1.1 Virtual Memory Layout**

### **Question:** How exactly does the virtual memory reservation work?

**Answer:**
```
Virtual Address Space (Reserved):
┌─────────────────────────────────────────────────┐
│ Region for EntityHeaders (96 bytes each)       │
│ Reserved: 1M entities × 96 = ~96 MB            │
│                                                 │
│ Committed chunks:                               │
│ Chunk 0: [0-1023] ────────── 64KB committed   │
│ Chunk 1: [1024-2047] ──────── Not committed    │
│ Chunk 2: [2048-3071] ──────── 64KB committed   │
│ ...                                             │
│ Chunk N: [N*1024...] ───────── Not committed   │
└─────────────────────────────────────────────────┘

Physical RAM:
Only committed chunks consume RAM (~64KB each)
```

**Key Points:**
1. **Reserve** allocates address space (costs ~0 bytes RAM)
2. **Commit** backs pages with physical memory (costs actual RAM)
3. **Windows policy:** Address space is cheap, RAM is precious
4. **64KB alignment:** VirtualAlloc guarantees this automatically

### **Question:** What happens if we run out of address space?

**Answer:**
On 64-bit Windows:
- Available address space: ~128 TB (terabytes)
- Our 1M entities need: ~96 MB
- **We will NEVER run out of address space**

On 32-bit (not supported):
- Available: ~2-3 GB
- Would exceed limits with large entity counts
- **This is why we target x64 only**

### **Question:** What happens if we run out of RAM?

**Answer:**
```csharp
// In NativeMemoryAllocator.Commit()
void* result = VirtualAlloc(ptr, size, MEM_COMMIT, PAGE_READWRITE);

if (result == null)
{
    int error = Marshal.GetLastWin32Error();
    // ERROR_NOT_ENOUGH_MEMORY = 8
    // ERROR_COMMITMENT_LIMIT = 1455
    throw new OutOfMemoryException($"Cannot commit {size} bytes: {error}");
}
```

**User action required:** 
- Catch OutOfMemoryException
- Reduce entity count
- Destroy unused entities to free chunks

---

## **1.2 Chunk Capacity Calculation**

### **Question:** How many entities fit in a chunk for different component sizes?

**Calculation:**
```csharp
ChunkCapacity = CHUNK_SIZE_BYTES / sizeof(T)

Examples:
- sizeof(EntityHeader) = 96 bytes
  → Capacity = 65536 / 96 = 682 entities per chunk
  
- sizeof(Position) = 12 bytes (3 floats)
  → Capacity = 65536 / 12 = 5461 entities per chunk
  
- sizeof(Transform4x4) = 64 bytes
  → Capacity = 65536 / 64 = 1024 entities per chunk
  
- sizeof(HugeComponent) = 10000 bytes
  → Capacity = 65536 / 10000 = 6 entities per chunk
```

### **Question:** What if a component is exactly 64KB?

**Answer:**
```csharp
public static int GetChunkCapacity<T>() where T : unmanaged
{
    int elementSize = Unsafe.SizeOf<T>();
    
    if (elementSize > CHUNK_SIZE_BYTES)
    {
        throw new InvalidOperationException(
            $"Component {typeof(T).Name} ({elementSize} bytes) " +
            $"exceeds maximum size ({CHUNK_SIZE_BYTES} bytes)");
    }
    
    return CHUNK_SIZE_BYTES / elementSize; // At least 1
}
```

**If sizeof(T) == 64KB:**
- Result: 1 entity per chunk
- Still works, just wasteful
- Consider splitting large components

**If sizeof(T) > 64KB:**
- **THROWS EXCEPTION** during RegisterComponent
- Must redesign component (use multi-part instead)

---

## **1.3 Memory Alignment**

### **Question:** Why does EntityHeader need to be 96 bytes?

**Answer:**

```
Memory Layout of EntityHeader:
┌─────────────────────────────────┐ Offset 0
│ ComponentMask (BitMask256)      │ 32 bytes (AVX2 aligned)
├─────────────────────────────────┤ Offset 32
│ AuthorityMask (BitMask256)      │ 32 bytes (AVX2 aligned)
├─────────────────────────────────┤ Offset 64
│ Generation (ushort)             │ 2 bytes
│ Flags (ushort)                  │ 2 bytes
│ Reserved1 (int)                 │ 4 bytes
│ Padding[24]                     │ 24 bytes
└─────────────────────────────────┘ Total: 96 bytes

Why 96?
- Multiple of 32 (AVX2 operations)
- Multiple of cache line (64 bytes)
- Fits exactly 682 per 64KB chunk (no waste)
```

**Critical:** The padding is NOT wasted:
1. Ensures alignment for SIMD operations
2. Reserved for future fields (TKB ID, DIS type)
3. Prevents false sharing in multi-threaded access

### **Question:** Do regular components need special alignment?

**Answer:**
```csharp
// NO special alignment needed for most components
public struct Position
{
    public float X, Y, Z; // 12 bytes, naturally aligned
}

public struct Velocity
{
    public float DX, DY, DZ; // 12 bytes, naturally aligned
}

// Exception: If you want SIMD optimization
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct Transform
{
    public Vector4 Position;  // 16-byte aligned
    public Vector4 Rotation;  // 16-byte aligned
    public Vector4 Scale;     // 16-byte aligned
}
```

**Rule:** Only EntityHeader needs strict alignment (for BitMask256 AVX2 ops)

---

# **2. Entity Lifecycle Edge Cases**

## **2.1 Generation Wraparound**

### **Question:** What happens when generation reaches 65535?

**Implementation:**
```csharp
// In DestroyEntity
header.Generation = (ushort)((header.Generation + 1) % ushort.MaxValue);
if (header.Generation == 0) 
    header.Generation = 1; // Skip 0 (reserved for "never created")
```

**Scenario:**
```
Entity created:  Index=42, Gen=1
Destroyed:       Index=42, Gen→2
Created again:   Index=42, Gen=2
...
After 65534 cycles:
Destroyed:       Index=42, Gen→65535
Created again:   Index=42, Gen=65535
Destroyed:       Index=42, Gen→1 (wraps, skips 0)
Created again:   Index=42, Gen=1
```

**ABA Problem:**
```csharp
Entity old = repo.CreateEntity();  // Gets (Index=42, Gen=1)
Entity stale = old;                // Keep old reference

repo.DestroyEntity(old);

// After 65535 destroy/create cycles on index 42
Entity new = repo.CreateEntity();  // Gets (Index=42, Gen=1) again!

// Now both "stale" and "new" have same values!
bool alive = repo.IsAlive(stale);  // Returns true! Wrong!
```

**Solution:** This is acceptable because:
1. 65535 cycles on ONE index is astronomical
2. If you destroy/create same index 65K times, you have bigger problems
3. Generation is a best-effort protection, not cryptographic

**Mitigation:** Track entities externally if critical:
```csharp
class SafeEntityTracker
{
    private HashSet<Entity> _knownEntities = new();
    
    public Entity CreateEntity(EntityRepository repo)
    {
        var e = repo.CreateEntity();
        _knownEntities.Add(e);
        return e;
    }
    
    public bool IsAlive(Entity e, EntityRepository repo)
    {
        return _knownEntities.Contains(e) && repo.IsAlive(e);
    }
}
```

---

## **2.2 Concurrent Creation/Destruction**

### **Question:** Can I create entities on multiple threads?

**Answer:** Yes, EntityIndex has a lock:

```csharp
public Entity CreateEntity()
{
    lock (_createLock) // Thread-safe
    {
        // Allocate index, increment generation, etc.
    }
}

public void DestroyEntity(Entity entity)
{
    lock (_createLock) // Thread-safe
    {
        // Free index, update chunk counts, etc.
    }
}
```

**Performance Impact:**
- Lock contention if many threads create simultaneously
- **Recommendation:** Use EntityCommandBuffer in parallel sections:

```csharp
// BAD: Lock contention
Parallel.For(0, 10000, i =>
{
    var e = repo.CreateEntity(); // Lock every iteration!
});

// GOOD: Deferred creation
var ecb = new EntityCommandBuffer();
Parallel.For(0, 10000, i =>
{
    var e = ecb.CreateEntity(); // Thread-local, no lock
});
ecb.Playback(repo); // Single-threaded playback
```

---

## **2.3 Entity Destruction During Iteration**

### **Question:** What happens if I destroy an entity while iterating?

**Answer:** **UNDEFINED BEHAVIOR** - DO NOT DO THIS!

```csharp
// WRONG!
foreach (var e in repo.Query(query))
{
    repo.DestroyEntity(e); // Corrupts iterator state!
}
```

**Why it fails:**
```csharp
public bool MoveNext()
{
    while (++_currentId <= _maxId) // _maxId is cached at start
    {
        ref var header = ref _index.GetHeader(_currentId);
        
        if (!header.IsActive) // May have been destroyed mid-iteration
            continue;
        
        // If you destroyed this entity OUTSIDE the iterator,
        // the iterator doesn't know and may skip/duplicate entities
    }
}
```

**Solution:** Use EntityCommandBuffer:
```csharp
// CORRECT
var ecb = new EntityCommandBuffer();
foreach (var e in repo.Query(query))
{
    ecb.DestroyEntity(e); // Queued for later
}
ecb.Playback(repo); // Applied after iteration
```

---

# **3. Component Type System**

## **3.1 Component ID Assignment**

### **Question:** Are component IDs deterministic across runs?

**Answer:** **NO, they are assigned in registration order!**

```csharp
// Run 1:
repo.RegisterComponent<Position>();  // Gets ID 0
repo.RegisterComponent<Velocity>();  // Gets ID 1

// Run 2 (different order):
repo.RegisterComponent<Velocity>();  // Gets ID 0
repo.RegisterComponent<Position>();  // Gets ID 1
```

**Implications for Serialization:**

When saving, you MUST save both:
1. Component data
2. Component ID → Type name mapping

**Save format:**
```json
{
  "componentTypes": [
    { "id": 0, "name": "Position" },
    { "id": 1, "name": "Velocity" }
  ],
  "entities": [...]
}
```

**Load strategy:**
```csharp
public void LoadFromFile(string path)
{
    var data = ReadFile(path);
    
    // 1. Re-register components IN SAVED ORDER
    foreach (var typeInfo in data.componentTypes)
    {
        // Use reflection to find type
        Type type = FindType(typeInfo.name);
        RegisterComponent(type);
    }
    
    // 2. Load entity data (IDs now match)
    foreach (var entityData in data.entities)
    {
        // ...
    }
}
```

### **Question:** Can I force a specific component ID?

**Answer:** Not in current design. Workaround:

```csharp
// Define registration order in one place
public static class ComponentRegistration
{
    public static void RegisterAll(EntityRepository repo)
    {
        // Fixed order - always same IDs
        repo.RegisterComponent<Position>();    // Always ID 0
        repo.RegisterComponent<Velocity>();    // Always ID 1
        repo.RegisterComponent<Health>();      // Always ID 2
        repo.RegisterTag<IsStatic>();          // Always ID 3
        // ...
    }
}
```

---

## **3.2 Tag vs Data Components**

### **Question:** How does the system know if something is a tag?

**Current implementation (Stage 5):**
```csharp
private static int InitializeId()
{
    Type type = typeof(T);
    
    // Check if it's an empty struct
    int size = Unsafe.SizeOf<T>();
    kind = size == 1 ? ComponentKind.Tag : ComponentKind.UnmanagedData;
    
    return ComponentRegistry.Register(type, kind);
}
```

**Problem:** Empty structs in C# have size = 1, not 0

```csharp
public struct IsStatic { }  // sizeof(IsStatic) == 1

// This works because we check for size == 1
```

**Alternative: Explicit Attributes**

```csharp
[Tag]
public struct IsStatic { }

[Tag]
public struct IsEnemy { }

// Then in InitializeId:
kind = type.GetCustomAttribute<TagAttribute>() != null 
    ? ComponentKind.Tag 
    : ComponentKind.UnmanagedData;
```

**Recommendation for Stage 5:**
Use size-based detection (simpler), but document:
```csharp
// Tags MUST be empty structs:
public struct MyTag { } // ✅ Correct

public struct NotATag { public int Value; } // ❌ Has data
```

---

## **3.3 Component Table Polymorphism**

### **Question:** How do we store different types in _componentTables[]?

**Implementation:**
```csharp
private object[] _componentTables; // Array of NativeChunkTable<T> or ManagedChunkTable<T>

// At registration:
_componentTables[typeId] = new NativeChunkTable<Position>(); // object cast

// At access:
var table = (NativeChunkTable<Position>)_componentTables[typeId]; // Cast back
```

**Type Safety Issue:**
```csharp
// What if user does this?
int posId = ComponentType<Position>.ID;
var table = (NativeChunkTable<Velocity>)_componentTables[posId]; // WRONG TYPE!
```

**Protection:**
```csharp
public ref T GetComponent<T>(Entity entity) where T : unmanaged
{
    int typeId = ComponentType<T>.ID; // Gets correct ID for T
    
    // Cast is safe because we registered with same T
    var table = (NativeChunkTable<T>)_componentTables[typeId];
    return ref table[entity.Index];
}
```

**The trick:** ComponentType<T>.ID is **per generic instantiation**, so:
- ComponentType<Position>.ID always returns the same ID
- That ID always maps to NativeChunkTable<Position>
- The cast is always safe

---

# **4. Multi-Part Descriptors - Critical Details**

## **4.1 Memory Layout**

### **Question:** How exactly are multi-part components laid out in memory?

**Diagram:**
```
Entity 42 has 4 wheels:

IndirectionTable (per entity):
┌──────────┬──────────┬──────────┬──────────┐
│ Entity 0 → null (no wheels)                │
│ Entity 1 → null                            │
│ ...                                        │
│ Entity 42→ {DataPtr, Count=4, Capacity=4} │◄─┐
│ Entity 43→ null                            │  │
└────────────────────────────────────────────┘  │
                                                 │
MultiPartHeap:                                   │
┌────────────────────────────────────────────┐  │
│ Page 0:                                    │  │
│   [Offset 0]   Wheel for Entity 42 [0]    │◄─┘
│   [Offset 16]  Wheel for Entity 42 [1]    │
│   [Offset 32]  Wheel for Entity 42 [2]    │
│   [Offset 48]  Wheel for Entity 42 [3]    │
│   [Offset 64]  Weapon for Entity 99 [0]   │
│   [Offset 80]  Weapon for Entity 99 [1]   │
│   ...                                      │
└────────────────────────────────────────────┘
```

**Key Points:**
1. Parts for ONE entity are **contiguous** (cache-friendly)
2. Parts for DIFFERENT entities are **interleaved** (allocation order)
3. IndirectionEntry holds pointer to start of contiguous block

---

## **4.2 Growth Strategy**

### **Question:** What happens when I add more parts than capacity?

**Code:**
```csharp
public void AddPart(int entityId, in T part)
{
    ref var entry = ref _indirectionTable[entityId];
    
    // Case 1: Have capacity
    if (entry.Count < entry.Capacity && entry.DataPtr != null)
    {
        T* array = (T*)entry.DataPtr;
        array[entry.Count] = part;  // In-place add
        entry.Count++;
        return;
    }
    
    // Case 2: Need to grow
    int newCapacity = Math.Max(4, entry.Capacity * 2); // 4 → 8 → 16 → 32 → ...
    void* newPtr = _heap.Allocate(newCapacity);
    
    // Copy existing
    if (entry.Count > 0)
    {
        Buffer.MemoryCopy(entry.DataPtr, newPtr, 
                          newCapacity * sizeof(T), 
                          entry.Count * sizeof(T));
        _heap.Free(entry.DataPtr); // Old memory "leaked" until decommit
    }
    
    // Add new
    T* newArray = (T*)newPtr;
    newArray[entry.Count] = part;
    
    entry.DataPtr = newPtr;
    entry.Count++;
    entry.Capacity = newCapacity;
}
```

**Growth sequence:**
```
Start:     Count=0,  Capacity=0
Add 1st:   Count=1,  Capacity=4   (allocate 4)
Add 2nd:   Count=2,  Capacity=4   (reuse)
Add 3rd:   Count=3,  Capacity=4   (reuse)
Add 4th:   Count=4,  Capacity=4   (reuse)
Add 5th:   Count=5,  Capacity=8   (reallocate, copy 4, add 1)
Add 9th:   Count=9,  Capacity=16  (reallocate, copy 8, add 1)
```

**Memory waste:**
- Overallocate by 25-50% for future growth
- Old allocations "leaked" until heap decommit
- **This is intentional** per user requirement (no compaction)

---

## **4.3 Part Removal**

### **Question:** Why swap-with-last instead of shifting?

**Comparison:**

**Shift-left (preserves order):**
```
Before: [A, B, C, D, E]
Remove index 1 (B):
  Shift C→B, D→C, E→D
After:  [A, C, D, E, ?]
Cost: O(N) copies
```

**Swap-with-last (doesn't preserve order):**
```
Before: [A, B, C, D, E]
Remove index 1 (B):
  Copy E→1
After:  [A, E, C, D, ?]
Cost: O(1) single copy
```

**Implications:**
```csharp
// If you have:
Wheel[0] = FrontLeft
Wheel[1] = FrontRight
Wheel[2] = RearLeft
Wheel[3] = RearRight

// And remove index 1:
Wheel[0] = FrontLeft
Wheel[1] = RearRight  ← Swapped from index 3!
Wheel[2] = RearLeft

// Your wheel indices changed!
```

**Solution:** Don't rely on stable indices:
```csharp
// BAD: Assumes stable indices
int frontRightIndex = 1;
wheels[frontRightIndex].Rotation += delta; // May be wrong after removal!

// GOOD: Search by ID
struct Wheel 
{ 
    public int WheelID; 
    public float Rotation; 
}

foreach (ref var wheel in wheels)
{
    if (wheel.WheelID == FRONT_RIGHT_ID)
        wheel.Rotation += delta;
}
```

---

## **4.4 Thread Safety**

### **Question:** Can I modify multi-part arrays from multiple threads?

**Answer:** **NO** without synchronization.

```csharp
// Thread A
Span<Wheel> wheels = repo.GetParts<Wheel>(entity);
wheels[0].Rotation = 1.0f;

// Thread B (same entity!)
Span<Wheel> wheels = repo.GetParts<Wheel>(entity);
wheels[0].Rotation = 2.0f; // Race condition!
```

**Safe patterns:**

**Pattern 1: Partition entities**
```csharp
Parallel.For(0, entities.Length, i =>
{
    Entity e = entities[i]; // Different entity per thread
    var wheels = repo.GetParts<Wheel>(e); // No conflict
    // ...
});
```

**Pattern 2: Read-only access**
```csharp
Parallel.For(0, entities.Length, i =>
{
    Entity e = entities[i];
    var wheels = repo.GetParts<Wheel>(e);
    
    // Only read, don't write
    float total = 0;
    foreach (var w in wheels)
        total += w.Rotation;
});
```

**Pattern 3: EntityCommandBuffer for modifications**
```csharp
var ecb = new EntityCommandBuffer();

Parallel.For(0, entities.Length, i =>
{
    // Queue multi-part changes
    ecb.AddPart(entities[i], new Wheel { ... });
});

ecb.Playback(repo); // Single-threaded
```

---

## **4.5 Memory Leak Concerns**

### **Question:** Does the heap leak memory when parts are removed?

**Yes, but it's managed:**

```csharp
// Scenario:
Entity e = repo.CreateEntity();

// Add 100 wheels
for (int i = 0; i < 100; i++)
    repo.AddPart<Wheel>(e, new Wheel {...});

// Heap allocated: ~6400 bytes (100 * 64)

// Remove all wheels
for (int i = 99; i >= 0; i--)
    repo.RemovePart<Wheel>(e, i);

// Heap still holds: ~6400 bytes (not reclaimed!)
```

**Why:**
Bump allocator doesn't track individual allocations.

**When it's reclaimed:**
```csharp
// Option 1: Entity destroyed
repo.DestroyEntity(e); // Indirection entry cleared

// Later:
repo.GetMultiPartTable<Wheel>().UncommitEmptyPages();
// If entire 64KB page has no live parts, decommit

// Option 2: Explicit call (rare)
repo.RebuildMultiPartHeap<Wheel>(); // Not in current spec, but could add
```

**Recommendation:**
- Don't worry about it for typical usage
- If you have pathological add/remove patterns, rebuild heap periodically

---

# **5. Query System & Iteration**

## **5.1 Query Matching Algorithm**

### **Question:** How exactly does BitMask256.Matches work?

**Formula:**
```
Match = (EntityMask & IncludeMask) == IncludeMask 
     && (EntityMask && ExcludeMask) == 0
```

**Step-by-step:**
```
Entity has: Position, Velocity, Health
  EntityMask = 00000111 (bits 0, 1, 2 set)

Query: With<Position>, With<Velocity>, Without<Static>
  IncludeMask = 00000011 (bits 0, 1)
  ExcludeMask = 00001000 (bit 3)

Step 1: Check required components
  EntityMask & IncludeMask = 00000111 & 00000011 = 00000011
  Result == IncludeMask? 00000011 == 00000011 → YES

Step 2: Check forbidden components
  EntityMask & ExcludeMask = 00000111 & 00001000 = 00000000
  Result == 0? YES

Match: TRUE
```

**AVX2 Implementation:**
```csharp
// All 256 bits checked in ~3 CPU cycles
Vector256<ulong> vTarget = Avx2.LoadAlignedVector256(pTarget);
Vector256<ulong> vInclude = Avx2.LoadAlignedVector256(pInclude);
Vector256<ulong> vExclude = Avx2.LoadAlignedVector256(pExclude);

// Check missing required bits
Vector256<ulong> hasRequired = Avx2.And(vTarget, vInclude);
Vector256<ulong> missing = Avx2.Xor(hasRequired, vInclude);

// Check forbidden bits
Vector256<ulong> forbidden = Avx2.And(vTarget, vExclude);

// Combine
Vector256<ulong> failures = Avx2.Or(missing, forbidden);
return Avx.TestZ(failures, failures); // All zeros?
```

---

## **5.2 Chunk Skipping Optimization**

### **Question:** How much faster is chunk skipping?

**Without chunk skipping:**
```csharp
for (int id = 0; id <= maxId; id++)
{
    if (!header[id].IsActive) continue; // Check every entity
    if (!MatchesQuery(header[id])) continue;
    yield return id;
}
```

**Iteration cost:** 1M entities × ~10ns = 10ms (even if only 100 match!)

**With chunk skipping:**
```csharp
for (int chunkIdx = 0; chunkIdx < totalChunks; chunkIdx++)
{
    if (_chunkCounts[chunkIdx] == 0) 
    {
        // Skip entire chunk (0-1024 entities)
        id += CHUNK_CAPACITY;
        continue; // 1 check instead of 1024!
    }
    
    // Check entities in this chunk
    for (int local = 0; local < CHUNK_CAPACITY; local++)
    {
        int id = chunkIdx * CHUNK_CAPACITY + local;
        // ...
    }
}
```

**Speedup example:**
```
Scenario: 1M entities, only 10K active (1%)

Without skipping:
  1M checks × 10ns = 10ms

With skipping:
  ~990 empty chunks skipped (1 check each) = ~10μs
  ~10K entities checked = ~100μs
  Total: ~110μs

Speedup: 90x faster!
```

---

## **5.3 Iteration Stability**

### **Question:** What if MaxIssuedIndex changes during iteration?

**Scenario:**
```csharp
// Thread A: Iterating
foreach (var e in repo.Query(query))
{
    // ...
}

// Thread B: Creating entities
var newEntity = repo.CreateEntity(); // Increments MaxIssuedIndex
```

**What happens:**
```csharp
public EntityEnumerator(EntityIndex index, EntityQuery query)
{
    _maxId = _index.MaxIssuedIndex; // Captured at start
    // ...
}

public bool MoveNext()
{
    while (++_currentId <= _maxId) // Uses captured value
    {
        // Won't see entities created after iteration started
    }
}
```

**Result:** New entities NOT visible until next iteration

**This is CORRECT behavior:**
- Iteration sees consistent snapshot
- No surprises mid-loop
- Matches user expectation

---

# **6. Threading Model**

## **6.1 Safe Operations**

### **Question:** What operations are thread-safe?

**Thread-Safe (with locks):**
```csharp
✅ CreateEntity()         // Lock in EntityIndex
✅ DestroyEntity()        // Lock in EntityIndex  
✅ RegisterComponent<T>() // Lock in ComponentRegistry
```

**NOT Thread-Safe:**
```csharp
❌ GetComponent<T>()      // No lock, data race if same entity
❌ SetParts<T>()          // No lock
❌ Query iteration        // Iterator reads shared state
```

**Safe Parallel Patterns:**

**Pattern 1: Disjoint entity sets**
```csharp
// Thread 1 modifies entities [0-499999]
// Thread 2 modifies entities [500000-999999]
// No overlap = safe
```

**Pattern 2: Read-only queries**
```csharp
Parallel.ForEach(entities, e =>
{
    // Only read, never write
    ref readonly var pos = ref repo.GetComponent<Position>(e);
    Console.WriteLine(pos.X);
});
```

**Pattern 3: Entity Command Buffers**
```csharp
var ecb = new EntityCommandBuffer();

Parallel.For(0, 10000, i =>
{
    // Thread-safe: Each thread has its own command list
    var e = ecb.CreateEntity();
    ecb.AddComponent(e, new Position { X = i });
});

// Single-threaded playback
ecb.Playback(repo);
```

---

## **6.2 False Sharing**

### **Question:** What is false sharing and how do we avoid it?

**Problem:**
```
CPU Core 0                    CPU Core 1
├─ Cache Line [0-63] ────────┤
  EntityHeader[0]              EntityHeader[0]
  Loads to L1 cache            Loads to L1 cache
  
Core 0 writes to entity 0
  → Cache line marked dirty
  → Core 1's cache invalidated
  → Core 1 must reload
  
Core 1 writes to entity 0
  → Cache line marked dirty  
  → Core 0's cache invalidated
  → Thrashing!
```

**Our Protection:**
```csharp
// EntityHeader is 96 bytes (aligned)
// Multiple of 64 (cache line size)

[StructLayout(LayoutKind.Sequential, Size = 96, Pack = 32)]
public struct EntityHeader
{
    // Never share cache line with adjacent entity
}
```

**When it matters:**
```csharp
// BAD: Two threads accessing adjacent entities
Parallel.For(0, 1000, i =>
{
    // Entity i and i+1 might share cache line for components!
    ref var pos = ref repo.GetComponent<Position>(entities[i]);
    pos.X = i;
});

// BETTER: Stride access
Parallel.For(0, threadCount, threadId =>
{
    for (int i = threadId; i < entityCount; i += threadCount)
    {
        // Entities are far apart in memory
        ref var pos = ref repo.GetComponent<Position>(entities[i]);
        pos.X = i;
    }
});
```

---

# **7. Network Authority Model**

## **7.1 Authority Mask Usage**

### **Question:** How do I set/check authority?

**Setting Authority:**
```csharp
// When creating a local entity
Entity e = repo.CreateEntity();

// Mark Position and Velocity as owned by us
ref var header = ref repo.EntityIndex.GetHeader(e.Index);
header.AuthorityMask.SetBit(ComponentType<Position>.ID);
header.AuthorityMask.SetBit(ComponentType<Velocity>.ID);

// ComponentMask is set when components are added
repo.GetComponent<Position>(e) = new Position { ... };
repo.GetComponent<Velocity>(e) = new Velocity { ... };
```

**Querying Owned Entities:**
```csharp
// Find all entities where we own Position
var query = new EntityQuery()
    .WithOwned<Position>();

foreach (var e in repo.Query(query))
{
    // Safe to modify Position - we own it
    ref var pos = ref repo.GetComponent<Position>(e);
    pos.X += velocity * dt;
}
```

**Partial Ownership:**
```csharp
// Scenario: Tank entity
// - We own Position, Velocity (we control movement)
// - Remote owns TurretRotation (remote controls turret)

Entity tank = receivedFromNetwork();

ref var header = ref repo.EntityIndex.GetHeader(tank.Index);

// Set what we own
header.ComponentMask.SetBit(ComponentType<Position>.ID);
header.ComponentMask.SetBit(ComponentType<Velocity>.ID);
header.ComponentMask.SetBit(ComponentType<TurretRotation>.ID);

header.AuthorityMask.SetBit(ComponentType<Position>.ID);
header.AuthorityMask.SetBit(ComponentType<Velocity>.ID);
// NOT TurretRotation - remote owns it

// Later, in simulation:
var ownedQuery = new EntityQuery()
    .WithOwned<Position>()
    .WithOwned<Velocity>();

foreach (var e in repo.Query(ownedQuery))
{
    // Only processes entities we own
    ref var pos = ref repo.GetComponent<Position>(e);
    ref var vel = ref repo.GetComponent<Velocity>(e);
    pos.X += vel.DX * dt; // We control movement
}

// Meanwhile, network updates TurretRotation from remote
```

---

## **7.2 Phase-Based Authority Enforcement**

### **Question:** How does phase enforcement prevent invalid writes?

**Phase Definitions:**
```csharp
public enum Phase
{
    Initialization,   // Setup only
    NetworkReceive,   // Apply remote updates
    Simulation,       // Local logic
    NetworkSend,      // Export changes
    Presentation      // Read-only rendering
}
```

**Authority Rules:**
```csharp
// In NetworkReceive phase:
// - Can write to components we DON'T own (remote updates)
// - CANNOT write to components we DO own (would conflict with local sim)

// In Simulation phase:
// - Can write to components we DO own (local logic)
// - CANNOT write to components we DON'T own (remote controls them)
```

**Enforcement Code:**
```csharp
public ref T GetComponent<T>(Entity entity) where T : unmanaged
{
    int typeId = ComponentType<T>.ID;
    ref var header = ref _entityIndex.GetHeader(entity.Index);
    
    // Check phase permissions
    if (_currentPhase == Phase.NetworkReceive)
    {
        // Must NOT be owned by us
        if (header.AuthorityMask.IsSet(typeId))
        {
            throw new WrongPhaseException(
                $"Cannot modify owned component {typeof(T).Name} during NetworkReceive");
        }
    }
    else if (_currentPhase == Phase.Simulation)
    {
        // Must BE owned by us
        if (!header.AuthorityMask.IsSet(typeId))
        {
            throw new WrongPhaseException(
                $"Cannot modify remote component {typeof(T).Name} during Simulation");
        }
    }
    else if (_currentPhase == Phase.Presentation)
    {
        throw new WrongPhaseException(
            "Cannot modify components during Presentation (read-only)");
    }
    
    return ref GetTable<T>()[entity.Index];
}
```

**Example Flow:**
```csharp
// Frame start
repo.SetPhase(Phase.NetworkReceive);

// Apply network packets
foreach (var update in networkUpdates)
{
    // OK: TurretRotation is remote-owned
    repo.GetComponent<TurretRotation>(update.Entity) = update.Rotation;
    
    // THROWS: Position is local-owned
    // repo.GetComponent<Position>(update.Entity) = ... // WrongPhaseException!
}

// Simulation
repo.SetPhase(Phase.Simulation);

foreach (var e in repo.Query(ownedQuery))
{
    // OK: Position is local-owned
    ref var pos = ref repo.GetComponent<Position>(e);
    pos.X += velocity * dt;
    
    // THROWS: TurretRotation is remote-owned
    // repo.GetComponent<TurretRotation>(e).Angle = 0; // WrongPhaseException!
}

// Send updates
repo.SetPhase(Phase.NetworkSend);
// Read-only: Serialize owned components

// Render
repo.SetPhase(Phase.Presentation);
// Read-only: Draw entities
```

---

# **8. Phase System Implementation**

## **8.1 Phase Transitions**

### **Question:** What happens during phase transitions?

**Safe Transition:**
```csharp
public void SetPhase(Phase newPhase)
{
    // Validate state before transitioning
    ValidatePhaseTransition(_currentPhase, newPhase);
    
    // Optional: Run phase exit handlers
    OnPhaseExit(_currentPhase);
    
    // Transition
    _currentPhase = newPhase;
    
    // Optional: Run phase enter handlers
    OnPhaseEnter(newPhase);
}
```

**Validation:**
```csharp
private void ValidatePhaseTransition(Phase from, Phase to)
{
    // Enforce valid transitions
    var validTransitions = new Dictionary<Phase, Phase[]>
    {
        [Phase.Initialization] = new[] { Phase.NetworkReceive },
        [Phase.NetworkReceive] = new[] { Phase.Simulation },
        [Phase.Simulation] = new[] { Phase.NetworkSend },
        [Phase.NetworkSend] = new[] { Phase.Presentation },
        [Phase.Presentation] = new[] { Phase.NetworkReceive } // Loop
    };
    
    if (!validTransitions[from].Contains(to))
    {
        throw new InvalidOperationException(
            $"Invalid phase transition: {from} → {to}");
    }
}
```


## Configurable Phase System - Overview

The **Configurable Phase System** allows complete external control over entity repository phases. Phases are **string-based** and defined entirely by their **attributes** (permissions, transitions), not by fixed enum values. This enables unlimited custom phases like multiple simulation phases (`PhysicsSim`, `AISim`, `CombatSim`) or presentation phases (`RenderWorld`, `RenderUI`).

### **Key Design Principles**

1. **Phases identified by string names** - Unlimited custom phases
2. **Attributes defined externally** - PhaseConfig defines behavior
3. **Hot path optimized** - Integer IDs for O(1) comparisons
4. **Zero string comparisons on hot path** - Cached permissions

---

## **Architecture**

### **Phase Class**

```csharp
public class Phase : IEquatable<Phase>
{
    public string Name { get; }
    internal int Id { get; }  // For hot path optimization
    
    // Common phases (convenience, not required)
    public static readonly Phase Initialization = new Phase("Initialization");
    public static readonly Phase NetworkReceive = new Phase("NetworkReceive");
    public static readonly Phase Simulation = new Phase("Simulation");
    public static readonly Phase NetworkSend = new Phase("NetworkSend");
    public static readonly Phase Presentation = new Phase("Presentation");
    
    public Phase(string name) { ... }
    
    // HOT PATH: Uses integer ID comparison
    public bool Equals(Phase other) => other != null && Id == other.Id;
}
```

**Design:**
- Phases created with any string name: `new Phase("PhysicsSim")`
- Each unique name gets a unique integer ID (via `PhaseRegistry`)
- Equality uses ID comparison (O(1)), not string comparison
- Names are for configuration/debugging only

---

### **PhaseConfig Class**

```csharp
public class PhaseConfig
{
    // Configuration (string-based for ease of use)
    public Dictionary<string, HashSet<string>> ValidTransitions { get; set; }
    public Dictionary<string, PhasePermission> Permissions { get; set; }
    
    // HOT PATH: Internal ID-based caches
    private Dictionary<int, PhasePermission> _idToPermissionCache;
    private Dictionary<int, HashSet<int>> _idTransitionsCache;
    
    // Called when config is set on EntityRepository
    internal void BuildCache() { ... }
    
    // HOT PATH methods (use integer IDs)
    internal PhasePermission GetPermissionById(int phaseId);
    internal bool IsTransitionValidById(int fromPhaseId, int toPhaseId);
}
```

**Optimization Strategy:**
1. Configuration uses strings (user-friendly)
2. `BuildCache()` converts to ID-based lookups
3. Hot path uses only integer comparisons
4. Zero string comparisons during runtime

---

### **PhasePermission Enum**

```csharp
public enum PhasePermission
{
    ReadOnly = 0,       // No modifications
    ReadWriteAll = 1,   // Unrestricted access
    OwnedOnly = 2,      // Only components with HasAuthority() = true
    UnownedOnly = 3     // Only components with HasAuthority() = false
}
```

---

## **Hot Path Performance**

### **ValidateWriteAccess - Critical Hot Path**

```csharp
private void ValidateWriteAccess<T>(Entity entity) where T : unmanaged
{
    // FAST: Single enum comparison using cached value
    if (_currentPhasePermission == PhasePermission.ReadWriteAll) return;
    
    if (_currentPhasePermission == PhasePermission.ReadOnly)
        throw new InvalidOperationException(...);
    
    // ... authority checks
}
```

**Performance:**
- ~2-3 CPU cycles (single enum comparison)
- No dictionary lookups
- No string comparisons
- Permission pre-cached in `_currentPhasePermission`

---

### **SetPhase - Transition Path**

```csharp
public void SetPhase(Phase phase)
{
    // HOT PATH: Integer ID lookups in HashSet<int>
    if (PhaseConfig != null && 
        !PhaseConfig.IsTransitionValidById(_currentPhase.Id, phase.Id))
    {
        throw new InvalidOperationException(...);
    }
    
    _currentPhase = phase;
    UpdatePermissionCache();  // Update cached permission
}
```

**Performance:**
- O(1) HashSet<int> lookup for transition validation
- Integer comparison for phase IDs
- Cache update only on phase change (not per-component access)

---

## **Built-in Configurations**

### **PhaseConfig.Default (Networked Simulation)**

```csharp
config.ValidTransitions = {
    ["Initialization"]  -> ["NetworkReceive"],
    ["NetworkReceive"]  -> ["Simulation"],
    ["Simulation"]      -> ["NetworkSend"],
    ["NetworkSend"]     -> ["Presentation"],
    ["Presentation"]    -> ["NetworkReceive"]  // Loop
};

config.Permissions = {
    ["Initialization"]  = ReadWriteAll,   // Setup
    ["NetworkReceive"]  = UnownedOnly,    // Update remote entities
    ["Simulation"]      = OwnedOnly,      // Update local entities
    ["NetworkSend"]     = ReadOnly,       // Serialize
    ["Presentation"]    = ReadOnly        // Render
};
```

**Use Case:** Multiplayer game with authority-based replication

---

### **PhaseConfig.Relaxed (Testing/Single-Player)**

```csharp
// All common phases allow all transitions and full write access
foreach (var phase in commonPhases)
{
    config.Permissions[phase] = ReadWriteAll;
    config.ValidTransitions[phase] = allPhases;
}
```

**Use Cases:**
- Unit tests
- Level editors
- Single-player games
- Prototyping

---

## **Custom Phase Examples**

### **Multiple Simulation Phases**

```csharp
var config = new PhaseConfig();
config.ValidTransitions = new()
{
    ["Init"]        -> ["PhysicsSim"],
    ["PhysicsSim"]  -> ["AISim"],
    ["AISim"]       -> ["CombatSim"],
    ["CombatSim"]   -> ["RenderWorld"],
    ["RenderWorld"] -> ["RenderUI"],
    ["RenderUI"]    -> ["PhysicsSim"]  // Loop
};

config.Permissions = new()
{
    ["PhysicsSim"]  = ReadWriteAll,
    ["AISim"]       = ReadWriteAll,
    ["CombatSim"]   = ReadWriteAll,
    ["RenderWorld"] = ReadOnly,
    ["RenderUI"]    = ReadOnly
};

repo.PhaseConfig = config;

// Use custom phases
repo.SetPhase(new Phase("PhysicsSim"));
// ... physics simulation
repo.SetPhase(new Phase("AISim"));
// ... AI logic
repo.SetPhase(new Phase("CombatSim"));
// ... combat resolution
```

---

### **Turn-Based Game**

```csharp
config.ValidTransitions = new()
{
    ["PlayerTurn"]  -> ["AITurn", "Render"],
    ["AITurn"]      -> ["PlayerTurn", "Render"],
    ["Render"]      -> ["PlayerTurn", "AITurn"]
};

config.Permissions = new()
{
    ["PlayerTurn"] = ReadWriteAll,
    ["AITurn"]     = ReadWriteAll,
    ["Render"]     = ReadOnly
};
```

---

## **Integration with EntityRepository**

```csharp
public class EntityRepository
{
    private Phase _currentPhase = Phase.Initialization;
    private PhasePermission _currentPhasePermission;  // CACHED for hot path
    private PhaseConfig _phaseConfig = PhaseConfig.Default;
    
    public PhaseConfig PhaseConfig
    {
        get => _phaseConfig;
        set
        {
            _phaseConfig = value;
            _phaseConfig?.BuildCache();  // Build ID caches
            UpdatePermissionCache();
        }
    }
    
    private void UpdatePermissionCache()
    {
        // HOT PATH: Use ID-based lookup
        if (_phaseConfig != null)
        {
            _currentPhasePermission = _phaseConfig.GetPermissionById(_currentPhase.Id);
        }
        else
        {
            _currentPhasePermission = PhasePermission.ReadWriteAll;
        }
    }
}
```

---

## **Performance Summary**

| Operation | Cost | Implementation |
|-----------|------|----------------|
| `ValidateWriteAccess` (HOT PATH) | ~2-3 cycles | Cached enum comparison |
| `SetPhase` transition check | O(1) | HashSet<int> lookup |
| Phase equality | O(1) | Integer ID comparison |
| Permission lookup | O(1) | Cached value |
| Config change | O(P) | Build ID cache (P = phase count) |

**Result:** Zero runtime overhead from string-based configuration!


---

## **Usage Examples**

### **Simple Game**
```csharp
repo.PhaseConfig = PhaseConfig.Relaxed;
// Do whatever you want, whenever you want
```

### **Networked Game**
```csharp
repo.PhaseConfig = PhaseConfig.Default;  // Strict enforcement
// Follow the defined phase flow
```

### **Complex MMO**
```csharp
var config = new PhaseConfig();
// Define 20+ custom phases for different systems
config.Permissions["WorldPhysics"] = ReadWriteAll;
config.Permissions["PlayerPhysics"] = OwnedOnly;
config.Permissions["NPCPhysics"] = ReadWriteAll;
config.Permissions["NetworkOut"] = ReadOnly;
// ... unlimited possibilities
```


---

## **8.2 Debug vs Release Behavior**

### **Question:** Is phase enforcement always on?

**Answer:** Yes, per user requirement:

```csharp
// NOT conditional
public ref T GetComponent<T>(Entity entity)
{
    AssertPhase(Phase.Simulation); // Always runs!
    // ...
}

// This is different from paranoid mode:
#if FDP_PARANOID_MODE
    if (index < 0) throw ... // Only in debug
#endif
```

**Rationale:**
- Phase violations are logic bugs, not performance issues
- Catching them in production prevents corruption
- Overhead is negligible (~1 integer comparison)

**Performance Impact:**
```csharp
// Worst case:
_currentPhase == Phase.Simulation  // 1 comparison
typeId < 256                        // 1 comparison  
AuthorityMask.IsSet(typeId)        // ~3 instructions

Total: ~5 instructions = ~1-2ns overhead
```

---

# **9. TKB & DIS Integration**

## **9.1 TKB Database Format**

### **Question:** What does a TKB file look like?

**Example TKB JSON:**
```json
{
  "templates": [
    {
      "name": "M1A2_Abrams",
      "dis": {
        "category": "Platform",
        "country": 225,
        "domain": "Land",
        "kind": 1,
        "extra": 1
      },
      "components": {
        "Position": { "X": 0, "Y": 0, "Z": 0 },
        "Health": { "Value": 100, "Max": 100 },
        "Armor": { "Front": 950, "Side": 600, "Rear": 250 }
      },
      "multiParts": {
        "WheelComponent": [
          { "Radius": 0.53, "Position": "FrontLeft" },
          { "Radius": 0.53, "Position": "FrontRight" },
          { "Radius": 0.53, "Position": "RearLeft" },
          { "Radius": 0.53, "Position": "RearRight" }
        ],
        "WeaponComponent": [
          { "Name": "M256_120mm", "Ammo": 40, "Type": "Main" },
          { "Name": "M2_50cal", "Ammo": 900, "Type": "Coax" }
        ]
      },
      "tags": ["IsVehicle", "IsPlatform"]
    }
  ]
}
```

**Loading:**
```csharp
public class TkbDatabase
{
    private Dictionary<string, EntityTemplate> _templates;
    private Dictionary<DISEntityType, string> _disToTemplate;
    
    public void LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<TkbData>(json);
        
        foreach (var template in data.Templates)
        {
            _templates[template.Name] = template;
            
            if (template.DIS != null)
            {
                var disType = new DISEntityType
                {
                    Category = template.DIS.Category,
                    Country = template.DIS.Country,
                    // ...
                };
                _disToTemplate[disType] = template.Name;
            }
        }
    }
}
```

---

## **9.2 Spawning from Templates**

### **Question:** How does Spawn() work exactly?

**Implementation:**
```csharp
public Entity Spawn(string templateName, EntityRepository repo)
{
    if (!_templates.TryGetValue(templateName, out var template))
        throw new ArgumentException($"Unknown template: {templateName}");
    
    // Create entity
    Entity e = repo.CreateEntity();
    
    // Add single-instance components
    foreach (var (typeName, data) in template.Components)
    {
        Type componentType = FindType(typeName);  // Reflection
        
        // Use reflection to call repo.GetComponent<T>()
        var getComponentMethod = typeof(EntityRepository)
            .GetMethod("GetComponent")
            .MakeGenericMethod(componentType);
        
        var componentRef = getComponentMethod.Invoke(repo, new object[] { e });
        
        // Deserialize data into component
        JsonSerializer.Populate(componentRef, data);
    }
    
    // Add multi-part components
    foreach (var (typeName, partsData) in template.MultiParts)
    {
        Type partType = FindType(typeName);
        
        // Deserialize parts array
        var parts = JsonSerializer.Deserialize(partsData, partType.MakeArrayType());
        
        // Call repo.SetParts<T>()
        var setPartsMethod = typeof(EntityRepository)
            .GetMethod("SetParts")
            .MakeGenericMethod(partType);
        
        setPartsMethod.Invoke(repo, new object[] { e, parts });
    }
    
    // Add tags
    foreach (var tagName in template.Tags)
    {
        Type tagType = FindType(tagName);
        
        var addTagMethod = typeof(EntityRepository)
            .GetMethod("AddTag")
            .MakeGenericMethod(tagType);
        
        addTagMethod.Invoke(repo, new object[] { e });
    }
    
    return e;
}
```

**Performance Note:**
- Uses reflection (slow!)
- Only used during entity creation (rare)
- Cache MethodInfo for performance if needed

---

## **9.3 DIS Entity Type Matching**

### **Question:** How do DIS queries work?

**DIS Entity Type:**
```csharp
public struct DISEntityType
{
    public byte Kind;       // 1 = Platform
    public byte Domain;     // 1 = Land, 2 = Air, 3 = Surface, 4 = Subsurface
    public ushort Country;  // 225 = USA
    public byte Category;   // Platform category
    public byte SubCategory;
    public byte Specific;
    public byte Extra;
}
```

**Stored in EntityHeader:**
```csharp
[StructLayout(LayoutKind.Sequential, Size = 96)]
public struct EntityHeader
{
    public BitMask256 ComponentMask;
    public BitMask256 AuthorityMask;
    public ushort Generation;
    public ushort Flags;
    
    // DIS type (uses reserved space)
    public DISEntityType DISType;  // 8 bytes
    
    private unsafe fixed byte _padding[16]; // Reduced padding
}
```

**Querying:**
```csharp
// Hierarchical iterator checks entity.DISType
public EntityView QueryDIS(DISCategory category)
{
    // Create query that checks DISType.Category == category
    return new EntityView(_entityIndex, new DISQuery { Category = category });
}

// Example
foreach (var e in repo.QueryDIS(DISCategory.Platform))
{
    // All platforms (tanks, planes, ships, etc.)
}

foreach (var e in repo.QueryDIS(DISCategory.Munition))
{
    // All munitions (bullets, missiles, etc.)
}
```

---

# **10. Performance Critical Paths**

## **10.1 Hot Path Analysis**

### **Question:** What are the most performance-critical operations?

**Ranking by frequency:**

| Operation | Calls/Frame | Time Budget | Critical? |
|-----------|-------------|-------------|-----------|
| GetComponent | ~1M | 5ms | ⭐⭐⭐⭐⭐ |
| Query iteration | ~100 | 10ms | ⭐⭐⭐⭐⭐ |
| Multi-part access | ~10K | 1ms | ⭐⭐⭐⭐ |
| IsAlive check | ~100K | 0.5ms | ⭐⭐⭐ |
| CreateEntity | ~100 | 0.1ms | ⭐⭐ |
| Destroy continuation | ~100 | 0.1ms | ⭐⭐ |

**Optimization priorities:**
1. **GetComponent** - Must be O(1), 1-5ns per call
2. **Iteration** - Chunk skipping essential, <10ms for 1M entities
3. **Multi-part** - Span-based, <20ns overhead
4. **Everything else** - Less critical

---

## **10.2 GetComponent Optimization**

### **Question:** How do we achieve <5ns GetComponent access?

**Breakdown:**
```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public ref T GetComponent<T>(Entity entity) where T : unmanaged
{
    // 1. Get type ID (JIT constant) - 0ns
    int typeId = ComponentType<T>.ID;
    
    // 2. Array bounds check (JIT may elide) - 0-1ns
    var table = (NativeChunkTable<T>)_componentTables[typeId];
    
    // 3. Direct index - 1-2ns
    return ref table[entity.Index];
}

// Inside NativeChunkTable:
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public ref T this[int entityId]
{
    get
    {
        int chunkIndex = entityId / _chunkCapacity;  // 1ns (fast div)
        int localIndex = entityId % _chunkCapacity;  // 1ns (fast mod)
        
        var chunk = _chunks[chunkIndex];  // 1ns (array access)
        return ref chunk[localIndex];     // 1ns (pointer arithmetic)
    }
}
```

**Total:** ~4-5ns (if chunk already allocated and JIT optimized)

**Verified by:**
```csharp
[Benchmark]
public float GetComponent_Hot()
{
    ref var pos = ref _repo.GetComponent<Position>(_entity);
    return pos.X;
}

// Expected: ~5ns per iteration
```

---

## **10.3 Iteration Optimization**

### **Question:** How to achieve <5ms for 1M entity iteration?

**Math:**
```
Target: 5ms for 1M entities
Per-entity budget: 5ns

Breakdown:
- Chunk skipping: Skip 900K entities in ~1ms (if 90% empty)
- Active entity check: 100K × 5ns = 0.5ms
- Query match (AVX2): 100K × 10ns = 1ms
- Component access: 100K × 5ns = 0.5ms
- User code: 100K × up to 20ns = 2ms

Total: ~5ms ✅
```

**Critical optimizations:**
1. **Chunk skipping** - Saves 90%+ time for sparse populations
2. **AVX2 matching** - 10x faster than scalar
3. **Inline everything** - Eliminates call overhead
4. **Span-based access** - Zero allocation

---

## **10.4 Cache Optimization**

### **Question:** How cache-friendly is the memory layout?

**Cache line utilization:**
```
L1 cache: 32KB per core
L2 cache: 256KB per core  
L3 cache: 8-32MB shared

EntityHeader (96 bytes):
- Fits in 2 cache lines (64 + 32)
- Prefetcher will load both
- Accessing ComponentMask loads AuthorityMask for free

Position component (12 bytes):
- 5 positions per cache line
- Iteration loads ~320 positions in L1 cache
- Excellent locality

Multi-part (contiguous):
- All wheels for one tank in 1-2 cache lines
- Prefetcher loads sequentially
- Much better than scattered pointers
```

**Worst case (cache-hostile):**
```csharp
// DON'T DO THIS
for (int i = 0; i < 1000; i++)
{
    // Random access pattern
    int randomEntity = Random.Next(0, 1000000);
    ref var pos = ref repo.GetComponent<Position>(entities[randomEntity]);
    // Cache miss every iteration!
}
```

**Best case (cache-friendly):**
```csharp
// DO THIS
foreach (var e in repo.Query(query))
{
    // Sequential access within chunks
    ref var pos = ref repo.GetComponent<Position>(e);
    // High cache hit rate
}
```

---

# **11. Testing Strategy Details**

## **11.1 Unit Test Structure**

### **Question:** How should I structure tests for each stage?

**Template:**
```csharp
public class Stage{N}_{FeatureName}Tests
{
    // Basic functionality
    [Fact]
    public void Feature_BasicCase_Works() { }
    
    // Edge cases
    [Fact]
    public void Feature_EmptyInput_HandlesCorrectly() { }
    
    [Fact]
    public void Feature_MaxCapacity_Works() { }
    
    // Error cases
    #if FDP_PARANOID_MODE
    [Fact]
    public void Feature_InvalidInput_Throws() { }
    #endif
    
    // Performance
    [Theory]
    [InlineData(100)]
    [InlineData(10000)]
    [InlineData(100000)]
    public void Feature_Scale_MeetsTargets(int count) { }
    
    // Thread safety (if applicable)
    [Fact]
    public void Feature_Concurrent_ThreadSafe() { }
    
    // Integration
    [Fact]
    public void Feature_WithOtherFeatures_WorksCorrectly() { }
}
```

---

## **11.2 Memory Leak Detection**

### **Question:** How do I verify no memory leaks?

**Pattern:**
```csharp
[Fact]
public void MemoryLeakTest_CreateDestroyLoop_NoLeaks()
{
    using var repo = new EntityRepository();
    repo.RegisterComponent<Position>();
    
    long before = GC.GetTotalMemory(forceFullCollection: true);
    
    // Create and destroy 100K entities, 100 times
    for (int iteration = 0; iteration < 100; iteration++)
    {
        var entities = new Entity[100_000];
        
        // Create
        for (int i = 0; i < 100_000; i++)
        {
            entities[i] = repo.CreateEntity();
            repo.GetComponent<Position>(entities[i]) = new Position { X = i };
        }
        
        // Destroy
        for (int i = 0; i < 100_000; i++)
        {
            repo.DestroyEntity(entities[i]);
        }
        
        // Force GC
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
    
    long after = GC.GetTotalMemory(forceFullCollection: true);
    long leaked = after - before;
    
    // Allow 1MB tolerance (GC overhead)
    Assert.True(leaked < 1024 * 1024, 
        $"Memory leaked: {leaked / 1024}KB");
}
```

---

## **11.3 Performance Regression Tests**

### **Question:** How do I catch performance regressions?

**Benchmark comparison:**
```csharp
// Step 1: Establish baseline
[Benchmark(Baseline = true)]
public void Iteration_1M_Baseline()
{
    var query = new EntityQuery().With<Position>();
    
    int count = 0;
    foreach (var e in _repo.Query(query))
    {
        ref var pos = ref _repo.GetComponent<Position>(e);
        pos.X += 1.0f;
        count++;
    }
}

// Step 2: Run regularly, compare to baseline
/* Expected output:
Method                    | Mean     | Ratio
------------------------- | -------- | -----
Iteration_1M_Baseline     | 4.823 ms | 1.00
Iteration_1M_CurrentImpl  | 4.891 ms | 1.01  ✅ <5% regression OK
Iteration_1M_SlowImpl     | 9.234 ms | 1.91  ❌ >10% regression BAD!
*/
```

**Automated check:**
```csharp
[Fact]
public void Performance_Iteration_MeetsTarget()
{
    using var repo = CreateRepoWith1MEntities();
    
    var sw = Stopwatch.StartNew();
    
    var query = new EntityQuery().With<Position>();
    foreach (var e in repo.Query(query))
    {
        ref var pos = ref repo.GetComponent<Position>(e);
        pos.X += 1.0f;
    }
    
    sw.Stop();
    
    // Assert < 10ms (with headroom for slow CI servers)
    Assert.True(sw.ElapsedMilliseconds < 10,
        $"Iteration took {sw.ElapsedMilliseconds}ms (target: <10ms)");
}
```

---

# **12. Common Pitfalls & Solutions**
13. [Delta Tracking & Versioning](#13-delta-tracking--versioning)
14. [Hierarchy Iteration](#14-hierarchy-iteration)
15. [Explicit Registration Policy](#15-explicit-registration-policy)


## **12.1 Pitfall: Modifying During Iteration**

**Problem:**
```csharp
foreach (var e in repo.Query(query))
{
    if (ShouldDestroy(e))
        repo.DestroyEntity(e); // CRASHES OR SKIPS ENTITIES!
}
```

**Why it fails:**
```csharp
// Iterator maintains state:
while (++_currentId <= _maxId)
{
    ref var header = ref _index.GetHeader(_currentId);
    
    if (!header.IsActive) // We just set this to false!
        continue; // Skips to next, but _currentId already incremented
}
```

**Solution:**
```csharp
var ecb = new EntityCommandBuffer();

foreach (var e in repo.Query(query))
{
    if (ShouldDestroy(e))
        ecb.DestroyEntity(e);
}

ecb.Playback(repo); // All at once, after iteration
```

---

## **12.2 Pitfall: Assuming Stable Multi-Part Indices**

**Problem:**
```csharp
Span<Wheel> wheels = repo.GetParts<Wheel>(vehicle);

int frontLeftIndex = 0;
wheels[frontLeftIndex].Rotation = 45;

// Later...
repo.RemovePart<Wheel>(vehicle, 2); // Removed rear left

// Now frontLeftIndex MAY STILL BE 0, but wheel[0] might be different!
```

**Solution:**
```csharp
struct Wheel
{
    public WheelPosition Position; // FL, FR, RL, RR
    public float Rotation;
}

Span<Wheel> wheels = repo.GetParts<Wheel>(vehicle);

for (int i = 0; i < wheels.Length; i++)
{
    if (wheels[i].Position == WheelPosition.FrontLeft)
    {
        wheels[i].Rotation = 45;
        break;
    }
}
```

---

## **12.3 Pitfall: Forgetting Component Registration**

**Problem:**
```csharp
var repo = new EntityRepository();
// Forgot: repo.RegisterComponent<Position>();

var e = repo.CreateEntity();
ref var pos = ref repo.GetComponent<Position>(e); // NULL REFERENCE!
```

**Solution:** Centralized registration
```csharp
public static class GameComponents
{
    public static void RegisterAll(EntityRepository repo)
    {
        // All components in one place
        repo.RegisterComponent<Position>();
        repo.RegisterComponent<Velocity>();
        repo.RegisterComponent<Health>();
        repo.RegisterMultiPart<WheelComponent>();
        repo.RegisterTag<IsStatic>();
        
        // If you forget here, it won't compile (reference error)
    }
}

// At startup:
var repo = new EntityRepository();
GameComponents.RegisterAll(repo);
```

---

## **12.4 Pitfall: Phase Violations**

**Problem:**
```csharp
repo.SetPhase(Phase.Presentation);

// In render code:
ref var pos = ref repo.GetComponent<Position>(entity);
pos.X = 100; // WrongPhaseException!
```

**Solution:** Use read-only access patterns
```csharp
// Option 1: Direct field access (no setter)
public readonly ref struct ReadOnlyComponentRef<T> where T : unmanaged
{
    private readonly ref T _value;
    
    public T Value => _value; // Read-only
    
    internal ReadOnlyComponentRef(ref T value)
    {
        _value = ref value;
    }
}

// Option 2: Check phase before writing
if (repo.CurrentPhase == Phase.Simulation)
{
    ref var pos = ref repo.GetComponent<Position>(entity);
    pos.X = 100; // OK
}
else
{
    // Read-only access
    Position pos = repo.GetComponent<Position>(entity);
    Console.WriteLine(pos.X);
}
```

---

## **12.5 Pitfall: Component Size Exceeds Chunk**

**Problem:**
```csharp
public struct HugeComponent
{
    public unsafe fixed float BigArray[100000]; // 400KB!
}

repo.RegisterComponent<HugeComponent>(); // THROWS!
```

**Error:**
```
InvalidOperationException: Component HugeComponent (400000 bytes) 
exceeds chunk size (65536 bytes)
```

**Solution:** Use multi-part or managed component
```csharp
// Option 1: Multi-part (if variable size)
public struct ArrayElement
{
    public float Value;
}

repo.RegisterMultiPart<ArrayElement>();
// Can have up to 65536 elements

// Option 2: Managed component (if fixed large size)
public class HugeData
{
    public float[] BigArray = new float[100000];
}

repo.RegisterManagedComponent<HugeData>();
```

---

# **Summary: Critical Implementation Checklist**

Before writing code, confirm understanding of:

## **Memory Model**
- [ ] Virtual memory reserve vs commit
- [ ] Chunk capacity calculation
- [ ] Lazy chunk allocation
- [ ] Alignment requirements (EntityHeader = 96 bytes)

## **Entity Lifecycle**
- [ ] Generation wraparound behavior
- [ ] ABA problem mitigation
- [ ] Thread-safe creation/destruction
- [ ] Destruction during iteration (use ECB!)

## **Component System**
- [ ] Type ID assignment order
- [ ] Tag detection (empty struct = size 1)
- [ ] Component table polymorphism
- [ ] Registration order for serialization

## **Multi-Part Descriptors**
- [ ] Contiguous memory layout
- [ ] Growth strategy (2x capacity)
- [ ] Swap-with-last removal
- [ ] No automatic memory reclaim
- [ ] Thread safety (partition entities)

## **Queries & Iteration**
- [ ] BitMask256.Matches algorithm
- [ ] Chunk skipping optimization
- [ ] Iteration stability (snapshot)
- [ ] Never modify during iteration

## **Threading Model**
- [ ] What's thread-safe (CreateEntity has lock)
- [ ] What's NOT (GetComponent)
- [ ] EntityCommandBuffer pattern
- [ ] False sharing prevention

## **Network Authority**
- [ ] ComponentMask vs AuthorityMask
- [ ] Partial ownership support
- [ ] Phase-based enforcement
- [ ] WithOwned() queries

## **Performance**
- [ ] GetComponent <5ns target
- [ ] Iteration <5ms for 1M entities  
- [ ] Multi-part <20ns access
- [ ] Cache-friendly access patterns

## **Testing**
- [ ] Per-stage test structure
- [ ] Memory leak detection
- [ ] Performance regression checks
- [ ] Common pitfall coverage

---

# **13. Delta Tracking & Versioning**

## **13.1 Chunk-Based Versioning**

To achieve high performance delta tracking ("Entities changed since X"), we use **Chunk Versioning** instead of per-entity dirty flags. This minimizes metadata overhead and allows skipping entire chunks of unchanged entities.

### **The Mechanism**
1. **Global Version:** The `EntityRepository` maintains a `GlobalVersion` (uint) which increments on every `Tick()`.
2. **Chunk Version:** Each `NativeChunk` stores a `Version`.
3. **Write Access:** When you request `GetComponentRW<T>(e)`, the engine updates the `Version` of the chunk containing `e` to the current `GlobalVersion`.
4. **Read Access:** `GetComponentRO<T>(e)` does **not** update the version.
5. **Structural Changes:** Adding/Removing components updates the **EntityHeader**'s `LastChangeTick`, handling structural deltas.

### **Usage Example**

```csharp
// 1. Tick the repository at start of frame
repo.Tick(); 

// 2. Systems modify data using RW access
// This marks the chunk as "Modified at CurrentVersion"
ref var pos = ref repo.GetComponentRW<Position>(entity);
pos.X += 1; 

// 3. Query for changes since last run
uint lastRunVersion = 5; // Saved from previous frame
repo.QueryDelta(query, lastRunVersion, entity => 
{
    // Only visits entities that:
    // a) Structurally changed (Header.LastChangeTick > lastRunVersion)
    // b) OR Component Value changed (Chunk.Version > lastRunVersion)
});
```

## **13.2 Read-Only vs Read-Write**

It is **critical** to use the correct accessor to avoid false positives in delta tracking:

*   `GetComponentRW<T>(e)`: Use when you intend to modify data. **Marks Chunk Dirty.**
*   `GetComponentRO<T>(e)`: Use when reading data. **No Side Effect.**

## **13.3 Strategy for Tier 2/Network Data (Managed Structs)**

**Use Case:** You have infrequently changing data (e.g., `Inventory`, `NetworkState`) and need precise updates to minimize bandwidth.

**Problem:** Chunk versioning has "False Positives" (entire chunk marked dirty if one entity changes). Using per-entity dirty flags is memory-expensive and hurts cache locality.

**Recommended Solution: "Broad Phase + Value Diffing"**
1.  **Broad Phase (QueryDelta):** Use the high-speed `QueryDelta` to efficiently skip 99% of chunks that haven't changed at all.
2.  **Narrow Phase (Shadow Copy):** For the remaining "Candidate Entities," perform a **Bitwise Comparison** (or semantic check) against a locally cached "Last Sent" state.

**Why this is better:**
*   **Memory:** Saves significant RAM by avoiding per-entity version integers.
*   **Bandwidth:** Detects *actual* value changes (shadow copy comparison), ignoring "logical" writes where the value remains identical (e.g. `Health = Clamp(Health)`).
*   **Speed:** Comparing unmanaged structs is extremely fast—often faster than managing complex flag logic during the simulation loop.

---

# **14. Hierarchy Iteration**

## **14.1 HierarchyNode Component**

Hierarchy is implemented as an intrusive doubly-linked list component (`HierarchyNode`). 
This provides O(1) insertion/removal and O(N) iteration without external map lookups.

### **Structure**
```csharp
struct HierarchyNode
{
    Entity Parent;
    Entity FirstChild;
    Entity PreviousSibling;
    Entity NextSibling;
}
```

## **14.2 Iterating Children**

Use `HierarchyExtensions.GetChildren(parent)` to iterate. This yields an enumerator that traverses the sibling chain.

**Example:**
```csharp
// Create hierarchy
repo.AddChild(parent, child1);
repo.AddChild(parent, child2);

// Iterate
foreach (var child in repo.GetChildren(parent))
{
    // child1, child2
}

// Low-level traversal (for performance/recursion)
ref var node = ref repo.GetComponentRO<HierarchyNode>(parent);
Entity current = node.FirstChild;
while (current != Entity.Null)
{
    // Process current...
    
    // Move to next
    ref var childNode = ref repo.GetComponentRO<HierarchyNode>(current);
    current = childNode.NextSibling;
}
```

```

---

# **15. Explicit Registration Policy**

## **15.1 The Rule: No Lazy Registration**

**Auto-registration is strictly FORBIDDEN in production code.** 
While the engine supports lazy registration (via `ComponentTypeRegistry`) for prototyping, production systems must enforce explicit registration.

### **Why?**
1.  **Memory Layout:** Ensures component tables are allocated contiguously and at predictable times (Startup) rather than random lazy times (Combat).
2.  **Thread Safety:** Lazy registration involves locks (`ComponentTypeRegistry`). Explicit registration happens single-threaded at startup, allowing lock-free reads later (future optimization).
3.  **Determinism:** ID assignment order is deterministic only if registration order is deterministic.

## **15.2 Implementation**

Your Application Composition Root (e.g., `Game.cs`) must register ALL components before any simulation begins.

```csharp
// ❌ BAD: Implicit registration
// If this is the first use, it triggers locks and allocation!
repo.AddComponent(e, new Position()); 

// ✅ GOOD: Explicit Registration Phase
public void Initialize(EntityRepository repo)
{
    // Register all types upfront
    repo.RegisterComponent<Position>();
    repo.RegisterComponent<Velocity>();
    repo.RegisterComponent<Damage>();
    
    // Now the simulation can run lock-free
}
```

**Enforcement:**
The `EntityRepository` enforces this rule strictly. All methods that access component storage (`AddComponent`, `GetComponent`, `Query`, etc.) will **throw an `InvalidOperationException`** if the component type has not been explicitly registered via `RegisterComponent<T>()`. This eliminates "lazy registration" bugs.

**You are now ready to implement!** Every edge case, design decision, and potential pitfall has been documented. Start with Stage 1 and proceed sequentially. 🚀

---

# **16. Time-Sliced Iterator & Determinism**

## **16.1 The Problem: Determinism vs. Wall Clock**
Standard time-sliced iterators use `Stopwatch` (Wall Clock) to limit execution time per frame. This is crucial for maintaining frame rates in UI/Render loops but is **non-deterministic** for simulation logic (CPU speed affects how many entities are processed).

## **16.2 The Solution: TimeSliceMetric**
The `TimeSlicedIterator` supports two modes via `EntityRepository.DefaultTimeSliceMetric`:

1.  **`TimeSliceMetric.WallClockTime` (Default)**
    *   **Behavior:** The `budget` parameter represents **Milliseconds**.
    *   **Use Case:** Rendering, UI updates, Asset loading, Non-critical background tasks.
    *   **Pros:** Prevents frame spikes.
    *   **Cons:** Non-deterministic execution path.

2.  **`TimeSliceMetric.EntityCount`**
    *   **Behavior:** The `budget` parameter represents **Number of Entities**.
    *   **Use Case:** Deterministic Simulation (Replay, Server), "Amortized" simulation updates.
    *   **Pros:** 100% Deterministic. "Entity Count" acts as the unit of work.
    *   **Cons:** Processing time may vary per frame (potentially missing frame deadlines).

## **16.3 Runtime Switching**
You can switch the metric dynamically. The iterator code at the call site does **not** need to change implementation; it simply delegates to `EntityRepository.DefaultTimeSliceMetric`.

```csharp
// Scenario: Deterministic Replay
repo.DefaultTimeSliceMetric = TimeSliceMetric.EntityCount;

// Loop runs, treating budget as 'Entities'
// Note: Ensure your budget value is appropriate for the mode (e.g., 500 entities)
repo.QueryTimeSliced(query, state, 500, e => { ... });

// ... Replay Finished -> Switch to Live ...

// Scenario: Real-time 
repo.DefaultTimeSliceMetric = TimeSliceMetric.WallClockTime;

// Loop continues, treating budget as 'Milliseconds'
// Note: Ensure your budget value is appropriate (e.g., 5.0 ms)
repo.QueryTimeSliced(query, state, 5.0, e => { ... }); 
```

This allows the **processing logic** (the method calling `QueryTimeSliced`) to remain agnostic of the simulation environment (Server vs Client vs Replay), provided global configuration is set correctly at the Composition Root.

---

# **13. Entity Command Buffers (ECB) - Stage 17**

## **13.1 Reference Semantics for Managed Components**

### **Critical Design Decision: No Automatic Cloning**

**EntityCommandBuffer** stores managed components (Tier 2) by **reference**, not by copying. This is a deliberate design choice to maintain consistency with direct `EntityRepository` usage.

### **Why No Cloning?**

1. **Consistency**: `repo.AddManagedComponent(e, obj)` stores by reference
2. **Simplicity**: No forced interface constraints (`ICloneable`, `IDeepCloneable`)
3. **Natural Usage**: Users already create fresh objects in normal code

### **The Rule: Always Pass Fresh Objects**

 **CORRECT Usage:**
``csharp
// Each entity gets its own object instance
ecb.AddManagedComponent(e1, new PlayerName { Name = "Hero", Level = 5 });
ecb.AddManagedComponent(e2, new PlayerName { Name = "Villain", Level = 10 });
ecb.AddManagedComponent(e3, new PlayerName { Name = "NPC", Level = 1 });
``

 **INCORRECT Usage:**
``csharp
// Shared reference - all entities will reference THE SAME object!
var sharedName = new PlayerName { Name = "Hero", Level = 5 };
ecb.AddManagedComponent(e1, sharedName); // e1 points to sharedName
ecb.AddManagedComponent(e2, sharedName); // e2 ALSO points to sharedName
ecb.AddManagedComponent(e3, sharedName); // e3 ALSO points to sharedName

// This is YOUR BUG, same as:
repo.AddManagedComponent(e1, sharedName);
repo.AddManagedComponent(e2, sharedName); // Weird, but allowed
``

 **DANGEROUS: Mutation After Recording:**
``csharp
var playerData = new PlayerName { Name = "Hero", Level = 5 };
ecb.AddManagedComponent(entity, playerData);

// Mutation AFTER recording but BEFORE playback
playerData.Level = 99; //  Changes what will be played back!

ecb.Playback(repo); // Plays back Level = 99, not 5!
``

### **Comparison: Unmanaged vs Managed**

| Aspect | Unmanaged (Tier 1) | Managed (Tier 2) |
|--------|-------------------|------------------|
| Storage | **Byte copy** into ECB buffer | **Reference** in object list |
| Semantics | Value semantics | Reference semantics |
| Mutation safety |  Safe (snapshot taken) |  User responsibility |
| Thread safety |  Independent copy |  Don't share across threads |
| Natural pattern | `new Position { X=10 }` | `new PlayerName { ... }` |

### **Why This Design Works**

In real-world usage, you naturally create fresh objects:

``csharp
// Parallel job processing network messages
void ProcessNetworkMessage(Message msg, EntityCommandBuffer ecb)
{
    // You naturally deserialize to NEW objects
    var playerData = JsonSerializer.Deserialize<PlayerName>(msg.Payload);
    var entity = FindEntityById(msg.EntityId);
    
    ecb.AddManagedComponent(entity, playerData); //  Fresh object
}
``

You **don't** typically do this:
``csharp
// Weird anti-pattern (nobody writes this)
var sharedState = new PlayerName();
for (int i = 0; i < 100; i++)
{
    ecb.AddManagedComponent(entities[i], sharedState); // Why?!
}
``

### **13.2 Thread Safety Model**

**Recording (Per-Buffer):**
- Each thread gets its **own** `EntityCommandBuffer`
- Thread-safe: no shared state during recording
- Each buffer accumulates commands independently

**Playback (Main Thread Only):**
- All buffers play back **sequentially** on the main thread
- After sync point (e.g., end of parallel phase)
- Structural changes applied to `EntityRepository`

**Example:**
``csharp
// Parallel simulation phase
var ecbs = new EntityCommandBuffer[threadCount];
for (int i = 0; i < threadCount; i++)
    ecbs[i] = new EntityCommandBuffer();

Parallel.For(0, entityCount, i =>
{
    int threadId = Thread.CurrentThread.ManagedThreadId % threadCount;
    var ecb = ecbs[threadId];
    
    // Each thread records to its own buffer
    ecb.AddManagedComponent(entities[i], new PlayerAI { ... });
});

// Sync point: Playback on main thread
foreach (var ecb in ecbs)
{
    ecb.Playback(repo); // Sequential playback
    ecb.Dispose();
}
``

### **13.3 When to Use ECB**

**Use Cases:**
1. **Parallel Systems**: Defer structural changes until sync point
2. **During Iteration**: Avoid modifying while iterating queries
3. **Batching**: Group many changes for efficient playback

**Don't Use For:**
- Single-threaded immediate changes (use repo directly)
- Simple one-off mutations (overhead not worth it)

---

# 20. System API & Scheduling (Stage 19+)

## **20.1 Feature Overview**

The System API provides a structured way to define game logic, manage dependencies, and execute updates in a deterministic order.

### **Key Components**

1.  **`ComponentSystem` (Base Class)**
    *   **Encapsulation:** Hides internal wiring (`World`, `Enabled` state) from user logic.
    *   **Lifecycle:** `OnCreate` (init), `OnUpdate` (loop), `OnDestroy` (cleanup).
    *   **Safety:** User methods are `protected` to enforce engine control.

2.  **`SystemGroup` (Container)**
    *   **Composite Pattern:** Is a `ComponentSystem` itself.
    *   **Dependency Resolution:** Automatically performs topological sorting based on attributes.
    *   **Error Isolation:** Wraps individual system updates in `try-catch` to prevent total engine failure.

3.  **Attributes (Scheduling)**
    *   `[UpdateBefore(typeof(OtherSystem))]`
    *   `[UpdateAfter(typeof(OtherSystem))]`
    *   `[UpdateInGroup(typeof(MyGroup))]`

---

## **20.2 Performance Characteristics**

This system is built for **high performance** games:

*   **Zero Allocation Loop:** The `OnUpdate` iteration uses a pre-allocated `List<ComponentSystem>` and no LINQ/delegates.
*   **Lazy Sorting:** Dependency graph sorting ($O(V+E)$) only happens when the system list changes (startup/reconfig), never during the frame loop.
*   **Minimal Overhead:** Calling a system adds only a virtual method dispatch overhead (nanoseconds).
*   **Static Reflection:** Reflection is used *only* during the sorting phase, never at runtime.

---

## **20.3 Common Questions & Clarifications**

### **Q1: Can I make a component derive from `ComponentSystem`?**
**Answer: NO.**
*   **Components (Structs/Classes):** Pure data. No logic. Store state (Position, Health).
*   **Systems (Classes):** Pure logic. Derive from `ComponentSystem`. Operate on data.

Combining them breaks the ECS pattern and will not work with the engine's scheduling.

### **Q2: Can I execute systems manually?**
**Answer: Sort of.**
You cannot call `mySystem.OnUpdate()` directly because it is `protected`.
**Recommended Pattern:** Extract logic into a `static` utility class.

```csharp
// 1. Core Logic (Shared)
// This logic is pure and can be called by anyone (Tests, Engine, Custom Events)
public static class PhysicsLogic 
{
    public static void ResolveCollisions(EntityRepository repo, float deltaTime) 
    {
        // ... Pure logic ...
    }
}

// 2. System (Engine-Scheduled)
// This wrapper lets the engine schedule and run the logic automatically
public class PhysicsSystem : ComponentSystem 
{
    protected override void OnUpdate() 
    {
        // Engine provides the World and timing
        PhysicsLogic.ResolveCollisions(World, 0.016f);
    }
}

// 3. Manual Call (User-Scheduled)
// You can call the same logic manually whenever you want!
public void MyCustomEvent(EntityRepository repo) 
{ 
    // Manual invocation without waiting for the engine update loop
    PhysicsLogic.ResolveCollisions(repo, 0.016f); 
}
```

### **Q3: What if I don't register ANY systems?**
**Answer:** The engine works as a **passive in-memory database**.
*   You can still `CreateEntity`, `AddComponent`, and `Query`.
*   Data remains static until you manually modify it.
*   **Performance:** Zero loop overhead (empty list check is instant).

---

## **20.4 Usage Examples**

### **Defining a System**
```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(MovementSystem))]
public class DamageSystem : ComponentSystem
{
    private Query _query;

    protected override void OnCreate()
    {
        // Cache query once
        _query = new QueryBuilder()
            .With<Health>()
            .With<DamageEvent>()
            .Build();
    }

    protected override void OnUpdate()
    {
        // Iterate entities
        foreach (var entity in World.Query(_query))
        {
            ref var hp = ref World.GetComponent<Health>(entity);
            ref var dmg = ref World.GetComponent<DamageEvent>(entity);
            
            hp.Current -= dmg.Amount;
        }
    }
}
```

### **Creating a Hierarchy**
```csharp
// 1. Define Groups
public class InitializationGroup : SystemGroup { }
public class SimulationGroup : SystemGroup { }
public class PresentationGroup : SystemGroup { }

// 2. Setup World
var world = new EntityRepository();
var rootGroup = new SystemGroup();
rootGroup.InternalCreate(world);

var init = new InitializationGroup();
var sim = new SimulationGroup();
var pres = new PresentationGroup();

rootGroup.AddSystem(init);
rootGroup.AddSystem(sim);
rootGroup.AddSystem(pres);

// 3. Add Systems
sim.AddSystem(new MovementSystem());
sim.AddSystem(new DamageSystem()); // Will sort based on attributes

// 4. Game Loop
while (running)
{
    rootGroup.InternalUpdate(); // Updates Init -> Sim -> Pres
}
```

# FastDataPlane Serialization Design

## Overview

The serialization system in FastDataPlane (Fdp.Kernel) provides a robust, high-performance mechanism for saving and loading the entire state of an `EntityRepository`. It is designed to be **tolerant to schema changes** and **insensitive to runtime execution order**, ensuring that save files remain valid even as the codebase evolves.

The system utilizes **MessagePack-CSharp** as the underlying serialization format, leveraging its speed, compact binary size, and built-in support for schema evolution.

## Key Features

1.  **Robust Component Identification**:
    *   Components are mapped by their **Assembly-Qualified Name** (or Full Name), not by volatile runtime integer IDs.
    *   This ensures that changing the order of `RegisterComponent<T>()` calls in your application startup does **not** break save files.

2.  **Tolerant Reading ("Future-Proofing")**:
    *   **Missing Types**: If a save file contains data for a component type that no longer exists in the codebase (e.g., an old feature was removed), the serializer gracefully **skips** that data block without crashing.
    *   **New Types**: If the codebase has new component types not present in the save file, they simply remain uninitialized (or default) for loaded entities.
    *   **Schema Evolution**: By using MessagePack's integer keys (`[Key(0)]`, `[Key(1)]`), you can add fields to your component structs without breaking old saves.

3.  **Full State Restoration**:
    *   **Entities**: Restores Entity IDs, Generations, and Active states.
    *   **Unmanaged Components**: Efficiently serializes high-performance struct arrays.
    *   **Managed Components**: Supports standard C# classes and reference types.
    *   **Entity Index**: Reconstructs the internal free-list and version counters to ensure safe `CreateEntity` calls after loading.

4.  **Performance**:
    *   Uses **LZ4 compression** by default for efficient disk usage.
    *   Component data is serialized in bulk (column-oriented), optimizing compression ratios for homogeneous data.

## Architecture

A save file is structured as a hierarchical `MessagePackObject`:

### 1. `SaveFileRoot`
The top-level container:
*   **FileVersion**: `int` (Current: 2) - For checking compatibility.
*   **Entities**: `List<EntitySaveData>` - The structural backbone.
*   **ComponentBlobs**: `Dictionary<string, byte[]>` - The payload.
    *   **Key**: The component's Type Name (`Type.FullName`).
    *   **Value**: A raw byte array containing the serialized specific component data.

### 2. Entity Data
*   **Id**: The unique index.
*   **Generation**: The generation version of the entity (crucial for weak references).
*   **IsActive**: Whether the entity is currently in use.

### 3. Component Data
Each `ComponentBlob` is deserialized individually. The format is a generic list of pairs:
*   `List<EntityComponentPair<T>>`
    *   **EntityId**: Which entity owns this data.
    *   **Value**: The component instance (`T`).

## Requirements for Components

To be serializable, your components must be compatible with MessagePack-CSharp.

### Unmanaged (Struct) Components
Annotate your structs with `[MessagePackObject]` and `[Key]`:

```csharp
using MessagePack;

[MessagePackObject]
public struct Position
{
    [Key(0)] public float X;
    [Key(1)] public float Y;
    
    // New field added later - safe!
    [Key(2)] public float Z; 
}
```

### Managed (Class) Components
Classes follow the same pattern:

```csharp
[MessagePackObject]
public class Inventory
{
    [Key(0)] public List<string> Items;
    [Key(1)] public int Gold;
}
```

### Limitations
1.  **Type Renaming**: Since data is keyed by `Type.FullName`, renaming a class/struct will cause the serializer to treat it as a "missing type" and skip loading its old data. You must implement a migration strategy (or manual name mapping) if you rename core types.
2.  **No Constructor Injection**: MessagePack deserializers typically require a parameterless constructor or annotated constructor. For simple structs, this is automatic.
3.  **Generics**: Generic components are supported, but their serialized Type Name will be specific (e.g., `MyType`1[[System.Int32...]]`). Changing generic arguments breaks compatibility.

## API Usage

Accessed via `Fdp.Kernel.Serialization.RepositorySerializer`.

### Saving
```csharp
var repo = new EntityRepository();
// ... populate repo ...

// Save to file
RepositorySerializer.SaveToFile(repo, "savegame.dat");

// Or to stream
using var ms = new MemoryStream();
RepositorySerializer.SaveToStream(repo, ms);
```

### Loading
**Warning**: Loading **replaces** the current state of the repository. It is best practice to load into a fresh or cleared repository.

```csharp
var repo = new EntityRepository();

// Load from file
RepositorySerializer.LoadFromFile(repo, "savegame.dat");
```

## Best Practices

1.  **Always use `[Key]`**: Do not rely on `[Key("string")]` unless necessary; integer keys are smaller and faster.
2.  **Don't Change Key Indices**: If you deprecate a field `[Key(1)]`, do not reuse `1` for a new property. Use `[Key(2)]` and leave `1` obsolete or remove it (but never renumber).
3.  **Clear Before Load**: While `LoadFromStream` calls `Clear()` internally, it is cleaner to discard the old `EntityRepository` and create a `new` one if possible to ensure zero memory fragmentation.


# FDP Optimized Query & Filtering Features

This document describes the high-performance query and filtering capabilities implemented in the Fast Data Plane (FDP) kernel. These features are designed to maximize throughput for large-scale entity selections using hardware acceleration and memory-layout optimizations.

---

## 1. SIMD-Accelerated Component Querying

The core component matching logic (`BitMask256::Matches`) has been optimized to use **AVX2 (Advanced Vector Extensions 2)** instructions.

### Mechanism
Instead of checking 4 `ulong` segments sequentially with conditional branches, the engine loads the entire 256-bit mask into a single CPU register (`ymm`) and compares it against the query requirements in a consistent, branchless stream of instructions.

**Logic Applied:**
```csharp
// Vectorized Logic (Pseudo-code)
hasAll = (target & include) == include;
hasNone = (target & exclude) == 0;
result = MoveMask(hasAll & hasNone) == -1; // -1 means all bits true
```

### Benefits
*   **Branchless Execution**: Removes CPU pipeline stalls caused by branch misprediction in standard `if/else` ladders.
*   **Throughput**: Processes all 256 component slots in parallel.
*   **Safety**: Uses `Unsafe.As` + `Vector256.LoadUnsafe` (`vmovdqu`) to safely handle unaligned stack memory, preventing access violations common with standard AVX loads (`vmovdqa`).

### Requirements
*   **Hardware**: CPU with AVX2 support (Intel Haswell / AMD Excavator or newer).
*   **Software**: .NET 8.0+ Runtime.
*   **Fallback**: The system automatically detects hardware support at startup via JIT constants. If AVX2 is unavailable, it gracefully falls back to the scalar implementation.

### Usage
This feature is **transparent & automatic**. No API changes are required. Any call to `EntityQuery.Matches` or `BitMask256.Matches` will utilize the optimized path if available.

---

## 2. High-Performance DIS Filtering

The engine supports native filtering of entities based on the **Distributed Interactive Simulation (DIS)** 7-field type system (Kind, Domain, Country, Category, etc.). This is optimized to be as fast as a primitive integer comparison.

### Mechanism: "Mask & Match"
The 7 DIS fields are packed into a single 64-bit integer (`ulong`). Relational filters are replaced by bitwise masks.

**Struct Layout (`DISEntityType`):**
A C# explicit struct overlays the 7 byte/short fields on top of a single 8-byte `Value`.

| Field       | Size     | Offset | Description                   |
|-------------|----------|--------|-------------------------------|
| `Kind`      | 1 Byte   | 7      | Platform, Munition, LifeForm  |
| `Domain`    | 1 Byte   | 6      | Land, Air, Surface, Space     |
| `Country`   | 2 Bytes  | 4      | 16-bit Country Code           |
| `Category`  | 1 Byte   | 3      | e.g., Tank vs Truck           |
| `Subcat`    | 1 Byte   | 2      | e.g., M1A1 vs T-72            |
| `Specific`  | 1 Byte   | 1      | Variant                       |
| `Extra`     | 1 Byte   | 0      | Extra data                    |
| **`Value`** | **8 Bytes**| **0**| **The merged 64-bit integer** |

### Usage Examples

#### A. Setting an Entity's Type
```csharp
var f16Type = new DISEntityType 
{ 
    Kind = 1,       // Platform
    Domain = 2,     // Air
    Country = 225,  // USA
    Category = 50   // Fighter
};

// Extremely fast: writes 1 ulong to the header
repo.SetDisType(entity, f16Type); 
```

#### B. Filtering by Broad Category (e.g., "All Air Platforms")
You want everything where `Kind == Platform` AND `Domain == Air`. You don't care about Country or Category.

```csharp
// 1. Define the target values
var target = new DISEntityType 
{ 
    Kind = 1, 
    Domain = 2 
};

// 2. Define the Mask (set bits to FF for fields to check, 00 to ignore)
// You can use the struct to build the mask clearly:
var maskStruct = new DISEntityType 
{ 
    Kind = 0xFF, 
    Domain = 0xFF 
    // All other fields are 0
};

// 3. Build Query
var query = repo.Query()
    .WithDisType(target, maskStruct.Value) 
    .Build();
```

#### C. Exact Match
To find a specific entity type (e.g., "M1A1 Tank"), check all bits.

```csharp
ulong fullMask = 0xFFFF_FFFF_FFFF_FFFF;
repo.Query().WithDisType(m1a1Type, fullMask).Build();
```

### Integration & Performance
*   **Storage**: The `DISEntityType` is stored directly in the `EntityHeader` (access cost: 0 cache misses if header is already loaded). It reuses previously wasted padding bytes.
*   **Query Cost**: 1 CPU instruction (`AND` + `CMP`) per entity.
*   **Component Query Compatible**: Can be combined with standard component queries (e.g., `With<Position>().WithDisType(...)`).

### Serialization
*   The `DisType` is fully serialized/deserialized via `RepositorySerializer`.
*   It is persisted as a raw `ulong` in the `EntitySaveData` structure (Key 3).
*   **Benefit**: Full state restoration of simulation scenarios.

---

## Limitations & Best Practices

1.  **Endianness**: The `StructLayout` assumes Little Endian (standard for Windows/x64). If porting to Big Endian systems, the internal offsets of `Kind`/`Domain` within the `Value` would flip. The C# code uses explicit offsets, so the *fields* work, but constructed `ulong` masks might need adjustment if doing raw hex manipulation. Using the struct-based mask construction (as shown in Examble B) is safe.
2.  **Unique Types**: This system supports exactly one DIS type per entity. If an entity represents an aggregate or complex object, use the primary type.
3.  **Zero Value**: The default value is `0` (Kind=0, Domain=0 ...). Ensure you initialize entities if queries depend on filtering active units.
4.  **Bitwise Only**: This system is strictly for equality checks (`==`) and masked equality. Range checks (e.g., "Country code > 200") are NOT supported in the fast path and must be done in user logic inside the `ForEach` loop.

