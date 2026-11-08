<!--STATUS state: LIVE · updated: 2026-11-08 · current-answer: the table -->
# STATUS — ui, after batch Z ([HANDOFF](HANDOFF_Coordinator_To_Ui_After_Z.md), dispatched at `99699401a`)

| item | id | what | state | sha | notes |
|---|---|---|---|---|---|
| U-1 | — | Start screen on the REAL editor | DONE | `6fdbbba43` | look keyed on the menu COMMAND id; reason → tooltip; measured widths; glyph-then-words row buttons; heading wraps; rail over the editor's own File menu at scale 1 and 1.5 |
| U-2 | CE-4333 | ONE icon-button chrome | DONE | `c61717172` `4e3fedb17` | `IconButtonChrome` in NodeEditor.Core.Widgets; GlyphWidgets + the mode indicator draw through it. Panels rendered (ORBAT, Details header both layouts, Rules list/timeline, condition ⋮, RowMarks ↻, presets) — transparent at rest, white frame on hover, accent fill selected; FYI: a teal glyph on the shared blue toggle fill is the weakest contrast (kept — the bitmap toolbar shares the fill) |
| U-3 | CE-4337 | Keyboard shortcuts → View ▸ Settings | DONE | `0d0815365` | one path `WindowManager.ShortcutsMenuPath`; the R-4176 two-path machinery removed; mockup + Input §6f |
| U-4 | CE-4334 | ORBAT row icons | DONE | `SHA_U4` | `IconKey` on the node, set by the adapter from components; 3 new `orbat/*` keys; rail over the real panel in all three relations, red-proved |
| U-5 | CE-4335 | End exercise greyed outside Live | QUEUED | — | |
| U-6 | CE-4336 | Data breakpoints: condition first | QUEUED | — | |
| U-7 | CE-4338 | Brain tab expander icons | QUEUED | — | |
| U-8 | CE-4339 | 3-D placement direction line | QUEUED | — | |
| U-9 | CE-4340 | 3-D hover pick volume | QUEUED | — | measure first |
| U-10 | CE-4325 | E7 bookmarks and notes | QUEUED | — | |
| U-11 | CE-4330 ui | recording-storage UI as facts land | QUEUED | — | |
| U-12 | CE-4129 · CE-4280 · CE-4273 | small rows | QUEUED | — | |

Gates after U-1…U-3: Presentation (Xvfb) 1910 / 6 — CE1039 / CE1380 / CE1227 known, `NoProductionHostKeepsAParallelSelectionStore…OnlyTheHostForwardWrites…` red at base `148b0a461` (VertexEditGizmo / RouteWaypointGizmo writes, pre-existing — not ui's batch), 2 load-flaky (CE4318, StringFieldTyping 0 B); Fdp.Presentation 821/0.
