using System.Numerics;

namespace NodeEditor.Core.Widgets;

/// <summary>
/// ⭐ Q-C1 (docs/UX/DESIGN_UI_Conventions.md §7, docs/UX/DESIGN_Colour_Icons.md §2) — a glyph and its colour as ONE value: a FontAwesome code point
/// (the string the font draws) and the palette colour it is painted in. It generalises the mode indicator's <c>ModeGlyph</c>. Lives in
/// <c>NodeEditor.Core</c> — not <c>Fdp.Presentation</c> — so the node-editor pickers can colour a glyph too. The semantic key → <see cref="IconGlyph"/> map
/// (<c>Icons</c>) is the Colour Icons slice (U-2); this is only the value type and its colours.
/// </summary>
public readonly record struct IconGlyph(string Glyph, Vector4 Color);
