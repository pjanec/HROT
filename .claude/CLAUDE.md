## Codebase Memory MCP

**MANDATORY: use Codebase Memory MCP graph tools FIRST — before reading files or making code changes.**

This rule applies to every request involving this codebase.

Always call `list_projects` first when you do not already know the project name, then use the `display_name` or exact `name` returned by that tool.

```json
// Step 0 — discover project names
mcp_codebase-memo_list_projects()

// Step 1 — use the project identifier returned above
mcp_codebase-memo_get_architecture({ "project": "<display_name>" })
```

### Workflow

1. Call `list_projects` to discover the correct project name.
2. Call `get_architecture(project)` to understand the codebase structure.
3. Use `search_graph` to find relevant symbols, `trace_call_path` for call chains.
4. Use `get_code_snippet` to read specific function implementations.
5. Only use `read_file` when you need exact raw content to edit a specific line.

### Available Tools (14 MCP tools)

**Indexing:**
- `index_repository(repo_path)` — Index a repository into the knowledge graph
- `list_projects` — List all indexed projects with node/edge counts
- `delete_project(project)` — Remove a project and all its graph data
- `index_status(project)` — Check indexing status

**Querying:**
- `search_graph(name_pattern, name_scope, label, file_pattern, exclude_file_pattern)` — Structured search by label, name/qualified_name, include/exclude file globs
- `trace_call_path(function_name, direction, depth)` — BFS call chain traversal
- `detect_changes(project)` — Map git diff to affected symbols + risk
- `query_graph(query)` — Execute Cypher-like graph queries (read-only)
- `get_graph_schema(project)` — Node/edge counts, relationship patterns
- `get_code_snippet(qualified_name)` — Read source code for a function
- `get_architecture(project)` — Codebase overview: languages, packages, routes, hotspots
- `search_code(pattern, project)` — Grep-like text search within indexed files
- `manage_adr(action)` — CRUD for Architecture Decision Records
- `ingest_traces(traces)` — Ingest runtime traces to validate HTTP edges

## Assistant interaction preferences

- **Ask questions in plain chat text, never with the question/multiple-choice widget** (do not use the `AskUserQuestion` tool). List options as normal prose the user can reply to.
- **Model delegation (token thrift):** keep Opus for orchestration and hard reviews; delegate heavier work that does not need Opus-level intelligence (mirror-an-existing-pattern slices, mechanical edits, broad searches) to a **Sonnet** subagent. Opus reviews the real diff and re-runs the gates. Do novel scheduler/IR/compiler work hands-on.
- **Build general, not just minimal (round-out):** when a task needs a generic node, implement the whole obvious set rather than only the one value the immediate task needs — e.g. the `Compare` node ships every `ComparisonOperator`, not just `==`; an operator/enum-keyed node covers the full enum. Proactively add closely-similar, generally-useful companions (the arithmetic/boolean peers of a comparison node) when they reuse the same machinery and are plausibly usable. Default toward completeness over minimalism. Balance against the architect's demand-driven caution: if a round-out means a whole new *speculative* vocabulary or contradicts an explicit architect ruling, flag it for a quick nod first rather than silently building it — but don't be stingy with cheap, obvious generality.
- **Architect-questioning discipline (engine-rules gate):** no non-trivial capability / node / slice starts without a design, and no non-trivial design ships without an **architect pass**. The "architect" is the user's NotebookLM system holding the engine design docs — Claude CANNOT reach it; the user relays. For each non-trivial task, draft an `docs/blueprints/Architect_Question_N_*.md` mirroring the existing Q#2–Q#5 docs (decision-shaped sub-questions A/B/C/D + Claude's recommended lean + the reuse-vs-build tradeoff for each), the user runs it through the architect, record the answers in that doc, **then** build. Prior sessions' architect answers repeatedly redirected the approach — treat this as load-bearing, not ceremony. Trivial mirror-pattern nodes (a documented recipe already exists) may proceed on a short in-repo design note without a full architect round.
- **Diagrams: prefer hand-authored SVG for anything non-trivial.** Mermaid is acceptable only for simple flowcharts; for richer pictures (memory layouts, timelines, architecture overviews) author SVG — it renders more reliably (Mermaid sometimes clips labels / lays out awkwardly) and looks better. Keep Mermaid box labels short so text is not clipped.
- **Keep documentation prose short.** Lead with visuals and terse tables; no long prose walls — they go unread.
