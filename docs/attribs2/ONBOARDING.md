# Onboarding: ATTR2 — Binary Attribute Pipeline

Welcome to the **ATTR2** workstream. This document orients you on what we are building, where
everything lives in the codebase, and how to get started.

---

## 1. What We Are Building

This workstream upgrades the entity attribute pipeline from JSON-over-the-wire to
**binary attribute records**, completing the evolution started by the ATTR workstream.

### 1.1 Background (ATTR workstream)

The ATTR workstream (now complete) replaced the old fixed-enum `EntityAttributePayload` wire
format with a `string InitialAttributesJson` / `string AttributePatchJson` field carried inside
`CreateEntityRequest` and `UpdateEntityAttributeRequest`.  On the SimHost these strings are
processed by `JsonAttributeCompiler` — a zero-allocation, FNV-1a–hashed delegate compiler.

### 1.2 ATTR2: What Changes

The SimHost still receives and parses UTF-8 JSON strings on the hot path.  ATTR2 eliminates that
by pushing all JSON parsing to the **client side** (the IG / IOS placement tool).

The new pipeline:

1. **`JsonToRecordCompiler` (Edge Compiler)** — runs in the IG before the DDS send.
   Converts any JSON attribute patch (flat or nested) to a list of
   `AttributeRecord { ushort AttributeId, short SubIndex1, short SubIndex2, AttributeValueUnion Value }`
   binary structs.

2. **`AttributeRecord` / `AttributeValueUnion` (wire types)** — new fields in
   `CreateEntityRequest.InitialAttributeRecords` and
   `UpdateEntityAttributeRequest.AttributeRecords`.

3. **`BinaryInterpreter` (Core Interpreter)** — runs in the SimHost.  Dispatches records via an
   O(1) array lookup (AttributeId is the array index) to domain-installed handler delegates.
   Handles authority checks, attribute grouping (Lat/Lon/Alt scratchpad), and SmartEgress dirty
   marks — all without a single JSON character on the SimHost side.

4. **Domain Installers** — `EntityDataAttributeInstaller` and `SimTransformAttributeInstaller`
   (in `Bagira.SimHost`) wire the SimHost-specific ECS components into the generic interpreter
   via `IBinaryAttributeInstaller`.

The existing JSON pipeline **is retained unchanged** for backward compatibility.  New code uses
the binary path; old code continues to work via the JSON fallback.

---

## 2. Planning Artifacts

| Document | Location | Purpose |
|----------|----------|---------|
| Design | [docs/attribs2/ATTR2-DESIGN.md](./ATTR2-DESIGN.md) | Architecture, phases, decisions |
| Task Detail | [docs/attribs2/ATTR2-TASK-DETAIL.md](./ATTR2-TASK-DETAIL.md) | Per-task scope, constraints, success conditions |
| Task Tracker | [docs/attribs2/ATTR2-TASK-TRACKER.md](./ATTR2-TASK-TRACKER.md) | Checklist with progress status |
| Predecessor Design | [docs/attribs-to-ecs/ATTR-DESIGN.md](../attribs-to-ecs/ATTR-DESIGN.md) | The ATTR workstream this builds on |

---

## 3. Folder Layout

### Generic toolkit — where the new machinery lives

```
FDP/Toolkits/FDP.Toolkit.Replication/Patching/
    AttributeCompilerBuilder.cs          ← existing (ATTR workstream, keep unchanged)
    JsonAttributeCompiler.cs             ← existing (ATTR workstream, keep unchanged)
    IEntityPatchContext.cs               ← existing (shared by both pipelines)
    ListPatchContext.cs                  ← existing (shared by both pipelines)
    EcsPatchContext.cs                   ← existing (shared by both pipelines)
    AttributeIds.cs                      ← NEW (P1T2)
    AttributeValueUnion.cs               ← NEW (P1T1, or part of GenericMessages.cs)
    JsonToRecordCompilerBuilder.cs       ← NEW (P2T1)
    JsonToRecordCompiler.cs              ← NEW (P2T1)
    IBinaryAttributeInstaller.cs         ← NEW (P3T1)
    BinaryPatchContext.cs                ← NEW (P3T1)
    BinaryInterpreterBuilder.cs          ← NEW (P3T1)
    BinaryInterpreter.cs                 ← NEW (P3T1)
```

### DDS wire types

```
Bagira.DDS.DataModel/
    GenericMessages.cs      ← add AttributeValueUnion, AttributeRecord, new list fields (P1T1, P1T3)
```

### SimHost domain code

```
Bagira.SimHost/
    AttributeCompilerFactory.cs          ← extend with BuildEdgeCompiler / BuildBinaryInterpreter (P2T2, P4T3)
    Installers/
        EntityDataAttributeInstaller.cs  ← NEW (P4T1)
        SimTransformAttributeInstaller.cs← NEW (P4T2)
    Systems/
        CreateEntityRequestSystem.cs     ← binary branch (P5T1)
```

### Map / Shared systems

```
Bagira.Map.Common/
    Systems/
        UpdateEntityAttributeRequestSystem.cs ← binary branch (P5T2)
```

### IG client

```
Bagira.IG/
    Tools/
        CreationTool.cs                  ← inject EdgeCompiler (P6T1)
```

### Tests

```
Bagira.SimHost.Tests/            ← system-level integration tests
Bagira.Map.Common.Tests/         ← UpdateEntityAttributeRequest tests
Bagira.IG.Tests/                 ← CreationTool tests
FDP.Toolkit.Replication          ← (optionally) unit tests for compilers/interpreter
```

---

## 4. Build & Run Tests

### Build everything

```powershell
dotnet build IOS-IG-SimHost.sln
```

### Run test suites relevant to this workstream

```powershell
# Unit tests for the data model
dotnet test "Bagira.DDS.DataModel.Tests/Bagira.DDS.DataModel.Tests.csproj" --no-build --nologo

# SimHost system tests (CreateEntityRequestSystem, AttributeCompilerFactory)
dotnet test "Bagira.SimHost.Tests/Bagira.SimHost.Tests.csproj" --no-build --nologo

# Map common tests (UpdateEntityAttributeRequestSystem)
dotnet test "Bagira.Map.Common.Tests/Bagira.Map.Common.Tests.csproj" --no-build --nologo

# IG tests (CreationTool)
dotnet test "Bagira.IG.Tests/Bagira.IG.Tests.csproj" --no-build --nologo
```

### Run a specific test class

```powershell
dotnet test "Bagira.SimHost.Tests/Bagira.SimHost.Tests.csproj" \
    --no-build --nologo \
    --filter "FullyQualifiedName~BinaryInterpreterTests"
```

---

## 5. Workflow

Before starting implementation, read the batch-based development workflow guide:

**[`.dev-workstream/guides/DEV-GUIDE.md`](../../.dev-workstream/guides/DEV-GUIDE.md)**

It defines how tasks are grouped into batches, how to write batch reports, and how the review
cycle works.  All implementation work in this workstream follows that workflow.

Key points:
- Work one batch at a time; do not multi-task across phases.
- Each batch ends with a written report against the success conditions in
  [ATTR2-TASK-DETAIL.md](./ATTR2-TASK-DETAIL.md).
- Do not modify existing passing tests unless the task explicitly requires it.
- Zero-allocation constraints are verified in tests (allocation-counting helpers are already
  present in the existing test projects — see `Bagira.SimHost.Tests/AttributeCompilerFactoryTests.cs`
  for examples).
