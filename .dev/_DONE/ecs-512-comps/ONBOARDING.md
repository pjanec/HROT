# Onboarding Guide — ECS 512-Component Expansion

Welcome to the `ecs-512-comps` workstream. This document gets you productive in under 30 minutes.

---

## What Is Being Built

The FDP engine's Entity Component System currently supports 256 component types (component IDs
0-255), stored as a 256-bit mask per entity. This workstream doubles that capacity to 512
component types by:

1. Widening the component ID type from `byte` to `int`.
2. Replacing the monolithic 96-byte `EntityHeader` struct with two separate, cache-optimised
   parallel arrays:
   - **Hot**: `BitMask512` (64 bytes = 1 CPU cache line) — only the component mask.
   - **Cold**: `EntityMetadataCold` (128 bytes = 2 CPU cache lines) — everything else.
3. Updating the traversal engine (`EntityQuery`) to check the hot mask first, fetching cold
   metadata only for entities that pass the component filter.
4. Updating the Flight Recorder to serialise the two index arrays as two separate data streams.

The result is 512 component types and approximately 40% faster entity query traversal for
typical workloads.

---

## Planning Artifacts

| File | Purpose |
|------|---------|
| [DESIGN.md](./DESIGN.md) | Architecture, rationale, phase breakdown |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task scope, constraints, and success conditions |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Progress checklist |
| [DEBT-TRACKER.md](./DEBT-TRACKER.md) | Known deferred issues |

---

## Folder Layout

All functional source changes are inside one project:

```
FDP/Engine/Fdp.Core/                  <- all source changes
    ComponentIdAttribute.cs           <- TASK-E001: byte -> int
    GlobalComponentIds.cs             <- TASK-E001: const byte -> const int
    FdpConfig.cs                      <- TASK-E002: MAX_COMPONENT_TYPES, FORMAT_VERSION
    QueryBuilder.cs                   <- TASK-E002: WithComponentId guard
    BitMask512.cs                     <- TASK-E003: new file
    EntityMetadataCold.cs             <- TASK-E004: new file
    EntityHeader.cs                   <- TASK-E005: deleted
    EntityIndex.cs                    <- TASK-E005: full rewrite
    EntityQuery.cs                    <- TASK-E006: BitMask512 + hot-first MoveNext
    EntityRepository.cs               <- TASK-E007: split GetHeader calls
    EntityRepository.Sync.cs          <- TASK-E007: mask methods return BitMask512
    FlightRecorder/RecorderSystem.cs  <- TASK-E008: dual stream
    FlightRecorder/PlaybackSystem.cs  <- TASK-E009: route -1/-2 streams
```

Tests are in `FDP/Engine/Fdp.Core.Tests/`.

Existing files that are **not changed** in this workstream:
- `BitMask256.cs` — still used by `PartDescriptor` (component-parts tracking, unrelated domain).
- `PartDescriptor.cs` — no change needed; it tracks parts-within-a-component, not component IDs.
- All projects outside `Fdp.Core` — they recompile cleanly but need no source changes.

---

## Build and Run

Build the engine:
```
cd FDP
dotnet build FDP.sln -c Debug
```

Run all Fdp.Core tests:
```
cd FDP/Engine/Fdp.Core.Tests
dotnet test
```

Run only ECS-related tests (fast feedback during development):
```
dotnet test --filter "FullyQualifiedName~EntityIndex|FullyQualifiedName~EntityQuery|FullyQualifiedName~BitMask"
```

Run FlightRecorder tests:
```
dotnet test --filter "FullyQualifiedName~FlightRecorder|FullyQualifiedName~Recorder|FullyQualifiedName~Playback"
```

---

## Workflow

Read `.dev-workstream/guides/DEV-GUIDE.md` to understand the batch-based development workflow
used in this project before starting any task.

The short version:
1. Pick the next unchecked task from [TASK-TRACKER.md](./TASK-TRACKER.md).
2. Read its entry in [TASK-DETAIL.md](./TASK-DETAIL.md) fully before writing a line of code.
3. Implement the task and its tests.
4. Verify all success conditions pass.
5. Submit a batch report.

---

## Key Concepts to Understand First

Before writing any code, read these files in this order:

1. `FDP/Engine/Fdp.Core/EntityHeader.cs` — the 96-byte struct being replaced.
2. `FDP/Engine/Fdp.Core/BitMask256.cs` — the 32-byte mask being superseded by `BitMask512`.
3. `FDP/Engine/Fdp.Core/EntityIndex.cs` — the entity lifecycle manager being rewritten.
4. `FDP/Engine/Fdp.Core/EntityQuery.cs` — the traversal engine being updated.
5. `FDP/Engine/Fdp.Core/NativeChunkTable.cs` — the generic unmanaged chunk allocator used by
   `EntityIndex` (the new implementation uses two instances of this).
