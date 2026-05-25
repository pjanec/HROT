# WHEN-BATCH-13 — M7 Behavior Recipes + NewFromRecipeService

## Context

Batch 13 implements Phase M7 of the When-Node blueprint feature. M7 ships five curated
behavior recipes (`.bp.json` files) and a `NewFromRecipeService` that clones a recipe
into a named user asset.

**TASK-DETAIL reference:** `.dev/blueprints-3-when-node/TASK-DETAIL.md` §Phase M7  
**DESIGN reference:** `.dev/blueprints-3-when-node/When_Reactivity_Iteration_Design_v2_2.md` §12 and §13

**All prior M0–M6 tasks are complete and committed.**

---

## Scope

| Task   | Deliverable |
|--------|-------------|
| M7-T1a | Bug fix: BP2003 must NOT fire for `PeerBlueprintVariable` source (ComponentTypeId/PropertyPath are not required for that source) |
| M7-T1b | Add `RecipeMetadata` class + `AssetMetadata.Recipe` property |
| M7-T1c | Create 5 recipe `.bp.json` files in `Hrot.Blueprints.Tests/TestAssets/Recipes/` |
| M7-T1d | `RecipeIntegrityTests.cs` with all named tests |
| M7-T2  | `NewFromRecipeService` + unit tests |

---

## M7-T1a: Fix BP2003 bug for PeerBlueprintVariable

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage2_Validate.cs`

In `ValidateValueChanged()`, the BP2003 check currently fires unconditionally when
`ComponentTypeId` or `PropertyPath` is empty. For `PeerBlueprintVariable` source, these
fields are intentionally empty — the component path is replaced by `PeerBlueprintAssetId`
+ `PeerVariableName`.

**Change this:**
```csharp
// BP2003 -- invalid property path
if (string.IsNullOrEmpty(vc.ComponentTypeId) || string.IsNullOrEmpty(vc.PropertyPath))
    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2003,
        "WhenNode ValueChanged: ComponentTypeId and PropertyPath must not be empty.",
        asset.AssetId, graph.Id, node.Id));
```

**To this:**
```csharp
// BP2003 -- invalid property path (not applicable for PeerBlueprintVariable source)
if (vc.Source != ValueChangedSource.PeerBlueprintVariable
    && (string.IsNullOrEmpty(vc.ComponentTypeId) || string.IsNullOrEmpty(vc.PropertyPath)))
    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2003,
        "WhenNode ValueChanged: ComponentTypeId and PropertyPath must not be empty.",
        asset.AssetId, graph.Id, node.Id));
```

Also add a regression test in `WhenNodeValidatorTests.cs`:

```csharp
[Fact]
public void Validate_PeerVariableSource_EmptyComponentFields_NoBP2003()
{
    // PeerBlueprintVariable does NOT require ComponentTypeId/PropertyPath.
    var peerId = Guid.NewGuid();
    var sig    = new BlueprintSignature(peerId, "SquadState",
        new List<BlueprintVariableSignature>
        {
            new("ThreatLevel", new BlueprintTypeRef { TypeId = "System.Single" })
        });
    var node = new WhenNode
    {
        Id   = Guid.NewGuid(),
        Mode = WhenMode.ValueChanged,
        Edges = WhenEdge.RisingEdge,
        ValueChanged = new ValueChangedPayload
        {
            ComponentTypeId      = "",            // intentionally empty
            PropertyPath         = "",            // intentionally empty
            Source               = ValueChangedSource.PeerBlueprintVariable,
            PeerBlueprintAssetId = peerId,
            PeerVariableName     = "ThreatLevel",
        },
    };
    var diags = ValidateInstance(node, DefaultOptions(siblings: new[] { sig }));
    Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP2003);
    // BP2004 should also not fire (peer is in siblings)
    Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP2004);
}
```

> **Note on `BlueprintSignature`:** check the actual constructor signature of `BlueprintSignature` in
> `Hrot.Blueprints.Core.Compiler` to match its exact parameter types. Also check
> `BlueprintVariableSignature` — adjust the test if the types or constructor differ.

---

## M7-T1b: Add RecipeMetadata to AssetMetadata

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/GraphTypes.cs`

Add `RecipeMetadata` class and `Recipe` property to `AssetMetadata`:

```csharp
public sealed class AssetMetadata
{
    public string? Description { get; set; }
    public string? Category { get; set; }
    public RecipeMetadata? Recipe { get; set; }   // NEW
}

public sealed class RecipeMetadata
{
    public string DisplayName { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string Difficulty { get; set; } = "Beginner";
    public List<string> ConceptsTaught { get; set; } = new();
}
```

---

## M7-T1c: Recipe JSON files

Place recipe files in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/`:
- `CoverAwarePatrol.bp.json`
- `HealthThresholdReaction.bp.json`
- `SquadAwareEngagement.bp.json`
- `MoveAndFireCombo.bp.json`
- `SquadState.bp.json`

Also register them as `<Content CopyToOutputDirectory="PreserveNewest">` in
`Hrot.Blueprints.Tests.csproj` using a glob:
```xml
<Content Include="TestAssets\Recipes\**\*.bp.json">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```

### JSON format rules

1. Use **PascalCase** for all JSON property names — matching `simple-action.bp.json`, `MoveToAndFire.bp.json`, etc.
2. Use **real GUIDs** for all Id fields (not symbolic names like `"n-tick-entry"`). You may use deterministic fake GUIDs (pattern: `{recipe-digit}0000001-0000-0000-0000-{sequence}`).
3. Channel ActionIds must match exactly what `BuiltInChannelCommandCatalog` has: `"MoveTo"`, `"FollowRoute"`, `"AimAndFire"`, `"OpenDoor"`, `"EjectPassengers"`.
4. `"Links"` entries use `"FromNodeId"`, `"FromPinId"`, `"ToNodeId"`, `"ToPinId"` (all GUIDs). These GUIDs reference pins — you can use short deterministic GUIDs like `e0000001-0000-0000-0000-{seq}`.
5. **Nodes may have `"Pins": []`** — the compiler infers exec pins from node types.
6. Ensure each recipe has `"EditorMetadata": { "Recipe": { ... } }` at the asset level (NOT the graph level).

### Recipe 1 — CoverAwarePatrol.bp.json

Stable `AssetId: "00000000-aaaa-0001-0000-000000000001"`. Instance Blueprint.

Must contain these node kinds for `CoverAwarePatrol_UsesAllThreeNewNodes` test:
- `"SpawnEqsSensor"` node with `"TemplateAssetId": "00000000-cccc-0001-0000-000000000001"`
- `"When"` node with `"Mode": "EqsResult"`, `"Edges": "RisingEdge"`, `"EqsResult": { "SensorVariableName": "CoverQuery", "Trigger": "TopChanged", "ScoreThreshold": 0, "MaxAgeSeconds": 0 }`
- `"ReadEqsResult"` node with `"SensorVariableName": "CoverQuery"`

Variables include:
- `CoverQuery` of type `"FDP.Eqs.EqsSensorHandle"`
- `Initialized` of type `"System.Boolean"`
- `PatrolTarget` of type `"System.Numerics.Vector2"`

Graph `Tick` of Kind `"Event"`.

Recipe metadata:
```json
"Recipe": {
  "DisplayName": "Cover-aware Patrol",
  "Category": "Combat",
  "Description": "Complete end-to-end EQS pipeline demonstration. First-tick branch gates one-shot sensor setup; WhenNode observes the sensor for top-result changes; ReadEqsResultNode extracts position; MoveTo channel command issued. Demonstrates all three new nodes.",
  "Difficulty": "Beginner",
  "ConceptsTaught": [
    "Declaring an EqsSensorHandle variable",
    "First-tick pattern: using an Initialized boolean to gate one-shot setup",
    "SpawnEqsSensorNode with SearchRadius and FactionFilter pins",
    "WhenNode in EQS Result mode (TopChanged trigger)",
    "ReadEqsResultNode for downstream data extraction",
    "The complete spawn → observe → read → act pipeline"
  ]
}
```

Simplified graph structure (nodes without full links is OK for M7 tests):
Nodes: `EventEntry`, `GetVariable` (Initialized), `Branch`, `SpawnEqsSensor`, `SetVariable` (Initialized=true), `When` (EqsResult), `ReadEqsResult`, `ChannelCommand` (MoveTo).
Links: wire exec pins from EventEntry → Branch → SpawnEqsSensor → WhenNode → ChannelCommand. For links, ensure `"ActionId": "MoveTo"` (matching catalog).

### Recipe 2 — HealthThresholdReaction.bp.json

Stable `AssetId: "00000000-aaaa-0001-0000-000000000002"`. Instance Blueprint.

Nodes: `EventEntry`, `When` (ConditionMet, RisingEdge, `ConditionMet` with non-null condition), `ChannelCommand` (FollowRoute — use existing catalog entry, NOT "Flee" which doesn't exist).

Variables: none required.

Recipe metadata:
```json
"Recipe": {
  "DisplayName": "Health-threshold Reaction",
  "Category": "Combat",
  "Description": "Reacts when a condition is met (e.g., low health in combat) by issuing a channel command. Demonstrates WhenNode Condition Met mode with edge-triggered behavior changes.",
  "Difficulty": "Beginner",
  "ConceptsTaught": [
    "WhenNode Condition Met mode",
    "Edge-triggered behavior changes",
    "Connecting OnFired to channel commands"
  ]
}
```

**Note on ConditionMet payload:** Set `"ConditionMet": { "Condition": null }` in JSON. The validator allows null condition (no BP2xxx for it).

### Recipe 3 — SquadAwareEngagement.bp.json

Stable `AssetId: "00000000-aaaa-0001-0000-000000000003"`. Instance Blueprint.
`"CallablePeers": ["00000000-aaaa-0001-0000-000000000005"]` (references SquadState, Recipe 5).

Nodes: `EventEntry`, `When` (ValueChanged, PeerBlueprintVariable source, PeerBlueprintAssetId = `"00000000-aaaa-0001-0000-000000000005"`, PeerVariableName = `"ThreatLevel"`).

For the `ValueChanged` payload:
```json
"ValueChanged": {
  "ComponentTypeId": "",
  "PropertyPath": "",
  "Epsilon": 0,
  "Source": "PeerBlueprintVariable",
  "PeerBlueprintAssetId": "00000000-aaaa-0001-0000-000000000005",
  "PeerVariableName": "ThreatLevel"
}
```

**Important:** This requires the BP2003 fix from M7-T1a. Without it, validation fails.

Recipe metadata (≥ 2 concepts):
```json
"Recipe": {
  "DisplayName": "Squad-aware Engagement",
  "Category": "Combat",
  "Description": "Reacts to changes in a peer Blueprint's variable (SquadState.ThreatLevel). Demonstrates WhenNode Value Changed with peer-variable source and callablePeers declaration.",
  "Difficulty": "Intermediate",
  "ConceptsTaught": [
    "callablePeers declaration",
    "WhenNode Value Changed / peer source",
    "Cross-Blueprint variable access"
  ]
}
```

### Recipe 4 — MoveAndFireCombo.bp.json

Stable `AssetId: "00000000-aaaa-0001-0000-000000000004"`. **AiPrimitive** dispatch.

```json
"Dispatch": "AiPrimitive",
"Primitive": { "Intent": "Action", "Hostings": ["BTreeAction", "HsmAction"] }
```

Nodes: `EventEntry`, `ChannelCommand` (ActionId: `"MoveTo"`, ChannelType: `"LocomotionChannel"`), `WaitForChannel` (ChannelType: `"LocomotionChannel"`), `ChannelCommand` (ActionId: `"AimAndFire"`, ChannelType: `"WeaponChannel"`), `WaitForChannel` (ChannelType: `"WeaponChannel"`), `Return` (Status: `"Success"`), `Return` (Status: `"Failure"`).

Recipe metadata (≥ 2 concepts):
```json
"Recipe": {
  "DisplayName": "Move and Fire Combo",
  "Category": "Combat",
  "Description": "Sequential AiPrimitive: move to position then fire. Demonstrates ChannelCommand + WaitForChannel pattern (imperative, not reactive). Use this for BTree/HSM actions where A→B→C sequencing is needed.",
  "Difficulty": "Beginner",
  "ConceptsTaught": [
    "AiPrimitive with multi-hosting (BTreeAction + HsmAction)",
    "ChannelCommand + WaitForChannel for sequential coordination",
    "When NOT to use WhenNode (AiPrimitives stay imperative)"
  ]
}
```

### Recipe 5 — SquadState.bp.json

Stable `AssetId: "00000000-aaaa-0001-0000-000000000005"`. Instance Blueprint.

Variables:
- `ThreatLevel` (System.Single)
- `SquadSize` (System.Int32)
- `Formation` (System.Byte)

Graph: `GetThreatLevel` (Kind: `"Function"`) with `Outputs: [{ "Id": "...", "Name": "ThreatLevel", "Type": { "TypeId": "System.Single" } }]`.
Nodes: `EventEntry`, `GetVariable` (variableId pointing to ThreatLevel), `Return`.

Recipe metadata (≥ 2 concepts):
```json
"Recipe": {
  "DisplayName": "Squad Shared State (template)",
  "Category": "Shared",
  "Description": "Holds cross-cutting squad state with pure getter graphs. Other Instance Blueprints declare this in callablePeers and read state via peer calls or WhenNode Value Changed (peer source). Use this pattern for any shared singleton entity state.",
  "Difficulty": "Intermediate",
  "ConceptsTaught": [
    "Instance Blueprint as a state container",
    "Pure-function graphs with output parameters",
    "Designed to be referenced via callablePeers",
    "The EntityState/SquadState shared-state pattern"
  ]
}
```

---

## M7-T1d: RecipeIntegrityTests.cs

Place in `Hrot.Blueprints.Tests/Compiler/` (or a new `Hrot.Blueprints.Tests/Recipes/` subfolder).

### Loading helper

```csharp
private static BlueprintAsset LoadRecipe(string name)
{
    var dir  = TestData.ResolveTestAssetsDir();
    var path = Path.Combine(dir, "Recipes", name + ".bp.json");
    var json = File.ReadAllText(path);
    return BlueprintJsonServices.Deserialize(json)
        ?? throw new InvalidDataException($"Null from '{path}'");
}

private static IEnumerable<string> AllRecipeNames() =>
    new[]
    {
        "CoverAwarePatrol", "HealthThresholdReaction",
        "SquadAwareEngagement", "MoveAndFireCombo", "SquadState"
    };
```

### Compile options for recipe tests

Use an **empty** channel command catalog to bypass channel-type validation (recipe tests
verify structure, not dispatch semantics):

```csharp
private static CompileOptions RecipeCompileOptions(
    IReadOnlyList<BlueprintSignature>? siblings = null) =>
    new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   EmptyChannelCommandCatalog.Instance,   // skip channel validation
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: siblings ?? Array.Empty<BlueprintSignature>());

// Put this inner class or nested class somewhere accessible:
private sealed class EmptyChannelCommandCatalog : IChannelCommandCatalog
{
    public static readonly EmptyChannelCommandCatalog Instance = new();
    public IReadOnlyList<ChannelCommandCatalogEntry> GetEntries() =>
        Array.Empty<ChannelCommandCatalogEntry>();
}
```

For Recipe 3 (SquadAwareEngagement), pass SquadState's signature as a sibling:
```csharp
private static BlueprintSignature MakeSquadStateSignature()
{
    var squadState = LoadRecipe("SquadState");
    // BlueprintSignature constructor — check the actual type signature.
    // It takes (Guid assetId, string name, IReadOnlyList<BlueprintVariableSignature> variables).
    // BlueprintVariableSignature takes (string name, BlueprintTypeRef typeRef).
    var vars = squadState.Variables.Select(v =>
        new BlueprintVariableSignature(v.Name, v.Type)).ToList();
    return new BlueprintSignature(squadState.AssetId, squadState.Name, vars);
}
```

> **Important:** Check the actual constructor of `BlueprintSignature` and
> `BlueprintVariableSignature` in the compiler before using them. Adjust accordingly.

### Tests to implement

```csharp
[Theory]
[InlineData("CoverAwarePatrol")]
[InlineData("HealthThresholdReaction")]
[InlineData("SquadAwareEngagement")]
[InlineData("MoveAndFireCombo")]
[InlineData("SquadState")]
public void AllRecipes_Parse(string name)
{
    var asset = LoadRecipe(name);
    Assert.NotEqual(Guid.Empty, asset.AssetId);
    Assert.NotEmpty(asset.Name);
}

[Theory]
[InlineData("CoverAwarePatrol")]
[InlineData("HealthThresholdReaction")]
[InlineData("SquadAwareEngagement")]
[InlineData("MoveAndFireCombo")]
[InlineData("SquadState")]
public void AllRecipes_HaveDescriptionsAndConcepts(string name)
{
    var asset = LoadRecipe(name);
    Assert.NotNull(asset.EditorMetadata.Recipe);
    Assert.NotEmpty(asset.EditorMetadata.Recipe!.Description);
    Assert.True(asset.EditorMetadata.Recipe.ConceptsTaught.Count >= 2,
        $"{name}: expected >= 2 ConceptsTaught, got {asset.EditorMetadata.Recipe.ConceptsTaught.Count}");
}

[Theory]
[InlineData("CoverAwarePatrol")]
[InlineData("HealthThresholdReaction")]
[InlineData("MoveAndFireCombo")]
[InlineData("SquadState")]
public void AllRecipes_ValidateOnly_NoErrors(string name)
{
    // Recipes 1, 2, 4, 5 compile without siblings.
    var asset  = LoadRecipe(name);
    var opts   = RecipeCompileOptions();
    var result = new BlueprintCompiler().Compile(asset, opts);
    var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    Assert.Empty(errors);
}

[Fact]
public void SquadAwareEngagement_ValidateOnly_NoErrors()
{
    // Recipe 3 needs SquadState (Recipe 5) as a sibling to pass BP2004.
    var asset  = LoadRecipe("SquadAwareEngagement");
    var opts   = RecipeCompileOptions(siblings: new[] { MakeSquadStateSignature() });
    var result = new BlueprintCompiler().Compile(asset, opts);
    var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    Assert.Empty(errors);
}

[Fact]
public void CoverAwarePatrol_UsesAllThreeNewNodes()
{
    var asset = LoadRecipe("CoverAwarePatrol");
    var allNodes = asset.Graphs.SelectMany(g => g.Nodes).ToList();
    Assert.Contains(allNodes, n => n is WhenNode);
    Assert.Contains(allNodes, n => n is ReadEqsResultNode);
    Assert.Contains(allNodes, n => n is SpawnEqsSensorNode);
}

[Theory]
[InlineData("CoverAwarePatrol",         "00000000-aaaa-0001-0000-000000000001")]
[InlineData("HealthThresholdReaction",  "00000000-aaaa-0001-0000-000000000002")]
[InlineData("SquadAwareEngagement",     "00000000-aaaa-0001-0000-000000000003")]
[InlineData("MoveAndFireCombo",         "00000000-aaaa-0001-0000-000000000004")]
[InlineData("SquadState",              "00000000-aaaa-0001-0000-000000000005")]
public void AllRecipes_HaveStableAssetIds(string name, string expectedId)
{
    var asset1 = LoadRecipe(name);
    var asset2 = LoadRecipe(name);
    Assert.Equal(new Guid(expectedId), asset1.AssetId);
    Assert.Equal(asset1.AssetId, asset2.AssetId);
}

[Fact]
public void CrossReferenceResolves_SquadAware_ReferencesSquadState()
{
    var squadAware = LoadRecipe("SquadAwareEngagement");
    var squadStateId = new Guid("00000000-aaaa-0001-0000-000000000005");
    Assert.Contains(squadAware.CallablePeers, id => id == squadStateId);
}
```

---

## M7-T2: NewFromRecipeService

### Where to put it

`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NewFromRecipeService.cs`
Namespace: `Hrot.Blueprints.Editor`

### Implementation

```csharp
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor;

/// <summary>
/// Creates a new Blueprint asset from a recipe template by cloning its structure
/// and assigning a fresh identity.
/// </summary>
public sealed class NewFromRecipeService
{
    /// <summary>
    /// Clones <paramref name="recipe"/> into a new asset with a fresh AssetId and the
    /// given <paramref name="newName"/>. The <c>EditorMetadata.Recipe</c> block is
    /// stripped from the clone so the copy is not itself treated as a recipe.
    /// </summary>
    /// <returns>The new (unregistered) asset, ready for the host to save and register.</returns>
    public BlueprintAsset CreateFromRecipe(BlueprintAsset recipe, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("newName must not be empty.", nameof(newName));

        var json  = BlueprintJsonServices.Serialize(recipe);
        var clone = BlueprintJsonServices.Deserialize(json)
                    ?? throw new InvalidOperationException("Serialization round-trip returned null.");

        clone.AssetId = Guid.NewGuid();
        clone.Name    = newName;
        clone.EditorMetadata.Recipe = null;  // strip recipe metadata from the copy

        return clone;
    }
}
```

### Tests for NewFromRecipeService

Place in `Hrot.Blueprints.Tests/Editor/NewFromRecipeServiceTests.cs`:

```csharp
namespace Hrot.Blueprints.Tests.Editor;

public sealed class NewFromRecipeServiceTests
{
    private static BlueprintAsset MakeRecipe(string name = "MyRecipe") =>
        new BlueprintAsset
        {
            AssetId = Guid.NewGuid(),
            Name    = name,
            Dispatch = BlueprintDispatchKind.Instance,
            EditorMetadata = new AssetMetadata
            {
                Recipe = new RecipeMetadata
                {
                    DisplayName    = "My Recipe",
                    Description    = "Test recipe",
                    ConceptsTaught = new List<string> { "ConceptA", "ConceptB" }
                }
            }
        };

    [Fact]
    public void CreateFromRecipe_AssignsFreshAssetId()
    {
        var svc    = new NewFromRecipeService();
        var recipe = MakeRecipe();
        var clone  = svc.CreateFromRecipe(recipe, "MyCopy");
        Assert.NotEqual(recipe.AssetId, clone.AssetId);
        Assert.NotEqual(Guid.Empty, clone.AssetId);
    }

    [Fact]
    public void CreateFromRecipe_SetsNewName()
    {
        var svc   = new NewFromRecipeService();
        var clone = svc.CreateFromRecipe(MakeRecipe(), "FancyName");
        Assert.Equal("FancyName", clone.Name);
    }

    [Fact]
    public void CreateFromRecipe_StripsRecipeMetadata()
    {
        var svc    = new NewFromRecipeService();
        var recipe = MakeRecipe();
        Assert.NotNull(recipe.EditorMetadata.Recipe);  // sanity-check recipe has metadata

        var clone  = svc.CreateFromRecipe(recipe, "Copy");
        Assert.Null(clone.EditorMetadata.Recipe);
    }

    [Fact]
    public void CreateFromRecipe_PreservesDispatch()
    {
        var svc    = new NewFromRecipeService();
        var recipe = MakeRecipe();
        var clone  = svc.CreateFromRecipe(recipe, "Copy");
        Assert.Equal(recipe.Dispatch, clone.Dispatch);
    }

    [Fact]
    public void CreateFromRecipe_DoesNotMutateOriginal()
    {
        var svc       = new NewFromRecipeService();
        var recipe    = MakeRecipe();
        var origId    = recipe.AssetId;
        var origName  = recipe.Name;
        _ = svc.CreateFromRecipe(recipe, "Copy");
        Assert.Equal(origId,   recipe.AssetId);
        Assert.Equal(origName, recipe.Name);
        Assert.NotNull(recipe.EditorMetadata.Recipe);  // original still has recipe metadata
    }

    [Fact]
    public void CreateFromRecipe_EmptyName_Throws()
    {
        var svc = new NewFromRecipeService();
        Assert.Throws<ArgumentException>(() => svc.CreateFromRecipe(MakeRecipe(), ""));
    }

    [Fact]
    public void CreateFromRecipe_TwoCalls_DifferentAssetIds()
    {
        var svc    = new NewFromRecipeService();
        var recipe = MakeRecipe();
        var clone1 = svc.CreateFromRecipe(recipe, "Copy1");
        var clone2 = svc.CreateFromRecipe(recipe, "Copy2");
        Assert.NotEqual(clone1.AssetId, clone2.AssetId);
    }
}
```

---

## Test run command

```powershell
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj `
  --filter "FullyQualifiedName~Recipe|FullyQualifiedName~NewFromRecipe|FullyQualifiedName~PeerVariable" `
  2>&1 | Select-String "passed|failed|Total" | Select-Object -Last 3
```

Also run the full WhenNode suite to confirm no regressions from the BP2003 fix:
```powershell
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj `
  --filter "FullyQualifiedName~WhenNode|FullyQualifiedName~Recipe|FullyQualifiedName~NewFromRecipe" `
  2>&1 | Select-String "passed|failed|Total" | Select-Object -Last 3
```

---

## Deliverables checklist

- [ ] `Stage2_Validate.cs` — BP2003 fix (PeerBlueprintVariable guard)
- [ ] `WhenNodeValidatorTests.cs` — `Validate_PeerVariableSource_EmptyComponentFields_NoBP2003` regression test
- [ ] `GraphTypes.cs` — `RecipeMetadata` class + `AssetMetadata.Recipe` property
- [ ] `TestAssets/Recipes/CoverAwarePatrol.bp.json`
- [ ] `TestAssets/Recipes/HealthThresholdReaction.bp.json`
- [ ] `TestAssets/Recipes/SquadAwareEngagement.bp.json`
- [ ] `TestAssets/Recipes/MoveAndFireCombo.bp.json`
- [ ] `TestAssets/Recipes/SquadState.bp.json`
- [ ] `Hrot.Blueprints.Tests.csproj` — glob `<Content>` for `TestAssets/Recipes/*.bp.json`
- [ ] `RecipeIntegrityTests.cs` (all named tests pass)
- [ ] `Hrot.Blueprints.Editor/NewFromRecipeService.cs`
- [ ] `Hrot.Blueprints.Tests/Editor/NewFromRecipeServiceTests.cs`

## Batch report

Return a brief report with:
- Files created/modified
- Test filter output (passed/failed counts)
- Any deviations from the spec and why
