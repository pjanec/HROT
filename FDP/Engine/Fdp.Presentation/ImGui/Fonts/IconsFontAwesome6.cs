namespace Fdp.Presentation.Fonts;

/// <summary>
/// Named codepoint constants for the <em>Font Awesome 6 Free Solid</em> glyphs merged
/// onto the editor UI font by <see cref="EditorFontService"/>.
///
/// <para>Usage: prefix a widget label with the glyph, e.g.
/// <c>ImGui.Button(IconsFontAwesome6.FloppyDisk + " Save")</c>. The glyphs occupy the
/// Unicode Private Use Area, so each fits in a single UTF-16 char, written here as a
/// <c>\uXXXX</c> escape (the raw glyph is not printable in source).</para>
///
/// <para>Curated subset for the editor toolbar / menus. FA6 Free Solid has ~1400 glyphs;
/// the whole PUA range (<see cref="IconMin"/>..<see cref="IconMax"/>) is baked, so any solid
/// glyph is already available — only the friendly name is missing. Add more as needed.</para>
/// </summary>
public static class IconsFontAwesome6
{
    /// <summary>Lowest codepoint baked for the FA merge (inclusive).</summary>
    public const ushort IconMin = 0xE000;

    /// <summary>Highest codepoint baked for the FA merge (inclusive).</summary>
    public const ushort IconMax = 0xF8FF;


    // File / document
    public const string FloppyDisk          = "\uf0c7"; // save
    public const string FolderOpen          = "\uf07c"; // open
    public const string File                = "\uf15b";
    public const string FileLines           = "\uf15c";
    public const string Copy                = "\uf0c5";
    public const string Trash               = "\uf1f8";

    // Edit / undo
    public const string Pen                 = "\uf304"; // edit
    public const string Plus                = "\uf067";
    public const string Xmark               = "\uf00d";
    public const string ArrowRotateLeft     = "\uf0e2"; // undo
    public const string ArrowRotateRight    = "\uf01e"; // redo
    public const string ArrowsRotate        = "\uf021"; // reload / refresh

    // Run / transport
    public const string Play                = "\uf04b";
    public const string Pause               = "\uf04c";
    public const string Stop                = "\uf04d";
    public const string ForwardStep         = "\uf051"; // single-step
    public const string Hammer              = "\uf6e3"; // build / compile

    // Diagnostics / status
    public const string MagnifyingGlass     = "\uf002"; // search / find
    public const string Bug                 = "\uf188";
    public const string CircleCheck         = "\uf058"; // success
    public const string CircleXmark         = "\uf057"; // error
    public const string CircleQuestion      = "\uf059"; // condition / query
    public const string TriangleExclamation = "\uf071"; // warning
    public const string CircleInfo          = "\uf05a";
    public const string Eye                 = "\uf06e";
    public const string Bolt                = "\uf0e7"; // action / event

    // Structure / navigation
    public const string Gear                = "\uf013"; // settings
    public const string House               = "\uf015";
    public const string Code                = "\uf121";
    public const string CodeBranch          = "\uf126";
    public const string DiagramProject      = "\uf542"; // blueprint graph
    public const string Sitemap             = "\uf0e8"; // behavior tree / hierarchy
    public const string CircleNodes         = "\ue4e2"; // state machine / graph nodes
    public const string LayerGroup          = "\uf5fd";
    public const string Bars                = "\uf0c9"; // menu
}
