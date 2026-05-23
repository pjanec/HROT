# BATCH-04 Report

**Batch:** BATCH-04 — Shared Windows and DI Wiring  
**Tasks:** TASK-S1-09, TASK-S1-10, TASK-S1-14, TASK-S1-15  
**Status:** COMPLETE

---

## Summary

All tasks implemented, all tests pass, solution builds cleanly.

---

## Checklist

- [x] `Hrot.Editor.AiShared.csproj` — added `Fdp.Presentation` project reference + `Microsoft.Extensions.DependencyInjection 8.0.0`
- [x] `Hrot.Editor.AiShared.Tests.csproj` — added `Microsoft.Extensions.DependencyInjection 8.0.0`
- [x] TASK-S1-15: `IRuntimeInspectorPane`, `ITraceLaneProvider`, `TraceLaneDescriptor` created
- [x] TASK-S1-15: `RuntimeInspectorWindow`, `TraceTimelineWindow` shell windows created
- [x] TASK-S1-09: `AssetBrowserWindow` created
- [x] TASK-S1-10: `InspectorWindow` created
- [x] TASK-S1-14: `SharedAiWindowRegistrar`, `SharedAiEditorServiceCollectionExtensions` created
- [x] All 139 tests pass (110 existing + 29 new)
- [x] `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 warnings

---

## Test Results

```
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj
Passed! - Failed: 0, Passed: 139, Skipped: 0, Total: 139
```

**New tests added: 29**

| Test file | Count |
|---|---|
| `Windows/AssetBrowserWindowTests.cs` | 4 |
| `Windows/InspectorWindowTests.cs` | 4 |
| `Windows/RuntimeInspectorWindowTests.cs` | 5 |
| `Windows/TraceTimelineWindowTests.cs` | 5 |
| `Debug/TraceLaneDescriptorTests.cs` | 3 |
| `Di/SharedAiEditorDiTests.cs` | 8 |
| **Total new** | **29** |

---

## Files Created

### Production code

| File | Description |
|---|---|
| `Hrot/Editor/Hrot.Editor.AiShared/Debug/IRuntimeInspectorPane.cs` | Interface for subsystem inspector panes |
| `Hrot/Editor/Hrot.Editor.AiShared/Debug/ITraceLaneProvider.cs` | Interface + `TraceLaneDescriptor` record for timeline swim lanes |
| `Hrot/Editor/Hrot.Editor.AiShared/Windows/RuntimeInspectorWindow.cs` | Shell window with `RegisterPane` + internal `RegisteredPaneCount` |
| `Hrot/Editor/Hrot.Editor.AiShared/Windows/TraceTimelineWindow.cs` | Shell window with `RegisterProvider` + internal `RegisteredProviderCount` |
| `Hrot/Editor/Hrot.Editor.AiShared/Windows/AssetBrowserWindow.cs` | Asset browser shell window |
| `Hrot/Editor/Hrot.Editor.AiShared/Windows/InspectorWindow.cs` | Inspector shell window |
| `Hrot/Editor/Hrot.Editor.AiShared/Windows/SharedAiWindowRegistrar.cs` | `IWindowRegistrar` implementation registering all four windows |
| `Hrot/Editor/Hrot.Editor.AiShared/Di/SharedAiEditorServiceCollectionExtensions.cs` | `AddSharedAiEditor()` DI extension |

### Test code

| File | Count |
|---|---|
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Windows/AssetBrowserWindowTests.cs` | 4 |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Windows/InspectorWindowTests.cs` | 4 |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Windows/RuntimeInspectorWindowTests.cs` | 5 |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Windows/TraceTimelineWindowTests.cs` | 5 |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Debug/TraceLaneDescriptorTests.cs` | 3 |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Di/SharedAiEditorDiTests.cs` | 8 |

---

## Deviations from Spec

None. All implementations match the spec exactly.

**One addition:** Both `RuntimeInspectorWindow` and `TraceTimelineWindow` expose `internal int RegisteredProviderCount` / `internal int RegisteredPaneCount` properties respectively for test verification. This is explicitly permitted and recommended by the instructions.

---

## Build Results

```
dotnet build IOS-IG-SimHost.sln
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Developer Insights

**1. Did you need to use the `using Gui = ImGuiNET.ImGui` alias or the full path? Why?**

Used the full path `ImGuiNET.ImGui.TextDisabled(...)` and `ImGuiNET.ImGui.Text(...)`. The `global using Gui = ImGuiNET.ImGui` alias is declared in `Fdp.Presentation`'s `GlobalUsings.cs`, which applies only within the `Fdp.Presentation` assembly. `Hrot.Editor.AiShared` is a separate assembly that merely references `Fdp.Presentation`; global usings do not propagate across assembly boundaries. Using the full namespace avoids any ambiguity and matches what the instructions explicitly call for.

**2. Is `AssetCatalog` constructor-injectable from DI without any additional setup? Any issue?**

Yes — `AssetCatalog()` has a no-arg public constructor and DI resolves it without any configuration. The empty catalog state (`All.Count == 0`) is handled gracefully in `AssetBrowserWindow.DrawClientArea()` by showing a "No assets loaded." disabled text. Contributors are added post-construction; this is fine for a singleton lifecycle.

**3. For the `RegisterPane` and `RegisterProvider` tests: how did you expose the count for test verification?**

Added `internal int RegisteredPaneCount => _panes.Count` to `RuntimeInspectorWindow` and `internal int RegisteredProviderCount => _providers.Count` to `TraceTimelineWindow`. The `<InternalsVisibleTo Include="Hrot.Editor.AiShared.Tests" />` entry already present in the csproj makes these properties accessible from the test project without exposing them as part of the public API.

**4. Any namespace conflicts between `Fdp.Toolkit.Runner.IWindowRegistrar` and other types?**

No conflicts. `Fdp.Toolkit.Runner` is a distinct namespace used exclusively for the runner/subsystem layer. The `using Fdp.Toolkit.Runner;` directive in `SharedAiWindowRegistrar.cs` and `SharedAiEditorServiceCollectionExtensions.cs` is unambiguous; no other `IWindowRegistrar` exists in scope.

**5. Were all 110 existing tests unaffected after adding the new project references?**

Yes. Adding `Fdp.Presentation` and `Microsoft.Extensions.DependencyInjection` references does not change any existing types, namespaces, or behaviour. All 110 pre-existing tests continue to pass unchanged.
