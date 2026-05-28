# Onboarding — Visual Asset Comparison

Welcome. This document orients a new developer joining the Visual Asset Comparison feature. Read this first; then read the design.

## What we're building

A feature in the visual AI editor that lets a designer compare two versions of a visually-authored asset (a BTree, HSM, Blackboard, or Blueprint) by:

1. Exporting both versions as sanitized text (presentation noise stripped, semantic content preserved).
2. Handing the export to an out-of-band LLM of the user's choice (no editor-side LLM calls — vendor-neutral).
3. Pasting the LLM's structured response back, which the editor uses to annotate affected nodes on the canvas with colored outlines and badges.

Phase 1 supports **historical diff** only (same asset at two points in time, with visualIds correlating). Sibling-asset diff and automatic LLM invocation are deferred.

The architectural detail and rationale are all in [Visual_Asset_Comparison_Detailed_Design.md](./Visual_Asset_Comparison_Detailed_Design.md). It's long — read it end-to-end before touching code.

## Project map — where the components live

| What | Where |
|---|---|
| Shared comparison code | `Hrot/Editor/Hrot.Editor.AiShared/Comparison/` (new) |
| Shared tests | `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/` (new) |
| Shared identity / catalog (existing) | `Hrot/Editor/Hrot.Editor.AiShared/{Identity,Catalog,Selection,Layout}/` |
| BTree editor (host) | `Hrot/Subsystems/AI/Hrot.BTree.Editor/` |
| BTree tests | `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/` |
| HSM editor (host) | `Hrot/Subsystems/AI/Hrot.Hsm.Editor/` |
| HSM tests | `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/` |
| Blueprint editor (host) — note plural | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/` |
| Blueprint tests | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/` |
| NodeEditor custom-canvas-renderer interface | `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/ICustomCanvasRenderer.cs` |
| JSON Migration system (design, not yet built) | `.dev/json-migration/` |

> **Naming gotcha.** The design refers to `Hrot.Blueprint.Editor` (singular). The actual project on disk is `Hrot.Blueprints.Editor` (plural). All tasks use the actual name.

## Where the docs live

| Doc | Path | Purpose |
|---|---|---|
| Detailed Design | [Visual_Asset_Comparison_Detailed_Design.md](./Visual_Asset_Comparison_Detailed_Design.md) | The authoritative spec — every architectural decision and rationale. |
| Task Details | [TASK-DETAILS.md](./TASK-DETAILS.md) | One section per task with success conditions; references the design. |
| Task Tracker | [TASK-TRACKER.md](./TASK-TRACKER.md) | Binary status per task; links to TASK-DETAILS. |
| Debt Tracker | [DEBT-TRACKER.md](./DEBT-TRACKER.md) | Known shortcuts, deferred work, P1/P2/P3 items. |
| Migration System (referenced) | `.dev/json-migration/Migration-system.md` | Background for §3.5 of the design (Blueprint migration step). |

## How to build

The project uses a standard .NET 8 solution:

```powershell
# From the repo root (d:\Work\IOS-IG-SimHost-FDP-2)
dotnet build IOS-IG-SimHost.sln
```

To run a specific test project:

```powershell
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj
dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj
```

Convenience scripts in the repo root:

- `run_Editor.bat` — launches the editor (the host into which the comparison UI integrates).
- `build_all_standalone.bat` / `run_all_standalone.bat` — repo-wide build/run.

## Existing infrastructure you'll be consuming

These already exist; do not re-create them. Read briefly before starting:

- `Hrot.Editor.AiShared.Catalog.IAssetCatalog` — GUID → (Name, Kind) lookup used by every sanitizer for cross-asset reference humanization.
- `Hrot.Editor.AiShared.Identity.AssetKind` — the enum that drives sanitizer registration (`BTree`, `Hsm`, `Blackboard`, `Blueprint`).
- `Hrot.Editor.AiShared.Selection.EditorSelectionStore` — gains an optional `ActiveComparisonId` per asset (§8.1).
- `Hrot.Editor.AiShared.Layout.{BTreeLayoutAttribute, HsmLayoutAttribute, LayoutDiscovery}` — used by the BTree/HSM sanitizers to find the layout method.
- `Hrot.Editor.AiShared.Blackboard.SubtreeSyncBinding` — the structural form the sanitizer extracts from `.SubtreeSyncField(...)` calls.
- `NodeEditor.Core.Interfaces.ICustomCanvasRenderer` — the renderer interface (`Pass = AfterNodes` for our annotation renderer).
- `BTreeFluentEmitter`, `HsmFluentEmitter` — the canonical emitters whose output our sanitizers must consume. Read these to understand the file shape we're parsing.

## What's NOT yet implemented

- The JSON Migration System (designed in `.dev/json-migration/`). The comparison feature ships **no-op default implementations** of `IComparisonMigrationAdapter` and `IMetaEnvelopeSanitizer` (TASK-C-08) so that Phase 1 can ship before the migration system lands. Once the migration system ships, production adapters wrap `ReadOnlyMigrationAdapter` and swap in via DI; comparison code is unchanged.
- All `Comparison/` sub-folders mentioned in TASK-DETAILS. This is greenfield.

## How to behave as a developer on this project

Read [.dev/.guides/DEV-GUIDE.md](../.guides/DEV-GUIDE.md) before submitting work. It defines the project's expectations for change scope, commit hygiene, testing discipline, and review etiquette.

## A reasonable first-week plan

1. Read the design end to end.
2. Read TASK-DETAILS for slice C-1.
3. Verify the existing infrastructure (`Hrot.Editor.AiShared`) compiles and tests pass locally.
4. Pick up TASK-C-01; treat it as the seam through which every other task hangs.
5. Pair-review with someone who's read the design to make sure your interfaces match §3.2's intent before writing C-2/C-3 sanitizers against them.
