# SE1 -- Wire InspectorWindow StructEdit: Report

Branch: `blueprint-integ-1`  
Date: 2026-06-06  
Status: **DONE** (no commit yet — lead commits)

---

## 1. StructEdit render-API findings

### How to build an EditDocument via reflection

```
var svc = new ComponentEditServiceBuilder()
    .Build();                                   // IComponentEditService
var session = svc.Open(boxedFacet, facetType); // IEditSession
// session.Document.Root = EditNode tree
```

`ComponentEditServiceBuilder` (in `StructEdit.Reflection`) accepts:
- `RegisterBufferViewProvider` for chameleon-buffer projections
- `RegisterFieldEditor(type, ICustomFieldEditor)` for per-type custom node creation
- `RegisterComponentEditor` for whole-component override
- `RegisterValidator` for commit-time validation

`ReflectionEditDocumentBuilder` is the engine called by `ComponentEditService.Open`. It scans public instance fields and properties, determines `EditNodeKind` for each:
- `bool` → `EditNodeKind.Boolean`
- `string` → `EditNodeKind.String`
- `int`/`float`/etc → `EditNodeKind.Scalar`
- **`type.IsEnum` → `EditNodeKind.Enum`** (auto combo in `ComponentEditDrawer.DrawPrimitiveInput`)
- value-type (struct) → `EditNodeKind.Struct` (recurses into children)
- classes/records → `EditNodeKind.Class`/`EditNodeKind.Record`

Custom attributes on fields are collected in `EditNodeMetadata.CustomAttributes` (everything that is NOT a known StructEdit attribute). This is how picker marker attributes flow through.

### Rendering per frame

`ComponentEditDrawer` (in `Fdp.Presentation/ImGui/Editing/`) is the recursive ImGui renderer. Call pattern:

```csharp
// Must be inside BeginTable/EndTable (2 columns: "Property" | "Value")
if (ImGui.BeginTable("##tbl", 2, SizingStretchProp))
{
    ImGui.TableSetupColumn("Property", WidthStretch, 0.4f);
    ImGui.TableSetupColumn("Value",    WidthStretch, 0.6f);
    drawer.DrawEditNode(session.Document.Root);
    ImGui.EndTable();
}
```

`DrawEditNode` dispatches by `node.Kind`:
- `Struct/Class/Record` → tree-node header (collapsible), recurses children
- `Scalar/Boolean/String/Enum/Custom/Guid/DateTime` → leaf row with input widget
- `Enum` specifically: `ImGui.Combo(names, values)` (non-flags) or checkboxes (flags enum)

`DrawPrimitiveInput` checks `_customDrawers[type]` first; if found, delegates to `IImGuiFieldDrawer.DrawInput(ref value, node)`. The node carries `EditNodeMetadata.CustomAttributes` so attribute-dispatch in `CompositeStringDrawer` works.

### Reading mutated values back

`session.IsDirty` → true if any binding was written. `session.Commit()` returns a boxed replacement struct (type-identical to input). The caller then calls `CommitCurrentFacet(committed)` which routes to `IFacetDispatcher.ApplyFacet`.

`IEditSession` is `IDisposable`. We cache one per facet type and dispose when the selection type changes or a commit happens.

---

## 2. Picker drawer registrations

### CompositeStringDrawer pattern (BTreePickerDrawers.cs)

`CompositeStringDrawer` is registered as the `typeof(string)` custom drawer. It dispatches by custom attributes on the `EditNode`:

```csharp
new CompositeStringDrawer()
    .Register<BehaviorHashPickerAttribute>(new BehaviorHashPickerDrawer(registry))
    .Register<BlackboardFieldPickerAttribute>(new BlackboardFieldPickerDrawer(asset))
    .Register<HsmActionPickerAttribute>(new HsmActionPickerDrawer(asset))
    .Register<HsmGuardPickerAttribute>(new HsmGuardPickerDrawer(asset))
    .Register<HsmStateSelectorAttribute>(new HsmStateSelectorDrawer(asset))
```

`HsmEventPickerDrawer` targets `typeof(ushort)` (event IDs are `ushort`), and `HsmSyncGroupPickerDrawer` also targets `typeof(ushort)`. These are registered directly:

```csharp
customDrawers[typeof(ushort)] = new HsmEventPickerDrawer(asset); // or SyncGroupPickerDrawer
```

(For a given facet type only one ushort picker is needed; the composition root chooses which.)

### Keeping AiShared dep-clean

`Hrot.Editor.AiShared` does NOT reference `Hrot.Hsm.Editor` or `Hrot.BTree.Editor`. The picker drawers and the `IComponentEditService` are injected from the composition root (`Hrot.Editor` / `EditorSubsystem`) which does reference both subsystem editors.

Two injection points were added:

**`InspectorWindow` constructor** (new optional params, no breaking change):
```csharp
IComponentEditService? facetEditService = null,
IReadOnlyDictionary<Type, IImGuiFieldDrawer>? facetCustomDrawers = null
```

**`InspectorWindow.SetFacetEditService(service, customDrawers)`** — runtime setter (same pattern as `SetFacetDispatcher`), safe to call after construction.

**`PerspectiveWorkspaceRegistrar` constructor** (new optional params, forwarded to Inspector):
```csharp
IComponentEditService? facetEditService = null,
IReadOnlyDictionary<Type, IImGuiFieldDrawer>? facetCustomDrawers = null
```

---

## 3. What was wired (exact changes)

### `FDP/Engine/Fdp.Presentation/ImGui/Editing/ComponentEditDrawer.cs`
- Changed `internal sealed class ComponentEditDrawer` → `public sealed class ComponentEditDrawer`
- Changed `internal ComponentEditDrawer(...)` → `public ComponentEditDrawer(...)` (constructor)
- Reason: `InspectorWindow` lives in a different assembly (`Hrot.Editor.AiShared`) and must instantiate `ComponentEditDrawer` directly. Making it `public` is cleaner than `InternalsVisibleTo` on a framework assembly.

### `Hrot/Editor/Hrot.Editor.AiShared/Windows/InspectorWindow.cs`
- Added usings: `Fdp.Presentation.Editing`, `StructEdit.Core`
- Added fields: `_facetEditService`, `_facetCustomDrawers`, `_facetSession`, `_facetSessionType`
- Extended constructor with two new optional params (`facetEditService`, `facetCustomDrawers`)
- Added `SetFacetEditService(editService, customDrawers)` public setter
- Updated `SetFacetDispatcher` to also call `DisposeAndClearFacetSession()`
- Updated `CommitCurrentFacet` to call `DisposeAndClearFacetSession()` (session rebuilt on next render)
- Replaced stub (old lines 207-213: `Text("[FacetTypeName]") + Button("Apply##facet")`) with real StructEdit render block:
  - Opens/reuses `IEditSession` keyed by facet type
  - Rebuilds document if `RebuildState == RebuildRequired`
  - Renders via `ComponentEditDrawer` inside a 2-column `BeginTable`
  - Calls `session.Commit()` + `CommitCurrentFacet()` on `session.IsDirty` (every dirty frame)
  - Falls back to original stub if `_facetEditService is null` (graceful degradation)
- Added `GetFacetSession()` internal accessor (for headless tests)
- Added `DisposeAndClearFacetSession()` private helper

### `Hrot/Editor/Hrot.Editor.AiShared/Windows/PerspectiveWorkspaceRegistrar.cs`
- Added usings: `System`, `System.Collections.Generic`, `Fdp.Presentation.Editing`, `StructEdit.Core`
- Extended constructor with two new optional params forwarded to `InspectorWindow`

### No changes needed to:
- `HsmPickerDrawers.cs` / `BTreePickerDrawers.cs` — already implement `IImGuiFieldDrawer`; registration is composition-root concern
- `HsmFacetDispatcher.cs` / `BTreeFacetMapper.cs` — unchanged
- `PerspectiveWorkspaceRegistrar.RegisterWindows` — unchanged
- No new project references added (all types flow transitively from existing refs)

### Construction-site change (Hrot.Editor / EditorSubsystem)
The composition root (`EditorSubsystem.Initialize` / wherever `PerspectiveWorkspaceRegistrar` is built) needs to pass the `facetEditService` and `facetCustomDrawers` when it builds the registrar for each perspective (HSM / BTree). This is the **user WIP** item — the footprint is minimal: build the `CompositeStringDrawer` for the specific asset and pass it in. Example for the BTree perspective:

```csharp
// Called once per asset-open, with the live BehaviorTreeAsset and BTreeNodeSelection:
var compositeStr = new CompositeStringDrawer()
    .Register<BehaviorHashPickerAttribute>(new BehaviorHashPickerDrawer(behaviorRegistry))
    .Register<BlackboardFieldPickerAttribute>(new BlackboardFieldPickerDrawer(btreeAsset));

var customDrawers = new Dictionary<Type, IImGuiFieldDrawer>
{
    [typeof(string)] = compositeStr,
};

var editSvc = new ComponentEditServiceBuilder().Build();

bTreeRegistrar.Inspector.SetFacetEditService(editSvc, customDrawers);
// or pass at construction:
//   new PerspectiveWorkspaceRegistrar(..., facetEditService: editSvc, facetCustomDrawers: customDrawers)
```

For the HSM perspective, use `HsmActionPickerDrawer`, `HsmGuardPickerDrawer`, `HsmStateSelectorDrawer` for `typeof(string)`, and `HsmEventPickerDrawer` for `typeof(ushort)`.

---

## 4. Headless test

**File:** `Hrot/Editor/Hrot.Editor.AiShared.Tests/Inspector/SE1_StructEditFacetRenderTests.cs`

8 tests, all pass (8/8 green):

| Test | Verifies |
|------|----------|
| `EditService_OpensFacet_DocumentHasFieldNodes` | Session + document open; all public fields present |
| `EditService_BoolField_ProducesBooleanNode` | `IsBreakpoint` → `EditNodeKind.Boolean` |
| `EditService_StringFieldWithBehaviorHashPicker_CarriesPickerAttribute` | `[BehaviorHashPicker]` in `CustomAttributes` |
| `EditService_StringFieldWithBlackboardFieldPicker_CarriesPickerAttribute` | `[BlackboardFieldPicker]` in `CustomAttributes` |
| `EditService_EnumField_ProducesEnumNode` | `SampleKind` enum field → `EditNodeKind.Enum` |
| `EditService_SetBoolValue_CommitReturnsMutatedFacet` | Set `IsBreakpoint=true` → `Commit()` → mutated struct |
| `CommitCurrentFacet_AppliesEditedFacetToAsset` | Round-trip via `InspectorWindow.CommitCurrentFacet` → asset `IsDirty` + field updated |
| `InspectorWindow_GetFacetSession_IsNullWhenNoEditService` | No edit service → `GetFacetSession()` returns null (no crash) |

---

## 5. Build / test results

| Project | Result |
|---------|--------|
| `Hrot.Editor.AiShared` | 0 errors, 0 warnings |
| `Hrot.Hsm.Editor` | 0 errors, 0 warnings |
| `Hrot.BTree.Editor` | 0 errors, 0 warnings |
| `Hrot.Editor` | 0 errors, 0 warnings |
| `Hrot.Editor.AiShared.Tests` | 840 pass / 0 fail (incl. 8 new SE1 tests) |
| `Hrot.Hsm.Editor.Tests` | 339 pass / 0 fail |
| `Hrot.BTree.Editor.Tests` | 391 pass / 0 fail |

Pre-existing known failures: ScoreCrossed, AllocatesZeroBytes, Library/LibraryMath CRLF — not in these test assemblies, unchanged.

---

## 6. Visual render (deferred to REVIEW-V2)

The actual ImGui combo/field rendering is visual and cannot be verified headlessly. The user's morning review should confirm:
- Each facet struct's fields appear as editable rows in the Inspector
- Enum fields (e.g. `StateFlags` in `StateFacet`) render as checkboxes (flags enum) or combo
- Picker fields (e.g. `MethodFqn` with `[BehaviorHashPicker]`) render as `CompositeStringDrawer` combo — **only after the composition root wires the `facetEditService`** (see construction-site note above)
- Without `facetEditService` wired, the old stub `[TypeName] / Apply` button is still shown (graceful degradation)

---

## 7. Stops / not-done

- **`[HsmSyncGroupPicker]` on `ushort`**: `HsmSyncGroupPickerDrawer` targets `typeof(ushort)`. If both event and sync-group pickers are needed simultaneously, a composite-ushort drawer analogous to `CompositeStringDrawer` would be needed. Currently the composition root must choose one per facet type. This is acceptable for Phase 6.

---

## 8. LIVE WIRING (2026-06-06) — composition root now passes the edit service

The SE1 render plumbing was correct but inert: `InspectorWindow` fell back to the
`[FacetTypeName] + Apply` stub because the composition root never supplied an
`IComponentEditService`. The live wiring is now complete.

### Exact `EditorSubsystem.cs` diff (minimal footprint)

`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`, in `Initialize()` immediately before
the `_btreeRegistrar` / `_hsmRegistrar` construction (~line 1850):

1. **One new local** (before the two registrar `new`s):
```csharp
var facetEditService = new ComponentEditServiceBuilder().Build();
```
(`ComponentEditServiceBuilder` is in `StructEdit.Reflection`, already `using`-imported on
line 101 and already used twice elsewhere in the file — no new usings.)

2. **One new named arg on the BTree registrar ctor:**
```csharp
            facetEditService:     facetEditService);
```

3. **One new named arg on the HSM registrar ctor:**
```csharp
            facetEditService:     facetEditService);
```

That is the entire EditorSubsystem change: 1 local + 2 argument lines (plus an explanatory
comment block). No other lines touched; the Blueprint registrar, the Subtree sync panel,
and all other windows are untouched.

### Picker drawers: DEFERRED to morning review (and why)

The attribute picker drawers (`BehaviorHashPickerDrawer`, `BlackboardFieldPickerDrawer`,
`HsmActionPickerDrawer`, `HsmGuardPickerDrawer`, `HsmStateSelectorDrawer`,
`HsmEventPickerDrawer`) each **capture a fixed asset in their constructor**
(`new BlackboardFieldPickerDrawer(BehaviorTreeAsset asset)`, `new HsmActionPickerDrawer(HsmAsset asset)`, …).
A `facetCustomDrawers` map is registered **once** at composition time, but the active asset
**changes per opened document** (tracked by `_btreeSelectionStore.ActiveAsset` /
`_hsmSelectionStore.ActiveAsset`). A single registered drawer instance therefore cannot
follow the current selection — it would be pinned to whatever asset happened to be active
at registration.

Wiring them correctly requires either (a) new provider-backed wrapper drawers that read
`selectionStore.ActiveAsset` on each `DrawInput`, or (b) re-registering `facetCustomDrawers`
on every `ActiveChanged`. Both are new code on the hot render path that the SE1 task
explicitly flags as "too entangled to do confidently without visual iteration." Per the
task's fallback instruction, the picker drawers are **deferred to the morning review**.

**Consequence (acceptable per task):** facet picker fields (e.g. `MethodFqn`,
`ExpressionTargetField`, HSM action/guard/state/event fields) render as **plain text
inputs** via `CompositeStringDrawer`'s default fallthrough, instead of dropdown combos.
All other facet fields render fully live: enums → combos, bools → checkboxes, numbers →
numeric inputs, strings → text inputs.

### Headless verification of the wiring

Added 2 tests to `PerspectiveWorkspaceRegistrarTests.cs` proving the constructor param
flows through to the Inspector (the exact path EditorSubsystem uses):

| Test | Verifies |
|------|----------|
| `PerspectiveRegistrar_ForwardsFacetEditService_ToInspector` | registrar built with `facetEditService` → `Inspector.HasFacetEditService == true` |
| `PerspectiveRegistrar_WithoutFacetEditService_InspectorHasNone` | negative control: no service → stub fallback path |

Added a 1-line internal accessor `InspectorWindow.HasFacetEditService` (test-only,
non-breaking) to support the assertion headlessly.

### Build / test results (live wiring)

| Target | Result |
|--------|--------|
| `dotnet build IOS-IG-SimHost.sln` | **0 errors** (26 pre-existing warnings, unchanged) |
| `Hrot.Editor.AiShared.Tests` | **842 pass / 0 fail** (840 + 2 new forwarding tests) |
| `Hrot.Hsm.Editor.Tests` | **339 pass / 0 fail** |
| `Hrot.BTree.Editor.Tests` | **391 pass / 0 fail** |

AiShared fs-race flake did not appear (no re-run needed).

### What the user will see at morning review

Selecting a BTree or HSM node in the running editor now shows its facet struct as a
**2-column "Property | Value" table of editable rows** (enum fields as combos, bools as
checkboxes, numbers/strings as inputs) — replacing the old `[FacetTypeName] + Apply` stub.
Edits commit back to the asset continuously. Picker fields appear as plain editable text
boxes (functional, just not yet dropdowns) — wiring those into dropdowns is the one
remaining morning-review item.
