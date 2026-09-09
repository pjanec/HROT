<!--STATUS
state: LIVE
build-state: DISPATCH — AUTONOMOUS OVERNIGHT RUN. Bundled MCP slice: the whole GraphCommand union backbone
  + read/discover completeness + harvested usage docs + UI-command actions. Extends the MERGED MA- surface.
updated: 2026-08-25
current-answer: this is a POINTER + the autonomy protocol. The design is the source:
  DESIGN_Mcp_Authoring.md §10 (discovery), §10.6 (harvested docs), §10.7 (UI commands), §11 (completeness),
  §12 (the AS-BUILT MA- surface you EXTEND). ⛔ Build §10/§11's shape; §12 is what already exists.
known-conflict: none live. The prior MCP-authoring session is DONE and merged; you own the DebugApi authoring
  surface + the generated catalog exclusively. ⛔ Do NOT touch Hrot.Editor.AiShared (variable-model freeze)
  beyond additive; ⛔ do NOT touch CgfSubsystem/the CGF time+debug files (a parallel CGF session may be there).
-->
# HANDOFF — **MCP: discover + complete + invoke** *(AUTONOMOUS overnight run — full MCP authoring capability)*

> 📌 **Dispatched at `<DISPATCH_SHA>`** *(the coordinator HEAD you branch from — read it from `git rev-parse HEAD` after the ff-merge)*.
> ⭐ **Branch FRESH from `claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: push an empty
> `chore: started mcp discover+complete+invoke at <sha>` marker BEFORE any code.** ⛔ **No PR.**
> ⭐ **You allocate the ids** *(rule 3)* — continue the **`MA-`** series *(Area M)* from `MA-011`; state every id *(rule 5)*.

## 0. ⛔⛔⛔ THE AUTONOMY PROTOCOL — **this run does NOT stop on unknowns** *(user, `2026-08-25`)*
> 🎯 **User:** *"a long autonomous overnight run that does not stop on unknowns and will analyze and implement
> on its own until done… this is mostly about controlling existing stuff via MCP so there should not be big
> risks that are not solvable autonomously. In the morning, deliver full MCP capability."*

| ⭐ rule | what it means |
|---|---|
| ⭐⭐⭐ **DECIDE, don't block** | On ANY ambiguity, pick the option most consistent with the design + the shipped MA- patterns *(§12)*, **write one line in the DECISION LOG** *(a running section of your report)*, and continue. ⛔ Never end a turn waiting for an answer |
| ⭐⭐ **STOP the ITEM, never the BATCH** *(R-106)* | The only thing that halts an item is a design premise MEASURED false with real blast radius. Record it, skip THAT item, do every other item. A blocked item cascades only through a genuine dependency, named in the log |
| ⭐⭐ **subagents are your instrument** | Fan out mechanical/parallel work to subagents *(the doc harvest §3.4; per-host serializer arms; the coverage-rail matrices)*. ⛔ Don't serialize what can parallelise |
| ⭐ **log as you go** | Keep a **DECISION LOG** and a **PROGRESS LOG** live in the report file, committing periodically — so the morning shows exactly what was decided and where it got to, even if a turn is cut off |
| ⭐⭐ **DONE is measurable** | §5's coverage rails are green + the report is written + the design is folded to as-built *(obligation ⑤)* + the gap/tracker updated. ⛔ "I ran out of ideas" is not done; "the rails are green" is |
| ⚠ **the risk profile is LOW by design** | Additive DebugApi routes + serialization + reading registries + additive `[Doc]` attributes + rails. ⛔ You touch NO kernel, NO variable-model internals, NO deletions. The one genuinely-hard part is polymorphic `GraphCommand` JSON — bounded, and solvable *(§3.1)* |

## 1. ⛔ THE DESIGN IS THE SOURCE — read it first
📄 **[`DESIGN_Mcp_Authoring.md`](../../DESIGN_Mcp_Authoring.md)** — **§11** *(completeness — the whole union, APPROVED)* ·
**§10** *(discovery)* · **§10.6** *(harvested docs)* · **§10.7** *(UI commands)* · **§12** *(the AS-BUILT MA- surface you extend)*.
⭐ §11's classDiagram + §10.3/§10.4/§10.7 diagrams are what you build. 📄 Decisions: `Architect_Question_56` *(resolved)*.

⛔⛔ **RULING 9 — EXTEND, do not duplicate.** MA- already shipped **`GET /assets/{id}/graph/catalog`** *(node-kind list, MA-004)*
and typed verbs *(`add_graph_node/link`, `set_graph_param`→pin default, `remove`)*. ⭐ **§10 discovery EXTENDS the
shipped catalog route; §11 WIDENS the typed verbs into the one generic union route.** ⛔ Never a parallel `/nodetypes`
or a second graph-mutation model. Read `DebugApiService.Authoring.cs` + `InMemoryGraphSerializer.cs` before adding.

## 2. ⛔ NEW BUILD/TEST RULES APPLY
`.claude/CLAUDE.md` → THREE TEST TIERS → the `2026-08-24` rule. **Build the AFFECTED PROJECT** *(`Hrot.Editor` ·
`Hrot.SystemTests` · `Hrot.ClusterRunner`)*, ⛔ **NEVER the whole solution in the fix loop** *(115 s vs 8 s)*. Build ONCE,
then `--no-build`. E2E/system suite is **T3 — background it, never a foreground blocker.** Pre-existing reds proven by
`git diff` against the started-marker, not by rebuild.

## 3. ⭐⭐⭐ WHAT TO BUILD *(five workstreams — parallelise across subagents where marked)*

### 3.1 ⭐⭐⭐ The union backbone — `POST /assets/{id}/graph/command` *(§11.2)*
One route carries **one serialized `GraphCommand`** *(a `type` tag + the variant's fields; `NodeId`/`PinId`/`AttachmentId`/
`LinkId` as strings; enums as names; `object?` param values as JSON)* → deserialize → apply through **`GraphView.Execute(fwd,
inv, label)`** *(the undo stack — §12.2, NOT the sink directly)* → return the `GraphCommandResult` + any new ids.
- ⭐ **`Batch(label, commands[])` = atomic multi-step** for free.
- ⭐ **Keep the shipped typed verbs as sugar** — they build the union command; ⛔ don't fork the model.
- ⭐⭐ **Cover the whole union**, especially the host specifics the 4 verbs cannot express: **`AddAttachment`/`Remove/Set/
  Reorder/MoveAttachment`** *(BTree decorators + pills)* · **`AddRegion`/`RemoveRegion`/`ReorderRegions`/`SetRegionProperty`/
  `ChangeParent(Multiple)`** *(HSM parallel regions + reparent)* · `SetPinDefault` · comments · reroutes · the refactor ops.
- ⚠ **Tier for the rail:** *semantic* variants must round-trip AND survive save→reload; *cosmetic* variants *(collapsed/
  advanced/move/reroute/comment-color)* need only round-trip in the read. A variant a host genuinely cannot accept is a
  LOGGED finding, not a block.

### 3.2 ⭐⭐ Read completeness — extend `InMemoryGraphSerializer` *(§11.3)*
Emit the **full** structure, not just nodes/pins/links/params: **attachments per node · regions/containers · comments ·
reroutes · host link metadata** *(HSM transition guard/event)*. ⇒ a decorator/region edit is READ-BACKABLE — the round-trip
proof for the host specifics. ⭐ Reuse the `IGraphModel` projection *(host-agnostic, one serializer)*.

### 3.3 ⭐⭐ Discovery completeness — EXTEND the shipped catalog *(§10)*
From the open document's `INodeCatalog.All` *(kinds + pins)* + `IActionSchemaExporter.DtoFields` *(editable params)*:
- extend `GET /assets/{id}/graph/catalog` → per kind: schema *(pins + params: name·type·enum·readOnly)* **and** host-structure
  capability *(is-container / accepts-regions via `IContainerNodeModel`; accepts-attachments + `AttachmentCategory`; pin kind
  exec-vs-data)*.
- add `GET /assets/{id}/graph/nodes/{guid}/properties` — the node's current values joined with its schema *(the Details view)*.

### 3.4 ⭐⭐⭐ Usage docs harvest — **the "action description scan" (SUBAGENT)** *(§10.6)*
⭐ **Spawn a subagent** to sweep the descriptive attributes and produce the doc payload, RouteDoc-style, **measured not authored**:
- harvest: `NodeCatalogEntry` *(name/desc/keywords/category)* · **`GeneratedAiPrimitiveActionAttribute`** *(host-validity flags)* ·
  `SharedAiActionAttribute` *(binding)* · the **StructEdit `Edit*Attribute` family** *(`EditDisplayName`/`EditRange`/`EditUnit`/
  `EditReadOnly`)* · pin tooltips · `RouteDoc` for the routes.
- ⚠ **the free-text gap:** "how to use" prose is not in any attribute today — it's in XML `/// <summary>`. Harvest the summary
  where present; where missing, **add a small `[Doc("…")]` attribute** to the StructEdit family *(additive, one line)* on the
  kinds/params that lack one. ⛔ **Never a parallel hand-authored table.**

### 3.5 ⭐⭐ UI-command actions *(§10.7)*
`GET /commands` *(list `EditorCommandDescriptor` + enabled/checked)* · `GET /commands/{id}` *(describe)* ·
`POST /commands/{id}/invoke` *(body `{args, canvasPos?}` → `IEditorCommands.Invoke(id, ctx)`; params ride
`EditorCommandContext.Args`)*. ⛔ **`GlobalActionRegistry` is OUT** *(int-keyed, undocumented — Axis-B/entity-action track)*.

### 3.6 ⭐ Every route: a `RouteDoc` + a handler in `src/index.mjs`; regenerate the catalog
`npm run gen:catalog` + `gen:skill` *(node/npm at `/opt/node22/bin`, OFF PATH)*; `node test-catalog.mjs` green.
📌 CE-009 §4c: advertised-but-unreachable tools are the classic miss — the gate catches them.

## 4. ⭐ LANE, SCOPE & COLLISION
⭐ **Yours:** extend `Hrot.Editor/DebugApi/DebugApiService.Authoring.cs` + `InMemoryGraphSerializer.cs`; new command/discovery/
UI-command route code; the generated catalog *(`DebugApiRouteDocs`, `tool-catalog.mjs`, `SKILL.md`, `src/index.mjs`)* — **you own
it, no concurrent MCP session**; `Hrot.SystemTests/**` rails; `tools/ai-debug-mcp/**`. ⛔ **NOT:** `Hrot.Editor.AiShared`
internals *(variable-model freeze — additive only, and STOP+log if you need more)*; `CgfSubsystem.cs` + the CGF time/debug files
*(a parallel CGF session)*; deletions. ⭐ **Rule 4:** re-pull coordinator before the final commit.

## 5. ⭐⭐⭐ DONE — the coverage rails *(this is the finish line, not a feeling)*
| rail | asserts |
|---|---|
| ⭐⭐⭐ **union coverage** | for **each host** *(BTree · HSM · Blueprint)*: create a small asset, apply **every SEMANTIC variant** over `POST /graph/command`, re-read, assert it landed *(decorators on BTree, regions on HSM, reparent, links, params, refactor where the host supports it)*; save→reload leaves a graph the compiler accepts. ⛔ a variant a host rejects is LOGGED, not skipped silently |
| ⭐⭐ **doc coverage** | **every** kind in `INodeCatalog.All` and **every** editable param resolves a non-empty **schema + doc** *(the "measured not authored" proof)* |
| ⭐⭐ **command coverage** | every `IEditorCommands` command is discoverable + describable; a safe subset is invocable over MCP and the effect is observable |
| ⭐ **the MA- round-trips still green** | read→edit→re-read→save→reload; create→appears→edit→save→reload; delete-entity |
| ⭐ **catalog** | `gen:catalog`/`gen:skill`/`test-catalog` green for every new route+handler |
| ⭐⭐ **integration** | `ClusterConformanceRails` / the system suite green *(T3, background)* — the DebugApi change did not break the cross-host contract |

## 6. GATES *(rule 8 contract)* + WHEN DONE
One row per gate · verbatim command · counts · Δ vs the started-marker · `--no-build` column · pre-existing reds by `git diff` ·
`tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · the `MA-` ids · golden movement as a diff shape ·
tree clean after every suite. **Row 8:** the union/doc/command coverage rails + the conformance suite named and run.
⭐ **When done:** fold the as-built into `DESIGN_Mcp_Authoring.md` *(§10/§10.6/§10.7/§11 → BUILT; obligation ⑤)*; state the ids;
the report carries the DECISION LOG + PROGRESS LOG; point the report at the design. ⛔ If a turn is cut off, the committed logs +
green rails so far ARE the delivery — the morning reads them.
