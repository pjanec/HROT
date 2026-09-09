# SE2 Report — Attribute-Driven Picker Drawers for BTree/HSM Inspector

Date: 2026-06-06
Branch: blueprint-integ-1

---

## Approach chosen: Re-register on ActiveChanged (preferred)

On every `_aiDocumentManager.ActiveChanged` fire, the `RegisterWindows` lambda now rebuilds a
fresh `IReadOnlyDictionary<Type, IImGuiFieldDrawer>` for the newly-active asset and calls
`Inspector.SetFacetEditService(facetEditService, newDrawers)`.  No new interfaces, no provider
wrappers, no hot-path allocations (the map is 1–2 entries and rebuilt only on document-switch).

---

## Picker sources wired

| Asset kind | Picker attribute          | Drawer class              | Source arg                         |
|------------|---------------------------|---------------------------|------------------------------------|
| BTree      | `[BehaviorHashPicker]`    | `BehaviorHashPickerDrawer`| `_behaviorRegistry` (shared field) |
| BTree      | `[BlackboardFieldPicker]` | `BlackboardFieldPickerDrawer` | `BehaviorTreeAsset` (active doc) |
| HSM        | `[HsmActionPicker]`       | `HsmActionPickerDrawer`   | `HsmAsset` (active doc)            |
| HSM        | `[HsmGuardPicker]`        | `HsmGuardPickerDrawer`    | `HsmAsset` (active doc)            |
| HSM        | `[HsmStateSelector]`      | `HsmStateSelectorDrawer`  | `HsmAsset` (active doc)            |
| HSM        | `[HsmEventPicker]`        | `HsmEventPickerDrawer`    | `HsmAsset` (active doc)            |
| HSM        | `[HsmSyncGroupPicker]`    | `HsmSyncGroupPickerDrawer`| `HsmAsset` (active doc) — ushort   |

`_behaviorRegistry` is the `BehaviorRegistry` instance (captured in the `RegisterWindows` closure).
`sharedSchemaExporter` / `IActionSchemaExporter` is NOT a picker source — it feeds the
collision diagnostic strip only and was not needed here.

---

## New types introduced

### `BTreePickerDrawerFactory` (static class)
File: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Inspector/BTreePickerDrawers.cs`

```csharp
public static IReadOnlyDictionary<Type, IImGuiFieldDrawer> BuildDrawers(
    BehaviorTreeAsset asset, BehaviorRegistry registry)
```

Returns a single-entry map: `typeof(string)` → `CompositeStringDrawer` with
`BehaviorHashPickerDrawer` + `BlackboardFieldPickerDrawer` sub-drawers.

### `HsmPickerDrawerFactory` (static class) + `HsmCompositeStringDrawer` (internal)
File: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Inspector/HsmPickerDrawers.cs`

```csharp
public static IReadOnlyDictionary<Type, IImGuiFieldDrawer> BuildDrawers(HsmAsset asset)
```

Returns a two-entry map:
- `typeof(string)` → `HsmCompositeStringDrawer` (action/guard/state/event sub-drawers)
- `typeof(ushort)` → `HsmSyncGroupPickerDrawer`

`HsmCompositeStringDrawer` mirrors `CompositeStringDrawer` from the BTree assembly (couldn't reuse
across assemblies without adding a cross-assembly dependency).

---

## EditorSubsystem diff (exact)

File: `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

**Location:** inside `_aiDocumentManager.ActiveChanged += () => { ... };` in `RegisterWindows`.
**After:** the three `*SelectionStore.ActiveAsset = ...` assignments.
**Before:** the `// AIE-047/048: Retarget Blueprint-specific windows.` comment.

```csharp
// SE2: Rebuild picker-drawer maps for the newly active BTree / HSM asset so that
// attribute-dispatched dropdowns (BehaviorHash, BlackboardField, HSM action/guard/
// state/event) reflect the fields and methods of the live document rather than a
// stale, fixed-at-ctor asset.  The maps are small (1–2 entries) and built cheaply
// from the asset already in memory — no I/O.  Calling SetFacetEditService also
// drops the cached StructEdit session so the next render opens a fresh one against
// the correct facet type (harmless when the asset type did not change).
if (active?.Kind == Hrot.Editor.AiShared.AssetKind.BTree
    && active.Asset is Hrot.BTree.Editor.Model.BehaviorTreeAsset btreeAsset
    && _behaviorRegistry is not null)
{
    var btreeDrawers = BTreePickerDrawerFactory.BuildDrawers(btreeAsset, _behaviorRegistry);
    _btreeRegistrar?.Inspector.SetFacetEditService(facetEditService, btreeDrawers);
}
else if (active?.Kind == Hrot.Editor.AiShared.AssetKind.Hsm
    && active.Asset is Hrot.Hsm.Editor.Model.HsmAsset hsmAsset)
{
    var hsmDrawers = HsmPickerDrawerFactory.BuildDrawers(hsmAsset);
    _hsmRegistrar?.Inspector.SetFacetEditService(facetEditService, hsmDrawers);
}
else
{
    // Switching to Blueprint or clearing: reset pickers to null (plain-text fallback).
    // The edit service itself remains so the inspector still renders struct fields.
    _btreeRegistrar?.Inspector.SetFacetEditService(facetEditService, null);
    _hsmRegistrar?.Inspector.SetFacetEditService(facetEditService, null);
}
```

No new `using` directives needed — `Hrot.BTree.Editor.Inspector` and `Hrot.Hsm.Editor.Inspector`
were already imported (lines 119–120), and `facetEditService` is a local variable in scope.

---

## Headless test

File: `Hrot/Editor/Hrot.Editor.AiShared.Tests/Inspector/SE2_PickerDrawerRebuildTests.cs`
Class: `SE2_PickerDrawerRebuildTests` — 14 tests, all headless (no ImGui context).

### BTree tests (7)
| Test | What it verifies |
|------|-----------------|
| `BTreeFactory_BuildDrawers_ContainsStringDrawer` | Map has `typeof(string)` key |
| `BTreeFactory_StringDrawer_DispatchesBehaviorHashPicker` | Resolves `[BehaviorHashPicker]` → `BehaviorHashPickerDrawer`; items include registered name |
| `BTreeFactory_StringDrawer_DispatchesBlackboardFieldPicker` | Resolves `[BlackboardFieldPicker]` → `BlackboardFieldPickerDrawer`; items include asset field |
| `BTreeFactory_AssetSwitch_DrawersReflectNewAsset` | Two successive BuildDrawers calls return independent maps referencing their respective assets |
| `BTreeFactory_NullAsset_Throws` | `ArgumentNullException` on null asset |
| `BTreeFactory_NullRegistry_Throws` | `ArgumentNullException` on null registry |

### HSM tests (8)
| Test | What it verifies |
|------|-----------------|
| `HsmFactory_BuildDrawers_ContainsStringDrawer` | Map has `typeof(string)` key |
| `HsmFactory_BuildDrawers_ContainsUshortDrawer` | Map has `typeof(ushort)` → `HsmSyncGroupPickerDrawer` |
| `HsmFactory_StringDrawer_DispatchesActionPicker` | Resolves `[HsmActionPicker]`; items include OnEntry/OnExit actions |
| `HsmFactory_StringDrawer_DispatchesGuardPicker` | Resolves `[HsmGuardPicker]` → `HsmGuardPickerDrawer` |
| `HsmFactory_StringDrawer_DispatchesStateSelector` | Resolves `[HsmStateSelector]`; items include declared states |
| `HsmFactory_StringDrawer_DispatchesEventPicker` | Resolves `[HsmEventPicker]`; items include declared events |
| `HsmFactory_AssetSwitch_DrawersReflectNewAsset` | Event pickers from two successive calls reflect their respective assets independently |
| `HsmFactory_NullAsset_Throws` | `ArgumentNullException` on null asset |

---

## Build results

| Project | Errors | Warnings |
|---------|--------|---------|
| `Hrot.BTree.Editor` | 0 | 0 |
| `Hrot.Hsm.Editor` | 0 | 0 |
| `Hrot.Editor` (EditorSubsystem) | 0 | 0 |
| `Hrot.Editor.AiShared.Tests` | 0 | 0 |
| Full solution | 0 | 26 (pre-existing, Blueprint CS0618/CS8601) |

---

## Test results

| Suite | Passed | Failed | Notes |
|-------|--------|--------|-------|
| `SE2_PickerDrawerRebuildTests` | 14 | 0 | New SE2 tests |
| `Hrot.Editor.AiShared.Tests` (full) | 856 | 0 | 0 regressions |
| `Hrot.BTree.Editor.Tests` (full) | 382 | 0 | 0 regressions |
| `Hrot.Hsm.Editor.Tests` (full) | 333 | 0 | 0 regressions |

Pre-existing known failures (none appeared): ScoreCrossed, AllocatesZeroBytes,
Library/LibraryMath CRLF flake, AiShared fs-race flake.

---

## Deferral / STOP

None. No broad refactoring was required. The per-asset re-register approach fit entirely within
the existing `ActiveChanged` lambda footprint. SE1's plain-text fallback remains intact for
Blueprint perspective (the else-branch resets both inspectors to `null` drawers while keeping
`facetEditService` active, so StructEdit render continues).

The dropdown **render** is visual and deferred to the morning review session per spec.
