# BATCH-05 — Stage 4 Backend: Search Domain, Compilation Layer, Service, Gizmo

## Context

This batch implements the **entire Stage 4 backend** of the Replay Browser:
- RB-4.1 — Search Domain DTOs
- RB-4.2 — `IPropertyEvaluator`
- RB-4.3 — `IPredicateCompiler` + `PredicateCompiler`
- RB-4.4 — `IEventScannerCompiler` + scanner types
- RB-4.5 — `IRecordingSearchService` + `RecordingSearchService`
- RB-4.6 — `BoundingBoxPickerGizmo`
- RB-4.7 — Stage 4 Backend Acceptance Gate (SR-T01..SR-T38)

For all task specifications, see [TASK-DETAILS.md §RB-4.1–RB-4.7](../TASK-DETAILS.md#rb-41--search-domain-dtos).
For architecture and algorithms, see [DESIGN.md §6](../DESIGN.md#6-stage-4--advanced-recording-search-engine).

**DO NOT implement any UI tasks** (RB-4.8..RB-4.11). This batch ends at RB-4.7.

---

## Repository layout reminder

| Path | Role |
|---|---|
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/` | All new production code for this batch |
| `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/` | All new test code for this batch |
| `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Support/FdpRecordingHarness.cs` | Existing test harness — extend as needed |

**FDP is a git submodule** at `d:\Work\IOS-IG-SimHost-FDP-2\FDP`. All new files go inside it.

---

## Required codebase exploration (do this first)

Before writing any production code, read:

1. `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Support/FdpRecordingHarness.cs` — understand all existing helper components (`HarnessPosition`, `HarnessVelocity`, `HarnessTransform`), event API (`FireUnmanagedEvent<T>`, `FireManagedEvent<T>`), and the tick/record cycle.

2. `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ReplayBrowserContext.cs` — understand `SandboxRepo`, `SandboxBus`, `Playback`, `SeekToFrame`, `StepForward`. **Critical**: `SeekToFrame` clears the bus — the search service must NOT use it for per-frame stepping.

3. `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/FdpLocationPickerGizmo.cs` — use as the pattern for `BoundingBoxPickerGizmo`. Look at `OnMouseEvent`, `OnDragUpdate`, `OnKeyEvent`, `Dispose`, `RequiresExclusiveFocus`, `WantsRawInput`.

4. `FDP/ExtDeps/StructEdit/src/StructEdit.Core/IComponentEditService.cs` — understand `Open(object instance, Type type, EditScope scope)`.

5. `FDP/ExtDeps/StructEdit/tests/StructEdit.Tests/Session/SessionTests.cs` — see how `EditScope.ForField` is used and how `IValueBinding.GetBoxed()` is called.

6. `FDP/ExtDeps/StructEdit/src/StructEdit.Reflection/ComponentEditServiceBuilder.cs` — understand how to construct an `IComponentEditService` for tests (using `ComponentEditServiceBuilder`).

7. `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/ComponentDiffService.cs` (and related) — understand how `EntityRepository` is used for frame-stepping and `QueryDelta` usage.

8. `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Export/RecordingExportServiceTests.cs` — read the existing export tests to understand the test pattern with `FdpRecordingHarness`.

9. `FDP/Toolkits/Fdp.Toolkits.csproj` — confirm what packages are referenced.

10. `FDP/Engine/Fdp.Core/EntityRepository*.cs` (or nearby) — find `QueryDelta`, `GetDestructionLog`, `ClearDestructionLog`, `HasComponent`, and how `EntityHeader` with `ComponentMask` / `AuthorityMask` / `LastChangeTick` works.

---

## Task 1: RB-4.1 — Search Domain DTOs

**Path**: `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/` (create directory and files)

**Spec**: DESIGN.md §6.1 and TASK-DETAILS.md §RB-4.1.

Create the following files under `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/`:

**`SearchPredicateDto.cs`** — all DTO types in one file or split logically:

```csharp
// Exact code from DESIGN.md §6.1:
public abstract class SearchPredicateDto { }

public enum LogicalOperator { And, Or }
public sealed class CompoundPredicateDto : SearchPredicateDto {
    public LogicalOperator Operator { get; set; } = LogicalOperator.And;
    public List<SearchPredicateDto> Conditions { get; set; } = new();
}

public enum SearchOperator { Equals, Contains, GreaterThan, LessThan, Changed, StartsWith }
public sealed class PropertyMatchDto : SearchPredicateDto {
    public Type ComponentType { get; set; } = null!;
    public string PropertyPath { get; set; } = string.Empty;
    public SearchOperator Operator { get; set; } = SearchOperator.Equals;
    public SearchPredicateDto Predicate { get; set; } = null!;
}

public abstract class SearchPredicateValueDto : SearchPredicateDto { }
public sealed class NumericPredicateDto : SearchPredicateValueDto {
    public double MinValue = double.MinValue;
    public double MaxValue = double.MaxValue;
}
public sealed class StringPredicateDto : SearchPredicateValueDto {
    public string Substring = "";
    public bool StartsWith;
    public bool ExactMatch;
}
public sealed class EnumPredicateDto<TEnum> : SearchPredicateValueDto where TEnum : struct, Enum {
    public List<TEnum> AllowedValues { get; set; } = new();
}

public sealed class TransientEventPredicateDto : SearchPredicateDto {
    public Type EventType { get; set; } = null!;
    public bool AnyOccurrence { get; set; } = true;
    public string PropertyPath { get; set; } = string.Empty;
    public SearchOperator Operator { get; set; } = SearchOperator.Equals;
    public string TargetValue { get; set; } = string.Empty;
}

public enum EntityIdentifierType { EcsHandle, NetworkId, NameSubstring }
public sealed class LifecyclePredicateDto : SearchPredicateDto {
    public EntityIdentifierType IdentifierType { get; set; } = EntityIdentifierType.NameSubstring;
    public string TargetValue { get; set; } = string.Empty;
}

public enum BoundaryEvent { Entry, Exit, EntryOrExit }
public sealed class SpatialBoundingPredicateDto : SearchPredicateDto {
    [MapPickableBoundingBox] public BoundingBox2D Bounds { get; set; }
    public BoundaryEvent TriggerEvent { get; set; } = BoundaryEvent.EntryOrExit;
}

public enum StructuralModification { Added, Removed, AnyChange }

/// <summary>
/// Distinguishes locally-owned components from ghost replicas in a distributed ECS.
/// In a multi-host deployment an entity can carry the same component bit in its
/// ComponentMask on every host but only one host holds AuthorityMask for it; the
/// others are read-only ghosts. Diagnostic searches must be able to scope to one
/// or the other to avoid investigating phantom state changes on replicas.
/// </summary>
public enum AuthorityRequirement { Any, RequireAuthority, RequireGhost }

public sealed class StructuralPredicateDto : SearchPredicateDto {
    public Type ComponentType { get; set; } = null!;
    public StructuralModification ModificationType { get; set; } = StructuralModification.Added;
    public AuthorityRequirement AuthorityRequirement { get; set; } = AuthorityRequirement.Any;
}

public sealed record SearchResultDto(int FrameIndex, long WallClockTicks, Entity Entity, string ContextMessage);
public sealed record LifecycleSearchResultDto(Entity Entity, int StartFrame, int EndFrame, string MatchContext);

public struct BoundingBox2D { public System.Numerics.Vector2 Min, Max; }

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class MapPickableBoundingBoxAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class BehaviorHashPickerAttribute : Attribute { }
```

**JSON round-trip concern**: `SearchPredicateDto` has polymorphic subclasses. For JSON serialization tests, use `JsonSerializerOptions` with a `JsonDerivedType` attribute chain OR a custom `JsonConverter`. Look at how existing DTOs in the codebase handle polymorphic JSON. A simple approach: annotate `SearchPredicateDto` with `[JsonPolymorphic]` + `[JsonDerivedType(typeof(CompoundPredicateDto), "Compound")]` etc. if the .NET version supports it (check the target framework in `Fdp.Toolkits.csproj`). If not, implement a `SearchPredicateDtoConverter : JsonConverter<SearchPredicateDto>`.

**Test**: `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/SearchPredicateDtoSerializationTests.cs`

The test must do a round-trip for each concrete type, verifying every field is preserved. Include `StructuralPredicateDto` with `AuthorityRequirement.RequireGhost` and `AuthorityRequirement.RequireAuthority`. Include a nested `CompoundPredicateDto` with 3 levels of nesting. Assert no `Fdp.Presentation` assembly reference from the Search namespace (use `typeof(CompoundPredicateDto).Assembly.GetReferencedAssemblies()` and check none is named `Fdp.Presentation`).

---

## Task 2: RB-4.2 — `IPropertyEvaluator`

**Path**: `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/IPropertyEvaluator.cs` and `PropertyEvaluator.cs`

**Spec**: DESIGN.md §6.2 and TASK-DETAILS.md §RB-4.2.

```csharp
public interface IPropertyEvaluator
{
    string GetValueAsString(object component);
}
```

Implementation `PropertyEvaluator`:
- Constructor takes `IComponentEditService editService, Type componentType, string propertyPath`.
- In the constructor: call `editService.Open(dummyInstance, componentType, EditScope.ForField($"$.{propertyPath}"))` where `dummyInstance` is a default-constructed component object (`Activator.CreateInstance(componentType)`). Cache the returned `IEditSession`'s `IValueBinding` and `IEditBuffer`.
- If `propertyPath` is invalid (Open throws), let the `ArgumentException` propagate at construction time.
- `GetValueAsString(object component)`: call `_buffer.ReplaceInstance(component); return _binding.GetBoxed()?.ToString() ?? "null";`

**How to obtain IValueBinding/IEditBuffer from IEditSession**: explore `IComponentEditService.Open(...)` return type. It likely returns an `IEditSession`. The session has a `Document` with a `Root` node. For a single-field scope, the root should directly be a node whose binding is the field binding. Explore `SessionTests.cs` for the exact API.

**Tests**: `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/PropertyEvaluatorTests.cs`

- Test: given a `HarnessPosition` struct with `X=42.5f`, a `PropertyEvaluator` for field `"X"` returns `"42.5"` (or similar numeric string).
- Test: invalid path `"NonExistent"` throws `ArgumentException` at construction.
- Allocation test: 10k calls to `GetValueAsString` allocate < 1 KB total (`GC.GetAllocatedBytesForCurrentThread()` snapshot before/after 10k calls with warmup).

Note: the allocation test depends on whether `_binding.GetBoxed()` boxes. If it allocates, the constraint may be relaxed to "< 1 MB". Check what the design says — it says "no reflection", meaning the binding is precompiled. Write the test and see what passes; the test must capture actual allocation behavior.

---

## Task 3: RB-4.3 — `IPredicateCompiler` + `PredicateCompiler`

**Path**: `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/IPredicateCompiler.cs` and `PredicateCompiler.cs`

**Spec**: DESIGN.md §6.2 and TASK-DETAILS.md §RB-4.3.

```csharp
public interface IPredicateCompiler
{
    Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto root);

    // Returns the set of component types that MUST be present (AND-only roots, for QueryBuilder optimization).
    IReadOnlyList<Type> ExtractMandatoryComponents(SearchPredicateDto root);
}
```

**`CompileComponentPredicate` algorithm** (recursive, builds closures):

```
CompileComponentPredicate(dto):
  switch dto:
    CompoundPredicateDto:
      childFuncs = dto.Conditions.Select(c => Compile(c)).ToList()  // compile all upfront
      if AND: return (repo, e) => childFuncs.All(f => f(repo, e))   // short-circuit
      if OR:  return (repo, e) => childFuncs.Any(f => f(repo, e))   // short-circuit
    
    PropertyMatchDto:
      evaluator = new PropertyEvaluator(_editService, dto.ComponentType, dto.PropertyPath)
      operatorFn = CompileOperator(dto.Operator, dto.Predicate)
      return (repo, e) =>
        repo.HasComponent(e, dto.ComponentType) &&   // guard
        operatorFn(evaluator.GetValueAsString(repo.GetComponentAsObject(e, dto.ComponentType)))
    
    NumericPredicateDto | StringPredicateDto | EnumPredicateDto<T>:
      // These are value predicates, used as Predicate field on PropertyMatchDto.
      // If called directly at root, return constant true (or throw - they're not standalone).
    
    StructuralPredicateDto | SpatialBoundingPredicateDto | LifecyclePredicateDto | TransientEventPredicateDto:
      // These are handled by specialized loops in the service, not by this compiler.
      // Return (repo, e) => true as a pass-through (the service handles the state machine).
```

**Operator dispatch** (per DESIGN.md §6.1 SearchOperator enum):
- `Equals`: `value == target`
- `Contains`: `value.Contains(target, OrdinalIgnoreCase)`
- `StartsWith`: `value.StartsWith(target, OrdinalIgnoreCase)`
- `GreaterThan`: `double.TryParse(value) && double.TryParse(target) && parsed > targetParsed`
- `LessThan`: same with `<`
- `Changed`: always `true` — the predicate fires whenever the component mutates (handled by QueryDelta in the service); the operator just means "any change detected"

For `NumericPredicateDto` as the `Predicate` field of `PropertyMatchDto`:
- `value >= MinValue && value <= MaxValue` (after parsing the string to double)

For `StringPredicateDto`:
- `ExactMatch`: `value == Substring`; `StartsWith`: `value.StartsWith(Substring)`; else `value.Contains(Substring)`

**`ExtractMandatoryComponents`** — per DESIGN.md §6.2 and TASK-DETAILS.md §RB-4.3:
- Walk the tree; for a `CompoundPredicateDto` with `Operator == And`, collect `ComponentType` from each direct `PropertyMatchDto` child.
- For `Or` compounds: do NOT add any types (union = no guarantee).
- Recurse into nested `And` compounds.
- Return the distinct list.

**`repo.GetComponentAsObject(entity, type)`** — you need a way to get the raw component as `object` for the evaluator. Explore the EntityRepository API for `GetComponent<T>`, `GetComponentAsObject`, or similar. If it doesn't exist, use reflection: `typeof(EntityRepository).GetMethod("GetComponent").MakeGenericMethod(type).Invoke(repo, [entity])`. Cache the `MethodInfo` per type to avoid per-call reflection.

**Tests**: `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/PredicateCompilerTests.cs`

Tests SR-T05..SR-T08, SR-T35. Use `FdpRecordingHarness` components for concrete types. Each test:
- Builds a DTO tree.
- Compiles to a predicate.
- Asserts the predicate evaluates correctly against an `EntityRepository` with known state.

For SR-T08 (allocation gate): compile once, then invoke the compiled predicate 10k times. Allocation should be < 1 KB. (No allocations inside the closure on invocation — only at compile time.)

For SR-T35 (short-circuit AND): put a spy component evaluator first that returns `false`, then a second spy that records calls. Second spy must not be called.

---

## Task 4: RB-4.4 — `IEventScannerCompiler` + scanners

**Path**: `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/IEventScannerCompiler.cs`, `EventScannerCompiler.cs`, and internal scanner classes.

**Spec**: DESIGN.md §6.2 and TASK-DETAILS.md §RB-4.4.

```csharp
internal delegate void EventScannerDelegate(FdpEventBus bus, int frame, long ticks, List<SearchResultDto> results);

public interface IEventScannerCompiler
{
    EventScannerDelegate CompileScanner(TransientEventPredicateDto predicate);
}
```

**`CompileScanner` branching logic**:

```
if AnyOccurrence == true || string.IsNullOrEmpty(PropertyPath):
    // Pure occurrence scanner
    return (bus, frame, ticks, results) =>
        if bus.HasEvent(EventType):
            results.Add(new SearchResultDto(frame, ticks, Entity.Null, $"{EventType.Name} Occurred"))

else if EventType.IsValueType:
    // FastEventScanner<T> (unmanaged)
    return (bus, frame, ticks, results) =>
        for each T evt in bus.Read<T>():   // or equivalent
            val = propertyEvaluator.GetValueAsString(evt)
            if operatorMatches(val, TargetValue):
                entity = TryExtractEntity(evt)
                results.Add(new SearchResultDto(frame, ticks, entity, $"{PropertyPath}: {val}"))

else:
    // ManagedEventScanner<T>
    return similar but using bus.ReadManaged<T>()
```

**PropertyEvaluator for events**: Create a `PropertyEvaluator` from the event struct/class type and `PropertyPath`. The event instance is the struct/class value from `bus.Read<T>()`.

**`TryExtractEntity`**: If the property path result can be parsed as an entity via `ImGuiEntityLink.TryParse(val, out entity)`, return that entity; else `Entity.Null`.

**`bus.Read<T>()` API**: Explore `FdpEventBus` for the read API. Look at how existing code reads events (search for `bus.Read<`, `ReadManaged`, `HasEvent`).

**Tests**: `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/EventScannerCompilerTests.cs`

Tests SR-T23..SR-T27. Each test uses `FdpRecordingHarness` to produce a recording with specific events on specific frames, then runs the scanner directly (not through the service yet) by replaying with a `PlaybackController`. The scanner test calls `playback.StepForward(repo)` then `scanner.Invoke(bus, frame, ticks, results)` without calling `ClearCurrentBuffers` between them.

For SR-T23 (pure occurrence, unmanaged): define a test event struct (can be a simple `struct HarnessFireEvent { public int WeaponIndex; }`), fire it on ticks 3 and 7, compile a pure-occurrence scanner, replay, assert exactly 2 results.

For SR-T27 (entity deep-link): the event payload must have a field that is formatted as `"[index, vN]"` — you'll need a test event that has an `Entity`-typed field, OR use `ImGuiEntityLink.TryParse` on a string field containing such a format.

---

## Task 5: RB-4.5 — `IRecordingSearchService` + `RecordingSearchService`

**Path**: `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/IRecordingSearchService.cs` and `RecordingSearchService.cs`

**Spec**: DESIGN.md §6.3, §6.4 and TASK-DETAILS.md §RB-4.5.

```csharp
public interface IRecordingSearchService
{
    IReadOnlyList<SearchResultDto> ExecuteSearch(string fdpPath, SearchPredicateDto root);
    IReadOnlyList<LifecycleSearchResultDto> ExecuteLifecycleSearch(string fdpPath, LifecyclePredicateDto criteria);
}
```

### Implementation structure

`RecordingSearchService` constructor takes `IPredicateCompiler predicateCompiler, IEventScannerCompiler eventScannerCompiler`.

**Dispatch**: `ExecuteSearch(fdpPath, root)` dispatches based on root type:
- `TransientEventPredicateDto` → `RunEventScan(fdpPath, (TransientEventPredicateDto)root)`
- `LifecyclePredicateDto` → call `ExecuteLifecycleSearch` (reuse)
- All others (component, structural, spatial, compound) → `RunFrameStepScan(fdpPath, root)`

**Per-invocation isolation**: each call creates its OWN `EntityRepository`, `FdpEventBus`, and `PlaybackController`. Never uses the GUI's `ReplayBrowserContext`. Dispose everything at end (in a `try/finally`).

### `RunFrameStepScan` — component/structural/spatial/compound

```
Allocate results = new List<SearchResultDto>(64)
Compile predicate: compiledFn = predicateCompiler.CompileComponentPredicate(root)
Allocate state machines ONCE before loop:
  HashSet<Entity> insideZone = new()   (for spatial only)
  HashSet<Entity> hasComponent = new() (for structural only)

Open recording: PlaybackController playback = new(fdpPath)
EntityRepository repo = new()
Load the recording (register required component types)

uint lastScannedVersion = 0

while (playback.StepForward(repo)):  // do NOT call ClearCurrentBuffers here
    frame = playback.CurrentFrame
    ticks = playback.GetFrameMetadata(frame).WallClockTicks
    
    // Component property mode: use QueryDelta
    if root is PropertyMatchDto || CompoundPredicateDto:
        for each entity in QueryDelta(componentType, lastScannedVersion):
            if compiledFn(repo, entity):
                results.Add(new SearchResultDto(frame, ticks, entity, BuildContextMessage(root, repo, entity)))
    
    // Spatial mode
    if root is SpatialBoundingPredicateDto spatial:
        RunSpatialFrame(repo, frame, ticks, spatial, insideZone, results)
    
    // Structural mode
    if root is StructuralPredicateDto structural:
        RunStructuralFrame(repo, frame, ticks, structural, hasComponent, results)
    
    // Cleanup per frame
    foreach destroyed entity from repo.GetDestructionLog():
        insideZone.Remove(destroyed)
        hasComponent.Remove(destroyed)
    repo.ClearDestructionLog()
    
    lastScannedVersion = repo.GlobalVersion

return results
```

**CRITICAL: zero-allocation loop body on no-match frames** (SR-T34):
- `results` and state sets are pre-allocated BEFORE the loop.
- The loop body must not call `string.Format`, `$"..."`, `.ToString()` on value types, LINQ materializations, or anything that allocates on the managed heap when there is no match.
- On-match: `new SearchResultDto(...)` is acceptable (result allocation).
- Context messages on match: pre-build string using `string.Concat` or literal strings, NOT interpolated strings inside the loop on no-match paths.
- Verify with SR-T34: `GC.GetAllocatedBytesForCurrentThread()` before/after 10k-frame scan (compiled predicate, no matches) must be 0.

### `RunEventScan` — transient events

**Per DESIGN.md §6.4 strict contract**:

```
EventScannerDelegate scanner = eventScannerCompiler.CompileScanner(predicate)
List<SearchResultDto> results = new List<SearchResultDto>(64)

PlaybackController playback = new(fdpPath)
EntityRepository repo = new()
FdpEventBus bus = new()

while playback.StepForward(repo):   // step 1: inject events into read buffer
    frame = playback.CurrentFrame
    ticks = playback.GetFrameMetadata(frame).WallClockTicks
    scanner.Invoke(bus, frame, ticks, results)  // step 2: read IMMEDIATELY after step
    // DO NOT call bus.ClearCurrentBuffers() here

return results
```

The headless search service steps `PlaybackController` directly — it does NOT use `ReplayBrowserContext.SeekToFrame` (which clears the bus). This is the SR-T38 invariant.

### `ExecuteLifecycleSearch`

```
activeRanges = Dictionary<Entity, int>()  // entity -> startFrame
results = List<LifecycleSearchResultDto>()

PlaybackController playback = new(fdpPath)
EntityRepository repo = new()
int eofFrame = -1

while playback.StepForward(repo):
    frame = playback.CurrentFrame
    eofFrame = frame
    
    // Detect births: new alive entities matching criteria
    for each alive entity in repo:
        if Matches(entity, criteria) && !activeRanges.ContainsKey(entity):
            activeRanges[entity] = frame
    
    // Detect deaths via destruction log
    for each destroyed entity in repo.GetDestructionLog():
        if activeRanges.TryGetValue(destroyed, out int start):
            results.Add(new LifecycleSearchResultDto(destroyed, start, frame, BuildLifecycleContext(destroyed, criteria)))
            activeRanges.Remove(destroyed)
    repo.ClearDestructionLog()

// Flush alive ranges at EOF
for each kvp in activeRanges:
    results.Add(new LifecycleSearchResultDto(kvp.Key, kvp.Value, eofFrame, BuildLifecycleContext(kvp.Key, criteria)))

return results
```

**Matching for lifecycle modes**:
- `EcsHandle`: entity index matches `int.TryParse(criteria.TargetValue)` and entity.Index equals it.
- `NetworkId`: check component `NetworkIdentity.Value` (if the component exists in the Hrot codebase). If that type is not resolvable at compile time in `Fdp.Toolkits`, use a string-based lookup by component name on whatever "network identity" component exists. You may stub: check if the entity has a component with `ComponentType.Name == "NetworkIdentity"` and read a field named `Value` via reflection as a fallback for tests. The important thing is that the contract is correct; the actual type name can be confirmed during exploration.
- `NameSubstring`: check a component `EntityInfo.Name` (or similar `Name` string field). Same approach as `NetworkId` — explore what component carries the name in `Hrot` and use it. For tests, you can use a `HarnessPosition.X` hack, OR add a new harness component `HarnessEntityInfo` with a string `Name` field if no suitable component exists.

### Structural frame loop per DESIGN.md §6.4

```csharp
private void RunStructuralFrame(EntityRepository repo, int frame, long ticks,
    StructuralPredicateDto predicate, HashSet<Entity> hasComponent, List<SearchResultDto> results)
{
    int typeId = ComponentTypeRegistry.GetTypeId(predicate.ComponentType);
    // Iterate entity headers 0..MaxIssuedIndex
    for (int idx = 0; idx <= repo.MaxIssuedEntityIndex; idx++)
    {
        Entity entity = repo.GetEntityAtIndex(idx);
        if (!entity.IsValid) continue;
        EntityHeader header = repo.GetHeader(idx);
        if (header.LastChangeTick <= lastScannedVersion) continue;  // skip unchanged rows
        
        bool present = ComputeEffectivePresence(header, typeId, predicate.AuthorityRequirement);
        bool was = hasComponent.Contains(entity);
        
        if (present != was)
        {
            if (present)
            {
                // Added edge
                if (predicate.ModificationType is StructuralModification.Added or StructuralModification.AnyChange)
                    results.Add(new SearchResultDto(frame, ticks, entity, $"Gained {predicate.ComponentType.Name}"));
                hasComponent.Add(entity);
            }
            else
            {
                // Removed edge
                if (predicate.ModificationType is StructuralModification.Removed or StructuralModification.AnyChange)
                    results.Add(new SearchResultDto(frame, ticks, entity, $"Lost {predicate.ComponentType.Name}"));
                hasComponent.Remove(entity);
            }
        }
    }
    // Destruction: emit "Lost {T} (Destroyed)"
    foreach (Entity destroyed in repo.GetDestructionLog())
    {
        if (hasComponent.Contains(destroyed))
        {
            results.Add(new SearchResultDto(frame, ticks, destroyed, $"Lost {predicate.ComponentType.Name} (Destroyed)"));
            hasComponent.Remove(destroyed);
        }
    }
}

private static bool ComputeEffectivePresence(EntityHeader header, int typeId, AuthorityRequirement req) =>
    req switch
    {
        AuthorityRequirement.Any            => header.ComponentMask.IsSet(typeId),
        AuthorityRequirement.RequireAuthority => header.ComponentMask.IsSet(typeId) && header.AuthorityMask.IsSet(typeId),
        AuthorityRequirement.RequireGhost    => header.ComponentMask.IsSet(typeId) && !header.AuthorityMask.IsSet(typeId),
        _ => false
    };
```

**Note**: explore `EntityRepository` to find the actual API for `GetHeader(index)`, `MaxIssuedEntityIndex`, `GetEntityAtIndex`. If these exact names don't exist, find the equivalent API.

### Tests: `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/RecordingSearchServiceTests.cs`

Cover SR-T02..SR-T38 (all tests in DESIGN.md §6.8 except SR-T01 which is an assembly test, SR-T28..SR-T33 which are UI/preset tests, SR-T39 which is a panel test). This is the bulk of Stage 4 tests.

**Key tests to implement precisely**:

**SR-T02** — Equals: 5-frame harness with `HarnessPosition.X` values 100,90,80,70,60. Search `Equals` 80 yields 1 result at frame 2 (0-indexed).

**SR-T03** — GreaterThan: same harness, search `> 75` yields 3 results (frames 0,1,2 with X=100,90,80).

**SR-T05** — Compound AND: 2 conditions both must be true simultaneously. Use two harness components.

**SR-T06** — Compound OR: union of two conditions, no duplicates per frame/entity.

**SR-T09** — Chunk-skip: fill the repository with many stationary entities; verify the inner loop skips them (spy on visit count by reading `GetAllocatedBytesForCurrentThread` and asserting it stays bounded, OR by wrapping `QueryDelta` with a counting proxy — choose whichever is feasible).

**SR-T10..SR-T13** — Spatial: `HarnessTransform.Position` as the spatial position. Move an entity across a zone boundary. Verify Entry/Exit/EntryOrExit emission at correct frames.

**SR-T14..SR-T18** — Structural: add/remove a component across frames; verify Added/Removed/AnyChange emission.

**SR-T37** — Authority: create two entities, one with authority (ComponentMask AND AuthorityMask set), one ghost (ComponentMask set, AuthorityMask clear). Assert `RequireAuthority` finds only entity A, `RequireGhost` finds only entity B, `Any` finds both.
- For tests where authority distinction matters, you need to set `AuthorityMask` directly on entity headers — find the API on `EntityRepository` for setting authority (explore `Hrot.Network.BDC` or `Fdp.Core` for authority-setting utilities).

**SR-T34** — Zero allocation: compile a predicate, run a 10k-frame scan with NO matches. Assert `GC.GetAllocatedBytesForCurrentThread()` delta == 0. Pre-warm with 1 frame before the assertion window.

**SR-T36** — Isolation: run `ExecuteSearch` in parallel with a `ReplayBrowserContext` loaded to frame 7. After search completes, verify context `CurrentFrame` is still 7.

**SR-T38** — Event-scanner timing: instrument the bus with a spy that counts `ClearCurrentBuffers` calls. Run `RunEventScan`. Assert `ClearCurrentBuffers` was never called between `StepForward` and `scanner.Invoke`. Alternatively, assert the scanner receives the event that was fired on a specific frame (if the buffer were cleared, the event would be invisible).

---

## Task 6: RB-4.6 — `BoundingBoxPickerGizmo`

**Path**: `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/BoundingBoxPickerGizmo.cs`

**Spec**: DESIGN.md §6.6 and TASK-DETAILS.md §RB-4.6.

Model on `FdpLocationPickerGizmo`. The gizmo:

```csharp
public sealed class BoundingBoxPickerGizmo : IEntityStatefulGizmo
{
    private readonly Action<BoundingBox2D> _onComplete;
    private readonly Action _onRemove;
    private Vector3 _startPos;
    private Vector3 _currentPos;
    private bool _isDragging;

    public bool RequiresExclusiveFocus => true;
    public bool WantsRawInput => true;
    public bool IsFocused { get; private set; }
    public void SetFocus(bool isFocused) => IsFocused = isFocused;

    public BoundingBoxPickerGizmo(Action<BoundingBox2D> onComplete, Action onRemove) { ... }

    public void UpdateAndDraw(float deltaTime, IDebugDrawBuilder draw)
    {
        if (_isDragging)
        {
            // draw translucent box via draw.EmitRaw(DebugPrimitive.MakeBox2D(...)) on PipelineTarget.Map2D
        }
    }

    public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
    {
        if (button == MapMouseButton.Left && isPressed)
        {
            _startPos = worldPos;
            _isDragging = true;
        }
        else if (button == MapMouseButton.Left && !isPressed && _isDragging)
        {
            _isDragging = false;
            var min = new Vector2(Math.Min(_startPos.X, _currentPos.X), Math.Min(_startPos.Y, _currentPos.Y));
            var max = new Vector2(Math.Max(_startPos.X, _currentPos.X), Math.Max(_startPos.Y, _currentPos.Y));
            _onComplete(new BoundingBox2D { Min = min, Max = max });
            _onRemove();
        }
        else if (button == MapMouseButton.Right && isPressed)
        {
            _isDragging = false;
            _onRemove();
        }
    }

    public void OnDragUpdate(Vector3 worldPos) => _currentPos = worldPos;

    public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
    {
        if (key == MapKeyboardKey.Escape && isPressed)
        {
            _isDragging = false;
            _onRemove();
        }
    }

    // Other IEntityStatefulGizmo members: OnInteractionStarted, OnCommit, OnCancel, OnMenuAction -> no-op
    public void Dispose() { }
}
```

**Note on `DebugPrimitive.MakeBox2D`**: Explore `DebugPrimitive.cs` in `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/Primitives/` for the correct method signature. If `MakeBox2D` doesn't exist, use the nearest equivalent (e.g., `DrawLine` for 4 box edges). The draw code is best-effort since tests will not exercise it (there's no headless draw context for gizmo drawing).

**Tests**: `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/BoundingBoxPickerGizmoTests.cs`

SR-T30: fire `OnMouseEvent(Left, true, (10,10,0))` → `OnDragUpdate((20,30,0))` → `OnMouseEvent(Left, false, (20,30,0))`. Assert `onComplete` called exactly once with `Min=(10,10)`, `Max=(20,30)`. Assert `onRemove` called exactly once.

SR-T31: fire `OnMouseEvent(Left, true, (10,10,0))` → `OnKeyEvent(Escape, true)`. Assert `onComplete` NOT called. Assert `onRemove` called once. Repeat with `OnMouseEvent(Right, true, ...)` → same assertion.

---

## Task 7: RB-4.7 — Acceptance Gate

Verify ALL tests pass:

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2\FDP
dotnet test Fdp.Toolkits.Tests --filter "ReplayBrowser" -v minimal
```

All SR-T01..SR-T38 must pass (total count of tests must equal 38 or more for Stage 4, but some SR tests may be split into multiple xUnit test methods — that is fine).

Also verify build is clean:
```powershell
dotnet build FDP.sln -c Release --no-restore
```

---

## Definition of Done for BATCH-05

- [ ] All production files compiled, no build errors.
- [ ] SR-T01 (assembly reference test): `typeof(RecordingSearchService).Assembly` has no reference to `Fdp.Presentation` or any Raylib assembly.
- [ ] SR-T02..SR-T09: component property and compiler tests pass.
- [ ] SR-T10..SR-T13: spatial bounding tests pass.
- [ ] SR-T14..SR-T18: structural modification tests pass.
- [ ] SR-T19..SR-T22: lifecycle tests pass.
- [ ] SR-T23..SR-T27: transient event scanner tests pass.
- [ ] SR-T30..SR-T31: bounding box gizmo tests pass.
- [ ] SR-T34: strict zero-allocation loop body assertion passes.
- [ ] SR-T35: AND short-circuit verified.
- [ ] SR-T36: search engine isolation verified.
- [ ] SR-T37: authority-aware structural search (RequireAuthority, RequireGhost, Any) verified.
- [ ] SR-T38: event scanner timing invariant verified (no `ClearCurrentBuffers` between StepForward and scanner.Invoke).
- [ ] All pre-existing tests still pass (pre-existing `Hrot.SimHost.Tests` errors involving `AreaQueryBatchData` and `EqsTargetPool` are NOT your responsibility — do not touch them).

---

## Report

Write your batch report to: `.dev/replay-browser-2/reports/BATCH-05-REPORT.md`

Report must include:
1. Summary table of all files created/modified with line counts.
2. Test counts per test file.
3. Full output of `dotnet test --filter ReplayBrowser` (or equivalent).
4. Any deviations from the design and why.
5. Any StructEdit API surface that differed from the design and how you adapted.
6. Any EntityRepository API that differed and how you adapted.

---

## Key Rules

1. **FDP is a git submodule** — all files for this batch live inside `FDP/`. Do not touch the parent repo.
2. **No Fdp.Presentation reference** from the Search namespace — this is a pure backend concern.
3. **Do not call `ReplayBrowserContext.SeekToFrame`** inside the search service's event scan loop — step `PlaybackController` directly.
4. **Preserve all existing comments** in files you edit — AGENTS.md invariant.
5. **No Unicode in ASCII contexts** — AGENTS.md invariant.
6. **Make it compile** — verify `dotnet build` before writing the report.
