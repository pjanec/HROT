# HSM Implementation Design

**Version:** 1.0.0  
**Date:** 2026-01-11  
**Status:** Ready for Implementation

---

## Table of Contents

1. [Data Layer: ROM and RAM Layouts](#1-data-layer-rom-and-ram-layouts)
2. [Compiler: Flattening, Linking, and Validation](#2-compiler-flattening-linking-and-validation)
3. [Kernel: UpdateBatch Logic](#3-kernel-updatebatch-logic)
4. [Tooling: Hot Reload and Debug Tracing](#4-tooling-hot-reload-and-debug-tracing)

---

## 1. Data Layer: ROM and RAM Layouts

### 1.1 Core Enumerations

```csharp
namespace FastHSM
{
    /// <summary>
    /// State flags for StateDef.
    /// </summary>
    [Flags]
    public enum StateFlags : ushort
    {
        None = 0,
        IsComposite = 1 << 0,       // Has child states
        HasHistory = 1 << 1,         // Stores history
        DeepHistory = 1 << 2,        // Deep history (vs shallow)
        HasRegions = 1 << 3,         // Contains orthogonal regions
        IsInitial = 1 << 4,          // Initial state of parent
        HasActivity = 1 << 5,        // Has OnUpdate logic
    }

    /// <summary>
    /// Transition flags for TransitionDef.
    /// </summary>
    [Flags]
    public enum TransitionFlags : ushort
    {
        None = 0,
        IsInternal = 1 << 0,         // Internal transition (no exit/entry)
        IsExternal = 1 << 1,         // External self-transition
        IsInterrupt = 1 << 2,        // Interrupt-class transition
        IsSynchronized = 1 << 3,     // Part of synchronized group
        TargetsHistory = 1 << 4,     // Target is a history state
        Priority_Mask = 0xF000,      // Upper 4 bits for priority (0-15)
    }

    /// <summary>
    /// Event priority classes.
    /// </summary>
    public enum EventPriority : byte
    {
        Low = 0,
        Normal = 1,
        Interrupt = 2,
        Critical = 3
    }

    /// <summary>
    /// Command lane identifiers.
    /// </summary>
    public enum CommandLane : byte
    {
        Animation = 0,
        Navigation = 1,
        Gameplay = 2,
        Blackboard = 3,
        Audio = 4,
        VFX = 5,
        Message = 6,
    }

    /// <summary>
    /// Instance tier size classes.
    /// </summary>
    public enum TierSize : byte
    {
        Crowd_64B = 64,
        Standard_128B = 128,
        Hero_256B = 256
    }
}
```

### 1.2 ROM: The Immutable Definition Blob

#### 1.2.1 HsmDefinitionHeader

```csharp
using System.Runtime.InteropServices;

namespace FastHSM
{
    /// <summary>
    /// Header for the compiled HSM blob.
    /// Size: 64 bytes (aligned).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct HsmDefinitionHeader
    {
        // === Verification (16 bytes) ===
        [FieldOffset(0)] public uint Magic;              // 'FHSM'
        [FieldOffset(4)] public uint Version;            // Format version
        [FieldOffset(8)] public ulong StructureHash;     // Topology hash
        [FieldOffset(16)] public ulong ParameterHash;    // Parameter hash

        // === Sizing & Limits (16 bytes) ===
        [FieldOffset(24)] public TierSize Tier;          // 64/128/256
        [FieldOffset(25)] public byte MaxDepth;          // Max hierarchy depth (≤16)
        [FieldOffset(26)] public ushort StateCount;      
        [FieldOffset(28)] public ushort TransitionCount; 
        [FieldOffset(30)] public ushort RegionCount;     
        [FieldOffset(32)] public ushort TimerSlotCount;  
        [FieldOffset(34)] public ushort HistorySlotCount;
        [FieldOffset(36)] public ushort EventTypeCount;  

        // === Table Offsets (24 bytes) ===
        // Offsets are relative to the start of the blob
        [FieldOffset(40)] public uint StateTableOffset;
        [FieldOffset(44)] public uint TransitionTableOffset;
        [FieldOffset(48)] public uint RegionTableOffset;
        [FieldOffset(52)] public uint GlobalTransitionTableOffset;
        [FieldOffset(56)] public uint LinkerTableOffset;
        [FieldOffset(60)] public ushort LinkerEntryCount;
        [FieldOffset(62)] public ushort Reserved;

        // Total: 64 bytes
    }
}
```

#### 1.2.2 StateDef

```csharp
namespace FastHSM
{
    /// <summary>
    /// State definition in the flat ROM array.
    /// Size: 32 bytes (power-of-2 for cache efficiency).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct StateDef
    {
        // === Topology (12 bytes) ===
        public ushort ParentIndex;          // 0xFFFF if root
        public ushort ChildStartIndex;      // First child in States array
        public ushort ChildCount;           // Number of children
        public ushort SiblingIndex;         // Next sibling (for fast traversal)
        public byte Depth;                  // Depth in hierarchy (0 = root)
        public byte Reserved1;              

        // === Regions (4 bytes) ===
        public ushort RegionStartIndex;     // Index into Regions array
        public ushort RegionCount;          // 0 if not orthogonal

        // === Transitions (4 bytes) ===
        public ushort TransitionStartIndex; // Index into Transitions array
        public ushort TransitionCount;      

        // === Execution Hooks (6 bytes) ===
        // These are indices into the LinkerTable
        public ushort EntryActionId;        // 0 = none
        public ushort ExitActionId;         // 0 = none
        public ushort UpdateActionId;       // 0 = none (activity)

        // === Configuration (6 bytes) ===
        public StateFlags Flags;            
        public ushort HistorySlotIndex;     // If HasHistory flag set
        public ushort StableId;             // For save/load (lower 16 bits of GUID hash)

        // Total: 32 bytes
    }
}
```

**Memory Layout Diagram:**
```
Offset  Size  Field
  0      2    ParentIndex
  2      2    ChildStartIndex
  4      2    ChildCount
  6      2    SiblingIndex
  8      1    Depth
  9      1    Reserved1
  10     2    RegionStartIndex
  12     2    RegionCount
  14     2    TransitionStartIndex
  16     2    TransitionCount
  18     2    EntryActionId
  20     2    ExitActionId
  22     2    UpdateActionId
  24     2    Flags (StateFlags)
  26     2    HistorySlotIndex
  28     2    StableId
  30     2    (padding to 32)
Total: 32 bytes
```

#### 1.2.3 TransitionDef

```csharp
namespace FastHSM
{
    /// <summary>
    /// Transition definition.
    /// Size: 16 bytes (aligned).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct TransitionDef
    {
        public ushort SourceStateIndex;     // Source state
        public ushort TargetStateIndex;     // Target state (0xFFFF for internal)
        public ushort TriggerEventId;       // Event that triggers this
        public ushort GuardId;              // Index into LinkerTable (0 = none)
        public ushort EffectActionId;       // Index into LinkerTable (0 = none)
        public TransitionFlags Flags;       // Priority in upper bits
        public ushort SyncGroupId;          // For synchronized transitions (0 = none)
        public ushort StableId;             // For debugging/save

        // Total: 16 bytes
    }
}
```

#### 1.2.4 RegionDef

```csharp
namespace FastHSM
{
    /// <summary>
    /// Orthogonal region definition.
    /// Size: 8 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct RegionDef
    {
        public ushort InitialStateIndex;    // Default active state
        public ushort Priority;             // For deterministic ordering
        public ushort OutputLaneMask;       // Which command lanes written (optimization)
        public ushort Reserved;

        // Total: 8 bytes
    }
}
```

#### 1.2.5 LinkerTableEntry

```csharp
namespace FastHSM
{
    /// <summary>
    /// Entry in the function linker table.
    /// Maps FunctionHash → Runtime Index.
    /// Size: 8 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct LinkerTableEntry
    {
        public uint FunctionHash;           // Hash("MyClass.Attack")
        public ushort SignatureTypeId;      // 0=Guard, 1=Action, 2=Activity
        public ushort Reserved;

        // Total: 8 bytes
    }
}
```

#### 1.2.6 HsmDefinitionBlob

```csharp
namespace FastHSM
{
    /// <summary>
    /// Complete immutable definition blob.
    /// Shared across all instances of the same machine type.
    /// </summary>
    public unsafe class HsmDefinitionBlob
    {
        // === Header ===
        public HsmDefinitionHeader Header;

        // === Core Tables (stored as byte[] internally) ===
        private byte[] _blobData;

        // Accessors return ReadOnlySpan<T> for zero-copy access
        public ReadOnlySpan<StateDef> States => 
            GetTable<StateDef>(Header.StateTableOffset, Header.StateCount);

        public ReadOnlySpan<TransitionDef> Transitions => 
            GetTable<TransitionDef>(Header.TransitionTableOffset, Header.TransitionCount);

        public ReadOnlySpan<RegionDef> Regions => 
            GetTable<RegionDef>(Header.RegionTableOffset, Header.RegionCount);

        public ReadOnlySpan<LinkerTableEntry> LinkerTable => 
            GetTable<LinkerTableEntry>(Header.LinkerTableOffset, Header.LinkerEntryCount);

        // === Helper ===
        private ReadOnlySpan<T> GetTable<T>(uint offset, int count) where T : unmanaged
        {
            var slice = _blobData.AsSpan((int)offset, count * sizeof(T));
            return MemoryMarshal.Cast<byte, T>(slice);
        }

        // === Metadata (optional, debug-only) ===
        public HsmDebugMetadata? DebugMetadata;
    }

    /// <summary>
    /// Debug metadata sidecar (stripped in release).
    /// </summary>
    public class HsmDebugMetadata
    {
        public Dictionary<ushort, string> StateNames;
        public Dictionary<ushort, string> EventNames;
        public Dictionary<uint, string> FunctionNames;
        public Dictionary<ushort, (string FilePath, int Line)> SourceLocations;
    }
}
```

### 1.3 RAM: The Mutable Instance State

#### 1.3.1 InstanceHeader (Common)

```csharp
namespace FastHSM
{
    /// <summary>
    /// Common header for all instance tiers.
    /// Size: 16 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct InstanceHeader
    {
        // === Identity & Safety (8 bytes) ===
        [FieldOffset(0)] public uint MachineId;         // Lower 32 bits of StructureHash
        [FieldOffset(4)] public uint RandomSeed;        // Deterministic RNG state

        // === Versioning (4 bytes) ===
        [FieldOffset(8)] public ushort Generation;      // Increments on reset
        [FieldOffset(10)] public ushort Flags;          // Paused, Overflow, etc.

        // === Execution State (4 bytes) ===
        [FieldOffset(12)] public byte Phase;            // 0=Setup, 1=Timers, 2=RTC, 3=Update
        [FieldOffset(13)] public byte MicroStep;        // RTC depth counter
        [FieldOffset(14)] public byte QueueHead;        // Event queue read cursor
        [FieldOffset(15)] public byte ConsecutiveClamps; // Safety counter
    }

    [Flags]
    public enum InstanceFlags : ushort
    {
        None = 0,
        Paused = 1 << 0,
        QueueOverflow = 1 << 1,
        CriticalCommandOverflow = 1 << 2,
        BudgetExceeded = 1 << 3,
        DebugTrace = 1 << 4,
    }
}
```

#### 1.3.2 HsmInstance64 (Tier 1: Crowd)

```csharp
namespace FastHSM
{
    /// <summary>
    /// Tier 1: Crowd AI (hordes, simple NPCs).
    /// Size: Exactly 64 bytes.
    /// ARCHITECT NOTE: Uses SINGLE SHARED QUEUE due to space constraints.
    /// Priority events overwrite oldest normal events if full.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public unsafe struct HsmInstance64
    {
        // === Header (16 bytes) ===
        [FieldOffset(0)] public InstanceHeader Header;

        // === Active Configuration (4 bytes) ===
        // Max 2 orthogonal regions supported
        [FieldOffset(16)] public fixed ushort ActiveLeafIds[2];

        // === Timers (8 bytes) ===
        // 2 timer slots × 4 bytes = tick deadlines
        [FieldOffset(20)] public fixed uint TimerDeadlines[2];

        // === History/Scratch (4 bytes) ===
        // ARCHITECT NOTE: Shared slots for history OR scratch registers (Q10)
        // Allows simple counters/flags without blackboard overhead
        [FieldOffset(28)] public fixed ushort HistorySlots[2];

        // === Event Queue (32 bytes) ===
        // SINGLE SHARED QUEUE (Tier 1 special case)
        // Can hold 1 full event (24B) with metadata
        // Priority logic: Interrupt events can evict oldest Normal event
        [FieldOffset(32)] public byte QueueHead;        // Read cursor
        [FieldOffset(33)] public byte QueueTail;        // Write cursor (active)
        [FieldOffset(34)] public byte DeferredTail;     // Write cursor (deferred)
        [FieldOffset(35)] public byte EventCount;       // Current count (max 1)
        [FieldOffset(36)] public fixed byte EventBuffer[28]; // 1 full event

        // Total: 64 bytes
    }
}
```

#### 1.3.3 HsmInstance128 (Tier 2: Standard)

```csharp
namespace FastHSM
{
    /// <summary>
    /// Tier 2: Standard enemies, items.
    /// Size: Exactly 128 bytes.
    /// ARCHITECT NOTE: Uses HYBRID QUEUE strategy.
    /// One reserved slot for Interrupt events + shared ring for Normal/Low.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    public unsafe struct HsmInstance128
    {
        // === Header (16 bytes) ===
        [FieldOffset(0)] public InstanceHeader Header;

        // === Active Configuration (8 bytes) ===
        // Max 4 orthogonal regions
        [FieldOffset(16)] public fixed ushort ActiveLeafIds[4];

        // === Timers (16 bytes) ===
        // 4 timer slots
        [FieldOffset(24)] public fixed uint TimerDeadlines[4];

        // === History/Scratch (16 bytes) ===
        // 8 slots (can be used for history OR scratch registers - Q10)
        [FieldOffset(40)] public fixed ushort HistorySlots[8];

        // === Event Queue (72 bytes) ===
        // HYBRID: [0-23] = Reserved for Interrupt (1 event)
        //         [24-71] = Shared ring for Normal/Low (2 events)
        [FieldOffset(56)] public byte QueueHead;
        [FieldOffset(57)] public byte QueueTail;
        [FieldOffset(58)] public byte DeferredTail;
        [FieldOffset(59)] public byte InterruptSlotUsed; // 0 or 1
        [FieldOffset(60)] public fixed byte EventBuffer[68]; // Interrupt + 2 shared

        // Total: 128 bytes
    }
}
```

#### 1.3.4 HsmInstance256 (Tier 3: Hero)

```csharp
namespace FastHSM
{
    /// <summary>
    /// Tier 3: Player characters, bosses.
    /// Size: Exactly 256 bytes.
    /// ARCHITECT NOTE: Uses HYBRID QUEUE strategy.
    /// One reserved slot for Interrupt events + shared ring for Normal/Low.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    public unsafe struct HsmInstance256
    {
        // === Header (16 bytes) ===
        [FieldOffset(0)] public InstanceHeader Header;

        // === Active Configuration (16 bytes) ===
        // Max 8 orthogonal regions
        [FieldOffset(16)] public fixed ushort ActiveLeafIds[8];

        // === Timers (32 bytes) ===
        // 8 timer slots
        [FieldOffset(32)] public fixed uint TimerDeadlines[8];

        // === History/Scratch (32 bytes) ===
        // 16 slots (can be used for history OR scratch registers - Q10)
        [FieldOffset(64)] public fixed ushort HistorySlots[16];

        // === Event Queue (160 bytes) ===
        // HYBRID: [0-23] = Reserved for Interrupt (1 event)
        //         [24-159] = Shared ring for Normal/Low (5-6 events)
        [FieldOffset(96)] public byte QueueHead;
        [FieldOffset(97)] public byte QueueTail;
        [FieldOffset(98)] public byte DeferredTail;
        [FieldOffset(99)] public byte InterruptSlotUsed; // 0 or 1
        [FieldOffset(100)] public fixed byte EventBuffer[156]; // Interrupt + 6 shared

        // Total: 256 bytes
    }
}
```

#### 1.3.5 HsmEvent (Fixed 24-byte)

```csharp
namespace FastHSM
{
    /// <summary>
    /// Fixed-size event structure.
    /// Size: Exactly 24 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct HsmEvent
    {
        // === Header (8 bytes) ===
        [FieldOffset(0)] public ushort EventId;         // Event type ID
        [FieldOffset(2)] public EventPriority Priority; 
        [FieldOffset(3)] public byte Flags;             // IsTimer, IsIndirect, etc.
        [FieldOffset(4)] public uint Timestamp;         // Frame/tick count

        // === Payload (16 bytes) ===
        // Union-style overlapping fields
        [FieldOffset(8)] public float FloatParam;
        [FieldOffset(8)] public int IntParam;
        [FieldOffset(8)] public uint EntityId;
        [FieldOffset(8)] public System.Numerics.Vector3 VectorParam; // 12 bytes
        [FieldOffset(8)] public System.Guid GuidParam;  // 16 bytes

        // Raw bytes for custom serialization
        [FieldOffset(8)] public unsafe fixed byte RawData[16];

        // Total: 24 bytes
    }

    [Flags]
    public enum EventFlags : byte
    {
        None = 0,
        IsTimer = 1 << 0,
        IsSystem = 1 << 1,
        CoalesceByType = 1 << 2,    // Drop older events of same type
        IsIndirect = 1 << 3,        // ID-only event (payload in blackboard)
    }
}
```

### 1.4 Command Buffer Structures

#### 1.4.1 PagedCommandWriter

```csharp
namespace FastHSM
{
    /// <summary>
    /// Thread-local paged command writer.
    /// Uses 4KB pages from a global pool.
    /// </summary>
    public unsafe ref struct HsmCommandWriter
    {
        private CommandPage* _currentPage;
        private int _offset;
        private readonly int _reservedThreshold; // For critical lanes

        /// <summary>
        /// Write a command to the buffer.
        /// Returns false if buffer is full.
        /// </summary>
        public bool Write<TCommand>(CommandLane lane, TCommand cmd) 
            where TCommand : unmanaged
        {
            int commandSize = sizeof(TCommand) + 2; // +2 for header

            // Check capacity
            if (!CanWrite(lane, commandSize))
                return false;

            // Write header
            *(_currentPage->Data + _offset) = (byte)lane;
            *(_currentPage->Data + _offset + 1) = (byte)commandSize;

            // Write payload
            *(TCommand*)(_currentPage->Data + _offset + 2) = cmd;

            _offset += commandSize;
            return true;
        }

        private bool CanWrite(CommandLane lane, int size)
        {
            const int PageSize = 4096;
            int available = PageSize - _offset;

            if (available < size)
            {
                // Need new page
                if (!AllocateNewPage())
                    return false;
                available = PageSize;
            }

            // Check critical lane reservation
            bool isCritical = IsCriticalLane(lane);
            if (!isCritical && (_offset + size > _reservedThreshold))
                return false; // Non-critical lanes can't use reserved space

            return true;
        }

        private bool IsCriticalLane(CommandLane lane)
        {
            return lane == CommandLane.Navigation || lane == CommandLane.Gameplay;
        }

        private bool AllocateNewPage()
        {
            // Request from global pool (not shown)
            // Link to previous page
            // Reset offset
            return true; // Stub
        }
    }

    /// <summary>
    /// 4KB command page.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct CommandPage
    {
        public CommandPage* Next;
        public int UsedBytes;
        public fixed byte Data[4088]; // 4096 - 8 (pointer)
    }
}
```

### 1.5 Scratch Registers (Local State Storage)

**ARCHITECT DECISION (Q10):** Approved Option B - Scratch Registers

The `HistorySlots` arrays in instance structs can serve dual purposes:
1. **History Storage:** Store last active leaf state for history transitions
2. **Scratch Registers:** General-purpose local state (counters, flags, timers)

**Capacity:**
- Tier 1: 2 slots (4 bytes)
- Tier 2: 8 slots (16 bytes)
- Tier 3: 16 slots (32 bytes)

**Usage:**
- States/composites declare which slots they need (via compiler analysis)
- Compiler validates no conflicts (two simultaneously-active states using same slot)
- Runtime accesses via index: `instance.HistorySlots[slotIndex]`

**Rationale:** Simple counters (loops, retry attempts) and flags are common in AI. Blackboard access is too slow for these. 16-32B overhead for scratch space is acceptable and avoids over-engineering.

### 1.6 RNG Wrapper

```csharp
namespace FastHSM
{
    /// <summary>
    /// Deterministic RNG wrapper.
    /// Advances seed in-place on each call.
    /// ARCHITECT DIRECTIVE: Debug builds track access count for replay validation.
    /// </summary>
    public unsafe ref struct HsmRng
    {
        private uint* _seedPtr; // Points into Instance.Header.RandomSeed
        
        #if DEBUG
        private int* _debugAccessCount; // Debug sidecar (per-entity)
        #endif

        public HsmRng(uint* seedPtr)
        {
            _seedPtr = seedPtr;
            #if DEBUG
            _debugAccessCount = null; // Set by debug system if tracking enabled
            #endif
        }

        #if DEBUG
        /// <summary>
        /// Attach debug access counter (optional, debug builds only).
        /// </summary>
        public void AttachDebugCounter(int* counterPtr)
        {
            _debugAccessCount = counterPtr;
        }
        #endif

        /// <summary>
        /// XorShift32 - fast, deterministic PRNG.
        /// CRITICAL: This advances the seed state. Each call must be deterministic.
        /// </summary>
        public float NextFloat()
        {
            uint x = *_seedPtr;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            *_seedPtr = x;

            #if DEBUG
            // Track RNG usage for replay validation
            if (_debugAccessCount != null)
                (*_debugAccessCount)++;
            #endif

            return (x >> 8) * (1.0f / 16777216.0f); // [0, 1)
        }

        public int NextInt(int min, int max)
        {
            return min + (int)(NextFloat() * (max - min));
        }

        public bool NextBool(float probability = 0.5f)
        {
            return NextFloat() < probability;
        }
    }
}
```

---

## 2. Compiler: Flattening, Linking, and Validation

### 2.1 Compiler Architecture

```
┌────────────────────────────────────────────────────┐
│            HsmCompiler Pipeline                    │
├────────────────────────────────────────────────────┤
│                                                     │
│  1. Parse (JSON → BuilderGraph)                    │
│     ↓                                               │
│  2. Normalize (Resolve references, assign IDs)     │
│     ↓                                               │
│  3. Validate (Check constraints, budgets)          │
│     ↓                                               │
│  4. Flatten (Graph → Flat arrays)                  │
│     ↓                                               │
│  5. Hash (Structure + Parameter hashes)            │
│     ↓                                               │
│  6. Emit (Write HsmDefinitionBlob)                 │
│                                                     │
└────────────────────────────────────────────────────┘
```

### 2.2 Input: Authoring Format (JSON)

```json
{
  "machine": "SoldierCombat",
  "tier": "Standard_128B",
  "states": [
    {
      "id": "root",
      "type": "composite",
      "initial": "idle",
      "children": ["idle", "combat"]
    },
    {
      "id": "idle",
      "type": "leaf",
      "onEntry": "StartPatrol",
      "onUpdate": "PatrolActivity"
    },
    {
      "id": "combat",
      "type": "composite",
      "regions": [
        {
          "name": "Movement",
          "initial": "approach"
        },
        {
          "name": "Weapon",
          "initial": "ready"
        }
      ],
      "children": ["approach", "ready", "firing"]
    }
  ],
  "transitions": [
    {
      "source": "idle",
      "target": "combat",
      "trigger": "EnemySighted",
      "guard": "HasAmmo"
    },
    {
      "source": "combat",
      "target": "idle",
      "trigger": "NoEnemies",
      "isInterrupt": true
    }
  ]
}
```

### 2.3 Builder Graph (Intermediate Representation)

```csharp
namespace FastHSM.Compiler
{
    /// <summary>
    /// Mutable builder graph for authoring.
    /// </summary>
    public class BuilderState
    {
        public string Name;
        public Guid StableId;                    // Immutable ID
        public BuilderState? Parent;
        public List<BuilderState> Children;
        public List<BuilderTransition> Transitions;
        public List<BuilderRegion> Regions;

        // Callbacks
        public string? OnEntry;
        public string? OnExit;
        public string? OnUpdate;

        // Flags
        public bool HasHistory;
        public bool IsDeepHistory;
    }

    public class BuilderTransition
    {
        public Guid StableId;
        public BuilderState Source;
        public BuilderState Target;
        public string TriggerEvent;
        public string? Guard;
        public string? Effect;
        public int Priority;
        public bool IsInterrupt;
        public bool IsInternal;
    }

    public class BuilderRegion
    {
        public string Name;
        public int Priority;
        public BuilderState InitialState;
    }

    public class BuilderMachine
    {
        public string Name;
        public TierSize Tier;
        public BuilderState Root;
        public Dictionary<string, BuilderState> StatesByName;
        public Dictionary<string, ushort> EventIds;
    }
}
```

### 2.4 Normalization Pass

```csharp
namespace FastHSM.Compiler
{
    /// <summary>
    /// Normalizes the builder graph.
    /// </summary>
    public class Normalizer
    {
        public BuilderMachine Normalize(BuilderMachine input)
        {
            // 1. Ensure all states have stable IDs
            AssignStableIds(input);

            // 2. Compute depths
            ComputeDepths(input.Root, 0);

            // 3. Resolve all string references to object references
            ResolveReferences(input);

            // 4. Assign event IDs (deterministic sort)
            AssignEventIds(input);

            // 5. Assign region priorities if not explicit
            AssignRegionPriorities(input);

            return input;
        }

        private void AssignStableIds(BuilderMachine machine)
        {
            foreach (var state in machine.StatesByName.Values)
            {
                if (state.StableId == Guid.Empty)
                    state.StableId = Guid.NewGuid();
            }

            // Also assign to transitions
            foreach (var state in machine.StatesByName.Values)
            {
                foreach (var trans in state.Transitions)
                {
                    if (trans.StableId == Guid.Empty)
                        trans.StableId = Guid.NewGuid();
                }
            }
        }

        private void ComputeDepths(BuilderState state, int depth)
        {
            state.Depth = depth;
            foreach (var child in state.Children)
                ComputeDepths(child, depth + 1);
        }
    }
}
```

### 2.5 Validation Pass

```csharp
namespace FastHSM.Compiler
{
    /// <summary>
    /// Validates the machine against design constraints.
    /// </summary>
    public class Validator
    {
        private readonly List<ValidationError> _errors = new();
        private readonly List<ValidationWarning> _warnings = new();

        public ValidationResult Validate(BuilderMachine machine)
        {
            // === Hard Constraints ===
            CheckMaxDepth(machine);
            CheckTierBudget(machine);
            CheckStateReachability(machine);
            CheckTransitionValidity(machine);
            CheckSlotConflicts(machine);

            // === Soft Warnings ===
            CheckUnusedStates(machine);
            CheckOrphanTransitions(machine);

            return new ValidationResult(_errors, _warnings);
        }

        private void CheckMaxDepth(BuilderMachine machine)
        {
            const int MaxDepth = 16;
            int maxFound = FindMaxDepth(machine.Root);

            if (maxFound > MaxDepth)
            {
                _errors.Add(new ValidationError(
                    $"Max depth {maxFound} exceeds limit {MaxDepth}"));
            }
        }

        private void CheckTierBudget(BuilderMachine machine)
        {
            // Count required regions, timers, history slots
            int maxRegions = 0;
            int timerSlots = 0;
            int historySlots = 0;

            foreach (var state in machine.StatesByName.Values)
            {
                maxRegions = Math.Max(maxRegions, state.Regions.Count);
                
                if (state.HasHistory)
                    historySlots++;
            }

            // Compute required instance size
            int required = ComputeInstanceSize(maxRegions, timerSlots, historySlots);

            if (required > (int)machine.Tier)
            {
                if (StrictMode)
                {
                    _errors.Add(new ValidationError(
                        $"Machine requires {required}B but tier is {machine.Tier}"));
                }
                else
                {
                    _warnings.Add(new ValidationWarning(
                        $"Auto-promoting tier to next size"));
                    // Auto-promote
                }
            }
        }

        private void CheckSlotConflicts(BuilderMachine machine)
        {
            // Build exclusion graph: which states can be active simultaneously
            var exclusionGraph = BuildExclusionGraph(machine);

            // For each timer/history slot, check that no two simultaneous
            // states try to use it
            foreach (var state in machine.StatesByName.Values)
            {
                if (state.OnUpdate != null) // Uses timer
                {
                    foreach (var other in machine.StatesByName.Values)
                    {
                        if (other == state) continue;

                        if (other.OnUpdate != null && 
                            !exclusionGraph.AreMutuallyExclusive(state, other))
                        {
                            _errors.Add(new ValidationError(
                                $"Timer slot conflict: {state.Name} and {other.Name}"));
                        }
                    }
                }
            }
        }

        private void ValidateIndirectEvents(BuilderMachine machine)
        {
            // ARCHITECT DIRECTIVE: Validate ID-Only events
            foreach (var eventDef in machine.EventDefinitions.Values)
            {
                if (eventDef.PayloadSize > 16)
                {
                    if (!eventDef.IsIndirect)
                    {
                        _errors.Add(new ValidationError(
                            $"Event '{eventDef.Name}' has payload size {eventDef.PayloadSize} " +
                            $"bytes (>16). Must be marked IsIndirect (ID-only)."));
                    }

                    if (eventDef.IsDeferrable)
                    {
                        _warnings.Add(new ValidationWarning(
                            $"Event '{eventDef.Name}' is marked both IsIndirect and " +
                            $"IsDeferrable. This may cause dangling references if payload " +
                            $"is stored in ephemeral memory (command buffer). " +
                            $"Payload MUST reside in Blackboard."));
                    }
                }
            }
        }
    }
}
```

### 2.6 Flattening Pass

```csharp
namespace FastHSM.Compiler
{
    /// <summary>
    /// Flattens the graph into ROM arrays.
    /// </summary>
    public class Flattener
    {
        public FlattenedData Flatten(BuilderMachine machine)
        {
            var result = new FlattenedData();

            // === 1. Flatten States ===
            // Depth-first traversal, assigning indices
            var stateToIndex = new Dictionary<BuilderState, ushort>();
            ushort nextIndex = 0;

            FlattenStateRecursive(machine.Root, ref nextIndex, stateToIndex, result);

            // === 2. Assign History Slots (STABLE SORTING) ===
            // ARCHITECT DIRECTIVE: Sort by StableID, NOT name or declaration order
            // This maximizes hot-reload compatibility
            AssignHistorySlots(machine, stateToIndex, result);

            // === 3. Flatten Transitions ===
            foreach (var state in machine.StatesByName.Values)
            {
                foreach (var trans in state.Transitions)
                {
                    result.Transitions.Add(ConvertTransition(trans, stateToIndex));
                }
            }

            // Sort transitions by source state, then priority
            result.Transitions.Sort((a, b) =>
            {
                int cmp = a.SourceStateIndex.CompareTo(b.SourceStateIndex);
                if (cmp != 0) return cmp;

                // Extract priority from flags
                int prioA = (int)(a.Flags & TransitionFlags.Priority_Mask) >> 12;
                int prioB = (int)(b.Flags & TransitionFlags.Priority_Mask) >> 12;
                return prioB.CompareTo(prioA); // Higher priority first
            });

            // Update TransitionStartIndex in States
            UpdateTransitionIndices(result);

            // === 4. Flatten Regions ===
            foreach (var state in machine.StatesByName.Values)
            {
                if (state.Regions.Count > 0)
                {
                    foreach (var region in state.Regions)
                    {
                        result.Regions.Add(ConvertRegion(region, stateToIndex));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Assign history slots using STABLE SORTING by StableID.
        /// CRITICAL: This prevents slot index changes on hot reload when states are reordered.
        /// </summary>
        private void AssignHistorySlots(
            BuilderMachine machine,
            Dictionary<BuilderState, ushort> stateToIndex,
            FlattenedData result)
        {
            // Collect all states with history, sorted by StableID
            var historyStates = machine.StatesByName.Values
                .Where(s => s.HasHistory)
                .OrderBy(s => s.StableId) // CRITICAL: Stable sort order
                .ToList();

            ushort slotIndex = 0;
            foreach (var state in historyStates)
            {
                var stateIndex = stateToIndex[state];
                var stateDef = result.States[stateIndex];
                stateDef.HistorySlotIndex = slotIndex++;
                result.States[stateIndex] = stateDef; // Update (struct copy)
            }

            result.HistorySlotCount = slotIndex;
        }

        private void FlattenStateRecursive(
            BuilderState state,
            ref ushort index,
            Dictionary<BuilderState, ushort> stateToIndex,
            FlattenedData result)
        {
            // Assign index
            ushort myIndex = index++;
            stateToIndex[state] = myIndex;

            // Convert to StateDef
            var def = new StateDef
            {
                ParentIndex = state.Parent != null ? stateToIndex[state.Parent] : (ushort)0xFFFF,
                ChildStartIndex = (ushort)(state.Children.Count > 0 ? index : 0),
                ChildCount = (ushort)state.Children.Count,
                Depth = (byte)state.Depth,
                Flags = ComputeFlags(state),
                // ... other fields
            };

            result.States.Add(def);

            // Recurse children
            foreach (var child in state.Children)
            {
                FlattenStateRecursive(child, ref index, stateToIndex, result);
            }

            // Update sibling links
            if (state.Children.Count > 0)
            {
                for (int i = 0; i < state.Children.Count - 1; i++)
                {
                    var childIndex = stateToIndex[state.Children[i]];
                    var nextSiblingIndex = stateToIndex[state.Children[i + 1]];
                    result.States[childIndex].SiblingIndex = nextSiblingIndex;
                }
            }
        }

        private StateFlags ComputeFlags(BuilderState state)
        {
            var flags = StateFlags.None;
            if (state.Children.Count > 0) flags |= StateFlags.IsComposite;
            if (state.HasHistory) flags |= StateFlags.HasHistory;
            if (state.IsDeepHistory) flags |= StateFlags.DeepHistory;
            if (state.Regions.Count > 0) flags |= StateFlags.HasRegions;
            if (state.OnUpdate != null) flags |= StateFlags.HasActivity;
            return flags;
        }
    }

    public class FlattenedData
    {
        public List<StateDef> States = new();
        public List<TransitionDef> Transitions = new();
        public List<RegionDef> Regions = new();
        public Dictionary<uint, ushort> FunctionHashToIndex = new();
    }
}
```

### 2.7 Linker Table Generation

```csharp
namespace FastHSM.Compiler
{
    /// <summary>
    /// Builds the function linker table.
    /// </summary>
    public class LinkerTableBuilder
    {
        public List<LinkerTableEntry> Build(BuilderMachine machine)
        {
            var entries = new List<LinkerTableEntry>();
            var seen = new HashSet<string>();

            // Collect all unique function names
            foreach (var state in machine.StatesByName.Values)
            {
                if (state.OnEntry != null) CollectFunction(state.OnEntry, seen, entries);
                if (state.OnExit != null) CollectFunction(state.OnExit, seen, entries);
                if (state.OnUpdate != null) CollectFunction(state.OnUpdate, seen, entries);

                foreach (var trans in state.Transitions)
                {
                    if (trans.Guard != null) CollectFunction(trans.Guard, seen, entries);
                    if (trans.Effect != null) CollectFunction(trans.Effect, seen, entries);
                }
            }

            // Sort deterministically by hash
            entries.Sort((a, b) => a.FunctionHash.CompareTo(b.FunctionHash));

            return entries;
        }

        private void CollectFunction(
            string name,
            HashSet<string> seen,
            List<LinkerTableEntry> entries)
        {
            if (!seen.Add(name)) return;

            uint hash = ComputeFunctionHash(name);

            entries.Add(new LinkerTableEntry
            {
                FunctionHash = hash,
                SignatureTypeId = 1, // Detect from name convention or metadata
            });
        }

        /// <summary>
        /// Stable hash of method name (e.g., "Combat.Attack").
        /// </summary>
        private uint ComputeFunctionHash(string name)
        {
            // Use a stable hash algorithm (e.g., FNV-1a)
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in name)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
                return hash;
            }
        }
    }
}
```

### 2.8 Hash Computation

```csharp
namespace FastHSM.Compiler
{
    /// <summary>
    /// Computes structure and parameter hashes.
    /// </summary>
    public class HashComputer
    {
        public (ulong structureHash, ulong parameterHash) Compute(FlattenedData data)
        {
            var structureHasher = new System.IO.Hashing.XxHash64();
            var paramHasher = new System.IO.Hashing.XxHash64();

            // === Structure Hash ===
            // Include topology only
            foreach (var state in data.States)
            {
                structureHasher.Append(BitConverter.GetBytes(state.ParentIndex));
                structureHasher.Append(BitConverter.GetBytes(state.ChildCount));
                structureHasher.Append(BitConverter.GetBytes(state.RegionCount));
                structureHasher.Append(BitConverter.GetBytes((ushort)state.Flags));
            }

            // === Parameter Hash ===
            // Include transition targets, guards, priorities
            foreach (var trans in data.Transitions)
            {
                paramHasher.Append(BitConverter.GetBytes(trans.TargetStateIndex));
                paramHasher.Append(BitConverter.GetBytes(trans.GuardId));
                paramHasher.Append(BitConverter.GetBytes(trans.EffectActionId));
                paramHasher.Append(BitConverter.GetBytes((ushort)trans.Flags));
            }

            return (structureHasher.GetCurrentHashAsUInt64(), 
                    paramHasher.GetCurrentHashAsUInt64());
        }
    }
}
```

### 2.9 Blob Emitter

```csharp
namespace FastHSM.Compiler
{
    /// <summary>
    /// Writes the final HsmDefinitionBlob.
    /// </summary>
    public class BlobEmitter
    {
        public HsmDefinitionBlob Emit(FlattenedData data, BuilderMachine machine)
        {
            // Compute total size
            int headerSize = 64;
            int statesSize = data.States.Count * 32;
            int transitionsSize = data.Transitions.Count * 16;
            int regionsSize = data.Regions.Count * 8;
            int linkerSize = data.FunctionHashToIndex.Count * 8;

            int totalSize = headerSize + statesSize + transitionsSize + regionsSize + linkerSize;

            // Allocate blob buffer
            var buffer = new byte[totalSize];

            // Write header
            var header = new HsmDefinitionHeader
            {
                Magic = 0x4653484D, // 'FHSM'
                Version = 1,
                StructureHash = data.StructureHash,
                ParameterHash = data.ParameterHash,
                Tier = machine.Tier,
                MaxDepth = data.MaxDepth,
                StateCount = (ushort)data.States.Count,
                TransitionCount = (ushort)data.Transitions.Count,
                RegionCount = (ushort)data.Regions.Count,
                StateTableOffset = headerSize,
                TransitionTableOffset = (uint)(headerSize + statesSize),
                RegionTableOffset = (uint)(headerSize + statesSize + transitionsSize),
                LinkerTableOffset = (uint)(headerSize + statesSize + transitionsSize + regionsSize),
                LinkerEntryCount = (ushort)data.FunctionHashToIndex.Count,
            };

            // Write header
            MemoryMarshal.Write(buffer, ref header);

            // Write states
            int offset = headerSize;
            foreach (var state in data.States)
            {
                MemoryMarshal.Write(buffer.AsSpan(offset), ref state);
                offset += 32;
            }

            // Write transitions
            foreach (var trans in data.Transitions)
            {
                MemoryMarshal.Write(buffer.AsSpan(offset), ref trans);
                offset += 16;
            }

            // ... (similar for regions, linker table)

            // Create blob
            var blob = new HsmDefinitionBlob
            {
                Header = header,
                _blobData = buffer,
            };

            return blob;
        }
    }
}
```

---

## 3. Kernel: UpdateBatch Logic

### 3.1 Kernel Entry Point

```csharp
using System.Runtime.CompilerServices;

namespace FastHSM.Runtime
{
    /// <summary>
    /// Core HSM execution kernel.
    /// Stateless, operates on immutable ROM + mutable RAM.
    /// ARCHITECT DIRECTIVE: "Thin Shim" pattern to prevent I-cache bloat.
    /// </summary>
    public static unsafe class HsmKernel
    {
        /// <summary>
        /// Execute one tick for a batch of instances.
        /// THIN GENERIC SHIM: Inlined wrapper that casts to void* and calls core.
        /// CRITICAL: Must be AggressiveInlining to eliminate call overhead.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateBatch<TContext>(
            HsmDefinitionBlob definition,
            Span<HsmInstance128> instances,  // Example tier
            in TContext context,
            ref HsmCommandWriter commands,
            float deltaTime,
            double currentTime)
            where TContext : unmanaged
        {
            // Cast context to void* for non-generic core
            fixed (TContext* ctxPtr = &context)
            {
                // For each instance
                for (int i = 0; i < instances.Length; i++)
                {
                    fixed (HsmInstance128* instancePtr = &instances[i])
                    {
                        UpdateSingleCore(
                            definition,
                            instancePtr,
                            sizeof(HsmInstance128),
                            ctxPtr,
                            ref commands,
                            deltaTime,
                            currentTime);
                    }
                }
            }
        }

        /// <summary>
        /// NON-GENERIC CORE: Compiled once, no template expansion.
        /// All heavy logic lives here to avoid I-cache bloat.
        /// </summary>
        private static void UpdateSingleCore(
            HsmDefinitionBlob definition,
            void* instancePtr,
            int instanceSize,
            void* contextPtr,  // No 'in' here - already a pointer
            ref HsmCommandWriter commands,
            float deltaTime,
            double currentTime)
        {
            // Phase 0: Setup
            var header = (InstanceHeader*)instancePtr;

            if (header->MachineId != (uint)definition.Header.StructureHash)
            {
                // Hard reload
                ResetInstance(instancePtr, instanceSize, definition);
            }

            // Phase 1: Timers → Event Injection
            ProcessTimers(definition, instancePtr, currentTime);

            // Phase 2: RTC Loop
            ExecuteRTCLoop(definition, instancePtr, contextPtr, ref commands);

            // Phase 3: Update/Activities
            ExecuteUpdates(definition, instancePtr, contextPtr, ref commands, deltaTime);
        }
    }
}
```

### 3.2 Phase 1: Timers

```csharp
namespace FastHSM.Runtime
{
    public static partial class HsmKernel
    {
        private static unsafe void ProcessTimers(
            HsmDefinitionBlob definition,
            void* instancePtr,
            double currentTime)
        {
            var header = (InstanceHeader*)instancePtr;

            // Access timer slots based on tier
            // (For HsmInstance128, timers start at offset 24)
            uint* timers = (uint*)((byte*)instancePtr + 24);
            int timerCount = definition.Header.TimerSlotCount;

            for (int i = 0; i < timerCount; i++)
            {
                uint deadline = timers[i];
                if (deadline == 0) continue; // Inactive

                if (currentTime >= deadline)
                {
                    // Timer expired → enqueue event
                    var evt = new HsmEvent
                    {
                        EventId = (ushort)(1000 + i), // Timer event IDs
                        Priority = EventPriority.Normal,
                        Flags = (byte)EventFlags.IsTimer,
                        Timestamp = (uint)currentTime,
                    };

                    EnqueueEvent(instancePtr, evt, isDeferred: false);

                    // Clear timer
                    timers[i] = 0;
                }
            }
        }
    }
}
```

### 3.3 Phase 2: RTC Loop

```csharp
namespace FastHSM.Runtime
{
    public static partial class HsmKernel
    {
        private static unsafe void ExecuteRTCLoop(
            HsmDefinitionBlob definition,
            void* instancePtr,
            in void* contextPtr,
            ref HsmCommandWriter commands)
        {
            var header = (InstanceHeader*)instancePtr;
            const int MaxMicrosteps = 32; // Budget

            int microstepCount = 0;

            while (microstepCount < MaxMicrosteps)
            {
                // 2A: Pop event
                if (!TryPopEvent(instancePtr, out HsmEvent evt))
                    break; // Queue empty

                // 2B: Resolve transition
                if (!TryResolveTransition(
                    definition, 
                    instancePtr, 
                    evt, 
                    contextPtr, 
                    out TransitionCandidate candidate))
                {
                    // No transition found → consume event
                    continue;
                }

                // 2C: Execute transition (atomic)
                ExecuteTransition(
                    definition,
                    instancePtr,
                    candidate,
                    contextPtr,
                    ref commands);

                microstepCount++;

                // 2D: Merge deferred queue at RTC boundary
                MergeDeferredQueue(instancePtr);
            }

            // Check for budget exceeded
            if (microstepCount >= MaxMicrosteps)
            {
                header->Flags |= (ushort)InstanceFlags.BudgetExceeded;
                header->ConsecutiveClamps++;

                // Fail-safe after N consecutive clamps
                if (header->ConsecutiveClamps > 5)
                {
                    // Transition to fail-safe state (index 0)
                    ForceTransitionToFailSafe(definition, instancePtr);
                }
            }
            else
            {
                header->ConsecutiveClamps = 0;
            }
        }

        private struct TransitionCandidate
        {
            public ushort TransitionIndex;
            public ushort RegionIndex;
            public byte Priority;
        }
    }
}
```

### 3.4 Transition Resolution

```csharp
namespace FastHSM.Runtime
{
    public static partial class HsmKernel
    {
        private static unsafe bool TryResolveTransition(
            HsmDefinitionBlob definition,
            void* instancePtr,
            HsmEvent evt,
            in void* contextPtr,
            out TransitionCandidate candidate)
        {
            candidate = default;

            // Get active configuration
            var regions = GetActiveRegions(definition, instancePtr);

            // Step A: Check global interrupts
            var globalTransitions = definition.GlobalTransitions; // (from ROM)
            foreach (ref readonly var trans in globalTransitions)
            {
                if (trans.TriggerEventId == evt.EventId)
                {
                    if (EvaluateGuard(definition, trans.GuardId, instancePtr, contextPtr))
                    {
                        candidate = new TransitionCandidate
                        {
                            TransitionIndex = trans.Index,
                            Priority = 255, // Highest
                        };
                        return true;
                    }
                }
            }

            // Step B: Check per-region transitions (child-first bubble)
            var candidates = stackalloc TransitionCandidate[8]; // Max regions
            int candidateCount = 0;

            for (int r = 0; r < regions.Length; r++)
            {
                ushort leafId = regions[r];
                if (leafId == 0xFFFF) continue;

                // Bubble up from leaf to root
                ushort currentStateId = leafId;
                while (currentStateId != 0xFFFF)
                {
                    ref readonly var state = ref definition.States[currentStateId];

                    // Check this state's transitions
                    for (int t = 0; t < state.TransitionCount; t++)
                    {
                        ref readonly var trans = ref definition.Transitions[
                            state.TransitionStartIndex + t];

                        if (trans.TriggerEventId == evt.EventId)
                        {
                            if (EvaluateGuard(definition, trans.GuardId, instancePtr, contextPtr))
                            {
                                // Found enabled transition
                                candidates[candidateCount++] = new TransitionCandidate
                                {
                                    TransitionIndex = (ushort)(state.TransitionStartIndex + t),
                                    RegionIndex = (ushort)r,
                                    Priority = ExtractPriority(trans.Flags),
                                };

                                goto NextRegion; // First-found in this region
                            }
                        }
                    }

                    // Move to parent
                    currentStateId = state.ParentIndex;
                }

                NextRegion:;
            }

            // Step C: Arbitrate multiple candidates (if any)
            if (candidateCount == 0)
                return false;

            if (candidateCount == 1)
            {
                candidate = candidates[0];
                return true;
            }

            // Multiple candidates → apply deterministic winner rule
            candidate = ArbitrateTransitions(candidates, candidateCount);
            return true;
        }

        private static byte ExtractPriority(TransitionFlags flags)
        {
            return (byte)(((ushort)flags & 0xF000) >> 12);
        }

        private static TransitionCandidate ArbitrateTransitions(
            TransitionCandidate* candidates,
            int count)
        {
            // Sort by: Priority (desc), RegionIndex (asc), TransitionIndex (asc)
            int bestIndex = 0;
            for (int i = 1; i < count; i++)
            {
                if (candidates[i].Priority > candidates[bestIndex].Priority)
                    bestIndex = i;
                else if (candidates[i].Priority == candidates[bestIndex].Priority)
                {
                    if (candidates[i].RegionIndex < candidates[bestIndex].RegionIndex)
                        bestIndex = i;
                    else if (candidates[i].RegionIndex == candidates[bestIndex].RegionIndex)
                    {
                        if (candidates[i].TransitionIndex < candidates[bestIndex].TransitionIndex)
                            bestIndex = i;
                    }
                }
            }

            return candidates[bestIndex];
        }
    }
}
```

### 3.5 Transition Execution (LCA)

```csharp
namespace FastHSM.Runtime
{
    public static partial class HsmKernel
    {
        private static unsafe void ExecuteTransition(
            HsmDefinitionBlob definition,
            void* instancePtr,
            TransitionCandidate candidate,
            in void* contextPtr,
            ref HsmCommandWriter commands)
        {
            ref readonly var trans = ref definition.Transitions[candidate.TransitionIndex];

            ushort sourceLeaf = GetActiveLeaf(instancePtr, candidate.RegionIndex);
            ushort targetLeaf = trans.TargetStateIndex;

            // Compute LCA
            ushort lca = ComputeLCA(definition, sourceLeaf, targetLeaf);

            // Phase 1: Exit (deepest → shallowest)
            ExecuteExitPath(definition, instancePtr, sourceLeaf, lca, contextPtr, ref commands);

            // Phase 2: Effect
            if (trans.EffectActionId != 0)
            {
                InvokeAction(definition, trans.EffectActionId, instancePtr, contextPtr, ref commands);
            }

            // Phase 3: Entry (shallowest → deepest)
            ushort actualTarget = targetLeaf;

            // Check for history redirect
            if ((trans.Flags & TransitionFlags.TargetsHistory) != 0)
            {
                actualTarget = RestoreHistory(definition, instancePtr, targetLeaf);
            }

            ExecuteEntryPath(definition, instancePtr, lca, actualTarget, contextPtr, ref commands);

            // Update active leaf
            SetActiveLeaf(instancePtr, candidate.RegionIndex, actualTarget);
        }

        private static ushort ComputeLCA(
            HsmDefinitionBlob definition,
            ushort stateA,
            ushort stateB)
        {
            // Walk both paths up to same depth
            ref readonly var defA = ref definition.States[stateA];
            ref readonly var defB = ref definition.States[stateB];

            int depthA = defA.Depth;
            int depthB = defB.Depth;

            // Equalize depths
            while (depthA > depthB)
            {
                stateA = definition.States[stateA].ParentIndex;
                depthA--;
            }
            while (depthB > depthA)
            {
                stateB = definition.States[stateB].ParentIndex;
                depthB--;
            }

            // Walk up together until they match
            while (stateA != stateB)
            {
                stateA = definition.States[stateA].ParentIndex;
                stateB = definition.States[stateB].ParentIndex;
            }

            return stateA;
        }

        private static unsafe void ExecuteExitPath(
            HsmDefinitionBlob definition,
            void* instancePtr,
            ushort from,
            ushort toLCA,
            in void* contextPtr,
            ref HsmCommandWriter commands)
        {
            // Collect path (deepest first)
            var path = stackalloc ushort[16]; // Max depth
            int pathLen = 0;

            ushort current = from;
            while (current != toLCA && current != 0xFFFF)
            {
                path[pathLen++] = current;
                current = definition.States[current].ParentIndex;
            }

            // Execute exits in order
            for (int i = 0; i < pathLen; i++)
            {
                ref readonly var state = ref definition.States[path[i]];

                // Save history if needed
                if ((state.Flags & StateFlags.HasHistory) != 0)
                {
                    SaveHistory(definition, instancePtr, path[i], from);
                }

                // Cancel timers owned by this state
                CancelTimers(instancePtr, path[i]);

                // Call OnExit
                if (state.ExitActionId != 0)
                {
                    InvokeAction(definition, state.ExitActionId, instancePtr, contextPtr, ref commands);
                }
            }
        }

        private static unsafe void ExecuteEntryPath(
            HsmDefinitionBlob definition,
            void* instancePtr,
            ushort fromLCA,
            ushort to,
            in void* contextPtr,
            ref HsmCommandWriter commands)
        {
            // Collect path (shallowest first)
            var path = stackalloc ushort[16];
            int pathLen = 0;

            ushort current = to;
            while (current != fromLCA && current != 0xFFFF)
            {
                path[pathLen++] = current;
                current = definition.States[current].ParentIndex;
            }

            // Reverse path (now shallowest → deepest)
            for (int i = pathLen - 1; i >= 0; i--)
            {
                ref readonly var state = ref definition.States[path[i]];

                // Call OnEntry
                if (state.EntryActionId != 0)
                {
                    InvokeAction(definition, state.EntryActionId, instancePtr, contextPtr, ref commands);
                }
            }
        }
    }
}
```

### 3.6 Phase 3: Update/Activities

```csharp
namespace FastHSM.Runtime
{
    public static partial class HsmKernel
    {
        private static unsafe void ExecuteUpdates(
            HsmDefinitionBlob definition,
            void* instancePtr,
            in void* contextPtr,
            ref HsmCommandWriter commands,
            float deltaTime)
        {
            // Get active configuration
            var regions = GetActiveRegions(definition, instancePtr);

            foreach (ushort leafId in regions)
            {
                if (leafId == 0xFFFF) continue;

                // Walk up from leaf, executing all OnUpdate actions
                ushort current = leafId;
                while (current != 0xFFFF)
                {
                    ref readonly var state = ref definition.States[current];

                    if (state.UpdateActionId != 0)
                    {
                        InvokeAction(definition, state.UpdateActionId, instancePtr, contextPtr, ref commands);
                    }

                    // Also execute composite activities if HasActivity flag
                    if ((state.Flags & StateFlags.HasActivity) != 0 && state.UpdateActionId != 0)
                    {
                        // Already called above
                    }

                    current = state.ParentIndex;
                }
            }
        }
    }
}
```

### 3.7 Action Invocation (via Dispatch Table)

```csharp
namespace FastHSM.Runtime
{
    /// <summary>
    /// Dispatch table populated by the bootstrapper.
    /// Maps function IDs to actual function pointers.
    /// </summary>
    public unsafe struct HsmDispatchTable
    {
        public delegate* unmanaged[Cdecl]<void*, void*, void*, bool>* Guards;
        public delegate* unmanaged[Cdecl]<void*, void*, void*, void>* Actions;

        // Indexing: DispatchTable.Guards[guardId](instancePtr, contextPtr, commandsPtr)
    }

    public static partial class HsmKernel
    {
        private static HsmDispatchTable s_dispatchTable; // Set by bootstrapper

        private static unsafe bool EvaluateGuard(
            HsmDefinitionBlob definition,
            ushort guardId,
            void* instancePtr,
            in void* contextPtr)
        {
            if (guardId == 0) return true; // No guard = always true

            var guardFn = s_dispatchTable.Guards[guardId];
            return guardFn(instancePtr, (void*)contextPtr, null);
        }

        private static unsafe void InvokeAction(
            HsmDefinitionBlob definition,
            ushort actionId,
            void* instancePtr,
            in void* contextPtr,
            ref HsmCommandWriter commands)
        {
            if (actionId == 0) return;

            fixed (HsmCommandWriter* cmdPtr = &commands)
            {
                var actionFn = s_dispatchTable.Actions[actionId];
                actionFn(instancePtr, (void*)contextPtr, cmdPtr);
            }
        }
    }
}
```

---

## 4. Tooling: Hot Reload and Debug Tracing

### 4.1 Hot Reload System

```csharp
namespace FastHSM.Runtime
{
    /// <summary>
    /// Manages hot reload of definitions.
    /// </summary>
    public class HotReloadManager
    {
        private readonly Dictionary<uint, HsmDefinitionBlob> _loadedBlobs = new();

        public ReloadResult TryReload(
            uint machineId,
            HsmDefinitionBlob newBlob,
            Span<HsmInstance128> instances)
        {
            if (!_loadedBlobs.TryGetValue(machineId, out var oldBlob))
                return ReloadResult.NewMachine;

            // Compare hashes
            bool structureChanged = newBlob.Header.StructureHash != oldBlob.Header.StructureHash;
            bool parameterChanged = newBlob.Header.ParameterHash != oldBlob.Header.ParameterHash;

            if (!structureChanged && !parameterChanged)
                return ReloadResult.NoChange;

            if (structureChanged)
            {
                // Hard reload
                foreach (ref var instance in instances)
                {
                    if (instance.Header.MachineId == machineId)
                    {
                        HardReset(ref instance, newBlob);
                    }
                }

                _loadedBlobs[machineId] = newBlob;
                return ReloadResult.HardReset;
            }
            else
            {
                // Soft reload (parameters only)
                _loadedBlobs[machineId] = newBlob;
                return ReloadResult.SoftReload;
            }
        }

        private unsafe void HardReset(
            ref HsmInstance128 instance,
            HsmDefinitionBlob newBlob)
        {
            // Increment generation (invalidates timers)
            instance.Header.Generation++;

            // Clear runtime state
            instance.Header.Phase = 0;
            instance.Header.MicroStep = 0;
            instance.Header.QueueHead = 0;
            instance.Header.ConsecutiveClamps = 0;

            // Clear active leaves
            for (int i = 0; i < 4; i++)
                instance.ActiveLeafIds[i] = 0xFFFF;

            // Clear timers
            for (int i = 0; i < 4; i++)
                instance.TimerDeadlines[i] = 0;

            // Clear history
            for (int i = 0; i < 8; i++)
                instance.HistorySlots[i] = 0xFFFF;

            // Clear event queue
            instance.QueueTail = 0;
            instance.DeferredTail = 0;
            instance.EventCount = 0;

            // Update machine ID
            instance.Header.MachineId = (uint)newBlob.Header.StructureHash;
        }
    }

    public enum ReloadResult
    {
        NewMachine,
        NoChange,
        SoftReload,
        HardReset,
    }
}
```

### 4.2 Debug Tracing System

```csharp
namespace FastHSM.Runtime
{
    /// <summary>
    /// Binary trace record for zero-allocation logging.
    /// Size: 16 bytes (variable-length internally).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct TraceRecord
    {
        [FieldOffset(0)] public TraceOpCode OpCode;
        [FieldOffset(1)] public byte Reserved;
        [FieldOffset(2)] public ushort EntityId;      // Or chunk-local index
        [FieldOffset(4)] public uint Timestamp;

        // OpCode-specific data (8 bytes)
        [FieldOffset(8)] public ushort StateIndex;
        [FieldOffset(10)] public ushort TransitionIndex;
        [FieldOffset(12)] public ushort EventId;
        [FieldOffset(14)] public ushort GuardResult;
    }

    public enum TraceOpCode : byte
    {
        Transition = 1,
        EventHandled = 2,
        StateEnter = 3,
        StateExit = 4,
        GuardEvaluated = 5,
        ActivityStatus = 6,
        TimerArmed = 7,
        TimerExpired = 8,
    }

    /// <summary>
    /// Per-thread trace buffer (64KB ring).
    /// </summary>
    public unsafe class TraceBuffer
    {
        private const int BufferSize = 64 * 1024;
        private readonly byte* _buffer;
        private int _offset;
        private readonly bool _enabled;

        public TraceBuffer(bool enabled)
        {
            _enabled = enabled;
            if (_enabled)
            {
                _buffer = (byte*)NativeMemory.Alloc(BufferSize);
            }
        }

        public void Write(in TraceRecord record)
        {
            if (!_enabled) return;

            // Write to ring buffer
            int recordSize = sizeof(TraceRecord);
            if (_offset + recordSize > BufferSize)
                _offset = 0; // Wrap

            *(TraceRecord*)(_buffer + _offset) = record;
            _offset += recordSize;
        }

        public ReadOnlySpan<TraceRecord> GetRecords()
        {
            if (!_enabled) return ReadOnlySpan<TraceRecord>.Empty;

            int count = _offset / sizeof(TraceRecord);
            return new ReadOnlySpan<TraceRecord>(_buffer, count);
        }

        ~TraceBuffer()
        {
            if (_enabled && _buffer != null)
                NativeMemory.Free(_buffer);
        }
    }
}
```

### 4.3 Symbolication Tool

```csharp
namespace FastHSM.Tools
{
    /// <summary>
    /// Converts binary trace to human-readable log.
    /// </summary>
    public class TraceSymbolicator
    {
        private readonly HsmDebugMetadata _metadata;

        public TraceSymbolicator(HsmDebugMetadata metadata)
        {
            _metadata = metadata;
        }

        public string Symbolicate(ReadOnlySpan<TraceRecord> records)
        {
            var sb = new StringBuilder();

            foreach (ref readonly var record in records)
            {
                sb.AppendLine(SymbolicateRecord(record));
            }

            return sb.ToString();
        }

        private string SymbolicateRecord(in TraceRecord record)
        {
            string timestamp = $"[{record.Timestamp:D8}]";
            string entity = $"Entity({record.EntityId})";

            return record.OpCode switch
            {
                TraceOpCode.Transition => 
                    $"{timestamp} {entity} Transition: {GetStateName(record.StateIndex)} " +
                    $"-> via Event({GetEventName(record.EventId)})",

                TraceOpCode.StateEnter => 
                    $"{timestamp} {entity} Enter: {GetStateName(record.StateIndex)}",

                TraceOpCode.StateExit => 
                    $"{timestamp} {entity} Exit: {GetStateName(record.StateIndex)}",

                TraceOpCode.GuardEvaluated => 
                    $"{timestamp} {entity} Guard: {GetFunctionName(record.GuardResult)} " +
                    $"=> {(record.GuardResult != 0 ? "TRUE" : "FALSE")}",

                _ => $"{timestamp} {entity} {record.OpCode}"
            };
        }

        private string GetStateName(ushort index)
        {
            return _metadata.StateNames.TryGetValue(index, out var name) 
                ? name 
                : $"State_{index}";
        }

        private string GetEventName(ushort id)
        {
            return _metadata.EventNames.TryGetValue(id, out var name) 
                ? name 
                : $"Event_{id}";
        }

        private string GetFunctionName(ushort id)
        {
            return _metadata.FunctionNames.TryGetValue(id, out var name) 
                ? name 
                : $"Func_{id}";
        }
    }
}
```

### 4.4 Bootstrapper & Registry

```csharp
namespace FastHSM.Runtime
{
    /// <summary>
    /// Global registry of loaded machines.
    /// </summary>
    public static class HsmBootstrapper
    {
        private static readonly Dictionary<ushort, RegistryEntry> s_registry = new();

        public struct RegistryEntry
        {
            public HsmDefinitionBlob Blob;
            public HsmDispatchTable DispatchTable;
        }

        /// <summary>
        /// Register a machine definition with function bindings.
        /// </summary>
        public static unsafe void Register(
            ushort definitionId,
            HsmDefinitionBlob blob,
            delegate* unmanaged[Cdecl]<void*, void*, void*, bool>[] guards,
            delegate* unmanaged[Cdecl]<void*, void*, void*, void>[] actions)
        {
            // Allocate unmanaged dispatch table
            var guardTable = (delegate* unmanaged[Cdecl]<void*, void*, void*, bool>*)
                NativeMemory.Alloc((nuint)(guards.Length * sizeof(nint)));
            var actionTable = (delegate* unmanaged[Cdecl]<void*, void*, void*, void>*)
                NativeMemory.Alloc((nuint)(actions.Length * sizeof(nint)));

            for (int i = 0; i < guards.Length; i++)
                guardTable[i] = guards[i];

            for (int i = 0; i < actions.Length; i++)
                actionTable[i] = actions[i];

            var dispatchTable = new HsmDispatchTable
            {
                Guards = guardTable,
                Actions = actionTable,
            };

            // Validate linker table completeness
            ValidateBindings(blob, guards.Length, actions.Length);

            s_registry[definitionId] = new RegistryEntry
            {
                Blob = blob,
                DispatchTable = dispatchTable,
            };

            // Set global dispatch table (for kernel)
            HsmKernel.s_dispatchTable = dispatchTable;
        }

        /// <summary>
        /// Cleanup on shutdown.
        /// </summary>
        public static unsafe void Shutdown()
        {
            foreach (var entry in s_registry.Values)
            {
                NativeMemory.Free(entry.DispatchTable.Guards);
                NativeMemory.Free(entry.DispatchTable.Actions);
            }
            s_registry.Clear();
        }

        private static void ValidateBindings(
            HsmDefinitionBlob blob,
            int guardCount,
            int actionCount)
        {
            // Check that all function IDs in LinkerTable have bindings
            foreach (ref readonly var entry in blob.LinkerTable)
            {
                // (Validation logic - ensure no nulls)
            }
        }
    }
}
```

---

## 5. Implementation Roadmap

### Phase 1: Data Layer (Week 1)
- [ ] Define all core structs (StateDef, TransitionDef, etc.)
- [ ] Implement HsmDefinitionBlob with span accessors
- [ ] Implement HsmInstance64/128/256 with proper alignment
- [ ] Write unit tests for struct sizes and layouts

### Phase 2: Compiler (Weeks 2-3)
- [ ] Implement JSON parser → BuilderGraph
- [ ] Implement Normalizer (stable IDs, depths)
- [ ] Implement Validator (depth, budgets, slot conflicts)
- [ ] Implement Flattener (graph → flat arrays)
- [ ] Implement LinkerTableBuilder
- [ ] Implement HashComputer
- [ ] Implement BlobEmitter
- [ ] Write integration tests with sample machines

### Phase 3: Kernel (Weeks 4-5)
- [ ] Implement UpdateBatch entry point
- [ ] Implement Phase 1 (timers)
- [ ] Implement Phase 2 (RTC loop, transition resolution)
- [ ] Implement LCA computation
- [ ] Implement ExecuteTransition (exit/entry paths)
- [ ] Implement Phase 3 (updates/activities)
- [ ] Implement event queue operations
- [ ] Write comprehensive kernel tests

### Phase 4: Tooling (Week 6)
- [ ] Implement HotReloadManager
- [ ] Implement TraceBuffer and TraceRecord
- [ ] Implement TraceSymbolicator
- [ ] Implement HsmBootstrapper
- [ ] Write example user actions
- [ ] Write golden-run tests

### Phase 5: Polish (Week 7)
- [ ] Performance profiling
- [ ] Memory optimization
- [ ] Documentation
- [ ] Example machines

---

## 6. Architect Review & Approved Directives

**Review Date:** 2026-01-11  
**Status:** ✅ APPROVED with specific directives

### 6.1 Critical Issues Identified and Resolved

#### Issue 1: Tier 1 Event Queue Fragmentation
**Problem:** Separate physical queues (3 priority classes) cannot fit in 32 bytes when a single event is 24 bytes (32 / 3 = 10 bytes per queue → insufficient).

**Resolution:** Tier-specific queue strategies:
- **Tier 1 (64B):** Single shared FIFO queue. Priority events can evict oldest normal events.
- **Tier 2/3 (128B/256B):** Hybrid strategy with reserved interrupt slot (24B) + shared ring buffer.

**Implementation:** See updated `HsmInstance64`, `HsmInstance128`, `HsmInstance256` structs above.

#### Issue 2: History Slot Stability on Hot Reload
**Problem:** If history slots are assigned by name or declaration order, adding a new state alphabetically before existing states will shift slot indices, causing hot reload to read wrong/garbage history data.

**Resolution:** History slots MUST be assigned in **StableID sort order** (GUID-based), never by name or declaration order.

**Implementation:** See `AssignHistorySlots()` method in Section 2.6 (Flattener).

### 6.2 Approved Implementation Directives

#### Directive 1: Thin Shim Pattern (Q9)
**Requirement:** Use void* core with generic inlined wrapper to prevent I-cache bloat.

**Pattern:**
```csharp
// 1. Non-generic core (compiled once)
private static void UpdateSingleCore(
    HsmDefinitionBlob definition,
    void* instancePtr,
    void* contextPtr, // void*, no generics
    ...)
{
    // Heavy logic here
}

// 2. Generic shim (inlined, zero overhead)
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void UpdateBatch<TContext>(...)
    where TContext : unmanaged
{
    fixed (TContext* ctx = &context)
    {
        UpdateSingleCore(definition, inst, ctx, ...);
    }
}
```

**Critical:** The `AggressiveInlining` attribute is MANDATORY.

#### Directive 2: ID-Only Event Validation (Q4 Related)
**Requirement:** Compiler must validate that events with payload > 16 bytes are marked `IsIndirect` and warn if they're also deferrable.

**Implementation:** See `ValidateIndirectEvents()` in Section 2.5 (Validator).

**Reasoning:** Large payloads must live in blackboard (persistent), not command buffers (ephemeral). Deferring an ID-only event pointing to ephemeral memory causes dangling references.

#### Directive 3: RNG Access Tracking (Q4)
**Requirement:** Guards marked `[HsmGuard(UsesRNG=true)]` must have debug-only access count tracking for replay validation.

**Implementation:** 
- Debug builds increment `debugAccessCount` on each `HsmRng.NextFloat()` call
- Replay validator compares access counts per frame to detect determinism drift
- See updated `HsmRng` in Section 1.6

### 6.3 Approved Decisions Summary

| Question | Decision | Rationale |
|----------|----------|-----------|
| Q1: Event Queue | **Hybrid (Tier-specific)** | Tier 1 math impossible with separate queues |
| Q2: Command Pages | **4KB Fixed** | Simple, standard, optimize later if needed |
| Q3: History Slots | **Compiler Pool + Stable Sort** | Performance + hot reload stability |
| Q4: RNG in Guards | **Allow with Declaration** | Pragmatic; tracking prevents issues |
| Q5: Sync Transitions | **Restricted (Reset to Initial)** | Start simple; 90% case coverage |
| Q6: Transition Cost | **Structural Only** | Only metric compiler can prove |
| Q7: Global Transitions | **Separate Table** | O(G) scan faster than filtering main table |
| Q8: Trace Filtering | **All Modes** | 3 boolean checks negligible vs value |
| Q9: Action Signature | **Void* Core + Wrappers** | Zero I-cache bloat |
| Q10: Local Storage | **Scratch Registers** | Simple; 16B overhead acceptable |

### 6.4 Implementation Status

**Approved to Proceed:**
- ✅ Phase 1 (Data Layer) - Use modified queue layouts
- ✅ Phase 2 (Compiler) - Implement stable slot sorting + indirect validation
- ✅ Phase 3 (Kernel) - Use thin shim pattern with AggressiveInlining
- ✅ Phase 4 (Tooling) - Implement RNG tracking in debug builds

**No Blockers Remaining**

---

---

## 7. Implementation Readiness

**Document Status:** ✅ APPROVED by Architect - Ready for Implementation  
**Review Date:** 2026-01-11  
**Approval Status:** All questions resolved, all directives incorporated

### Critical Path Items
- ✅ Event queue layout (Tier-specific hybrid strategy)
- ✅ History slot stability (StableID-based sorting)
- ✅ Kernel dispatch pattern (Thin shim with AggressiveInlining)
- ✅ Indirect event validation (Compiler checks)
- ✅ RNG tracking (Debug-only access counts)

### Ready to Implement
**Phase 1 (Data Layer):** Start immediately with updated struct layouts  
**Phase 2 (Compiler):** Implement stable sorting and validation rules  
**Phase 3 (Kernel):** Use thin shim pattern as specified  
**Phase 4 (Tooling):** Implement RNG tracking in debug builds

**Next Step:** Begin Phase 1 implementation (Week 1)
