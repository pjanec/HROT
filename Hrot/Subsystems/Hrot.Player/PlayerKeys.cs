using System.Numerics;
using Fdp.Toolkit.Input;
using NodeEditor.Primitives;

namespace Hrot.Player;

/// <summary>
/// ⭐⭐ B-10 K4 (📄 docs/UX/DESIGN_Input_And_Commands.md §6g) — the Player's 2-D map keys as rows of the keymap's MAP scope, qualifier <see cref="Qualifier"/> (the map's view: pan and zoom):
/// the arrow keys pan (two Axis rows each), <c>=</c> / <c>+</c> and <c>−</c> (and the keypad's) zoom one wheel notch (Press). ⛔ SUPERSEDES the raw <c>Raylib.IsKeyDown(KeyboardKey.Right)</c> … reads of
/// <c>PlayerApplication.HandleCameraInput</c>.
/// <para>⛔ Not here: the Player's Ctrl+Z / Ctrl+Y — nothing ever pushes onto its undo stack and R-4120 says no undo in Live / Preview (design §6g, STOPPED, R-106).</para>
/// </summary>
public static class PlayerKeys
{
    /// <summary>The map scope's qualifier of the map-view rows.</summary>
    public const string Qualifier = "view";

    private static ActionRef Id(string id) => ActionRef.Command(id);

    /// <summary>Pan east (+) / west (−).</summary>
    public static readonly ActionRef PanX = Id("map.pan.x");
    /// <summary>Pan north (+) / south (−): the 2-D map is north-up (CE-1040).</summary>
    public static readonly ActionRef PanY = Id("map.pan.y");
    /// <summary>Zoom in one notch.</summary>
    public static readonly ActionRef ZoomIn = Id("map.zoom.in");
    /// <summary>Zoom out one notch.</summary>
    public static readonly ActionRef ZoomOut = Id("map.zoom.out");

    /// <summary>Declares the map-view rows into <paramref name="keymap"/> (idempotent; today's keys).</summary>
    public static void Declare(Keymap keymap)
    {
        new RowDeclarer(keymap, InputScope.Map, Qualifier)
            .Axis(EditorKey.Right, PanX, "Pan the map east / west", +1f)
            .Axis(EditorKey.Left, PanX, "Pan the map east / west", -1f)
            .Axis(EditorKey.Up, PanY, "Pan the map north / south", +1f)
            .Axis(EditorKey.Down, PanY, "Pan the map north / south", -1f)
            .Press(EditorKey.Equals, ZoomIn, "Zoom the map in")
            .Press(EditorKey.KpAdd, ZoomIn, "Zoom the map in")
            .Press(EditorKey.Minus, ZoomOut, "Zoom the map out")
            .Press(EditorKey.KpSubtract, ZoomOut, "Zoom the map out");
    }

    /// <summary>A reader of the map-view rows over <paramref name="input"/> (declares them first).</summary>
    public static KeymapReader Reader(Keymap keymap, IChordInput? input)
    {
        Declare(keymap);
        return new KeymapReader(keymap, InputScope.Map, Qualifier) { Input = input };
    }

    /// <summary>The pan direction this frame in WORLD axes (east +X, north +Y); zero at rest.</summary>
    public static Vector2 Pan(KeymapReader keys) => new(keys.Axis(PanX), keys.Axis(PanY));
}
