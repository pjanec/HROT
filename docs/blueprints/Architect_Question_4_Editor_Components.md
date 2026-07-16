# Architect question #4 — reusable editor components before we build node drawers

**Context.** A feature-maturity audit found the blueprint **authoring UI is the weakest axis**: ~16/30
node kinds can't be configured in the editor, including several that are fully runtime-wired
(`When` forms are stubs, `WaitForChannel`, `ScoreDecision`, `ReadRankedResult`, `CallPeerBlueprint`,
`CallCustomEvent` have no editing surface), plus the new nodes we're adding (`GetComponent`,
`GetSingleton`, `PublishEvent`, `FlowForEach`, `SlotRotation`/`MemberSlotList`). Before we design *any*
node editing surface, we want to **reuse existing components, not reinvent** — the same win as
`SlotRotation` and the predicate builder. So this is a "what already exists?" shopping list.

We already know these exist (will reuse): the **predicate builder** (`PredicateBuilderState` +
`DataBreakpointManagerPanel`, Universal Breakpoints / Replay search); the **filtered type picker** +
**struct-type choice picker** we built for GetShared / Add-Variable; the **EnumPinEditor** (per the
ChannelCommand catalog comment); the **EQS template/sensor combos** (`SpawnEqsSensor`/`ReadEqsResult`
drawers); and `ChannelCommand`'s **per-action palette + reified param pins**.

## Q — for each editing surface below, is there a ready component to reuse, and how do we mount it in a node drawer?

1. **Predicate / condition-tree editor** — for `When`(ConditionMet). Is `PredicateBuilderState` /
   the DataBreakpoint predicate UI the right thing to embed in the `WhenNodeDrawer` ConditionMet form,
   and is there a drop-in way to bind it to a node's `SearchPredicateDto` (stored as JSON)?
2. **Component + field/property picker** — for `When`(ValueChanged) (`ComponentTypeId` + `PropertyPath`)
   AND for the new `GetComponent` catalog (pick a `[BlueprintReadable]` component + which fields). The
   DataBreakpoint UI seems to pick `Component.Field` — is that extractable as a reusable picker?
3. **Catalog dropdown + reified field pins** — for `PublishEvent` (EngineEventCatalog), named
   `EventEntry` handlers, `GetSingleton`. `ChannelCommand` already does "pick a catalog entry → project
   its param struct's fields as pins." Is that pin-reification path (NodePinSchema-from-FQN) a reusable
   helper we can point at the EventCatalog / component catalog, or is it ChannelCommand-specific?
4. **Asset picker** — for `ScoreDecision` (`AssetId` → a `UtilityDecisionDef`). Any existing
   asset-reference picker widget?
5. **Peer-blueprint + function picker** — for `CallPeerBlueprint` (`PeerBlueprintId` + `FunctionRef`).
   Existing picker over the blueprint catalog + a blueprint's callable functions?
6. **Custom-event picker** — for `CallCustomEvent` (`EventId` over the asset's declared custom events).
7. **Channel-type picker** — for `WaitForChannel` (`ChannelType`); presumably the same catalog as
   `ChannelCommand`. Reuse?
8. **Entity picker** — for wiring an `Entity` pin (self / a named blackboard entity / a subordinate);
   relevant to `GetComponent`/`GetShared` cross-entity and `PublishEvent` target. Existing widget?

## Q — drawer framework
What's the canonical way to add a new node drawer + register it (`BlueprintEditorBootstrap`
`CreateNodeDrawerRegistry` + `IBlueprintNodeDrawer` + create-time baking in `BlueprintCommandSink`),
and is there a shared base/helper for the common "catalog dropdown → reified pins" drawer so we don't
hand-roll each one? (We want `PublishEvent`, `GetComponent`, `GetSingleton` to share one mechanism.)

## Q — the wired-but-unauthorable cohort
`When`, `WaitForChannel`, `ScoreDecision`, `ReadRankedResult`, `CallPeerBlueprint`, `CallCustomEvent`
are runtime-complete but have no editing surface. Any reason their drawers were parked (blocked on a
missing component we should build first), or just not-yet-done? Priority order you'd suggest?
