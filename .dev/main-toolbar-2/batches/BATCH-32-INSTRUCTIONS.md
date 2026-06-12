# BATCH-32 — MTB2-T3: `DynamicDisplayName` on commands → dynamic menu label + toolbar tooltip

**Task:** MTB2-T3 (Item 3) · **Model:** pro · **Repo root:** `D:\Work\IOS-IG-SimHost-FDP`
**Detail:** `.dev/main-toolbar-2/TASK-DETAIL.md` (`MTB2-T3`) · DECISIONS D-T3-1.

## Onboarding (do NOT use codebase-memory tooling)
1. `.dev/.guides/DEV-GUIDE.md`. 2. This file.

## ⚙️ RULES (non-negotiable)
1. Do this ONE objective only. Touch ONLY the files listed. No drive-by edits/renames.
2. NEVER hide a problem to pass a build: no excluding assets, no `[Skip]`/commented/weakened tests, no stubs, no
   diagnostic suppression, no `#if false`. If blocked, STOP and report why.
3. Add the EXACT named tests; they must assert real values and fail if the code is wrong.
4. DO NOT STOP until build = 0 warnings AND the test commands show `Failed: 0` (no `BLUEPRINT_REGENERATE_SNAPSHOTS`).
5. Report exact files/tests changed + final test summaries. No litter.

## Objective
Let a command supply a **per-frame label** that the menu item text and the toolbar tooltip use. Generic; this is the
infrastructure for T4's dynamic Save label (`"Save [blueprint: Count5]"`). Default behavior unchanged (field defaults
to null → existing labels render exactly as today).

## Scope — ONLY these files
1. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Action/IEditorCommands.cs` — `EditorCommandDescriptor` record: add a
   **trailing optional** `Func<string>? DynamicDisplayName = null` (after `IsChecked`). All existing constructions
   stay valid.
2. `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/GlobalMenuRegistry.cs` — `MenuItemNode`: add
   `public Func<string>? DynamicLabel { get; set; }` and a pure accessor
   `public string ResolveLabel() => DynamicLabel?.Invoke() ?? Name;`.
3. `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs` — at the two leaf-render sites (the checkable
   `Gui.MenuItem(child.Name, …)` ~L537 and the plain-action `Gui.MenuItem(child.Name, …)` ~L548), use
   `child.ResolveLabel()` instead of `child.Name`.
4. `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/MenuCommandAdapter.cs` — in `ApplyLeafNode(...)`, set
   `node.DynamicLabel = descriptor.DynamicDisplayName;` (alongside the existing Shortcut/GetEnabled wiring).
5. `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/ToolbarCommandAdapter.cs` — add a **pure** seam
   `public static string ResolveTooltip(IEditorCommands commands, string commandId)` returning the tooltip's first
   line as `descriptor.DynamicDisplayName?.Invoke() ?? descriptor.DisplayName` (append `\n{Description}` and
   ` ({DefaultKey})` exactly as `RenderEntry` builds the tooltip today). Make `RenderEntry`'s inline tooltip call
   `ResolveTooltip` (no behavior change beyond using the dynamic label). The checkable-no-icon text fallback (the
   `Gui.MenuItem(descriptor.DisplayName, …)` at ~L114) likewise uses `descriptor.DynamicDisplayName?.Invoke() ??
   descriptor.DisplayName`.

## Tests — EXACT names
**New file** `FDP/Engine/Fdp.Presentation.Tests/ImGui/WindowManager/MenuCommandAdapterTests.cs`:
- `Descriptor_DynamicDisplayName_DefaultsNull` — a descriptor constructed without it has `DynamicDisplayName == null`.
- `MenuNode_DynamicLabel_OverridesName_WhenSet` — a `MenuItemNode { Name = "Save", DynamicLabel = () => "Save [x]" }`
  has `ResolveLabel() == "Save [x]"`; with `DynamicLabel == null`, `ResolveLabel() == "Save"`.
- `MenuAdapter_SetsDynamicLabel_FromDescriptor` — register a command (via `GlobalMenuRegistry` + `MenuCommandAdapter
  .Register`) whose descriptor's `DynamicDisplayName` returns `"DYN"`; locate the produced leaf node and assert its
  `ResolveLabel() == "DYN"`. With a null `DynamicDisplayName`, `ResolveLabel()` returns the path-leaf `Name`.

**In** `FDP/Engine/Fdp.Presentation.Tests/ImGui/WindowManager/ToolbarCommandAdapterTests.cs`:
- `ToolbarTooltip_UsesDynamicDisplayName_WhenSet` — `ResolveTooltip` returns a string whose first line is the dynamic
  value when `DynamicDisplayName` is set, and `DisplayName` when it is null.

> Tests must assert actual resolved strings — no `Assert.True(true)`, no skips, no asserting only the mock.

## Build & test (no BLUEPRINT_REGENERATE_SNAPSHOTS)
```
dotnet build FDP/Engine/Fdp.Presentation/Fdp.Presentation.csproj
dotnet test  FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj
dotnet test  FDP/Engine/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj ^
  --filter "FullyQualifiedName~MenuCommandAdapter|FullyQualifiedName~ToolbarCommandAdapter"
```
All must be `Failed: 0`. (Use the class filter — the full `Fdp.Presentation.Tests` suite deadlocks; do not "fix"
that by disabling tests.)

## Definition of done
- `DynamicDisplayName` added (optional, default null); `MenuItemNode.DynamicLabel`/`ResolveLabel` added and used by the
  two WindowManager leaf-render sites; `MenuCommandAdapter` sets it; `ToolbarCommandAdapter.ResolveTooltip` added and
  used. Existing commands (no `DynamicDisplayName`) render exactly as before.
- The 4 named tests pass; build 0 warnings; `NodeEditor.Core.Tests` `Failed: 0`; filtered `Fdp.Presentation.Tests`
  `Failed: 0`.
- Write `.dev/main-toolbar-2/reports/BATCH-32-REPORT.md`: files changed, tests added, final summaries.

If something cannot be done as specified, STOP and report why.
