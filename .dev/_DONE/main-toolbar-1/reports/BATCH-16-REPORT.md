# BATCH-16 Report (compiled by dev-lead — worker did not emit a report)

## Files changed
- **NEW** `Hrot/Subsystems/Hrot.Editor/Browser/ScenarioEnumeration.cs` — recursive scenario relpath enumeration (T5).
- **NEW** `Hrot/Subsystems/Hrot.Editor/Browser/AssetPickActionRouter.cs` — pick→action router (T6).
- **NEW** `Hrot/Subsystems/Hrot.Editor.Tests/ScenarioNestedNameTests.cs` — T5 tests (temp-dir).
- **NEW** `Hrot/Subsystems/Hrot.Editor.Tests/Browser/AssetPickActionRouterTests.cs` — T6 tests.
- **MOD** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — `SetAvailableScenariosSource(() =>
  ScenarioEnumeration.EnumerateRelPaths(EditorBootstrap.ScenariosRoot))`.

## T5 — Scenario nested-name
- `ScenarioEnumeration.EnumerateRelPaths(root)`: recursively walks `root`, emits the relpath (relative
  to `root`, `/`-normalized, ordinal-sorted) of every directory containing a `scenario.json` marker;
  excludes the root itself; returns empty for a missing/blank root.
- `AvailableScenarios` source rewired to this helper (was top-level-only).
- `SaveScenarioAs`/`SaveCurrentScenario` already use `Path.Combine(ScenariosRoot, name)` +
  `Directory.CreateDirectory`, so a nested name (`Combat/Patrol`) creates the nested folder — verified
  by test.

## T6 — Caller wiring (pick → action)
- `AssetPickActionRouter.Route(IEditableAsset)`: Scenario → `loadScenario(asset.Name)`; Blueprint/BTree/
  Hsm → `openDocument(asset)`; other kinds → no-op. Uses delegate seams
  (`Action<IEditableAsset> openDocument`, `Action<string> loadScenario`) so it is unit-testable;
  production wires `openDocument`→`AiDocumentManager.Open`, `loadScenario`→`IEditorLogic.LoadScenarioByName`.
- **Wiring gap (DBT-2, P1):** the router + the BATCH-15 hosts are not yet instantiated/registered at a
  production composition point — surfacing the browser is a Phase 7 concern (Workspace/Scenario menu).
  The named T6 success-condition tests pass; production glue tracked as DBT-2.

## Tests (run by dev-lead)
- `ScenarioNestedNameTests` + `AssetPickActionRouterTests` → **21 passed, 0 failed** (unfiltered).
- Full `dotnet build IOS-IG-SimHost.sln` → 0 errors, 0 new warnings.
- (A lone `Hrot.SimHost.Tests.AtomicMultiFileWriterTests` ordering flake was observed by the worker;
  it passes in isolation — same nondeterministic test-infra family as PRE-3/PRE-4, unrelated.)
