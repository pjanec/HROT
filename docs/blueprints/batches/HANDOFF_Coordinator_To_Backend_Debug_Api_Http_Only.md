<!--STATUS
state: LIVE — dispatched, QUEUED (the first backend VM lane to finish its current batch takes it)
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - ../../DESIGN_Debug_Api_Http_Only.md — ⭐ THE design (§2 diagrams, §3 where every piece of information lives, §4 rules, §5 acceptance) — build it
  - ../../RUNBOOK_Cluster_Debugging_Over_Http.md — how to start a node; the skill points there
  - ../../MCP_Integration.md — the API's as-built record (its MCP-server sections become HISTORY)
-->

# HANDOFF — coordinator → backend (VM): AUTONOMOUS — the AI debug API HTTP-only, self-describing (R-4223)

**For:** whichever backend VM lane finishes its current batch first. **From:** coordinator. **Dispatched at `5057d7b06`** — scope frozen.
🔒 User, `2026-11-13`: *"ok, lets drop the MCP"* · *"unify and share, no duplication of information, we can keep generating but from single
source"* · *"guide generation is still ok but i guess this is what the GET help would return"*.

## ⛔⛔⛔ Never stop — the same contract

Never stop until every item is built and tested; no question in chat. Trouble ⇒ the best-practice option that fits the engine, no hacks,
recorded in DECISIONS. A bug ⇒ fix + a rail. Blocked ⇒ the other items, then back (two approaches before OPEN). ⛔ Never skip a test. Commit +
push per item, by path; tick its tracker row. Deviations go back into the design (obligation ⑤).

## Where to work

A NEW branch **`exp/debug-http`** from **`origin/exp/threaded-build`** (it carries the newest route table: the threaded route sides,
`/world/hash`, the review routes). ⛔ Never commit to `exp/threaded-build`. Push `chore: started debug-http at <sha>`. Status file
`docs/blueprints/batches/STATUS_Debug_Api_Http_Only.md`.

## The work — the design's §2–§5

| # | item | done when |
|---|---|---|
| **H-1** · `CE-4523` | ⭐ **param location DERIVED** (design §4): path from `{…}` in the template; GET / DELETE ⇒ query; POST / PUT / PATCH ⇒ body. A rail calls EVERY route with its documented example args through a recording `RouteContext` and fails when a handler reads a key from another place than derived, or an undocumented key | the rail green over all routes; red-proved (one handler made to read a body key from the query ⇒ red); every mismatch it finds FIXED in the handler or the doc |
| **H-2** · `CE-4524` | ⭐ **the guide prose MOVES** from `tools/ai-debug-mcp/skill-parts/*.md` into embedded resources in `Hrot.Editor` (moved, not rewritten; MCP / tool wording turned into HTTP wording; start-up instructions go to the runbook, not here — design §3) | the resources build; nothing in the prose names a tool or the MCP server |
| **H-3** · `CE-4525` | ⭐ **`GET /help`** (`AgentGuide.Render`): the prose + the command reference rendered from `DebugApiRouteDocs` as `METHOD /path` with query / body keys, types, defaults, enums, hints, notes; given a host, each route marked with what `/capabilities` says is available now. **`ClusterRunner --help-api`** prints the same text with no world started. Language-neutral (R-4221) | rails: `/help` names every documented route; `--help-api` equals `/help` minus availability; under `cs-CZ` a point |
| **H-4** · `CE-4526` | ⭐ **remove the MCP server**: `tools/ai-debug-mcp/` (server, catalog, supplement, generators, generated `SKILL.md`, package files), the `hrot-ai-debug` entry in `.mcp.json`, the C# rails that check the JS catalog, `RouteDoc.NotATool` (or rename to `Internal` if a route must stay out of `/help` — design §4). `EveryRouteIsDocumentedTests` STAYS | the build and the debug-API rails green; grep finds no `ai-debug-mcp` / `tool-catalog` outside `.dev/` and HISTORY sections |
| **H-5** · `CE-4527` | ⭐ **the skill becomes a pointer**: `.claude/skills/ai-debug-mcp` → `.claude/skills/ai-debug-http` — when it triggers, then: start per the runbook (§ the editor, a node, `--mode all`), then `GET /help` or `--help-api`. CLAUDE.md lines that name the MCP skill / `tools/ai-debug-mcp/SKILL.md` point to it | following the skill on this VM from scratch works: build, start, `GET /help`, one call |
| **H-6** · `CE-4528` | **the docs**: every doc that names the MCP server (24 under `docs/`, design INVENTORY) — a pointer to `/help` or a HISTORY note, ⛔ never a rewrite of what they own; `MCP_Integration.md`'s MCP-server sections → HISTORY; code comments naming `skill-parts` (`WindowManager.cs:206`, `scripts/ui-snapshot.py:26`) re-pointed | grep clean; `design-digest.py --check` no new failures |
| **H-7** · `CE-4529` | fold the as-built into `DESIGN_Debug_Api_Http_Only.md` (`build-state: BUILT`) | the design commit named |

## How to work

`scripts/cbm.sh` (graph) AND grep before claiming something exists. The debug API's own rails (`Hrot.Editor.Tests` `DebugApi` / `EveryRoute`,
the system tests' capability smoke) — ⛔ no broad suites. Build the TEST project before `--no-build`; restore after merging. Long runs in the
background, never polled. ⛔ No screenshots in git.

---
_coordinator lane, 2026-11-13_
