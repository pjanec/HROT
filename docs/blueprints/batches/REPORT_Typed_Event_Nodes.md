<!--STATUS
state: LIVE
updated: 2026-10-02
current-answer: the whole file — the report of HANDOFF_Typed_Event_Nodes.md (dispatched at 33dbb52bf).
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->

# REPORT — Typed event nodes (E1–E6)

**Handoff:** [`HANDOFF_Typed_Event_Nodes.md`](HANDOFF_Typed_Event_Nodes.md) · **design:** [`DESIGN_Typed_Event_Nodes.md`](../DESIGN_Typed_Event_Nodes.md)
(as-built in §6) · **branch:** `behaviors` (the session's push branch, per the user) · **scope frozen at** `33dbb52bf`;
started-marker `13726f4ef`.

## 1. What was built — one commit per slice, green at each

| slice | commit | ids | what |
|---|---|---|---|
| E1 | `cf8a8d695` | CE-2010, CE-2011 | the two silent drops: a second event node is `BP1682`; an Instance with two handlers of one event runs both (one table entry per key, `EventGroup_<first>_Thunk`) |
| E2 | `233db743d` | CE-2012, CE-2013 | `EventEntryNode.Fields` + `EventPayload`; load-time migration; `Stage2_6_SplitEventHandlers` (one handler per typed node, shared tails cloned); `BP1682` lifted for typed nodes; new `BP1683` |
| E3 | `40331581f` | CE-2014 | the whole-event `Event` pin, split by `BreakStructNode` |
| E4 | `efa051081` | CE-2015 | "On: {Event}" palette, `EventEntryNodeDrawer` (Policy/Capacity/Self), signature window "n/a"; closes build tracker 4a/4b |
| E5 | `f86047db0` | CE-2016 | system events in the one discovery source (Q14-A2, in the form the layering allows); closes build tracker 1f |
| E6 | `63d46aac4` | CE-2017 | debug identities of a split handler name the authored nodes; step-over starts at the handler that reaches the node |

⭐ **Ids allocated:** `CE-2010` – `CE-2017` (from the reserved `CE-2010`–`CE-2029`). `CE-2009` closed. Diagnostic codes
`BP1682`, `BP1683`.

## 2. Deviations from the design — each folded into the design's §6.3

| # | deviation | why |
|---|---|---|
| D1 | the split runs at **Stage 2.6** (after macro expansion, before Stage 3) | a macro body can sit in a shared tail; Stage 3's orphan pass and literal synthesis and Stage 4's typing then run per handler, clones included. §4 said only "before Stage 5" |
| D2 | `BP1682` does **not** bind a second entry in a Function graph | a loose extra entry there was already an orphan, and `ListVariableWriteTests.BP1505_…` relies on it (Stage 2 stops at its first error). T-7 adds no rule for those graphs |
| D3 | ⚠⚠ **E5 / Q14-A2:** `[BlueprintEvent]` is NOT put on the system event structs | the attribute lives in `Hrot.Editor.AiShared`; the FDP toolkit assemblies that own the structs cannot reference it. The catalog stays the source of identity and metadata; the fields are reflected (no hand list). ⭐ **A user call if the full form is still wanted:** move the attribute to an assembly FDP can reference |
| D4 | E6: a clone's back-reference is `Graph.HandlerDebugIds`, not `Node.OriginNodeId` (E2's first cut) | nothing in the editor reads `OriginNodeId`, and the macro precedent keeps the clone id on probes. A per-graph map applied to DEBUG identities only gives authored probes and leaves the clones' state keys alone |

## 3. Gates *(⭐ base = `33dbb52bf`, measured in a worktree this session)*

| # | gate — verbatim command | result | delta vs base |
|---|---|---|---|
| 1 | `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj --no-build` | **4060 / 0 / 17** | base 4040 / 0 / 17 ⇒ **+20 new rails, none lost** |
| 1 | feature suites first (T-1): `--filter "FullyQualifiedName~BlueprintBehaviourTests\|…~CustomEventPubSubCapstoneTests\|…~EventGraphEmitTests\|…~WhenNode\|…~NodeCoverageTests\|…~V_ResolverPurityTests"` | baseline **255 / 0 / 3** before any change | all new rails added INTO these suites (+ `BlueprintEventPaletteEntriesTests`, `TickBridgeTests`) |
| 2 | `dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/… --no-build` | **342 / 0 / 0** | unchanged |
| 2 | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/… --no-build` | **445 / 0 / 2** | unchanged. ⚠ one intermittent red seen once at E5 — `EditorCargoSystemTests.Embark_AtCapacity_DoesNotExceedCapacityLimit`; green in 3 isolated runs and every full run since; cargo code untouched ⇒ a FINDING for its owner (R-131), not this batch's |
| 2 | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/… --no-build` | **1034 / 3 / 3** | the 3 reds are exactly the handoff's known pre-existing ones: `MapPresentationParityRails…EditorStrideSubsystem`, `NodeRolePersistenceRails.TheSaveHandlerSetIsStillComplete`, `FullBranchPipelineTests.BranchedRecording…` |
| 2 | `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler.Tests/…` *(not in the handoff's list; run anyway)* | 2 / 1 / 0 | ⭐ **pre-existing**: `BlueprintJsonServicesTests.BlueprintJsonServices_Serialize_ProducesMetaEnvelope` fails identically at `33dbb52bf` (expects envelope version 1; `Serialize` writes v2) |
| 2 | `Fdp.Toolkits.Tests` | **skipped** | no runtime file changed: `git diff --stat 33dbb52bf..HEAD -- FDP` is empty |
| 3 | golden movement | **3 files, +13 −27** | ⭐ **zero generated-C# goldens moved.** `CustomEventSubscriberDemo.bp.json`: its `OnPing` graph's 2 `Inputs` moved onto its event node's `Fields` (the E2 migration, written through the guarded `Canonicalise_Rewrite`); `persistence-shape.txt`: that one file's hash/size line; `GoldenCorpus.cs`: the harness runs Stage 2.6 like the real compiler (1 line) |
| 4 | reds pre-existing against the base | yes | see rows above — every red named, every one red at `33dbb52bf` or listed in the handoff |
| 5 | working tree clean after each suite run | yes | `git status` showed only this batch's edits after every run |
| 6 | `python3 scripts/tracker-counts.py --check` · `design-digest.py --check` · `rulings-check.py` · `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs` | OK · OK · 45/45 · 5/5 blocks (`DESIGN_Typed_Event_Nodes.md`), 1/1 (`Custom_Events_Design.md`) | — |
| 7 | ids | `CE-2010`…`CE-2017` | — |

⚠ **Honest note on E1:** its commit lacked `[CoversDiagnosticCode("BP1682")]`, so `V_AllValidatorsCoverageTests` was red
for that one commit (I ran the feature suites, not the full suite, before pushing it). Fixed in E2's commit; every
later slice was gated on the full suite.

## 4. Red-proofs *(gate 8 — what was broken, that it went red, that it was restored)*

| rail(s) | broken how | red |
|---|---|---|
| E1 `ASecondEventNodeInOneEventGraph…`, `TwoEventGraphsOnOneEventType…BothRun` | the compiler changes stashed | 2/2 |
| E2 — the six split rails | the multi-node split replaced by pass-through | 6/6 |
| E2 `E2_ARunBehaviourInASharedTail…` | shared tail not cloned | red (one site slot, not two). ⭐ `TwoEventNodesIntoOneExecChain…` stays green — correctly: it holds no per-node state, so it proves the tail runs for both, not the salt |
| E2 `ALegacyEventGraph_MovesItsInputs…` | migration removed from the load | red |
| E3 both | the thunk passes `__ev.__event` | 2/2 |
| E4 `AnOnNodeInAFunctionGraph_IsBP1682` | the placement rule disabled | red. *(The palette entry and drawer did not exist before E4: those rails could not compile without it)* |
| E5 both | the system-event branch of `UnifiedEventDiscovery` emptied | 2/2 |
| E6 both | the Stage 5 debug-id rewrite skipped · the old `FirstOrDefault` entry | 2/2 |

## 5. Findings for others *(not fixed — outside the batch)*

| finding | where |
|---|---|
| `DebugMapEntry.GraphId` is empty for many entries of EVERY graph (`Stage5_Schedule.DebugOf` sets `default`) | compiler debug map — pre-existing |
| macro-clone probes fire with the CLONE's id, and nothing in the editor reads `OriginNodeId` ⇒ a macro body's nodes cannot light on the canvas | debugger — pre-existing; E6's `HandlerDebugIds` is a pattern it could reuse |
| `EditorCargoSystemTests.Embark_AtCapacity…` intermittent | `Hrot.Editor.Tests` — R-131 |
| `BlueprintJsonServices_Serialize_ProducesMetaEnvelope` red at base | `Hrot.Blueprints.Compiler.Tests` — R-131 |

## 6. Deliverables checklist

- ✅ code + rails per slice, committed and pushed on `behaviors`
- ✅ as-built folded into [`DESIGN_Typed_Event_Nodes.md`](../DESIGN_Typed_Event_Nodes.md) §6 (class + sequence UML, deviations), its STATUS `build-state: BUILT`
- ✅ [`Custom_Events_Design.md`](../Custom_Events_Design.md) §4.5 marked as-built (+ a STATUS block); [`Custom_Events_BUILD_TRACKER.md`](../Custom_Events_BUILD_TRACKER.md) 4a/4b/1f closed
- ✅ tracker rows `CE-2010`…`CE-2017`; `CE-2009` closed
- ✅ this report
