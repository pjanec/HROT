# Blueprint editor components — what to reuse (architect-confirmed, question #4)

> Building node drawers = **wiring existing components**, not inventing. Two architecture rules first,
> then the component map + drawer priority. All editor UI is Windows-verifiable (ImGui).

## Two rules that shape everything

1. **`StructEdit` is the universal editor.** To edit any config DTO, call
   `_editService.Open(dto, typeof(TDto))` — `ComponentEditDrawer` renders the full (nested,
   polymorphic) UI from the DTO's JSON attributes. So a "drawer" is often just: open the node's
   config struct in StructEdit. Two big consequences:
   - **Predicate/condition editor** (When ConditionMet) = `_editService.Open(dto, typeof(SearchPredicateDto))`.
     No panel extraction — StructEdit renders the whole condition tree, same as the Replay Browser.
   - **Entity pick** = expose the target as a struct field with `[MapPickableEntity]`; StructEdit auto-renders
     a "Pick Entity" map button via `MapPickServiceBridge`.
2. **Drawers do NOT project pins — `NodePinSchema` does.** A drawer (`IBlueprintNodeDrawer` +
   `INodeEditSession`) only *mutates node properties* (save a string `ActionId`/`TargetTypeId`/…). Pin
   **reification** is a separate case in **`NodePinSchema.GetCanonicalPins`** that reads those string
   props, queries the catalog, and calls **`ReflectDataMembers(Type)`** — the reusable helper that
   decomposes any struct into data-IN pins (handles `[InlineArray]`, primitives, nested structs). So
   there is **no shared "drawer base for pins"**; the shared engine is `ReflectDataMembers` in
   `NodePinSchema`. Build P2/P3/P4 as: thin drawer (pick catalog entry → save a string) + a
   `NodePinSchema` case (string → catalog → `ReflectDataMembers`).

## Component map (per editing surface we need)

| Surface | Reuse | How |
|---|---|---|
| Predicate / condition tree (When ConditionMet) | `SearchPredicateDto` + StructEdit | `_editService.Open(dto, typeof(SearchPredicateDto))` |
| Component picker + field/property picker (When ValueChanged; `GetComponent`) | `FilteredTypeComboFieldDrawer` + `PropertyPathFieldDrawer` | currently in `Fdp.Presentation.Panels.ReplayBrowser.Drawers`; **lift into `Hrot.Editor.AiShared`** and register in the Blueprint editor's `ComponentEditServiceBuilder` so StructEdit applies them to the payload fields |
| Catalog dropdown → reified pins (PublishEvent, EventEntry, GetSingleton) | `NodePinSchema.GetCanonicalPins` + `ReflectDataMembers(Type)` | drawer saves the picked entry's string id; `NodePinSchema` case reflects the struct into pins |
| Asset picker (ScoreDecision `AssetId`) | `AssetPickerLauncher` (Tree-layout, all asset kinds) | *not* the stubbed `BlueprintAssetGridPickerSource` |
| Peer + function picker (CallPeerBlueprint) | build a combo | over `asset.CallablePeers` + `peerSignatureLookup` (in the graph model) |
| Custom-event picker (CallCustomEvent) | ImGui combo | over `_parentAsset.CustomEvents` |
| Channel-type picker (WaitForChannel) | `IChannelCommandCatalog.GetEntries()` | distinct `ChannelTypeFqn` values |
| Entity picker (cross-entity reads, PublishEvent target) | `[MapPickableEntity]` + StructEdit | auto map-pick button via `MapPickServiceBridge` |
| Type picker / struct-type picker / EnumPinEditor / EQS combos | already built (ours + engine) | reuse as-is |

## Drawer framework
`IBlueprintNodeDrawer` + `INodeEditSession`, registered in
`BlueprintEditorBootstrap.CreateNodeDrawerRegistry`. Create-time property baking (if any) goes in
`BlueprintCommandSink`. A node with **no** registered drawer falls back to the raw-field dump drawer —
so the parked cohort is authorable-in-the-ugly-way today, just not nicely.

## Architect-suggested drawer priority
1. **`When`** — flagship reactive primitive; wire all four mode forms using the lifted Replay
   components (predicate via StructEdit; component/field via the lifted drawers).
2. **`CallCustomEvent` + `CallPeerBlueprint`** — needed for Instance composition / cross-asset logic.
3. **`WaitForChannel` + `ReadRankedResult`** — simple dropdowns; unblock CQRS + Utility flows.

*(The parked cohort was just not-done — prioritization, not a missing component.)*
