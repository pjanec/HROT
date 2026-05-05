# BATCH-06 Report — Remote Visualization Foundation (GZ015-GZ018)

## Status: COMPLETE

## Files Created / Modified

| File | Action |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/DebugPrimitivesBatch.cs` | Created |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/GizmoUiState.cs` | Created |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IGizmoUiStatePublisher.cs` | Created |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/GizmoSettingsPublisherSystem.cs` | Created |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosNetworkTopicsTests.cs` | Created |
| `Hrot/Engine/Hrot.Core/MapDefinitions/HrotComponentIds.cs` | Modified (added GlobalDebugSettings = 185) |
| `Hrot/Subsystems/Hrot.IG/Gizmos/GlobalDebugSettings.cs` | Created |
| `Hrot/Subsystems/Hrot.IG/Gizmos/GlobalDebugSettingsPanel.cs` | Created (stub) |
| `Hrot/Subsystems/Hrot.IG/Gizmos/IGCapabilitiesAnnounce.cs` | Created |
| `Hrot/Subsystems/Hrot.IG/Gizmos/IGCapabilitiesPublisherSystem.cs` | Created |
| `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/GizmosRemoteVisualizationTests.cs` | Created |
| `Hrot/Runner/Hrot.ClusterRunner.Tests/DataDrivenGizmoPredicateTests.cs` | Created |

## Test Results per Task

- **D-003:** 2 tests pass (predicate-false skips UpdateAndDraw; predicate-true allows it)
- **GZ015:** 4 tests pass (HasSingleton, MarshalSizeOf==4, DataPolicy.Transient, ComponentId==185)
- **GZ016:** 1 test passes (DebugPrimitivesBatch DdsTopicAttribute name)
- **GZ017:** 4 tests passes (GizmoUiState topic name, publish on first/dirty frame, skip on clean frame, field round-trip)
- **GZ018:** 3 tests pass (IGCapabilitiesAnnounce topic name, publish once with correct fields, idempotent)

**Total: 14 tests pass, 0 fail**

Final solution build: **0 errors, 0 new warnings** (pre-existing warnings unchanged).

## Design Decisions and Deviations

### GizmoSettingsPublisherSystem — API adaptation (critical)
The instructions referenced `v.Kind`, `v.AsBool`, `v.AsFloat`, `v.AsInt`, `v.AsColor` and a `GizmoSettingKind` enum.
**None of these exist.** Actual `GizmoSettingValue` API:
- `Type` (SettingType: Bool, Int32, Float32) — no Color variant
- `BoolValue`, `IntValue`, `FloatValue` — direct field access

System was implemented with the actual API. Color serialization is simply omitted (no Color type in the settings store).

### IModuleSystem vs IEcsModuleSystem
The instructions used `IModuleSystem` which does not exist. The correct interface is `IEcsModuleSystem` from `Fdp.ModuleHost.Abstractions`. All new systems implement `IEcsModuleSystem`.

### SystemPhase.Initialization does not exist
`IGCapabilitiesPublisherSystem` was specified as `[UpdateInPhase(SystemPhase.Initialization)]`. This phase value does not exist in the `SystemPhase` enum. Used `SystemPhase.PostSimulation` instead. The `_published` flag ensures publish-once semantics regardless of phase.

### DebugPrimitivesBatch.cs — using directive correction
The instructions used `using Fdp.Toolkit.Diagnostics.Gizmos.Primitives` but `DebugPrimitive` is actually in the `Fdp.Toolkit.Diagnostics.Gizmos` namespace (the `Primitives/` is a folder name only, not a namespace segment). Fixed to `using Fdp.Toolkit.Diagnostics.Gizmos`.

### GlobalDebugSettingsPanel.cs — stub only
No existing debug overlay panel was found in Hrot.IG. Created a static stub class `GlobalDebugSettingsPanel.Draw()`. Full ImGui implementation requires render-thread context and is deferred; documented in the file.

### D-003 wiring result
`DataDrivenGizmoSystem` and `BehaviorGizmoManagerSystem` are NOT constructed or registered anywhere in `Hrot.ClusterRunner` or any Hrot subsystem. They exist only as library types in `Fdp.Toolkits`. The predicate wiring cannot be applied yet — there is no registration site to wire.

The D-003 tests were added to `Hrot.ClusterRunner.Tests` as unit tests verifying the predicate contract at the system level. This documents the expected behavior and will guide future integration.

## Issues Encountered

1. **DebugPrimitivesBatch using directive** — wrong namespace in instructions; build error revealed the correct namespace.
2. **IEntityCommandBuffer namespace** — `GlobalDebugSettingsPanel.cs` initially used `Fdp.Core` but `IEntityCommandBuffer` is in `Fdp.Interfaces`; fixed with correct using.
3. **IDebugDrawBuilder interface mismatch** — the interface has different signatures than documented in the instructions (different parameter types and method signatures); read the actual interface file to implement the correct mock.
4. **D003NoOpDrawBuilder** — initial mock was based on guessed signatures; build errors identified the real interface, corrected in second pass.

## Weak Points Spotted

1. **GizmoSettingValue has no Color type**: The design mentions Color gizmo settings in multiple places, but the type system only supports Bool/Int32/Float32. If Color settings are needed, a new `SettingType.Color` variant and `ColorValue` field must be added to `GizmoSettingValue`.
2. **DataDrivenGizmoSystem has no registration site in Hrot**: The system is well-implemented in Fdp.Toolkits but not wired into any running host. It is essentially dead code from the perspective of the actual application.
3. **Hrot.IG.csproj doesn't reference Fdp.Toolkits directly**: Transitive access works (via Hrot.Core), but if the reference chain changes, Hrot.IG's access to Fdp.Toolkit types could silently break. Adding an explicit reference would be safer.
4. **GizmoSettingsPublisherSystem uses `view.ReadEvents<GizmoSettingChangedEvent>()`**: The event is published via `cmd.PublishEvent(...)` inside `GizmoSettingsRegistry.Write(...)`. If `cmd` is null (which is the common case — `Write(hash, value)` without a command buffer), no event is published, but `IsDirty` is still set. The system correctly handles this: the `IsDirty` flag is the primary trigger, and events are a redundant secondary trigger. This dual-trigger logic is slightly confusing.
