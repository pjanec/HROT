# BATCH-03 Instructions -- Debug Interfaces, TestData, CapturingDebugSession, Foundation Stubs

**Target tasks:** TASK-TH-008, TASK-TH-009, plus prerequisite type stubs needed by TH-003.
**Design documents:** `.dev/blueprints-1/TASK-DETAIL.md`, `.dev/blueprints-1/Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md`, `.dev/blueprints-1/Blueprint_Subsystem_Test_Harness_Detailed_Design.md`
**Workspace root:** `d:\Work\IOS-IG-SimHost-FDP-2`
**Solution:** `IOS-IG-SimHost.sln`

---

## Context

BATCH-02 is complete and committed (commit 8ba13749).
Tests: 57 passing, 1 skipped (TierUpgrade -- pending BATCH-04).

Files created in BATCH-02:
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Mocks/MockSimulationView.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Mocks/MockEntityCommandBuffer.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Mocks/MockTestTypes.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Mocks/MockSimulationViewContractTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Mocks/MockEntityCommandBufferContractTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Mocks/MockContractTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Builders/BlueprintAssetBuilder.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Builders/BlueprintAssetBuilderTests.cs`

Existing placeholder stubs (just empty namespace declarations):
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintRegistry.cs` -- placeholder only
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintDefinition.cs` -- placeholder only
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintTickSystem.cs` -- placeholder only
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintMaintenanceSystem.cs` -- placeholder only
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard1024.cs` -- placeholder only
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard4096.cs` -- placeholder only
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard16384.cs` -- placeholder only
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintBlackboardPartitions.cs` -- placeholder only
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintBlackboardHeader.cs` -- check existing
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintSlotEntry.cs` -- check existing
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/Attributes/BlueprintRegistrarAttribute.cs` -- check existing

Already defined types:
- `NodeStatus` enum -- in `Hrot.Blueprints.Core.Assets.GraphTypes` (values: `Success, Failure, Running`)
- `BlueprintAsset`, `BlueprintDispatchKind`, `Graph`, `GraphKind`, all node types -- in `Hrot.Blueprints.Core.Assets`
- `BlueprintJsonServices` -- in `Hrot.Blueprints.Core`
- `MockSimulationView`, `MockEntityCommandBuffer`, `MockTestTypes` -- in `Hrot.Blueprints.Tests.Mocks`
- `BlueprintAssetBuilder`, `GraphBuilder`, `SyntheticGuidHelper` -- in `Hrot.Blueprints.Tests.Builders`
- `[ComponentId(252)]` on TestComponent, `[ComponentId(253)]` on LargeTestStruct, `[ComponentId(254)]` on AnotherTestComponent -- reserved for tests
- `FdpEventBus` / `EntityRepository` / `ISimulationView` / `IEntityCommandBuffer` -- in `Fdp.Core`
- `IEcsModuleSystem` / `IProfiledSystem` -- check if they exist in `Fdp.Core`

---

## Prerequisite: Read existing files before modifying

Before replacing any placeholder, **read the file** to understand its content.

---

## Deliverable 1: Debug Protocol Types (needed by TH-008)

### 1a. Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Debug/IBlueprintProbeSink.cs`

Namespace: `Hrot.Blueprints.Core.Debug`
Referenced from: `Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md` §2.4

```csharp
using Fdp.Core;

namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// Thin probe sink that generated Blueprint code calls into via DebugProbe.
/// </summary>
public interface IBlueprintProbeSink
{
    void OnNodeEnter(Entity self, string nodeId);
    void OnPinValueChanged<T>(Entity self, string pinId, T value);
}
```

### 1b. Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Debug/DebugProbe.cs`

Namespace: `Hrot.Blueprints.Core.Debug`

```csharp
using Fdp.Core;

namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// Static dispatcher that generated Blueprint code calls.
/// Wire DebugProbe.Sink to a CapturingDebugSession in tests.
/// In production (no session), Sink defaults to NullProbeSink which is a no-op.
/// </summary>
public static class DebugProbe
{
    public static IBlueprintProbeSink Sink { get; set; } = NullProbeSink.Instance;

    public static void NodeEnter(Entity self, string nodeId)
        => Sink.OnNodeEnter(self, nodeId);

    public static void PinValueChanged<T>(Entity self, string pinId, T value)
        => Sink.OnPinValueChanged(self, pinId, value);
}

/// <summary>No-op sink used when no debug session is attached.</summary>
public sealed class NullProbeSink : IBlueprintProbeSink
{
    public static NullProbeSink Instance { get; } = new NullProbeSink();
    private NullProbeSink() { }
    public void OnNodeEnter(Entity self, string nodeId) { }
    public void OnPinValueChanged<T>(Entity self, string pinId, T value) { }
}
```

### 1c. Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Debug/IBlueprintDebugSession.cs`

Namespace: `Hrot.Blueprints.Core.Debug`
Referenced from: `Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md` §2.1

Implement a MINIMAL version suitable for the CapturingDebugSession test stub. The full interface from the debug DD is complex; only include what CapturingDebugSession needs:

```csharp
using Fdp.Core;

namespace Hrot.Blueprints.Core.Debug;

// Minimal record types for debug session events.
public sealed record BreakpointHit(Entity Self, string NodeId);
public sealed record NodeExecuted(Entity Self, string NodeId, float Time);
public sealed record PinValueChanged(Entity Self, string PinId, object? Value);

/// <summary>
/// Minimal debug session interface for test use (Slice 1).
/// Full surface in Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md.
/// </summary>
public interface IBlueprintDebugSession : IBlueprintProbeSink
{
    // Breakpoint management
    void SetBreakpoint(string nodeId);
    void ClearBreakpoint(string nodeId);
    bool IsAnyBreakpointActive { get; }

    // Watch management
    bool IsAnyWatchActive { get; }

    // Pause control stubs
    void Continue();
    void StepOver();
    void StepInto();
    void StepOut();

    // Events
    event Action<BreakpointHit>? OnBreakpointHit;
    event Action<NodeExecuted>? OnNodeExecuted;
    event Action<PinValueChanged>? OnPinValueChanged;
}
```

---

## Deliverable 2: Foundation Stubs (needed by TH-003 in BATCH-04)

These replace the placeholder-only files. Implement minimal working stubs that compile and allow BlueprintTestFixture to be constructed without throwing.

### 2a. `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlackboardTier.cs` (NEW file)

```csharp
namespace Fdp.Toolkit.Blueprints;

/// <summary>Blackboard memory tier selection for Blueprint state storage.</summary>
public enum BlackboardTier
{
    B1024  = 0,   // up to 928 bytes of state
    B4096  = 1,   // up to 3936 bytes of state
    B16384 = 2,   // up to 16368 bytes of state
}
```

### 2b. `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintLatentCursor.cs` (NEW file)

```csharp
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// 16-byte cursor tracking the current latent execution point in a Blueprint graph.
/// Stored inline inside the entity's blackboard slot.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct BlueprintLatentCursor
{
    public Guid GraphId;
    // Reserved bytes for frame counter / sub-state are part of the 16-byte budget.
}
```

### 2c. `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintCompileException.cs` (NEW file)

```csharp
namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Thrown by BlueprintTestFixture.CompileAndLoad when the compiler emits errors.
/// </summary>
public sealed class BlueprintCompileException : Exception
{
    public string Diagnostics { get; }

    public BlueprintCompileException(string message, string diagnostics)
        : base(message)
    {
        Diagnostics = diagnostics;
    }
}
```

### 2d. `FDP/Toolkits/Fdp.Toolkits/Blueprints/CompilerMode.cs` (NEW file)

```csharp
namespace Fdp.Toolkit.Blueprints;

/// <summary>Compilation mode passed to CompileAndLoad.</summary>
public enum CompilerMode
{
    Release = 0,
    Debug   = 1,
    Trace   = 2,
}
```

### 2e. Replace `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintDefinition.cs` with minimal stub

```csharp
namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Runtime definition produced from a compiled BlueprintAsset.
/// Full implementation in Phase 2 (TASK-RT-002).
/// </summary>
public sealed class BlueprintDefinition
{
    public Guid AssetId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int StateSize { get; init; }

    /// <summary>Named state fields used by BlueprintStateView.GetField.</summary>
    public IReadOnlyDictionary<string, int> StateFields { get; init; }
        = new Dictionary<string, int>();

    /// <summary>Initializes the entity's blackboard slot to its default state.</summary>
    public unsafe void InitDefault(byte* slotPtr, int slotSize) { }
}
```

### 2f. Replace `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintRegistry.cs` with minimal working class

The full implementation is TASK-RT-001 (Phase 2). Provide enough for the fixture constructor to work:

```csharp
using System.Collections.Concurrent;

namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Registry of all compiled Blueprint definitions.
/// Minimal slice for Phase 1 test harness; full implementation in TASK-RT-001.
/// </summary>
public sealed class BlueprintRegistry
{
    private volatile Snapshot _current = new Snapshot();

    public event Action? OnRegistryChanged;

    public BlueprintRegistryStaging BeginStaging() => new BlueprintRegistryStaging();

    public void CommitStaging(BlueprintRegistryStaging staging)
    {
        var next = new Snapshot(staging);
        Interlocked.Exchange(ref _current, next);
        OnRegistryChanged?.Invoke();
    }

    public bool TryGetById(Guid id, out BlueprintDefinition? def)
        => _current.ById.TryGetValue(id, out def);

    public bool TryGetByName(string name, out BlueprintDefinition? def)
        => _current.ByName.TryGetValue(name, out def);

    public IReadOnlyCollection<BlueprintDefinition> GetAll()
        => _current.ById.Values;

    public void RegisterWorldSingleton(Guid blueprintId, BlackboardTier tier)
    {
        // Validated by CommitStaging in full impl; stub is permissive.
    }

    public bool TryGetWorldSingleton(Guid blueprintId, out BlackboardTier tier)
    {
        tier = BlackboardTier.B1024;
        return false;
    }

    public IReadOnlyList<(Guid, BlackboardTier)> GetAllWorldSingletons()
        => Array.Empty<(Guid, BlackboardTier)>();

    private sealed class Snapshot
    {
        public readonly Dictionary<Guid, BlueprintDefinition> ById = new();
        public readonly Dictionary<string, BlueprintDefinition> ByName = new();

        public Snapshot() { }

        public Snapshot(BlueprintRegistryStaging staging)
        {
            foreach (var def in staging.Definitions)
            {
                ById[def.AssetId] = def;
                ByName[def.Name] = def;
            }
        }
    }
}

/// <summary>Staging area for atomic registry updates.</summary>
public sealed class BlueprintRegistryStaging
{
    internal readonly List<BlueprintDefinition> Definitions = new();

    public void Add(BlueprintDefinition def)
    {
        if (Definitions.Any(d => d.AssetId == def.AssetId))
            throw new InvalidOperationException(
                $"Duplicate BlueprintId {def.AssetId} ('{def.Name}')");
        Definitions.Add(def);
    }
}
```

### 2g. Replace `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintTickSystem.cs` with minimal stub

```csharp
using Fdp.Core;

namespace Fdp.Toolkit.Blueprints.Systems;

/// <summary>
/// Ticks all active Blueprint instances each frame.
/// Minimal stub for Phase 1 test harness; full implementation in TASK-RT-005.
/// </summary>
public sealed class BlueprintTickSystem
{
    private readonly BlueprintRegistry _registry;

    public BlueprintTickSystem(BlueprintRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Execute Blueprint tick for all active instances. Stub is no-op.</summary>
    public void Execute(ISimulationView view) { }
}
```

### 2h. Replace `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintMaintenanceSystem.cs` with minimal stub

```csharp
using Fdp.Core;

namespace Fdp.Toolkit.Blueprints.Systems;

/// <summary>
/// Handles Blueprint lifecycle transitions (attach, detach, tier upgrades).
/// Minimal stub for Phase 1 test harness; full implementation in TASK-RT-006.
/// </summary>
public sealed class BlueprintMaintenanceSystem
{
    /// <summary>Execute maintenance pass. Stub is no-op.</summary>
    public void Execute(ISimulationView view) { }
}
```

### 2i. Replace `FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard1024.cs`

Component IDs reserved in the range 205-207 for Blueprint blackboard components.
Read `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` to verify 205-207 are not taken.

```csharp
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Blueprints.Components;

/// <summary>
/// Small blackboard slot -- up to 928 bytes of Blueprint state plus a 96-byte header.
/// Component ID 205 reserved for Blueprint subsystem.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 1024)]
[ComponentId(205)]
public unsafe struct BlueprintBlackboard1024
{
    public fixed byte Data[1024];
}
```

### 2j. Replace `FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard4096.cs`

```csharp
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Blueprints.Components;

/// <summary>
/// Medium blackboard slot -- up to 3936 bytes of Blueprint state plus a 160-byte header.
/// Component ID 206 reserved for Blueprint subsystem.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 4096)]
[ComponentId(206)]
public unsafe struct BlueprintBlackboard4096
{
    public fixed byte Data[4096];
}
```

### 2k. Replace `FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard16384.cs`

```csharp
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Blueprints.Components;

/// <summary>
/// Large blackboard slot -- up to 16368 bytes of Blueprint state plus a 16-byte header.
/// Component ID 207 reserved for Blueprint subsystem.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 16384)]
[ComponentId(207)]
public unsafe struct BlueprintBlackboard16384
{
    public fixed byte Data[16384];
}
```

### 2l. Replace `FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintBlackboardHeader.cs` if placeholder

Check existing content. If it's a placeholder, replace with:

```csharp
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Blueprints.Partitioning;

/// <summary>
/// Header written at offset 0 of every blackboard slot.
/// Magic bytes identify the slot as initialized.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct BlueprintBlackboardHeader
{
    public const uint MagicValue = 0xBP_1234U; // 0x42503132 -- 'BP12' in ASCII
    public uint Magic;           // 0 = uninitialized, MagicValue = initialized
    public int SlotCount;        // how many blueprint slots are active
    public fixed byte Reserved[8];
}
```

Wait -- `0xBP_1234U` is not valid hex. Use `0x42503132U` instead (ASCII for "BP12").

### 2m. Replace `FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintBlackboardPartitions.cs` if placeholder

```csharp
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Components;

namespace Fdp.Toolkit.Blueprints.Partitioning;

/// <summary>
/// Manages per-entity Blueprint slot allocation within the blackboard components.
/// Minimal stub for Phase 1; full implementation in TASK-RT-004.
/// </summary>
public static class BlueprintBlackboardPartitions
{
    /// <summary>
    /// Attempts to attach a Blueprint definition to an entity's blackboard slot.
    /// Stub always returns false (no slots allocated yet until Phase 2).
    /// </summary>
    public static bool TryAttach(
        EntityRepository repo,
        Entity entity,
        BlueprintDefinition def,
        BlackboardTier tier,
        out int slotIndex)
    {
        slotIndex = -1;
        return false;
    }

    /// <summary>
    /// Attempts to find the slot for a given blueprint on an entity.
    /// Stub always returns false.
    /// </summary>
    public static bool TryGetSlotOffset(
        EntityRepository repo,
        Entity entity,
        Guid blueprintId,
        out BlackboardTier tier,
        out int slotIndex,
        out int payloadOffset)
    {
        tier = BlackboardTier.B1024;
        slotIndex = -1;
        payloadOffset = -1;
        return false;
    }
}
```

### 2n. Check `FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintSlotEntry.cs`

If placeholder, replace with:

```csharp
namespace Fdp.Toolkit.Blueprints.Partitioning;

/// <summary>Identifies one Blueprint slot within an entity's blackboard.</summary>
public readonly struct BlueprintSlotEntry
{
    public Guid BlueprintId { get; init; }
    public int SlotIndex { get; init; }
    public int PayloadOffset { get; init; }
    public BlackboardTier Tier { get; init; }
}
```

### 2o. Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/BlueprintCompiler.cs` (NEW file)

```csharp
using Hrot.Blueprints.Core.Assets;
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Core;

/// <summary>
/// Compiles BlueprintAsset objects to C# source code.
/// Minimal stub for Phase 1; full implementation in Phase 3 (Compiler DD).
/// </summary>
public sealed class BlueprintCompiler
{
    /// <summary>
    /// Compile a single asset to C# source.
    /// Stub throws NotImplementedException -- full compiler is Phase 3.
    /// </summary>
    public string Compile(BlueprintAsset asset, CompilerMode mode)
        => throw new NotImplementedException(
            "BlueprintCompiler is not yet implemented (Phase 3). " +
            "Do not call CompileAndLoad in Phase 1 tests.");
}
```

### 2p. Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/InMemoryRoslynCompiler.cs` (NEW file)

```csharp
using System.Reflection;
using System.Runtime.Loader;

namespace Hrot.Blueprints.Core;

/// <summary>
/// Compiles C# source strings in memory using Roslyn and loads the result
/// into a collectible AssemblyLoadContext.
/// Stub for Phase 1; full implementation in Phase 3 (Compiler DD).
/// </summary>
public sealed class InMemoryRoslynCompiler
{
    /// <summary>
    /// Compile source code and load into a new collectible ALC.
    /// Stub throws NotImplementedException -- full implementation is Phase 3.
    /// </summary>
    public Assembly CompileAndLoad(string sourceCode, AssemblyLoadContext alc)
        => throw new NotImplementedException(
            "InMemoryRoslynCompiler is not yet implemented (Phase 3).");
}
```

---

## Deliverable 3: TASK-TH-008 -- CapturingDebugSession

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CapturingDebugSession.cs`.

Namespace: `Hrot.Blueprints.Tests.Debug`
Implements: `IBlueprintProbeSink`, `IBlueprintDebugSession`
Reference: TASK-DETAIL.md §TASK-TH-008, Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md §10.3

```csharp
using Fdp.Core;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Test-use implementation of IBlueprintDebugSession that captures all
/// probe events into inspectable lists. Wire to DebugProbe.Sink in tests.
/// </summary>
public sealed class CapturingDebugSession : IBlueprintProbeSink, IBlueprintDebugSession
{
    // ---- Captured data ----
    private readonly List<NodeEnterRecord> _nodeEntries = new();
    private readonly List<PinValueRecord> _pinValues = new();
    private readonly HashSet<string> _breakpoints = new();

    // ---- IBlueprintProbeSink ----
    public void OnNodeEnter(Entity self, string nodeId)
    {
        _nodeEntries.Add(new NodeEnterRecord(self, nodeId, 0f));
        if (_breakpoints.Contains(nodeId))
            OnBreakpointHit?.Invoke(new BreakpointHit(self, nodeId));
    }

    public void OnPinValueChanged<T>(Entity self, string pinId, T value)
        => _pinValues.Add(new PinValueRecord(self, pinId, value));

    // ---- IBlueprintDebugSession ----
    public void SetBreakpoint(string nodeId) => _breakpoints.Add(nodeId);
    public void ClearBreakpoint(string nodeId) => _breakpoints.Remove(nodeId);
    public bool IsAnyBreakpointActive => _breakpoints.Count > 0;
    public bool IsAnyWatchActive => false;

    public void Continue() { }
    public void StepOver() { }
    public void StepInto() { }
    public void StepOut() { }

    public event Action<BreakpointHit>? OnBreakpointHit;
    public event Action<NodeExecuted>? OnNodeExecuted;
    public event Action<PinValueChanged>? OnPinValueChanged;

    // ---- Inspection helpers ----
    public IReadOnlyList<NodeEnterRecord> NodeEntries => _nodeEntries;
    public IReadOnlyList<PinValueRecord> PinValues => _pinValues;

    public bool Hit(string nodeId)
        => _nodeEntries.Any(r => r.NodeId == nodeId);

    public int HitCount(string nodeId)
        => _nodeEntries.Count(r => r.NodeId == nodeId);

    public IReadOnlyList<NodeEnterRecord> HitsFor(Entity self)
        => _nodeEntries.Where(r => r.Self == self).ToList();

    public void Clear()
    {
        _nodeEntries.Clear();
        _pinValues.Clear();
    }
}

// ---- Record types ----
public sealed record NodeEnterRecord(Entity Self, string NodeId, float Time);
public sealed record PinValueRecord(Entity Self, string PinId, object? Value);
```

### CapturingDebugSession Contract Tests

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CapturingDebugSessionTests.cs`:

- SC1: CapturingDebugSession compiles implementing both IBlueprintProbeSink and IBlueprintDebugSession.
- SC2: `DebugProbe.Sink = session; DebugProbe.NodeEnter(entity, "n-001")`. Assert `session.Hit("n-001") == true`, `session.HitCount("n-001") == 1`.
- SC3: Set breakpoint "n-002", call `DebugProbe.NodeEnter(entity, "n-002")`. Assert `OnBreakpointHit` fired once.
- SC4: `session.IsAnyBreakpointActive == true` after SetBreakpoint; false after ClearBreakpoint.
- SC5: Multiple `DebugProbe.PinValueChanged` calls accumulate in `session.PinValues`.

Note: Use a separate `EntityRepository` for each test. Do NOT restore `DebugProbe.Sink` after each test -- use try/finally in tests or a simple fixture cleanup to set `DebugProbe.Sink = NullProbeSink.Instance` at the end.

Also, SC6 from TH-008 (`fixture.DebugSession != null`) is deferred to BATCH-04 (requires BlueprintTestFixture).

Two usage-pattern tests (Debug_TraceMode_RecordsAllNodeEntries, Debug_Breakpoint_FiresWhenNodeEntered) are marked `[Fact(Skip = "Requires Phase 3 compiler")]`.

---

## Deliverable 4: TASK-TH-009 -- TestData Infrastructure

### 4a. Create TestAssets directory structure

Create these 9 valid .bp.json files in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/`:

Each file must parse without error via `BlueprintJsonServices.Deserialize<BlueprintAsset>()`.

The schema is from `Hrot.Blueprints.Core.Assets`. Required fields: `name` (string), `dispatch` (string: "Library" | "AiPrimitive" | "Instance"), `assetId` (Guid string), `schemaVersion` (string), `subsystemType` (string). Optional lists default to empty.

**`LibraryMath.bp.json`** -- `Dispatch: "Library"`, 2 graphs with FunctionCallNode:
```json
{
  "name": "LibraryMath",
  "dispatch": "Library",
  "assetId": "00000001-0000-0000-0000-000000000001",
  "schemaVersion": "1.0",
  "subsystemType": "Hrot.Blueprints",
  "graphs": [
    {
      "id": "10000001-0000-0000-0000-000000000001",
      "name": "Add",
      "kind": "Function",
      "nodes": [
        { "$type": "FunctionCall", "id": "20000001-0000-0000-0000-000000000001", "functionName": "Math.Add", "pins": [], "metadata": {} }
      ],
      "links": [],
      "metadata": {}
    }
  ],
  "variables": [], "parameters": [], "customEvents": [], "eventDispatchers": [],
  "callablePeers": [], "workingState": [], "metadata": {}
}
```

**`InstanceCounter.bp.json`** -- `Dispatch: "Instance"`, variable `Count` of type `int`:
```json
{
  "name": "InstanceCounter",
  "dispatch": "Instance",
  "assetId": "00000002-0000-0000-0000-000000000001",
  "schemaVersion": "1.0",
  "subsystemType": "Hrot.Blueprints",
  "variables": [{ "name": "Count", "typeRef": { "clrTypeName": "System.Int32" }, "initialValue": "0" }],
  "graphs": [], "parameters": [], "customEvents": [], "eventDispatchers": [],
  "callablePeers": [], "workingState": [], "metadata": {}
}
```

**`InstanceCounterV1ModifiedBody.bp.json`** -- same assetId as InstanceCounter but different graph body (for hot-reload tests):
```json
{
  "name": "InstanceCounterV1ModifiedBody",
  "dispatch": "Instance",
  "assetId": "00000002-0000-0000-0000-000000000001",
  "schemaVersion": "1.0",
  "subsystemType": "Hrot.Blueprints",
  "variables": [{ "name": "Count", "typeRef": { "clrTypeName": "System.Int32" }, "initialValue": "0" }],
  "graphs": [
    {
      "id": "10000002-0000-0000-0000-000000000001",
      "name": "Tick",
      "kind": "Function",
      "nodes": [
        { "$type": "SetVariable", "id": "20000002-0000-0000-0000-000000000001", "variableName": "Count", "valueExpression": "Count + 1", "pins": [], "metadata": {} }
      ],
      "links": [],
      "metadata": {}
    }
  ],
  "parameters": [], "customEvents": [], "eventDispatchers": [],
  "callablePeers": [], "workingState": [], "metadata": {}
}
```

**`InstanceCounterV2WithBonus.bp.json`** -- same assetId, adds Bonus variable:
```json
{
  "name": "InstanceCounterV2WithBonus",
  "dispatch": "Instance",
  "assetId": "00000002-0000-0000-0000-000000000001",
  "schemaVersion": "1.0",
  "subsystemType": "Hrot.Blueprints",
  "variables": [
    { "name": "Count", "typeRef": { "clrTypeName": "System.Int32" }, "initialValue": "0" },
    { "name": "Bonus", "typeRef": { "clrTypeName": "System.Int32" }, "initialValue": "10" }
  ],
  "graphs": [], "parameters": [], "customEvents": [], "eventDispatchers": [],
  "callablePeers": [], "workingState": [], "metadata": {}
}
```

**`HealthRegen.bp.json`** -- `Dispatch: "Instance"`, variables `CurrentHealth` and `MaxHealth`:
```json
{
  "name": "HealthRegen",
  "dispatch": "Instance",
  "assetId": "00000003-0000-0000-0000-000000000001",
  "schemaVersion": "1.0",
  "subsystemType": "Hrot.Blueprints",
  "variables": [
    { "name": "CurrentHealth", "typeRef": { "clrTypeName": "System.Single" }, "initialValue": "100" },
    { "name": "MaxHealth", "typeRef": { "clrTypeName": "System.Single" }, "initialValue": "100" }
  ],
  "graphs": [], "parameters": [], "customEvents": [], "eventDispatchers": [],
  "callablePeers": [], "workingState": [], "metadata": {}
}
```

**`HasVisibleTarget.bp.json`** -- `Dispatch: "AiPrimitive"`, intent `Condition`, hosting `BTreeCondition`:
```json
{
  "name": "HasVisibleTarget",
  "dispatch": "AiPrimitive",
  "assetId": "00000004-0000-0000-0000-000000000001",
  "schemaVersion": "1.0",
  "subsystemType": "Hrot.Blueprints",
  "primitive": { "intent": "Condition", "hostings": ["BTreeCondition"] },
  "graphs": [
    {
      "id": "10000004-0000-0000-0000-000000000001",
      "name": "Main",
      "kind": "Function",
      "nodes": [
        { "$type": "EventEntry", "id": "20000004-0000-0000-0000-000000000001", "pins": [], "metadata": {} },
        { "$type": "Return", "id": "20000004-0000-0000-0000-000000000002", "status": "Success", "pins": [], "metadata": {} }
      ],
      "links": [
        {
          "from": { "nodeId": "20000004-0000-0000-0000-000000000001", "pinId": "exec-out" },
          "to":   { "nodeId": "20000004-0000-0000-0000-000000000002", "pinId": "exec-in" }
        }
      ],
      "metadata": {}
    }
  ],
  "variables": [], "parameters": [], "customEvents": [], "eventDispatchers": [],
  "callablePeers": [], "workingState": [], "metadata": {}
}
```

**`MoveToAndFire.bp.json`** -- `Dispatch: "AiPrimitive"`, `Intent: "Action"`, `Hosting: "BTreeAction"`, must contain ChannelCommandNode and WaitForChannelNode:
```json
{
  "name": "MoveToAndFire",
  "dispatch": "AiPrimitive",
  "assetId": "00000005-0000-0000-0000-000000000001",
  "schemaVersion": "1.0",
  "subsystemType": "Hrot.Blueprints",
  "primitive": { "intent": "Action", "hostings": ["BTreeAction"] },
  "parameters": [{ "name": "TargetEntity", "typeRef": { "clrTypeName": "System.UInt64" } }],
  "workingState": [{ "name": "Phase", "typeRef": { "clrTypeName": "System.Int32" } }],
  "graphs": [
    {
      "id": "10000005-0000-0000-0000-000000000001",
      "name": "Main",
      "kind": "Function",
      "nodes": [
        { "$type": "EventEntry", "id": "20000005-0000-0000-0000-000000000001", "pins": [], "metadata": {} },
        { "$type": "ChannelCommand", "id": "20000005-0000-0000-0000-000000000002", "channelType": "LocomotionChannel", "actionId": "MoveTo", "pins": [], "metadata": {} },
        { "$type": "WaitForChannel", "id": "20000005-0000-0000-0000-000000000003", "channelType": "LocomotionChannel", "pins": [], "metadata": {} },
        { "$type": "Return", "id": "20000005-0000-0000-0000-000000000004", "status": "Success", "pins": [], "metadata": {} }
      ],
      "links": [
        { "from": { "nodeId": "20000005-0000-0000-0000-000000000001", "pinId": "exec-out" }, "to": { "nodeId": "20000005-0000-0000-0000-000000000002", "pinId": "exec-in" } },
        { "from": { "nodeId": "20000005-0000-0000-0000-000000000002", "pinId": "exec-out" }, "to": { "nodeId": "20000005-0000-0000-0000-000000000003", "pinId": "exec-in" } },
        { "from": { "nodeId": "20000005-0000-0000-0000-000000000003", "pinId": "exec-out" }, "to": { "nodeId": "20000005-0000-0000-0000-000000000004", "pinId": "exec-in" } }
      ],
      "metadata": {}
    }
  ],
  "variables": [], "customEvents": [], "eventDispatchers": [],
  "callablePeers": [], "metadata": {}
}
```

**`DoorActor.bp.json`** -- `Dispatch: "Instance"`, custom event `OnDoorOpen`:
```json
{
  "name": "DoorActor",
  "dispatch": "Instance",
  "assetId": "00000006-0000-0000-0000-000000000001",
  "schemaVersion": "1.0",
  "subsystemType": "Hrot.Blueprints",
  "variables": [{ "name": "IsOpen", "typeRef": { "clrTypeName": "System.Boolean" }, "initialValue": "false" }],
  "customEvents": [{ "name": "OnDoorOpen", "parameters": [] }],
  "graphs": [], "parameters": [], "eventDispatchers": [],
  "callablePeers": [], "workingState": [], "metadata": {}
}
```

**`DoorSensor.bp.json`** -- `Dispatch: "Instance"`, callable peer to DoorActor:
```json
{
  "name": "DoorSensor",
  "dispatch": "Instance",
  "assetId": "00000007-0000-0000-0000-000000000001",
  "schemaVersion": "1.0",
  "subsystemType": "Hrot.Blueprints",
  "callablePeers": [{ "assetId": "00000006-0000-0000-0000-000000000001" }],
  "graphs": [], "variables": [], "parameters": [], "customEvents": [], "eventDispatchers": [],
  "workingState": [], "metadata": {}
}
```

### 4b. Create `TestAssets/Invalid/` directory with 4 semantically-invalid files

**`ConditionWithRunning.bp.json`** -- AiPrimitive Condition that returns Running (semantically wrong):
```json
{
  "name": "ConditionWithRunning",
  "dispatch": "AiPrimitive",
  "assetId": "00000010-0000-0000-0000-000000000001",
  "schemaVersion": "1.0",
  "subsystemType": "Hrot.Blueprints",
  "primitive": { "intent": "Condition", "hostings": ["BTreeCondition"] },
  "graphs": [
    {
      "id": "10000010-0000-0000-0000-000000000001",
      "name": "Main",
      "kind": "Function",
      "nodes": [
        { "$type": "Return", "id": "20000010-0000-0000-0000-000000000001", "status": "Running", "pins": [], "metadata": {} }
      ],
      "links": [],
      "metadata": {}
    }
  ],
  "variables": [], "parameters": [], "customEvents": [], "eventDispatchers": [],
  "callablePeers": [], "workingState": [], "metadata": {}
}
```

**`ConditionWithDelay.bp.json`** -- AiPrimitive Condition with LatentDelayNode (semantically wrong):
```json
{
  "name": "ConditionWithDelay",
  "dispatch": "AiPrimitive",
  "assetId": "00000011-0000-0000-0000-000000000001",
  "schemaVersion": "1.0",
  "subsystemType": "Hrot.Blueprints",
  "primitive": { "intent": "Condition", "hostings": ["BTreeCondition"] },
  "graphs": [
    {
      "id": "10000011-0000-0000-0000-000000000001",
      "name": "Main",
      "kind": "Function",
      "nodes": [
        { "$type": "Delay", "id": "20000011-0000-0000-0000-000000000001", "seconds": 1.0, "pins": [], "metadata": {} }
      ],
      "links": [],
      "metadata": {}
    }
  ],
  "variables": [], "parameters": [], "customEvents": [], "eventDispatchers": [],
  "callablePeers": [], "workingState": [], "metadata": {}
}
```

**`AiPrimitiveParamsTooLarge.bp.json`** -- AiPrimitive with 100 parameters (exceeds schema limit):
```json
{
  "name": "AiPrimitiveParamsTooLarge",
  "dispatch": "AiPrimitive",
  "assetId": "00000012-0000-0000-0000-000000000001",
  "schemaVersion": "1.0",
  "subsystemType": "Hrot.Blueprints",
  "primitive": { "intent": "Action", "hostings": ["BTreeAction"] },
  "parameters": [
    { "name": "P01", "typeRef": { "clrTypeName": "System.Int32" } },
    { "name": "P02", "typeRef": { "clrTypeName": "System.Int32" } },
    { "name": "P03", "typeRef": { "clrTypeName": "System.Int32" } }
  ],
  "graphs": [], "variables": [], "customEvents": [], "eventDispatchers": [],
  "callablePeers": [], "workingState": [], "metadata": {}
}
```

**`InstanceStateExceedsLargestTier.bp.json`** -- declares more working state than fits in B16384:
```json
{
  "name": "InstanceStateExceedsLargestTier",
  "dispatch": "Instance",
  "assetId": "00000013-0000-0000-0000-000000000001",
  "schemaVersion": "1.0",
  "subsystemType": "Hrot.Blueprints",
  "workingState": [
    { "name": "HugeBuffer", "typeRef": { "clrTypeName": "System.Byte[]" } }
  ],
  "variables": [], "parameters": [], "customEvents": [], "eventDispatchers": [],
  "callablePeers": [], "graphs": [], "metadata": {}
}
```

### 4c. Create empty Snapshots directory structure

Create placeholder `.gitkeep` files in:
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Snapshots/Schedule/.gitkeep`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Snapshots/Emit/.gitkeep`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Snapshots/DebugMap/.gitkeep`

### 4d. Update `.csproj` for TestAssets and Snapshots

In `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj`, add inside `<Project>`:

```xml
  <ItemGroup>
    <Content Include="TestAssets\**\*" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="Snapshots\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

### 4e. Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestEventDefinitions.cs`

```csharp
using System.Numerics;
using Fdp.Core;

namespace Hrot.Blueprints.Tests;

/// <summary>Demo event types for Blueprint test scenarios.</summary>
[EventId(90010)]
internal struct HitEvent
{
    public Entity Target;
    public Entity Attacker;
    public float Damage;
    public Vector3 Direction;
}
```

### 4f. Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestData.cs`

```csharp
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests;

/// <summary>
/// Helpers for loading test assets from the TestAssets/ directory.
/// </summary>
public static class TestData
{
    public static class SampleAssets
    {
        public const string LibraryMath = "LibraryMath";
        public const string InstanceCounter = "InstanceCounter";
        public const string InstanceCounterV1ModifiedBody = "InstanceCounterV1ModifiedBody";
        public const string InstanceCounterV2WithBonus = "InstanceCounterV2WithBonus";
        public const string HealthRegen = "HealthRegen";
        public const string HasVisibleTarget = "HasVisibleTarget";
        public const string MoveToAndFire = "MoveToAndFire";
        public const string DoorActor = "DoorActor";
        public const string DoorSensor = "DoorSensor";
    }

    public static BlueprintAsset LoadAsset(string name)
    {
        var path = Path.Combine(ResolveTestAssetsDir(), name + ".bp.json");
        var json = File.ReadAllText(path);
        return BlueprintJsonServices.Deserialize(json)
            ?? throw new InvalidDataException($"Deserialized null from '{path}'");
    }

    public static string LoadSnapshot(string relativePath)
    {
        var path = Path.Combine(ResolveSnapshotsDir(), relativePath);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Snapshot not found: '{path}'", path);
        return File.ReadAllText(path);
    }

    /// <summary>
    /// When BLUEPRINT_REGENERATE_SNAPSHOTS=1 env var is set, writes the snapshot.
    /// Otherwise asserts the content matches the stored snapshot.
    /// </summary>
    public static void ReadOrRegenerateSnapshot(string relativePath, string actual)
    {
        var path = Path.Combine(ResolveSnapshotsDir(), relativePath);
        if (Environment.GetEnvironmentVariable("BLUEPRINT_REGENERATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
        }
        else
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Snapshot not found: '{path}'. Set BLUEPRINT_REGENERATE_SNAPSHOTS=1 to create.", path);
            var expected = File.ReadAllText(path);
            Xunit.Assert.Equal(expected, actual);
        }
    }

    /// <summary>
    /// Walk up from the current directory to find TestAssets/.
    /// Works both in bin/ output and when run from repo root.
    /// </summary>
    public static string ResolveTestAssetsDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "TestAssets");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            "TestAssets directory not found. Ensure CopyToOutputDirectory=PreserveNewest in .csproj.");
    }

    private static string ResolveSnapshotsDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "Snapshots");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Snapshots directory not found.");
    }
}
```

Note: `using Xunit;` is used inside `ReadOrRegenerateSnapshot`. The file needs `using Xunit;` at the top. Add it.

### 4g. Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/SampleAssetLoadTests.cs`

```csharp
namespace Hrot.Blueprints.Tests;

public sealed class SampleAssetLoadTests
{
    public static IEnumerable<object[]> AllSampleNames =>
        new[]
        {
            new object[] { TestData.SampleAssets.LibraryMath },
            new object[] { TestData.SampleAssets.InstanceCounter },
            new object[] { TestData.SampleAssets.InstanceCounterV1ModifiedBody },
            new object[] { TestData.SampleAssets.InstanceCounterV2WithBonus },
            new object[] { TestData.SampleAssets.HealthRegen },
            new object[] { TestData.SampleAssets.HasVisibleTarget },
            new object[] { TestData.SampleAssets.MoveToAndFire },
            new object[] { TestData.SampleAssets.DoorActor },
            new object[] { TestData.SampleAssets.DoorSensor },
        };

    [Theory]
    [MemberData(nameof(AllSampleNames))]
    public void LoadAsset_ValidSamples_ParseWithoutException(string name)
    {
        var asset = TestData.LoadAsset(name);
        Assert.NotNull(asset);
        Assert.NotEmpty(asset.Name);
    }

    [Fact]
    public void LoadAsset_InvalidConditionWithRunning_ParsesOk()
    {
        // Semantically invalid but syntactically valid -- should parse.
        var asset = TestData.LoadAsset("Invalid/ConditionWithRunning");
        Assert.NotNull(asset);
    }

    [Fact]
    public void LoadSnapshot_NonExistentSnapshot_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(
            () => TestData.LoadSnapshot("Schedule/LibraryMath.ir.txt"));
    }
}
```

---

## Deliverable 5: Update Fdp.Toolkits.csproj for unsafe blocks

The Blueprint component stubs (BlueprintBlackboard1024, etc.) use `unsafe` code with `fixed` arrays.
In `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj`, add `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` to the PropertyGroup.
Read the file first to find the correct location.

---

## Build and Test Verification

After implementing all deliverables:

1. Run `dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj`
   - Fix ALL errors before proceeding.
   - Warnings are OK, errors are not.

2. Run `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj`
   - Expected: previous 57 passing tests still pass + new tests for TH-008 (5+) and TH-009 (11+).
   - No regressions allowed.

3. Run `dotnet build FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj`
   - All new stub types must compile.

---

## Output: BATCH-03-REVIEW.md

After all tests pass, write `.dev/blueprints-1/reviews/BATCH-03-REVIEW.md` with:
- Summary of what was implemented
- Test counts (before / after)
- Any decisions or deviations
- Proposed git commit message

Format for commit message:
```
feat(blueprints): Phase 1 TH-008 CapturingDebugSession + TH-009 TestData (BATCH-03)

- TH-008: IBlueprintProbeSink, IBlueprintDebugSession, DebugProbe, CapturingDebugSession
- TH-009: 9 sample .bp.json files, 4 invalid .bp.json files, TestData class, SampleAssetLoadTests
- Foundation stubs: BlackboardTier, BlueprintLatentCursor, BlueprintCompileException, CompilerMode,
  BlueprintRegistry (minimal), BlueprintDefinition (minimal), BlueprintTickSystem stub,
  BlueprintMaintenanceSystem stub, BlueprintBlackboard1024/4096/16384 with ComponentIds 205-207,
  BlueprintBlackboardPartitions stub, BlueprintCompiler stub, InMemoryRoslynCompiler stub
- AllowUnsafeBlocks enabled in Fdp.Toolkits.csproj
```

---

## CORRECTION: JSON Schema Details

Before writing the .bp.json files, read `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Assets/Nodes.cs`,
`BlueprintAsset.cs`, `GraphTypes.cs`, and `Declarations.cs` to understand the exact property names.

Key findings (use these in the JSON files):

**Node polymorphic discriminator:** `"kind"` (lowercase, from `[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]`)

**BlueprintAsset top-level fields (PascalCase):**
- `"Header"` (object with `"SubsystemType"` and `"SchemaVersion"`)
- `"AssetId"`, `"Name"`, `"Dispatch"`, `"TierHint"`, `"IsWorldSingleton"`
- `"Primitive"` (AiPrimitive only), `"Parameters"`, `"WorkingState"`
- `"Variables"`, `"EventDispatchers"`, `"CustomEvents"`, `"CallablePeers"` (list of Guids)
- `"Graphs"`, `"EditorMetadata"`

**Graph fields:** `"Id"`, `"Name"`, `"Kind"`, `"Inputs"`, `"Outputs"`, `"Nodes"`, `"Links"`, `"EditorMetadata"`

**Node base fields:** `"kind"` (discriminator), `"Id"`, `"Pins"`, `"EditorMetadata"`

**Link fields (flat Guids):** `"FromNodeId"`, `"FromPinId"`, `"ToNodeId"`, `"ToPinId"`

**VariableDecl fields:** `"Id"`, `"Name"`, `"Type"` (BlueprintTypeRef), `"DefaultValueJson"`, `"IsEditable"`, `"IsExposedOnSpawn"`
**BlueprintTypeRef:** `"TypeId"` (string, e.g. `"System.Int32"`), `"IsArray"`, `"GenericArgs"`

**ReturnNode:** no extra fields beyond Node base
**LatentDelayNode:** no extra fields beyond Node base
**ChannelCommandNode:** `"ChannelType"`, `"ActionId"` (plus Node base fields)
**WaitForChannelNode:** `"ChannelType"` (plus Node base fields)
**FunctionCallNode:** `"TargetTypeId"`, `"MethodName"`, `"IsPure"` (plus Node base fields)
**SetVariableNode:** `"VariableId"` (Guid, plus Node base fields)

**Enum values (StrictStringEnumConverter, string-only):**
- `BlueprintDispatchKind`: `"Library"`, `"AiPrimitive"`, `"Instance"`
- `GraphKind`: `"Function"`, `"Event"`, `"Construction"`
- `AiPrimitiveIntent`: `"Action"`, `"Condition"`
- `AiPrimitiveHosting`: `"BTreeAction"`, `"BTreeCondition"`, `"HsmAction"`, `"HsmGuard"`, `"BlueprintCall"`

**Property names are PascalCase** because there is no `PropertyNamingPolicy` set.
`PropertyNameCaseInsensitive = true` means deserialization is case-insensitive.

**Recommended approach:** Write a single minimal test JSON manually, verify it parses, then use that
as the template for all 9 files. A correct minimal Asset JSON:
```json
{
  "Header": { "SubsystemType": "Hrot.Blueprints", "SchemaVersion": "1.0" },
  "AssetId": "00000001-0000-0000-0000-000000000001",
  "Name": "LibraryMath",
  "Dispatch": "Library",
  "Graphs": [],
  "Variables": [],
  "Parameters": [],
  "WorkingState": [],
  "CustomEvents": [],
  "EventDispatchers": [],
  "CallablePeers": [],
  "EditorMetadata": {}
}
```

A node with links:
```json
{
  "kind": "EventEntry",
  "Id": "20000001-0000-0000-0000-000000000001",
  "Pins": [],
  "EditorMetadata": {}
}
```

A link between two nodes (using Guid.Empty for pin IDs when no real pins exist):
```json
{
  "FromNodeId": "20000001-0000-0000-0000-000000000001",
  "FromPinId": "00000000-0000-0000-0000-000000000000",
  "ToNodeId": "20000001-0000-0000-0000-000000000002",
  "ToPinId": "00000000-0000-0000-0000-000000000000"
}
```

**Verify every JSON file parses** by running a quick dotnet test after creating the files.
Fix any parse errors before committing.

---

## Key Constraints (continued)

9. The `BlueprintBlackboard1024` `fixed byte Data[1024]` struct needs `unsafe`. Check that `AllowUnsafeBlocks` is added to `Fdp.Toolkits.csproj` (Deliverable 5).

10. The `BlueprintBlackboardHeader` `MagicValue` constant: use `0x42503132u` (ASCII "BP12"), NOT `0xBP_1234u` which is invalid hex syntax.

11. `TestData.cs` needs `using Xunit;` for `Assert.Equal` in `ReadOrRegenerateSnapshot`. Alternatively, just use `if (expected != actual) throw new Exception(...)` to avoid Xunit coupling in the helper. Prefer the non-Xunit approach.
