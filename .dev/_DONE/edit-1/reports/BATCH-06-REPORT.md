# BATCH-06 Report — Phase 4 Part 2: Complex Editor Adapter + ECS Systems + Rendering Layer

**Batch:** BATCH-06  
**Tasks:** EDIT1-A006, EDIT1-A009, EDIT1-A010, EDIT1-A011, EDIT1-A012  
**Status:** ✅ COMPLETE  

---

## Summary

All five components have been implemented.  57 of 58 tests pass; the single remaining failure
(`SaveScenario_WritesValidJson_WithCorrectHeaderAndEntityCount`) is pre-existing and unrelated to
this batch.  The full solution (`IOS-IG-SimHost.sln`) builds with zero errors.

---

## Changes Made

### New Files

| File | Purpose |
|------|---------|
| `Hrot.Editor/Commands/CenterOnEntityCommand.cs` | Command published when the map should pan to centre on an entity |
| `Hrot.Editor/Commands/SelectEntityCommand.cs` | Command published to programmatically set the selection |
| `Hrot.Editor/Commands/OpenRenameDialogCommand.cs` | Command that triggers the inline rename dialog |
| `Hrot.Map.Common/Components/ZoneMembership.cs` | Managed ECS component recording zone name for obstacle entities |
| `Hrot.Editor/UI/EditorEntityContextMenuHandler.cs` | A006 — implements `IEntityContextMenuHandler` + `IEntityActionController` |
| `Hrot.Editor/Systems/EditorCargoSystem.cs` | A009 — processes `EmbarkEntityCommand` / `DisembarkEntityCommand` |
| `Hrot.Editor/Systems/EditorPerceptionSetupSystem.cs` | A010 — processes `SeedTargetCommand`, writes to `TargetMemory` |
| `Hrot.Editor/Systems/EditorZoneAuthoringSystem.cs` | A011 — spawns zone obstacle entities; updates `ZoneEnvironmentData` singleton |
| `Hrot.Editor/Rendering/PerceptionMapLayer.cs` | A012 — `IMapLayer` that draws target-memory perception links |
| `Hrot.Editor.Tests/UI/ContextMenuHandlerTests.cs` | 5 unit tests for A006 |
| `Hrot.Editor.Tests/Systems/SystemTests.cs` | 8 unit tests for A009 / A010 / A011 / A012 |

### Modified Files

| File | Change |
|------|--------|
| `Hrot.Editor/IEditorLogic.cs` | Added `CenterOnEntity`, `SelectEntity`, `OpenRenameDialog` declarations |
| `Hrot.Editor/EditorApplication.cs` | Implemented the three new `IEditorLogic` methods; added `using Hrot.Editor.Commands;` |
| `Hrot.Editor/Hrot.Editor.csproj` | Added `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` |
| `Hrot.Editor.Tests/Hrot.Editor.Tests.csproj` | Added `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` |
| `Hrot.Map.Definitions/HrotComponentIds.cs` | Added `ZoneMembership = 171` in new zone-authoring block |

---

## Unit Tests Added (13 total)

### A006 — `ContextMenuHandlerTests` (5 tests)

| Test | What it verifies |
|------|-----------------|
| `PopulateMenu_EntityWithEditablePolyline_ContainsEditShapeItem` | `EditablePolyline` present → "Edit Shape" item added |
| `PopulateMenu_EntityWithoutComponents_NoEditShapeOrEditRouteItem` | No polyline or route plan → neither item appears |
| `PopulateMenu_DeadEntity_NoItemsAdded` | `!IsAlive` guard → zero builder calls |
| `DeleteEntity_PublishesDestroyEntityCommand` | `DeleteEntity(42)` → `DestroyEntityCommand { NetworkId = 42 }` on bus |
| `PopulateMenu_EntityWithTargetMemory_IncludesPerceiverCountLabel` | 2 selected perceivers → "Mark Target for 2 Units..." label present |

### A009 — `EditorCargoSystemTests` (3 tests)

| Test | What it verifies |
|------|-----------------|
| `Embark_ValidPassengerAndVehicle_AddsToBuffer` | `EmbarkEntityCommand` → `buffer.Count == 1` |
| `Embark_FullBuffer_DoesNotExceedCapacity` | 8 passengers already in buffer → 9th command ignored |
| `Disembark_AfterEmbark_RemovesIsEmbarkedTag` | Round-trip embark → disembark removes `IsEmbarkedTag` |

### A010 — `EditorPerceptionSetupSystemTests` (2 tests)

| Test | What it verifies |
|------|-----------------|
| `SeedTarget_ValidPerceiverAndTarget_WritesToTargetMemory` | `TargetMemory.EntityIds[0]` matches target `PackedValue` |
| `SeedTarget_DeadPerceiver_SkipsSilently` | Dead perceiver → no exception, memory unchanged |

### A011 — `EditorZoneAuthoringSystemTests` (2 tests)

| Test | What it verifies |
|------|-----------------|
| `SpawnObstacle_PublishCommand_CreatesEntityWithZoneMembership` | Query returns 1 entity with `ZoneMembership.ZoneName` set |
| `UpdateZoneConfig_ValidJsonPath_SetsSingletonRoadNetwork` | `HasSingleton<ZoneEnvironmentData>()` is true after system run |

### A012 — `PerceptionMapLayerTests` (1 test)

| Test | What it verifies |
|------|-----------------|
| `PerceptionMapLayer_ImplementsIMapLayer_NoConstructionException` | Layer constructs without error and implements `IMapLayer` |

---

## Developer Insights (Q1–Q5)

### Q1 — What additional dependencies required pre-work?

Three `IEditorLogic` method declarations and matching `EditorApplication` implementations had to be
created before `EditorEntityContextMenuHandler` could compile:
`CenterOnEntity`, `SelectEntity`, `OpenRenameDialog`.  These were absent from the interface and are
wired to three new command types (`CenterOnEntityCommand`, `SelectEntityCommand`,
`OpenRenameDialogCommand`) in `Hrot.Editor/Commands/`.

The `ZoneMembership` managed component required a `[ComponentId]` attribute backed by a registered
constant before the ECS engine would allow it to be added to an entity.  This constant
(`ZoneMembership = 171`) was added to `HrotComponentIds` rather than `GlobalComponentIds` because
the 160–199 range is the correct project-level application block.

The batch spec stated `IContextMenuBuilder` was missing `AddSeparator()`; inspection showed it was
already present — no change needed there.

`SeedTargetCommand` is in namespace `FDP.Toolkit.Perception.Events` — the `using` directive was
missing from `EditorEntityContextMenuHandler.cs` but was straightforward to add once the type was
located.

### Q2 — Did `ComponentSystem` constructor / override approach match expectations?

Yes in structure; no in one important API detail.  The `[UpdateInPhase(SystemPhase.Input)]` +
`protected override void OnUpdate()` pattern matched exactly.  However the batch spec's
`ConsumeSequence<T>(out T[] array, out int count)` overload **does not exist**.  The real API is:

```csharp
ReadOnlySpan<T> cmds = World.Bus.Consume<T>();   // unmanaged events
IReadOnlyList<T> managed = World.Bus.ConsumeManaged<T>();  // managed events
```

`Consume<T>()` returns a `ReadOnlySpan<T>`; iteration uses `for (int i = 0; i < cmds.Length; i++)` with
`ref readonly var cmd = ref cmds[i]`.  `ConsumeManaged<T>` returns `IReadOnlyList<T>` iterated with
`foreach`.

Similarly `AddManagedComponent<T>` is internal; the public unified API is `AddComponent<T>` which
dispatches to the managed or unmanaged table at runtime based on
`ComponentTypeHelper.IsUnmanaged<T>()`.

### Q3 — What was the exact `ForEach`/query API for `PerceptionMapLayer`?

`World.ForEach<T>((ref T, Entity) => …)` and the spec's `_world.ForEach<T1,T2>(...)` overloads were
not found.  The existing systems use a stored `EntityQuery` built once:

```csharp
_query = _world.Query().With<TargetMemory>().With<SimTransform>().Build();
```

Iteration in `Draw`:

```csharp
foreach (Entity e in _query)
{
    ref readonly var mem = ref _world.GetComponent<TargetMemory>(e);
    ref readonly var xfm = ref _world.GetComponent<SimTransform>(e);
    // draw lines
}
```

### Q4 — Did `Hrot.Editor.csproj` need a `FDP.Toolkit.CarKinem` reference?

No direct reference was needed.  `Hrot.Editor.csproj` already references `Hrot.SimHost`, which
itself references `FDP.Toolkit.CarKinem`; the `ZoneEnvironmentData` and `RoadNetworkLoader` types
are therefore available transitively.  No new `<ProjectReference>` entry was required.

### Q5 — Which ECS types were hardest to find?

**`ActorCapabilityState`** — located in `FDP.Toolkit.Behavior.Contracts`.  The flags `CanMove` and
`CanShoot` are `[Flags]` enum values on `ActorCapabilities`; the component holds a single
`ActorCapabilities Flags` field.  Stripping the flags requires reading via `GetComponentRW` and
clearing with `&= ~(ActorCapabilities.CanMove | ActorCapabilities.CanShoot)`.

**`SimTransform.Position`** — confirmed as `Vector3` (not `Vector2`).  The `X` and `Y` fields map
to east and north respectively, so `xfm.Position.X` / `xfm.Position.Y` are used for 2-D rendering.

**`NetworkIdentity`** — field name is `Value` (type `long`), not `NetworkId`.  The component lives
in `FDP.Toolkit.NetworkSpawning`.

**`TkbIdentity`** — field is `TkbType` (type `long`).  Located in `FDP.Toolkit.Tkb`.

`PhysicsCollider` and `PhysicsConstants` were straightforward — `Radius float` and
`PhysicsConstants.EntityCollisionLayer int` both matched expectations.

---

## Pre-existing Failures (unchanged)

| Test | Reason |
|------|--------|
| `SaveScenario_WritesValidJson_WithCorrectHeaderAndEntityCount` | Pre-existing failure unrelated to BATCH-06; present before this batch started |
