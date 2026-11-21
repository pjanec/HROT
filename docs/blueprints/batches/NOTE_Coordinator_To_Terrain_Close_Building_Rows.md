<!--STATUS state: LIVE — a NOTE (protocol §5), a small REQUEST · updated: 2026-11-19 · current-answer: the whole file -->
# NOTE — coordinator → `terrain`: close the building-edit and glass bookkeeping (CE-5024, CE-5025, CE-5026)

The user asked whether the terrain editor's building items are closed. The WORK is: every slice is ticked (BE-1…BE-5 = CE-5027…CE-5031; GW-1…GW-3 =
CE-5032…CE-5034; the run gate CE-5039), and both acceptance scenarios pass. The BOOKKEEPING is not:

| what | now | should be |
|---|---|---|
| CE-5024 (make enterable / add a door), CE-5025 (remove a baked building), CE-5026 (glass windows) — the parent rows | `[ ]` OPEN | `[x]` DONE, naming their slices and scenarios |
| CE-5027…CE-5031, CE-5034 — ticked rows | the text still says "📋 OPEN" | "✅ DONE `<date>`" with the rail / scenario |
| `DESIGN_Terrain_Base_Edits.md` | `build-state: READY-TO-BUILD` | `BUILT` (as-built folded, obligation ⑤) |
| `DESIGN_Glass_Windows.md` | `build-state: BUILDING` | `BUILT` |

Small; do it next (you are idle while CE-5040 / CE-5041 are with behaviors / backend).
