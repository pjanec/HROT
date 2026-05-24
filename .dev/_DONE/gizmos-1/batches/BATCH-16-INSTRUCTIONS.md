# BATCH-16 Implementation Instructions

**Tasks:** GZ041 (Phase A), GZ042 (Phase B)  
**Agent:** Claude Sonnet 4.6  
**Task details:** `.dev/gizmos-1/TASK-DETAIL.md` sections TASK-GZ041 and TASK-GZ042  
**Tracker:** `.dev/gizmos-1/TASK-TRACKER.md`

---

## MANDATORY READING BEFORE STARTING

1. Read `.dev/gizmos-1/TASK-DETAIL.md` sections for TASK-GZ041 and TASK-GZ042 in full.
2. Read `AGENTS.md` for coding standards.
3. Read `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj` — understand current project structure.
4. Read `FDP/FDP.sln` — understand solution structure.
5. Read `Hrot/Network/Hrot.Network.NED/Hrot.Network.NED.csproj` — note `CycloneDDS.targets` import pattern.
6. Read `FDP/ExtDeps/FastCycloneDds/tools/CycloneDDS.CodeGen/CycloneDDS.targets` — codegen mechanism.

---

## Pre-existing Failures (Do NOT count against your work)
- ~26 tests in `Fdp.Toolkits.Tests`, ~4 in `Hrot.IG.Tests`, ~3 in `Fdp.Presentation.Tests`, ~20 in `Hrot.SimHost.Tests`

---

## OVERVIEW

This batch creates two new projects by extracting types from `Fdp.Toolkits`:

1. **GZ041 — `Fdp.Diagnostics.Contracts`**: A minimal project containing only the Phase 1  
   primitive protocol types. References only `Fdp.Core`. No CycloneDDS dependency.

2. **GZ042 — `Fdp.Diagnostics.Network`**: A project containing DDS schema types.  
   References `Fdp.Diagnostics.Contracts` + CycloneDDS. Imports `CycloneDDS.targets`.

Both projects go under `FDP/Diagnostics/` (new directory).

---

## Phase A — GZ041: Fdp.Diagnostics.Contracts

### A1 — Create the project

**Directory to create:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts/`

**File to create:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Fdp.Diagnostics.Contracts.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <LangVersion>12.0</LangVersion>
    <RootNamespace>Fdp.Diagnostics.Contracts</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Fdp.Toolkits.Tests" />
    <InternalsVisibleTo Include="Fdp.Diagnostics.Contracts.Tests" />
  </ItemGroup>
  <ItemGroup>
    <!-- Only Fdp.Core is allowed — no Fdp.Toolkits, no CycloneDDS -->
    <ProjectReference Include="..\..\Engine\Fdp.Core\Fdp.Core.csproj" />
  </ItemGroup>
</Project>
```

### A2 — Move source files

**IMPORTANT**: Move (not copy) the following files from `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/`  
to `FDP/Diagnostics/Fdp.Diagnostics.Contracts/`.

Keep the same relative directory structure (e.g., `Primitives/` subfolder).

Files to move:
- `Primitives/Rgba32.cs`
- `Primitives/CoordinateSpace.cs`
- `Primitives/SizeMode.cs`
- `Primitives/PickToken.cs`
- `Primitives/PipelineTarget.cs`
- `Primitives/DebugPrimitive.cs`
- `Primitives/DebugPrimitiveShape.cs`
- `Primitives/ScreenAnchor.cs`
- `IDebugDrawBuilder.cs`
- `DebugPrimitiveBuffer.cs`
- `StringInternMap.cs`

**Do NOT change the namespaces** in these files. They must retain their original namespace  
`Fdp.Toolkit.Diagnostics.Gizmos` (and sub-namespaces) to avoid breaking callers.

**Do NOT move** `GizmoProjectorAttribute.cs` — it belongs to the gizmo registry system and stays in `Fdp.Toolkits`.

### A3 — Update Fdp.Toolkits.csproj

Add a project reference so `Fdp.Toolkits` can still use the moved types transitively:

```xml
<!-- Fdp.Diagnostics.Contracts: Phase 1 primitive protocol types -->
<ItemGroup>
  <ProjectReference Include="..\..\Diagnostics\Fdp.Diagnostics.Contracts\Fdp.Diagnostics.Contracts.csproj" />
</ItemGroup>
```

### A4 — Add to FDP.sln

```
cd d:\Work\IOS-IG-SimHost-FDP-2\FDP
dotnet sln FDP.sln add Diagnostics/Fdp.Diagnostics.Contracts/Fdp.Diagnostics.Contracts.csproj
```

### A5 — Build and fix

```
cd d:\Work\IOS-IG-SimHost-FDP-2\FDP
dotnet build FDP.sln --no-incremental -clp:ErrorsOnly
```

Fix all errors. Common issues:
- Files that still `using` from `Fdp.Toolkits` may need namespace adjustment.
- If `IStatefulGizmo.cs` or other files in `Fdp.Toolkits` directly reference types that moved,  
  they'll resolve via the new project reference to `Fdp.Diagnostics.Contracts`.
- Check that `GizmoUndoStack.cs` and `IGizmoUndoRecord.cs` compile (they use `IEntityCommandBuffer`  
  from `Fdp.Core`, not from the moved types — should be fine).

### A6 — Verify GZ041 SC-3 (standalone test)

Create a minimal test project to verify SC-GZ041-3:

**File to create:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/Fdp.Diagnostics.Contracts.Tests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <!-- ONLY Fdp.Diagnostics.Contracts — NO Fdp.Toolkits reference -->
    <ProjectReference Include="..\Fdp.Diagnostics.Contracts\Fdp.Diagnostics.Contracts.csproj" />
  </ItemGroup>
</Project>
```

**Test file:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/ContractsStandaloneTests.cs`

```csharp
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;

namespace Fdp.Diagnostics.Contracts.Tests
{
    public class ContractsStandaloneTests
    {
        // SC-GZ041-3: standalone usage of DebugPrimitiveBuffer without Fdp.Toolkits reference.
        [Fact]
        public void SC_GZ041_3_DebugPrimitiveBuffer_StandaloneUsage()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 64);
            buffer.DrawLine(
                System.Numerics.Vector3.Zero,
                System.Numerics.Vector3.UnitX,
                new Rgba32(255, 0, 0, 255));
            Assert.Equal(1, buffer.GetFrame().Length);
        }
    }
}
```

Add to FDP.sln:
```
dotnet sln FDP.sln add Diagnostics/Fdp.Diagnostics.Contracts.Tests/Fdp.Diagnostics.Contracts.Tests.csproj
```

---

## Phase B — GZ042: Fdp.Diagnostics.Network

**Prerequisite:** Phase A must be complete and building before starting Phase B.

### B1 — Create the project

**File to create:** `FDP/Diagnostics/Fdp.Diagnostics.Network/Fdp.Diagnostics.Network.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <LangVersion>12.0</LangVersion>
    <RootNamespace>Fdp.Diagnostics.Network</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Fdp.Diagnostics.Contracts\Fdp.Diagnostics.Contracts.csproj" />
    <ProjectReference Include="..\..\ExtDeps\FastCycloneDds\src\CycloneDDS.Runtime\CycloneDDS.Runtime.csproj" />
    <ProjectReference Include="..\..\ExtDeps\FastCycloneDds\src\CycloneDDS.Schema\CycloneDDS.Schema.csproj" />
    <ProjectReference Include="..\..\ExtDeps\FastCycloneDds\src\CycloneDDS.Core\CycloneDDS.Core.csproj" />
  </ItemGroup>
  <Import Project="..\..\ExtDeps\FastCycloneDds\tools\CycloneDDS.CodeGen\CycloneDDS.targets" />
</Project>
```

### B2 — Move DDS schema files

Move the following from `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/`  
to `FDP/Diagnostics/Fdp.Diagnostics.Network/`:

- `DebugPrimitivesBatch.cs`
- `GizmoUiState.cs`
- `StringInternBatch.cs`

**Do NOT change their namespaces** — keep `Fdp.Toolkit.Diagnostics.Gizmos.Network`.

Also move the abstract interfaces (for symmetry — they're network abstractions):
- `IDdsReader.cs` → `FDP/Diagnostics/Fdp.Diagnostics.Network/IDdsReader.cs`
- `IDdsWriter.cs` → `FDP/Diagnostics/Fdp.Diagnostics.Network/IDdsWriter.cs`

### B3 — Move GizmoInteraction types from Hrot.Network.NED

Move the following from `Hrot/Network/Hrot.Network.NED/Gizmos/`  
to `FDP/Diagnostics/Fdp.Diagnostics.Network/`:

- `GizmoInteractionEventKind.cs`
- `GizmoInteractionBatch.cs`

**Change their namespaces** from `Hrot.Network.NED.Gizmos` to `Fdp.Toolkit.Diagnostics.Gizmos.Network`.

After namespace change, update all callers in `Hrot.Network.NED/Gizmos/`:
- `GizmoInteractionEgressSystem.cs`: change `using Hrot.Network.NED.Gizmos;` to `using Fdp.Toolkit.Diagnostics.Gizmos.Network;`
- `GizmoInteractionIngressSystem.cs`: same
- `DebugPrimitivesIngressTranslator.cs`: already in `Fdp.Toolkit.Diagnostics.Gizmos.Network` — no change needed

Also update test files that reference the old namespace:
- `Hrot/Network/Hrot.Network.NED.Tests/GizmoInteractionTranslatorTests.cs`
- `Hrot/Network/Hrot.Network.NED.Tests/GizmoIngressTranslatorTests.cs`

### B4 — Update Fdp.Toolkits.csproj

`DebugPrimitivesBatch` was used by `DebugPrimitivesBatchPublisherSystem` in `Fdp.Toolkits`.
Now that it moved to `Fdp.Diagnostics.Network`, `Fdp.Toolkits` needs a reference:

```xml
<!-- Fdp.Diagnostics.Network: DDS schema types for gizmo network protocol -->
<ItemGroup>
  <ProjectReference Include="..\..\Diagnostics\Fdp.Diagnostics.Network\Fdp.Diagnostics.Network.csproj" />
</ItemGroup>
```

**Also remove** the three network schema files from `Fdp.Toolkits` (they moved):
- `Diagnostics/Gizmos/Network/DebugPrimitivesBatch.cs` → DELETED (moved to Fdp.Diagnostics.Network)
- `Diagnostics/Gizmos/Network/GizmoUiState.cs` → DELETED
- `Diagnostics/Gizmos/Network/StringInternBatch.cs` → DELETED
- `Diagnostics/Gizmos/Network/IDdsReader.cs` → DELETED
- `Diagnostics/Gizmos/Network/IDdsWriter.cs` → DELETED

The `Network/` folder in `Fdp.Toolkits` may be empty or have only the systems.

### B5 — Update Hrot.Network.NED.csproj

Add reference to `Fdp.Diagnostics.Network` (for the moved GizmoInteraction types):

```xml
<ProjectReference Include="..\..\..\FDP\Diagnostics\Fdp.Diagnostics.Network\Fdp.Diagnostics.Network.csproj" />
```

**Note:** `Hrot.Network.NED` already imports `CycloneDDS.targets` and references `Fdp.Toolkits`.  
Since `Fdp.Toolkits` now references `Fdp.Diagnostics.Network`, the types are accessible either  
directly or transitively. The direct reference is still recommended for clarity.

### B6 — Add to FDP.sln

```
cd d:\Work\IOS-IG-SimHost-FDP-2\FDP
dotnet sln FDP.sln add Diagnostics/Fdp.Diagnostics.Network/Fdp.Diagnostics.Network.csproj
```

### B7 — Build and fix

```
cd d:\Work\IOS-IG-SimHost-FDP-2\FDP
dotnet build FDP.sln --no-incremental -clp:ErrorsOnly
```

Then full solution:
```
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln --no-incremental -clp:ErrorsOnly
```

Fix all errors. Common issues:
- Namespace changes for `GizmoInteractionBatch` → update all callers
- `DebugPrimitivesBatchPublisherSystem` uses `DebugPrimitivesBatch` — resolves via transitive ref
- Any code in `Fdp.Toolkits.Tests` that directly references the moved types — may need `using` updates

### B8 — CycloneDDS codegen verification

After building, verify the codegen ran for `Fdp.Diagnostics.Network`:
```
Get-ChildItem "FDP/Diagnostics/Fdp.Diagnostics.Network/obj" -Recurse -Filter "*.cs" | Select FullName
```

There should be generated files in `obj\Debug\net8.0\` or `obj\Generated\`.

If `DebugPrimitivesBatch` has an issue with the generated partial (because `DebugPrimitive` is now  
in a different project), try explicitly adding a using directive:
```csharp
// In DebugPrimitivesBatch.cs, ensure the using for DebugPrimitive is present:
using Fdp.Toolkit.Diagnostics.Gizmos;
```

---

## Tests

### SC-GZ041-1 through SC-GZ041-5

SC-GZ041-1: Both `Fdp.Diagnostics.Contracts` AND `Fdp.Toolkits` build without errors → verified by build

SC-GZ041-2: `Fdp.Toolkits` still compiles → verified by build

SC-GZ041-3: Run `Fdp.Diagnostics.Contracts.Tests` standalone:
```
dotnet test FDP\Diagnostics\Fdp.Diagnostics.Contracts.Tests\Fdp.Diagnostics.Contracts.Tests.csproj --logger "console;verbosity=quiet"
```

SC-GZ041-4: All existing tests still pass → verified by running full suite

SC-GZ041-5: `Fdp.Diagnostics.Contracts` is in `FDP.sln` → verified by `dotnet build FDP/FDP.sln`

### SC-GZ042-1 through SC-GZ042-5

SC-GZ042-1/2/3/4: Run NED tests to verify DDS types still work:
```
dotnet test Hrot\Network\Hrot.Network.NED.Tests\Hrot.Network.NED.Tests.csproj --no-build --logger "console;verbosity=quiet"
```

SC-GZ042-5: `Fdp.Diagnostics.Network` is in `FDP.sln` → verified by `dotnet build FDP/FDP.sln`

---

## Full Test Suite Verification

```
dotnet test IOS-IG-SimHost.sln --no-build --logger "console;verbosity=quiet" 2>&1 | Select-String "Passed|Failed|Error" | Select-Object -Last 20
```

Check that no NEW failures appear beyond the pre-existing ones listed above.

---

## Commit Instructions

**Step 1 — FDP submodule (new projects + moved FDP files):**
```
cd d:\Work\IOS-IG-SimHost-FDP-2\FDP
git add -A
git commit -m "GZ041/GZ042: Fdp.Diagnostics.Contracts and Fdp.Diagnostics.Network assemblies"
```

**Step 2 — Root repo (Hrot.Network.NED updates + GizmoInteraction types moved):**
```
cd d:\Work\IOS-IG-SimHost-FDP-2
git add -A
git commit -m "GZ041/GZ042: Update Hrot references for new Diagnostics assemblies"
```

---

## Batch Report

Create `.dev/gizmos-1/reports/BATCH-16-REPORT.md` documenting:
- Files created/modified/deleted (with relative paths)
- Build result
- Test results
- Any deviations (e.g., if GizmoInteractionBatch namespace retained instead of changed)
- CycloneDDS codegen status

Update `.dev/gizmos-1/TASK-TRACKER.md`: mark GZ041 and GZ042 as `[x]` done.

---

## CRITICAL NOTES

1. **Build first, test second.** If the full solution build is clean, then run tests.

2. **Namespace retention for FDP types**: Types from `Fdp.Toolkits` keep namespace  
   `Fdp.Toolkit.Diagnostics.Gizmos` (note: this is the EXISTING namespace, not matching the new  
   project's `RootNamespace`). This is intentional and matches existing code.

3. **GizmoInteractionBatch namespace change**: This is the only type whose namespace CHANGES  
   (from `Hrot.Network.NED.Gizmos` to `Fdp.Toolkit.Diagnostics.Gizmos.Network`). Update all callers.

4. **Transitive references**: Because `Fdp.Toolkits` → `Fdp.Diagnostics.Contracts` and  
   `Fdp.Toolkits` → `Fdp.Diagnostics.Network`, projects that already reference `Fdp.Toolkits`  
   get the new types transitively. No changes needed for most downstream consumers.

5. **Do not change `IOS-IG-SimHost.sln`** (root solution file). Only modify `FDP/FDP.sln`.

6. **If CycloneDDS codegen fails**: The codegen in `Fdp.Diagnostics.Network` may fail if it  
   can't find referenced types from `Fdp.Diagnostics.Contracts`. Try adding  
   `<CycloneDdsDisableCodeGen>true</CycloneDdsDisableCodeGen>` to see if the types compile  
   without generated code. If `DebugPrimitivesBatch` compiles as a simple partial struct  
   (no generated DDS serialization needed for FDP tests), that's acceptable for now.

7. **Test for GZ042 SC-3**: After updating `Hrot.Network.NED.csproj`, ensure `GizmoInteractionEgressSystem`  
   and `GizmoInteractionIngressSystem` can see `GizmoInteractionBatch` in the new namespace.
