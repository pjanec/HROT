<!--STATUS
state: LIVE — ui → backend: two cross-lane edits, said (CE-1371 in your keep handler; CE-1375 the MCP server serves every catalogued route)
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Director_Timeline_Editor.md — §11o (takes in a kept exercise), §11s (P4)
-->
# NOTE — ui → backend: a keep now carries the takes; the MCP server reaches every catalogued route

**Dispatched at** the commit that adds this file (`ui`). FYI — nothing to do unless you disagree.

| id | the edit in your area | why |
|---|---|---|
| [CE-1371](../Blueprint_Issues_Tracker.md) | `ReferenceArchiveHandler.PrepareAsync` adds every `<scenario>/takes/*.take.json` beside the recording to the keep manifest (the existing pull makes the folders). Rail `ReferenceArchiveHandlerTests.CE1371_The_archive_takes_the_takes_saved_beside_the_recording` | takes saved while an exercise was not yet kept stayed in the node's temp folder; your Export (*Keep for films*, CE-4058 ③) is the ONE copy, so it carries them — no second copier in the RB |
| [CE-1375](../Blueprint_Issues_Tracker.md) | `tools/ai-debug-mcp/src/index.mjs`: every catalog entry with an `http` route and no hand-written handler is served by ONE generic rule (path `{x}` from the arguments; GET → query, POST / PUT / DELETE → body); `callApi` gained PUT. `test-catalog.mjs` derives its expectation from `DebugApiRouteDocs.cs` (no hand-kept list) | 58 catalogued tools (`director_*`, `film_*`, `set_audio`, …) were listed in SKILL.md and unreachable over MCP; `test-catalog.mjs` was red with 111 failures |

Gates: `Hrot.Orchestrator.Tests` 293 / 0 / 0 · `node test-catalog.mjs` green (1505 checks) · `generate-skill.mjs --check` green.

_ui lane, 2026-10-27_
