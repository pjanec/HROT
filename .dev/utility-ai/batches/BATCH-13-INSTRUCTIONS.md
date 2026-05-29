# BATCH-13 Instructions: P4-03 — TuningRegistry + TuningConsoleGizmo (Slice 1, scalars)

**Task:** TASK-UAI-P4-03
**Design reference:** `.dev/utility-ai/Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md` §3, §4, §5
**Related tasks completed:** P4-01 (AiOverlayFlags), P4-02 (5 overlay sources)

---

## Goal

Implement the runtime AI tuning registry and a gizmo-backed console that exposes scalar-editable
tunables for all loaded `UtilityDecisionDef` consideration parameters. Curves are exposed as
four bounded scalars (m=Slope, k=Exponent, b=XShift, c=Weight) — the visual widget comes in
Phase 6.

This batch covers SC-P4-03-1 and SC-P4-03-2 from TASK-DETAIL.md. SC-P4-03-3 (replay honesty),
SC-P4-03-4 (DDS Brain routing), and SC-P4-03-5 (Muscle routing) require Flight Recorder and DDS
infrastructure not present in this slice; they are deferred.

---

## Scope

New project to create:
- `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/` — main library
- `Hrot/Diagnostics/Hrot.Diagnostics.Tuning.Tests/` — test project

Files to create (all internal unless stated otherwise):

| File | Description |
|------|-------------|
| `Hrot.Diagnostics.Tuning.csproj` | Project file |
| `TuningKey.cs` | FNV-1a-keyed name struct |
| `TuningKind.cs` | Enum: Float, Int, Bool |
| `TuningScope.cs` | Enum: Global, PerNodeRole, PerEntity, PerSquad |
| `TuningOwner.cs` | Enum: Brain, Muscle |
| `Tunable.cs` | Registry entry with bounds, delegates, provenance |
| `TuningChangeEvent.cs` | Struct (ready for Flight Recorder, not wired in this batch) |
| `TuningAttribute.cs` | `[Tunable]` field marker attribute |
| `TuningRegistry.cs` | Register, apply-queue, drain, clamp/validate |
| `UtilityTuningBinder.cs` | Auto-registers all loaded UtilityDecisionDef fields |
| `Gizmos/TuningConsoleGizmo.cs` | IStatefulGizmo, StructInspector-backed (minimal) |
| `Hrot.Diagnostics.Tuning.Tests.csproj` | Test project file |
| `TuningRegistryTests.cs` | Tests for SC-P4-03-1 and SC-P4-03-2 |
| `TuningConsoleGizmoTests.cs` | Tests for gizmo OnStructUpdate and MainMenuBinding emission |
| `UtilityTuningBinderTests.cs` | Tests that registered tunables are readable and writable |

---

## Existing patterns to follow

**Study these files before implementing:**

1. `FDP/ExtDeps/GizmoMap/GizmoMap.Example/Gizmos/LayerControlGizmo.cs`
   — The exact pattern for `TuningConsoleGizmo`. Follow it exactly.
2. `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/OverlayBudgetArbiter.cs`
   — The pattern for internal sealed classes with `InternalsVisibleTo` tests.
3. `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/Hrot.Diagnostics.Overlays.csproj`
   — The project file pattern to copy for `Hrot.Diagnostics.Tuning.csproj`.
4. `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/Interaction/IStatefulGizmo.cs`
   — The interface `TuningConsoleGizmo` must implement.

---

## Implementation Details

### 1. `TuningKey.cs`

Namespace: `Hrot.Diagnostics.Tuning`

```csharp
public readonly struct TuningKey : IEquatable<TuningKey>
{
    public readonly uint Id;     // FNV-1a-32 of Name
    public readonly string Name; // dotted name, e.g. "utility.CombatPosture.0.0.weight"

    public TuningKey(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Id   = Fnv1a32(name);
    }

    public bool Equals(TuningKey other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is TuningKey k && Equals(k);
    public override int GetHashCode() => (int)Id;

    private static uint Fnv1a32(string s)
    {
        uint hash = 2166136261u;
        foreach (char c in s)
        {
            hash ^= (byte)c;
            hash *= 16777619u;
        }
        return hash;
    }
}
```

### 2. `TuningKind.cs`

```csharp
public enum TuningKind { Float, Int, Bool }
```

### 3. `TuningScope.cs`

```csharp
public enum TuningScope { Global, PerNodeRole, PerEntity, PerSquad }
```

### 4. `TuningOwner.cs`

```csharp
public enum TuningOwner { Brain, Muscle }
```

### 5. `Tunable.cs`

```csharp
public sealed class Tunable
{
    public TuningKey   Key;
    public TuningKind  Kind;
    public float       Min;
    public float       Max;
    public TuningScope Scope;
    public TuningOwner Owner;
    public required Func<float>    Read;
    public required Action<float>  Write;
    public string      Provenance = string.Empty;
    // GroupKey is the first segment of Key.Name up to and including the second dot.
    // e.g. "utility.CombatPosture.0.0.weight" -> group "utility.CombatPosture"
}
```

### 6. `TuningChangeEvent.cs`

Struct ready for Flight Recorder recording. Not wired to FlightRecorder in this batch.

```csharp
// Records a tuning change for replay honesty (Design §5.4).
// Not wired to FlightRecorder in Slice 1; the field layout is stable.
public readonly struct TuningChangeEvent
{
    public readonly TuningKey Key;
    public readonly float     OldValue;
    public readonly float     NewValue;
    public readonly ulong     WallTick;   // frame counter at apply time
    // OperatorId placeholder for Slice 2 access control (§11 T-1).
}
```

Do not wire `TuningChangeEvent` to the flight recorder in this batch. Just define the struct.

### 7. `TuningAttribute.cs`

```csharp
// Marks a field for automatic discovery by the tuning source-gen (follow-on).
// In Slice 1 only manual registration is used; this attribute is a forward declaration.
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TunableAttribute : Attribute
{
    public float Min { get; set; } = float.MinValue;
    public float Max { get; set; } = float.MaxValue;
    public TuningScope Scope { get; set; } = TuningScope.Global;
    public TuningOwner Owner { get; set; } = TuningOwner.Brain;
}
```

### 8. `TuningRegistry.cs`

```csharp
// Thread-safe registration; drain must be called on the simulation thread.
public sealed class TuningRegistry
{
    private readonly Dictionary<uint, Tunable>      _tunables  = new();
    private readonly Queue<(uint id, float value)>  _applyQueue = new();
    private readonly object                          _queueLock  = new();
    // Warnings emitted when out-of-range commits are clamped:
    private readonly Action<string>?                _warn;

    public TuningRegistry(Action<string>? warn = null) { _warn = warn; }

    // Register a tunable. Overwrites any existing entry with the same key.
    public void Register(TuningKey key, Tunable tunable)
    {
        tunable.Key = key;
        _tunables[key.Id] = tunable;
    }

    // Enqueue a value change. Does NOT apply immediately.
    // Called from any thread (e.g., OnStructUpdate callback from the network layer).
    public bool Apply(TuningKey key, float value)
    {
        if (!_tunables.TryGetValue(key.Id, out var tunable)) return false;
        lock (_queueLock)
            _applyQueue.Enqueue((key.Id, value));
        return true;
    }

    // Drain the apply queue and write all pending changes.
    // Must be called at frame top, before any system reads config.
    public void BeginFrame()
    {
        (uint id, float value)[] pending;
        lock (_queueLock)
        {
            if (_applyQueue.Count == 0) return;
            pending = _applyQueue.ToArray();
            _applyQueue.Clear();
        }
        foreach (var (id, value) in pending)
        {
            if (!_tunables.TryGetValue(id, out var tunable)) continue;
            float clamped = Math.Clamp(value, tunable.Min, tunable.Max);
            if (clamped != value)
                _warn?.Invoke($"Tuning value for '{tunable.Key.Name}' clamped {value} -> {clamped}");
            tunable.Write(clamped);
        }
    }

    // Returns groups as (prefix, tunables) pairs. Prefix is the dotted namespace up to
    // the third segment, e.g. "utility.CombatPosture".
    public IEnumerable<(string prefix, IReadOnlyList<Tunable> tunables)> GetGroups()
    {
        var groups = new Dictionary<string, List<Tunable>>();
        foreach (var t in _tunables.Values)
        {
            string prefix = GetGroupPrefix(t.Key.Name);
            if (!groups.TryGetValue(prefix, out var list))
                groups[prefix] = list = new List<Tunable>();
            list.Add(t);
        }
        foreach (var kv in groups)
            yield return (kv.Key, kv.Value);
    }

    public bool TryGet(TuningKey key, out Tunable? tunable)
        => _tunables.TryGetValue(key.Id, out tunable);

    private static string GetGroupPrefix(string name)
    {
        // Return the first two segments of a dotted name, e.g.
        // "utility.CombatPosture.0.0.weight" -> "utility.CombatPosture"
        int first = name.IndexOf('.');
        if (first < 0) return name;
        int second = name.IndexOf('.', first + 1);
        return second < 0 ? name : name[..second];
    }
}
```

### 9. `UtilityTuningBinder.cs`

Auto-registers all `UtilityDecisionDef` consideration fields. Uses closure capture to
implement Read/Write via array-element replacement on the managed `Considerations[]` array.

```csharp
// Auto-registers consideration fields from a UtilityDecisionDef as tunables.
// Registered names follow: utility.<DecisionName>.<optionId>.<considerationIdx>.<field>
// Fields per consideration: weight, slope (m), exponent (k), xShift (b)
public static class UtilityTuningBinder
{
    public static void RegisterDecision(TuningRegistry registry, UtilityDecisionDef def)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(def);

        string decName = def.DebugName;
        foreach (var option in def.Options)
        {
            for (int ci = 0; ci < option.Considerations.Length; ci++)
            {
                RegisterConsideration(registry, decName, option, ci);
            }
        }
    }

    private static void RegisterConsideration(
        TuningRegistry registry,
        string decName,
        UtilityOption option,
        int ci)
    {
        string prefix = $"utility.{decName}.{option.OptionId}.{ci}";

        // weight [0..10]
        registry.Register(new TuningKey($"{prefix}.weight"), new Tunable
        {
            Kind       = TuningKind.Float,
            Min        = 0f,
            Max        = 10f,
            Scope      = TuningScope.Global,
            Owner      = TuningOwner.Brain,
            Provenance = $"decision:{decName}",
            Read       = () => option.Considerations[ci].Weight,
            Write      = v =>
            {
                var old = option.Considerations[ci];
                option.Considerations[ci] = new UtilityConsideration(
                    old.InputId, old.Context, v, old.Curve, old.Params);
            },
        });

        // slope / m [-2..2]
        registry.Register(new TuningKey($"{prefix}.slope"), new Tunable
        {
            Kind       = TuningKind.Float,
            Min        = -2f,
            Max        = 2f,
            Scope      = TuningScope.Global,
            Owner      = TuningOwner.Brain,
            Provenance = $"decision:{decName}",
            Read       = () => option.Considerations[ci].Curve.Slope,
            Write      = v =>
            {
                var old = option.Considerations[ci];
                var c = old.Curve;
                option.Considerations[ci] = new UtilityConsideration(
                    old.InputId, old.Context, old.Weight,
                    new ResponseCurve(c.Kind, v, c.Exponent, c.XShift),
                    old.Params);
            },
        });

        // exponent / k [0..20]
        registry.Register(new TuningKey($"{prefix}.exponent"), new Tunable
        {
            Kind       = TuningKind.Float,
            Min        = 0f,
            Max        = 20f,
            Scope      = TuningScope.Global,
            Owner      = TuningOwner.Brain,
            Provenance = $"decision:{decName}",
            Read       = () => option.Considerations[ci].Curve.Exponent,
            Write      = v =>
            {
                var old = option.Considerations[ci];
                var c = old.Curve;
                option.Considerations[ci] = new UtilityConsideration(
                    old.InputId, old.Context, old.Weight,
                    new ResponseCurve(c.Kind, c.Slope, v, c.XShift),
                    old.Params);
            },
        });

        // xShift / b [-1..2]
        registry.Register(new TuningKey($"{prefix}.xShift"), new Tunable
        {
            Kind       = TuningKind.Float,
            Min        = -1f,
            Max        = 2f,
            Scope      = TuningScope.Global,
            Owner      = TuningOwner.Brain,
            Provenance = $"decision:{decName}",
            Read       = () => option.Considerations[ci].Curve.XShift,
            Write      = v =>
            {
                var old = option.Considerations[ci];
                var c = old.Curve;
                option.Considerations[ci] = new UtilityConsideration(
                    old.InputId, old.Context, old.Weight,
                    new ResponseCurve(c.Kind, c.Slope, c.Exponent, v),
                    old.Params);
            },
        });
    }
}
```

### 10. `Gizmos/TuningConsoleGizmo.cs`

Follows the exact same pattern as `FDP/ExtDeps/GizmoMap/GizmoMap.Example/Gizmos/LayerControlGizmo.cs`.
No `IComponentEditService` dependency in Slice 1 — OnStructUpdate parses JSON manually using
`System.Text.Json.JsonDocument`.

```csharp
// Generalized LayerControlGizmo for AI tuning parameters.
// Pattern: design §4.1, follows LayerControlGizmo.Example exactly.
//
// Each frame it emits:
//   - A MainMenuBinding ("Tools > AI Tuning Console...")
//   - A StructInspector primitive (when _isEditing == true)
//
// OnStructUpdate parses the JSON payload and applies field values to the registry.
public sealed class TuningConsoleGizmo : IStatefulGizmo
{
    public const  long AnchorId      = 9001L;
    public const  int  OpenActionId  = 260;
    private static readonly uint SchemaHash = Fnv1a32("Hrot.Diagnostics.Tuning.TuningConsoleGizmo");
    private static readonly string MainMenuJson =
        "[{\"label\":\"Tools\",\"priority\":50,\"children\":[{\"id\":" + OpenActionId + ",\"label\":\"AI Tuning Console...\"}]}]";

    private readonly TuningRegistry _registry;
    private bool _isEditing;

    public bool RequiresExclusiveFocus => false;
    public bool IsFocused { get; private set; }
    public void SetFocus(bool isFocused) => IsFocused = isFocused;

    public TuningConsoleGizmo(TuningRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public void ToggleEditor() => _isEditing = !_isEditing;

    public void UpdateAndDraw(float deltaTime, IGizmoDrawBuilder draw)
    {
        // Inject "Tools > AI Tuning Console..." into the host main menu bar.
        draw.DrawMainMenuBinding(MainMenuJson);

        // Emit StructInspector panel when editing is active.
        if (_isEditing)
        {
            draw.EmitRaw(DebugPrimitive.MakeStructInspector(
                networkId:  AnchorId,
                schemaHash: SchemaHash,
                anchor:     ScreenAnchor.Center,
                sizeMode:   SizeMode.ScreenPercent,
                isReadOnly: false));
        }
    }

    // Called by GizmoInteractionManager when the StructInspector panel fires an Apply.
    // Payload is a JSON object: {"utility.CombatPosture.0.0.weight": 1.5, ...}
    public void OnStructUpdate(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.TryGetSingle(out float v))
                    _registry.Apply(new TuningKey(prop.Name), v);
            }
        }
        catch (Exception ex)
        {
            // Dropped invalid tuning StructUpdate -- do not propagate.
            Console.Error.WriteLine($"[TuningConsoleGizmo] Dropped invalid StructUpdate: {ex.Message}");
        }
    }

    public void OnMenuAction(int actionId)
    {
        if (actionId == OpenActionId) _isEditing = !_isEditing;
    }

    // No-op stubs for IGizmoInteractionHandler methods not used by this gizmo.
    public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
    public void OnDragUpdate(Vector3 worldPos) { }
    public void OnCommit(Vector3 worldPos) { }
    public void OnCancel() { }
    public void OnMouseEvent(MapMouseButton button, bool pressed, Vector3 worldPos) { }
    public void OnKeyEvent(MapKeyboardKey key, bool pressed) { }
    public void Dispose() { }

    private static uint Fnv1a32(string s)
    {
        uint hash = 2166136261u;
        foreach (char c in s) { hash ^= (byte)c; hash *= 16777619u; }
        return hash;
    }
}
```

### 11. Project file `Hrot.Diagnostics.Tuning.csproj`

References needed:
- `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj` (for UtilityDecisionDef, ResponseCurve, etc.)
- `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/GizmoMap.Contracts.csproj` (for IGizmoDrawBuilder, IStatefulGizmo, DebugPrimitive, etc.)

Add `InternalsVisibleTo` for `Hrot.Diagnostics.Tuning.Tests`.

No need for `AllowUnsafeBlocks`.

### 12. Project file `Hrot.Diagnostics.Tuning.Tests.csproj`

References:
- `Hrot.Diagnostics.Tuning.csproj`
- `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj`
- `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/GizmoMap.Contracts.csproj`
- xUnit 2.5.3 package reference (match the version in other test projects)

---

## Tests to implement

### File: `TuningRegistryTests.cs`

Namespace: `Hrot.Diagnostics.Tuning.Tests`

**SC-P4-03-1: Out-of-range clamped, warning emitted**

```
[Fact] Apply_AboveMax_ClampsToMax_AndWarns()
  - Register a tunable with Min=0f, Max=1f
  - Apply value 5.0f
  - BeginFrame()
  - Assert live value == 1.0f (clamped)
  - Assert warning was emitted (capture via warn callback)

[Fact] Apply_BelowMin_ClampsToMin_AndWarns()
  - Register a tunable with Min=0f, Max=1f
  - Apply value -1.0f
  - BeginFrame()
  - Assert live value == 0.0f (clamped)
  - Assert warning was emitted

[Fact] Apply_InRange_NoClamp_NoWarn()
  - Register tunable Min=0f, Max=1f
  - Apply 0.5f
  - BeginFrame()
  - Assert live == 0.5f
  - Assert no warning emitted
```

**SC-P4-03-2: Enqueued, applied at frame top**

```
[Fact] Apply_IsQueuedNotImmediate()
  - Register tunable, initial live = 0.5f
  - Apply(key, 0.9f)
  - Assert live still == 0.5f (not applied mid-tick)
  - BeginFrame()
  - Assert live == 0.9f (applied at frame top)

[Fact] Apply_UnknownKey_ReturnsFalse()
  - Create registry with no tunables
  - Assert Apply(unknownKey, 0f) returns false

[Fact] BeginFrame_MultipleQueued_AppliesAll()
  - Register two tunables, both at 0.0f
  - Apply both with values 0.3f and 0.7f
  - BeginFrame()
  - Assert both values applied
```

**TuningKey equality**

```
[Fact] TuningKey_SameName_EqualId()
  - new TuningKey("a.b.c").Id == new TuningKey("a.b.c").Id

[Fact] TuningKey_DifferentName_DifferentId()
  - new TuningKey("a.b.c").Id != new TuningKey("a.b.d").Id
```

### File: `TuningConsoleGizmoTests.cs`

Use the same `CountingDrawBuilder` pattern from `Hrot.Diagnostics.Overlays.Tests`.
Copy `CountingDrawBuilder` into this test project (or reference the overlays test project if
dependencies allow — but prefer copy to avoid circular test dependencies).

```
[Fact] UpdateAndDraw_AlwaysEmitsMainMenuBinding()
  - Create registry, gizmo
  - Call UpdateAndDraw with CountingDrawBuilder
  - Since CountingDrawBuilder.DrawMainMenuBinding increments EmitCount, assert >= 1

[Fact] UpdateAndDraw_NotEditing_NoStructInspector()
  - Gizmo starts with _isEditing = false
  - Call UpdateAndDraw; no EmitRaw should be called
  - Need to distinguish DrawMainMenuBinding vs EmitRaw calls:
    Use a builder that counts EmitRaw separately (or use a specialized stub)

[Fact] OnStructUpdate_ValidJson_AppliesValueAfterBeginFrame()
  - Register tunable "utility.test.0.0.weight" with Min=0 Max=10
  - Call gizmo.OnStructUpdate("{\"utility.test.0.0.weight\": 3.5}")
  - Assert live value still unchanged (queued, not applied)
  - Call registry.BeginFrame()
  - Assert live value == 3.5f

[Fact] OnStructUpdate_EmptyJson_DoesNotThrow()
  - Call OnStructUpdate("") -- must not throw

[Fact] OnStructUpdate_InvalidJson_DoesNotThrow()
  - Call OnStructUpdate("{bad json}") -- must not throw

[Fact] OnMenuAction_OpenActionId_TogglesEditing()
  - Gizmo._isEditing is false
  - OnMenuAction(TuningConsoleGizmo.OpenActionId)
  - The gizmo is now in editing state (test indirectly: after another UpdateAndDraw, EmitRaw count > 0)
```

For the `CountingDrawBuilder` in this test project, it needs to implement `IGizmoDrawBuilder` and
also count `DrawMainMenuBinding` and `EmitRaw` calls separately:

```csharp
internal sealed class TuningDrawBuilder : IGizmoDrawBuilder
{
    public int MainMenuCount;
    public int EmitRawCount;
    public int OtherCount;

    public void DrawMainMenuBinding(string json) => MainMenuCount++;
    public void EmitRaw(in DebugPrimitive p)      => EmitRawCount++;
    // All other IGizmoDrawBuilder methods: => OtherCount++
    // ... (implement all required interface members as no-ops)
}
```

### File: `UtilityTuningBinderTests.cs`

```
[Fact] RegisterDecision_SingleOptionSingleConsideration_RegistersFourTunables()
  - Create a UtilityDecisionDef with DebugName="Test", one option (id=1) with one consideration
  - Create registry, call UtilityTuningBinder.RegisterDecision(registry, def)
  - Assert registry has entries for:
    "utility.Test.1.0.weight", "utility.Test.1.0.slope",
    "utility.Test.1.0.exponent", "utility.Test.1.0.xShift"
  - 4 tunables registered total (TryGet for each returns true)

[Fact] RegisterDecision_Read_ReturnsCurrentConsiderationValue()
  - Create def with Weight=0.8f, Slope=1.5f
  - Register
  - Read() on weight tunable == 0.8f
  - Read() on slope tunable == 1.5f

[Fact] RegisterDecision_Write_UpdatesConsiderationInPlace()
  - Create def with Weight=0.8f
  - Register
  - Apply weight = 1.2f; BeginFrame()
  - Read() on weight tunable == 1.2f
  - Read() on slope tunable is unchanged

[Fact] RegisterDecision_MultipleOptions_RegistersAllConsiderations()
  - Create def with 2 options, 2 considerations each = 2*2*4 = 16 tunables expected
  - Assert 16 tunables registered
```

---

## Solution file update

Add two `Project(...)` entries to `IOS-IG-SimHost.sln`:
- `Hrot.Diagnostics.Tuning` — use GUID `{A1B2C3D4-E5F6-7890-AB12-CD34EF560003}`
- `Hrot.Diagnostics.Tuning.Tests` — use GUID `{A1B2C3D4-E5F6-7890-AB12-CD34EF560004}`

Both projects must be nested inside the existing `Diagnostics` solution folder:
`{5E4C52BA-6213-E083-B735-5DDE0CCE6DA3}`.

To find the existing Debug/Release configuration block pattern, look at how
`Hrot.Diagnostics.Overlays` and `Hrot.Diagnostics.Overlays.Tests` were added — add the same
6 configuration entries (Debug|Any CPU, Debug|x64, Debug|x86, Release|Any CPU, Release|x64,
Release|x86) for both new projects.

---

## Deferred (NOT in this batch)

- `TuningChangeEvent` wired to `FlightRecorder` — SC-P4-03-3 (replay honesty)
- DDS routing: Brain-owned tunable from ExCon — SC-P4-03-4
- Muscle-owned tunable forwarding — SC-P4-03-5
- `TuningConsoleGizmo` synthesized DTO (requires `IComponentEditService`) — Phase 6
- Source-gen discovery of `[Tunable]` fields — Phase 6
- `GlobalDebugSettings` tunables fold-in — Phase 6

---

## Success criteria for this batch

1. `dotnet build IOS-IG-SimHost.sln` succeeds with zero errors.
2. All tests in `Hrot.Diagnostics.Tuning.Tests` pass (target: >= 18 tests).
3. SC-P4-03-1 covered: `Apply_AboveMax_ClampsToMax_AndWarns`, `Apply_BelowMin_ClampsToMin_AndWarns`.
4. SC-P4-03-2 covered: `Apply_IsQueuedNotImmediate` asserts no mid-tick mutation.
5. `UtilityTuningBinder.RegisterDecision` registers 4 tunables per consideration; write delegates
   update the live `UtilityDecisionDef.Options[].Considerations[]` array elements.
6. `TuningConsoleGizmo.OnStructUpdate` with a valid JSON payload enqueues changes to the registry;
   changes are applied after `BeginFrame()`.
7. Report placed at `.dev/utility-ai/reports/BATCH-13-REPORT.md`.
