## Codebase Memory MCP

**MANDATORY: use Codebase Memory MCP graph tools FIRST — before reading files, before making code
changes, and before asserting anything about what exists in this codebase.**

This rule applies to every request involving this codebase.

### 🔴 Why this keeps failing — read before deciding it does not apply

The rule used to say only *"before reading files or making changes"*, so during **design and prior-art
research** — where nothing is being edited — it read as inapplicable, and grep felt sufficient. **That is
the failure mode.** It has recurred across sessions.

**The trigger is not editing. It is the shape of the claim you are about to make.**

| Claim shape | Tool | Why grep cannot do it |
|---|---|---|
| *"There are N of these"* · *"these are all the implementations / levels / hosts"* | 🔴 **`search_graph`** | grep answers *"does X exist"*. It **cannot** answer *"what is the complete set of X"* — you only ever see what your pattern happened to match |
| *"Nothing implements / reads / writes this"* — the seam-law claim | 🔴 **`search_graph`**, then open the sites | an absence claim from grep is an absence *in your pattern*, not in the repo |
| *"What is the overall shape here?"* | **`get_architecture`** (try `aspects:["clusters"]` — it surfaces de-facto modules that cut across the folder layout) | — |
| *"Who calls this?"* | ⚠ **`trace_path` AND grep** — see the caveat below | — |
| *"What does this function do?"* | **`get_code_snippet`**, then `Read` for exact lines | — |

⚠ **Measured, 2026-08-13, on the TKB stack** — both directions matter:

- **Graph found what grep missed.** Three rounds of grep produced a **four**-interface inventory;
  a single `search_graph(name_pattern=".*Tkb.*", label="Interface")` returned **five**. The missed one
  (`ITkbHotReloadEvents`) was load-bearing — a cache-invalidation contract with a live subscriber and no
  publisher.
- **Grep found what the graph missed.** `trace_path` on `ITkbDatabase.TryGetByType` reported **3 callers**
  (all tests/examples) where grep found **9+ production sites**. **C# interface dispatch defeats the call
  resolver.**

🔒 **So: graph first for inventory and structure, grep to confirm call sites, and never rely on either
alone for an exhaustive claim.** Use `check_index_coverage` before any negative or exhaustive claim —
coverage is best-effort and is never proof of completeness.

⚠ **If the MCP server is disconnected mid-session** (it does drop), say so explicitly in any inventory
claim made while it was down, and re-verify those claims once it reconnects. Do not silently downgrade to
grep and present the result with the same confidence.

> **Cloud / Claude Code on the web:** if the `mcp__codebase-memory-mcp__*` tools are
> not connected or `dotnet` is missing, this is a fresh cloud VM — run the
> `/cloud-bootstrap` skill (or `bash scripts/cloud-bootstrap.sh`) to install the
> .NET 8 SDK and the codebase-memory-mcp server. Note: if the binary is installed
> mid-session, the graph tools connect on the **next** session (MCP servers spawn
> at session start). For session-#1 tools, run the bootstrap from the environment's
> Setup script. Details: `docs/cloud-codebase-memory-mcp.md`. (Local Windows/VS Code
> sessions already have the tools — skip this.)

Always call `list_projects` first when you do not already know the project name, then use the `display_name` or exact `name` returned by that tool.

```json
// Step 0 — discover project names
mcp_codebase-memo_list_projects()

// Step 1 — use the project identifier returned above
mcp_codebase-memo_get_architecture({ "project": "<display_name>" })
```

### Workflow

0. **If `list_projects` returns an empty list** (a fresh cloud VM re-indexes each
   session — the graph is not persisted): **immediately** call
   `index_repository(repo_path="<repo root>")` for this repo, **without asking**,
   then continue. Indexing ~5k C# files takes only tens of seconds. On Linux the
   repo root is the current working directory (e.g. `/home/user/IOS-IG-SimHost-FDP`).
1. Call `list_projects` to discover the correct project name.
2. Call `get_architecture(project)` to understand the codebase structure.
3. Use `search_graph` to find relevant symbols, `trace_path` for call chains.
4. Use `get_code_snippet` to read specific function implementations.
5. Only use `Read` when you need exact raw content to edit a specific line.

**Opening move for any design / prior-art task** — cheap, and it is the step that keeps getting skipped:

```
search_graph(project, name_pattern=".*<Topic>.*", label="Interface")   # the complete seam inventory
search_graph(project, name_pattern=".*<Topic>.*", label="Class")       # the implementations
get_architecture(project, aspects:["clusters"])                        # the de-facto modules
```

⚠ **`trace_path` names are qualified.** A bare name returns `status:"ambiguous"` with suggestions —
pick the production `qualified_name` from them (the list will be mostly test doubles; that is expected,
not a signal).

### Available Tools (15 MCP tools)

**Indexing:**
- `index_repository(repo_path)` — Index a repository into the knowledge graph
- `list_projects` — List all indexed projects with node/edge counts
- `delete_project(project)` — Remove a project and all its graph data
- `index_status(project)` — Check indexing status
- `check_index_coverage` — 🔴 **run before any negative or exhaustive claim**; coverage is best-effort, never proof of completeness

**Querying:**
- `search_graph(query | name_pattern | semantic_query, label, file_pattern, …)` — 🔴 **the inventory tool.** Three independent modes: `query=` BM25 full-text, `name_pattern=` regex, `semantic_query=[...]` vector. Paginate while `has_more` is true — a truncated page is not the answer
- `trace_path(function_name, direction, depth, mode)` — call chains; `mode` also does `data_flow` and `cross_service`. ⚠ under-reports C# interface dispatch
- `detect_changes(project)` — Map git diff to affected symbols + risk
- `query_graph(query)` — Execute Cypher-like graph queries (read-only)
- `get_graph_schema(project)` — Node/edge counts, relationship patterns
- `get_code_snippet(qualified_name)` — Read source code for a function
- `get_architecture(project, aspects)` — Overview; `clusters` finds the real seams, `cycles` is opt-in
- `search_code(pattern, project)` — Grep-like text search within indexed files
- `manage_adr(action)` — CRUD for Architecture Decision Records
- `ingest_traces(traces)` — Ingest runtime traces to validate HTTP edges

## Assistant interaction preferences

- **Ask questions in plain chat text, never with the question/multiple-choice widget** (do not use the `AskUserQuestion` tool). List options as normal prose the user can reply to.
- **Model delegation (token thrift):** keep Opus for orchestration and hard reviews; delegate heavier work that does not need Opus-level intelligence (mirror-an-existing-pattern slices, mechanical edits, broad searches) to a **Sonnet** subagent. Opus reviews the real diff and re-runs the gates. Do novel scheduler/IR/compiler work hands-on.
- **Build general, not just minimal (round-out):** when a task needs a generic node, implement the whole obvious set rather than only the one value the immediate task needs — e.g. the `Compare` node ships every `ComparisonOperator`, not just `==`; an operator/enum-keyed node covers the full enum. Proactively add closely-similar, generally-useful companions (the arithmetic/boolean peers of a comparison node) when they reuse the same machinery and are plausibly usable. Default toward completeness over minimalism. Balance against the architect's demand-driven caution: if a round-out means a whole new *speculative* vocabulary or contradicts an explicit architect ruling, flag it for a quick nod first rather than silently building it — but don't be stingy with cheap, obvious generality.
- **Prior-art discipline (the seam law):** in this codebase a *"we need a shared X"* almost always means **X already exists and is under-adopted** — 24 measured instances so far. So every design opens with a prior-art pass, and that pass **starts with `search_graph`, not grep** (see the Codebase Memory section above — this is the rule that keeps getting skipped). Two failure modes to name explicitly: ⚠ **never read a reference *count* as adoption** — open the call sites; and ⚠ *"the seam is unused"* has two very different meanings — an interface nobody calls, versus one called every frame with a dead parameter. The fixes differ completely.
- **Architect-questioning discipline (engine-rules gate):** no non-trivial capability / node / slice starts without a design, and no non-trivial design ships without an **architect pass**. The "architect" is the user's NotebookLM system holding the engine design docs — Claude CANNOT reach it; the user relays. For each non-trivial task, draft an `docs/blueprints/Architect_Question_N_*.md` mirroring the existing Q#2–Q#5 docs (decision-shaped sub-questions A/B/C/D + Claude's recommended lean + the reuse-vs-build tradeoff for each), the user runs it through the architect, record the answers in that doc, **then** build. Prior sessions' architect answers repeatedly redirected the approach — treat this as load-bearing, not ceremony. Trivial mirror-pattern nodes (a documented recipe already exists) may proceed on a short in-repo design note without a full architect round.
- **Diagrams: prefer hand-authored SVG for anything non-trivial.** Mermaid is acceptable only for simple flowcharts; for richer pictures (memory layouts, timelines, architecture overviews) author SVG — it renders more reliably (Mermaid sometimes clips labels / lays out awkwardly) and looks better. Keep Mermaid box labels short so text is not clipped.
- **Keep documentation prose short.** Lead with visuals and terse tables; no long prose walls — they go unread.
