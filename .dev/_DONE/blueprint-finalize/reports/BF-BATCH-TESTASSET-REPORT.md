# BF-BATCH-TESTASSET Report

**Branch:** blueprint-integ-1  
**Date:** 2026-06-06  
**Status:** COMPLETE — all gates green

---

## Deliverables

### 1. Recipe: LocomotionMoveToDemo

**File:** `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/LocomotionMoveToDemo.bp.json`  
**Test asset copy:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/LocomotionMoveToDemo.bp.json`  
**AssetId:** `00000000-aaaa-0001-0000-000000000006`

What it demonstrates:
- **AiPrimitive dispatch** (`Dispatch: 1`, `Primitive.Intent: 0`, `Hostings: [0]` = BTreeAction). ChannelCommand is ONLY valid in AiPrimitive blueprints — Stage2 V_ChannelCommandReferences and V_DispatchKindCompatibility enforce this.
- **ChannelCommand node** with `ChannelType: "LocomotionChannel"`, `ActionId: "MoveTo"` — fully configured. Selecting this node in the Details panel shows the action Combo dropdown and MoveToParams projection (9 data-IN pins for fields: Target, AcceptanceRadius, etc.) via NodePinSchema.GetCanonicalPins.
- **Exec wiring:** EventEntry → ChannelCommand → Return, fully wired with explicit link GUIDs. Stage0_Rehydrate maps the GUIDs to the positional exec-pin slots (ExecOut slot 0 on EventEntry → ExecIn slot 0 on ChannelCommand → ExecOut slot 0 on ChannelCommand → ExecIn slot 0 on Return).
- **X/Y layout:** EventEntry at (80, 220), ChannelCommand at (420, 220), Return at (760, 220).
- **Recipe block:** DisplayName "Locomotion MoveTo Demo", Category "AI Primitives", Difficulty "Beginner", 4 ConceptsTaught.

User-facing instruction:
> Open New-from-Recipe → "AI Primitives" → "Locomotion MoveTo Demo". The blueprint opens with three nodes wired: EventEntry → ChannelCommand (LocomotionChannel/MoveTo) → Return. Click the ChannelCommand node. The Details panel shows the action combo (currently "MoveTo") and the MoveToParams data-IN pins (Target, AcceptanceRadius, etc.). You can change the action via the combo to see different param pins.

### 2. Recipe: EditorTypesDemo

**File:** `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/EditorTypesDemo.bp.json`  
**Test asset copy:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/EditorTypesDemo.bp.json`  
**AssetId:** `00000000-aaaa-0001-0000-000000000007`

What it demonstrates:
- **Instance dispatch** (`Dispatch: "Instance"`) — standard event-driven Instance blueprint.
- **Exec chain:** EventEntry → Return (minimal valid graph).
- **Floating pure nodes** (not exec-connected; Stage2/5 prunes them safely) covering all StaticTypeRegistry-supported editor types:

| Node | Type | Inline Editor |
|------|------|---------------|
| Branch (id: `f7000007-0010-bb10-0010-...`) | `System.Boolean` (Condition pin) | BoolPinEditor — checkbox |
| FunctionCall AddInt (id: `f7000007-0020-bb20-0020-...`) | `System.Int32` (a, b pins via Stage0) | IntPinEditor — integer field |
| FunctionCall Add (id: `f7000007-0030-bb30-0030-...`) | `System.Single` (a, b pins via Stage0) | FloatPinEditor — float field |
| FunctionCall explicit (id: `f7000007-0040-bb40-0040-...`) | `System.String` (Value pin explicit) | StringPinEditor — text field |
| FunctionCall AddVec (id: `f7000007-0050-bb50-0050-...`) | `System.Numerics.Vector3` (a, b pins via Stage0) | VectorPinEditor(3) |
| FunctionCall explicit (id: `f7000007-0060-bb60-0060-...`) | `System.Numerics.Vector2` (Value explicit) | VectorPinEditor(2) |
| FunctionCall explicit (id: `f7000007-0070-bb70-0070-...`) | `System.Numerics.Vector4` (Value explicit) | VectorPinEditor(4) |
| FunctionCall explicit (id: `f7000007-0080-bb80-0080-...`) | `System.Numerics.Quaternion` (Value explicit) | QuaternionPinEditor |

**Note on NodeEditor.Color and System.Guid:** Both are registered in `PinDefaultValueEditorRegistry.CreateWithBuiltins()` but are NOT in `StaticTypeRegistry`. Using them as pin types causes a BP1500 compile error. They are omitted from this recipe but would show their editors if added via a host-side TypeRegistry extension.

User-facing instruction:
> Open New-from-Recipe → "Tutorial" → "Inline-Editor Types Demo". The graph shows a small exec chain (EventEntry → Return) and eight floating pure/impure nodes below it, one per supported type. Click any floating node in the canvas — its unconnected In-data pin(s) render inline editors directly in the node body: checkbox for bool, integer spinner for int, float field for float, text box for string, X/Y for Vector2, X/Y/Z for Vector3, X/Y/Z/W for Vector4, and a quaternion editor.

---

## 3. Headless Verifications

### 3a. Recipe parse + round-trip (RecipeIntegrityTests)

New test cases added to `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/RecipeIntegrityTests.cs`:
- `AllRecipes_Parse("LocomotionMoveToDemo")` — deserializes, non-empty AssetId + Name. **PASS**
- `AllRecipes_Parse("EditorTypesDemo")` — deserializes, non-empty AssetId + Name. **PASS**
- `AllRecipes_HaveDescriptionsAndConcepts("LocomotionMoveToDemo")` — Recipe block present, ≥2 ConceptsTaught. **PASS**
- `AllRecipes_HaveDescriptionsAndConcepts("EditorTypesDemo")` — Recipe block present, ≥2 ConceptsTaught. **PASS**
- `AllRecipes_HaveStableAssetIds("LocomotionMoveToDemo", "00000000-aaaa-0001-0000-000000000006")`. **PASS**
- `AllRecipes_HaveStableAssetIds("EditorTypesDemo", "00000000-aaaa-0001-0000-000000000007")`. **PASS**

### 3b. Compile with no errors

- `LocomotionMoveToDemo_ValidateOnly_NoErrors` — compiles with `BuiltInChannelCommandCatalog` (required for V_ChannelCommandReferences to resolve LocomotionChannel/MoveTo). **PASS**
- `AllRecipes_ValidateOnly_NoErrors("EditorTypesDemo")` — compiles with `EmptyChannelCommandCatalog` (Instance blueprint, no ChannelCommands needed). **PASS**

### 3c. NodePinSchema projects ChannelCommand param pins

Pre-existing test `ChannelCommandNodeDrawerTests.Session_SelectMoveTo_NodePinSchema_ProjectsMoveToParams` (CC-04) verifies `GetCanonicalPins` returns `dataInPins.Count > 0` after setting LocomotionChannel/MoveTo. **PASS**

Pre-existing test `ChannelCommandNodeDrawerTests.DrawerRegistry_Contains_ChannelCommandNodeDrawer` (CC-06) verifies the real drawer registry resolves a non-null `ChannelCommandNodeDrawer`. **PASS**

---

## 4. ChannelCommand Drawer Diagnostic Test

**New test:** `BlueprintDetailsWindowTests.BlueprintDetails_ChannelCommandNode_ResolvesChannelCommandDrawer` (BF-TA-01)  
**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/BlueprintDetailsWindowTests.cs`

What it does: Constructs a `BlueprintDetailsWindow` with the REAL registry from `BlueprintEditorBootstrap.CreateNodeDrawerRegistry(...)`, sets an asset + graph containing a `ChannelCommandNode(LocomotionChannel/MoveTo)`, sets a `BlueprintNodeSelection` pointing at that node, then calls `window.ResolveSession()`.

**Result: PASS** — session is non-null and `window.ResolvedDrawerKind == typeof(ChannelCommandNodeDrawer)`.

### Implication for the live "No node selected" report

The drawer resolves correctly headlessly. The live "Details: No node selected" is a **selection / node-ID issue**, NOT a drawer registration bug. Specifically, `BlueprintDetailsWindow.ResolveSession()` shows "No node selected" when:
1. `_selectionStore.ActiveSubSelection` is null or not a `BlueprintNodeSelection`, OR
2. The `GraphId` in the selection does not match any graph in the retargeted `BlueprintAsset`, OR
3. The `NodeId` in the selection does not match any node in that graph.

Check: Is the window's `_asset` the same instance that owns the selected node? Is the selection being set with the correct `GraphId` + `NodeId` after the blueprint is loaded/retargeted?

---

## 5. Gate Results

| Gate | Result |
|------|--------|
| `dotnet build IOS-IG-SimHost.sln -c Debug` | **0 errors / 0 new warnings** (26 pre-existing warnings unchanged) |
| `Hrot.Blueprints.Tests` failures subset of 7 pre-existing | **7 failures, all pre-existing** (golden snapshots + allocation test + condition summary) |
| New tests pass | **10 new tests added, all pass** |
| EditorSubsystem tests | **9/9 pass** |
| Recipe JSON `kind`-first + `$meta` | **Verified** — both recipes have `"kind"` as first key on every node object, and `"$meta"` envelope |
| Recipes excluded from MSBuild generator | **Verified** — `Hrot.AI.Behaviors.csproj` excludes `Blueprints/Recipes/*.bp.json` from `AdditionalFiles` |

### Pre-existing failures (all 7, unchanged):
1. `LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource`
2. `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource(MoveToAndFire)`
3. `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource(HasVisibleTarget)`
4. `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold`
5. `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes`
6. `LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot`
7. `MoveToAndFireDemoTests.MoveToAndFire_GeneratedSource_Snapshot`

---

## 6. Files Changed / Created

| File | Action |
|------|--------|
| `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/LocomotionMoveToDemo.bp.json` | **Created** |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/EditorTypesDemo.bp.json` | **Created** |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/LocomotionMoveToDemo.bp.json` | **Created** (test asset copy) |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/EditorTypesDemo.bp.json` | **Created** (test asset copy) |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/RecipeIntegrityTests.cs` | **Modified** — added LocomotionMoveToDemo + EditorTypesDemo to all theory tests; added `LocomotionMoveToDemo_ValidateOnly_NoErrors` fact; extended `RecipeCompileOptions` to accept optional `channelCommands` override |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/BlueprintDetailsWindowTests.cs` | **Modified** — added BF-TA-01 drawer diagnostic test + NullEditService/NullPredicateCompiler stubs |

No WIP files touched. No goldens regenerated. No commit made.
