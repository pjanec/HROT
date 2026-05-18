# **FDP Technical Specification \- Chapter 1: Core Architecture & Memory Model**

Version: 1.0 (Draft)  
Scope: FDP Internals, Memory Layout, Hybrid Data Strategy

## **1.1 Architectural Philosophy**

The **Generic Blackboard (FDP)** is the central data backbone of the B-One simulation backend. Unlike traditional "Shared Memory" backends which rely on OS-level memory mapping (and thus suffer from rigid sizing and marshaling overhead), the new FDP is an **In-Process, High-Performance Database** designed for Data-Oriented Design (DOD).

### **1.1.1 Key Tenets**

1. **Zero-Allocation Runtime:** Once the simulation stabilizes, the FDP must operate without triggering the C\# Garbage Collector (GC) in the hot path (30-100Hz loop).  
2. **Cache Locality:** Data is stored in contiguous arrays (Structure of Arrays \- SoA) rather than scattered objects, ensuring maximum CPU cache efficiency for physics and logic iterators.  
3. **Hybrid Data Model:** The system acknowledges that not all data is equal. It transparently manages two distinct types of memory:  
   * **Tier 1 (Hot):** Blittable structs for high-frequency simulation (Physics, IG).  
   * **Tier 2 (Cold):** Managed objects for complex logic and UI state.  
4. **DDS-Aware:** The memory layout is designed to facilitate fast "Scatter/Gather" operations for network replication without complex serialization logic.

## **1.2 The Paged Memory Architecture**

To balance the need for contiguous memory (speed) with the need for dynamic entity counts (flexibility), the FDP utilizes a **Paged Chunk Architecture**.

### **1.2.1 The "Binder" Concept**

Data is not stored in one monolithic array. Instead, it is organized into **Component Pools** (Binders). There is one Pool for every Descriptor Type in the system.

* Pool\<SpatialDescriptor\>  
* Pool\<DamageDescriptor\>  
* Pool\<IdentityDescriptor\>

### **1.2.2 The Chunk (Page)**

Each Pool is composed of a linked list of **Chunks**.

* **Capacity:** Fixed size (e.g., 1024 entities per chunk).  
* **Allocation:** When Chunk $N$ is full, Chunk $N+1$ is allocated from the OS.  
* **Memory layout:**  
  \[ Chunk 0 (0-1023) \] \-\> \[ Chunk 1 (1024-2047) \] \-\> \[ Chunk 2 (2048-3071) \]

This allows the system to grow infinitely (unlike rigid shared memory segments) while maintaining strict locality within each 1024-entity block.

### **1.2.3 Tier 1: The "Hot" Chunk (Unmanaged)**

For performance-critical data (Tier 1), the Chunk is backed by **Pinned Unmanaged Memory** (IntPtr or byte\[\] pinned via GCHandle).

* **Structure:**  
  public unsafe struct Tier1Chunk\<T\> where T : unmanaged  
  {  
      public int Count;           // Active entities in this chunk  
      public uint LastWriteTick;  // Coarse versioning for Delta Sync  
      public T\* DataPtr;          // Direct pointer to the memory block  
      public uint\* VersionPtr;    // Pointer to the "Per-Entity" change tick array  
  }

* **Benefit:** This pointer (T\*) can be passed directly to C++ modules (PhysX, Native IG Drivers) via P/Invoke. No marshaling is required.

### **1.2.4 Tier 2: The "Cold" Chunk (Managed)**

For complex data types (Tier 2), the Chunk is backed by a standard C\# Array.

* **Structure:**  
  public class Tier2Chunk\<T\> where T : class  
  {  
      public int Count;  
      public T\[\] Data; // Array of references (pointers to Heap)  
  }

* **Trade-off:** Access incurs a pointer chase (Cache Miss), but allows for flexible data structures (List\<Waypoint\>, Dictionary\<string, string\>).

## **1.3 The Hybrid Data Classification**

The FDP strictly enforces a classification system for Descriptors.

### **1.3.1 Tier 1: High-Frequency / Blittable (The "Structs")**

* **Definition:** Must be unmanaged struct (contain only primitives or fixed buffers).  
* **Usage:** Physics, Collision, IG Transform, Raycasting.  
* **Update Rate:** 30-100Hz.  
* **Storage:** Unmanaged Chunks.  
* **Examples:**  
  * SpatialDescriptor: Position (Vector3), Orientation (Quaternion).  
  * DynamicsDescriptor: Velocity, Acceleration.  
  * IdentityDescriptor: TypeID (int), Side (enum), Callsign (FixedString32).

### **1.3.2 Tier 2: Low-Frequency / Logic (The "Managed")**

* **Definition:** Standard C\# class or ref struct.  
* **Usage:** Game Logic, Mission Scripts, Detailed UI Info.  
* **Update Rate:** Event-driven / Rare (0-5Hz).  
* **Storage:** Managed Object Pools.  
* **Examples:**  
  * InventoryDescriptor: List\<Item\>.  
  * MissionDataDescriptor: Briefing text, complex objectives.

## **1.4 Handling Strings: The FixedString32**

Standard C\# strings are prohibited in Tier 1 descriptors to prevent GC pressure and marshaling costs. We utilize a custom value type.

### **1.4.1 FixedString32 Implementation**

* **Storage:** A fixed 32-byte buffer embedded inline within the struct.  
* **Behavior:**  
  * **Write:** Truncates input to 32 bytes, writes to buffer.  
  * **Read:** Lazily decodes UTF-8 bytes to C\# string only when requested by UI/Logging.  
* **Snapshotting:** Because it is just 32 bytes of raw data, it is included automatically in memcpy operations for Snapshot/Replay.

\[StructLayout(LayoutKind.Sequential, Size \= 32)\]  
public unsafe struct FixedString32  
{  
    private fixed byte \_buffer\[32\];  
    // Helper property 'Value' performs the lazy encoding/decoding  
}

## **1.5 Versioning Strategy (Change Tracking)**

To support the requirement of **"sending updates only when modified"** (DDS/BFF), the FDP implements a dual-layer versioning system.

### **1.5.1 The Global Tick**

The FDP maintains a uint GlobalTick counter, incremented every simulation frame.

### **1.5.2 Coarse Versioning (Chunk Level)**

Every Chunk stores a LastWriteTick.

* **Logic:** If any entity in the chunk is modified, Chunk.LastWriteTick \= GlobalTick.  
* **Usage:** The DeltaIterator checks this first. If Chunk.Tick \< Consumer.LastTick, the **entire 1024-entity block is skipped**.

### **1.5.3 Fine Versioning (Entity Level)**

Every Chunk maintains a parallel array uint\[\] EntityVersions.

* **Logic:** When Entity\[i\] is modified, EntityVersions\[i\] \= GlobalTick.  
* **Usage:** If the Chunk check passes, the iterator scans this array to find the specific entities that changed.

# **FDP Technical Specification \- Chapter 2: Identity, Composition & Multi-Part Storage**

Version: 1.0 (Draft)  
Scope: Entity Handles, Header Architecture, Multi-Descriptor Indirection

## **2.1 Entity Identity & Lifecycle**

In a performance-critical engine, using GUIDs or Objects for identity is prohibited due to hashing costs and GC pressure. We utilize a **Index-Based Handle System**.

### **2.1.1 The EntityHandle Struct**

An entity is strictly identified by a lightweight struct that fits in a register.

\[StructLayout(LayoutKind.Sequential)\]  
public readonly struct EntityHandle : IEquatable\<EntityHandle\>  
{  
    public readonly int Index;    // Direct array index (0..MaxEntities)  
    public readonly byte Version; // Generational counter (0..255)

    // implicit operator int() \=\> Index; // Allowed for internal FDP use only  
}

### **2.1.2 Generational Indexing (The ABA Problem)**

Since Index 500 will be reused after an entity dies, we must prevent "stale references" (e.g., a Missile targeting a now-dead Tank).

* **Mechanism:**  
  * The FDP maintains a byte\[\] Generations array.  
  * On CreateEntity(): A free index is dequeued. Generations\[Index\] is passed to the new Handle.  
  * On DestroyEntity(): Generations\[Index\] is incremented (wrapping at 255).  
* **Validation:**  
  public bool IsAlive(EntityHandle handle)  
  {  
      // One array lookup \+ one comparison \= \~2 CPU cycles  
      return \_generations\[handle.Index\] \== handle.Version;  
  }

## **2.2 The Entity Header (The "Super Component")**

The EntityHeader is the most critical data structure in the FDP. It acts as the **"Gatekeeper"** for iteration and filtering. It is stored in a dedicated ChunkedPool\<EntityHeader\> that is always kept hot in the L1/L2 Cache.

### **2.2.1 Architecture & Alignment**

To support Single-Instruction (SIMD) filtering, the Header is aligned to **32-byte boundaries**.

\[StructLayout(LayoutKind.Sequential, Pack \= 1)\]  
public struct EntityHeader  
{  
    // \--- IDENTITY (8 Bytes) \---  
    public int EntityId;       // Self-reference (for iterator convenience)  
    public int TkbTypeId;      // The Static "DNA" (DIS Type / Archetype)  
      
    // \--- FILTERING (32 Bytes) \---  
    // The Global Bitmask. 256 bits representing existence of component types.  
    // Aligned for AVX2 loading.  
    public FixedBitSet256 DescriptorMask; 

    // \--- HIERARCHY (8 Bytes) \---  
    // The flattened DIS Hierarchy (Kind, Domain, Category, Subcat)  
    // Used for "Give me all Land Platforms" queries.  
    public ulong CategoryMask; 

    // \--- METADATA (8 Bytes) \---  
    public uint SpawnTick;  
    public byte Generation;  
    public byte SideId;  
    public ushort \_padding;    // Ensure 64-byte total size (Cache Line aligned)  
}

### **2.2.2 FixedBitSet256 (AVX Optimization)**

The DescriptorMask allows the FDP to support up to 256 unique Descriptor Types globally (e.g., Bit 0 \= Spatial, Bit 50 \= Wheels).

* **Optimization:** The C\# backend uses System.Runtime.Intrinsics.X86.Avx2 to compare this 256-bit mask against a query mask in a **single CPU instruction**.  
* **Benefit:** Iterators can filter millions of entities per second without "branchy" logic.

## **2.3 Multi-Descriptor Support (The "Heap")**

Standard ECS patterns fail when an entity needs *multiple* components of the same type (e.g., a Truck with 6 Wheels, a Ship with 4 Radars). Allocating these as managed List\<T\> objects is unacceptable for performance (GC pressure).

We utilize a **Contiguous Indirection Strategy**.

### **2.3.1 The Indirection Table**

For a specific descriptor type (e.g., WheelDescriptor), the FDP maintains:

1. **The Heap:** A massive, contiguous Paged Buffer of WheelDescriptor structs.  
2. **The Indirection Map:** An array indexed by EntityID.

public struct IndirectionEntry  
{  
    public int StartIndex; // Index into the Heap  
    public int Count;      // Number of items (wheels)  
}

### **2.3.2 High-Speed Access (Span\<T\>)**

Modules accessing multi-parts (like Physics) receive a Span\<T\>.

// Returns a pointer to the contiguous slice of memory for this entity's wheels  
public Span\<WheelDescriptor\> GetWheels(int entityId)  
{  
    var entry \= \_indirectionMap\[entityId\];  
    return \_heap.GetSpan(entry.StartIndex, entry.Count);  
}

* **Performance:** Iterating this Span is strictly linear. The CPU pre-fetcher works perfectly because the wheels for Entity X are stored adjacent to each other.

### **2.3.3 Dynamic "Move and Grow" Strategy**

Since the Heap is contiguous, we cannot insert a new wheel "in between" existing data. To support runtime modification (e.g., adding a sensor):

1. **Allocate:** Find free space at the *end* of the Heap (or in a fragment gap) large enough for Count \+ 1\.  
2. **Copy:** memcpy the existing descriptors to the new location.  
3. **Update:** Update the IndirectionEntry to point to the new range.  
4. **Free:** Mark the old range as a "Hole" for later defragmentation.

## **2.4 TKB Integration: The "Factory" Pattern**

The TkbTypeId stored in the Header is the link between **Runtime State** and **Static Definition**.

### **2.4.1 Automatic Hydration**

The CreateEntity(int tkbId) API acts as a factory.

1. **Lookup:** FDP looks up TkbId in the Static Database.  
2. **Blueprint:** The DB defines: "Type 55 is a Tank. It needs Spatial, Dynamics (Mass=50t), and 12 Wheels."  
3. **Allocation:**  
   * Sets Header.DescriptorMask bits for Spatial/Dynamics/Wheels.  
   * Allocates 1 slot in SpatialPool.  
   * Allocates 12 slots in WheelPool (Multi-Descriptor).  
   * Copies default values (Mass, Wheel Radius) from TKB to the new descriptors.

This ensures that a "Tank" entity is never created in an invalid state (e.g., missing its physics data).

# **FDP Technical Specification \- Chapter 3: Data Access & Iteration APIs**

Version: 1.0 (Draft)  
Scope: Zero-Allocation Iterators, Parallel Processing, C\# Interfaces

## **3.1 The Iterator Philosophy**

Standard C\# iteration (foreach (var x in list)) is prohibited in the core simulation loop due to heap allocation (enumerators) and lack of CPU cache awareness.

The FDP provides a suite of **Struct-Based Iterators**. These are lightweight value types allocated on the stack. They allow developers to write clean loops that the JIT compiler optimizes into efficient pointer arithmetic.

### **3.1.1 The Golden Rule**

**"Never iterate entities individually. Iterate Chunks."**

Iterating Chunks ($N=1024$) allows the CPU to load memory once and process a tight loop of contiguous data.

## **3.2 The Core Interface: IFdp**

The FDP exposes a consolidated interface for data access.

public interface IFdp  
{  
    // \--- LIFECYCLE \---  
    EntityHandle CreateEntity(int tkbTypeId, Vector3 initialPos, int sideId);  
    void DestroyEntity(EntityHandle handle);  
    bool IsAlive(EntityHandle handle);

    // \--- DIRECT ACCESS (O(1)) \---  
    // Returns a reference to the hot data (Tier 1\)  
    ref T Get\<T\>(EntityHandle handle) where T : unmanaged;  
      
    // Returns a Span for multi-part data (e.g., Wheels)  
    Span\<T\> GetParts\<T\>(EntityHandle handle) where T : unmanaged;

    // \--- ITERATION FACTORY \---  
    // These methods return 'ref struct' iterators (Zero GC)  
      
    // 1\. Standard: "All Tanks with Physics"  
    ChunkIterator\<T\> Query\<T\>(FixedBitSet256 mask) where T : unmanaged;  
      
    // 2\. Hierarchical: "All Land Platforms"  
    HierarchicalIterator QueryCategory(ulong categoryMask);

    // 3\. Delta: "What changed since Tick 500?"  
    DeltaIterator QueryChanges(uint sinceTick);  
      
    // 4\. Parallel: "Update Physics on 8 Threads"  
    void ParallelQuery(FixedBitSet256 mask, ParallelChunkDelegate action);  
}

## **3.3 The Performance Iterators**

### **3.3.1 The Chunk Iterator (Standard)**

Used for the vast majority of simulation logic (Physics, Movement). It iterates over contiguous blocks of memory.

* **Usage:**  
  foreach (var chunk in fdp.Query\<SpatialDescriptor\>(physMask))  
  {  
      // Get raw pointers/spans to the data arrays  
      var positions \= chunk.GetSpan\<SpatialDescriptor\>();  
      var velocities \= chunk.GetSpan\<DynamicsDescriptor\>();

      // Tight Loop (Auto-Vectorized by JIT)  
      for (int i \= 0; i \< chunk.Count; i++)  
      {  
          positions\[i\].Value \+= velocities\[i\].Value \* dt;  
      }  
  }

* **Internal Logic:** The iterator checks the EntityHeader bitmask for the *entire chunk*. If the chunk does not contain the required components, it skips 1024 entities instantly.

### **3.3.2 The Filtered Iterator (AVX2 Logic)**

Used for systems with complex exclusion rules (e.g., "Apply Fire Damage to Flammable entities that are NOT already burning").

* **Optimization:** Uses Vector256\<ulong\> to compare the EntityHeader.DescriptorMask against the query mask.  
* **Throughput:** Can filter \~10 million entities per second on modern hardware.

### **3.3.3 The Hierarchical Iterator (DIS Categories)**

Used by Renderers and Sensors to filter by broad category without checking TKB definitions.

* **Logic:** Checks (Header.CategoryMask & RequestMask) \== RequestMask.  
* **Example:**  
  * RequestMask: Kind=Platform | Domain=Air  
  * Matches: F-16, Apache Helicopter, UAV.  
  * Ignores: Tanks, Infantry, Munitions.

### **3.3.4 The Delta Iterator (Bandwidth Saver)**

Used by the **IG Connector** and **Web BFF** to minimize data transfer.

* **Mechanism:**  
  1. **Chunk Check:** if (Chunk.LastWriteTick \<= sinceTick) continue; (Skips 90% of static entities).  
  2. **Entity Check:** if (EntityVersion\[i\] \<= sinceTick) continue;  
* **Result:** Only "Dirty" data is serialized to DDS or JSON.

### **3.3.5 The Time-Sliced Iterator (Amortized Processing)**

Used for heavy AI/Pathfinding jobs that exceed a single frame's budget.

* **State:** The iterator struct holds { int ChunkIndex, int EntityIndex }.  
* **Behavior:**  
  // Resume from where we left off last frame  
  var iterator \= \_aiSystem.SavedIterator;

  while (iterator.MoveNext()) {  
      if (Stopwatch.ElapsedMilliseconds \> 5\) {  
          \_aiSystem.SavedIterator \= iterator; // Save state  
          return; // Yield  
      }  
      ProcessAI(iterator.Current);  
  }

## **3.4 Parallel Processing (The "Job System")**

Since the backend runs in a shared process, we can utilize the .NET ThreadPool for massive scalability.

### **3.4.1 ParallelQuery**

Instead of a loop, the developer provides a callback. The FDP partitions the active Chunks across available cores.

fdp.ParallelQuery(mask, (ChunkView chunk) \=\>   
{  
    // This code runs on Thread 1, 2, 3... simultaneously.  
    // Memory safety is guaranteed because Chunks are disjoint memory regions.  
    PhysicsKernel.SolveConstraints(chunk.GetSpan\<SpatialDescriptor\>());  
});

### **3.4.2 Safety Rules**

1. **Read-Only Random Access:** Threads can read any entity (via Get\<T\>) safely.  
2. **Write Isolation:** A thread may ONLY write to the Chunk it is currently processing. Writing to other entities (e.g., "Bullet hits Target") requires a **Command Buffer** (deferred action) to avoid race conditions.

## **3.5 Singleton Access (The "Tag" System)**

While iterators handle "The Many," logic often needs "The One" (e.g., The Instructor, The Ownship).

* **API:** EntityHandle GetSingleton(int roleId);  
* **Implementation:** A simple Dictionary\<int, EntityHandle\> maintained by the FDP.  
* **Usage:**  
  var instructor \= fdp.GetSingleton(Role.Instructor);  
  if (fdp.IsAlive(instructor)) { ... }

# **FDP Technical Specification \- Chapter 4: Systems Integration**

Version: 1.0 (Draft)  
Scope: TKB Hydration, DDS Networking, Time Management

## **4.1 TKB Integration: The Entity Factory**

The **Technical Knowledge Base (TKB)** is the static "DNA" of the simulation. The FDP does not store static properties (e.g., "Tank Mass", "Max Speed") per entity to save memory. Instead, it links every entity to a TKB Archetype.

### **4.1.1 The Hydration Workflow**

When CreateEntity(int tkbTypeId) is called, the FDP acts as a factory, ensuring no entity is ever created in a "partial" or invalid state.

1. **Lookup:** The FDP queries the ITkbDatabase for the definition of tkbTypeId.  
2. **Allocation Strategy:**  
   * **Header:** Sets the TkbTypeId, CategoryMask (DIS), and DescriptorMask (Component Bits).  
   * **Tier 1 Pools:** Allocates slots in SpatialPool, DynamicsPool, etc.  
   * **Multi-Part Pools:** Allocates correct count of Wheels/Turrets based on the TKB blueprint.  
3. **Initialization:**  
   * Copies *default values* from the TKB Definition to the new Runtime Descriptors.  
   * *Example:* DynamicsDescriptor.Mass is set to 50,000 (from TKB).

### **4.1.2 Runtime vs. Static Separation**

* **Static (TKB):** "Max Speed", "Armor Thickness", "Fuel Capacity".  
* **Runtime (FDP):** "Current Speed", "Damage State", "Current Fuel".  
* **Access Pattern:** Modules needing static data call fdp.GetStaticDefinition(id).

## **4.2 DDS Integration: The Separate Structure Strategy**

To maximize performance, the FDP does **not** store data in DDS-compatible formats (IDL). We use a **Dual-Structure Strategy**.

### **4.2.1 The Problem**

* **FDP Needs:** Packed arrays, implicit IDs, FixedString32, native pointers.  
* **DDS Needs:** Explicit @key EntityId, standard strings, IDL serialization.

### **4.2.2 The Solution: "Scatter Copy" Converters**

We generate code (during the build process) that bridges the two worlds efficiently.

**1\. FDP Struct (Optimized):**

struct Spatial\_FDP { Vector3 Pos; Quat Rot; } // 28 Bytes, No ID

**2\. IDL Struct (Network):**

struct Spatial\_DDS { @key long EntityId; float X,Y,Z; float QX,QY,QZ,QW; };

3\. The Converter Job (Generated):  
Instead of serializing one by one, the "DDS Writer Module" processes a Dirty Chunk:

1. **Input:** A Span\<Spatial\_FDP\> and Span\<int\> (Entity IDs) from the FDP Chunk.  
2. **Output:** A pre-allocated buffer of Spatial\_DDS.  
3. **Process:** A tight loop that copies Pos/Rot *and* injects the EntityId into the output.

### **4.2.3 Bandwidth Optimization**

* **Delta Sync:** The converter *only* runs on Chunks/Entities marked "Dirty" since the last publish tick.  
* **Topic Granularity:** Each Descriptor Type maps to one DDS Topic. Subscribers only pay for the data they need (e.g., "Fuel Gauge" only subscribes to FuelTopic).

## **4.3 Time & Scheduling Architecture**

The FDP is the authority on "Simulation Time," decoupling physics from wall-clock time to enable **Deterministic Replay** and **Time Dilation** (2x Speed).

### **4.3.1 The Two Clocks**

1. **Wall Clock:** System time. Used for UI rendering and network timeouts.  
2. **Sim Clock (**$T$**):** The virtual time.  
   * **Normal:** $T \+= \\Delta t$  
   * **Paused:** $T$ stops.  
   * **Fast Forward:** $T \+= \\Delta t \\times 2.0$

### **4.3.2 The Frame Scheduler (Tick)**

The backend runs a fixed or variable time-step loop.

1. **Prepare:**  
   * GlobalTick++  
   * Time.Delta \= ...  
   * Swap Double Buffers (if Consistent Mode is active).  
2. **Execute (Parallel):**  
   * Sim Modules read $T$, write $T+1$.  
   * Thread Pool executes ParallelQuery jobs.  
3. **Commit:**  
   * Mark updated Chunks as Dirty.  
   * Flush Command Buffers (handle created/destroyed entities).  
4. **Publish:**  
   * IG Connector reads changes, pushes to Shared Memory.  
   * DDS Writer reads changes, pushes to Network.

### **4.3.3 Time Slicing (The Budget)**

For heavy jobs (Pathfinding for 500 units), the scheduler assigns a **Time Budget** (e.g., 5ms).

* **Mechanism:** The TimeSlicedIterator checks the stopwatch.  
* **Guarantee:** If the job yields, the FDP guarantees the iterator state remains valid (or invalidates it if the entity died) for the next frame.

# **FDP Technical Specification \- Chapter 5: Implementation Guidelines & Best Practices**

Version: 1.0 (Draft)  
Scope: Coding Standards, Memory Safety, Performance Patterns

## **5.1 The "Performance Mindset" Rules**

The B-One backend operates under strict constraints to ensure a 30-100Hz simulation loop. Code within the "Hot Path" (inside Tick()) must adhere to these rules.

### **5.1.1 Rule \#1: Zero Allocation in Hot Paths**

* **Prohibited:** new Class(), foreach (on IEnumerable), LINQ (.Where, .Select), Lambda Closures (() \=\> x), String Concatenation.  
* **Allowed:** Structs, ref struct Iterators, Span\<T\>, stackalloc, Pre-allocated Object Pools.  
* **Reason:** Garbage Collection (Gen 0\) runs are fast but frequent. In a 10ms frame budget, even a 1ms GC pause causes visible stutter in the IG.

### **5.1.2 Rule \#2: Refs Over Copies**

When passing Tier 1 descriptors (which can be 64-128 bytes), always use ref or in.

* **Bad:** void Update(SpatialDescriptor data) (Copies 28 bytes)  
* **Good:** void Update(ref SpatialDescriptor data) (Passes 8-byte pointer)

### **5.1.3 Rule \#3: Struct Layouts**

All FDP descriptors must be \[StructLayout(LayoutKind.Sequential)\].

* **Pack=1:** Required if the struct exactly matches a C++ counterpart.  
* **Padding:** Be explicit. Add private byte \_padding to align fields to 4 or 8 bytes. Misaligned reads on some CPUs incur penalties.

## **5.2 Memory Safety & Unsafe Code**

The FDP uses unsafe pointers to interface with C++ and to perform high-speed buffer copying.

### **5.2.1 The "Safe Unsafe" Pattern**

Raw pointers (T\*) should rarely leave the FDP internals.

* **Expose:** Span\<T\>  
* **Internal:** void\* \_ptr

### **5.2.2 Using Unsafe.As\<T\>**

Use System.Runtime.CompilerServices.Unsafe for casting instead of standard casting, but only when types are layout-compatible.

* **Usage:** rapid interpretation of byte\[\] as SpatialDescriptor\[\].

### **5.2.3 Memory Alignment for AVX**

To enable the SIMD optimizations in the FilteredIterator:

* **Requirement:** The EntityHeader chunk must start at a memory address divisible by 32\.  
* **Implementation:** The FDP Chunk Allocator uses NativeMemory.AlignedAlloc(size, 32\) instead of new byte\[\].

## **5.3 Module Development Pattern**

Sim Modules (e.g., Physics, Fuel) should follow this structural template to ensure compatibility with the Frame Scheduler.

### **5.3.1 The Template**

public class FuelSystem : IModule  
{  
    private ChunkIterator\<FuelDescriptor\> \_iterator;  
      
    // 1\. Initialization (Fail-Fast)  
    public bool Initialize(IServiceProvider services)  
    {  
        var fdp \= services.Get\<IFdp\>();  
        // Cache the query mask to avoid re-creating it every frame  
        var mask \= new FixedBitSet256(DescriptorType.Fuel);   
        \_iterator \= fdp.CreateQuery(mask);  
        return true;  
    }

    // 2\. The Hot Loop  
    public void Tick(double dt)  
    {  
        // 3\. Iterate Chunks (Zero Allocation)  
        foreach (var chunk in \_iterator)  
        {  
            // 4\. Get Raw Spans (No bounds checking)  
            var fuel \= chunk.GetSpan\<FuelDescriptor\>();  
            var dynamics \= chunk.GetSpan\<DynamicsDescriptor\>(); // Optional dependency

            // 5\. Loop (Auto-Vectorized)  
            for (int i \= 0; i \< chunk.Count; i++)  
            {  
                if (fuel\[i\].Amount \> 0\)  
                {  
                    // Logic: Consumption based on velocity  
                    float consumption \= dynamics\[i\].Velocity.Magnitude \* 0.1f \* (float)dt;  
                    fuel\[i\].Amount \-= consumption;  
                }  
            }  
              
            // 6\. Mark Dirty (For DDS/IG Sync)  
            chunk.MarkDirty();  
        }  
    }  
}

## **5.4 Debugging & Profiling**

### **5.4.1 The "Visualizer"**

Since we use raw memory, the Visual Studio Debugger cannot easily display IntPtr data.

* **Standard:** All Descriptors must implement a DebuggerDisplay attribute or a proxy struct for inspection.

### **5.4.2 Performance Counters**

The Backend must emit internal telemetry (not Windows PerfCounters, too slow).

* **Metrics:**  
  * Fdp.EntityCount  
  * Fdp.MemoryUsageMB  
  * Frame.TimeMS  
  * GC.CollectionCount  
* **Visibility:** Displayed in the "Site Manager" dashboard for live health monitoring.

# **FDP Technical Specification \- Chapter 6: Advanced Patterns**

Version: 1.0 (Draft)  
Scope: Parallel Structural Changes, Memory Defragmentation, Code Generation Pipeline

## **6.1 Parallel Structural Changes: The ECB**

In a multi-threaded simulation (Chapter 3), we face a critical problem: **Chunks cannot be resized while threads are reading them.** Therefore, a Parallel Job cannot directly call CreateEntity or DestroyEntity.

We solve this with the **Entity Command Buffer (ECB)** pattern.

### **6.1.1 The Concept**

Instead of applying structural changes immediately, the job **records** the intent to change. These commands are replayed sequentially by the Main Thread at a safe "Sync Point" (End of Frame).

### **6.1.2 Implementation**

The ECB is a thread-local queue of lightweight command structs.

public class EntityCommandBuffer  
{  
    // Thread-local queues to avoid locking  
    private ConcurrentQueue\<Command\> \_commands;

    public void CreateEntity(int tkbId, Vector3 pos)   
    {  
        \_commands.Enqueue(new Command { Type \= CmdType.Create, Arg1 \= tkbId, Pos \= pos });  
    }

    public void DestroyEntity(EntityHandle handle)  
    {  
        \_commands.Enqueue(new Command { Type \= CmdType.Destroy, Handle \= handle });  
    }

    public void AddComponent\<T\>(EntityHandle handle, T component) where T : unmanaged  
    {  
         // Record intent to add dynamic component  
    }

    // Called by Main Thread during "Frame Commit" phase  
    public void Playback(IFdp fdp)  
    {  
        while (\_commands.TryDequeue(out var cmd))  
        {  
            switch (cmd.Type)  
            {  
                case CmdType.Create: fdp.CreateEntity(cmd.Arg1, cmd.Pos, ...); break;  
                case CmdType.Destroy: fdp.DestroyEntity(cmd.Handle); break;  
                // ...  
            }  
        }  
    }  
}

### **6.1.3 Usage in Parallel Jobs**

1. **Job Start:** The Scheduler assigns a temporary ECB to the job context.  
2. **Execution:** PhysicsJob detects a collision $\\rightarrow$ calls ecb.DestroyEntity(target).  
3. **Job End:** The Scheduler collects the ECB.  
4. **Frame Sync:** All ECBs are merged and executed against the FDP safely.

## **6.2 Heap Defragmentation (Long-Running Stability)**

The "Multi-Part Indirection Heap" (Chapter 2\) uses a "Move-and-Grow" strategy. Over a 48-hour drill, frequent addition/removal of parts (e.g., vehicle damage) will cause **Fragmentation** (holes in the contiguous array), leading to memory bloat and cache misses.

### **6.2.1 The Auto-Defragmenter**

The FDP includes a background maintenance service that runs during idle time or amortized across frames.

**Trigger Conditions:**

* Heap Density \< 70% (i.e., 30% of the array is "holes").  
* Memory Page Count \> Threshold.

**The Algorithm (Per Page):**

1. **Identify:** Find a "Sparse Page" (lots of holes).  
2. **Compact:** Iterate the page. Copy valid Descriptors to a **Fresh Page** (densely packed).  
3. **Relink:** For each moved descriptor, update the owning Entity's IndirectionTable entry to point to the new location.  
4. **Release:** Return the Sparse Page to the OS/Pool.

**Impact:**

* **Safety:** This operation requires a "Write Lock" on the specific descriptor pool, so it is typically run during the "Frame Sync" phase, limited to processing 1-2 pages per frame to avoid stalls.

## **6.3 Unified Code Generation Pipeline**

To ensure the **BFF (JSON)** and **DDS (IDL)** layers never drift from the C\# definitions, we utilize Roslyn Source Generators.

### **6.3.1 The "Single Source of Truth"**

The developer defines the data **only once** in C\# (the FDP struct).

\[GenerateDdsTopic("VehiclePos")\]  
\[GenerateJsonModel\]  
public struct SpatialDescriptor { ... }

### **6.3.2 The Generators**

1. **DDS Generator:**  
   * Outputs .idl files.  
   * Generates ScatterCopy() methods (Chapter 4).  
2. **JSON Generator (BFF):**  
   * Outputs a Utf8JsonWriter routine optimized for this specific struct.  
   * *Why:* System.Text.Json uses reflection. A generated writer is 5-10x faster and zero-allocation.

// Generated Code  
public void WriteJson(Utf8JsonWriter w, in SpatialDescriptor val) {  
    w.WriteStartObject();  
    w.WriteNumber("x", val.X);  
    w.WriteNumber("y", val.Y);  
    w.WriteEndObject();  
}

This pipeline ensures that adding a field to SpatialDescriptor automatically updates the Network Layer (DDS) and the UI Layer (JSON) at compile time.

# **FDP Technical Specification \- Chapter 7: Implementation Addendum**

Version: 1.1 (Draft)  
Scope: Storage Mapping, Type ID Optimization, Threading Phases

## **7.1 Storage Strategy: Direct Mapping (Dense)**

To minimize CPU cache misses during random access (e.g., fdp.Get\<T\>(id)), the FDP adopts a **Direct Mapping Strategy** over a Sparse/Archetype approach.

### **7.1.1 The Mechanism**

Every Descriptor Pool is sized to match the maximum capacity of the FDP (via Chunks), regardless of how common that component is.

* **Logic:** Pool\[EntityIndex\] always maps to the data location. There is no intermediate lookup table (EntityIndex \-\> Archetype \-\> PoolIndex).  
* **Trade-off:** If only 1 entity has a NukeLauncherDescriptor, we still conceptually reserve slots for NukeLauncher for all 10,000 entities.  
* **Justification:**  
  1. **Virtual Memory is Cheap:** Modern OS paging ensures unused chunks of "rare" pools are never actually backed by physical RAM.  
  2. **Access Speed:** Random access is strictly $O(1)$ (Array Indexing). Sparse Maps require $O(\\log n)$ or hash lookups, which destroy performance in the hot path.  
  3. **Iterator Efficiency:** Iterators simply skip chunks where Count \== 0\. The cost of iteration is proportional to *active* components, not *reserved* space.

## **7.2 High-Speed Type Identification**

The FDP Generic API (fdp.Get\<T\>) requires mapping a C\# Type to an internal integer index to locate the correct ComponentPool. Standard typeof(T).GetHashCode() is too slow for the simulation loop.

### **7.2.1 The Static Generic Cache Pattern**

We utilize the CLR's ability to generate distinct static fields for generic classes.

// Internal Helper  
internal static class ComponentType\<T\> where T : unmanaged  
{  
    // Assigned once at startup. Thread-safe initialization required.  
    public static readonly int Id \= ComponentRegistry.Register(typeof(T));  
}

// Usage in FDP (Hot Path)  
public ref T Get\<T\>(EntityHandle handle) where T : unmanaged  
{  
    // 1\. Get Integer ID (Compiled to a constant field access)  
    int typeId \= ComponentType\<T\>.Id;  
      
    // 2\. Direct Array Access (No Dictionary lookup)  
    var pool \= \_pools\[typeId\];  
      
    return ref ((Tier1Pool\<T\>)pool).Get(handle.Index);  
}

* **Performance:** This compiles down to a simple static field load, which is practically instantaneous compared to dictionary lookups.

## **7.3 Threading Model: Phased Execution**

To achieve lock-free parallelism, the Backend Frame Scheduler enforces a strict **4-Phase Execution Model**. Accessing the FDP incorrectly (e.g., writing during the Read Phase) will trigger a "WrongPhaseException" in Debug builds.

### **7.3.1 Phase 1: Network Ingest (Pre-Sim)**

This phase applies state updates from **external backends** (DDS) to the local "Ghost" entities. This ensures the local simulation (Phase 2\) sees the latest state of the distributed world.

* **Allowed:**  
  * Writing to **Remote/Ghost** Entity Descriptors (Position, Status).  
  * **Creating/Destroying** Ghost Entities (Handling Network Discovery/Loss).  
* **Prohibited:**  
  * Writing to **Local/Owned** Entities (Preventing race conditions with previous frame's logic).  
* **Mechanism:**  
  * **Main Thread:** Handles structural changes (Mapping UUIDs to local Handles, creating new Ghosts).  
  * **Parallel:** Large batches of spatial updates can be applied to Ghost chunks in parallel, provided the UUID-to-Handle map is pre-calculated.

### **7.3.2 Phase 2: Simulation (Parallel)**

The core simulation loop for **Local/Owned** entities.

* **Allowed:**  
  * Read/Write **Local** Component Data (fdp.Get\<T\>).  
  * Read **Remote** Component Data (e.g., Sensors reading a Ghost's position).  
  * Record Commands to EntityCommandBuffer (Create/Destroy Intent).  
* **Prohibited:**  
  * Writing to **Remote** entities.  
  * Immediate Structural Changes (fdp.CreateEntity, fdp.DestroyEntity).  
  * Resizing Pools.  
* **Mechanism:** Chunks are partitioned. Threads operate on disjoint memory. Random access to *other* entities is Read-Only safe.

### **7.3.3 Phase 3: Structural Sync (Main Thread Only)**

The "Frame Commit" point where deferred local operations are applied.

* **Allowed:**  
  * Execution of EntityCommandBuffer queues (Applying Phase 2's intents).  
  * fdp.CreateEntity, fdp.DestroyEntity (Local).  
  * Heap Defragmentation (Chapter 6).  
* **Prohibited:**  
  * Any other thread accessing the FDP.  
* **Mechanism:** This is a "Stop-the-World" phase (barrier sync). All worker threads are waiting.

### **7.3.4 Phase 4: Export/Snapshot (Parallel Read-Only)**

Data is pushed out to the rest of the distributed system and local visualization.

* **Allowed:**  
  * DDS Writer (Read-Only) \-\> Publishes **Local** entity updates to the network.  
  * IG Connector (Read-Only) \-\> Sends visual updates (Local \+ Ghosts) to the Image Generator.  
  * Visual Replay Recorder (Read-Only).  
* **Prohibited:**  
  * Writing to any Descriptor.  
* **Mechanism:** Snapshotters can run safely while the Scheduler prepares the next frame's inputs, as long as the inputs (Phase 1 of Next Frame) don't mutate the data being exported.

### **7.3.5 Enforcement**

The FDP stores a volatile \_currentPhase field.

\[Conditional("DEBUG")\]  
private void AssertPhase(Phase required)  
{  
    // E.g., Writing is only allowed if (Phase \== NetworkIngest && IsGhost)   
    // OR (Phase \== Simulation && IsLocal)  
    if (\_currentPhase \!= required) throw new WrongPhaseException();  
}

# **FDP Technical Specification \- Chapter 8: Memory & Allocator Specification**

Version: 1.0 (Draft)  
Scope: Virtual Memory Management, Custom Allocators, Dynamic Growth  
8.1 The Memory Problem  
In the Direct Mapped architecture (Chapter 7), we conceptually reserve space for every component for every possible entity ID.

* **Naive Approach:** new Component\[MaxEntities\]  
  * **Result:** The .NET Runtime (CLR) asks the OS to **COMMIT** the entire block immediately.  
  * **Impact:** 100,000 entities \* 128 bytes \= \~12MB per component. For 100 component types, this consumes **1.2GB of RAM** instantly, even if the world is empty.  
  * **Risk:** This replicates the "Shared Memory" exhaustion issue.

8.2 The Solution: The "Virtual Allocator"  
To prevent this, the FDP must not use standard C\# arrays for Tier 1 storage. Instead, it must implement a custom NativeMemoryAllocator that interacts directly with the OS Kernel to separate Reservation (Address Space) from Commitment (Physical RAM).  
8.2.1 Reserve vs. Commit

* **Reserve (The Promise):** We ask the OS for a contiguous range of addresses (e.g., 0x0000 to 0xFFFF).  
  * **Cost:** 0 Bytes of RAM. Just an entry in the OS Kernel's VAD (Virtual Address Descriptor) tree.  
* **Commit (The Payment):** We tell the OS, "I am about to write to page 5\. Please give me 4KB of real RAM now."  
  * **Cost:** 4KB of RAM per committed page.

8.3 Implementation Strategy  
The FDP ChunkAllocator operates on a Demand Paging logic.  
8.3.1 Windows Implementation (VirtualAlloc)  
// 1\. Initialization (Reserve ONLY)  
// Reserves Address Space for Capacity (e.g., 100M Entities).   
// Physical RAM usage \= 0\.  
IntPtr baseAddress \= Kernel32.VirtualAlloc(  
    IntPtr.Zero,  
    (UIntPtr)(Capacity \* itemSize),  
    AllocationType.Reserve, // MEM\_RESERVE (0x2000)  
    MemoryProtection.NoAccess);

// 2\. On Chunk Creation (Commit)  
// When Entity 500 is created, we commit only the memory for Chunk 0\.  
void EnsureChunkCommitted(int chunkIndex) {  
    if (\_committedChunks\[chunkIndex\]) return;

    IntPtr chunkAddr \= baseAddress \+ (chunkIndex \* ChunkSize);  
      
    // Asks OS for actual RAM for just this chunk (e.g., 64KB)  
    Kernel32.VirtualAlloc(  
        chunkAddr,  
        (UIntPtr)ChunkSizeBytes,  
        AllocationType.Commit,  // MEM\_COMMIT (0x1000)  
        MemoryProtection.ReadWrite);  
          
    \_committedChunks\[chunkIndex\] \= true;  
}

8.3.2 Linux/Unix Implementation (mmap)  
// 1\. Initialization (Reserve)  
// PROT\_NONE tells Linux "Reserve this range, but SEGFAULT if touched."  
IntPtr ptr \= LibC.mmap(  
    IntPtr.Zero,   
    totalSize,   
    Prot.PROT\_NONE,   
    Flags.MAP\_PRIVATE | Flags.MAP\_ANONYMOUS, \-1, 0);

// 2\. On Chunk Creation (Commit)  
// PROT\_READ | PROT\_WRITE tells Linux "Back this specific page with RAM."  
LibC.mprotect(  
    chunkAddr,   
    chunkSize,   
    Prot.PROT\_READ | Prot.PROT\_WRITE);

8.4 Dynamic Growth Strategies  
What happens if the entity count exceeds the initial Capacity?  
8.4.1 Strategy A: The "Infinite" Reservation (Recommended)  
On 64-bit systems, Virtual Address Space is effectively infinite (16 Exabytes).

* **Mechanism:** Reserve space for **100 Million Entities** at startup.  
* **RAM Cost:** 0 Bytes (until used).  
* **Benefit:** The "Limit" is so high it is unreachable. Pointers (T\*) passed to PhysX/C++ remain valid forever because the base address never changes.  
* **Safety:** This is the preferred Data-Oriented approach.

8.4.2 Strategy B: Explicit Reallocation (Fallback)  
If strict limits are required (or running on 32-bit), we must "Move and Grow".

* **Trigger:** Count \>= Capacity  
* **Action:**  
  1. VirtualAlloc a new block (Size \* 2).  
  2. Unsafe.CopyBlock (memcpy) old data to new.  
  3. Free old block.  
* **CRITICAL RISK:** The Base Address has changed. All pointers held by external systems (PhysX userData, Graphics integration) are now **DANGLING**.  
* Mitigation (Pointer Safety Protocol):  
  The FDP must raise a synchronous event:  
  public static event Action OnMemoryMoved;

  // Usage in PhysX Wrapper  
  void OnResize() {  
      // STOP THE WORLD  
      foreach(var actor in \_physxActors) {  
          // Re-calculate pointer from new base  
          actor.userData \= fdp.GetPointer(actor.EntityId);  
      }  
  }

8.5 Memory Cost Table  
Assuming 1,000,000 Entity Capacity, 100 Component Types.

| State | Standard Array (new) | FDP Virtual Allocator |
| :---- | :---- | :---- |
| **Empty World** | **\~12 GB** (Physical RAM) | **\~0 MB** (Physical RAM) |
| **1,000 Entities** | **\~12 GB** | **\~12 MB** |
| **Full World** | **\~12 GB** | **\~12 GB** |

This confirms that the FDP scales linearly with **Active Usage**, not **Reserved Capacity**.

# **FDP Memory Architecture Specification**

**Status:** Pre-Implementation Review

## **1\. Access Pattern: The "Page Table"**

Decision: APPROVED (User Item 1\)

We will strictly use the O(1) Direct Mapped approach.

* **Structure:** We will maintain a master array of Chunk pointers (the "Page Table").  
* **Lookup:** To find Entity ID 500,000:  
  1. ChunkIndex \= 500,000 / ChunkSize  
  2. EntityIndex \= 500,000 % ChunkSize  
  3. Data \= \_chunks\[ChunkIndex\]\[EntityIndex\]  
* **Benefit:** Zero iteration. Access time is constant regardless of whether we have 10 entities or 1,000,000.

## **2\. The "Split-Brain" Memory Strategy**

**Decision:** PENDING EXPLANATION (User Item 2\)

We are adopting a Hybrid Memory Model.

* **Tier 1 (Hot/Structs):** Unmanaged, OS-Allocated (VirtualAlloc), Invisible to GC.  
* **Tier 2 (Cold/Classes):** Managed, Heap-Allocated (new T\[\]), Managed by GC.

### **The Consequences of this Approach**

You asked for an explanation of the consequences of letting Tier 2 behave like standard .NET objects.

#### **A. The "Garbage Collection" Consequence (The Performance Cliff)**

* **Tier 1:** The GC **does not know** Tier 1 memory exists. You can allocate 1GB of Physics position data, and the GC will not scan it, will not mark it, and will not pause your game to clean it. This is pure speed.  
* **Tier 2:** The GC **sees everything**. If you allocate arrays for Strings or Lists for 1 million entities, the GC adds them to its workload.  
  * **The Risk:** If you frequently create and destroy Chunks (loading/unloading zones), the GC has to clean up the old Tier 2 arrays. This can cause "micro-stutters" (Gen 0/1 collections).  
  * **Mitigation:** We are relying on the fact that Tier 2 data is usually "Cold" (game logic, names, inventory) and not updated every single frame like Physics.

#### **B. The "Cache Locality" Consequence**

* **Tier 1:** We guarantee **Physical Contiguity**. The OS gives us a solid block of RAM. When the CPU reads position 1, it pre-fetches position 2, 3, and 4 automatically.  
* **Tier 2:** We only guarantee **Logical Contiguity**. The .NET Heap might put "Chunk A's Name Array" at memory address 0x1000 and "Chunk B's Name Array" at 0x9000.  
  * **The Risk:** Iterating through *all* Tier 2 data linearly will be slower than Tier 1 because the CPU has to jump around the RAM (Cache Misses).

#### **C. The "Reference" Consequence**

* **Restriction:** You cannot store a Reference Type (e.g., string, List\<Item\>, MyClass) inside Tier 1 memory. The runtime would crash because the GC cannot relocate objects if they are buried inside raw unmanaged memory.  
* **Conclusion:** This split is **unavoidable** if you want to support strings or classes. We accept the Tier 2 penalties to gain the flexibility of using standard C\# types.

## **3\. Allocation Granularity**

**Decision:** APPROVED Option A (User Item 3\)

We will use **Per-Chunk Commits**.

* **Mechanism:** When the first entity in a Chunk is created, we commit the **entire Chunk size** (e.g., 16KB or 64KB) to physical RAM immediately.  
* **Reasoning:**  
  * Calling VirtualAlloc (the OS Kernel function) is expensive (approx. 5-10 microseconds).  
  * Doing this once per 1024 entities (Per Chunk) is negligible.  
  * Doing this once per 64 entities (Per Page) would introduce measurable lag during rapid entity spawning.

# **FDP (Generic Blackboard) API Reference**

Version: 1.0 (Final)  
Target: B-One Simulation Backend  
Philosophy: Data-Oriented, Zero-Allocation, Cache-First.

## **1\. Core Types**

### **1.1 EntityHandle**

The fundamental identifier for all simulation objects. It replaces pointers and GUIDs.

* **Size:** 5 Bytes (padded to 8 in arrays).  
* **Safety:** Includes a Version byte to prevent "ABA" problems (accessing dead slots reused by new entities).

\[StructLayout(LayoutKind.Sequential)\]  
public readonly struct EntityHandle : IEquatable\<EntityHandle\>  
{  
    public readonly int Index;       // Direct array index (0..MaxEntities)  
    public readonly byte Version;    // Generational counter  
      
    public static readonly EntityHandle Null \= default;  
}

### **1.2 FixedBitSet256**

A high-performance bitmask optimized for AVX2 SIMD instructions. Used for filtering entities by component composition.

\[StructLayout(LayoutKind.Sequential)\]  
public struct FixedBitSet256  
{  
    // Sets the bit corresponding to component T  
    public void Set\<T\>() where T : unmanaged;  
      
    // Checks if this mask contains ALL bits of 'other' (Single CPU Cycle)  
    public bool ContainsAll(in FixedBitSet256 other);  
}

## **2\. The Core Interface: IFdp**

The primary entry point for all Simulation Modules.

### **2.1 Lifecycle (Phase 3 Only)**

Operations that change the existence of entities.

public interface IFdp  
{  
    /// \<summary\>  
    /// Creates a new entity based on the Static TKB Blueprint.  
    /// Hydrates all required descriptors defined in the TKB.  
    /// \</summary\>  
    /// \<param name="tkbTypeId"\>The unique ID of the archetype (e.g., Tank=505).\</param\>  
    /// \<returns\>A safe handle to the new entity.\</returns\>  
    EntityHandle CreateEntity(int tkbTypeId);

    /// \<summary\>  
    /// Marks an entity for destruction. Memory is reclaimed immediately.  
    /// \</summary\>  
    void DestroyEntity(EntityHandle handle);

    /// \<summary\>  
    /// Checks if a handle refers to a currently active entity.  
    /// Must be checked before accessing data if the handle was stored across frames.  
    /// Cost: \~2 Cycles.  
    /// \</summary\>  
    bool IsAlive(EntityHandle handle);  
}

### **2.2 Tier 1 Data Access (Hot / Structs)**

Fast, zero-copy access to unmanaged data.

public interface IFdp  
{  
    /// \<summary\>  
    /// Check if entity has a specific component (via Bitmask).  
    /// \</summary\>  
    bool Has\<T\>(EntityHandle handle) where T : unmanaged;

    /// \<summary\>  
    /// Returns a REFERENCE to the live data in the FDP chunk.  
    /// Modifying the return value modifies the FDP directly.  
    /// \</summary\>  
    /// \<exception cref="WrongPhaseException"\>If called during Phase 4 (Read-Only).\</exception\>  
    ref T Get\<T\>(EntityHandle handle) where T : unmanaged;

    /// \<summary\>  
    /// Replaces the entire struct value.   
    /// Automatically marks the Chunk as Dirty for DDS/IG Sync.  
    /// Preferred pattern for struct updates to avoid GC pressure.  
    /// \</summary\>  
    void Set\<T\>(EntityHandle handle, in T value) where T : unmanaged;  
      
    /// \<summary\>  
    /// Adds a component dynamically at runtime.  
    /// \</summary\>  
    void AddComponent\<T\>(EntityHandle handle, in T component) where T : unmanaged;

    /// \<summary\>  
    /// Removes a component dynamically.  
    /// \</summary\>  
    void RemoveComponent\<T\>(EntityHandle handle) where T : unmanaged;  
}

### **2.3 Tier 2 Data Access (Cold / Managed)**

Access to complex C\# objects (Strings, Lists).

public interface IFdp  
{  
    /// \<summary\>  
    /// Returns the managed object associated with the entity.  
    /// \</summary\>  
    T GetManaged\<T\>(EntityHandle handle) where T : class;

    /// \<summary\>  
    /// CRITICAL: Must be called after modifying any property of a managed object.  
    /// Triggers the 'Dirty' flag for Network/UI replication.  
    /// \</summary\>  
    void MarkModified\<T\>(EntityHandle handle) where T : class;  
}

### **2.4 Multi-Part Access (1-to-N)**

Access to dynamic sub-components (Wheels, Turrets, Hardpoints).

public interface IFdp  
{  
    /// \<summary\>  
    /// Returns a linear memory span of all parts of type T for this entity.  
    /// Fast iteration (contiguous memory).  
    /// \</summary\>  
    Span\<T\> GetParts\<T\>(EntityHandle handle) where T : unmanaged;

    /// \<summary\>  
    /// Returns a specific part by its user-defined Key/ID.  
    /// Performs a linear scan over the small Span.  
    /// \</summary\>  
    ref T GetPart\<T\>(EntityHandle handle, int partId) where T : unmanaged;

    /// \<summary\>  
    /// Adds a new part. Triggers "Move and Grow" reallocation of the Heap.  
    /// \</summary\>  
    void AddPart\<T\>(EntityHandle handle, in T part) where T : unmanaged;  
}

### **2.5 Singleton & Role Access**

Access to unique entities without iteration.

public interface IFdp  
{  
    /// \<summary\>  
    /// Retrieves a unique entity registered with a specific Role ID.  
    /// Returns EntityHandle.Null if not found.  
    /// \</summary\>  
    EntityHandle GetEntityByRole(int roleId);

    /// \<summary\>  
    /// Registers an entity to a role.  
    /// \</summary\>  
    void RegisterRole(int roleId, EntityHandle handle);  
}

## **3\. Iterators (Zero-Allocation)**

All iterators return ref struct types. They cannot be boxed or used in async methods.

### **3.1 Standard Iteration**

// 1\. Iterate Chunks (Fastest \- Physics/Simulation)  
// Returns ChunkView\<T\>  
foreach (var chunk in fdp.QueryChunks\<SpatialDescriptor\>())   
{   
    Span\<SpatialDescriptor\> data \= chunk.GetSpan\<SpatialDescriptor\>();  
    // ...  
}

// 2\. Filtered Iteration (Logic Systems)  
// Uses AVX to verify mask matches.  
var mask \= new FixedBitSet256();  
mask.Set\<SpatialDescriptor\>();  
mask.Set\<BurningTag\>();  
foreach (EntityHandle h in fdp.Query(mask)) { ... }

### **3.2 specialized Iteration**

// 3\. Hierarchical (DIS Category)  
// Filters using EntityHeader.CategoryMask  
// "Get all Air Platforms"  
foreach (ref readonly EntityHeader h in fdp.QueryHeaders(Kind.Platform, Domain.Air)) { ... }

// 4\. Delta Sync (Network/IG)  
// Returns chunks modified since 'lastTick'  
foreach (var chunk in fdp.QueryChangedChunks\<SpatialDescriptor\>(lastTick)) { ... }

### **3.3 Time Slicing (Budgeted)**

For heavy AI/Pathfinding jobs spread across frames.

// Context persists state between frames  
public class TimeSliceContext { ... }

// Usage  
fdp.QueryTimeSliced\<AiDescriptor\>(context, TimeSpan.FromMilliseconds(5));

## **4\. Parallel Jobs (Phase 2\)**

### **4.1 The Parallel API**

Executes logic on the .NET ThreadPool. Partitioning is automatic based on Chunk count.

public interface IFdp  
{  
    /// \<summary\>  
    /// Runs the callback in parallel across all chunks matching T.  
    /// \</summary\>  
    void ParallelQuery\<T\>(ParallelJobDelegate\<T\> job) where T : unmanaged;  
}

// Delegate Signature  
public delegate void ParallelJobDelegate\<T\>(  
    ChunkView\<T\> chunk,   
    EntityCommandBuffer ecb  
) where T : unmanaged;

### **4.2 Entity Command Buffer (ECB)**

Thread-safe queue for structural changes during parallel jobs.

public class EntityCommandBuffer  
{  
    void CreateEntity(int tkbId);  
    void DestroyEntity(EntityHandle handle);  
    void AddComponent\<T\>(EntityHandle handle, T component);  
}

## **5\. TKB Integration (Static Data)**

Access to the read-only technical database.

public interface ITkbDatabase  
{  
    /// \<summary\>  
    /// Returns the blueprint used to hydrate new entities.  
    /// \</summary\>  
    TkbBlueprint GetBlueprint(int tkbTypeId);

    /// \<summary\>  
    /// Returns READ-ONLY reference to static data (Mass, Dimensions).  
    /// Prevents memory duplication in FDP.  
    /// \</summary\>  
    ref readonly T GetStaticData\<T\>(int tkbTypeId) where T : unmanaged;  
}

## **6\. Usage Examples**

### **Example 1: Creating a Unit (Extension Method)**

Since CreateEntity is generic, we use helpers for domain-specific initialization.

public static class SimExtensions  
{  
    public static void SpawnTank(this IFdp fdp, int tkbId, Vector3 pos, int side)  
    {  
        // 1\. Generic Creation (Hydrates from Blueprint)  
        var handle \= fdp.CreateEntity(tkbId);

        // 2\. Set Initial State (Tier 1\)  
        if (fdp.Has\<SpatialDescriptor\>(handle))  
        {  
            ref var spatial \= ref fdp.Get\<SpatialDescriptor\>(handle);  
            spatial.Position \= pos;  
            spatial.Rotation \= Quaternion.Identity;  
        }

        // 3\. Set Side (Tier 1\)  
        if (fdp.Has\<IdentityDescriptor\>(handle))  
        {  
             ref var id \= ref fdp.Get\<IdentityDescriptor\>(handle);  
             id.Side \= side;  
        }  
    }  
}

### **Example 2: Parallel Physics Update**

Demonstrates safe parallel execution and struct replacement.

public class PhysicsSystem : IModule  
{  
    public void Tick(IFdp fdp, ITkbDatabase tkb)  
    {  
        fdp.ParallelQuery\<DynamicsDescriptor\>((chunk, ecb) \=\>  
        {  
            var dynamics \= chunk.GetSpan\<DynamicsDescriptor\>();  
            var spatials \= chunk.GetSpan\<SpatialDescriptor\>();  
              
            // Optimization: Get Static Data for this Chunk's Archetype  
            // Assumes chunk contains same TKB type (Archetype Chunking)  
            ref readonly var staticDef \= ref tkb.GetStaticData\<VehicleDef\>(chunk.Header.TkbTypeId);

            for (int i \= 0; i \< chunk.Count; i++)  
            {  
                // 1\. Logic  
                dynamics\[i\].Velocity \+= dynamics\[i\].Acceleration \* DeltaTime;  
                spatials\[i\].Position \+= dynamics\[i\].Velocity \* DeltaTime;

                // 2\. Structural Change (Safe)  
                if (spatials\[i\].Position.Y \< \-100)  
                {  
                    ecb.DestroyEntity(chunk.Handles\[i\]);  
                }  
            }  
        });  
    }  
}

### **Example 3: Network Ingest (Ghost Tags)**

Demonstrates creating entities from remote updates.

public void OnDdsPacket(IFdp fdp, DdsPacket packet)  
{  
    // 1\. Create Ghost  
    var ghost \= fdp.CreateEntity(packet.TkbId);

    // 2\. Tag as Ghost (Crucial for Physics to ignore it)  
    fdp.AddComponent(ghost, new GhostTag());

    // 3\. Apply State  
    fdp.Set(ghost, new SpatialDescriptor { Position \= packet.Position });  
}

### **Example 4: Singleton Access (The Instructor)**

Demonstrates role-based lookup.

public void UpdateInstructorLogic(IFdp fdp)  
{  
    var handle \= fdp.GetEntityByRole(Roles.Instructor);  
      
    if (fdp.IsAlive(handle))  
    {  
        // Process  
    }  
}
