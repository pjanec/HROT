# BATCH-03: NodeOpType in NodeOpCompletedEvent + ScenarioSerializer Layer Fix

**Batch Number:** BATCH-03  
**Tasks:** TASK-D02, TASK-D07 (partial)  
**Phase:** 3 — Anti-Corruption Layer Improvements and Architecture Cleanup  
**Estimated Effort:** 5–8 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-02 (approved and complete)

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.github/skills/developer/SKILL.md`
2. **Task Definitions:** `.dev/cluster-master-cqrs-2/TASK-DEFINITIONS.md` — see TASK-D02, TASK-D07
3. **Code Standards:** `.github/skills/CODE-STANDARDS.md`
4. **Previous Review:** `.dev/cluster-master-cqrs-2/reviews/BATCH-02-REVIEW.md`

### Source Code Locations
- **FDP events:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterCqrsEvents.cs`
- **DDS structs:** `Hrot.NED/Orchestration/OrchestrationMessages.cs`
- **Slave translator:** `Hrot.Common/Orchestration/NodeOpSlaveTranslator.cs`
- **Master translator:** `Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs`
- **ClusterMaster:** `Hrot.Orchestrator/ClusterMaster.cs`
- **ScenarioSerializer:** `FDP/Toolkits/FDP.Toolkit.Scenario/ScenarioSerializer.cs`
- **ScenarioHeader:** `FDP/Toolkits/FDP.Toolkit.Scenario/ScenarioHeader.cs`
- **Handlers (3 to change):** `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferenceScenarioLoadHandler.cs`, `ReferenceEditLoadHandler.cs`, `ReferenceEpisodeLoadHandler.cs`
- **New file to create:** `Hrot.Common/Scenario/HrotScenarioEnvelope.cs`

### Test Projects
- `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/`
- `FDP/Toolkits/FDP.Toolkit.Scenario.Tests/`
- `Hrot.Orchestrator.Tests/` — NodeOpMasterTranslatorTests.cs, ClusterMasterArchiveTests.cs
- `Hrot.Orchestrator.Integration.Tests/`
- `Hrot.SimHost.Tests/`
- `Hrot.SimHost.Integration.Tests/`

### Report Destination
`.dev/cluster-master-cqrs-2/reports/BATCH-03-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW

1. **TASK-D02 first:** Add `NodeOpType Operation` to event + DDS struct; update translators; extend ClusterMaster bus-mode. Build → test → fix → all pass.
2. **TASK-D07 second:** Strip `PeekSubsystemType`/`IsMatchingSubsystem` from FDP; add `HrotScenarioEnvelope`; update handlers. Build → test → fix → all pass.

Do NOT stop and ask for permission for any obvious step. Work through the fix-check loop until all tests pass.

Build & test commands (run from `d:\Work\IOS-IG-SimHost-FDP-2`):
```powershell
dotnet build IOS-IG-SimHost.sln
dotnet test FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/FDP.Toolkit.Orchestration.Tests.csproj --no-build -v n
dotnet test FDP/Toolkits/FDP.Toolkit.Scenario.Tests/FDP.Toolkit.Scenario.Tests.csproj --no-build -v n
dotnet test Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj --no-build -v n
dotnet test Hrot.Orchestrator.Integration.Tests/Hrot.Orchestrator.Integration.Tests.csproj --no-build -v n
dotnet test Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build -v n
```

---

## ✅ Task A: TASK-D02 — NodeOpType in NodeOpCompletedEvent and NodeOpStatus

**Task Definition:** See [TASK-DEFINITIONS.md](../TASK-DEFINITIONS.md#task-d02--add-nodeoptype-to-nodeopcompletedevent-and-nodeopstatus)

### Step A1: Add `Operation` field to `NodeOpCompletedEvent`

**File:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterCqrsEvents.cs`

Add `NodeOpType Operation;` as the second field of `NodeOpCompletedEvent` (after `TransactionId`):

```csharp
public struct NodeOpCompletedEvent
{
    public Guid TransactionId;
    public NodeOpType Operation;    // ← NEW
    public int NodeId;
    public OrchestrationStatusCode StatusCode;
    public bool IsParticipating;
    public object? ResultPayload;
}
```

### Step A2: Add `Operation` field to `NodeOpStatus` DDS struct

**File:** `Hrot.NED/Orchestration/OrchestrationMessages.cs`

Add `NodeOpType Operation;` to `NodeOpStatus` (after `TransactionId`):

```csharp
public partial struct NodeOpStatus
{
    public Guid TransactionId;
    public NodeOpType Operation;    // ← NEW
    public int NodeId;
    public int StatusCode;
    public bool IsParticipating;
    [DdsManaged] public string ResultJson;
}
```

Use `Hrot.NED.Descriptors.Orchestration.NodeOpType` which is already defined in that file.

### Step A3: Update `NodeOpSlaveTranslator.Tick()`

**File:** `Hrot.Common/Orchestration/NodeOpSlaveTranslator.cs`

In the "Status egress" section (around line 97), set `Operation`:

```csharp
_statusWriter.Write(new NodeOpStatus
{
    TransactionId   = ev.TransactionId,
    Operation       = (NedNodeOpType)(int)ev.Operation,   // ← NEW
    NodeId          = ev.NodeId,
    StatusCode      = (int)ev.StatusCode,
    IsParticipating = ev.IsParticipating,
    ResultJson      = SerializeResultPayload(ev.ResultPayload),
});
```

(`NedNodeOpType` is already aliased at the top of the file as `using NedNodeOpType = Hrot.NED.Descriptors.Orchestration.NodeOpType;`)

### Step A4: Update `NodeOpMasterTranslator.Tick()` and `DeserializeResultPayload()`

**File:** `Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs`

In the "Ingress" section (around line 87), copy `Operation` and pass it to `DeserializeResultPayload`:

```csharp
var fdpOp         = (FdpNodeOpType)(int)status.Operation;
var resultPayload = DeserializeResultPayload(fdpOp, status.ResultJson);
_bus.PublishManaged(new NodeOpCompletedEvent
{
    TransactionId   = status.TransactionId,
    Operation       = fdpOp,                     // ← NEW
    NodeId          = status.NodeId,
    StatusCode      = (OrchestrationStatusCode)status.StatusCode,
    IsParticipating = status.IsParticipating,
    ResultPayload   = resultPayload,
});
```

Change `DeserializeResultPayload` signature and body:

```csharp
/// <summary>
/// Deserialises the <c>ResultJson</c> from a <see cref="NodeOpStatus"/> into a typed domain
/// result object based on the operation type.
/// </summary>
private static object? DeserializeResultPayload(FdpNodeOpType operation, string? resultJson)
{
    if (string.IsNullOrWhiteSpace(resultJson)) return null;

    switch (operation)
    {
        case FdpNodeOpType.SerializeLocal:
        {
            try
            {
                var entries = System.Text.Json.JsonSerializer.Deserialize<List<FileManifestEntry>>(
                    resultJson!,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return entries;
            }
            catch
            {
                return null;
            }
        }
        default:
            return null;
    }
}
```

You will need `using Hrot.Orchestrator;` in NodeOpMasterTranslator.cs (for `FileManifestEntry`). Check if it's already in the file; if not, add it.

### Step A5: Extend `ClusterMaster.ConsumeNodeOpStatuses()` bus-mode path

**File:** `Hrot.Orchestrator/ClusterMaster.cs`

Currently, the bus-mode path (inside `if (_eventBus != null)`) only handles `_pendingBusTransitionAcks` and then `return`s. This path does NOT handle SerializeLocal results at all. Extend it:

After the transition ACK handling block, add SerializeLocal ACK handling BEFORE the `return`:

```csharp
if (_eventBus != null)
{
    foreach (var ev in _eventBus.ConsumeManaged<NodeOpCompletedEvent>())
    {
        // Transition ACK handling (existing — no changes)
        if (_pendingBusTransitionAcks.TryGetValue(ev.TransactionId, out var tracker))
        {
            if (ev.StatusCode.IsError())
            {
                tracker.HasFailure  = true;
                tracker.FailureCode = ev.StatusCode;
            }
            tracker.Received++;
            if (tracker.Received >= tracker.Expected)
            {
                _pendingBusTransitionAcks.Remove(ev.TransactionId);
                PublishOpStatus(tracker.RequestId,
                    tracker.HasFailure ? tracker.FailureCode : OrchestrationStatusCode.Success);
            }
            continue;  // ← Add continue to avoid fall-through
        }

        // NEW: SerializeLocal ACK handling for bus-mode
        if (ev.Operation == FDP.Toolkit.Orchestration.NodeOpType.SerializeLocal &&
            _pendingSerializeTasks.TryGetValue(ev.TransactionId, out var serTask))
        {
            if (!ev.StatusCode.IsError() && ev.ResultPayload is List<FileManifestEntry> entries)
                serTask.Manifests.AddRange(entries);
            else if (ev.StatusCode.IsError())
                serTask.FailureCount++;

            serTask.RemainingAcks--;
            if (serTask.RemainingAcks <= 0)
            {
                _pendingSerializeTasks.Remove(ev.TransactionId);
                HandleSerializeLocalCompletion(serTask);
            }
        }
    }
    return;
}
```

Then extract the completion logic into a helper method `HandleSerializeLocalCompletion(SerializeLocalTask task)` that contains the logic currently at lines ~1394–1475 (the archive export, legacy save, global-context manifest append, etc.). The DDS path calls this same helper.

**Important:** Look at the full DDS-path logic for completing a `SerializeLocalTask` (lines 1383–1475 in the current file) and extract the "what to do when all ACKs arrive" part into a private helper method:

```csharp
private void HandleSerializeLocalCompletion(SerializeLocalTask task)
{
    if (task.FailureCount > 0)
        FdpLog<ClusterMaster>.Error(...);
    
    // Append orchestrator's own manifest if any
    if (_globalContextHandler?.CommitManifestEntry != null)
        task.Manifests.Add(_globalContextHandler.CommitManifestEntry);
    
    if (task.ArchiveCts != null) { /* archive path */ }
    else if (_gateway != null && task.Manifests.Count > 0) { /* save scenario path */ }
    else { /* no manifests path */ }
}
```

The DDS path changes to call `HandleSerializeLocalCompletion(task)` instead of inline logic.

**Note on the `continue`:** Before this change, if an event matched `_pendingBusTransitionAcks`, it would fall through to the serialize check. Adding `continue` is correct — each event should only be handled once.

### Step A6: Tests for TASK-D02

**In `Hrot.Orchestrator.Tests/NodeOpMasterTranslatorTests.cs`:** Add a test verifying that `DeserializeResultPayload` for `SerializeLocal` returns `List<FileManifestEntry>` when given valid JSON, and `null` for other operations or empty JSON.

**In `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/FdpOrchestrationCqrsStructTests.cs`:** If that file validates `NodeOpCompletedEvent` fields, update it to include the new `Operation` field.

Update any existing test that constructs `NodeOpCompletedEvent { TransactionId = ..., NodeId = ..., StatusCode = ... }` — the new `Operation` field will default to `0` (which maps to `PrepareState`). Add `Operation = NodeOpType.CommitState` (or whichever is appropriate) to make tests explicit.

---

## ✅ Task B: TASK-D07 (Partial) — Strip PeekSubsystemType from FDP

**Task Definition:** See [TASK-DEFINITIONS.md](../TASK-DEFINITIONS.md#task-d07--scenarioserializer-strip-hrot-specific-knowledge)

**Scope of this batch:** Remove `PeekSubsystemType()` and `IsMatchingSubsystem()` from `ScenarioSerializer` and create `HrotScenarioEnvelope` in `Hrot.Common`. The `Serialize/Deserialize` API signature changes and `ScenarioHeader` removal are **deferred** — this batch only removes the two leaking methods and moves them to the correct layer.

### Step B1: Create `Hrot.Common/Scenario/HrotScenarioEnvelope.cs`

Create new directory `Hrot.Common/Scenario/` and file:

```csharp
using System;
using System.Text.Json.Nodes;

namespace Hrot.Common.Scenario;

/// <summary>
/// Application-layer helper that knows the Hrot scenario file envelope format:
/// <c>{ "Header": { "SubsystemType": "...", "SchemaVersion": 1 }, "Entities": { ... } }</c>.
///
/// <para>Moved here from <c>FDP.Toolkit.Scenario.ScenarioSerializer</c> to keep
/// the FDP engine toolkit free of application-layer format knowledge.</para>
/// </summary>
public static class HrotScenarioEnvelope
{
    /// <summary>
    /// Parses the <c>Header.SubsystemType</c> value from raw scenario JSON text
    /// without a full DOM parse. Returns <see langword="null"/> on failure.
    /// </summary>
    public static string? PeekSubsystemType(string jsonText)
    {
        try
        {
            var node = JsonNode.Parse(jsonText);
            return node?["Header"]?["SubsystemType"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="subsystemType"/> matches
    /// <paramref name="expected"/> (ordinal, case-sensitive).
    /// </summary>
    public static bool IsMatchingSubsystem(string? subsystemType, string expected)
        => string.Equals(subsystemType, expected, StringComparison.Ordinal);
}
```

Also add the new directory's project reference if needed — check that `Hrot.Common.csproj` will pick up files in `Hrot.Common/Scenario/` (it should by default with glob patterns).

### Step B2: Add `_subsystemType` field exposure

The three handlers currently call `_serializer.IsMatchingSubsystem(subsysType)` to check if the subsystem type in a file matches the serializer's own type. For `HrotScenarioEnvelope.IsMatchingSubsystem(subsysType, expected)`, the `expected` value needs to come from somewhere.

Option A: Expose the subsystem type from `ScenarioSerializer` as a read-only property:  
Add `public string SubsystemType => _subsystemType;` to `ScenarioSerializer`.

Option B: Each handler already knows its subsystem type from construction context, or it can pass the serializer's type to the new method.

**Use Option A** — it is the least-invasive change. The FDP ScenarioSerializer still knows its own subsystem type (that's just internal config, not "Hrot knowledge"). The issue was the `PeekSubsystemType` and `IsMatchingSubsystem` methods that parsed Hrot file envelopes — not the fact that the serializer was initialized with a type string.

Add to `ScenarioSerializer.cs`:
```csharp
/// <summary>
/// The subsystem type string this serializer was built for.
/// Used by application-layer handlers to determine if a scenario file belongs
/// to this subsystem via <see cref="Hrot.Common.Scenario.HrotScenarioEnvelope.IsMatchingSubsystem"/>.
/// </summary>
public string SubsystemType => _subsystemType;
```

### Step B3: Update the three handlers to use `HrotScenarioEnvelope`

**Files:**
- `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferenceScenarioLoadHandler.cs` (line ~96)
- `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferenceEditLoadHandler.cs` (line ~89)
- `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferenceEpisodeLoadHandler.cs` (line ~136)

**Pattern to change in all three:**
```csharp
// Before:
var subsysType = _serializer.PeekSubsystemType(text);
if (!_serializer.IsMatchingSubsystem(subsysType)) continue;

// After:
var subsysType = HrotScenarioEnvelope.PeekSubsystemType(text);
if (!HrotScenarioEnvelope.IsMatchingSubsystem(subsysType, _serializer.SubsystemType)) continue;
```

Add `using Hrot.Common.Scenario;` to each handler. Also, you need to add a project reference: `FDP.Toolkit.Orchestration` needs to reference `Hrot.Common`. Check the `.csproj` files:
- `FDP/Toolkits/FDP.Toolkit.Orchestration/FDP.Toolkit.Orchestration.csproj` — add `<ProjectReference Include="..." />` for `Hrot.Common` if it doesn't already exist.

**IMPORTANT:** Check for circular references first. If `Hrot.Common` already depends on `FDP.Toolkit.Orchestration`, then this would create a circular dependency — in that case, use a different approach: move the handlers OUT of `FDP.Toolkit.Orchestration.Handlers` into `Hrot.Common.Orchestration.Handlers` instead (see alternative below).

**If circular dependency exists:** Move the three handlers to `Hrot.Common/Orchestration/Handlers/` (creating the directory). Update their namespace to `Hrot.Common.Orchestration.Handlers`. Update composition roots (`Hrot.SimHost/`, `Hrot.IG/`, etc.) that reference these handler types.

### Step B4: Remove `PeekSubsystemType` and `IsMatchingSubsystem` from `ScenarioSerializer`

**File:** `FDP/Toolkits/FDP.Toolkit.Scenario/ScenarioSerializer.cs`

Delete the `IsMatchingSubsystem()` method (lines ~63-65) and `PeekSubsystemType()` method (lines ~71-80).  
Update the XML doc comment on the class to remove the "Peek Header.SubsystemType" reference in the Load pipeline description.

Remove `using System.Text.Json;` from `ScenarioSerializer.cs` if `PeekSubsystemType` was the only user of it — check first.

### Step B5: Tests for TASK-D07

**New tests in `Hrot.Common` test project (or `Hrot.SimHost.Tests`):**

Add tests for `HrotScenarioEnvelope`:
```csharp
[Fact]
public void HrotScenarioEnvelope_PeekSubsystemType_ReturnsCorrectType()
{
    const string json = @"{""Header"":{""SubsystemType"":""Hrot.SimHost"",""SchemaVersion"":1},""Entities"":{}}";
    Assert.Equal("Hrot.SimHost", HrotScenarioEnvelope.PeekSubsystemType(json));
}

[Fact]
public void HrotScenarioEnvelope_PeekSubsystemType_ReturnsNullForInvalidJson()
{
    Assert.Null(HrotScenarioEnvelope.PeekSubsystemType("not json"));
}

[Fact]
public void HrotScenarioEnvelope_IsMatchingSubsystem_TrueForExactMatch()
{
    Assert.True(HrotScenarioEnvelope.IsMatchingSubsystem("Hrot.SimHost", "Hrot.SimHost"));
}

[Fact]
public void HrotScenarioEnvelope_IsMatchingSubsystem_FalseForCaseMismatch()
{
    Assert.False(HrotScenarioEnvelope.IsMatchingSubsystem("hrot.simhost", "Hrot.SimHost"));
}
```

Verify it is correct project for these tests — if `Hrot.SimHost.Tests` already references `Hrot.Common`, that's the right place; otherwise put them in a test project that references `Hrot.Common`.

---

## ⚠️ Quality Standards

**TASK-D02:**
- Every test that constructs `NodeOpCompletedEvent` must have an explicit `Operation` value — no implicit zero defaults in tests.
- The `DeserializeResultPayload` test must verify actual type: `Assert.IsType<List<FileManifestEntry>>(result)` and verify actual values, not just null-checks.

**TASK-D07 (Step B3):**
- Verify no circular dependency before adding the project reference.
- After the change, GREP the entire codebase for `_serializer.PeekSubsystemType` and `_serializer.IsMatchingSubsystem` — there must be zero matches.

---

## 📊 Report Requirements

**Q1:** Was the `continue` keyword missing from the bus-mode transition ACK block before? Did adding it change any behavior? How did you determine it was safe to add?

**Q2:** Was there a circular dependency issue when trying to reference `Hrot.Common` from `FDP.Toolkit.Orchestration`? How did you resolve it?

**Q3:** What tests in `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/` needed updating due to the new `Operation` field on `NodeOpCompletedEvent`?

**Q4:** What edge cases did you discover in `HandleSerializeLocalCompletion()` extraction? Were there any state side-effects that couldn't be cleanly extracted?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `NodeOpCompletedEvent.Operation` field exists and is `NodeOpType`.
- [ ] `NodeOpStatus.Operation` field exists and is `NedNodeOpType`.
- [ ] Translators correctly set and read `Operation` at both ends.
- [ ] `DeserializeResultPayload()` returns `List<FileManifestEntry>` for SerializeLocal.
- [ ] Bus-mode `ConsumeNodeOpStatuses()` handles SerializeLocal results.
- [ ] `HrotScenarioEnvelope` created in `Hrot.Common/Scenario/`.
- [ ] `PeekSubsystemType` and `IsMatchingSubsystem` removed from `ScenarioSerializer`.
- [ ] Three handlers use `HrotScenarioEnvelope.*` instead of `_serializer.*`.
- [ ] No broken circular dependencies.
- [ ] Full build: 0 errors.
- [ ] All affected test projects pass.
- [ ] Report submitted.

---

## 📚 Reference Materials
- **Task Definitions:** [TASK-DEFINITIONS.md](../TASK-DEFINITIONS.md) — TASK-D02, TASK-D07
- **BATCH-02 review:** `.dev/cluster-master-cqrs-2/reviews/BATCH-02-REVIEW.md`
- **FileManifestEntry:** `Hrot.Orchestrator/StorageGatewayModule.cs`
- **FileManifestResult:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferenceArchiveHandler.cs`
- **Handler examples:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferenceScenarioLoadHandler.cs`, `ReferenceEditLoadHandler.cs`, `ReferenceEpisodeLoadHandler.cs`
