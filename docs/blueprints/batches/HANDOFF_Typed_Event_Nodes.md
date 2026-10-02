<!--STATUS
state: LIVE — DISPATCHED at 33dbb52bf (2026-10-02, user: "approved. record it. and pls write handoff, i will run this in
  another session"); scope frozen there.
updated: 2026-10-02
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/DESIGN_Typed_Event_Nodes.md — THE design this batch builds (inventory, UML, slices, approved decisions).
  - docs/blueprints/Custom_Events_Design.md — owns discovery/publish/dispatch; its subscribe side (§4.5, 4a/4b) is what changes.
  - docs/blueprints/DESIGN_Unified_Behaviour_Run.md — owns fibers + event policies (§4a); each handler is one fiber graph there.
-->

# HANDOFF — Typed event nodes: any number of events handled in one graph

**For:** a separate session. **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at `33dbb52bf`** — your scope is
FROZEN there; documents that change after it are FYI only. A later document that invalidates an item ⇒ **STOP that
item and report it** (do every item that is not blocked).

## 0. Before anything

1. Read `docs/blueprints/RULINGS.md`, then **[`DESIGN_Typed_Event_Nodes.md`](../DESIGN_Typed_Event_Nodes.md) end to end** —
   it is the design: inventory I1–I12 (`file:line`), class/sequence/module diagrams, slices E1–E6, decisions T-1..T-8.
   ✅ **All approved by the user, 2026-10-02 — build them as written.** A deviation is a finding: argue it in your report
   AND fold it back into that design (marking the prior text superseded).
2. **Branch:** start from `behaviors` at `33dbb52bf` (or later). Work on the branch you are given (suggest
   `behaviors-events`); push the empty marker `chore: started typed-event-nodes at <sha>` before code. The behaviours lane
   does not touch the event compile/emit path while you run (it holds S7 back — design T-8).
3. **Ids:** allocate tracker rows ONLY from **`CE-2010`–`CE-2029`** (reserved for you in the tracker header). Plain
   incrementing numbers, no letter suffixes. List every id you used in the report.

## 1. The user's words

> 🔒 *"having one single event ONLY in a graph is very restrictive, why can't we have any number of events handled by one
> graph, just by adding typed-event nodes? (node that is configured for certain type and fire its exec pin when event
> comes, another pin to get the event data)"* · *"yes, but measure and document it first"* · *"approved"*

## 2. Items — the design's slices, in order *(one commit each, green at each)*

| # | item | acceptance — a NEW rail per bullet, red-proved |
|---|---|---|
| **E1** | stop the two silent drops (design §2 I1, I4) — rails FIRST, red on today's code | a second event node in an Event graph is a diagnostic (new BP code; lifted by E2) · an Instance with two Event graphs on one event type runs BOTH (no overwrite) |
| **E2** | the split + payload on the node (T-1, T-2, T-3, T-5) | `EventEntryNode.Fields` baked like `PublishEventNode.PayloadFields`; a legacy single-entry graph migrates `Graph.Inputs` onto its node on load (zero golden movement for unchanged assets — prove it) · `HandlerSplit` before Stage 5, one handler per event node · two event nodes into ONE exec chain both work, and a Run Behaviour / When node in that shared tail keeps separate state per handler · each handler keeps its own policy (Parallel/Restart/Queue) |
| **E3** | whole-event pin (T-6) | an `Event` data-out typed as the event struct, split by `BreakStructNode`, beside the per-field pins |
| **E4** | editor subscribe UX (I8) | "On: X" palette entries per discovered event, pre-baked like "Publish: X" (`BlueprintEventPaletteEntries`) · node pins from its Fields · per-node Policy/Capacity editable in the details panel · several event nodes on one canvas. Closes `Custom_Events_BUILD_TRACKER` 4a/4b |
| **E5** | Q14-A2 (I9): system events (`BuiltInEngineEventCatalog`, 25) discoverable from the same source as `[BlueprintEvent]` | "On: HitEvent" exists in the palette; the When node keeps working from the same source |
| **E6** | debugger per handler (I11) | step-over enters the handler that fired; probes keep the authored node id |

⚠ T-7: event nodes in **Event graphs only**; Function / custom-event / Macro graphs keep their one empty entry (I10).
⚠ T-4 for Instances: ONE generated thunk per event type calls its handlers in authored order — the dispatch table
(`BlueprintEventDispatch`, keyed by FQN) does not change.

## 3. Invariants you must keep green *(the feature suites — run them FIRST, T-1)*

| suite | why |
|---|---|
| `Hrot.Blueprints.Tests` `FullyQualifiedName~BlueprintBehaviourTests` | behaviour events, fibers, policies — ⭐ **incl. the two record + replay rails** (`S6_AWaitingHandler_IsSavedToTheRecording…`, `S6b_Queue2_…`): the user's requirement is that blueprint state is saved to recordings and restored on replay |
| `…~CustomEventPubSubCapstoneTests`, `…~EventGraphEmitTests`, `…~WhenNode` | the Instance pub/sub path and the When node |
| `…~NodeCoverageTests`, `…~V_ResolverPurityTests` | a new node kind must be covered / classified |
| `Hrot.Blueprints.Tests` Golden + `BLUEPRINT_REGENERATE_SNAPSHOTS=1` | report golden movement as a DIFF SHAPE |

## 4. Gates — report every row *(the reviewing lane does not re-run them)*

| # | report |
|---|---|
| 1 | per gate: verbatim command · pass/fail/skip · delta vs baseline. Build the TEST project, then `--no-build` |
| 2 | Blueprints (full), Generators (`Hrot.AiEditor.Generators.Tests`), Editor (`Hrot.Editor.Tests`), Toolkits (`Fdp.Toolkits.Tests`) if a runtime file changed, SimHost (`Hrot.SimHost.Tests`) |
| 3 | golden movement as a diff shape ("N files, M lines, what changed") |
| 4 | every red confirmed PRE-EXISTING against `33dbb52bf`. Known pre-existing: SimHost `MapPresentationParityRails…EditorStrideSubsystem`, `NodeRolePersistenceRails.TheSaveHandlerSetIsStillComplete`, `FullBranchPipelineTests.BranchedRecording…` (and `LiveFromReplayTests.TeardownReplay…` flakes; passes in isolation) |
| 5 | working tree clean after every suite run |
| 6 | `python3 scripts/tracker-counts.py --check` · `design-digest.py --check` · `rulings-check.py` · `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs` on every touched design |
| 7 | every id allocated (from `CE-2010`–`CE-2029`) |
| 8 | each new rail's red-proof: what you broke, that it went red, that you restored it |

## 5. Deliverables

- code + rails per item, committed and pushed on your branch;
- the design's as-built folded into `DESIGN_Typed_Event_Nodes.md` (and `Custom_Events_Design.md` §4.5 / its build tracker
  4a/4b/1f when E4/E5 land);
- tracker rows for each item; `CE-2009` closed when E1–E6 are done;
- a report `docs/blueprints/batches/REPORT_Typed_Event_Nodes.md` with the gate table (§4) and any deviations.
