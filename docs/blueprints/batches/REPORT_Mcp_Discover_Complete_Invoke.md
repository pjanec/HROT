<!--STATUS
state: LIVE
doc-type: batch report (ephemeral — the durable record is the DESIGN)
updated: 2026-08-25
current-answer: §1 the DECISION LOG and §2 the PROGRESS LOG are written LIVE during the run and committed
  periodically, per the autonomy protocol — if a turn is cut off, what is committed here IS the delivery.
  The as-built lives in docs/DESIGN_Mcp_Authoring.md §15 (obligation ⑤); this report POINTS there.
-->
# REPORT — **MCP: discover + complete + invoke** *(autonomous overnight run)*

> 📌 **Dispatch `0356ab680`** · started-marker `9163a60d6` · ids continue **`MA-011`…** *(tracker Area M)*.
> 📄 **The design is the record:** [`DESIGN_Mcp_Authoring.md`](../../DESIGN_Mcp_Authoring.md) —
> §10 *(discovery)* · §10.6 *(harvested docs)* · §10.7 *(UI commands)* · §11 *(completeness)* ·
> §12 *(the shipped `MA-001`…`010` surface this EXTENDS)*.

## 1. ⭐⭐⭐ DECISION LOG *(the autonomy protocol — decide, log, continue)*

| # | the ambiguity | ⭐ the decision, and why |
|---|---|---|
| **D1** | 🔴 **`GET /commands` is ALREADY TAKEN.** §10.7 proposes it for the editor command bus; 📐 measured — it exists since Group F as `list_commands`, *"enumerate publishable FDP event types with field schemas"*, and `send_entity_command` depends on it | ⭐⭐ **Use `/editor/commands`** *(`GET` · `GET /{id}` · `POST /{id}/invoke`)*. ⛔ Shadowing an existing route would break `send_entity_command`'s discovery, and renaming the FDP one is a breaking change outside this scope. ⭐ The prefix is also HONEST: this is the **editor** command bus, ⛔ not the FDP event bus. ⚠ A new prefix means `CapabilityManifest` must classify it — which is the designed inversion *(an unclassified prefix REDDENS `CapabilityManifestRails`)*, ⛔ not a hand-authored availability cell |
| **D2** | ⛔ §10.2 ① proposes a parallel **`GET /assets/{id}/nodetypes`** | ⭐ **Not built.** The handoff §1 already overrides it *("§10 discovery EXTENDS the shipped catalog route… ⛔ never a parallel `/nodetypes`")*, and that override came from the `MA-` batch's own report §8. ⇒ the shipped **`GET /assets/{id}/graph/catalog`** is EXTENDED, and one-kind schema hangs off it as `…/graph/catalog/{kind}` |
| **D3** | ⚠ The handoff §0 says *"subagents are your instrument"* | ⭐ **Not used.** This session's operating instructions forbid spawning agents unless the user asks directly, and the user asked me to follow the handoff — ⛔ which is not the same as asking for subagents. ⚠ The doc harvest is done in-session instead; it costs wall-clock, ⛔ not coverage, so the deliverable is unchanged |

## 2. ⭐⭐ PROGRESS LOG

| when | item | state |
|---|---|---|
| start | rule 7 re-sync + rule 1b started-marker `9163a60d6` | ✅ |
| 1 | **§3.2 serializer completeness** — attachments *(per node AND asset-level)* · containers/regions + per-child region index · reroute waypoints · link style · collapsed/advanced flags | ✅ `MA-011` |
| 2 | **§3.1 the union backbone** — `GraphCommandJson` reads **35 variants**; `POST …/graph/command` applies through `GraphView.Execute`; `GET …/graph/command` is self-describing | ✅ `MA-012` |
| 3 | **§3.3 discovery** — the shipped catalog route EXTENDED *(one shared `DescribeKind` projection)* · `GET …/graph/catalog/{kind}` · `GET …/graph/nodes/{guid}/properties` | ✅ `MA-013` `MA-014` |
| 4 | **§3.5 UI-command actions** — `GET /editor/commands` · `…/{id}` · `POST …/{id}/invoke` | ✅ `MA-015` |
| — | **`EditDocAttribute`** added to the StructEdit `Edit*` family — the free-text half §10.6 measured as missing | ✅ `MA-016` |
| 5 | **§3.4 usage-doc harvest** — the StructEdit `Edit*` family read off each DTO field *(display name · range · unit · read-only · buffer shape · enum values)* + the new `EditDoc` prose | ✅ `MA-016` |
| 6 | **§3.6 the MCP surface** — 7 `RouteDoc`s + 7 handlers; `CapabilityManifest` classifies `/editor`; **82 → 89 tools**, `test-catalog` **713 / 0** | ✅ |
| 7 | 🔴 **a DEFECT the rails found**: a sink can ACCEPT a variant and build nothing ⇒ the union route now verifies every MINTED id resolves before reporting success | ✅ `MA-017` |
| 8 | **§5 the coverage rails** — 1 unit *(union completeness, probed red)* + 3 T3 *(union round-trip per host · schema coverage · the command bus)* | ✅ `MA-018` |
| end | as-built folded into the design **§15**; tracker Area M extended `MA-011`…`MA-018` | ✅ |

## 3. ⭐⭐ WHAT THE RAILS FOUND — **two defects, both in this batch's own work**

> ⭐⭐⭐ **Neither would have been visible without running the rails against a real host**, and that is the
> point of the handoff's §5 finish line: *"the rails are green" is done; "I ran out of ideas" is not.*

| # | found by | the defect | the fix |
|---|---|---|---|
| 🔴🔴 **1** | the union rail's FIRST T3 run | **`AddAttachment` on a Blueprint returns `Success` and builds NOTHING.** 📐 Attachments are a BTree/HSM concept *(decorators, condition pills)* and `BlueprintCommandSink` has no arm for them ⇒ ⛔ the route handed back an `attachmentId` addressing nothing | ⭐⭐ **`MA-017`: every id a command MINTS must resolve in the model before the route reports success** — `MA-004`'s add-node lesson generalised to the whole union. A host that cannot serve a variant now says so, naming it |
| ⚠ **2** | the union rail's SECOND run | **A BTree needs `hostProperties.paletteKind`** to know WHICH decorator to construct *(what `PaletteEntryExecutor` passes when a human picks one)*, and **`BTreeCommandSink` refuses `AddComment` outright** | ⭐ the rail drives each variant on a host that OWNS it, builds the attachment from a **catalog entry** *(`paletteAction == AttachToSelected`)* rather than from nothing, and LOGS every refusal instead of skipping it |

⚠ **And one weakness in a rail I wrote**, caught by its own probe *(carried over from the previous
batch's habit)*: the union rail's first floor assertion would have passed on a host that refused
everything, because it only counted attempts. ⭐ It now asserts that the union path reached **something no
typed verb could**, and prints the refusals when it cannot.

## 4. GATES *(rule 8 contract)*

> 📌 **Base: the started-marker `9163a60d6`** *(dispatch `0356ab680`)*.
> ⭐ **Built ONCE per project, then `--no-build`.** ⛔ No full-solution build at any point.
> ⚠ **A T3 probe must rebuild `Hrot.ClusterRunner`** — the rails launch that binary, so rebuilding only
> `Hrot.SystemTests` measures the OLD editor and reads as *"the guard is unnecessary."*

| # | gate | verbatim command | `--no-build`? | result | Δ vs base |
|---|---|---|---|---|---|
| 1 | **affected-project builds** | `dotnet build {StructEdit.Core,Hrot.Editor,Hrot.ClusterRunner,Hrot.SystemTests,*.Tests}.csproj --no-restore -v q -nologo` | ⛔ builds *(once each)* | ✅ **0 errors**, 9–16 s each | — |
| 2 | ⭐⭐⭐ **the UNION-COVERAGE unit rail** — the control that makes the fix permanent | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests --no-build --filter TheCommandRouteCoversTheWholeUnion` | ✅ | ✅ **3 / 3**, **~5 ms**. 📐 Reflection finds **35** variants; all 35 reachable, no phantom rows, no field-less variant | **+3** |
| 3 | **the editor unit suite** *(carries `EveryRouteIsDocumentedTests` + `CapabilityManifestRails` — the two gates the 7 new routes and the new `/editor` prefix had to satisfy)* | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj --no-build -v q --nologo` | ✅ | ⚠ **250 / 1 / 1 skipped** — the one red is row 6 | **+3 tests** |
| 4 | **the AiShared unit suite** | `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj --no-build -v q --nologo` | ✅ | ✅ **2016 / 0 / 1 skipped** — ⭐ **unchanged, and it must be: `Hrot.Editor.AiShared` was NOT touched** *(handoff §4's freeze)* | **none** |
| 5 | ⭐⭐ **the three T3 coverage rails** | `dotnet test Hrot/Runner/Hrot.SystemTests --no-build --filter "…union…|…node_kind…|…command_bus…"` | ✅ | ✅ **3 / 3** *(union round-trip per host · schema coverage over every catalogued kind · the command bus incl. the 409 parity assertion)* | **+3 rails** |
| 6 | ⚠ **the one RED, A/B'd earlier this session** | `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected` | ✅ | ⛔ **PRE-EXISTING GC/ALC timing flake.** 📐 A/B'd today at an earlier base: **3 red of 6 runs on the BASE binary**. Recorded under `ST-035` and in four prior batch reports. ⚠ It passed on one run of this batch and failed on another — ⭐ neither colour is evidence | **none** |
| 7 | ⭐⭐⭐ **the INTEGRATION suite** *(rule 8 row 8)* — 7 routes on a shared route table + a composition-root change ⇒ `ClusterConformanceRails` is the only thing that can show the cross-host contract still holds | `scripts/run-system-tests.sh --no-build` *(**T3**, BACKGROUNDED — ⛔ never a foreground blocker)* | ✅ | **§4b** | **+3 rails** |
| 8 | ⭐⭐ **the revert probe** | delete the `AddRegion` row from `GraphCommandJson.Schema`, rebuild, re-run the unit rail | ✅ | ✅ 🔴 **red, naming it**: *"1 GraphCommand variant(s) are unreachable over MCP and undeclared: AddRegion"* | — |
| 9 | **golden movement** | — | — | ⭐ **ZERO** — no file under `Goldens/` added, removed or modified. These rails assert behaviour over MCP, not snapshots | **none** |
| 10 | 🔴 **tree CLEAN after every suite run** | `git status --short --untracked-files=all` | — | ✅ **only this batch's own files.** ⚠ Not a formality: the previous batch's §4b cost a committed asset 372 deleted lines, and **every rail here edits IN MEMORY and never saves** | — |
| 11 | **quarantine / skips** | — | — | ⭐ **1 skip in each unit suite, both PRE-EXISTING. This batch adds no skip and quarantines nothing** | **none** |
| 12 | **the MCP catalog is GENERATED** | `npm run gen:catalog` · `npm run gen:skill` | — | ✅ **82 → 89 tools** from 89 endpoints; `SKILL.md` regenerated *(493 lines)*. ⚠ `node`/`npm` at `/opt/node22/bin`, OFF `PATH` | **+7 tools** |
| 13 | **every catalogued tool has a handler** | `node test-catalog.mjs` | — | ✅ **713 / 0** | **+56 assertions** |
| 14 | **tracker** | `python3 scripts/tracker-counts.py --check` | — | ✅ `open 102 / done 346 (+1 refuted)` — ⭐ unchanged: `MA-` rows carry no `BP-` id, by design | — |
| 15 | **the ledger** | `python3 scripts/rulings-check.py` | — | ✅ **25 / 25.** ⭐⭐ **`R-133` holds and this batch strengthens it again:** the new `/editor` prefix required a `CapabilityManifest` line, which is the **designed inversion** *(an unclassified prefix REDDENS `CapabilityManifestRails`)* — ⛔ not a hand-authored availability cell | — |
| 16 | **design-doc format + UML** | `python3 scripts/design-digest.py --check` | — | ✅ **86 documents**: STATUS headers, INVENTORY blocks, class + sequence diagrams on every buildable design | — |
| 17 | **mermaid parses** | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs docs/DESIGN_Mcp_Authoring.md` | — | ✅ **7 / 7** | — |

### 4b. ⏳ The full T3 suite

*(filled in from the background run before the batch closes — §4 row 7.)*

## 5. ⭐ IDS ALLOCATED *(rule 5)*

**`MA-011`…`MA-018`**, tracker **Area M** *(continuing the series, as the handoff asked)*.
✅ `MA-011` read completeness · `MA-012` the union backbone · `MA-013` discovery extension ·
`MA-014` node properties · `MA-015` the editor command bus · `MA-016` the doc harvest + `EditDoc` ·
`MA-017` **the accept-and-build-nothing defect** · `MA-018` the coverage rails.
⛔ **No item was stopped.** The two design premises that turned out false *(`/commands` was taken;
`/nodetypes` would duplicate)* were decided and logged rather than escalated — §1.

## 6. ⛔ WHAT THIS BATCH DID **NOT** DO

| | |
|---|---|
| ⛔ **`GlobalActionRegistry`** | §10.7 ruled it out and the measurement agrees: int-keyed, no descriptor, no display name. ⭐ It needs an author-a-descriptor pass and belongs with the entity-action / Axis-B vocabulary |
| ⛔ **`Hrot.Editor.AiShared` internals** | untouched — 2016 / 0 unchanged proves it *(handoff §4's freeze)* |
| ⛔ **`CgfSubsystem.cs` and the CGF time/debug files** | untouched — a parallel CGF session owns them *(handoff §4)*. ⚠ CGF therefore wires **no** schema exporter, so its `paramsSource` reports `none:no-exporter-wired`; ⭐ a one-line follow-up in that lane |
| ⛔ **NodeEdit itself** | the union reader is hand-written precisely BECAUSE `[JsonPolymorphic]` cannot be added to a vendored tree this lane does not own |
| ⚠ **100% prose coverage on node kinds** | `EditDoc` makes it POSSIBLE; filling it in across the catalog is a sweep, not this batch. ⭐ The rail prints the percentage so it can be ratcheted rather than assumed |

