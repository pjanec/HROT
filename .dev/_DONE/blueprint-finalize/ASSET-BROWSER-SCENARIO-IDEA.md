# Design idea: surface Scenarios as an asset type in the shared Asset Browser

**Status:** captured idea — NOT yet planned/scheduled. Editor + cross-layer; visual/wiring-heavy (token-hungry, not a
quick Zoo task). **Architect-consult candidate** (cross-layer seam is a genuine design fork). Recorded 2026-06-07.

## The idea (user)
Today scenarios are loaded via a **separate scenario browser + a custom Load modal**. Instead, list scenarios as a
**fourth asset type** in the shared **Asset Browser** (alongside BTree / HSM / Blueprint), with a "Scenario"
type-filter chip — unifying the user experience (one place to discover/open all assets).

## Verdict: makes sense — unify DISCOVERY, keep the action type-specific
- **UX win:** the Asset Browser is already the shared discovery surface (folder tree + type-filter chips, see
  `docs/blueprints/AI_Editor_Shared_Infrastructure.md §9`). A separate scenario browser fragments the mental model.
- **Feasible — the catalog is already pluggable:** the browser lists from `IAssetCatalog`
  (`Hrot/Editor/Hrot.Editor.AiShared/Catalog/`), and there is an **`IAssetCatalogContributor`** + `AiAssetCatalogBuilder`.
  So scenarios can be added to the *listing* via a new contributor — no browser rewrite.

## Key design points (the load-bearing nuance)
1. **Unify discovery, NOT the open action.** BTree/HSM/Blueprint rows open in a NodeEditor canvas (graph editing). A
   **scenario is a different kind of asset** — it lives in another layer and its row action is "**load** world/sim
   state," not a canvas edit. So make the browser row's **primary action pluggable per asset type**: open-in-canvas
   for AI assets; invoke the **existing scenario Load flow/modal** for scenarios. Keep the Load modal — just trigger
   it from a unified row instead of a separate browser.
2. **Invert the dependency.** `Hrot.Editor.AiShared` (the browser) must NOT depend on the scenario/engine layer. The
   scenario/CGF subsystem should **register** an `IAssetCatalogContributor` + a row-action handler into the shared
   browser. Scenario layer lives in: `Hrot/Engine/Hrot.Core/Scenario/`, `Hrot/Engine/Hrot.Presentation/ScenarioEditor/`
   (`HrotEditLoadHandler`, `ScenarioFileService`), and CGF (`Hrot/Subsystems/Hrot.CGF/...ScenarioLoadHandler`,
   `HrotScenarioLoader`).
3. **Caveats:** (a) scenarios likely don't use the AI `Guid AssetId` identity — the catalog-entry abstraction must
   tolerate a different handle/id; (b) the optional reverse-dependency sidebar (subtree calls, AiPrimitive hostings)
   doesn't map to scenarios — make it per-type/optional.

## Next steps when picked up
- Confirm `IAssetCatalogContributor` is the real extension seam (read it) and how `AssetBrowserWindow` dispatches a
  row's open action.
- Run the cross-layer seam past the architect (how to plug a foreign-layer asset type + its load action without
  AiShared depending on the scenario layer).
- Then: scenario contributor (listing) + pluggable per-type row action (scenario → existing Load flow). Defer until
  there's a visual-iteration budget; gate on a user smoke. `AssetBrowserWindow` is user WIP — coordinate before edits.
