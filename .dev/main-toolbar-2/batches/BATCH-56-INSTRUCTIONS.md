# BATCH-56 — main-toolbar Compile/Reload (Blueprint) + global Full Rebuild icons (BUG-A24)

**Model: pro (Zoo).** Do NOT use codebase-memory tooling. **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`.
Touch ONLY: `Hrot/Editor/Hrot.Editor.AiShared/Adapters/SilkIconProvider.cs`,
`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (+ `SilkIconProviderTests` if one asserts the key map).

## Context / scope (already decided — do not re-investigate)
- The Blueprint compile/full-rebuild callbacks already exist as instance fields in `EditorSubsystem`:
  `private Action? _blueprintCompileCallback;` and `private Action? _blueprintFullRebuildCallback;` (assigned earlier
  in `Initialize`, before the main-toolbar block).
- **Compile/Reload (quick hot-reload) is Blueprint-only by architecture** (BTree/HSM are JSON-owned, recompiled by the
  source generator on Full Rebuild — no quick-reload exists for them). So Compile/Reload is enabled only for an active
  Blueprint document; **Full Rebuild is global** (it rebuilds all AI assets).

## ⚠️ Build note (read carefully)
`Hrot.Editor` currently CANNOT build because its dependency `Hrot.AI.Behaviors` has a PRE-EXISTING, unrelated
source-generator error (`SampleScout.g.cs` / `NewBTree.g.cs`: `'AssetId' could not be found`) being fixed separately.
**Do NOT try to fix the generator, do NOT delete/modify any `*.btree.json` / `*.hsm.json` / `*.bp.json` assets, do NOT
stub anything.** Author the wiring to be obviously compiler-correct (follow the existing patterns exactly). Verify what
you can: `Hrot.Editor.AiShared` builds (the icon change), and the `EditorSubsystem` edit matches the existing
registration idioms. If `dotnet build Hrot.Editor` fails ONLY with the `AssetId` generator errors in
`Hrot.AI.Behaviors` (no NEW errors in `EditorSubsystem.cs`), that is expected — report it and proceed.

## Part 1 — `SilkIconProvider.cs`: add two icon keys
In `DefaultCellMap`, add (reusing existing packed silk glyphs — both cells are already in the sheet):
```csharp
// ── Build / reload (toolbar) ──────────────────────────────────────
["build/compile"] = "b4",   // lightning → compile / quick reload
["build/rebuild"] = "d4",   // refresh → full rebuild
```
(If a `SilkIconProviderTests` asserts the exact count/contents of the map, update it to include these two keys.)

## Part 2 — `EditorSubsystem.cs`: register commands + toolbar icons
Inside the `if (windowManager.MainToolbar != null)` block (the one that registers New Asset / Open Asset / Save and
the AI-debug group — search for `AiDebugCommands.Register`), AFTER the AI-debug toolbar entries (the `aiSort`
sequence ending with `StepBackId`), add:

1. Register the two shell commands (handlers close over the existing callback fields so they read the live value):
```csharp
// ── Compile / Reload (Blueprint quick hot-reload) + Full Rebuild (global) ──
windowManager.ShellCommands.Register(
    new EditorCommandDescriptor(
        Id:          "blueprint.compileReload",
        DisplayName: "Compile / Reload",
        Category:    "Blueprint",
        Description: "Compile & hot-reload the active blueprint",
        IconKey:     "build/compile",
        DefaultKey:  null,
        IsEnabled:   () => _aiDocumentManager?.Active?.Kind == Hrot.Editor.AiShared.AssetKind.Blueprint),
    _ => _blueprintCompileCallback?.Invoke());

windowManager.ShellCommands.Register(
    new EditorCommandDescriptor(
        Id:          "blueprint.fullRebuild",
        DisplayName: "Full Rebuild",
        Category:    "Build",
        Description: "Rebuild all AI behavior assets",
        IconKey:     "build/rebuild",
        DefaultKey:  null,
        IsEnabled:   () => true),
    _ => _blueprintFullRebuildCallback?.Invoke());
```
(Match the EXACT `EditorCommandDescriptor` constructor argument names used elsewhere in this file — e.g. in
`ShellSaveCommands`/`ScenarioMenuCommands`. If `EditorCommandDescriptor` has no `IconKey`/named-arg you expect,
inspect an existing `new EditorCommandDescriptor(...)` and mirror it precisely.)

2. Add a separator after the AI-debug group, then the two toolbar entries (Compile/Reload right of the debug icons,
   Full Rebuild last):
```csharp
windowManager.MainToolbar.RegisterSeparator("ToolbarSep_AiDebugToBuild", sortOrder: 49);
ToolbarCommandAdapter.Register(windowManager.MainToolbar, windowManager.ShellCommands,
    "blueprint.compileReload", toolbarIconProvider, sortOrder: 50);
ToolbarCommandAdapter.Register(windowManager.MainToolbar, windowManager.ShellCommands,
    "blueprint.fullRebuild", toolbarIconProvider, sortOrder: 51);
```
Keep everything null-safe (the block is already guarded by `windowManager.MainToolbar != null`; the callbacks are
nullable and invoked with `?.`).

## Build & test (no BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj      # must be 0 warnings / 0 errors
dotnet test  Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj  # Failed: 0
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj                    # EXPECTED to fail ONLY on the
#   pre-existing Hrot.AI.Behaviors 'AssetId' generator errors — confirm NO new errors originate in EditorSubsystem.cs
```

## Definition of done
- `SilkIconProvider` maps `build/compile` + `build/rebuild`; `Hrot.Editor.AiShared` builds 0 warnings + tests green.
- `EditorSubsystem` registers `blueprint.compileReload` (enabled for active Blueprint) + `blueprint.fullRebuild`
  (always enabled) and adds them to the main toolbar (sortOrder 50/51, separator at 49), invoking the existing
  callbacks. The only `Hrot.Editor` build errors are the pre-existing generator `AssetId` ones (no new ones in
  EditorSubsystem).
- Write `.dev/main-toolbar-2/reports/BATCH-56-REPORT.md`: the icon keys, the command + toolbar registration, the
  build results (noting the pre-existing generator failure is unrelated and unchanged).

If something cannot be done as specified, STOP and report why rather than stubbing.
