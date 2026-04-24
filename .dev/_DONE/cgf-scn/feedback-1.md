I have reviewed the implementation in the sources. While you successfully integrated the expression-tree compilers and corrected the episode teardown pipeline, there are critical architectural remnants and missing abstractions that violate our previous design constraints. 

Here is the objective architectural assessment of your updates.

### The Good: Compiler Architecture and Teardown Pipeline
Your implementation of the `BehaviorParamRemapperCompiler` is architecturally excellent. By building typed expression trees (`Expression.Property`, `Expression.Assign`) to evaluate getters and setters dynamically, you completely bypassed the heavy CPU overhead of `PropertyInfo.GetValue/SetValue` on the hot path. The transient in-place mutation of the managed `ActiveMissionPlan` JSON strings during extraction is now safely handled.

Furthermore, the `CgfEpisodeLoadHandler` correctly adheres to the distributed genesis guarantees. On `StopEpisode`, it now collects the `NetworkIdentity` of the episode's entities and routes their destruction through the local event bus via `DestroyEntityCommand`, preventing the ghost entity memory leaks that plagued the previous direct `DestroyEntity` calls.

### Critical Flaw 1: Incomplete UI Abstraction (The Hardcoded Methods)
While you successfully built the `BehaviorUiCompiler` to generate ImGui delegates from DTO attributes (`[MapPickableEntity]`, `[MapPickableWorldLocation]`), you failed to clean up the legacy UI presentation layer.

In `MissionPanel.cs`, the presentation hot path still contains hardcoded structural checks for specific behaviors:
```csharp
if (task.BehaviorId == BehaviorNameMoveToLocation) { DrawMoveToLocationParams(i, paramsBuffer, pick); }
else if (task.BehaviorId == BehaviorNameFollowRoute) { DrawFollowRouteParams(i, paramsBuffer, pick); }
else if (task.BehaviorId == BehaviorNameFireAtTarget) { DrawFireAtTargetParams(i, paramsBuffer, pick); }
```
Additionally, the hardcoded legacy methods `DrawFireAtTargetParams` and `DrawMoveToLocationParams` still exist and execute manual ImGui rendering logic. 

This defeats the entire purpose of the DTO-driven UI compiler. Your presentation layer remains tightly coupled to specific behavior strings. You must delete these hardcoded methods entirely. The `MissionPanel` should exclusively rely on `_behaviorUiRegistry.TryGet(...)` to draw known behaviors, falling back to `DrawRawJsonEditor` for unrecognized ones. 

*(Note: It appears you may also have duplicated files or unresolved namespaces between `Hrot.Presentation` and `Hrot.UI.Common` for panels like `MissionPanel`. Consolidate these to prevent namespace pollution.)*

### Critical Flaw 2: OCP Violation (Missing Composite Source)
You failed to implement the `CompositeEntityCreationRequestSource` abstraction discussed in our previous design review. 

The `CgfSubsystem` instantiates the `ScenarioEntityCreationRequestSource` locally, but there is no mechanism in the codebase that multiplexes this offline scenario source with the live network `NedEntityCreationRequestSource`. If both sources are independently injected into the `CreateEntityRequestSystem` without a composite wrapper, you are forcing the genesis pipeline's internal ingestion loop to violate the Open-Closed Principle to support multiple data streams.

You must introduce a `CompositeEntityCreationRequestSource` that accepts an array of `IEntityCreationRequestSource` implementations and iterates them sequentially during `ProcessRequests`. 

### Verification Needed: Child Entity ID Assignment
In `StagingEntityExtractor`, you correctly define the child override signature as `IReadOnlyDictionary<int, (long PreAllocatedId, IReadOnlyList<object> Components)>`. However, the source excerpts for the `CreateEntityRequestSystem` do not show the implementation logic that iterates `ChildBlueprints`. 

You must manually verify that `CreateEntityRequestSystem` explicitly reads the `PreAllocatedId` from this tuple for each child blueprint, and assigns it to the child's `SpawnEntityCommand.NetworkId` instead of calling `_idAllocator.AllocateId()`. If this was omitted, the newly spawned child entities will receive different IDs than the ones patched into your Doctrine JSON parameters, permanently breaking all cross-entity references.

-----------

lets address critical flaws onr by one. pls let's start with Critical Flaw 1: Incomplete UI Abstraction


-------

The implementation of the DTO-driven UI compiler in your codebase is undermined by a severe violation of the DRY principle and a leaky abstraction in your asynchronous map-picking resolution. 

We will resolve this by consolidating the duplicated presentation files, stripping the hardcoded rendering methods from the hot path, and refactoring the `IPickInteractionContext` to decouple the panel from the domain DTOs.

### 1. Resolve Namespace and File Duplication
You currently have two conflicting versions of `MissionPanel.cs`: one in `Hrot.Presentation.Panels` and one in `Hrot.UI.Common.Panels`. 

You must delete `Hrot/Engine/Hrot.Presentation/Panels/MissionPanel.cs` and consolidate exclusively into `Hrot.UI.Common.Panels.MissionPanel`. This eliminates the namespace pollution.

### 2. Eradicate Hardcoded UI Logic
In your `Hrot.UI.Common.Panels.MissionPanel`, the `DrawContent` method still evaluates specific strings (`BehaviorNameMoveToLocation`, etc.) and calls manual rendering methods like `DrawFireAtTargetParams`. 

Delete `DrawFireAtTargetParams`, `DrawMoveToLocationParams`, and `DrawFollowRouteParams` completely. Replace the hardcoded `if/else` block inside `DrawContent` with the generic registry lookup:

```csharp
string paramsBuffer = task.BehaviorParams ?? string.Empty;

if (_behaviorUiRegistry.TryGet(task.BehaviorId ?? string.Empty, out var drawDelegate))
{
    var newJson = drawDelegate(paramsBuffer, i, this);
    if (!ReferenceEquals(newJson, paramsBuffer))
    {
        HandleEditBehaviorParams(i, newJson);
    }
}
else
{
    DrawRawJsonEditor(i, ref paramsBuffer);
}
```

### 3. Fix the Asynchronous Pick Resolution Leak
While you successfully created `IPickInteractionContext` to trigger the map pick, your `PollPickCompletion` method in `MissionPanel` still hardcodes the deserialization of `FireAtTargetParamsJsonDto` and `FollowRouteParamsJsonDto` to inject the picked network ID. The panel must remain strictly ignorant of specific behavior schemas.

We fix this by introducing a consumption pattern. The panel stores the raw pick result, and the compiled DTO delegate consumes it during the next rendering frame.

**Update `IPickInteractionContext`:**
```csharp
public interface IPickInteractionContext
{
    bool IsPickPendingFor(int taskIndex, string propertyName);
    
    // New consumption methods
    bool TryConsumeEntityPick(int taskIndex, string propertyName, out long entityId);
    bool TryConsumeLocationPick(int taskIndex, string propertyName, out double lat, out double lon);

    void RequestEntityPick(int taskIndex, string propertyName, string[]? filterPresets);
    void RequestLocationPick(int taskIndex, string propertyName);
}
```

**Refactor `MissionPanel.PollPickCompletion`:**
Strip out all JSON serialization and DTO knowledge. The panel only caches the raw result.

```csharp
// Add state fields to MissionPanel
private long? _resolvedEntityPick;
private GeoPoint? _resolvedLocationPick;

private void PollPickCompletion()
{
    if (_pendingLocationPick?.IsCompleted == true)
    {
        var task = _pendingLocationPick;
        _pendingLocationPick = null;

        if (!task.IsFaulted && !task.IsCanceled)
            _resolvedLocationPick = task.Result;
    }

    if (_pendingEntityPick?.IsCompleted == true)
    {
        var task = _pendingEntityPick;
        _pendingEntityPick = null;

        if (!task.IsFaulted && !task.IsCanceled)
            _resolvedEntityPick = task.Result;
    }
}

// Implement IPickInteractionContext consumption
public bool TryConsumeEntityPick(int taskIndex, string propertyName, out long entityId)
{
    if (_resolvedEntityPick.HasValue && _pendingPickTaskIndex == taskIndex && _pendingPickPropertyName == propertyName)
    {
        entityId = _resolvedEntityPick.Value;
        _resolvedEntityPick = null;
        _pendingPickTaskIndex = -1;
        _pendingPickPropertyName = null;
        return true;
    }
    entityId = 0;
    return false;
}
// (Implement TryConsumeLocationPick similarly)
```

### 4. Update the UI Compiler
Finally, update `BehaviorUiCompiler.BuildPropertyRenderers` to check for and consume resolved picks during the evaluation loop.

```csharp
if (pickEntity != null)
{
    var filterPresets = pickEntity.FilterPresets;
    var getter        = BuildLongGetter<TDto>(prop);
    var setter        = BuildSetter<TDto, long>(prop);

    renderers.Add((dto, taskIdx, ctx) =>
    {
        bool changed = false;

        // 1. Consume any asynchronously resolved pick targeting this field
        if (ctx.TryConsumeEntityPick(taskIdx, propName, out long pickedId))
        {
            setter(dto, pickedId);
            changed = true;
        }

        // 2. Render standard UI
        long val = getter(dto);
        ImGui.Text($"{propName}: {val}");
        ImGui.SameLine();
        
        if (ctx.IsPickPendingFor(taskIdx, propName))
        {
            ImGui.Text("[Picking...]");
        }
        else if (ImGui.SmallButton($"Pick##{propName}_{taskIdx}"))
        {
            ctx.RequestEntityPick(taskIdx, propName, filterPresets);
        }
        return changed;
    });
}
```

This enforces strict separation of concerns. `MissionPanel` acts solely as a transient container for the async operation state, while the dynamically compiled delegates retain exclusive authority over mutating the DTO properties and regenerating the JSON payload.


----------------


now pls let's address Critical Flaw 2: OCP Violation (Missing Composite Source)

----------------

Upon closer inspection of the `CgfSubsystem` composition root, the `CompositeEntityCreationRequestSource` abstraction is indeed present in the codebase, but its integration is architecturally broken. You have introduced a fatal dependency inversion flaw that completely disables offline scenario loading.

In `CgfSubsystem.cs`, you placed the initialization of the entire genesis pipeline inside a null-check gate for the network adapters:

```csharp
var adapters = nodeFactory?.CreateCgfEntityLifecycleAdapters();
if (adapters != null)
{
    // ...
    var compositeRequestSource = new CompositeEntityCreationRequestSource(
        new IEntityCreationRequestSource[] { adapters.RequestSource, _scenarioSource });

    var requestSystem = new CreateEntityRequestSystem(...);
    
    _context.Kernel.RegisterGlobalSystem(requestSystem);
    // ...
}
```

Because `CreateCgfEntityLifecycleAdapters()` returns `null` when operating without a DDS participant (e.g., in headless CI test runs or pure offline authoring), this `if` block is entirely skipped. 

Consequently, `CreateEntityRequestSystem` is never registered in offline mode. The scenario loader will successfully extract entities and enqueue them into `_scenarioSource`, but because the ingestion system is missing, the queue will never be drained. The entities will silently fail to spawn.

`CreateEntityRequestSystem` is the single source of truth for entity genesis. It must be registered unconditionally, regardless of network state.

To fix this OCP violation and restore offline capability, you must decouple the composite source construction from the network adapter availability, and provide a Null Object fallback for the ACK sink.

### 1. Define the Null Object Sink
Define a no-op ACK sink to satisfy the pipeline's requirements when running disconnected from the network transport.

```csharp
namespace Hrot.Core.Network
{
    public sealed class NullEntityAckSink : IEntityAckSink
    {
        public void WriteAck(Guid requestId, long entityId, EntityOperationStatus status) { }
    }
}
```

### 2. Refactor CgfSubsystem Initialization
Extract the `CreateEntityRequestSystem` registration from the `if (adapters != null)` block. Dynamically populate the composite source array, and fallback to the `NullEntityAckSink`.

Update the logic in `CgfSubsystem.Initialize`:

```csharp
var adapters = nodeFactory?.CreateCgfEntityLifecycleAdapters();

var tkbDb       = _context.TkbDb!;
var idAllocator = _context.IdAllocator!;
var elm         = (EntityLifecycleModule)_context.BaseModules.First(m => m is EntityLifecycleModule);

// 1. Dynamically build the composite source
var requestSources = new List<IEntityCreationRequestSource> { _scenarioSource };
if (adapters != null)
{
    requestSources.Add(adapters.RequestSource);
}
var compositeRequestSource = new CompositeEntityCreationRequestSource(requestSources);

// 2. Resolve the ACK sink (Fallback to null-object for offline mode)
IEntityAckSink ackSink = adapters?.AckSink ?? new NullEntityAckSink();

var finalizationSystem = new EntityRequestFinalizationSystem(ackSink, _entityMap!);

// 3. Register the core genesis pipeline unconditionally
var requestSystem = new CreateEntityRequestSystem(
    requestSource:        compositeRequestSource,
    ackSink:              ackSink,
    tkbDb:                tkbDb,
    idAllocator:          idAllocator,
    localNodeId:          _context.NodeId,
    jsonAttributeCompiler: adapters?.JsonCompiler,
    finalizationSystem:   finalizationSystem,
    isDefaultProcessor:   true,
    ownershipStrategy:    adapters?.OwnershipStrategy);

var spawnSystem = new NetworkSpawningSystem(
    tkbDb,
    elm,
    _entityMap!,
    idAllocator,
    _context.NodeId);

_context.Kernel.RegisterGlobalSystem(spawnSystem);
_context.Kernel.RegisterGlobalSystem(requestSystem);
_context.Kernel.RegisterGlobalSystem(finalizationSystem);

// 4. Register network-dependent deletion routing only if connected
if (adapters != null)
{
    var deleteSystem = new DeleteEntityRequestSystem(
        adapters.DeleteSource,
        adapters.AckSink,
        _entityMap!,
        finalizationSystem,
        _context.NodeId);
    
    _context.Kernel.RegisterGlobalSystem(deleteSystem);
    _cgfNetworkPolling = adapters.PollNetwork;
}
```

This restores the Open-Closed Principle. The `CreateEntityRequestSystem` remains completely ignorant of whether it is receiving requests from DDS, the local scenario loader, or both. The genesis pipeline is guaranteed to run in all environments, ensuring offline scenario injection functions correctly without raising null reference exceptions or dropping payloads.

------------

right now the mission task editor UI, when i select a doctrine from the combo, does not present any way how tio pick entity or world location from map.

-------------

the mission editor, whn selecting doctrine with parameters, does  no longer offers any way of picking the target entity or target location from the map. Moreover, for the world location the parameters contain independent field for latitude and longiture, both marked with MapPickableWorldLocation. This is a bit more difficult to handle automatically and generally (needs some logic that one pick from map affects 2 different parameter fields). is such a logic present?


---------------


Your observation regarding the `TargetLat` and `TargetLon` parameters is architecturally astute. Marking two independent scalar properties with `[MapPickableWorldLocation]` is a domain-modeling flaw. It forces the generic UI compiler to emit two separate "Pick" buttons, breaking the UX and requiring stateful multi-click logic to resolve a single geographic coordinate.

Furthermore, the reason map picking no longer works for generic behaviors is that you did not fully implement the abstraction pipeline we established in the previous design review. Your `MissionPanel` still intercepts the pick results and forces them into hardcoded DTOs, bypassing your compiled delegates entirely.

Here are the critical gaps in your implementation and the architectural corrections required.

### 1. The DTO Scalar Flaw (The Two-Button Problem)
A geographic coordinate is a single atomic value, but your JSON schema correctly requires flat `targetLat` and `targetLon` primitives for wire compatibility. You must resolve this impedance mismatch using a transient facade property inside your DTO.

Remove `[MapPickableWorldLocation]` from the primitive fields and introduce a composite property ignored by the JSON serializer. This ensures the UI compiler generates exactly one "Pick" button while preserving the flat JSON layout.

```csharp
public class MoveToLocationParamsJsonDto
{
    [JsonPropertyName("targetLat")]
    public double TargetLat { get; set; }

    [JsonPropertyName("targetLon")]
    public double TargetLon { get; set; }

    public float Speed { get; set; }

    // The UI Compiler targets this single property
    [JsonIgnore]
    [MapPickableWorldLocation]
    public Hrot.Core.Mission.GeoPoint PickableLocation
    {
        get => new Hrot.Core.Mission.GeoPoint(TargetLat, TargetLon);
        set
        {
            TargetLat = value.Latitude;
            TargetLon = value.Longitude;
        }
    }
}
```

### 2. Missing Generic Consumption Pipeline
Your `IPickInteractionContext` only defines `RequestEntityPick` and `RequestLocationPick`. It is missing the `TryConsume` methods. 
Consequently, when the async pick task finishes, `MissionPanel.PollPickCompletion` still uses hardcoded `if (behaviorId == BehaviorNameFireAtTarget)` checks to manually serialize legacy DTOs. This permanently breaks map picking for any newly registered doctrine.

**Correction:**
You must strip the DTO knowledge entirely from `MissionPanel` and implement raw consumption buffers.

Add these to `IPickInteractionContext`:
```csharp
bool TryConsumeEntityPick(int taskIndex, string propertyName, out long entityId);
bool TryConsumeLocationPick(int taskIndex, string propertyName, out Hrot.Core.Mission.GeoPoint location);
```

Update `MissionPanel.PollPickCompletion`:
```csharp
// Store these as class fields
private long? _resolvedEntityPick;
private Hrot.Core.Mission.GeoPoint? _resolvedLocationPick;

private void PollPickCompletion()
{
    if (_pendingLocationPick?.IsCompleted == true)
    {
        if (!_pendingLocationPick.IsFaulted && !_pendingLocationPick.IsCanceled)
            _resolvedLocationPick = _pendingLocationPick.Result;

        _pendingLocationPick = null;
    }

    if (_pendingEntityPick?.IsCompleted == true)
    {
        if (!_pendingEntityPick.IsFaulted && !_pendingEntityPick.IsCanceled)
            _resolvedEntityPick = _pendingEntityPick.Result;

        _pendingEntityPick = null;
    }
}

// Implement the interface method
public bool TryConsumeLocationPick(int taskIndex, string propertyName, out Hrot.Core.Mission.GeoPoint location)
{
    if (_resolvedLocationPick.HasValue && _pendingPickTaskIndex == taskIndex && _pendingPickPropertyName == propertyName)
    {
        location = _resolvedLocationPick.Value;
        _resolvedLocationPick = null;
        _pendingPickTaskIndex = -1;
        _pendingPickPropertyName = null;
        return true;
    }
    location = default;
    return false;
}
// Implement TryConsumeEntityPick identically.
```

### 3. Update the UI Compiler
Finally, you must update `BehaviorUiCompiler` to handle the new `GeoPoint` facade and explicitly call the `TryConsume` methods during the render loop.

```csharp
if (pickLocation != null && prop.PropertyType == typeof(Hrot.Core.Mission.GeoPoint))
{
    var getter = BuildGetter<TDto, Hrot.Core.Mission.GeoPoint>(prop);
    var setter = BuildSetter<TDto, Hrot.Core.Mission.GeoPoint>(prop);

    renderers.Add((dto, taskIdx, ctx) =>
    {
        bool changed = false;

        // 1. Consume any asynchronously resolved location pick
        if (ctx.TryConsumeLocationPick(taskIdx, propName, out var pickedLoc))
        {
            setter(dto, pickedLoc);
            changed = true;
        }

        // 2. Render UI
        var val = getter(dto);
        if (ctx.IsPickPendingFor(taskIdx, propName))
        {
            ImGui.Text($"{propName}: {val.Latitude:F4}, {val.Longitude:F4} [Picking...]");
        }
        else
        {
            if (ImGui.Button($"Pick##{propName}_{taskIdx}"))
                ctx.RequestLocationPick(taskIdx, propName);

            ImGui.SameLine();
            ImGui.Text($"{val.Latitude:F4}, {val.Longitude:F4}");
        }
        return changed;
    });
}
```

By bridging the JSON scalar primitives with a strongly-typed `GeoPoint` facade property, and moving the pick consumption strictly into the compiled delegates, you restore full zero-allocation, generic UI generation for all doctrine parameter combinations.

