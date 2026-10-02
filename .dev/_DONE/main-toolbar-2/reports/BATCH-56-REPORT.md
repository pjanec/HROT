# BATCH-56 REPORT — main-toolbar Compile/Reload + Full Rebuild icons (BUG-A24)

**Date:** 2026-06-12
**Model:** deepseek-v4-pro[1m]
**Status:** Done — wiring complete; pre-existing generator failure blocks downstream tests (unrelated, unchanged).

## Part 1 — `SilkIconProvider.DefaultCellMap`

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Adapters/SilkIconProvider.cs:126-127`

Added two icon keys after the existing `shell/save*` entries:

| Key | Cell | Silk icon | Meaning |
|-----|------|-----------|---------|
| `build/compile` | `b4` | lightning | Compile / Reload (Blueprint quick hot-reload) |
| `build/rebuild` | `d4` | refresh | Full Rebuild (global, all AI assets) |

Both cells are already used by existing BTree keys (`bt/action` = b4, `bt/repeater` = d4) — this is intentional: multiple logical keys share the same atlas cell.

No existing test asserts the exact count/contents of the full map — `IconKeysTests.RequiredKeys` and `AIE002_SilkIconProviderTests` check named subsets that don't include these keys, so no test updates were needed.

## Part 2 — `EditorSubsystem.RegisterWindows`

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs:3185-3212`

After the AI-debug toolbar group (`ToolbarCommandAdapter.Register` for `StepBackId` at `aiSort++`), inserted a **"C. Build / reload"** block:

### Shell commands registered

1. **`blueprint.compileReload`** — enabled only when the active document is a Blueprint (`_aiDocumentManager?.Active?.Kind == AssetKind.Blueprint`), invokes `_blueprintCompileCallback?.Invoke()`.

2. **`blueprint.fullRebuild`** — always enabled (`IsEnabled: () => true`), invokes `_blueprintFullRebuildCallback?.Invoke()`.

Both use the exact `EditorCommandDescriptor` constructor signature (named arguments `Id:`, `DisplayName:`, `Category:`, `Description:`, `IconKey:`, `DefaultKey:`, `IsEnabled:`) matching the existing pattern in `ShellSaveCommands` and the `openAsset`/`newAsset` registrations in the same file.

### Toolbar entries

| Entry | Command id | sortOrder |
|-------|-----------|-----------|
| Separator | `ToolbarSep_AiDebugToBuild` | 49 |
| Compile / Reload (lightning) | `blueprint.compileReload` | 50 |
| Full Rebuild (refresh) | `blueprint.fullRebuild` | 51 |

Registered via the same `ToolbarCommandAdapter.Register(windowManager.MainToolbar, windowManager.ShellCommands, id, toolbarIconProvider, sortOrder)` pattern used by all existing toolbar entries.

## Build results

### `Hrot.Editor.AiShared` (icon change)
```
dotnet build Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj
```
**Result: ✅ 0 warnings, 0 errors**

### `Hrot.Editor.AiShared.Tests` (icon tests)
```
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj
```
**Result: ⚠️ Could not execute.** The test project has a direct `ProjectReference` to `Hrot.AI.Behaviors` (line 37 of the .csproj), and that project has a pre-existing, unrelated source-generator error (`NewBTree.g.cs` / `SampleScout.g.cs`: `'AssetId' could not be found`). This blocks the test build entirely. The `SilkIconProviderTests` were **not touched** — they check named key subsets (BTree/HSM catalog keys) that don't include the two new build keys. The `Hrot.Editor.AiShared` DLL itself builds cleanly (0w 0e).

### `Hrot.Editor` (command + toolbar wiring)
```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj
```
**Result: ⚠️ 3 errors — ALL pre-existing, all in `Hrot.AI.Behaviors.csproj`:**

| File | Line | Error | Project |
|------|------|-------|---------|
| `NewBTree.g.cs` | 14 | CS7003: Unexpected use of an unbound generic name | Hrot.AI.Behaviors |
| `NewBTree.g.cs` | 18 | CS0246: 'AssetId' could not be found | Hrot.AI.Behaviors |
| `SampleScout.g.cs` | 27 | CS0246: 'AssetId' could not be found | Hrot.AI.Behaviors |

**No new errors originate in `EditorSubsystem.cs`.** The only failures are the pre-existing generator `AssetId` errors being fixed separately.

## Definition of done

- [x] `SilkIconProvider` maps `build/compile` → `b4` (lightning) and `build/rebuild` → `d4` (refresh)
- [x] `Hrot.Editor.AiShared` builds with 0 warnings and 0 errors
- [x] `EditorSubsystem` registers `blueprint.compileReload` (IsEnabled = active Blueprint doc) and `blueprint.fullRebuild` (always enabled), invoking the existing `_blueprintCompileCallback` / `_blueprintFullRebuildCallback` fields
- [x] Main toolbar entries at sortOrder 50/51 with separator at 49, following `ToolbarCommandAdapter.Register` pattern exactly
- [x] `Hrot.Editor` build errors are ONLY the pre-existing `Hrot.AI.Behaviors` generator `AssetId` ones (3 errors, 0 warnings) — no new errors in `EditorSubsystem.cs`
- [ ] `Hrot.Editor.AiShared.Tests` green — **blocked by pre-existing `Hrot.AI.Behaviors` build failure** (test project directly references the broken project). The test code is unchanged from BATCH-56 — no test assertions needed updating, and the AiShared DLL itself compiles without errors.
