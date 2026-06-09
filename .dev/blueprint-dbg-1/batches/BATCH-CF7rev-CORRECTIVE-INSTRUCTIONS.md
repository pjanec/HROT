# BATCH-CF7rev-CORRECTIVE — Fix auto-instrumentation in production code

**Batch Number:** BATCH-CF7rev-CORRECTIVE  
**Tasks:** CF-7-rev (fix production callback + add real integration test)  
**Priority:** HIGH — P0 (feature broken in production)  
**Dependencies:** BATCH-CF7rev

---

## 📋 Onboarding & Workflow

### What Went Wrong

The user tested the feature: "set bp (no Compile) → sim does NOT pause. After clicking Compile → sim DOES pause." The end-to-end test passes but is a **false positive** — it bypasses `QuickReloadService` entirely. The production callback in `EditorSubsystem` has a critical bug in how it loads the asset.

### Root Cause (confirmed by code review)

The callback in `EditorSubsystem.cs` loads the asset from disk with:
```csharp
var options = new JsonSerializerOptions
{
    IncludeFields = true,
    PropertyNameCaseInsensitive = true,
};
var asset = JsonSerializer.Deserialize<BlueprintAsset>(json, options);
```

This is **wrong** — `BlueprintAsset` contains polymorphic types (`List<Node>` where `Node` has derived types like `FunctionCallNode`, `SetVariableNode`, etc.) and enum properties stored as strings. The plain `JsonSerializerOptions` lacks:
- **`JsonStringEnumConverter`** — enums are stored as strings in `.bp.json` (`"Kind": "Function"`, `"Direction": "Input"`)
- **`AllowTrailingCommas = true`** — safety against generated JSON
- **`ReadCommentHandling = JsonCommentHandling.Skip`** — safety against commented JSON

This causes deserialization to either throw (caught by try-catch, silently swallowed — `Console.WriteLine` is invisible in the editor) or produce malformed assets that fail to compile. The **correct** deserialization function is `BlueprintJsonServices.Deserialize(json)`.

Additionally, the test at `CF7rev_EndToEndTests.SetBreakpoint_TriggersAutoInstrument_ThenPauses` bypasses QuickReloadService entirely — it calls `CompileCount4` + `fixture.CompileAndLoad` directly, which is a different code path from production. The test proves the callback mechanism works but NOT the production pipeline.

### Required Reading
1. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/BlueprintJsonServices.cs` — the CORRECT deserializer
2. Current state of `EditorSubsystem.cs` around line 2505-2547 — the broken callback
3. Current state of `CF7rev_EndToEndTests.cs` — the false-positive test

---

## 🎯 Batch Objectives

1. Fix the EditorSubsystem callback to use `BlueprintJsonServices.Deserialize`
2. Add proper logging via the existing output console (not `Console.WriteLine`)
3. Fix the end-to-end test to exercise the ACTUAL QuickReloadService pipeline
4. Add a defensive test proving the callback's asset loading produces a compilable asset

---

## ✅ Tasks

### Task 0 (CORRECTIVE): Fix EditorSubsystem callback asset loading

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (UPDATE)

**Problem:** The callback uses plain `JsonSerializer.Deserialize<BlueprintAsset>` which can't correctly read `.bp.json` files (missing enum converter, etc.).

**Fix:** Replace the deserialization block with `BlueprintJsonServices.Deserialize`. Also improve logging to use the `qrsConsole` (an `IOutputConsole`) instead of `Console.WriteLine` which is invisible in the editor.

**Required changes:**

1. **Replace the asset loading code** (around lines 2528-2536):
   ```csharp
   // BEFORE (broken):
   var json = System.IO.File.ReadAllText(filePath);
   var options = new JsonSerializerOptions
   {
       IncludeFields = true,
       PropertyNameCaseInsensitive = true,
   };
   var asset = JsonSerializer.Deserialize<BlueprintAsset>(json, options);
   if (asset == null) return;
   
   // AFTER (fixed):
   var json = System.IO.File.ReadAllText(filePath);
   var asset = BlueprintJsonServices.Deserialize(json);
   if (asset == null) return;
   ```

2. **Add usings:** `using Hrot.Blueprints.Core;` (for `BlueprintJsonServices`) — verify it's already imported; if not, add it.

3. **Improve logging:** Replace `Console.WriteLine(...)` calls with proper logging via the output console. Use the `_hotReloadSource` pattern already used elsewhere in EditorSubsystem:
   ```csharp
   // Instead of:
   Console.WriteLine($"[BP] warning: Auto-instrumentation: asset {assetId} not found in catalog.");
   // Use:
   _hotReloadSource?.LogWarning($"Auto-instrumentation: asset {assetId} not found in catalog.");
   
   // Instead of:
   Console.WriteLine($"[BP] error: Auto-instrumentation failed for asset {assetId}: {ex.Message}");
   // Use:
   _hotReloadSource?.LogError($"Auto-instrumentation failed for asset {assetId}: {ex.Message}");
   ```
   
   Also add a success log:
   ```csharp
   asset.EditorMetadata.CompilerMode = mode;
   await _blueprintQuickReloadService.TriggerAsync(asset);
   _hotReloadSource?.LogInfo($"Auto-instrumentation: {asset.Name} compiled in {mode} mode.");
   ```

### Task 1: Fix the false-positive end-to-end test

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CF7rev_EndToEndTests.cs` (UPDATE)

**Problem:** `SetBreakpoint_TriggersAutoInstrument_ThenPauses` test callback bypasses QuickReloadService — it calls `CompileCount4` + `fixture.CompileAndLoad` directly.

**Fix:** Replace the test callback to use the SAME code path as production: `BlueprintJsonServices.Deserialize` + `QuickReloadService.TriggerAsync`. This requires constructing a minimal QuickReloadService in the test.

Since `QuickReloadService` needs an `AiHotReloadCoordinator`, we can construct one with the fixture's registry:
```csharp
var coordinator = new AiHotReloadCoordinator(
    fixture.Registry.BehaviorRegistry,  // or however it's accessed
    fixture.Registry,
    new AiHotReloadCoordinatorOptions());
```

**ALTERNATIVE (simpler):** If constructing the full QuickReloadService pipeline is too complex for a unit test, write a more focused test that proves the **asset loading** works correctly:

```csharp
[Fact]
public void CallbackAssetLoading_Uses_BlueprintJsonServices_ProducesCompilableAsset()
{
    // Load Count4 using the SAME code as the production callback.
    var repoRoot = ResolveRepoRoot();
    var assetPath = Path.Combine(repoRoot,
        "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Blueprints", "Count4.bp.json");
    var json = File.ReadAllText(assetPath);
    
    // THIS is the production callback's loading code — must use BlueprintJsonServices.
    var asset = BlueprintJsonServices.Deserialize(json);
    Assert.NotNull(asset);
    
    // Set CompilerMode.Debug (what the callback does).
    asset.EditorMetadata.CompilerMode = CompilerMode.Debug;
    
    // Compile — must succeed.
    var options = new CompileOptions(
        Mode: CompilerMode.Debug,
        NodeRegistry: BuiltInNodeRegistry.Instance,
        TypeRegistry: StaticTypeRegistry.Instance,
        EngineEvents: BuiltInEngineEventCatalog.Instance,
        ChannelCommands: BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives: BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());
    
    var compiler = new BlueprintCompiler();
    var result = compiler.Compile(asset, options);
    
    Assert.True(result.Succeeded, 
        $"Compilation failed: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}");
    Assert.NotNull(result.DebugMap);
    Assert.NotEmpty(result.DebugMap.Entries);
}
```

And rename/modify the existing `SetBreakpoint_TriggersAutoInstrument_ThenPauses` to be explicit about what it tests (callback mechanism + re-resolution), and add a comment noting it uses a synthetic callback (not the production QuickReloadService path) because the full QuickReloadService requires a running editor coordinator.

### Task 2: Verify the fix against the Count4 asset

**Test:** Compile-and-run a quick verification — load `Count4.bp.json` with both `JsonSerializer.Deserialize<BlueprintAsset>(json, plainOptions)` (the BROKEN path) and `BlueprintJsonServices.Deserialize(json)` (the FIXED path), and verify the former fails or produces a different (wrong) asset.

Add this as a test in `CF7rev_EndToEndTests.cs`:

```csharp
[Fact]
public void PlainJsonDeserialization_FailsOrProducesDifferentAsset_ThanBlueprintJsonServices()
{
    var repoRoot = ResolveRepoRoot();
    var assetPath = Path.Combine(repoRoot,
        "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Blueprints", "Count4.bp.json");
    var json = File.ReadAllText(assetPath);
    
    // Production-correct path:
    var correctAsset = BlueprintJsonServices.Deserialize(json);
    Assert.NotNull(correctAsset);
    
    // The BROKEN path (what the current callback does):
    var brokenOptions = new JsonSerializerOptions
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
    };
    BlueprintAsset? brokenAsset = null;
    bool brokenThrew = false;
    try
    {
        brokenAsset = JsonSerializer.Deserialize<BlueprintAsset>(json, brokenOptions);
    }
    catch (JsonException)
    {
        brokenThrew = true;
    }
    
    // The broken path either throws OR produces null OR produces an asset with mismatched graphs.
    // At minimum, document what actually happens.
    Assert.True(brokenThrew || brokenAsset == null 
        || brokenAsset.Graphs.Count != correctAsset.Graphs.Count
        || brokenAsset.Graphs[0].Nodes.Count != correctAsset.Graphs[0].Nodes.Count,
        "Expected the plain-JsonSerializer path to fail or produce different results. " +
        "If this passes, the deserialization may not be the root cause.");
}
```

Note: If this test fails (i.e., plain deserialization works identically), report it — the root cause is elsewhere.

---

## 🧪 Testing Requirements

All existing CF7rev tests must still pass. The corrected end-to-end test must still verify:
- Callback fires on SetBreakpoint when no DebugMap
- RegisterDebugMap re-resolves ProbeNodeId
- Breakpoint causes pause after auto-instrumentation

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `dotnet build IOS-IG-SimHost.sln -c Debug` → 0 errors
- [ ] All CF7rev tests pass (8 original + new tests)
- [ ] Blueprints full suite: 7 pre-existing, 0 new
- [ ] EditorSubsystem callback uses `BlueprintJsonServices.Deserialize`
- [ ] Logging uses `_hotReloadSource` (visible in editor), not `Console.WriteLine`
- [ ] New test `PlainJsonDeserialization_FailsOrProducesDifferentAsset_ThanBlueprintJsonServices` confirms the root cause
- [ ] New test `CallbackAssetLoading_Uses_BlueprintJsonServices_ProducesCompilableAsset` passes
- [ ] Report submitted with diagnosis and fix details

---

## ⚠️ Common Pitfalls

- Do NOT change `BlueprintJsonServices` — it's the correct deserializer.
- Do NOT add `JsonStringEnumConverter` to the callback's plain options — use `BlueprintJsonServices.Deserialize` instead. One source of truth.
- The test proving the broken path may pass (if `System.Text.Json` in .NET 8 silently handles string enums). If so, the root cause is NOT deserialization — report this and do NOT force the test to fail.
