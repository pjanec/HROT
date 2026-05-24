# BATCH-09 INSTRUCTIONS

## Task
- **EQS-022** -- ImGui inspector and gizmo projector

## References
- Task spec: `.dev/eqs-2/TASK-DETAIL.md` section TASK-EQS-022
- Implementation details: `.dev/eqs-2/IMPLEM_DETAILS.md` L:2740--3000
- Existing gizmo pattern: `Hrot/Subsystems/Hrot.IG/Gizmos/ProjectilePresentationGizmo.cs`
- Existing settings pattern: `Hrot/Subsystems/Hrot.IG/Gizmos/MeasureToolGizmoSettings.cs`
- Existing renderer pattern: `FDP/Engine/Fdp.Presentation/ImGui/Renderers/SingletonRenderers.cs`
- Task tracker: `.dev/eqs-2/TASK-TRACKER.md`

## Constraints (apply to all files)
- ASCII only -- no Unicode in comments or strings
- Minimize diffs -- do not reformat unrelated code
- Build must succeed with 0 errors before reporting
- Namespace for all three new files: `Hrot.IG.Gizmos` (matching existing gizmo files in the project)
- New files go in: `Hrot/Subsystems/Hrot.IG/Gizmos/`
- Unit tests go in: `Hrot/Subsystems/Hrot.IG.Tests/` (separate from integration tests)

---

## 022-A: `EqsGizmoSettings`

**File (NEW):** `Hrot/Subsystems/Hrot.IG/Gizmos/EqsGizmoSettings.cs`

Follow the exact pattern of `MeasureToolGizmoSettings.cs`.

```csharp
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.IG.Gizmos
{
    internal static class EqsGizmoSettings
    {
        // Setting key strings -- must be stable (hashed into uint at construction time)
        public const string ShowRadius     = "EQS.ShowSearchRadius";
        public const string ShowCandidates = "EQS.ShowTopKCandidates";
        public const string ShowScores     = "EQS.ShowScores";

        public static void Register(GizmoSettingsRegistry settings)
        {
            settings.RegisterSetting(ShowRadius,     GizmoSettingValue.From(true));
            settings.RegisterSetting(ShowCandidates, GizmoSettingValue.From(true));
            settings.RegisterSetting(ShowScores,     GizmoSettingValue.From(true));
        }
    }
}
```

---

## 022-B: `EqsSensorGizmo`

**File (NEW):** `Hrot/Subsystems/Hrot.IG/Gizmos/EqsSensorGizmo.cs`

Follow the pattern of `ProjectilePresentationGizmo.cs` for the class structure.

Key implementation points:
- `[GizmoProjector(typeof(SimTransform), typeof(EqsSensor))]` attribute
- Constructor receives `GizmoSettingsRegistry settings` -- calls `EqsGizmoSettings.Register(settings)`
- Pre-compute FNV-1a hashes in constructor (use `GizmoSettingsRegistry.ComputeHash(key)` static method):
  ```csharp
  _hashShowRadius     = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowRadius);
  _hashShowCandidates = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowCandidates);
  _hashShowScores     = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowScores);
  ```
- `Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)` implementation:
  1. Read `SimTransform` and `EqsSensor` from view (they are guaranteed by `[GizmoProjector]`)
  2. If `ShowRadius` setting is true: `draw.DrawSphere(obsPos, sensor.SearchRadius, cyanColor, thickness: 1f, style: LineStyle.Dashed)` where `cyanColor = new Rgba32(0, 255, 255, 100)`
  3. If entity has `EqsCognitiveBuffer` AND `ShowCandidates` is true:
     - Read buffer (ref readonly)
     - If `!buffer.IsReady || buffer.Count == 0` return
     - For each `i` in `[0, buffer.Count)`:
       - Read `var candidate = buffer[i]`
       - `targetPos = new Vector3(candidate.PositionX, candidate.PositionY, 0f)`
       - Line color: `EntityId == 0` -> green `Rgba32(0, 255, 0, 150)`, else yellow `Rgba32(255, 255, 0, 150)`
       - `draw.DrawLine(obsPos, targetPos, lineColor, thickness: 1.5f)`
       - `draw.DrawSphere(targetPos, 1.5f, lineColor)`
       - If `ShowScores` is true: format score as ASCII only (`string.Format("#{0} ({1:F2})", i+1, candidate.Score)`)

**Note on DrawText:** The IMPLEM_DETAILS shows `draw.DrawText(...)` for scores. Check if `IDebugDrawBuilder` has a `DrawText` method; if not, use `draw.DrawLabel` or skip text (only draw sphere+line). Do not add a method that doesn't exist.

**Usings needed:**
```csharp
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Replication.Components;  // SimTransform
using Fdp.Toolkit.Spatial.Eqs;             // EqsSensor, EqsCognitiveBuffer
```

Check the actual `IDebugDrawBuilder` interface in `Fdp.Diagnostics.Contracts` or `GizmoMap.Contracts` to confirm available methods.

---

## 022-C: `EqsCognitiveBufferRenderer`

**File (NEW):** `Hrot/Subsystems/Hrot.IG/Gizmos/EqsCognitiveBufferRenderer.cs`

Follow the pattern of `SingletonRenderers.cs` in `Fdp.Presentation`.

```csharp
using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Spatial.Eqs;
using ImGuiApi = ImGuiNET.ImGui;

namespace Hrot.IG.Gizmos
{
    [ImGuiRenderer(typeof(EqsCognitiveBuffer))]
    public sealed class EqsCognitiveBufferRenderer : IImGuiRenderer
    {
        public string? GetSummary(object value)
        {
            var buf = (EqsCognitiveBuffer)value;
            return buf.IsReady
                ? string.Format("Ready ({0} candidates)", buf.Count)
                : "Awaiting Results...";
        }

        public bool RenderValue(object value)
        {
            var buf = (EqsCognitiveBuffer)value;

            ImGuiApi.TextUnformatted(string.Format("Last Update Tick : {0}", buf.LastUpdateTick));
            ImGuiApi.TextUnformatted(string.Format("Refresh Epoch    : {0}", buf.LastUpdateEpoch));

            if (buf.Count > 0 && ImGuiApi.BeginTable("EqsResultsTable", 4,
                ImGuiNET.ImGuiTableFlags.Borders | ImGuiNET.ImGuiTableFlags.RowBg))
            {
                ImGuiApi.TableSetupColumn("Rank",     ImGuiNET.ImGuiTableColumnFlags.WidthFixed, 40f);
                ImGuiApi.TableSetupColumn("EntityId", ImGuiNET.ImGuiTableColumnFlags.WidthStretch);
                ImGuiApi.TableSetupColumn("Position", ImGuiNET.ImGuiTableColumnFlags.WidthStretch);
                ImGuiApi.TableSetupColumn("Score",    ImGuiNET.ImGuiTableColumnFlags.WidthFixed,  70f);
                ImGuiApi.TableHeadersRow();

                for (int i = 0; i < buf.Count; i++)
                {
                    var res = buf[i];
                    ImGuiApi.TableNextRow();
                    ImGuiApi.TableSetColumnIndex(0);
                    ImGuiApi.TextUnformatted(string.Format("#{0}", i + 1));
                    ImGuiApi.TableSetColumnIndex(1);
                    ImGuiApi.TextUnformatted(res.EntityId == 0
                        ? "Positional"
                        : res.EntityId.ToString());
                    ImGuiApi.TableSetColumnIndex(2);
                    ImGuiApi.TextUnformatted(string.Format("({0:F1}, {1:F1})", res.PositionX, res.PositionY));
                    ImGuiApi.TableSetColumnIndex(3);
                    ImGuiApi.TextUnformatted(string.Format("{0:F3}", res.Score));
                }

                ImGuiApi.EndTable();
            }

            return true;
        }
    }
}
```

**Note on TextColored:** The IMPLEM_DETAILS uses `ImGuiApi.TextColored(...)` for the score cell. Use plain `TextUnformatted` if `TextColored` requires a `Vector4` import -- only add `using System.Numerics` if needed and it doesn't create ambiguity. Keep it simple.

---

## 022-D: Unit tests

**File (NEW):** `Hrot/Subsystems/Hrot.IG.Tests/Eqs/EqsVisualizersTests.cs`

First verify `Hrot.IG.Tests.csproj` exists and references `Hrot.IG`. If it does, add the test file there.

**Test T-VIS1 -- `EqsSensorGizmo_HasGizmoProjectorAttribute_WithCorrectTypes`:**
```csharp
[Fact]
public void EqsSensorGizmo_HasGizmoProjectorAttribute_WithCorrectTypes()
{
    var attr = typeof(EqsSensorGizmo)
        .GetCustomAttribute<GizmoProjectorAttribute>();
    Assert.NotNull(attr);
    Assert.Contains(typeof(SimTransform),   attr.RequiredComponents);
    Assert.Contains(typeof(EqsSensor),      attr.RequiredComponents);
}
```

**Test T-VIS2 -- `EqsCognitiveBufferRenderer_HasImGuiRendererAttribute_ForCognitiveBuffer`:**
```csharp
[Fact]
public void EqsCognitiveBufferRenderer_HasImGuiRendererAttribute_ForCognitiveBuffer()
{
    var attrs = typeof(EqsCognitiveBufferRenderer)
        .GetCustomAttributes<ImGuiRendererAttribute>();
    Assert.True(attrs.Any(a => a.TargetType == typeof(EqsCognitiveBuffer)));
}
```

**Test T-VIS3 -- `EqsCognitiveBufferRenderer_GetSummary_ReadyBuffer_ReturnsCorrectString`:**
```csharp
[Fact]
public void EqsCognitiveBufferRenderer_GetSummary_ReadyBuffer_ReturnsCorrectString()
{
    var renderer = new EqsCognitiveBufferRenderer();
    var buffer   = new EqsCognitiveBuffer { IsReady = true, Count = 3, LastUpdateTick = 1 };
    var summary  = renderer.GetSummary(buffer);
    Assert.NotNull(summary);
    Assert.Contains("3", summary);
    Assert.Contains("Ready", summary);
}
```

**Test T-VIS4 -- `EqsCognitiveBufferRenderer_GetSummary_NotReady_ReturnsAwaitingString`:**
```csharp
[Fact]
public void EqsCognitiveBufferRenderer_GetSummary_NotReady_ReturnsAwaitingString()
{
    var renderer = new EqsCognitiveBufferRenderer();
    var buffer   = new EqsCognitiveBuffer { IsReady = false, Count = 0 };
    var summary  = renderer.GetSummary(buffer);
    Assert.NotNull(summary);
    Assert.Contains("Awaiting", summary);
}
```

**Test T-VIS5 -- `EqsGizmoSettings_KeyHashes_AreDistinct`:**
```csharp
[Fact]
public void EqsGizmoSettings_KeyHashes_AreDistinct()
{
    uint h1 = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowRadius);
    uint h2 = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowCandidates);
    uint h3 = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowScores);
    Assert.NotEqual(h1, h2);
    Assert.NotEqual(h2, h3);
    Assert.NotEqual(h1, h3);
}
```

Add `[Collection("EqsVisualizersTests")]` on the test class if needed to avoid parallel conflicts. Use `[Fact]` for all tests.

Add required usings:
```csharp
using System.Linq;
using System.Reflection;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Presentation.Renderers;
using Hrot.IG.Gizmos;
using Xunit;
```

---

## Build and Test Verification

After implementation, verify:

```
dotnet build Hrot/Subsystems/Hrot.IG/Hrot.IG.csproj --no-restore
dotnet build Hrot/Subsystems/Hrot.IG.Tests/Hrot.IG.Tests.csproj --no-restore
dotnet test Hrot/Subsystems/Hrot.IG.Tests/Hrot.IG.Tests.csproj --no-build --filter "FullyQualifiedName~Eqs"
```

Also confirm no regressions in existing EQS tests:
```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build --filter "FullyQualifiedName~Eqs"
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --no-build --filter "FullyQualifiedName~Eqs"
```

Expected results:
- All builds succeed with 0 errors
- New visualizer unit tests: T-VIS1 through T-VIS5 all pass
- Pre-existing EQS unit tests: still 49/49
- Pre-existing EQS integration tests: still 21/21

---

## Report

When done, write your report to `.dev/eqs-2/reports/BATCH-09-REPORT.md` including:
- Files created/modified
- Test counts (before and after)
- Any deviations from the plan (with justification)
- All test names and pass/fail status
- Confirmation that all builds pass with 0 errors
