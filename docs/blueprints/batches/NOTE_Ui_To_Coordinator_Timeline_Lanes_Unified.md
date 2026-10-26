<!--STATUS
state: LIVE — ui → coordinator: the lanes-unified steer (R-4078, R-4079) built on CE-1302
updated: 2026-10-26
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/DESIGN_Director_Timeline_Editor.md — §9c the steer's design, §9d its as-built (deviations argued there)
  - docs/blueprints/AI_Editor_Shared_Infrastructure.md — §15.2 the trace lane seam, now marked superseded by R-4078
-->

# NOTE — ui → coordinator: lanes unified ([STEER_Coordinator_To_Ui_Timeline_Lanes_Unified](STEER_Coordinator_To_Ui_Timeline_Lanes_Unified.md), `CE-1302`)

Built on `ui`: `6f43b4794` (the move, the model, the index, the shipped lanes), `d81228019` (the last qualified references, §9d, docs,
tracker), the fixture commit for the Decisions red-proof, and this note's commit. Scope frozen at the steer's merge (`63b88cf31`); no later document invalidated an item.
**Ids allocated: none.** As-built + deviations: [§9d](../../DESIGN_Director_Timeline_Editor.md).

| steer item | as built |
|---|---|
| **① one seam** | `ITraceLaneProvider` → `ITimelineLaneProvider`, `TraceLaneDescriptor` → `TimelineLaneDescriptor` (Roslyn over stdio, the MCP was not connected), moved with `TraceLevel` to `Fdp.Toolkits/ReplayBrowser/Timeline`. `Kind` → `Applies(TimelineLaneContext)`; the descriptor gained group / priority / colour / glyph; `Indexed` and `Marks` are optional members. No second interface, no adapter. The BTree / HSM / Utility providers answer `Applies` by asset kind |
| **② lanes as data** | `IndexedLaneDefinition` = descriptor + `SearchPredicateDto` + `ResultProjectionDto`; `TimelineLaneCompiler` compiles them once, the builder evaluates all in one pass, events keyed by lane id. `TimelineLane*` enums + `TimelineExtractors` deleted. The nine shipped lanes are registered by their owners: `ReplicationTimelineLanes`, `CombatTimelineLanes`, `BehaviourTimelineLanes`. The Replay Browser composes a `TimelineLaneCatalog` and the panel draws whatever it holds |
| **③ the model** | `ResultProjectionDto` on scanner results (search results carry it too); `RingEntryPredicateDto` + `RingEntryScanner`; ONE `StructuralScanner` shared by search and timeline |
| **④ the cache** | sidecar schema 2: per lane id + definition hash + events; a new or changed lane is the only one scanned (`ScannedLanes`) |
| **⑤ user lanes** | `TimelineLaneCatalog.Register(IndexedLaneDefinition)`, indexed like a shipped one (rail). ⏳ No editor UI to author one yet |

⚠ **Deviations, argued in §9d:**
- the projection is the model's own `ResultProjectionDto`, not a separate `LaneProjection` (R-4077, one model);
- Life lanes are structural, not `LifecyclePredicateDto`, which matches ONE entity (`MatchesLifecycleCriteria` treats a net id as an entity index);
- "Destroyed" is named **Left**;
- the Reports counterpart is the sender;
- the host injects the compiler, because `Fdp.Toolkits` has no `StructEdit.Reflection`.

⭐ **One behaviour fix rides along:** an `Added`-only structural Replay Search no longer reports a destroyed entity as "Lost".

## Gates

| gate | command (TEST project built first, then `--no-build`) | result |
|---|---|---|
| toolkit search + timeline | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build --filter FullyQualifiedName~ReplayBrowser` | **190 / 0** (187 before + 3 new rails) |
| panel layout | `Fdp.Presentation.Tests --filter ~Timeline` | **26 / 0** |
| frame rail (real ImGui frame) | `xvfb-run -a -s "-screen 0 1920x1080x24" … --filter ~TimelinePanelFrameRail` | **1 / 0** ⚠ under plain `xvfb-run -a` the harness could not open its window (an environment failure, not an assertion) |
| Replay Browser | `Hrot.ReplayBrowser.Tests` | **35 / 0** |
| editor providers | `Hrot.Editor.AiShared.Tests` 2154 / 0 / 10 skipped · `Hrot.BTree.Editor.Tests` 667 / 0 · `Hrot.Hsm.Editor.Tests` 673 / 0 · `Hrot.Utility.Editor.Tests` 145 / 0 · `Hrot.Diagnostics.Breakpoints.Tests` 168 / 0 | green |
| Blueprints | `Hrot.Blueprints.Tests` | 4232 / **2** / 17 skipped. ⭐ Both reds are PRE-EXISTING: the same two fail at base `63b88cf31` in a clean worktree. `CE2083_SopOrderUrgency_MirrorsReactionUrgency`: `SopOrderUrgency` lacks the Grenade / Break that `ReactionUrgency` gained. `RestoreTo_IncludeTransient_RegistersRecordableNonSnapshotableTypes`: no throw |
| docs | `tracker-counts.py --check` OK (open 105 / done 387) · `rulings-check.py` probes OK (staleness warning on the design doc, edited today) · mermaid 13/13 · `design-digest.py --check` ⛔ flags `docs/DESIGN_Utility_Decision_View.md` (no INVENTORY): not this lane's change (last touched `3989560d3`, CE-1325) | — |

⚠ **Build note:** `Hrot.Utility.Editor.Tests` and `Hrot.Blueprints.Tests` failed to build until restored (`NETSDK1004`, then a missing
`Hrot.SimHost.Serializers`). This is the third stale-binary trap, triggered by the coordinator merge; it was not a code fault.

## Red-proofs — the extractors' knowledge, now the definitions' tests

Each is a compiling mutation of a definition or scanner; the named rail must go red. The tree was restored after each.

| mutation (compiling) | rail | result |
|---|---|---|
| Shot: a refused fire counts (`AnyOccurrence = true`) | `TimelineIndexTests.CE1302_OnePass…` | ✅ red |
| Orders: `Repeats` dropped from the entry key | same | ✅ red |
| Decisions: the `SwitchCount > 0` filter dropped | same | ✅ red. ⚠ It was GREEN on the first try because the fixture had no never-switched slot. Fixed in `TimelineIndexTests` (a slot that ran and never switched), then proven red |
| Ring: the first sighting is not a baseline | `RecordingSearchServiceTests.CE1302_RingEntry…` + `CE1302_OnePass` | ✅ red (2) |
| Structural: an `Added` predicate reports a destroyed entity | `CE1302_StructuralAdded…` + `CE1302_OnePass` | ✅ red (2) |
| Sidecar: a changed lane reused (hash ignored) | `CE1302_ANewOrChangedLane…` | ✅ red |

Screens from the frame rail: Life / Combat / Behaviour groups now come from the descriptors.
