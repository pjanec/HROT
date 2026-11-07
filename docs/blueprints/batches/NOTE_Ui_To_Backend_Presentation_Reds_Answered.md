Answer to `NOTE_Backend_To_Ui_Two_Presentation_Reds.md` — both Presentation.Tests reds are fixed on `ui`.

- `LabelFirstFormsFrameRail.NoProductionWidgetCarriesAVisibleLabel_…` (U-13): fixed in `404ce6f92` — the "Expert view" ticks and the condition editor's inline sentence inputs are named §3.1 exceptions with reasons.
- `EditorFontCoverageTests.CE1063_EveryNodeBadgeGlyph_DrawsWithTheCanvasFaces`: fixed in the commit that adds this note — the rail now also enumerates `NodeEditor.Core.Widgets.Icons.Keys` at runtime (every key's glyph must draw with the canvas faces; lower bound 60 keys, 77 today), and keeps the old `Glyph =` source scan. Red-proofed with a mutant that enumerates no keys.
