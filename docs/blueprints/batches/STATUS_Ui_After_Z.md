<!--STATUS state: LIVE · updated: 2026-11-08 · current-answer: the table -->
# STATUS — ui, after batch Z ([HANDOFF](HANDOFF_Coordinator_To_Ui_After_Z.md), dispatched at `99699401a`)

| item | id | what | state | sha | notes |
|---|---|---|---|---|---|
| U-1 | — | Start screen on the REAL editor | DONE | `6fdbbba43` | look keyed on the menu COMMAND id; reason → tooltip; measured widths; glyph-then-words row buttons; heading wraps; rail over the editor's own File menu at scale 1 and 1.5 |
| U-2 | CE-4333 | ONE icon-button chrome | DONE | `c61717172` `4e3fedb17` | `IconButtonChrome` in NodeEditor.Core.Widgets; GlyphWidgets + the mode indicator draw through it. Panels rendered (ORBAT, Details header both layouts, Rules list/timeline, condition ⋮, RowMarks ↻, presets) — transparent at rest, white frame on hover, accent fill selected; FYI: a teal glyph on the shared blue toggle fill is the weakest contrast (kept — the bitmap toolbar shares the fill) |
| U-3 | CE-4337 | Keyboard shortcuts → View ▸ Settings | DONE | `0d0815365` | one path `WindowManager.ShortcutsMenuPath`; the R-4176 two-path machinery removed; mockup + Input §6f |
| U-4 | CE-4334 | ORBAT row icons | DONE | `cdf40c359` | `IconKey` on the node, set by the adapter from components; 3 new `orbat/*` keys; rail over the real panel in all three relations, red-proved |
| U-5 | CE-4335 | End exercise greyed outside Live | DONE | `03f787977` | `InstructorEnd.RunRefusal` by transport mode, wired to both End controls by the rules bundle; Live · ended keeps its own words, AAR not drawn; rails per mode (pure, bundle wiring, real window frame) |
| U-6 | CE-4336 | Data breakpoints: condition first | DONE | `ab4894875` | *Add* opens the condition editor over a draft, row only on commit; *Show condition* was hidden below an unbounded table (measured) — the list now keeps a bounded height; rails for both |
| U-7 | CE-4338 | Brain tab expander icons | DONE | `98d948775` | `EntityBrainView.SectionIconKey` maps each section id to a glyph of the one map (new `section/debug`); frame rail over a unit and a commander, every section open |
| U-8 | CE-4339 | 3-D placement direction line | DONE | `af0ac6068` | `PlacementDirectionLine` (one drawer) in the placement tool and Paste; the placement ghost now stands at the press point while dragging; rails: both tools headless + the real tool through the 3-D gizmo layer in a frame |
| U-9 | CE-4340 | 3-D hover pick volume | DONE | `815453ab2` | measured: the hover read the hidden 2-D camera's zoom + the 2-D footprint box — a 0.5 m man answered from 5–23 m (13–202 px); `PickEntity` in 3-D now = the camera's own ray pick (drawn box + 6 px); a 3-D click-select is unchanged (follow-up) |
| U-10 | CE-4325 | E7 bookmarks and notes | QUEUED | — | |
| U-11 | CE-4330 ui | recording-storage UI as facts land | QUEUED | — | |
| U-12 | CE-4129 · CE-4280 · CE-4273 | small rows | DONE (CE-4273 half) | `202cd5c5c` | CE-4129: the raw transport obeys the run gate (`PlayRefusal`), `/sim/play` says `refused`, an empty world in a run says so · CE-4280: HUD texts Tab / J, railed from the keymap · CE-4273: script hold-still + wait-gone railed with a fake API; ⚠ the Car's picture needs a run against a live editor (row stays open) |

Gates after U-1…U-3: Presentation (Xvfb) 1910 / 6 — CE1039 / CE1380 / CE1227 known, `NoProductionHostKeepsAParallelSelectionStore…OnlyTheHostForwardWrites…` red at base `148b0a461` (VertexEditGizmo / RouteWaypointGizmo writes, pre-existing — not ui's batch), 2 load-flaky (CE4318, StringFieldTyping 0 B); Fdp.Presentation 821/0.

Gates after U-4…U-12: Presentation (Xvfb) 1933 / 5 (4 known + RouteWaypointGizmo CE1096 load-flaky), Editor 868 / 1 (`EveryToolIsInTheGeneratedCatalog` — backend's new storage routes missing from the MCP catalog: [FYI note](NOTE_Ui_To_Backend_Tool_Catalog_Regen.md)), AiShared 2326 / 1 (CE1325 load-flaky). Filed `CE-1465` (3-D click-select still on the 2-D footprints, found in U-9). ⚠ CE-4273: the script half built; the Car's thumbnail itself needs a run against a live editor (`scripts/thumbnails.py --types Car --pack`) — row stays open.
