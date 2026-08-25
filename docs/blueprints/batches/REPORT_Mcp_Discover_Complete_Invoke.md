<!--STATUS
state: LIVE
doc-type: batch report (ephemeral — the durable record is the DESIGN)
updated: 2026-08-25
current-answer: §1 the DECISION LOG and §2 the PROGRESS LOG are written LIVE during the run and committed
  periodically, per the autonomy protocol — if a turn is cut off, what is committed here IS the delivery.
  The as-built lives in docs/DESIGN_Mcp_Authoring.md (obligation ⑤); this report POINTS there.
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
| — | §3.4 usage-doc harvest | ⏳ |
| — | §3.6 RouteDocs + handlers + catalog | ⏳ |
| — | §5 the coverage rails | ⏳ |

## 3. GATES *(rule 8 contract)*

*(filled in as they are run.)*
