using Fdp.Toolkit.Input;
using NodeEditor.Primitives;

namespace Hrot.UI.Common.Map3D
{
    /// <summary>
    /// ⭐ <c>CE-1989</c> (📄 docs/designs/sensor-view/DESIGN_Sensor_View.md §10, R-1057 — user: *"F4 is fine"*) — the 3-D map's view keys as rows of the keymap's
    /// Global scope, qualifier <see cref="Qualifier"/> (like <see cref="DebugKeys"/>: the K1 KeyRouter does not dispatch a qualified row; the map's
    /// <see cref="SensorView"/> READS it), so the key works on the map and in first person, is listed in the Keyboard shortcuts window and can be rebound.
    /// 📐 F4 measured free in every scope `2026-11-19` (F2 rename, F3 the performance overlay, F6–F10 audio; N is the first person's message log).
    /// </summary>
    public static class SensorViewKeys
    {
        /// <summary>The Global scope's qualifier of the map-view rows.</summary>
        public const string Qualifier = "map-view";

        /// <summary>The next view: normal → night vision → thermal → normal.</summary>
        public static readonly ActionRef Next = ActionRef.Command("map.view.sensor.next");

        /// <summary>Declares the rows into <paramref name="keymap"/> (idempotent).</summary>
        public static void Declare(Keymap keymap)
            => new RowDeclarer(keymap, InputScope.Global, Qualifier).Press(EditorKey.F4, Next, "Map view: normal / night vision / thermal");

        /// <summary>A reader of the rows over <paramref name="input"/> (declares them first).</summary>
        public static KeymapReader Reader(Keymap keymap, IChordInput? input)
        {
            Declare(keymap);
            return new KeymapReader(keymap, InputScope.Global, Qualifier) { Input = input };
        }
    }
}
