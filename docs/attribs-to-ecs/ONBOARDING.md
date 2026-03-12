# Onboarding: Attributes-to-ECS — Zero-Allocation JSON Entity Patching

Welcome to the **ATTR** workstream. This document orients you on what we are building, where
everything lives in the codebase, and how to get started.

---

## 1. What We Are Building

This workstream upgrades the **entity attribute patch** pipeline across the IOS → IG → SimHost
stack. The change has two complementary goals:

### 1.1 Flexible Wire Format

The current `CreateEntityRequest` DDS message uses a fixed-enum discriminated union
(`List<EntityAttributePayload>`) to carry fine-grained entity property overrides. Adding any new
settable field — even something as simple as `Affiliation` — requires modifying the IDL enum,
regenerating serialisation code, and updating the `EntityAttributeCompiler` switch statement.

We replace this with a single `string InitialAttributesJson` field carrying a JSON object:

```json
{ "Name": "Bravo-1", "Affiliation": "FORCE_FRIENDLY" }
```

This JSON matches the `EntityPropertyPatch` C# class that the IOS already produces and that the
IG already receives as `initialPropertiesJson`. No new serialisation roundtrips are introduced
anywhere; the IG simply forwards the string verbatim to the SimHost.

### 1.2 Zero-Allocation SimHost Processing

The SimHost must process thousands of entity spawning requests per frame without triggering Garbage
Collector (GC) pauses. The existing `EntityAttributeCompiler` violates the project's zero-allocation
mandate by creating a new `List<object>` and `new IgEntityData()` on every call.

We replace it with a `JsonAttributeCompiler` built on:

- **`System.Text.Json.Utf8JsonReader`** — a `ref struct` that scans UTF-8 bytes on the thread stack
  with zero heap allocations.
- **`stackalloc` arrays** — depth tracking, path-hash stack, and array-index stack all live on the
  thread stack.
- **FNV-1a incremental hashing** — transforms JSON property paths into `ulong` keys used to look up
  pre-compiled setter delegates at O(1) cost.
- **Pre-compiled `System.Linq.Expressions` delegates** — native-like setters for both unmanaged
  struct components (`ref T`) and managed class components, compiled once at startup.

---

## 2. Design and Task Documents

| Document | Purpose |
|----------|---------|
| [ATTR-DESIGN.md](./ATTR-DESIGN.md) | Architecture, problem analysis, current vs. target state, phase descriptions, data-flow diagram |
| [ATTR-TASK-DETAIL.md](./ATTR-TASK-DETAIL.md) | One section per task — exact files, code changes, and unit test success conditions |
| [ATTR-TASK-TRACKER.md](./ATTR-TASK-TRACKER.md) | Progress checklist; mark `[x]` when each task is done |

**Read in this order:** `ATTR-DESIGN.md` → `ATTR-TASK-DETAIL.md` → start implementing.

---

## 3. Key Source Locations

### What We Are Changing

| Component | Location | Change |
|-----------|----------|--------|
| DDS wire message | `Bagira.DDS.DataModel/GenericMessages.cs` | Replace `InitialAttributes` list with `InitialAttributesJson` string |
| Entity property POCO | `Bagira.DDS.DataModel/EntityPropertyPatch.cs` | No change — already defines the JSON schema |
| IG creation tool | `Bagira.IG/Tools/CreationTool.cs` | Remove `dtEntityInfo` descriptor; forward JSON verbatim |
| Existing attribute compiler | `Bagira.Map.Common/Replication/Utils/EntityAttributeCompiler.cs` | Superseded by `JsonAttributeCompiler` |
| DescriptorMapper | `Bagira.Map.Common/Replication/Utils/DescriptorMapper.cs` | Phase 6 only: share delegates |
| SimHost spawn system | `Bagira.SimHost/Systems/CreateEntityRequestSystem.cs` | Use `JsonAttributeCompiler` |
| Live update system | `Bagira.Map.Common/Systems/UpdateEntityAttributeRequestSystem.cs` | Use `JsonAttributeCompiler` |
| SimHost app startup | `Bagira.SimHost/SimHostApp.cs` | Build `AttributeCompilerBuilder` and inject compiler |

### New Files to Create

| File | Location | Purpose |
|------|----------|---------|
| `JsonAttributeCompiler.cs` | `Bagira.Map.Common/Replication/Utils/` | Core streaming compiler |
| `IEntityPatchContext.cs` | `Bagira.Map.Common/Replication/Utils/` | Context abstraction + delegate types |
| `AttributeCompilerBuilder.cs` | `Bagira.Map.Common/Replication/Utils/` | Builder / registration API |
| `ListPatchContext.cs` | `Bagira.Map.Common/Replication/Utils/` | Spawn-time context (list baseline) |
| `EcsPatchContext.cs` | `Bagira.Map.Common/Replication/Utils/` | Live-update context (ECS baseline) |

### Tests

| Test project | Key files |
|-------------|-----------|
| `Bagira.Map.Common.Tests` | `EntityAttributeCompilerTests.cs` — update for new compiler; add `JsonAttributeCompilerTests.cs` |
| `Bagira.IG.Tests` | `CreationToolTests.cs` — update descriptor-count assertions |
| `Bagira.SimHost.Tests` | `CreateEntityRequestSystemTests.cs` — add JSON attribute path tests |

---

## 4. Background Reading

Before touching any code, read these:

| Document | Why |
|----------|-----|
| [ATTR-DESIGN.md](./ATTR-DESIGN.md) | Full architectural context for this workstream |
| [CODE-STANDARDS.md](../../.dev-workstream/guides/CODE-STANDARDS.md) | Zero-allocation rules, naming conventions, test requirements |
| [DEV-GUIDE.md](../../.dev-workstream/guides/DEV-GUIDE.md) | How to work in this codebase (batch workflow, review process) |
| `Bagira.DDS.DataModel/EntityPropertyPatch.cs` | The JSON schema the IOS already produces |
| `Bagira.DDS.DataModel/GenericMessages.cs` | Current `CreateEntityRequest` and `EntityAttributePayload` shapes |
| `Bagira.Map.Common/Replication/Utils/EntityAttributeCompiler.cs` | What the new compiler replaces |
| `Bagira.Map.Common/Replication/Utils/DescriptorMapper.cs` | How descriptors become ECS components |
| `Bagira.SimHost/Systems/CreateEntityRequestSystem.cs` | Where the spawn pipeline is orchestrated |
| `Bagira.IG/Tools/CreationTool.cs` | Current IG-side emit logic (becomes a dumb pipe) |

---

## 5. Building and Running Tests

### First-Time Setup

```powershell
# Build native CycloneDDS libraries (required after fresh clone):
.\FDP\ExtDeps\FastCycloneDds\build\native-win.ps1

# Restore packages and build:
dotnet restore IOS-IG-SimHost.sln
dotnet build IOS-IG-SimHost.sln
```

### Running Affected Test Projects

```powershell
# Map.Common compiler tests:
dotnet test Bagira.Map.Common.Tests\Bagira.Map.Common.Tests.csproj --no-build --nologo -v q

# IG tool tests:
dotnet test Bagira.IG.Tests\Bagira.IG.Tests.csproj --no-build --nologo -v q

# SimHost system tests:
dotnet test Bagira.SimHost.Tests\Bagira.SimHost.Tests.csproj --no-build --nologo -v q
```

---

## 6. Phase Summary

| Phase | Tasks | Key Files |
|-------|-------|-----------|
| 1 — DDS API Migration | ATTR-S1T1 | `GenericMessages.cs` |
| 2 — IG Pipe Simplification | ATTR-S2T1 | `CreationTool.cs` |
| 3 — Zero-Allocation Compiler Core | ATTR-S3T1, ATTR-S3T2 | `JsonAttributeCompiler.cs` *(new)* |
| 4 — Pre-Compiled Delegate Registry | ATTR-S4T1, ATTR-S4T2, ATTR-S4T3 | `IEntityPatchContext.cs`, `AttributeCompilerBuilder.cs`, `ListPatchContext.cs`, `EcsPatchContext.cs` *(all new)* |
| 5 — Registration & Integration | ATTR-S5T1, ATTR-S5T2, ATTR-S5T3 | `SimHostApp.cs`, `CreateEntityRequestSystem.cs`, `UpdateEntityAttributeRequestSystem.cs` |
| 6 — Unified Descriptor Routing | ATTR-S6T1, ATTR-S6T2 | `DescriptorMapper.cs` *(optional / advanced)* |

Phases 1–5 are the core delivery. Phase 6 is an optional clean-up that eliminates the remaining
duplicate field-mapping logic between the JSON compiler and `DescriptorMapper`.
