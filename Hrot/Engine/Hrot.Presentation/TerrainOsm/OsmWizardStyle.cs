using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.ScenarioEditor.Gizmos;

namespace Hrot.UI.Common.TerrainOsm;

/// <summary>
/// ⭐ CE-4375 (R-4138 fidelity) — the APPROVED mockup's named colours and glyphs (<c>scripts/mockup_kit.py</c> <c>C</c> / <c>FA</c>, <c>scripts/gen-terrain-osm-wizard-mockup.py</c>),
/// one place the window and its conformance rails read. ⭐ The preview's map colours are NOT here: they are <see cref="TerrainPlan2D"/>'s (read, never copied) — only the two
/// the scenario map has no fill for (solid buildings, scrub) and the road classes are the mockup's own.
/// </summary>
public static class OsmWizardStyle
{
    private static Vector4 Hex(uint rgb, float a = 1f) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);
    public static Vector4 Of(Rgba32 c, float? alpha = null) => new(c.R / 255f, c.G / 255f, c.B / 255f, alpha ?? c.A / 255f);

    // mockup_kit C
    public static readonly Vector4 FRAME = Hex(0x1D2F48), BTN = Hex(0x23415F), BTNSEL = Hex(0x3369AD), TXT = Hex(0xFFFFFF), DIS = Hex(0x808080), SUB = Hex(0xC8C8D0),
        NOTE = Hex(0x8A8A96), BORDER = Hex(0x3A3A44), SEP = Hex(0x4A4A55), ACC = Hex(0x4296FA), POPUP = Hex(0x141418), NEUTRAL = Hex(0x1C1C22), NEUTRALSEL = Hex(0x34343E),
        ERR = Hex(0xE65555), PEND = Hex(0xF2C033), PENDROW = Hex(0x3A3112), INFO = Hex(0x6AB0FF), OK = Hex(0x5BD06A), TEAL = Hex(0x3EC6B0), ORANGE = Hex(0xF29B38),
        BLUE = Hex(0x5AA9FF), AMBER = Hex(0xF2C033), GREEN = Hex(0x5BD06A), RED = Hex(0xE65555), GREY = Hex(0xA0A0AA), OKBOX = Hex(0x173A1E), WHITE = Hex(0xFFFFFF),
        /// <summary>mockup_kit separator_text's label colour.</summary>
        SEPTEXT = Hex(0x9FB7D8);

    /// <summary>The light map the tiles show where none has arrived yet (the mockup's tile ground).</summary>
    public static readonly Vector4 MapGround = Hex(0xF2EFE9), MapGrid = Hex(0xE2DED6);

    // the preview's own (no TerrainPlan2D fill exists for these)
    public static readonly Vector4 Solid = Hex(0x8C8C96), Scrub = Hex(0x6E8C3C), Outline = Hex(0x141414);
    public static Vector4 Forest => Of(TerrainPlan2D.ForestFill, 0.55f);
    public static Vector4 Water => Of(TerrainPlan2D.WaterFill, 0.9f);
    public static Vector4 Wall => Of(TerrainPlan2D.WallFill, 1f);
    /// <summary>A building's fill by storeys (3 m each) — <see cref="TerrainPlan2D.HeightColor"/>, opaque as the mockup draws it.</summary>
    public static Vector4 Building(int storeys) => Of(TerrainPlan2D.HeightColor(storeys * 3f), 1f);

    /// <summary>The road classes (colour, width px) — the mockup's ROAD table.</summary>
    public static (Vector4 Colour, float Width) Road(string cls) => cls switch
    {
        "motorway" or "trunk" => (Hex(0xE07A5F), 5f),
        "primary" => (Hex(0xF2C033), 4f),
        "secondary" or "tertiary" => (Hex(0xE8D58A), 3.2f),
        "track" or "path" or "footway" => (Hex(0xA08868), 1.2f),
        "rail" => (Hex(0xB0B0B8), 1.4f),
        _ => (Hex(0xD8D8DC), 2.2f),
    };

    // mockup_kit FA (FontAwesome 6 solid) — the glyph strings the window draws
    public const string CHECK = "", XMARK = "", SEARCH = "", LOCATION = "", INFO_G = "", CIRCLE_CHECK = "", WARNING = "",
        HOUSE = "", EYE = "", HOURGLASS = "", CIRCLE = "", FILE = "", FOLDER_OPEN = "", ROUTE = "", PLUS = "",
        MINUS = "", CROSSHAIRS = "", BACK = "", NEXT = "", TREE = "", FENCE = "", WATER_G = "", SQUARE = "",
        DOOR = "", BACKGROUND = "";
}
