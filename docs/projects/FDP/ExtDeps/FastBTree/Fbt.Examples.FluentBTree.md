# Fbt.Examples.FluentBTree

**Project Path**: `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/Fbt.Examples.FluentBTree.csproj`
**Date**: 2026-05-23
**Framework**: net8.0
**Output Type**: Executable

---

## README Validation

**Status: Missing.**

No `README.md` exists in the `examples/Fbt.Examples.FluentBTree/` folder or in the `examples/` parent. The `Program.cs` file uses top-level statements and contains inline comments describing the hot-reload workflow, but there is no standalone documentation. A README should be added explaining the Raylib+ImGui setup requirements and the hot-reload development workflow.

---

## Executive Overview

`Fbt.Examples.FluentBTree` is the most advanced FastBTree example project. It demonstrates two complementary features simultaneously:

1. **The Fluent Builder API** (`BTreeBuilder<BB, Ctx>`) - building behavior trees as type-safe C# expressions rather than loading them from JSON. The tree structure and action bindings are expressed in a single fluent chain.

2. **Hot-Reload** - the tree assembly (`Fbt.Examples.FluentBTree.Trees`) is watched for changes at runtime. When the assembly is recompiled, the running tree is transparently updated without restarting the process. Live instances are migrated (soft reload) or reset (hard reload) depending on whether the tree structure changed.

The demo runs a single combat AI agent (the "Ambush_BT" tree) in a Raylib window with an ImGui side panel that exposes the blackboard fields as live sliders and checkboxes. The developer can modify the tree definition in `Fbt.Examples.FluentBTree.Trees`, save, and watch the running agent switch to the new tree behavior within seconds.

Key learning outcomes:
1. How `BTreeBuilder<BB, Ctx>` chains Selector/Sequence/Action/Condition nodes.
2. How `[BTreeAction]` and `[BTreeCondition]` attributes enable source-generator-driven registration.
3. How `[BTreeDefinition("treeName")]` marks a factory method for the source-generated catalog.
4. How `FbtAssemblyHotReloader` and `BTreeHotReloadManager` collaborate to reload live trees.
5. How `ReloadResult` (HardReset, SoftReset, NewTree, NoChange) drives state migration decisions.

---

## Architecture

```
+---[ Fbt.Examples.FluentBTree ]------------------------------+
|                                                             |
|  Program.cs (top-level statements)                          |
|    |                                                        |
|    +-- AmbushTree.CreateInterpreter()   <-- Trees project   |
|    |     -> BTreeBuilder.Compile("Ambush_BT")              |
|    |     -> Interpreter<CombatBlackboard, CombatContext>    |
|    |                                                        |
|    +-- FbtAssemblyHotReloader(BaseDir, ReloadHandler)       |
|    |     Watches Trees .csproj output for .dll changes      |
|    |     Calls ReloadHandler(registrarType, newAssembly)    |
|    |       -> discovers [BTreeDefinition] methods           |
|    |       -> builds new BehaviorTreeBlob + ActionRegistry  |
|    |                                                        |
|    +-- BTreeHotReloadManager                                |
|    |     TryReload(treeName, newBlob, stateSpan, resetFn)   |
|    |     -> compares StructureHash + ParameterHash          |
|    |     -> returns ReloadResult                            |
|    |                                                        |
|    +-- Raylib main loop (60 FPS)                            |
|          interpreter.Tick(ref bb, ref state, ref ctx)       |
|          DrawUI() -> ImGui windows                          |
+-------------------------------------------------------------+
```

```
+---[ Hot-Reload Sequence ]----------------------------------+
|                                                           |
|  Developer edits Trees project source                     |
|    |                                                      |
|    v  (save -> dotnet watch / manual build)               |
|  New .dll written to BaseDirectory                        |
|    |                                                      |
|    v  FbtAssemblyHotReloader detects file change          |
|  Loads new assembly in isolated load context              |
|    |                                                      |
|    v  Scans for [HsmActionRegistrar] type                 |
|  Calls ReloadHandler(registrarType, newAssembly)          |
|    |                                                      |
|    v  ReloadHandler builds new registry + blob            |
|  _pendingBlob, _pendingRegistry stored                    |
|    |                                                      |
|    v  assemblyReloader.OnReloadCompleted fires            |
|  BTreeHotReloadManager.TryReload(...)                     |
|    |                                                      |
|    +-- StructureHash same? -> SoftReset (keep state)      |
|    +-- StructureHash diff? -> HardReset (new state)       |
|    +-- Unknown tree?       -> NewTree                     |
|                                                           |
|  interpreter replaced with new blob + registry            |
+-----------------------------------------------------------+
```

---

## Source Structure

```
Fbt.Examples.FluentBTree/
+-- Program.cs                  All application logic in top-level statements:
|                               - interpreter, bb, state, ctx variables
|                               - FbtAssemblyHotReloader + BTreeHotReloadManager setup
|                               - Raylib window + ImGui main loop
|                               - FindTreesProject() local function
|                               - ReloadHandler() local function
|                               - DrawUI() local function (ImGui windows)
+-- Fbt.Examples.FluentBTree.csproj
                                References:
                                - Fbt.Kernel
                                - Fbt.Compiler
                                - Fbt.Examples.FluentBTree.Trees
                                - Fbt.SourceGen (Analyzer, no output ref)
                                Packages:
                                - ImGui.NET 1.91.6.1
                                - Raylib-cs 7.0.2
                                - rlImgui-cs 3.2.0
```

---

## Public API Used

### AmbushTree (from Trees project)

```csharp
// Creates a fluent builder pre-wired with action delegates
public static BTreeBuilder<CombatBlackboard, CombatContext> CreateBuilder();

// Factory method tagged for source generator
[BTreeDefinition("Ambush_BT")]
public static BehaviorTreeBlob BuildAmbushTree();

// Convenience method: compile + wire registry + create interpreter
public static Interpreter<CombatBlackboard, CombatContext> CreateInterpreter();
```

### FbtAssemblyHotReloader

```csharp
public class FbtAssemblyHotReloader : IDisposable
{
    // assemblyBaseDir: directory to watch for new .dll
    // reloadHandler: factory called when new assembly loaded
    public FbtAssemblyHotReloader(
        string assemblyBaseDir,
        Func<Type, Assembly, IEnumerable<(string, BehaviorTreeBlob)>> reloadHandler);

    public event Action<string, Exception>? OnReloadFailed;
    public event Action<string>? OnReloadCompleted;   // treeName

    // Drain callbacks from the file-watcher thread onto the main thread.
    // Must be called each frame before Tick().
    public void DrainPendingCallbacks();

    public void Dispose();
}
```

### BTreeHotReloadManager

```csharp
public class BTreeHotReloadManager
{
    // Attempt to migrate live instances to a new blob.
    public ReloadResult TryReload<TState>(
        string treeName,
        BehaviorTreeBlob newBlob,
        Span<TState> instances,
        Action<Span<TState>, int> resetInstance)
        where TState : struct;
}

public enum ReloadResult
{
    NoChange,      // Hashes identical; nothing to do
    SoftReset,     // Parameters changed only; state preserved
    HardReset,     // Structure changed; instances reset to initial state
    NewTree        // Previously unknown tree name; added fresh
}
```

### BTreeBuilder (Fbt.Compiler)

```csharp
public class BTreeBuilder<TBlackboard, TContext>
{
    public BTreeBuilder<TBlackboard, TContext> Selector(
        Action<BTreeBuilder<TBlackboard, TContext>> children);

    public BTreeBuilder<TBlackboard, TContext> Sequence(
        Action<BTreeBuilder<TBlackboard, TContext>> children);

    // 4-param full-BB action (NodeLogicDelegate)
    public BTreeBuilder<TBlackboard, TContext> Action(
        NodeLogicDelegate<TBlackboard, TContext> action);

    // 3-param field-projection action (ReusableActionDelegate<TField, TCtx>)
    public BTreeBuilder<TBlackboard, TContext> Action<TField>(
        Expression<Func<TBlackboard, TField>> fieldSelector,
        ReusableActionDelegate<TField, TContext> action);

    // 3-param field-projection condition
    public BTreeBuilder<TBlackboard, TContext> Condition<TField>(
        Expression<Func<TBlackboard, TField>> fieldSelector,
        ReusableConditionDelegate<TField, TContext> condition);

    public BehaviorTreeBlob Compile(string treeName);
    public ActionRegistry<TBlackboard, TContext> GetRegistry();
}
```

---

## Dependencies

| Package / Project | Version / Path | Purpose |
|---|---|---|
| `Raylib-cs` | 7.0.2 | 2D rendering, window management |
| `ImGui.NET` | 1.91.6.1 | Immediate-mode debug UI |
| `rlImgui-cs` | 3.2.0 | Raylib-ImGui bridge |
| `Fbt.Kernel` | (project ref) | Runtime: `Interpreter`, `BehaviorTreeBlob`, `BehaviorTreeState` |
| `Fbt.Compiler` | (project ref) | `BTreeBuilder`, `BTreeHotReloadManager`, `FbtAssemblyHotReloader` |
| `Fbt.Examples.FluentBTree.Trees` | (project ref) | `AmbushTree`, `CombatBlackboard`, `CombatActions`, `CombatContext` |
| `Fbt.SourceGen` | (analyzer) | Source-generates `FbtTreeCatalog` and `HsmActionRegistrar`; not in output assembly |

---

## Usage Examples

### Example 1: Running the FluentBTree Demo

```bash
cd FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree
dotnet run
```

A Raylib window opens (1280x720). Two ImGui panels appear:
- **Blackboard**: sliders for AmmoCount (0-20), ThreatVisible checkbox, EngagementRange slider. All values modify the `CombatBlackboard` live.
- **Tree Inspector**: shows the current running node, reload status, and a "Trigger Reload" button.

Adjust sliders to observe the tree changing behavior:
- Set `ThreatVisible = true` and `AmmoCount > 0` -> tree enters the "Aim and Fire" branch.
- Set `AmmoCount = 0` -> `CheckAmmo` condition returns Failure -> tree falls back to "HoldPosition".
- Set `ThreatVisible = false` -> `HasThreat` condition returns Failure -> tree falls back to "HoldPosition".

### Example 2: How the Fluent Builder is Used

The tree is built in `AmbushTree.CreateBuilder()`:

```csharp
public static BTreeBuilder<CombatBlackboard, CombatContext> CreateBuilder()
{
    return new BTreeBuilder<CombatBlackboard, CombatContext>()
        .Selector(s => s
            .Sequence(seq => seq
                .Condition(dto => dto.ThreatVisible, CombatActions.HasThreat)
                .Condition(dto => dto.AmmoCount, CombatActions.CheckAmmo)
                .Action(dto => dto.AmmoCount, CombatActions.AimAndFire)
            )
            .Action(CombatActions.HoldPosition)
        );
}
```

This is equivalent to the JSON tree:

```json
{
    "TreeName": "Ambush_BT",
    "Root": {
        "Type": "Selector",
        "Children": [
            {
                "Type": "Sequence",
                "Children": [
                    { "Type": "Condition", "Action": "HasThreat" },
                    { "Type": "Condition", "Action": "CheckAmmo" },
                    { "Type": "Action",    "Action": "AimAndFire" }
                ]
            },
            { "Type": "Action", "Action": "HoldPosition" }
        ]
    }
}
```

The lambda `dto => dto.ThreatVisible` is an expression-tree field selector. The compiler uses it to generate a thin shim that reads `CombatBlackboard.ThreatVisible` by offset and passes it as a `ref bool` to `HasThreat`. This allows `HasThreat` to be a simple 3-param delegate working with a `bool` directly, rather than having to accept the full blackboard.

### Example 3: Hot-Reload Workflow

1. Run the demo: `dotnet run`
2. Open a second terminal.
3. Edit `AmbushTree.cs` in the Trees project - for example, change the fallback from `HoldPosition` to a new action.
4. Rebuild the Trees project:
   ```bash
   dotnet build FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree.Trees
   ```
5. The `FbtAssemblyHotReloader` detects the new `.dll` in `BaseDirectory`.
6. `ReloadHandler` is called, producing a new `BehaviorTreeBlob` and `ActionRegistry`.
7. `BTreeHotReloadManager.TryReload()` compares structure hashes:
   - If only parameter values changed (e.g., a Wait duration): `SoftReset` - state is preserved, interpreter is replaced.
   - If the node graph changed (e.g., added a node): `HardReset` - `state` is reset to `new BehaviorTreeState()`.
8. The ImGui panel shows the reload result string: "SoftReset", "HardReset", or "NewTree".

---

## Architecture Diagram: Fluent Builder Internals

```
+---[ BTreeBuilder<BB, Ctx> ]--------------------------------+
|                                                           |
|  .Selector(s => ...)                                      |
|    adds SelectorNode to internal AST                      |
|    recursively processes lambda body                      |
|                                                           |
|  .Sequence(seq => ...)                                    |
|    adds SequenceNode                                      |
|                                                           |
|  .Condition(dto => dto.ThreatVisible, HasThreat)          |
|    Expression<Func<BB, bool>> -> field offset (compile)   |
|    Generates shim: (ref BB bb, ...) => HasThreat(         |
|                         ref Unsafe.As<BB,bool>(bb+offset))|
|    Adds ConditionNode + registers shim in internal registry|
|                                                           |
|  .Action(dto => dto.AmmoCount, AimAndFire)                |
|    Same projection for int field                          |
|                                                           |
|  .Action(HoldPosition)  <-- 4-param full-BB delegate      |
|    Registered directly                                    |
|                                                           |
|  .Compile("Ambush_BT")                                    |
|    AST -> BehaviorTreeBlob (Nodes[], MethodNames[])       |
|                                                           |
|  .GetRegistry()                                           |
|    Returns ActionRegistry with all delegates              |
+-----------------------------------------------------------+
```

---

## Architecture Diagram: Top-Level Statement Program Flow

```
+---[ Program.cs execution order ]---------------------------+
|                                                           |
|  [startup]                                                |
|    AmbushTree.CreateInterpreter() -> interpreter          |
|    new CombatBlackboard { AmmoCount=5, ThreatVisible=true }|
|    new BehaviorTreeState() -> state                       |
|    new CombatContext() -> ctx                             |
|    new BTreeHotReloadManager() -> hotReloadManager        |
|    new FbtAssemblyHotReloader(BaseDir, ReloadHandler)     |
|      subscribe OnReloadFailed, OnReloadCompleted          |
|                                                           |
|  [Raylib.InitWindow + rlImGui.Setup]                      |
|                                                           |
|  [main loop - 60 FPS]                                     |
|    assemblyReloader.DrainPendingCallbacks()  <-- CRITICAL  |
|    if !paused: interpreter.Tick(bb, state, ctx)           |
|    Raylib.BeginDrawing()                                  |
|    rlImGui.Begin() -> DrawUI() -> rlImGui.End()           |
|    Raylib.EndDrawing()                                    |
|                                                           |
|  [OnReloadCompleted event handler]                        |
|    hotReloadManager.TryReload(treeName, blob, states, fn) |
|    interpreter = new Interpreter(pendingBlob, pendingReg) |
|    apply ReloadResult (HardReset -> reset state)          |
|                                                           |
|  [shutdown]                                               |
|    rlImGui.Shutdown()                                     |
|    assemblyReloader.Dispose()                             |
|    Raylib.CloseWindow()                                   |
+-----------------------------------------------------------+
```

---

## Best Practices Illustrated

1. **`DrainPendingCallbacks()` must be called on the main thread each frame.** The file watcher fires on a background thread. `DrainPendingCallbacks()` moves work to the main thread, ensuring `interpreter` is only replaced while no `Tick()` is in progress.

2. **Store the pending reload in fields, not locals.** `_pendingBlob` and `_pendingRegistry` are captured in the `ReloadHandler` closure and read in `OnReloadCompleted`. This decouples the reload data production (on callback thread) from its consumption (on main thread after drain).

3. **Field-projection delegates are reusable across trees.** `CombatActions.CheckAmmo` is a `ReusableConditionDelegate<int, CombatContext>` that works with any tree that has an `int` ammo field. It does not depend on `CombatBlackboard` specifically, enabling code sharing.

4. **`[BTreeDefinition("treeName")]` marks hot-reload entry points.** The source generator and the `FbtAssemblyHotReloader` use reflection to discover methods with this attribute. Every tree that should support hot-reload must have exactly one such factory method.

5. **`assemblyReloader.Dispose()` is critical.** It stops the file watcher and releases the assembly load context. Forgetting to dispose it causes file handle leaks that prevent subsequent builds from overwriting the output DLL.

6. **Blackboard layout must be deterministic.** `CombatBlackboard` is tagged with `[StructLayout(LayoutKind.Sequential)]` and uses explicit `_pad` fields. The fluent builder captures field offsets at compile time; changing layout without recompiling the app would produce wrong offsets.

---

## Extended Usage: Adding a Tree Node with a Cooldown

The fluent builder supports additional node types beyond Selector/Sequence/Action/Condition. Here is how to add a Cooldown node:

```csharp
// A cooldown node prevents its child from executing again
// within a specified duration after the child last succeeded.
public static BTreeBuilder<CombatBlackboard, CombatContext> CreateBuilderWithCooldown()
{
    return new BTreeBuilder<CombatBlackboard, CombatContext>()
        .Selector(s => s
            .Sequence(seq => seq
                .Condition(dto => dto.ThreatVisible, CombatActions.HasThreat)
                .Condition(dto => dto.AmmoCount, CombatActions.CheckAmmo)
                .Cooldown(5.0f, cd => cd   // can only fire once per 5 seconds
                    .Action(dto => dto.AmmoCount, CombatActions.AimAndFire)
                )
            )
            .Action(CombatActions.HoldPosition)
        );
}
```

The `float` parameter to `.Cooldown()` is a duration in seconds. The `BTreeBuilder` records the duration in `blob.FloatParams` and the kernel's Cooldown node reads it via `context.GetFloatParam(payloadIndex)`.

---

## How Hot-Reload Interacts with BehaviorTreeState

When `BTreeHotReloadManager.TryReload()` returns `HardReset`, the instance's `BehaviorTreeState` must be replaced with a fresh one. The demo handles this in the `OnReloadCompleted` event handler:

```csharp
assemblyReloader.OnReloadCompleted += treeName =>
{
    // Wrap current state in a 1-element span for TryReload
    var stateArr = new BehaviorTreeState[] { state };

    var reloadResult = hotReloadManager.TryReload(
        treeName,
        _pendingBlob!,
        stateArr.AsSpan(),
        // resetFn: called for each instance that must be hard-reset
        (span, i) => span[i] = new BehaviorTreeState());

    // Always replace interpreter with new blob + registry
    interpreter = new Interpreter<CombatBlackboard, CombatContext>(
        _pendingBlob!, _pendingRegistry!);

    // Apply state changes based on reload result
    switch (reloadResult)
    {
        case ReloadResult.HardReset:
            state = stateArr[0];  // was reset by resetFn
            bb = new CombatBlackboard { AmmoCount = 5, ThreatVisible = true };
            break;
        case ReloadResult.NewTree:
            state = new BehaviorTreeState();
            bb = new CombatBlackboard { AmmoCount = 5, ThreatVisible = true };
            break;
        // SoftReset or NoChange: keep state unchanged
    }

    reloadStatus = reloadResult.ToString();
};
```

The `resetFn` lambda is called by `TryReload` for each instance whose tree structure changed. It replaces the stale state with a fresh `new BehaviorTreeState()`. The caller then re-creates the blackboard to match the new tree's expectations.

---

## ImGui Panel Layout

The `DrawUI()` local function renders two ImGui windows:

**Window 1: Blackboard**
```
FPS: 60
[ ] Paused
-----
AmmoCount:       [====|====] 5
ThreatVisible:   [X]
EngagementRange: [==|======] 50.0
```

**Window 2: Tree Inspector**
```
Last Reload: SoftReset
[Trigger Reload]
-----
Running Node: 3 (AimAndFire)
Tree: Ambush_BT
-----
[0] Selector
    [1] Sequence
        [2] Condition "HasThreat"
        [3] Condition "CheckAmmo"
        [4] Action "AimAndFire"     <- highlighted
    [5] Action "HoldPosition"
```

The highlighted node shows the `state.RunningNodeIndex` value in yellow, exactly as `Fbt.Demo.Visual` does in its `TreeVisualPanel`.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fbt.Kernel` | Core runtime dependency |
| `Fbt.Compiler` | Provides `BTreeBuilder`, hot-reload infrastructure |
| `Fbt.Examples.FluentBTree.Trees` | Contains `AmbushTree`, `CombatActions`, `CombatBlackboard` - the actual tree definitions loaded by this app |
| `Fbt.SourceGen` | Analyzer-only; generates `FbtTreeCatalog` and action registrar code at build time |
| `Fbt.Examples.Console` | Simpler sibling that uses JSON trees instead of the fluent API |
| `Fbt.Demo.Visual` | Multi-agent demo with three tree types and a full inspector UI |
