# BATCH-04 Instructions

**Batch:** BATCH-04  
**Tasks:** PACK2-E002, PACK2-E003  
**Estimated effort:** ~8–10h  
**Prerequisites:** BATCH-03 merged (Hrot.ScenarioEditor project scaffolded at E001 ✅)

---

## Overview

Move the core interaction tools (E002) and visual rendering layers + adapters (E003) from `Hrot.IG`
into `Hrot.ScenarioEditor`. After this batch:

- `Hrot.IG` becomes a **host-specific composition root** that references `Hrot.ScenarioEditor` for the
  shared tool and rendering logic.
- `Hrot.ScenarioEditor` contains the reusable, DDS-free tool and rendering code.
- All `Hrot.IG.Tests` must continue to pass (regression requirement for both tasks).

> ⚠️ **CRITICAL dependency note:** `SelectionState` (in `Hrot.IG.Components`) is used by several
> tools and render systems being moved. You must move it to `Hrot.Map.Common/Components/` (in the
> existing `Hrot.IG.Components` namespace) before moving the files that depend on it. Do this first.

---

## Context You Must Read First

1. **Task definitions:** `.dev/packs-2/TASK-DETAIL.md` — read §PACK2-E002 and §PACK2-E003 in full.
2. **Design:** `.dev/packs-2/DESIGN.md` — read §2.B and §2.C.
3. **Existing E001 scaffold:** `Hrot.ScenarioEditor/Hrot.ScenarioEditor.csproj` and
   `Hrot.ScenarioEditor/ScenarioEditorModule.cs` — both exist from BATCH-03.
4. **Component precedent:** `Hrot.Map.Common/Components/EditablePolyline.cs` — shows the established
   pattern for the component-relocation approach (type lives in Map.Common but uses `Hrot.IG.Components`
   namespace, and `Hrot.IG/Components/EditablePolyline.cs` is a comment-only file pointing there).
5. **Full list of files to move:** see §Task 1 and §Task 2 below.

---

## Prerequisite: Move `SelectionState` to `Hrot.Map.Common`

Before doing anything else, perform this step.

`SelectionState` is currently at `Hrot.IG/Components/SelectionState.cs`. It is used by:
- `StandardInteractionTool.cs` (moves to ScenarioEditor)
- `SelectionRenderSystem.cs` (moves to ScenarioEditor)
- `MissionRenderLayer.cs` (moves to ScenarioEditor)
- `SstVisualizerAdapter.cs` (moves to ScenarioEditor)
- `EntityInspectorState.cs` (stays in Hrot.IG — will still work via Map.Common reference)

**Move steps:**

1. Copy `Hrot.IG/Components/SelectionState.cs` to `Hrot.Map.Common/Components/SelectionState.cs`.
   - Keep the `namespace Hrot.IG.Components;` declaration (do NOT rename to `Hrot.Map.Common.Components`).
   - This matches the `EditablePolyline.cs` pattern exactly.

2. Replace `Hrot.IG/Components/SelectionState.cs` with a comment-only stub:
   ```csharp
   // SelectionState has been moved to Hrot.Map.Common\Components\SelectionState.cs
   // so that Hrot.ScenarioEditor can depend on it without a circular reference.
   // The type remains in the Hrot.IG.Components namespace and is accessible here
   // via the Hrot.Map.Common project reference.
   ```

3. Run `dotnet build IOS-IG-SimHost.sln --no-incremental` to confirm the move compiled cleanly
   before proceeding.

**Note:** `ForceId` is already in `Hrot.Map.Common/Components/ForceId.cs`. No action needed.  
`EditablePolyline` and `MapOverlayStyle` are also already in Map.Common. No action needed for those.

---

## Task 1 — Migrate Core Interaction Tools (PACK2-E002)

**Task Definition:** [TASK-DETAIL.md §PACK2-E002](../TASK-DETAIL.md#pack2-e002--migrate-core-interaction-tools-into-hrotscenarioeditor)

### 1.1 — Update `Hrot.ScenarioEditor.csproj`

Add to `Hrot.ScenarioEditor/Hrot.ScenarioEditor.csproj`:
```xml
<PackageReference Include="Raylib-cs" Version="7.0.2" />
<PackageReference Include="rlImgui-cs" Version="3.2.0" />
<PackageReference Include="NLog" Version="5.2.8" />
```

Also check if `FDP.Toolkit.ImGui` is needed for any tool — if so add its ProjectReference too.
Check `Hrot.IG/Hrot.IG.csproj` for the exact version strings of the NuGet packages.

### 1.2 — Move the 10 tool files

Move (not copy) each tool file from `Hrot.IG/Tools/` to `Hrot.ScenarioEditor/Tools/`:

| Source | Destination |
|--------|-------------|
| `Hrot.IG/Tools/CreationTool.cs` | `Hrot.ScenarioEditor/Tools/CreationTool.cs` |
| `Hrot.IG/Tools/CreationToolConstants.cs` | `Hrot.ScenarioEditor/Tools/CreationToolConstants.cs` |
| `Hrot.IG/Tools/EditTool.cs` | `Hrot.ScenarioEditor/Tools/EditTool.cs` |
| `Hrot.IG/Tools/EditToolConstants.cs` | `Hrot.ScenarioEditor/Tools/EditToolConstants.cs` |
| `Hrot.IG/Tools/RouteEditTool.cs` | `Hrot.ScenarioEditor/Tools/RouteEditTool.cs` |
| `Hrot.IG/Tools/RouteEditToolConstants.cs` | `Hrot.ScenarioEditor/Tools/RouteEditToolConstants.cs` |
| `Hrot.IG/Tools/MeasureTool.cs` | `Hrot.ScenarioEditor/Tools/MeasureTool.cs` |
| `Hrot.IG/Tools/MeasureToolConstants.cs` | `Hrot.ScenarioEditor/Tools/MeasureToolConstants.cs` |
| `Hrot.IG/Tools/StandardInteractionTool.cs` | `Hrot.ScenarioEditor/Tools/StandardInteractionTool.cs` |
| `Hrot.IG/Tools/StandardInteractionToolConstants.cs` | `Hrot.ScenarioEditor/Tools/StandardInteractionToolConstants.cs` |

### 1.3 — Update namespaces in moved tool files

In each moved file, change:
```
namespace Hrot.IG.Tools;
```
to:
```
namespace Hrot.ScenarioEditor.Tools;
```

> **Note:** Do NOT change `using Hrot.IG.Components;` — that namespace is still valid (types are
> in `Hrot.Map.Common` which ScenarioEditor already references).

### 1.4 — Update `Hrot.IG.csproj` to reference ScenarioEditor

Add to `Hrot.IG/Hrot.IG.csproj`:
```xml
<ProjectReference Include="..\Hrot.ScenarioEditor\Hrot.ScenarioEditor.csproj" />
```

### 1.5 — Update all consumers of moved types in `Hrot.IG`

Search for `using Hrot.IG.Tools;` (and fully-qualified `Hrot.IG.Tools.*` references) in all
`Hrot.IG/*.cs` and `Hrot.IG/**/*.cs` files. Add `using Hrot.ScenarioEditor.Tools;` and remove
the old using where needed, OR add `global using` in a file if it simplifies the update.

The main consumer is `IgApplication.cs` which wires all tools. Run a search:
```
Select-String -Path "Hrot.IG/**/*.cs" -Pattern "Hrot\.IG\.Tools|using.*\.Tools;" -Recurse
```

### 1.6 — Register tools in `ScenarioEditorModule.RegisterSystems`

The task definition says "Register the tools in `ScenarioEditorModule.RegisterSystems` where
applicable." For this batch, leave a TODO comment — tools are not ECS systems (they're MapCanvas
push-based); registration will be formalized in a later composition-root task.

### 1.7 — Success criteria (E002)

After implementation:

1. `dotnet build IOS-IG-SimHost.sln --no-incremental` → 0 errors.
2. Write `Hrot.ScenarioEditor.Tests/ToolPresenceTests.cs` with reflection tests:
   ```csharp
   [Fact]
   public void ScenarioEditor_Assembly_ContainsAllToolTypes()
   {
       var asm = typeof(ScenarioEditorModule).Assembly;
       Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.CreationTool"));
       Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.EditTool"));
       Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.RouteEditTool"));
       Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.MeasureTool"));
       Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Tools.StandardInteractionTool"));
   }
   
   [Fact]
   public void IG_Assembly_DoesNotContainToolTypes()
   {
       var asm = typeof(IgApplication).Assembly;
       Assert.Null(asm.GetType("Hrot.IG.Tools.CreationTool"));
       Assert.Null(asm.GetType("Hrot.IG.Tools.EditTool"));
       Assert.Null(asm.GetType("Hrot.IG.Tools.RouteEditTool"));
   }
   ```
   These tests verify the move was complete.
3. `dotnet test Hrot.IG.Tests --no-build` — all tests that passed before still pass.

> **Note:** The `IgApplication` type is in `Hrot.IG`. Add `using Hrot.IG;` to the test.
> Add `<ProjectReference Include="..\Hrot.IG\Hrot.IG.csproj" />` to
> `Hrot.ScenarioEditor.Tests.csproj` (compile-only; needed for the reflection test).

---

## Task 2 — Extract Visual Rendering Layers (PACK2-E003)

**Task Definition:** [TASK-DETAIL.md §PACK2-E003](../TASK-DETAIL.md#pack2-e003--extract-visual-rendering-layers-into-hrotscenarioeditor)

### 2.1 — Move rendering system files

Move from `Hrot.IG/Systems/` to `Hrot.ScenarioEditor/Rendering/`:

| Source | Destination |
|--------|-------------|
| `Hrot.IG/Systems/MapOverlayRenderLayer.cs` | `Hrot.ScenarioEditor/Rendering/MapOverlayRenderLayer.cs` |
| `Hrot.IG/Systems/RouteRenderLayer.cs` | `Hrot.ScenarioEditor/Rendering/RouteRenderLayer.cs` |
| `Hrot.IG/Systems/MissionRenderLayer.cs` | `Hrot.ScenarioEditor/Rendering/MissionRenderLayer.cs` |
| `Hrot.IG/Systems/SelectionRenderSystem.cs` | `Hrot.ScenarioEditor/Rendering/SelectionRenderSystem.cs` |
| `Hrot.IG/Systems/SelectionRenderConstants.cs` | `Hrot.ScenarioEditor/Rendering/SelectionRenderConstants.cs` |

### 2.2 — Move adapter files

Move from `Hrot.IG/Adapters/` to `Hrot.ScenarioEditor/Adapters/`:

| Source | Destination |
|--------|-------------|
| `Hrot.IG/Adapters/SstVisualizerAdapter.cs` | `Hrot.ScenarioEditor/Adapters/SstVisualizerAdapter.cs` |
| `Hrot.IG/Adapters/SstVisualizerAdapterConstants.cs` | `Hrot.ScenarioEditor/Adapters/SstVisualizerAdapterConstants.cs` |
| `Hrot.IG/Adapters/StubVisualizerAdapter.cs` | `Hrot.ScenarioEditor/Adapters/StubVisualizerAdapter.cs` |
| `Hrot.IG/Adapters/StubVisualizerConstants.cs` | `Hrot.ScenarioEditor/Adapters/StubVisualizerConstants.cs` |

### 2.3 — Update namespaces in moved files

For rendering files, change `namespace Hrot.IG.Systems;` → `namespace Hrot.ScenarioEditor.Rendering;`  
For adapter files, change `namespace Hrot.IG.Adapters;` → `namespace Hrot.ScenarioEditor.Adapters;`

> **Note on `Hrot.IG.Adapters` in `SelectionRenderSystem`:** The moved
> `SelectionRenderSystem.cs` uses `Hrot.IG.Adapters` (for `SstVisualizerAdapter`). After adapters
> move to `Hrot.ScenarioEditor.Adapters`, update the using directive in `SelectionRenderSystem.cs`
> from `using Hrot.IG.Adapters;` to `using Hrot.ScenarioEditor.Adapters;`.

> **Note on FDP.Toolkit.Behavior:** `MissionRenderLayer.cs` imports
> `FDP.Toolkit.Behavior.Components`. Add this reference to `Hrot.ScenarioEditor.csproj` if not
> already present. Check `Hrot.IG.csproj` for the exact project reference path.

### 2.4 — Add `FDP.Toolkit.Behavior.Components` reference if needed

Check whether `Hrot.ScenarioEditor.csproj` already has a reference to
`FDP.Toolkit.Behavior.Components` or `FDP.Toolkit.Behavior`. If not, add:
```xml
<ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Behavior\FDP.Toolkit.Behavior.csproj" />
```
(Verify the exact path relative to Hrot.ScenarioEditor by checking Hrot.IG.csproj or the FDP directory.)

### 2.5 — Update consumers in `Hrot.IG`

Search for `using Hrot.IG.Systems;` and `using Hrot.IG.Adapters;` in all `Hrot.IG/**/*.cs` files.
Update usings to the new ScenarioEditor namespaces:
- `Hrot.IG.Systems` → `Hrot.ScenarioEditor.Rendering` (for moved types only)
- `Hrot.IG.Adapters` → `Hrot.ScenarioEditor.Adapters` (for moved types only)

> **Note:** `Hrot.IG.Systems` still contains many NON-moved systems (e.g. `ContextMenuSystem`,
> `MapCommandController`). You must NOT remove the `using Hrot.IG.Systems;` directive globally —
> only add `using Hrot.ScenarioEditor.Rendering;` and `using Hrot.ScenarioEditor.Adapters;` where
> the moved types are referenced.

### 2.6 — Success criteria (E003)

1. `dotnet build IOS-IG-SimHost.sln --no-incremental` → 0 errors.
2. Dependency check: `Hrot.ScenarioEditor` assembly must not directly reference `Hrot.NED`
   (check .csproj — no direct `Hrot.NED` reference allowed in the moved files).
3. Write a render round-trip smoke test (can be minimal — no actual rendering canvas needed):
   ```csharp
   // In Hrot.ScenarioEditor.Tests/RenderLayerPresenceTests.cs
   [Fact]
   public void ScenarioEditor_Assembly_ContainsRenderLayers()
   {
       var asm = typeof(ScenarioEditorModule).Assembly;
       Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Rendering.MapOverlayRenderLayer"));
       Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Rendering.RouteRenderLayer"));
       Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Rendering.SelectionRenderSystem"));
       Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Adapters.SstVisualizerAdapter"));
   }
   ```
4. `dotnet test Hrot.IG.Tests --no-build` → all tests pass (no regression).

---

## Verification Checklist

Before writing the batch report:

- [ ] `dotnet build IOS-IG-SimHost.sln --no-incremental` → **0 errors**
- [ ] `dotnet test Hrot.IG.Tests --no-build` → same pass/fail counts as pre-BATCH-04
- [ ] `dotnet test Hrot.ScenarioEditor.Tests --no-build` → all pass (including new reflection tests)
- [ ] `dotnet test Hrot.Map.Common.Tests --no-build` → all 99 pass (SelectionState move not breaking)
- [ ] `dotnet test Hrot.ClusterRunner.Integration.Tests --no-build` → no new failures

---

## Files Produced

| File | Change |
|------|--------|
| `Hrot.Map.Common/Components/SelectionState.cs` | New (moved from IG, keeps Hrot.IG.Components namespace) |
| `Hrot.IG/Components/SelectionState.cs` | Replaced with comment-stub |
| `Hrot.ScenarioEditor/Tools/CreationTool.cs` | New (moved from IG) |
| `Hrot.ScenarioEditor/Tools/CreationToolConstants.cs` | New (moved) |
| `Hrot.ScenarioEditor/Tools/EditTool.cs` | New (moved) |
| `Hrot.ScenarioEditor/Tools/EditToolConstants.cs` | New (moved) |
| `Hrot.ScenarioEditor/Tools/RouteEditTool.cs` | New (moved) |
| `Hrot.ScenarioEditor/Tools/RouteEditToolConstants.cs` | New (moved) |
| `Hrot.ScenarioEditor/Tools/MeasureTool.cs` | New (moved) |
| `Hrot.ScenarioEditor/Tools/MeasureToolConstants.cs` | New (moved) |
| `Hrot.ScenarioEditor/Tools/StandardInteractionTool.cs` | New (moved) |
| `Hrot.ScenarioEditor/Tools/StandardInteractionToolConstants.cs` | New (moved) |
| `Hrot.ScenarioEditor/Rendering/MapOverlayRenderLayer.cs` | New (moved) |
| `Hrot.ScenarioEditor/Rendering/RouteRenderLayer.cs` | New (moved) |
| `Hrot.ScenarioEditor/Rendering/MissionRenderLayer.cs` | New (moved) |
| `Hrot.ScenarioEditor/Rendering/SelectionRenderSystem.cs` | New (moved) |
| `Hrot.ScenarioEditor/Rendering/SelectionRenderConstants.cs` | New (moved) |
| `Hrot.ScenarioEditor/Adapters/SstVisualizerAdapter.cs` | New (moved) |
| `Hrot.ScenarioEditor/Adapters/SstVisualizerAdapterConstants.cs` | New (moved) |
| `Hrot.ScenarioEditor/Adapters/StubVisualizerAdapter.cs` | New (moved) |
| `Hrot.ScenarioEditor/Adapters/StubVisualizerConstants.cs` | New (moved) |
| `Hrot.ScenarioEditor.Tests/ToolPresenceTests.cs` | New |
| `Hrot.ScenarioEditor.Tests/RenderLayerPresenceTests.cs` | New |
| `Hrot.ScenarioEditor.Tests/Hrot.ScenarioEditor.Tests.csproj` | Add Hrot.IG ref for reflection test |
| `Hrot.ScenarioEditor/Hrot.ScenarioEditor.csproj` | Add Raylib-cs, rlImgui-cs, FDP.Toolkit.Behavior ref |
| `Hrot.IG/Hrot.IG.csproj` | Add Hrot.ScenarioEditor reference |
| `Hrot.IG/**/*.cs` | Update using directives for moved namespaces |
| `Hrot.IG/Tools/*.cs` | Deleted (source files moved to ScenarioEditor) |
| `Hrot.IG/Systems/MapOverlayRenderLayer.cs` | Deleted |
| `Hrot.IG/Systems/RouteRenderLayer.cs` | Deleted |
| `Hrot.IG/Systems/MissionRenderLayer.cs` | Deleted |
| `Hrot.IG/Systems/SelectionRenderSystem.cs` | Deleted |
| `Hrot.IG/Systems/SelectionRenderConstants.cs` | Deleted |
| `Hrot.IG/Adapters/*.cs` | Deleted (moved to ScenarioEditor) |

---

## Batch Report

Submit `BATCH-04-REPORT.md` in `.dev/packs-2/reports/` when done. Include:

1. Task completion table.
2. Test counts.
3. Answers to:
   - **Q1:** Which files in `Hrot.IG` still reference the old `Hrot.IG.Tools` namespace (e.g.
     in `IgApplication.cs`)? How many `using` directives needed updating?
   - **Q2:** Were any tools or render layers still importing `Hrot.NED` or `CycloneDDS` types?
     If so, how were they resolved?
   - **Q3:** Did `FDP.Toolkit.Behavior.Components` need to be added to `Hrot.ScenarioEditor.csproj`?
     Did this introduce any transitive `Hrot.NED` references?
   - **Q4:** Were there any other IG-specific component types (beyond `SelectionState`) that
     needed moving to `Hrot.Map.Common` before the tools/renderers could compile?
4. Suggested git commit message.
