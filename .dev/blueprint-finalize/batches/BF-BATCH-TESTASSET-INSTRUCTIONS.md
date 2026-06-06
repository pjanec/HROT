# BF-TESTASSET: working ChannelCommand + inline-editor-types test recipe(s) + ChannelCommand drawer diagnostic
**Goal:** Give the user concrete, openable test blueprint(s) that (1) show a WORKING, pre-configured
ChannelCommand node (so its param pins + Details drawer are visible), and (2) exercise the inline value-pin
editor types (int/float/bool/string/enum). Plus a headless test confirming the ChannelCommand drawer resolves
(to isolate the live "Details: No node selected" report).

## Context / lead-verified
- BATCH-06 enriched `BuiltInChannelCommandCatalog` so a ChannelCommand with `ChannelType` + `ActionId` SET
  projects real param pins (MoveTo→MoveToParams 9 fields, etc.). An UNCONFIGURED ChannelCommand (no ActionId)
  shows only 2 exec pins — likely what the user saw.
- BATCH-07/fix added inline editors for unconnected In-data pins of registry-supported types
  (`PinDefaultValueEditorRegistry.CreateWithBuiltins()` — confirm which TypeKeys it covers: int/float/bool/
  string/enum?). An editor shows only for those types.
- `ChannelCommandNodeDrawer` is registered (BlueprintEditorBootstrap.cs:46); `BlueprintDetailsWindow.ResolveSession`
  shows "No node selected" when there's no `BlueprintNodeSelection`, the node isn't found by id in the asset
  graph, the drawer is null, or `CreateSession` returns null.
- Recipe format reference: `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/CountingDemo.bp.json` (the lead
  authored it: $meta envelope, `EditorMetadata.Recipe` block to appear in New-from-Recipe, per-node
  `EditorMetadata.X/Y` layout, `"kind"`-first node objects, links with deterministic pin GUIDs). Recipes are
  EXCLUDED from the MSBuild generator (templates).

## Tasks
1. **Author test recipe(s)** under `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/`:
   - **A working ChannelCommand example:** a valid blueprint (use the dispatch that legally hosts a
     ChannelCommand — verify against Stage2 validation; locomotion/AiPrimitive likely) with a ChannelCommand
     node whose `ChannelType`/`ActionId` are SET to a real action (e.g. LocomotionChannel/MoveTo). EventEntry →
     ChannelCommand → Return (exec wired). Laid out (X/Y), `$meta`, Recipe block, kind-first nodes.
   - **Inline-editor-type coverage:** include node(s) with UNCONNECTED In-data pins covering the registry's
     supported types (int, float, bool, string, enum if supported) so each inline editor renders. Determine the
     cleanest way to produce such pins (e.g. the MoveTo param pins already cover some types; add a FunctionCall
     or other node for the rest — pick whatever yields one unconnected pin per supported editor type). It's fine
     to make this a SECOND recipe ("EditorTypesDemo") if cleaner than one combined asset.
   - Each recipe must be VALID (compiles) and project the intended pins. Verify HEADLESSLY:
     - It deserializes (`BlueprintJsonServices.Deserialize`), round-trips byte-stable.
     - `NodePinSchema.GetCanonicalPins` for the configured ChannelCommand projects the action's param pins.
     - For the editor-types asset, the relevant nodes' unconnected In-data pins are of the supported types
       (so `BlueprintPinModel.Default` would return an editor — assert via the registry/BlueprintPinModel).
     - The asset compiles through `BlueprintCompiler.Compile` with no errors (write a small proof test, or
       confirm via an existing harness).
2. **ChannelCommand drawer diagnostic test:** add a headless test that, given a `BlueprintDetailsWindow` with the
   real drawer registry (from `BlueprintEditorBootstrap.CreateNodeDrawerRegistry`) and a selection pointing at a
   ChannelCommandNode in the asset, `ResolveSession()` returns a NON-NULL session whose drawer is
   `ChannelCommandNodeDrawer`. If this PASSES, the live "No node selected" is a selection/wrong-node issue, not a
   drawer bug — state that in the report. If it FAILS, root-cause + fix the drawer resolution.

## Success criteria
- [ ] Working ChannelCommand test recipe (configured action → param pins) + editor-types coverage recipe,
      both VALID + compile + project the intended pins (headless-verified). Appear in New-from-Recipe.
- [ ] Drawer diagnostic test added; report states whether ChannelCommand drawer resolves headlessly (and if not,
      the fix).
- [ ] `dotnet build IOS-IG-SimHost.sln` 0 errors / 0 new warnings; Full Rebuild succeeds (recipes excluded from
      generator, so no BP0002).
- [ ] Blueprints failures a SUBSET of the 7 pre-existing (0 new) — list the final set. Boot 10/10.
- [ ] Report → `.dev/blueprint-finalize/reports/BF-BATCH-TESTASSET-REPORT.md` with: the recipe contents +
      what each node/pin demonstrates, the headless verifications, the drawer-diagnostic result, and the
      user-facing instructions ("New-from-Recipe → X → select the ChannelCommand node, its Details shows the
      action Combo + param pins; node Y's pins show int/float/bool/string editors").

## Constraints
Branch `blueprint-integ-1`. Projection-only. Do NOT regenerate goldens. Do NOT touch user WIP
(RecipeCreateModal/AssetBrowserWindow/EditorSubsystem) or the Count*.bp.json experiment files. Do NOT commit
(lead commits). If the running editor locks dlls, report it. Verify recipe JSON is `kind`-first per node +
`$meta` present (System.Text.Json polymorphism needs the discriminator first).
