# BATCH-06 Instructions — Remote Visualization Foundation (GZ015-GZ018)

## Onboarding

**Design reference:** `.dev/gizmos-1/DESIGN.md` (§2.4, §6.1–6.4)
**Task details:** `.dev/gizmos-1/TASK-DETAIL.md` (GZ015, GZ016, GZ017, GZ018)

**Previous batch reviews:**
- `.dev/gizmos-1/reviews/BATCH-05-REVIEW.md` — 2D adapter approved; see design deviations.
- `.dev/gizmos-1/reviews/BATCH-04-REVIEW.md` — Settings store approved.

**What exists already:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/` — Full gizmo primitive, buffer, settings, and string intern code.
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/StringInternBatch.cs` — Pattern example for DDS topics in FDP.
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/GizmoSettingsRegistry.cs` — `IsDirty`, `EnumerateAll()`, `ClearDirty()`.
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/` — Directory exists (has GizmoSettingsPersistence).
- `Hrot/Engine/Hrot.Core/MapDefinitions/HrotComponentIds.cs` — Component ID registry.
- `Hrot/Subsystems/Hrot.IG/Abstractions/IDdsWriter.cs` — `IDdsWriter<T>.Write(T sample)`.
- `Hrot/Subsystems/Hrot.IG/` — IG subsystem where GZ015 and GZ018 components/systems go.

**Key constraints:**
- `Fdp.Toolkits` references `CycloneDDS.Schema` and `CycloneDDS.Runtime`. Use `using CycloneDDS.Schema;` for DDS attributes.
- `Hrot.IG` already has `IDdsWriter<T>` in namespace `Hrot.IG.Abstractions`.
- Next available `HrotComponentId` byte value: **185** (check HrotComponentIds.cs to confirm 185 is not taken before using it).
- DDS topic names must not collide with existing topics. Existing gizmo-related topics: `"StringInternBatch"`.

---

## Corrective: D-003 — Selection Predicate Wiring

**Priority: P2 — must be included in this batch.**

**Problem:** `DataDrivenGizmoSystem` and `BehaviorGizmoManagerSystem` both accept an optional `Func<Entity, bool>? predicate` but the game host / cluster runner does not wire it. Currently the predicate is always null → all entities draw gizmos.

**Task:** In `Hrot.ClusterRunner` (the startup/registration code), find where `DataDrivenGizmoSystem` and `BehaviorGizmoManagerSystem` are constructed and registered, and wire a default predicate that:
- Returns `true` for all entities when `GlobalDebugSettings.ForceAllGizmosVisible == true`
- Otherwise returns `true` if the entity's `DebugLayer` matches `GlobalDebugSettings.DebugLayerMask`

**Implementation notes:**
- Do not modify system signatures — they already have the optional predicate parameter.
- Wire at the kernel/module-host registration site (search for `DataDrivenGizmoSystem` construction in `Hrot.ClusterRunner`).
- If `GlobalDebugSettings` is not yet set when the predicate runs, treat it as `ForceAllGizmosVisible=false, DebugLayerMask=0xFFFF` (all visible = safe default).

**Tests:** Add a test to the relevant test project (likely `Hrot.ClusterRunner.Tests`) verifying that `DataDrivenGizmoSystem` with a `predicate` returning `false` skips execution for the filtered entity.

---

## TASK-GZ015 — GlobalDebugSettings ECS Singleton

**Target project:** `Hrot/Subsystems/Hrot.IG/`

**Files to create:** `Hrot/Subsystems/Hrot.IG/Gizmos/GlobalDebugSettings.cs`

```csharp
using System.Runtime.InteropServices;
using Fdp.Core.Attributes;
using Hrot.Map.Definitions;

namespace Hrot.IG.Gizmos
{
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(HrotComponentIds.GlobalDebugSettings)]
    [DataPolicy(DataPolicy.Transient)]
    public struct GlobalDebugSettings
    {
        [MarshalAs(UnmanagedType.I1)] public bool ForceAllGizmosVisible;
        public ushort DebugLayerMask; // 16 bits for layers 0-15; default 0xFFFF (all on)
    }
}
```

**Files to modify:** `Hrot/Engine/Hrot.Core/MapDefinitions/HrotComponentIds.cs`

Add a constant: `public const byte GlobalDebugSettings = 185;` in the application-level section with a doc comment.

**ImGui UI hook:** In the existing `Hrot.IG` settings panel (look for ImGui debug UI code), add a collapsible section that:
1. Reads the singleton via `view.HasSingleton<GlobalDebugSettings>()` / `view.GetSingleton<GlobalDebugSettings>()`
2. Shows a checkbox for `ForceAllGizmosVisible`
3. Shows 16 individual layer-bit checkboxes or a hex input for `DebugLayerMask`
4. Writes changes back via `cmd.SetSingleton<GlobalDebugSettings>(updated)`

**Note:** If there is no existing debug settings panel, create a stub method `DrawGlobalDebugSettingsPanel(ISimulationView view, IEntityCommandBuffer cmd)` in a new file `Hrot/Subsystems/Hrot.IG/Gizmos/GlobalDebugSettingsPanel.cs`.

**Tests** (in `Hrot.IG.Tests` or `Hrot.ClusterRunner.Tests`):
- **SC-GZ015-1:** After `repo.SetSingleton(new GlobalDebugSettings { ... })`, `repo.HasSingleton<GlobalDebugSettings>()` is `true`.
- **SC-GZ015-2:** `GlobalDebugSettings` struct size is `Marshal.SizeOf<GlobalDebugSettings>() == 4` (1 byte bool padded to alignment + 2 bytes ushort).
  - Note: `[MarshalAs(UnmanagedType.I1)]` makes bool 1 byte; ushort at offset 2 needs 1 byte pad. Total = 4.
- **SC-GZ015-3:** `DataPolicy.Transient` is set (verify via reflection: `typeof(GlobalDebugSettings).GetCustomAttribute<DataPolicyAttribute>()`).

---

## TASK-GZ016 — DebugPrimitivesBatch DDS Topic

**Target project:** `FDP/Toolkits/Fdp.Toolkits/`

**Files to create:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/DebugPrimitivesBatch.cs`

Pattern: follow `StringInternBatch.cs` exactly.

```csharp
using CycloneDDS.Schema;
using Fdp.Toolkit.Diagnostics.Gizmos.Primitives;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Network
{
    [DdsTopic("DebugPrimitivesBatch")]
    [DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.Volatile,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct DebugPrimitivesBatch
    {
        [DdsKey] public uint FrameNumber;
        [DdsKey] public byte NodeId;
        [DdsManaged] public DebugPrimitive[] Primitives;
    }
}
```

**Tests** (in `Fdp.Toolkits.Tests`):
- **SC-GZ016-1:** `typeof(DebugPrimitivesBatch).GetCustomAttribute<DdsTopicAttribute>()?.TopicName == "DebugPrimitivesBatch"`.
- **SC-GZ016-2:** Round-trip serialization test: create a `DebugPrimitivesBatch` with 2 `DebugPrimitive` entries, serialize with the CycloneDDS schema serializer, deserialize, and assert `Primitives[0]` preserves all 64 bytes.
  - Use the existing serialization test pattern from `Fdp.Toolkits.Tests`. If CycloneDDS schema round-trip tests are not already in the project, you may skip this specific assertion (mark test as "compile-only" SC-GZ016-1 is sufficient).
  - **Do not** add a CycloneDDS.Runtime dependency to the test project if it is not already present.

---

## TASK-GZ017 — GizmoUiState DDS Topic and GizmoSettingsPublisherSystem

**Target project:** `FDP/Toolkits/Fdp.Toolkits/`

### Part A: GizmoUiState DDS Topic

**File to create:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/GizmoUiState.cs`

```csharp
using CycloneDDS.Schema;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Network
{
    [DdsTopic("GizmoUiState")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct GizmoUiState
    {
        [DdsKey] public uint GizmoInstanceId;
        [DdsManaged] public string EditDocumentJson;
    }
}
```

### Part B: IGizmoUiStatePublisher Interface

**File to create:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IGizmoUiStatePublisher.cs`

```csharp
namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    // Abstraction over the DDS writer for GizmoUiState, enabling test injection.
    public interface IGizmoUiStatePublisher
    {
        void Publish(GizmoUiState state);
    }
}
```

**Note:** Place `GizmoUiState` in the `Network` sub-namespace. The interface refers to it directly.
If there's a namespace ambiguity, add a using in the interface file.

### Part C: GizmoSettingsPublisherSystem

**File to create:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/GizmoSettingsPublisherSystem.cs`

```csharp
using System.Text.Json;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Systems
{
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class GizmoSettingsPublisherSystem : IModuleSystem
    {
        private readonly GizmoSettingsRegistry _registry;
        private readonly IGizmoUiStatePublisher? _publisher; // null = local-only, system is no-op
        private bool _firstFrame = true;

        public GizmoSettingsPublisherSystem(GizmoSettingsRegistry registry, IGizmoUiStatePublisher? publisher = null)
        {
            _registry = registry;
            _publisher = publisher;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (_publisher == null) return;

            bool hasEvent = false;
            foreach (var _ in view.ReadEvents<GizmoSettingChangedEvent>())
            {
                hasEvent = true;
                break;
            }

            if (!_firstFrame && !_registry.IsDirty && !hasEvent) return;

            _firstFrame = false;

            // Build JSON of all settings (key → active value).
            using var ms = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                foreach (var (key, active, _) in _registry.EnumerateAll())
                {
                    writer.WritePropertyName(key);
                    WriteSettingValue(writer, active);
                }
                writer.WriteEndObject();
            }
            string json = System.Text.Encoding.UTF8.GetString(ms.ToArray());

            _publisher.Publish(new GizmoUiState { GizmoInstanceId = 0, EditDocumentJson = json });
            _registry.ClearDirty();
        }

        private static void WriteSettingValue(Utf8JsonWriter w, GizmoSettingValue v)
        {
            switch (v.Kind)
            {
                case GizmoSettingKind.Bool:   w.WriteBooleanValue(v.AsBool); break;
                case GizmoSettingKind.Float:  w.WriteNumberValue(v.AsFloat); break;
                case GizmoSettingKind.Int:    w.WriteNumberValue(v.AsInt);   break;
                case GizmoSettingKind.Color:  w.WriteStringValue($"#{v.AsColor.R:X2}{v.AsColor.G:X2}{v.AsColor.B:X2}"); break;
                default:                      w.WriteNullValue();             break;
            }
        }
    }
}
```

**Important notes:**
- `GizmoSettingValue.Kind`, `AsBool`, `AsFloat`, `AsInt`, `AsColor` — verify property names in the actual `GizmoSettingValue.cs` before using them. Adjust if the names differ.
- The `ClearDirty()` method on `GizmoSettingsRegistry` is `internal` — the system is in the same assembly (`Fdp.Toolkits`), so this is accessible.

**Tests** (in `Fdp.Toolkits.Tests`):
- **SC-GZ017-1:** `typeof(GizmoUiState).GetCustomAttribute<DdsTopicAttribute>()?.TopicName == "GizmoUiState"`.
- **SC-GZ017-2:** After `registry.Write(hash, value)` followed by `system.Execute(view, 0)`, a `CapturingPublisher` receives exactly one `GizmoUiState` with `GizmoInstanceId == 0` and non-empty `EditDocumentJson`.
- **SC-GZ017-3:** If `IsDirty == false` and no `GizmoSettingChangedEvent` in the bus and it is not the first frame, `system.Execute(view, 0)` does NOT call `Publish` (0 captures).
- **SC-GZ017-4:** `GizmoUiState` struct has `GizmoInstanceId = 42` and `EditDocumentJson = "{}"` — round-trip (no serialization needed; just assert fields accessible and correct).

For SC-GZ017-3: use a pre-run frame to clear the `_firstFrame` flag, then run a second frame with no changes.

Test helper:
```csharp
private sealed class CapturingPublisher : IGizmoUiStatePublisher
{
    public readonly List<GizmoUiState> Published = new();
    public void Publish(GizmoUiState state) => Published.Add(state);
}
```

---

## TASK-GZ018 — IGCapabilitiesAnnounce DDS Message

**Target project:** `Hrot/Subsystems/Hrot.IG/`

### Part A: IGCapabilitiesAnnounce DDS Topic

**File to create:** `Hrot/Subsystems/Hrot.IG/Gizmos/IGCapabilitiesAnnounce.cs`

```csharp
using CycloneDDS.Schema;
using Fdp.Toolkit.Diagnostics.Gizmos.Primitives;

namespace Hrot.IG.Gizmos
{
    [DdsTopic("IGCapabilitiesAnnounce")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct IGCapabilitiesAnnounce
    {
        [DdsKey] public uint NodeId;
        public PipelineTarget SupportedTargets;
        public ushort SupportedLayerMask;
        public byte SupportedShapes;
        [DdsManaged] public string LayerNamesJson;
    }
}
```

**Verify:** `Hrot.IG.csproj` already references `Fdp.Toolkits` (it does — IG uses `DebugPrimitiveBuffer` etc.). If not, confirm the reference chain is in place before using `PipelineTarget`.

### Part B: IGCapabilitiesPublisherSystem

**File to create:** `Hrot/Subsystems/Hrot.IG/Gizmos/IGCapabilitiesPublisherSystem.cs`

```csharp
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Primitives;
using Hrot.IG.Abstractions;

namespace Hrot.IG.Gizmos
{
    [UpdateInPhase(SystemPhase.Initialization)]
    public sealed class IGCapabilitiesPublisherSystem : IModuleSystem
    {
        private readonly IDdsWriter<IGCapabilitiesAnnounce>? _writer; // null = local-only, no-op
        private readonly uint _nodeId;
        private bool _published;

        public IGCapabilitiesPublisherSystem(uint nodeId, IDdsWriter<IGCapabilitiesAnnounce>? writer = null)
        {
            _nodeId = nodeId;
            _writer = writer;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (_writer == null || _published) return;
            _published = true;

            _writer.Write(new IGCapabilitiesAnnounce
            {
                NodeId             = _nodeId,
                SupportedTargets   = PipelineTarget.Map2D,
                SupportedLayerMask = 0xFFFF,
                SupportedShapes    = 0xFF,
                LayerNamesJson     = "[]",
            });
        }
    }
}
```

**Tests** (in `Hrot.IG.Tests` or a new `Hrot.IG.Tests` if one doesn't exist):
- **SC-GZ018-1:** `typeof(IGCapabilitiesAnnounce).GetCustomAttribute<DdsTopicAttribute>()?.TopicName == "IGCapabilitiesAnnounce"`.
- **SC-GZ018-2:** `Execute(view, 0)` called once publishes exactly one record with `SupportedTargets == PipelineTarget.Map2D` and `SupportedLayerMask == 0xFFFF`.
- **SC-GZ018-3:** `Execute(view, 0)` called twice publishes only once (idempotent due to `_published` flag).

Test helper (same pattern as CapturingWriter in other tests):
```csharp
private sealed class CapturingDdsWriter<T> : IDdsWriter<T>
{
    public readonly List<T> Written = new();
    public void Write(T sample) => Written.Add(sample);
}
```

---

## Developer Insights Section

After completing the batch, **answer these questions in your report:**

1. **Issues encountered:** What build errors or unexpected API mismatches did you hit? How did you resolve them?
2. **Weak points spotted:** Did you find any existing code that is fragile, poorly encapsulated, or likely to break? Record these even if they are out of scope for this batch.
3. **Design decisions made beyond the spec:** Did you make architectural choices not explicitly covered? Justify them.
4. **D-003 wiring result:** Was `DataDrivenGizmoSystem` actually constructed in `Hrot.ClusterRunner`? If not, where is the registration site and what did you wire?

---

## Test-Driven Task Progression (MANDATORY)

Follow this exact workflow for every task:

1. **Write the test first.** Even if it does not compile yet.
2. **Write the minimum implementation to make it compile and pass.**
3. **Run the tests.** Do not move to the next task until all tests for the current task pass.
4. **Record the pass count** in your report per task.

Do not skip a failing test. Do not leave a test with a `// TODO` assertion. Every test must either pass or be explicitly excluded with a documented reason in the report.

---

## Report Format

Write your completion report to `.dev/gizmos-1/reports/BATCH-06-REPORT.md`.

Structure:
```
# BATCH-06 Report — Remote Visualization Foundation (GZ015-GZ018)

## Status: COMPLETE / PARTIAL

## Files Created / Modified
(table)

## Test Results per Task
- D-003: X tests pass
- GZ015: X tests pass
- GZ016: X tests pass
- GZ017: X tests pass
- GZ018: X tests pass

## Design Decisions and Deviations

## Issues Encountered

## Weak Points Spotted
```

---

## Success Criteria Summary

| Task | Gate |
|---|---|
| D-003 | ClusterRunner wires predicate; at least 1 test verifies predicate filters correctly |
| GZ015 | `HasSingleton<GlobalDebugSettings>()` test passes; `HrotComponentIds.GlobalDebugSettings = 185` present |
| GZ016 | `DebugPrimitivesBatch` compiles; DdsTopicAttribute test passes |
| GZ017 | `GizmoSettingsPublisherSystem` publishes on first frame and dirty; skips on clean frame |
| GZ018 | `IGCapabilitiesPublisherSystem` publishes exactly once |
