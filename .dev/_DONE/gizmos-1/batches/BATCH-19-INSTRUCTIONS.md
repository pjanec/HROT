# BATCH-19: Phase 19 Library Segregation — GizmoMap.Contracts and GizmoMap.Network

**Batch Number:** BATCH-19
**Tasks:** TASK-GZ053, TASK-GZ054
**Phase:** Phase 19 (Library Segregation — Extract GizmoMap to ExtDeps), part 1
**Estimated Effort:** 10-14 hours
**Priority:** HIGH — establishes the dependency-free assembly boundary required by GZ055/GZ056
**Dependencies:** BATCH-18 complete (DebugPrimitive layout stable, EntityAttributeSchema added)

---

## Mandatory Reading (IN ORDER)

1. **Task Definitions:** `.dev/gizmos-1/TASK-DETAIL.md` — sections TASK-GZ053 and TASK-GZ054
2. **Design Document:** `.dev/gizmos-1/DESIGN.md`
3. **Coding Standards:** `AGENTS.md`
4. **Previous Review:** `.dev/gizmos-1/reviews/BATCH-18-REVIEW.md`
5. **Tracker:** `.dev/gizmos-1/TASK-TRACKER.md`

## Source Code Locations (READ BEFORE MODIFYING)

- **Fdp.Diagnostics.Contracts:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts/`
- **Fdp.Diagnostics.Network:** `FDP/Diagnostics/Fdp.Diagnostics.Network/`
- **Hrot.Network.NED (DDS topics):** `Hrot/Network/Hrot.Network.NED/`
- **Target output:** `ExtDeps/GizmoMap/GizmoMap.Contracts/`, `ExtDeps/GizmoMap/GizmoMap.Network/`

## Build and Test Commands

```
dotnet build IOS-IG-SimHost.sln --no-incremental -clp:ErrorsOnly
dotnet build ExtDeps/GizmoMap/GizmoMap.Contracts/GizmoMap.Contracts.csproj
dotnet build ExtDeps/GizmoMap/GizmoMap.Network/GizmoMap.Network.csproj
dotnet test FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/Fdp.Diagnostics.Contracts.Tests.csproj
dotnet test Hrot/Network/Hrot.Network.NED.Tests/Hrot.Network.NED.Tests.csproj
```

## Pre-existing Failures (Do NOT count against your work)
- ~26 in Fdp.Toolkits.Tests (non-gizmo areas)
- ~4 in Hrot.IG.Tests
- ~3 in Fdp.Presentation.Tests
- ~20 in Hrot.SimHost.Tests

---

## Context

Phase 19 extracts the GizmoMap framework into self-contained assemblies in `ExtDeps/GizmoMap/`.
The goal is to decouple the presentation layer from the FDP/HROT simulation engine so that
external tools (standalone map viewers, scenario editors, CI validators) can consume GizmoMap
without taking a dependency on the simulation engine.

**Critical assembly boundary rule (HARD CONSTRAINT throughout this entire phase):**

The GizmoMap assemblies must NEVER contain:
- `Entity`, `ISimulationView`, `BitMask256`, `ComponentTypeRegistry`
- `IEcsModuleSystem`, `DataDrivenGizmoSystem`, `StatelessGizmoSystem`
- `IStatefulGizmo`, `IGizmoDefinition`, `IStatelessGizmo`, `IGizmoVisibilityPolicy`
- Any reference to `Fdp.Core`, `Fdp.ModuleHost`, or any `Hrot.*` assembly

This batch covers two tasks:
- **GZ053**: Create `GizmoMap.Contracts` — zero BCL-only dependency assembly
- **GZ054**: Create `GizmoMap.Network` — DDS topic structs + transport adapters

---

## Mandatory Workflow

Complete tasks in sequence. Build and verify before proceeding:

1. **GZ053** → create assembly → migrate types → ensure backward compat → standalone build → tests
2. **GZ054** → create assembly → migrate DDS topics → create transport adapters → standalone build → tests

Run `dotnet build IOS-IG-SimHost.sln --no-incremental -clp:ErrorsOnly` after EACH task.

---

## Task 1: GZ053 — Create GizmoMap.Contracts Assembly

**Task Definition:** Read TASK-GZ053 in `.dev/gizmos-1/TASK-DETAIL.md`

**Strategy: COPY, not MOVE.** Do NOT remove types from `Fdp.Diagnostics.Contracts`. Instead:
1. Create the new assembly with all required types
2. Make `Fdp.Diagnostics.Contracts` forward to `GizmoMap.Contracts` via `extern alias` or
   type aliases so existing code compiles without changes

This avoids breaking hundreds of callsites across the solution.

### Step 1: Create the project

Create `ExtDeps/GizmoMap/GizmoMap.Contracts/GizmoMap.Contracts.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;netstandard2.1</TargetFrameworks>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <!-- NO ProjectReferences — this assembly is self-contained -->
</Project>
```

Add `ExtDeps/GizmoMap/GizmoMap.Contracts/GizmoMap.Contracts.csproj` to `IOS-IG-SimHost.sln`.

### Step 2: Copy types into GizmoMap.Contracts

Read the existing files in `FDP/Diagnostics/Fdp.Diagnostics.Contracts/` and copy these types
(with the SAME namespace initially — `Fdp.Toolkit.Diagnostics.Gizmos`):

| Type | Source File |
|------|-------------|
| `Rgba32` | `Primitives/Rgba32.cs` |
| `DebugPrimitive` | `Primitives/DebugPrimitive.cs` |
| `DebugPrimitiveShape` | `Primitives/DebugPrimitiveShape.cs` |
| `CoordinateSpace` | `Primitives/CoordinateSpace.cs` |
| `PipelineTarget` | `Primitives/PipelineTarget.cs` |
| `SizeMode` | `Primitives/SizeMode.cs` |
| `ScreenAnchor` | `Primitives/ScreenAnchor.cs` |
| `FixedString32` | `Primitives/FixedString32.cs` |
| `DebugPrimitiveBuffer` | `Primitives/DebugPrimitiveBuffer.cs` |
| `IDebugDrawBuilder` | `Abstractions/IDebugDrawBuilder.cs` |
| `StringInternMap` | (find by searching `grep -r "class StringInternMap"`) |

Also add the new types described in TASK-GZ053:

**`GizmoPickToken`** (new file `Sources/GizmoPickToken.cs`):
```csharp
namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    public struct GizmoPickToken
    {
        public long  AnchorId;      // NetworkId / semantic object id (0 = invalid)
        public uint  SubElementId;  // gizmo sub-element index within the anchored entity
        public uint  StreamId;      // publisher stream discriminator (for multi-SimHost clusters)
        public bool  IsValid => AnchorId != 0;
    }
}
```

**`IGizmoSource`** (new file `Sources/IGizmoSource.cs`):
```csharp
namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    public interface IGizmoSource
    {
        // Called once per frame; emit primitives into 'draw'.
        void Emit(float deltaTime, IDebugDrawBuilder draw);
    }
}
```

### Step 3: Ensure `Fdp.Diagnostics.Contracts` builds without modification

`Fdp.Diagnostics.Contracts` currently defines these types in its own files. To maintain backward
compatibility WITHOUT modifying the original files, simply do NOT modify the existing assembly.
The new `GizmoMap.Contracts` is an ADDITIONAL assembly, not a replacement yet.

After this task, both assemblies define the same types. `Fdp.Diagnostics.Contracts` stays as-is.
GZ055 (next batch) will update `Fdp.Presentation` to reference `GizmoMap.Contracts` directly.

### Step 4: Test

**Test file:** Create `ExtDeps/GizmoMap/GizmoMap.Contracts.Tests/GizmoContractsTests.cs`
(in a new test project `GizmoMap.Contracts.Tests.csproj` that references ONLY `GizmoMap.Contracts`)

**Required tests (SC-GZ053):**
- SC-GZ053-1: `GizmoMap.Contracts.csproj` references no FDP or HROT project — verified by `dotnet build --no-incremental` succeeding standalone (no Fdp.Core, no Hrot.*) 
- SC-GZ053-2: `Marshal.SizeOf<DebugPrimitive>() == 64` from a test that references ONLY `GizmoMap.Contracts` (no FDP assemblies)
- SC-GZ053-3: `GizmoPickToken { AnchorId = 42L, SubElementId = 7u }.IsValid == true`
- SC-GZ053-4: `GizmoPickToken { AnchorId = 0L }.IsValid == false`
- SC-GZ053-5: All `DebugPrimitiveShape` enum values (0-10) are accessible — verify `SemanticShape`, `MilStd2525`, `SpatialAnchor` exist
- SC-GZ053-6: `new IGizmoSource` — verify the interface is accessible (create a mock implementation in the test and call Emit)

---

## Task 2: GZ054 — Create GizmoMap.Network Assembly

**Task Definition:** Read TASK-GZ054 in `.dev/gizmos-1/TASK-DETAIL.md`

**Strategy:** Copy DDS topic struct definitions from `Fdp.Diagnostics.Network` and
`Hrot.Network.NED` into the new assembly. Create new stateless transport adapter classes.
Do NOT move (delete) any types from the existing assemblies — backward compat is required.

### Step 1: Create the project

Create `ExtDeps/GizmoMap/GizmoMap.Network/GizmoMap.Network.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0</TargetFrameworks>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\GizmoMap.Contracts\GizmoMap.Contracts.csproj" />
  </ItemGroup>
  <!-- Also reference CycloneDDS binding used by the solution -->
</Project>
```

To reference CycloneDDS, look at how `Fdp.Diagnostics.Network.csproj` references it and use the
same `<PackageReference>` or `<ProjectReference>` pattern.

Add the project to `IOS-IG-SimHost.sln`.

### Step 2: Copy DDS topic struct definitions

Read the existing DDS topic files and copy these types to `GizmoMap.Network` (in the same or
a new namespace — e.g., `GizmoMap.Network`):

| Type | Source |
|------|--------|
| `DebugPrimitivesBatch` | `FDP/Diagnostics/Fdp.Diagnostics.Network/DebugPrimitivesBatch.cs` |
| `GizmoInteractionBatch` | `FDP/Diagnostics/Fdp.Diagnostics.Network/GizmoInteractionBatch.cs` |
| `GizmoUiState` | search: `grep -r "struct GizmoUiState" FDP/ Hrot/` |
| `StringInternBatch` | search: `grep -r "struct StringInternBatch" FDP/ Hrot/` |
| `EntityAttributeSchema` | `Hrot/Network/Hrot.Network.NED/GenericMessages.cs` (added in GZ052) |

When copying:
- Update field types from `Fdp.Toolkit.Diagnostics.Gizmos.*` to their equivalents in
  `GizmoMap.Contracts` (same namespace, so this may be a no-op if namespace is the same)
- Keep DDS attributes (`[DdsTopic]`, `[DdsManaged]`, `[DdsKey]`, `[DdsQos]`) identical
- The `CoordinateSpace` field in `GizmoInteractionBatch` (added in GZ047) must be present

### Step 3: Create stateless transport adapters

Create `GizmoMap.Network/Transport/` with these new classes as described in TASK-GZ054:

**`DdsDebugPrimitivePublisher`** (wraps `IDdsWriter<DebugPrimitivesBatch>`):
- Takes a `DebugPrimitiveBuffer` and an `IDdsWriter<DebugPrimitivesBatch>`
- `Publish(DebugPrimitiveBuffer buffer)` method: packs primitives into `DebugPrimitivesBatch` and writes
- No ECS dependencies; pure data transformation

**`DdsDebugPrimitiveSubscriber`** (wraps `IDdsReader<DebugPrimitivesBatch>`):
- `PollAndApply(DebugPrimitiveBuffer target)` method: reads from DDS reader, unpacks into target buffer
- No ECS dependencies

**`DdsGizmoInteractionPublisher`** (wraps `IDdsWriter<GizmoInteractionBatch>`):
- `Publish(GizmoPickToken token, CoordinateSpace space, Vector3 worldPos, GizmoInteractionEventKind kind)` method

**`DdsGizmoInteractionSubscriber`** (wraps `IDdsReader<GizmoInteractionBatch>`):
- `PollAndRead()` returns `GizmoInteractionBatch?` from the DDS reader

### Step 4: Ensure backward compatibility

`Fdp.Diagnostics.Network` still defines `DebugPrimitivesBatch`, `GizmoInteractionBatch`, etc.
Do NOT remove them. The new `GizmoMap.Network` versions are additional copies. The FDP solution
continues to use the existing `Fdp.Diagnostics.Network` types.

### Step 5: Test

**Test file:** Create `ExtDeps/GizmoMap/GizmoMap.Network.Tests/GizmoNetworkTests.cs`
(test project `GizmoMap.Network.Tests.csproj` that references ONLY `GizmoMap.Contracts` and
`GizmoMap.Network`, no FDP assemblies)

**Required tests (SC-GZ054):**
- SC-GZ054-1: `GizmoMap.Network.csproj` references only `GizmoMap.Contracts` and CycloneDDS (no Fdp.Core, Hrot.*) — standalone build passes
- SC-GZ054-2: `DebugPrimitivesBatch` struct in `GizmoMap.Network` has the same field count and types as the original in `Fdp.Diagnostics.Network` (verified by reflection: both have the same public field names)
- SC-GZ054-3: `EntityAttributeSchema` in `GizmoMap.Network` has `NodeId` (int) and `SchemaJson` (string)
- SC-GZ054-4: `GizmoMap.Network` does NOT contain any type named `IEcsModuleSystem` or any type implementing it (reflection check: `assembly.GetTypes().Any(t => t.GetInterface("IEcsModuleSystem") != null) == false`)
- SC-GZ054-5: `DdsDebugPrimitivePublisher` constructor accepts an `IDdsWriter<DebugPrimitivesBatch>` parameter and does not throw when created

---

## Quality Standards

**ASSEMBLY BOUNDARY (NON-NEGOTIABLE):**
- `GizmoMap.Contracts`: ZERO project references to FDP/HROT assemblies. Build must succeed standalone.
- `GizmoMap.Network`: References ONLY `GizmoMap.Contracts` + CycloneDDS. Build must succeed standalone.
- If you find it impossible to copy a type without pulling in FDP/HROT dependencies, do NOT include that type in the GizmoMap assembly. Flag it in your Q&A section of the report.

**TEST QUALITY:**
- Tests for GZ053/GZ054 must run from test projects that reference ONLY the GizmoMap assemblies
- NOT ACCEPTABLE: Tests that reference `Fdp.Core` or `Hrot.*` assemblies
- REQUIRED: Tests that verify the assembly boundary itself (standalone build success)

**BACKWARD COMPATIBILITY:**
- `dotnet build IOS-IG-SimHost.sln --no-incremental` must still succeed with 0 errors
- `dotnet build FDP/FDP.sln` must still succeed with 0 errors
- Do NOT delete or rename any type in `Fdp.Diagnostics.Contracts` or `Fdp.Diagnostics.Network`

---

## Success Criteria

This batch is DONE when:
- [ ] GZ053: `ExtDeps/GizmoMap/GizmoMap.Contracts/` exists; builds standalone with 0 errors; 6 tests pass from a test project with no FDP/HROT references
- [ ] GZ054: `ExtDeps/GizmoMap/GizmoMap.Network/` exists; builds standalone with 0 errors; 5 tests pass
- [ ] Full solution: `dotnet build IOS-IG-SimHost.sln --no-incremental` → 0 errors
- [ ] All existing tests still pass (no regressions)
- [ ] TASK-TRACKER.md updated (GZ053, GZ054 marked done)
- [ ] Report submitted

---

## Report Submission

**Submit your report to:** `.dev/gizmos-1/reports/BATCH-19-REPORT.md`

**If you have questions:** `.dev/gizmos-1/questions/BATCH-19-QUESTIONS.md`

## Developer Insights (required in report)

**Q1:** Which types from `Fdp.Diagnostics.Contracts` required the most effort to copy cleanly into `GizmoMap.Contracts` without pulling in FDP dependencies?

**Q2:** Did any type depend on `Fdp.Core` types (e.g., `Entity`)? How did you handle that?

**Q3:** What CycloneDDS binding package/project reference did you use for `GizmoMap.Network`?

**Q4:** Were there any types in `Fdp.Diagnostics.Network` that you could NOT copy cleanly due to FDP dependencies? List them.

**Q5:** Suggested commit message.
