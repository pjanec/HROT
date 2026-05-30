# BATCH-33 Instructions — Phase 6 Part B: Blueprint Host for Squad Logic (P6-02)

**Covers:** TASK-SQD-P6-02  
**Design reference:** `.dev/group-maneuvers/Squad_Coordination_Design_v1_1.md` §7 item 2

---

## Context

Blueprint nodes wrapping the 4 squad primitives:
- `PartitionElementsNode` — wraps `ElementPartitionPrimitive.Partition()`
- `AssignRolesNode`       — wraps `RoleSlotAssignmentPrimitive.AssignRoles()`
- `AdvancePhaseNode`      — wraps `PhaseSequencer.Advance()`
- `AcquireSlotNode`       — wraps `SlotRotation.AcquireSlot()`

Plus a worked example Blueprint JSON showing BoundingOverwatch's swap-on-bound logic.

---

## Task 1 — Add node types to `Nodes.cs`

**Modify** (TARGETED — only add lines; never remove or reformat existing lines):
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/Nodes.cs`

After the LAST existing `[JsonDerivedType]` attribute (currently `ReadRankedResult`), add exactly 4 new ones:
```csharp
[JsonDerivedType(typeof(PartitionElementsNode), "PartitionElements")]
[JsonDerivedType(typeof(AssignRolesNode),        "AssignRoles")]
[JsonDerivedType(typeof(AdvancePhaseNode),       "AdvancePhase")]
[JsonDerivedType(typeof(AcquireSlotNode),        "AcquireSlot")]
```

After the LAST class in the file (`ReadRankedResultNode`), append these four class definitions:

```csharp
// ──────────────────────────────────────────────────────────────────────────
// Squad Primitive Nodes (TASK-SQD-P6-02 -- Blueprint host for squad logic)
// These nodes wrap the five squad coordination primitives (Phase 1 library).
// The node carries only authoring-time configuration; execution is delegated
// to the corresponding FDP primitive at IR stage.
// ──────────────────────────────────────────────────────────────────────────

/// <summary>
/// Partition squad members into N elements (wraps ElementPartitionPrimitive.Partition).
/// </summary>
public sealed class PartitionElementsNode : Node
{
    /// <summary>Number of elements to partition into (e.g. 2 for Lead/Overwatch).</summary>
    public int ElementCount { get; set; } = 2;
}

/// <summary>
/// Assign roles to squad members via greedy matrix (wraps RoleSlotAssignmentPrimitive.AssignRoles).
/// </summary>
public sealed class AssignRolesNode : Node
{
    /// <summary>The ManeuverKind whose StandardCandidates table to use (e.g. 2 for BoundingOverwatch).</summary>
    public ushort ManeuverKind { get; set; }
}

/// <summary>
/// Advance the phase sequencer one step (wraps PhaseSequencer.Advance).
/// </summary>
public sealed class AdvancePhaseNode : Node
{
    /// <summary>Phase ID to jump to if dwell timeout elapses. Use the terminal Aborted phase.</summary>
    public ushort AbortPhaseId { get; set; }
    /// <summary>Dwell timeout in simulation ticks (0 = never timeout).</summary>
    public uint DwellTimeoutTicks { get; set; }
}

/// <summary>
/// Acquire the next available slot from the slot rotation ring (wraps SlotRotation.AcquireSlot).
/// </summary>
public sealed class AcquireSlotNode : Node
{
    /// <summary>Total number of slots in the ring.</summary>
    public int TotalSlots { get; set; } = 1;
}
```

---

## Task 2 — New `SquadPrimitiveNodeCatalog.cs`

**New file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Catalogs/SquadPrimitiveNodeCatalog.cs`

This catalog lists the 4 squad primitive node types with their display metadata.
It does NOT require INodeRegistry (which is still a stub) — it is its own static catalog.

```csharp
namespace Hrot.Blueprints.Core.Compiler.Catalogs;

/// <summary>
/// Catalog of Blueprint node types wrapping the 5 squad coordination primitives (TASK-SQD-P6-02).
/// Provides display metadata for use by the Blueprint editor palette.
/// </summary>
public static class SquadPrimitiveNodeCatalog
{
    /// <summary>All squad primitive node entries.</summary>
    public static readonly SquadPrimitiveNodeEntry[] Entries = new SquadPrimitiveNodeEntry[]
    {
        new SquadPrimitiveNodeEntry(
            Kind:        "PartitionElements",
            DisplayName: "Partition Elements",
            Category:    "Squad/Primitives",
            Tooltip:     "Partition squad members into N elements with hysteresis."),
        new SquadPrimitiveNodeEntry(
            Kind:        "AssignRoles",
            DisplayName: "Assign Roles",
            Category:    "Squad/Primitives",
            Tooltip:     "Assign roles to squad members via greedy score matrix."),
        new SquadPrimitiveNodeEntry(
            Kind:        "AdvancePhase",
            DisplayName: "Advance Phase",
            Category:    "Squad/Primitives",
            Tooltip:     "Advance the squad phase sequencer on an event."),
        new SquadPrimitiveNodeEntry(
            Kind:        "AcquireSlot",
            DisplayName: "Acquire Slot",
            Category:    "Squad/Primitives",
            Tooltip:     "Acquire the next available slot from the rotation ring."),
    };
}

/// <summary>Metadata entry for a squad primitive Blueprint node type.</summary>
public sealed record SquadPrimitiveNodeEntry(
    string Kind,
    string DisplayName,
    string Category,
    string Tooltip);
```

---

## Task 3 — Worked example Blueprint JSON

**New file:**
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/BoundingOverwatchSwap.bp.json`

This is the "worked Blueprint authoring of the bounding-overwatch swap-on-bound sub-logic."
It represents, in Blueprint JSON format, how a Blueprint would call the squad primitive nodes.

```json
{
  "Header": { "SubsystemType": "Hrot.Blueprints", "SchemaVersion": "1.0" },
  "AssetId": "00000000-bbbb-0001-0000-000000000001",
  "Name": "BoundingOverwatchSwap",
  "Dispatch": 2,
  "TierHint": 0,
  "Primitive": null,
  "Parameters": [],
  "WorkingState": [],
  "Variables": [],
  "EventDispatchers": [],
  "CustomEvents": [],
  "CallablePeers": [],
  "Graphs": [
    {
      "Id": "b6000001-0001-0001-0001-000000000001",
      "Name": "SwapOnBound",
      "Kind": 0,
      "Inputs": [],
      "Outputs": [],
      "Nodes": [
        {
          "kind": "EventEntry",
          "Id": "c6000001-0001-0001-0001-000000000001",
          "EventTypeId": "",
          "Pins": [],
          "EditorMetadata": {}
        },
        {
          "kind": "AdvancePhase",
          "Id": "c6000001-0002-0002-0002-000000000002",
          "AbortPhaseId": 2,
          "DwellTimeoutTicks": 0,
          "Pins": [],
          "EditorMetadata": {}
        },
        {
          "kind": "AssignRoles",
          "Id": "c6000001-0003-0003-0003-000000000003",
          "ManeuverKind": 2,
          "Pins": [],
          "EditorMetadata": {}
        },
        {
          "kind": "Return",
          "Id": "c6000001-0004-0004-0004-000000000004",
          "Status": 1,
          "Pins": [],
          "EditorMetadata": {}
        }
      ]
    }
  ]
}
```

---

## Task 4 — Tests

**New file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Squad/SquadPrimitiveNodeTests.cs`

### SC-P6-02-1: Catalog has 4 node entries in Squad/Primitives category

```csharp
[Fact]
public void SquadPrimitiveNodeCatalog_HasFourEntriesInSquadCategory()
{
    var entries = SquadPrimitiveNodeCatalog.Entries;
    Assert.Equal(4, entries.Length);
    Assert.All(entries, e => Assert.Equal("Squad/Primitives", e.Category));
    Assert.Contains(entries, e => e.Kind == "PartitionElements");
    Assert.Contains(entries, e => e.Kind == "AssignRoles");
    Assert.Contains(entries, e => e.Kind == "AdvancePhase");
    Assert.Contains(entries, e => e.Kind == "AcquireSlot");
}
```

### SC-P6-02-1b: All 4 node types are JSON-serializable with correct kind discriminator

```csharp
[Fact]
public void SquadPrimitiveNodes_JsonRoundTrip_PreservesKindDiscriminator()
{
    var nodes = new Node[]
    {
        new PartitionElementsNode { Id = Guid.NewGuid(), ElementCount = 2 },
        new AssignRolesNode       { Id = Guid.NewGuid(), ManeuverKind = 2 },
        new AdvancePhaseNode      { Id = Guid.NewGuid(), AbortPhaseId = 3, DwellTimeoutTicks = 0 },
        new AcquireSlotNode       { Id = Guid.NewGuid(), TotalSlots = 6 },
    };

    var options = new System.Text.Json.JsonSerializerOptions();
    foreach (var node in nodes)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(node, options);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<Node>(json, options);
        Assert.NotNull(deserialized);
        Assert.Equal(node.GetType(), deserialized.GetType());
    }
}
```

### SC-P6-02-2: Worked example Blueprint JSON loads and contains squad primitive nodes

```csharp
[Fact]
public void BoundingOverwatchSwap_Blueprint_LoadsAndContainsSquadNodes()
{
    // Load the worked example Blueprint JSON.
    var jsonPath = Path.Combine(
        Path.GetDirectoryName(typeof(SquadPrimitiveNodeTests).Assembly.Location)!,
        "TestAssets", "Recipes", "BoundingOverwatchSwap.bp.json");
    var json = File.ReadAllText(jsonPath);

    var asset = System.Text.Json.JsonSerializer.Deserialize<BlueprintAsset>(json);
    Assert.NotNull(asset);

    var graph = Assert.Single(asset.Graphs);
    Assert.Equal("SwapOnBound", graph.Name);

    // Verify squad primitive nodes are present in the graph.
    Assert.Contains(graph.Nodes, n => n is AdvancePhaseNode);
    Assert.Contains(graph.Nodes, n => n is AssignRolesNode a && a.ManeuverKind == 2);
}
```

### SC-P6-02-2b: Calling underlying squad primitives on bounding-overwatch fixture produces same outcome as HSM form

```csharp
[Fact]
public void BoundingOverwatchSwap_PrimitiveCalls_MatchHsmOutcome()
{
    // This test represents what the Blueprint's AdvancePhase + AssignRoles nodes
    // would execute at runtime, verifying the "same outcomes as the HSM version."
    var state = default(SquadCognitiveState);
    state.PhaseId = BoundingOverwatchManeuver.PhaseElement0Moving;
    state.PhaseEnteredTick = 0;

    var table = BoundingOverwatchManeuver.BuildTransitionTable();

    // Simulate: BoundComplete event -> should advance to PhaseElement1Moving.
    bool advanced = PhaseSequencer.Advance(ref state,
        new System.ReadOnlySpan<PhaseEvent>(new[] { new PhaseEvent(PhaseEventKind.BoundComplete) }),
        table, currentTick: 5, dwellTimeoutTicks: 0, recoveryPhaseId: BoundingOverwatchManeuver.PhaseAborted);

    Assert.True(advanced);
    Assert.Equal(BoundingOverwatchManeuver.PhaseElement1Moving, state.PhaseId);
}
```

---

## Usings for `SquadPrimitiveNodeTests.cs`

```csharp
using System;
using System.IO;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;
using Xunit;
```

IMPORTANT: Check if `Hrot.Blueprints.Tests.csproj` already references `Fdp.Toolkits`. If NOT, do NOT add a project reference — instead, remove the SC-P6-02-2b test that uses FDP types and only keep the Blueprint tests that don't need FDP.

Read `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj` before writing tests to verify which namespaces are available.

---

## Build and test commands

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj -c Debug 2>&1 | Select-Object -Last 3
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj --no-build --filter "FullyQualifiedName~Squad" 2>&1 | Select-Object -Last 5
```

Also verify no regressions in Blueprint tests:
```powershell
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj --no-build 2>&1 | Select-Object -Last 5
```

Fix all errors. Do NOT commit.

Write a report to `.dev/group-maneuvers/reports/BATCH-33-REPORT.md`.

---

## File summary

| Action | File |
|---|---|
| MODIFY (targeted) | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/Nodes.cs` |
| CREATE | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Catalogs/SquadPrimitiveNodeCatalog.cs` |
| CREATE | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/BoundingOverwatchSwap.bp.json` |
| CREATE | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Squad/SquadPrimitiveNodeTests.cs` |
