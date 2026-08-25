<!--STATUS
state: LIVE
doc-type: batch report (ephemeral — the durable record is the DESIGN)
updated: 2026-08-25
current-answer: the whole file. ⛔ No design content: the as-built lives in
  docs/DESIGN_Mcp_Authoring.md §10 (obligation ⑤); this report POINTS there.
-->
# REPORT — **the MCP authoring surface** *(AQ56 — AI-asset editing + scenario authoring)*

> 📌 **Dispatch `8cf450cec`** · started-marker `393612b60` · **ids `MA-001`…`MA-010`** *(rule 5; new tracker
> **Area M**)*.
> 📄 **The design is the record:** [`DESIGN_Mcp_Authoring.md`](../../DESIGN_Mcp_Authoring.md) —
> **§10 is new and is the AS-BUILT**; §5/§6's diagrams are REDRAWN; the dispatched class diagram is §11
> HISTORY. `AQ56` is marked **BUILT**.

## 1. ⭐⭐⭐ THE RESULT

⭐ **The MCP server authors.** Eight routes — read the graph by its in-memory guids, discover the node
kinds, add nodes and links, set pin defaults, remove, create an asset, delete an entity — all in a new
`DebugApiService.Authoring.cs`, all reaching the **same command sink, validator and undo stack the canvas
uses** *(`Q56-A`: "MCP edits ARE human edits over the wire")*.

📐 `gen:catalog` **74 → 82 tools** · `test-catalog` **657 / 0** · the four new conformance rails **5 / 5**
*(the filter also picks up one pre-existing rail)* · revert probes **3 / 3 red**, plus a fourth that
**stayed green and exposed a hole in my own rail** — §4.

## 2. 🔴🔴 THE DEFECT THIS BATCH FOUND — **CGF's save was a silent no-op after any edit** *(`MA-003`)*

📐 **Measured.** `SaveAllAiDocumentsCommand.Execute` skips a document whose `IsDirty` is false, and
`AiDocument.MarkDirty` had **exactly ONE production caller in the repo**: the EDITOR's `DocumentOpened`
factory *(`EditorSubsystem:4016`)*, which subscribes `doc.Asset.Changed` — ⛔ **and only when a
regeneration scheduler exists.** ⚠⚠ **CGF's factory never subscribed at all** *(`CE-015` built the
factories; the dirty subscription was not among them)*.

⇒ ⛔ **CGF could edit a graph and then write NOTHING while reporting success.** `CE-020`'s save was
reachable and inert — the exact shape slice 3 could not have caught, because MCP could not author an edit
to make a document dirty in the first place.

⭐ Fixed by adding the subscription, trimmed to what CGF has *(no scheduler — it regenerates nothing)*.
📌 **The silent-default pattern verbatim: the editor HAD the wiring, CGF did not, and nothing compared
them.**

## 3. ⭐ OBLIGATION ③ — the diagrams vs what was built

> §5 carried **9 classes**, §6 **1 sequence**. ⭐ **The direction of every dependency was right.**
> ⚠ **Four deviations, each argued and each folded into the design** *(obligation ⑤ — §10.1–§10.5, and
> §5/§6 are REDRAWN so the diagram is TRUE again rather than merely annotated)*.

| # | the design said | measured | what shipped |
|---|---|---|---|
| ⭐⭐⭐ **1** | §3: *"closer analog to reuse: `BlueprintClipboard`"* | ⛔ the clipboard round-trips Blueprint **ASSET** nodes — its own header says NodeEdit *"knows nothing about"* them — and **carries no `PinId` at all** | ⭐⭐ `InMemoryGraphSerializer` projects **`IGraphModel`**, whose `NodeId`/`PinId`/`LinkId` ARE what `GraphCommand` takes ⇒ one id space **by construction**. ⭐⭐⭐ And **host-agnostic: one serializer, not three**. ⚠ §3's RULE was right and is untouched; only its named analog was wrong |
| ⭐⭐ **2** | §5: `DebugApiAuthoring ..> ICommandSink : Apply` | the seam is `IGraphCommandSink`, and applying to it **skips the undo stack** | ⭐ every edit goes through **`GraphView.Execute(fwd, inv, label)`** ⇒ an MCP edit is undoable exactly like a human one |
| ⭐⭐ **3** | §5: `CommandBuilder` covers `SetNodeProperty` / `RemoveNodes` | ⛔ **it covers neither**, and `INodeModel` exposes no property bag ⇒ no inverse could be built | **params → a PIN DEFAULT** *(`SetPinDefault`, the one edit whose inverse the model produces)*; **remove → `editor.delete-selection`**, which already handles implicit incident links, reroutes, attachments and a **reversed** inverse |
| ⭐⭐ **4** | §7 lists 6 items | ⛔ **a 7th was necessary**: `add_graph_node` takes a kind STRING and the sink builds nothing for an unknown one *while reporting success* | **`GET /assets/{id}/graph/catalog`** + a **model re-read** inside add-node. 📌 Without them a wrong guess is a silent no-op — the `CE-009` §4c shape |

⚠ **And one item's premise was already true:** §7 ④ asked to extend `/entities/*` for *place / configure /
assign*; 📐 all three already had routes *(`spawn` · `attribute`+`component` · `attach-blueprint`)* and
**delete had none** ⇒ `DELETE /entities/{networkId}` is the whole of item ④ *(§10.5)*.

## 4. ⭐⭐ THE RAILS, AND THE PROBE THAT CAUGHT MY OWN RAIL

📐 **Revert probes — one per claim, each rebuilt against `Hrot.ClusterRunner` and re-run:**

| probe | what was disabled | result |
|---|---|---|
| ⭐⭐⭐ **A** | the `ILinkValidator` pre-check in `add_graph_link` | 🔴🔴 **STAYED GREEN — the rail was under-specified.** 📐 `BlueprintCommandSink` refuses a self-link too, so *"it was refused"* could not tell the validator from the sink. ⭐ **The rail was tightened** to assert the validator arm's own prefix *(`"The editor refuses"`)* vs the sink arm's *(`"The host sink refused the link"`)* — and then reddened correctly |
| ✅ **B** | the per-edit `MarkEdited` guard | **stayed green, and that is the finding** — on the EDITOR the `Asset.Changed` subscription already marks the document. ⇒ the guard is **redundant on a correctly-wired host, which is what it defends against**. ⛔ Not rail-coverable; filed `MA-009` rather than deleted |
| ✅ **C** | the all-or-nothing id check in `remove` | 🔴 red — *"a remove naming one id that is NOT in the graph was accepted"* |
| ✅ **D** | the unknown-entity guard in `delete_entity` | 🔴 red — *"deleting an entity that does not exist was accepted"* |

| rail | what it pins |
|---|---|
| ⭐⭐⭐ `An_agent_can_read_and_edit_a_graph_over_mcp` | the **round trip**: every id received is spent on a RE-READ ⛔ — a rail asserting only *"add-node answered 200"* would pass against a route that mutates nothing. Plus the validator's own refusal, **by prefix** *(probe A)*, and a reload **after removing what was added**, which asserts the edit path leaves a graph the compiler still accepts |
| `An_agent_can_remove_what_it_added_over_mcp` | that the removal reaches the editor's Delete command, and the **all-or-nothing** refusal |
| ⭐⭐ `An_agent_can_create_edit_save_and_reload_its_own_asset` | the design's **whole sequence** — create → appears in `GET /assets` → edit → save → reload — on an asset the rail owns and deletes |
| `An_agent_can_delete_an_entity_and_the_world_loses_it` | `Q56-C`'s world-manipulation delete: refused for an unknown id, queued for a real one, gone after 5 ticks, and `scenario/save` still snapshots |

### ⛔⛔ 4b. A RULE THE RAILS LEARNED THE HARD WAY *(`MA-010`)*

🔴 **The first cut saved a COMMITTED asset, and `git status` came back with 372 deleted lines in
`ComponentCollectionDemo.bp.json`.** ⚠ **Not a defect this batch introduced:** `SaveActiveBlueprintCommand`
strips the projected pins and rewrites link endpoints to deterministic name-derived ids *(design §3;
`AQ10` rehydration reverses it on load)*, so the fatter committed file is an older shape. ⇒ **any save of
any blueprint through the editor dirties the tree**; slice 3's rail never noticed because it saved a
**clean** document, which is a no-op.

⭐ **The file was restored and the rails split**: the EDIT half never saves; the SAVE half runs on an asset
the rail creates in a sentinel folder it deletes in a `finally`. 📐 **Verified: `git status` after the
final green run shows only this batch's own files.** ⚠ *"The committed assets are not in the shape the
editor writes"* is filed as `MA-010` — a real question for whoever owns asset persistence, ⛔ out of scope
here.

### ⚠ 4c. AND A LESSON ABOUT PROBING A T3 RAIL

📐 Probe A's first run passed because I rebuilt **`Hrot.SystemTests` only**. ⛔ **A conformance rail
launches `Hrot.ClusterRunner`** — probing the editor's behaviour requires rebuilding *that* project, or
the probe silently measures the old binary and reads as *"the guard is unnecessary."*

## 5. GATES *(rule 8 contract)*

> 📌 **Base for every pre-existing claim: the started-marker `393612b60`** *(dispatch `8cf450cec`)*.
> ⭐ **Built ONCE per project, then `--no-build`.** ⛔ No full-solution build at any point.

| # | gate | verbatim command | `--no-build`? | result | Δ vs base |
|---|---|---|---|---|---|
| 1 | **affected-project builds** | `dotnet build {Hrot.Editor,Hrot.CGF,Hrot.ClusterRunner,Hrot.SystemTests}.csproj --no-restore -v q -nologo` | ⛔ builds *(once each)* | ✅ **0 errors**; 10–15 s each | — |
| 2 | **the editor unit suite** *(carries `EveryRouteIsDocumentedTests` + `CapabilityManifestRails` — the two gates these 8 routes had to satisfy)* | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj --no-build -v q --nologo` | ✅ | ⚠ **247 / 1 / 1 skipped**, 3 runs of 3 — the single red is row 5 | **none** |
| 3 | **the AiShared unit suite** | `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj --no-build -v q --nologo` | ✅ | ✅ **2016 / 0 / 1 skipped** — ⭐ **unchanged, and it should be: `Hrot.Editor.AiShared` was NOT touched** *(handoff §4)* | **none** |
| 4 | ⭐⭐⭐ **the INTEGRATION suite** *(rule 8 row 8)* — `ClusterConformanceRails` is the only thing that can prove a DebugApi change did not break the cross-host contract | `scripts/run-system-tests.sh --no-build` *(**T3**, run in the BACKGROUND — ⛔ never a foreground blocker)* | ✅ | **§5b** | **+4 rails** |
| 4b | **the four new rails, filtered** | `scripts/run-system-tests.sh --no-build An_agent_can` | ✅ | ✅ **5 / 0**, 21 s *(the filter also matches one pre-existing rail)* | **+4** |
| 5 | ⚠ **the one RED, already A/B'd this session** | `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected` | ✅ | ⛔ **PRE-EXISTING GC/ALC timing flake.** 📐 A/B'd earlier today at the slice-3 base `03c65240f`: **3 red of 6 runs** on the BASE binary *(`git stash -u`, rebuilt)*. Green **16 / 16 in isolation** here. ⭐ Already recorded under `ST-035` and in three prior batch reports | **none** |
| 6 | ⭐⭐ **golden movement** | — | — | ⭐ **ZERO. No file under `Goldens/` is added, removed or modified.** These rails assert BEHAVIOUR over MCP, not serialized snapshots | **none** |
| 7 | 🔴 **tree CLEAN after every suite run** | `git status --short` | — | ✅ **only this batch's 13 modified + 2 new files.** ⚠⚠ **This row is not a formality here — see §4b: an earlier cut FAILED it, mutilating a committed asset. The file was restored and the rails redesigned** | — |
| 8 | **quarantine / skips** | — | — | ⭐ **1 skip in each unit suite, both PRE-EXISTING. This batch adds no skip and quarantines nothing** | **none** |
| 9 | **the MCP catalog is GENERATED** | `npm run gen:catalog` · `npm run gen:skill` | — | ✅ **74 → 82 tools** from 82 endpoints; `SKILL.md` regenerated *(470 lines)*. ⚠ **`node`/`npm` live at `/opt/node22/bin`, OFF `PATH`** | **+8 tools** |
| 10 | **every catalogued tool has a handler** | `node test-catalog.mjs` | — | ✅ **657 / 0** | **+64 assertions** |
| 11 | ⭐⭐ **the revert probes** | 4 probes, each rebuilding `Hrot.ClusterRunner` then re-running its rail | ✅ | ✅ **3 red as intended; the 4th stayed green and improved the rail** — §4 | — |
| 12 | **tracker** | `python3 scripts/tracker-counts.py --check` | — | ✅ `open 102 / done 346 (+1 refuted)` — ⭐ unchanged: `MA-` rows carry no `BP-` id, by design | — |
| 13 | **the ledger** | `python3 scripts/rulings-check.py` | — | ✅ **25 / 25.** ⚠ 1 staleness WARN on `CapabilityManifest.cs`, **investigated**: it is my own slice-2 commit `f194cd088`. ⭐⭐ **`R-133` is not merely intact — this batch is its strongest evidence yet: 8 new routes needed ZERO manifest edits**, because classification is prefix-derived and `/assets*` is already `EditorAuthoring` | — |
| 14 | **design-doc format + UML** | `python3 scripts/design-digest.py --check` | — | ✅ **82 documents**: STATUS headers, INVENTORY blocks, and **every buildable design carries a class AND a sequence diagram** | — |
| 15 | **mermaid parses** | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs docs/DESIGN_Mcp_Authoring.md` | — | ✅ **3 / 3** *(the redrawn classDiagram, the redrawn sequenceDiagram, and the HISTORY one)* | — |

### 5b. ⏳ The full T3 suite

*(filled in from the background run before the batch closes — §5 row 4.)*

## 6. ⭐ IDS ALLOCATED *(rule 5)*

**`MA-001`…`MA-010`**, tracker **Area M** *(new — the handoff asked for a fresh prefix and area)*.
✅ `MA-001` the graph read · `MA-002` the four edit routes · `MA-003` **the CGF dirty-save defect** ·
`MA-004` the node-kind catalog + model re-read · `MA-005` create-asset *(the dialog body EXTRACTED, not
duplicated)* · `MA-006` `DELETE /entities/{id}` · `MA-007` link validation · `MA-008` the MCP surface.
⚠ Open: **`MA-009`** *(the `MarkEdited` guard is not rail-coverable — probe B)* · **`MA-010`** *(committed
assets differ in shape from what the editor writes)*.

## 7. ⛔ WHAT THIS BATCH DID **NOT** DO

| | |
|---|---|
| ⛔ **`create_asset` on CGF** | 📐 the path needs the per-kind `INewAssetService` registry, the Blueprint source-root override *(`BUG-A6`)* and the per-contributor `Refresh`; **CGF composes none**. ⇒ it answers **503** saying so, and pointing out that EDITING an existing asset needs none of it. ⚠ Declared in design §10.7 — ⭐ closing it is a `CE-` item, not an `MA-` one |
| ⛔ **the `IAssetValidator` set** *(design item ⑤'s other half)* | those validate a WHOLE asset at save/authoring-window time; this surface validates a single edit, where `ILinkValidator` is the right level. ⚠ Stated, not silently skipped |
| ⛔ **node PROPERTIES as a free-form key/value edit** | `CommandBuilder` has no `SetNodeProperty` and `INodeModel` no property bag ⇒ **no inverse could be built**, so it would be the one un-undoable edit in the set. ⭐ Pin defaults cover the authoring case that matters |
| ⛔ **changing `DELETE /entities/{id}`'s capability class** | it classifies `WorldRead` like every other `/entities/*` WRITE *(`spawn`, `command`, `attribute`)*. ⚠ Special-casing delete would make the table inconsistent — flagged for whoever revisits write-capability granularity |
| ⛔ **`DebugApiService.Assets.cs`** | ⭐⭐ **the collision boundary HELD from this side**: the 8 routes went in a NEW `DebugApiService.Authoring.cs`, which CALLS `Assets.cs`'s private `ResolveOpenDocument` *(one partial class)* and re-implements none of it. ⛔ Not one line of that file changed |
| ⛔ **`Hrot.Editor.AiShared`** | untouched, additively or otherwise — 2016 / 0 unchanged proves it |
