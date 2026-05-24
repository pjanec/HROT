# BATCH-10: Phase 5 Visual App + Hot Reload Demo (FBT-043, FBT-045)

**Batch Number:** BATCH-10
**Tasks:** FBT-043, FBT-045
**Phase:** Phase 5 — Sample Project (visual app + hot reload)
**Estimated Effort:** 6–9 hours
**Dependencies:** BATCH-09 complete (commit dd43664)

---

## Mandatory Reading (in order)

Read these files BEFORE writing any code:

1. `.dev/fluent-btree/TASK-DETAIL.md` — §TASK-FBT-043, §TASK-FBT-045
2. `FDP/ExtDeps/FastBTree/demos/Fbt.Demo.Visual/Fbt.Demo.Visual.csproj` — NuGet package versions to use
3. `FDP/ExtDeps/FastBTree/demos/Fbt.Demo.Visual/DemoApp.cs` — Raylib + rlImGui loop pattern
4. `FDP/ExtDeps/FastBTree/demos/Fbt.Demo.Visual/UI/TreeVisualizer.cs` — tree rendering reference
5. `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/HotReload/FbtAssemblyHotReloader.cs` — hot reloader API
6. `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/HotReload/BTreeHotReloadManager.cs` — TryReload API
7. `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/HotReload/ReloadResult.cs` — enum values
8. `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/AmbushTree.cs` — CreateInterpreter, CreateBuilder
9. `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/CombatBlackboard.cs`
10. `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/CombatActions.cs`
11. `FDP/ExtDeps/FastBTree/FastBTree.sln` — must add new Trees project

---

## Architecture Overview

To avoid the self-rebuild file-lock problem (an exe cannot overwrite itself while running), FBT-045 requires a **separate Trees class library**:

```
Fbt.Examples.FluentBTree (exe)        -- visual app, watches Trees DLL for changes
  |-- references --> Fbt.Examples.FluentBTree.Trees (classlib)  -- pure tree defs + actions
```

Step 1 creates the Trees library (moving existing source files).
Steps 2-3 convert the main project to the visual app.
Step 4 adds the Recompile & Reload button.

---

## Step 1: Create `Fbt.Examples.FluentBTree.Trees` Class Library

### 1a. Create the project file

**File:** `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree.Trees/Fbt.Examples.FluentBTree.Trees.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Fbt.Kernel\Fbt.Kernel.csproj" />
    <ProjectReference Include="..\..\src\Fbt.Compiler\Fbt.Compiler.csproj" />
    <ProjectReference Include="..\..\src\Fbt.SourceGen\Fbt.SourceGen.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>

</Project>
```

### 1b. Move source files

Move (do NOT copy — delete originals) these files from `Fbt.Examples.FluentBTree/` to `Fbt.Examples.FluentBTree.Trees/`:
- `CombatBlackboard.cs`
- `CombatActions.cs`
- `AmbushTree.cs`

The namespace in those files remains `Fbt.Examples.FluentBTree` — do NOT change namespaces.

### 1c. Update `Fbt.Examples.FluentBTree.csproj`

Add a reference to the Trees library and the three Raylib/ImGui NuGet packages.
Use the same package versions as `Fbt.Demo.Visual`:
- `ImGui.NET 1.91.6.1`
- `Raylib-cs 7.0.2`
- `rlImgui-cs 3.2.0`

Replace the current content of `Fbt.Examples.FluentBTree.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ImGui.NET" Version="1.91.6.1" />
    <PackageReference Include="Raylib-cs" Version="7.0.2" />
    <PackageReference Include="rlImgui-cs" Version="3.2.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Fbt.Kernel\Fbt.Kernel.csproj" />
    <ProjectReference Include="..\..\src\Fbt.Compiler\Fbt.Compiler.csproj" />
    <ProjectReference Include="Fbt.Examples.FluentBTree.Trees\Fbt.Examples.FluentBTree.Trees.csproj" />
    <ProjectReference Include="..\..\src\Fbt.SourceGen\Fbt.SourceGen.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>

</Project>
```

NOTE: The `Fbt.Examples.FluentBTree.Trees.csproj` path is relative from `Fbt.Examples.FluentBTree/` directory. Adjust the path accordingly.

### 1d. Update `Fbt.Tests.csproj`

The existing `Fbt.Tests.csproj` has a `<ProjectReference>` to `Fbt.Examples.FluentBTree`. The source files moved to `Fbt.Examples.FluentBTree.Trees`. Update `Fbt.Tests.csproj` to ALSO reference the Trees project:

```xml
<ProjectReference Include="..\..\examples\Fbt.Examples.FluentBTree.Trees\Fbt.Examples.FluentBTree.Trees.csproj" />
```

Keep the existing reference to `Fbt.Examples.FluentBTree` too (tests may need both).

### 1e. Add to `FastBTree.sln`

Generate a new GUID:
```powershell
[System.Guid]::NewGuid().ToString("B").ToUpper()
```

Add `Fbt.Examples.FluentBTree.Trees` to the `examples` solution folder in `FastBTree.sln`. Follow the exact format of the `Fbt.Examples.Console` entry.

### 1f. Build check

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree.Trees/Fbt.Examples.FluentBTree.Trees.csproj 2>&1 | Select-String "error" | Select-Object -First 10
dotnet test FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 2
```

**Expected:** 160 tests still pass (no regression).

---

## Step 2: FBT-043 — Visual Application

Replace `Program.cs` in `Fbt.Examples.FluentBTree/` with the visual Raylib/ImGui app.

Study `DemoApp.cs` and `TreeVisualizer.cs` in `Fbt.Demo.Visual` carefully before implementing.

**File:** `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/Program.cs`

The app must:
1. Open a 1280x720 Raylib window titled "Ambush BTree Demo".
2. Initialize rlImGui.
3. Create an `Interpreter<CombatBlackboard, CombatContext>` via `AmbushTree.CreateInterpreter()`.
4. Maintain a single `CombatBlackboard` and `BehaviorTreeState`.
5. Each frame (when not paused): tick the interpreter once.
6. Render two ImGui windows: "Blackboard" and "Behavior Tree".
7. In "Blackboard" window: show `ImGui.SliderInt` for `AmmoCount` (range 0–20) and `ImGui.Checkbox` for `ThreatVisible`. Also show `EngagementRange` as a `SliderFloat` (0–200).
8. In "Behavior Tree" window: show the tree using the `RenderNode` pattern from `Fbt.Demo.Visual/UI/TreeVisualizer.cs`. The running node is highlighted in yellow.
9. Pause button / checkbox.
10. FPS display.

**Node color scheme** (same as `Fbt.Demo.Visual`):
- Running node: yellow `(1f, 1f, 0f, 1f)`
- All other nodes: white `(1f, 1f, 1f, 1f)`

Use `ImGui.PushStyleColor(ImGuiCol.Text, color)` / `ImGui.PopStyleColor()` around each node line.

Use `ImGui.Selectable(nodeText, false)` for leaf nodes and composites (simple flat list with indentation via spaces, as in `Fbt.Demo.Visual`). Do NOT use `ImGui.TreeNode` (it adds expand/collapse arrows).

**Node label format:** `"  [index] NodeType"` (indentation = `depth * 2` spaces).

**Method name display:** For action/condition nodes, also show the last segment of `blob.MethodNames[node.PayloadIndex]` after `@` (or the full name if no `@`). Use `blob.MethodNames != null && node.PayloadIndex >= 0 && node.PayloadIndex < blob.MethodNames.Length` before accessing.

For composite nodes, use `blob.Nodes[childIdx].SubtreeOffset` to advance to the next sibling (same as `Fbt.Demo.Visual`).

**Hot reload status label (placeholder for FBT-045):** Add a `string _reloadStatus = "No reload yet."` field. Display it in the Behavior Tree window. The actual reload button is added in Step 4.

Here is a reference structure for `Program.cs`:

```csharp
using Fbt;
using Fbt.Examples.FluentBTree;
using Fbt.HotReload;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using System;
using System.Numerics;

// ---- App state ----
var interpreter = AmbushTree.CreateInterpreter();
var bb = new CombatBlackboard { AmmoCount = 5, ThreatVisible = true, EngagementRange = 50f };
var state = new BehaviorTreeState();
var ctx = new CombatContext();
bool paused = false;
string reloadStatus = "No reload yet.";

// FBT-045: hot reload manager + assembly reloader (wired in Step 4)
BTreeHotReloadManager? hotReloadManager = null;   // initialized in Step 4
FbtAssemblyHotReloader? assemblyReloader = null;  // initialized in Step 4

// ---- Raylib init ----
Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
Raylib.InitWindow(1280, 720, "Ambush BTree Demo");
Raylib.SetTargetFPS(60);
rlImGui.Setup(true);

// ---- Main loop ----
while (!Raylib.WindowShouldClose())
{
    float dt = Raylib.GetFrameTime();
    ctx.DeltaTime = dt;
    ctx.Time += dt;
    ctx.FrameCount++;

    // Drain hot reload callbacks (FBT-045: wired in Step 4)
    assemblyReloader?.DrainPendingCallbacks();

    if (!paused)
        interpreter.Tick(ref bb, ref state, ref ctx);

    Raylib.BeginDrawing();
    Raylib.ClearBackground(Color.DarkGray);

    rlImGui.Begin();
    DrawUI();
    rlImGui.End();

    Raylib.EndDrawing();
}

rlImGui.Shutdown();
assemblyReloader?.Dispose();
Raylib.CloseWindow();

// ---- Local function: draw all ImGui windows ----
void DrawUI()
{
    // --- Window 1: Blackboard ---
    ImGui.Begin("Blackboard");
    ImGui.Text($"FPS: {Raylib.GetFPS()}");
    ImGui.Checkbox("Paused", ref paused);
    ImGui.Separator();
    int ammo = bb.AmmoCount;
    if (ImGui.SliderInt("AmmoCount", ref ammo, 0, 20))
        bb.AmmoCount = ammo;
    bool threat = bb.ThreatVisible;
    if (ImGui.Checkbox("ThreatVisible", ref threat))
        bb.ThreatVisible = threat;
    float range = bb.EngagementRange;
    if (ImGui.SliderFloat("EngagementRange", ref range, 0f, 200f))
        bb.EngagementRange = range;
    ImGui.End();

    // --- Window 2: Behavior Tree ---
    ImGui.Begin("Behavior Tree");
    ImGui.Text($"RunningNode: {state.RunningNodeIndex}  TreeVersion: {state.TreeVersion}");
    ImGui.Text($"Reload: {reloadStatus}");
    // FBT-045: "Recompile & Reload" button goes here (Step 4)
    ImGui.Separator();
    var blob = interpreter.Blob;
    if (blob.Nodes != null)
        RenderNode(blob, 0, state.RunningNodeIndex, 0);
    ImGui.End();
}

// ---- Local function: recursive tree renderer ----
void RenderNode(BehaviorTreeBlob blob, int index, int runningIndex, int depth)
{
    if (index >= blob.Nodes.Length) return;

    var node = blob.Nodes[index];
    string indent = new string(' ', depth * 2);

    string label = $"{indent}[{index}] {node.Type}";
    if ((node.Type == NodeType.Action || node.Type == NodeType.Condition)
        && blob.MethodNames != null
        && node.PayloadIndex >= 0 && node.PayloadIndex < blob.MethodNames.Length)
    {
        string fullName = blob.MethodNames[node.PayloadIndex];
        int at = fullName.LastIndexOf('@');
        int dot = fullName.LastIndexOf('.', at >= 0 ? at - 1 : fullName.Length - 1);
        label += $" \"{(dot >= 0 ? fullName.Substring(dot + 1) : fullName)}\"";
    }

    bool isRunning = (index == runningIndex);
    if (isRunning)
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 0f, 1f));

    ImGui.Selectable(label, false);

    if (isRunning)
        ImGui.PopStyleColor();

    int childIdx = index + 1;
    for (int i = 0; i < node.ChildCount; i++)
    {
        RenderNode(blob, childIdx, runningIndex, depth + 1);
        childIdx += blob.Nodes[childIdx].SubtreeOffset;
    }
}
```

**Note on `interpreter.Blob`:** This property was added in BATCH-08 (see `Interpreter.cs`). Verify it exists before using it.

**Build check:**
```powershell
dotnet build FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/Fbt.Examples.FluentBTree.csproj 2>&1 | Select-String "error" | Select-Object -First 10
```

---

## Step 3: Verify Visual Build Compiles

The visual app does NOT need to run in CI/build verification — only compile. The test project (`Fbt.Tests`) still references `Fbt.Examples.FluentBTree.Trees` and the 160 tests must still pass.

```powershell
dotnet test FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 2
```

---

## Step 4: FBT-045 — "Recompile & Reload" Button

Wire the hot reload machinery. This step modifies `Program.cs` ONLY (adding to the existing structure from Step 2).

### Architecture

- `FbtAssemblyHotReloader` watches the build output directory of `Fbt.Examples.FluentBTree.Trees`
- Clicking "Recompile & Reload" runs `dotnet build Fbt.Examples.FluentBTree.Trees.csproj` via `System.Diagnostics.Process`
- The watcher fires when the DLL changes; the handler extracts the new blob from the new assembly
- `BTreeHotReloadManager.TryReload` determines the reload type
- On `SoftReload`: keep existing `bb` and `state` (no reset)
- On `HardReset`: keep `bb` but reset `state` to default `BehaviorTreeState()`
- On `NewTree`: reset both `state` and `bb`
- On `NoChange`: no-op
- After any successful reload, create a new `Interpreter<CombatBlackboard, CombatContext>` from the new blob + new registry

### Key paths

Determine the Trees project DLL output directory at runtime:
```csharp
// The Trees DLL lives next to the exe in the output folder
string treesAssemblyName = "Fbt.Examples.FluentBTree.Trees.dll";
string watchDir = AppContext.BaseDirectory;
```

When `dotnet build` runs, it writes to `bin/Debug/net8.0/` relative to the Trees project. Since both projects share the same output directory (the exe's directory), `AppContext.BaseDirectory` is correct.

The Trees project csproj path (for `dotnet build`) needs to be resolved at runtime relative to the exe's location:
```csharp
// Go up from bin/Debug/net8.0/ to find the Trees project
// Path: AppContext.BaseDirectory/../../../../../../examples/Fbt.Examples.FluentBTree.Trees/Fbt.Examples.FluentBTree.Trees.csproj
```
This path traversal is fragile. Instead, use a hardcoded relative path from the solution root OR embed the path as a constant.

Recommended: use the following approach to find the Trees csproj at runtime:
```csharp
// Walk up from AppContext.BaseDirectory to find the Trees project
string? FindTreesProject()
{
    string? dir = AppContext.BaseDirectory;
    for (int i = 0; i < 10 && dir != null; i++, dir = Path.GetDirectoryName(dir))
    {
        string candidate = Path.Combine(dir, "examples",
            "Fbt.Examples.FluentBTree.Trees",
            "Fbt.Examples.FluentBTree.Trees.csproj");
        if (File.Exists(candidate)) return candidate;
    }
    return null;
}
```

### AssemblyReloadHandler

The handler must:
1. Find `[FbtRegistrar]`-annotated type in the new assembly (this is the generated `FbtActionRegistrar`)
2. Create a new `ActionRegistry<CombatBlackboard, CombatContext>` and call `RegisterAll` on it via reflection
3. Find `[BTreeDefinition]`-annotated methods in the new assembly and invoke them to get blobs
4. Return `(treeName, blob)` pairs

```csharp
IEnumerable<(string, BehaviorTreeBlob)> ReloadHandler(Type registrarType, Assembly newAssembly)
{
    // Create new registry and call RegisterAll
    var newRegistry = new ActionRegistry<CombatBlackboard, CombatContext>();
    var registerAllMethod = registrarType.GetMethod("RegisterAll",
        new[] { typeof(ActionRegistry<CombatBlackboard, CombatContext>) });
    registerAllMethod?.Invoke(null, new object[] { newRegistry });

    // Find [BTreeDefinition] methods and collect blobs
    var results = new List<(string, BehaviorTreeBlob)>();
    foreach (var type in newAssembly.GetTypes())
    {
        foreach (var method in type.GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            var attr = method.GetCustomAttribute(typeof(BTreeDefinitionAttribute));
            if (attr == null) continue;
            string treeName = ((BTreeDefinitionAttribute)attr).TreeName;
            if (method.Invoke(null, null) is BehaviorTreeBlob blob)
                results.Add((treeName, blob));
        }
    }

    // Store newRegistry for interpreter rebuild (via closure or field)
    _pendingRegistry = newRegistry;
    return results;
}
```

Note: `_pendingRegistry` needs to be accessible from the callback. Use a local variable captured in the closure pattern, or a field in the app scope.

### Rebuild flow in Program.cs

After the blob is obtained from `ReloadHandler`, subscribe to `assemblyReloader.OnReloadCompleted`:

```csharp
assemblyReloader.OnReloadCompleted += treeName =>
{
    // _pendingRegistry was set in ReloadHandler; _pendingBlob needs to be stored too
    // Use BTreeHotReloadManager to determine reload type
    var reloadResult = hotReloadManager!.TryReload(
        treeName,
        _pendingBlob,           // new blob from new assembly
        new Span<BehaviorTreeState>(ref state),  // or an array if multiple entities
        (span, _) => span[0] = new BehaviorTreeState());  // hardReset action

    // Rebuild interpreter with new blob and new registry
    interpreter = new Interpreter<CombatBlackboard, CombatContext>(
        _pendingBlob!, _pendingRegistry!);

    if (reloadResult == ReloadResult.HardReset)
        state = new BehaviorTreeState();
    else if (reloadResult == ReloadResult.NewTree)
    {
        state = new BehaviorTreeState();
        bb = new CombatBlackboard { AmmoCount = 5, ThreatVisible = true, EngagementRange = 50f };
    }

    reloadStatus = reloadResult.ToString();
};
```

**Note on `BTreeHotReloadManager.TryReload` signature:** Read the actual method signature in `BTreeHotReloadManager.cs` before implementing. It takes a `Span<TState>` but `TState` here refers to `BehaviorTreeState` in the context of the manager. Verify the exact signature and use it correctly. You may need to pass an array `new BehaviorTreeState[] { state }` instead of a span.

**Note on `SpanResetAction<TState>`:** This is the custom delegate defined in `BTreeHotReloadManager.cs` (because `Action<Span<T>, int>` is invalid with ref structs in .NET 8). Read the actual definition before using it.

### "Recompile & Reload" button in DrawUI

In the `DrawUI` function's Behavior Tree window, add after the reload status text:

```csharp
if (ImGui.Button("Recompile & Reload"))
{
    string? proj = FindTreesProject();
    if (proj != null)
    {
        reloadStatus = "Building...";
        // Run dotnet build asynchronously so UI stays responsive
        System.Threading.Tasks.Task.Run(() =>
        {
            var psi = new System.Diagnostics.ProcessStartInfo("dotnet",
                $"build \"{proj}\" --no-restore -c Debug")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                string err = proc.StandardError.ReadToEnd();
                reloadStatus = $"Build failed: {err.Substring(0, Math.Min(60, err.Length))}";
            }
            // On success, FbtAssemblyHotReloader will detect the new DLL and fire OnReloadCompleted
        });
    }
    else
    {
        reloadStatus = "Trees project not found.";
    }
}
```

### Initialization of hot reload (add near top of Program.cs, before main loop)

```csharp
hotReloadManager = new BTreeHotReloadManager();
assemblyReloader = new FbtAssemblyHotReloader(
    AppContext.BaseDirectory,
    ReloadHandler);

assemblyReloader.OnReloadFailed += (path, ex) =>
{
    reloadStatus = $"Reload failed: {ex.Message.Substring(0, Math.Min(40, ex.Message.Length))}";
};
```

---

## Step 5: Verify Everything Builds

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2

# Trees library builds:
dotnet build FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree.Trees/Fbt.Examples.FluentBTree.Trees.csproj 2>&1 | Select-String "error|warning CS" | Select-Object -First 10

# Visual app builds:
dotnet build FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/Fbt.Examples.FluentBTree.csproj 2>&1 | Select-String "error|warning CS" | Select-Object -First 10

# Tests still pass (160):
dotnet test FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 2
```

---

## Common Pitfalls

1. **File path for Trees project reference** — The `Fbt.Examples.FluentBTree.csproj` is in `examples/Fbt.Examples.FluentBTree/`. The Trees project is in `examples/Fbt.Examples.FluentBTree.Trees/`. The relative path in the csproj is `../Fbt.Examples.FluentBTree.Trees/Fbt.Examples.FluentBTree.Trees.csproj` — NOT the path shown in Step 1 above (Step 1 showed a placeholder; verify the actual relative path).

2. **`interpreter.Blob` property** — Added in BATCH-08. Verify it exists in `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs` before using.

3. **`BTreeHotReloadManager.TryReload` signature** — Read the actual source in `BTreeHotReloadManager.cs`. The `SpanResetAction<TState>` delegate is defined in the same file. Use the exact delegate type.

4. **rlImgui-cs package ID** — The package is `rlImgui-cs` (lowercase 'i' in Imgui) in the csproj, but used as `rlImGui_cs` namespace (capital G). Match what `Fbt.Demo.Visual.csproj` uses exactly.

5. **`ImGui.Selectable` vs `ImGui.Text`** — Use `ImGui.Selectable` for nodes (makes them clickable/hoverable) not `ImGui.Text`. Pass `false` as the second argument (not selected by default).

6. **Top-level statements in Program.cs** — The existing `Program.cs` uses top-level statements. Keep using top-level statements; do NOT introduce a class or `static void Main`. Local functions in top-level statements work correctly in .NET 8.

7. **Thread safety for `reloadStatus`** — The `OnReloadCompleted` callback fires on the app thread (via `DrainPendingCallbacks`), which is called in the main loop. The Task.Run for `dotnet build` runs on a ThreadPool thread. Only update `reloadStatus` from the app thread callbacks, not from the Task.Run itself. The build failure message from `Task.Run` is ok to write directly since it's a string assignment (atomic on 64-bit).

8. **`BTreeHotReloadManager` state across hot reloads** — The manager tracks known blobs. After a reload, the NEW blob should be registered with the manager (it already handles this in `TryReload` if you pass the new blob). Read the source to understand the internal `_knownBlobs` dictionary.

9. **Moving files to Trees project** — After moving, the `Fbt.Tests.csproj` must reference `Fbt.Examples.FluentBTree.Trees` to continue compiling `SampleProjectTests.cs`. The `SampleProjectTests.cs` references `CombatBlackboard`, `CombatActions`, and `AmbushTree` — all of which moved to the Trees project.

---

## Report Requirements

Create `.dev/fluent-btree/reports/BATCH-10-REPORT.md` with:

**Q1:** What GUID was assigned to `Fbt.Examples.FluentBTree.Trees` in `FastBTree.sln`?

**Q2:** Did `interpreter.Blob` exist as a property in `Interpreter.cs`? What is its exact signature?

**Q3:** What is the exact signature of `BTreeHotReloadManager.TryReload`? What delegate type is `SpanResetAction`?

**Q4:** Did the visual app compile without errors? List any warnings encountered.

**Q5:** Did the 160 existing tests continue to pass after the restructuring?

**Q6:** Any deviations from the instructions, and their root cause?

---

## Git Commit

After all builds succeed and 160 tests pass:

1. **FastBTree submodule:**
   ```powershell
   cd d:\Work\IOS-IG-SimHost-FDP-2\FDP\ExtDeps\FastBTree
   git add -A
   git commit -m "FBT-043/045: Phase 5 visual app + hot reload -- Trees library, Raylib/ImGui app, Recompile & Reload button"
   ```

2. **Parent repo:**
   ```powershell
   cd d:\Work\IOS-IG-SimHost-FDP-2
   git add -A
   git commit -m "FBT-043/045: BATCH-10 Phase 5 visual app and hot reload demo"
   ```

---

## Success Criteria

- [ ] `Fbt.Examples.FluentBTree.Trees.csproj` builds without errors.
- [ ] `Fbt.Examples.FluentBTree.csproj` (visual app) builds without errors.
- [ ] 160 tests in `Fbt.Tests` still pass (no regression).
- [ ] `FastBTree.sln` includes both `Fbt.Examples.FluentBTree` and `Fbt.Examples.FluentBTree.Trees` in the `examples` folder.
- [ ] Both git commits (FastBTree + parent repo) are made.
