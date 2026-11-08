<!--STATUS
state: LIVE
updated: 2026-11-08
current-answer: the whole file
related-designs:
  - ../../UX/DESIGN_Colour_Icons.md — §2 (IconWidgets +GlyphButton · GlyphToggle, the switch framed by IconButtonChrome), §I1 as built (GlyphWidgets in NodeEditor.Core, R-4145)
  - ../../designs/main-toolbar-1/DESIGN.md — the toolbar and its icons
-->

# NOTE — coordinator → ui: two chromes for icon buttons on ONE toolbar — make it one (CE-4333)

🔒 User, `2026-11-08`, on the main bar: *"the yellow flag button has pitch black background, also armed mode split button, which is different from the
other icons. The yellow flag when hovered is NOT showing the white rectangle. Are we using shared code for rendering the icon/glyph buttons?"* —
⛔ **no, two.** Measured:

| button | drawn by | look |
|---|---|---|
| the bitmap toolbar icons (★, the window icon …) | `IconWidgets.IconButton` → `IconButtonChrome` (`FDP/Engine/Fdp.Presentation/ImGui/Icons/IconButtonChrome.cs`: "single shared chrome for all toolbar icon buttons") | transparent; a white inset frame on hover; an accent fill when toggled |
| the yellow flag = **Save in a run** (its checkpoint glyph, `ShellSaveCommands.cs:167` `DynamicGlyph`) | `ToolbarCommandAdapter.cs:93-97` → `GlyphWidgets.GlyphButton` (`NodeEditor.Core/Widgets/GlyphWidgets.cs:34-49`) | a filled neutral `#1C1C22` box, a lighter box on hover, ⛔ no frame |
| the mode indicator (● ▾) | its own copy of that look (`MainToolbarTimeControlSection.cs:749-780`, CE-3406) | the same neutral box |

📐 Design basis: [Colour Icons](../../UX/DESIGN_Colour_Icons.md) §2 meant `GlyphButton` / `GlyphToggle` to sit in `IconWidgets` and a switch's
selected option to be *"framed (`IconButtonChrome`)"*; the as-built moved them to `NodeEditor.Core` (R-4145, so the pickers reach them) and gave them
a chrome of their own — the second copy (ruling 9: one implementation per concept).

**Fix (the shape, yours to build):** ONE chrome. Move `IconButtonChrome` down to `NodeEditor.Core.Widgets` (where `GlyphWidgets` already lives, so
both assemblies reach it); `GlyphWidgets.Draw`, `IconWidgets.ToggleIcon` and the mode indicator all draw through it: transparent at rest, the white
inset frame on hover, the toggled fill for a selected switch option. Fold the as-built back into Colour Icons §I1 (prior text SUPERSEDED).
⚠ `GlyphWidgets` is also used inside panels (Details header, Rules list / timeline switch, ORBAT, condition rows, reference drawers) — they change
with it; that is the point (one look), but render each of those and say in the report what changed.

**Acceptance:** a frame rail on the main toolbar in a run (Save showing the flag, the mode indicator, a bitmap icon): at rest no button draws a fill;
hovering each draws the same frame (read the draw list or the pixels); a selected switch option is filled. Side-by-side shot in the report (R-4146).
Small — after the Start-screen note, before Z-7 continues.

---
_coordinator lane, 2026-11-08_
