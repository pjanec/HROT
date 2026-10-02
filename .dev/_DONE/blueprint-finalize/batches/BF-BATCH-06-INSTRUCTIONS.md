# BF-BATCH-06: ChannelCommand param enrichment (DEBT-BCP-006)
**Goal:** Replace the placeholder `ParamsTypeFqn = "System.Int32"` in `BuiltInChannelCommandCatalog` with each
action's REAL parameter struct FQN, so `NodePinSchema.ChannelCommandPins` projects **rich per-arg data pins**
(e.g. MoveTo → Destination/Speed/…) instead of one placeholder pin, in the editor.

## Lead-verified facts (re-verify, cite)
- `BuiltInChannelCommandCatalog.GetEntries()` (Hrot.Blueprints.Compiler/Compiler/Catalogs/BuiltInChannelCommandCatalog.cs:13-20)
  has 5 entries, each with `ParamsTypeFqn = "System.Int32"` (placeholder). Real structs exist in Fdp.Toolkits:
  - `MoveTo` → `MoveToParams` (FDP/Toolkits/Fdp.Toolkits/Navigation/Executors/MoveToExecutor.cs — has `Destination` etc.)
  - `AimAndFire` → `AimAndFireParams` (FDP/Toolkits/Fdp.Toolkits/Combat/Executors/AimAndFireParams.cs)
  - `OpenDoor` → `OpenDoorParams` (FDP/Toolkits/Fdp.Toolkits/Behavior/Executors/OpenDoorExecutor.cs)
  - `FollowRoute` → find its params struct (search Navigation/Executors for the FollowRoute executor's params)
  - `EjectPassengers` → find its params struct (Behavior/Executors — EmbarkParams or an eject-specific struct)
  - Confirm each struct's **full namespace** for the FQN; confirm the fields are public instance fields
    (NodePinSchema reflects public instance fields → one data-IN pin per field).
- `NodePinSchema.ChannelCommandPins` (Hrot.Blueprints.Editor/Host/NodePinSchema.cs ~341-388) reflects over the
  catalog entry's `ParamsTypeFqn` via `Type.GetType(fqn)`/assembly probing to project per-field pins; if the
  type can't be resolved it degrades to a single value pin. In the net8 EDITOR the Fdp.Toolkits types resolve →
  rich pins. (In the netstandard2.0 MSBuild generator host Fdp.Toolkits isn't loaded → graceful exec-only/
  placeholder fallback — that's expected and tracked as BCF-D03; do NOT make the generator hard-depend on
  Fdp.Toolkits — see BP-3.)

## Tasks
1. Find each action's real param struct + its full FQN (cite file). Update the 5 `ChannelCommandCatalogEntry`
   rows' `ParamsTypeFqn` from `"System.Int32"` to the real FQN. If a struct genuinely doesn't exist for an
   action (no executor params), leave that one as-is and note it.
2. Confirm `NodePinSchema.ChannelCommandPins` resolves the real types in a net8 test and projects one data-IN
   pin per public field (correct names/types). Add/extend a test asserting e.g. a `MoveTo` ChannelCommand node
   projects MoveToParams' fields as pins (in the editor/net8 context).
3. Confirm the COMPILER path still works when the type DOESN'T resolve (ns2.0/generator): no crash, graceful
   fallback (BP-2 no-swallow). Do NOT introduce a hard Fdp.Toolkits load in the generator deserialize/compile
   path (re-run a Full Rebuild of a blueprint to confirm no BP0002 regression — Count2.bp.json is under Blueprints/).

## Success criteria
- [ ] Catalog entries carry real ParamsTypeFqn; editor ChannelCommand nodes project rich per-arg pins. + test.
- [ ] `dotnet build IOS-IG-SimHost.sln` 0 errors / 0 new warnings; Full Rebuild still succeeds (no BP0002).
- [ ] Blueprints suite failures stay a SUBSET of the current 7 pre-existing (0 new) — list the exact final set;
      do NOT claim 0 regressions without the before/after comparison. EditorSubsystemBoot 10/10.
- [ ] Report → `.dev/_DONE/blueprint-finalize/reports/BF-BATCH-06-REPORT.md`.

## Constraints
Branch `blueprint-integ-1`. Projection-only invariant (never persist pins). Do NOT regenerate golden snapshots.
Do NOT add a hard Fdp.Toolkits dependency to the netstandard2.0 compiler/generator deserialize path. Do NOT
touch user WIP (RecipeCreateModal/AssetBrowserWindow/EditorSubsystem). Do NOT commit (lead commits). If the
running editor locks dlls, report it.
