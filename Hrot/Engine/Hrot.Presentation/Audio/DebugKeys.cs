using Fdp.Toolkit.Input;
using NodeEditor.Primitives;

namespace Hrot.UI.Common
{
    /// <summary>
    /// ⭐⭐ B-10 K4 (📄 docs/UX/DESIGN_Input_And_Commands.md §6g) — the DEBUG F-keys as rows of the keymap's Global scope, qualifier <see cref="Qualifier"/> (the Keyboard shortcuts window groups them as
    /// <i>Debug keys</i>): the performance overlay (F3) and the audio keys (F6–F10 and its panel's arrows). ⛔ SUPERSEDES the raw <c>Raylib.IsKeyPressed(KeyboardKey.F3)</c> / <c>_keyPressed(F6…)</c> reads.
    /// <para>⚠ The qualifier keeps the K1 <c>KeyRouter</c> from dispatching them (it runs the Global rows with NO qualifier — the shell's commands): a debug surface READS its rows through
    /// <see cref="Reader"/>, the same way the Hold / Axis rows are read.</para>
    /// </summary>
    public static class DebugKeys
    {
        /// <summary>The Global scope's qualifier of the debug rows.</summary>
        public const string Qualifier = "debug";

        private static ActionRef Id(string id) => ActionRef.Command(id);

        /// <summary>Show / hide the Player's performance overlay.</summary>
        public static readonly ActionRef PerformanceOverlay = Id("debug.performance-overlay");
        /// <summary>Audio: master volume down.</summary>
        public static readonly ActionRef AudioMasterDown = Id("debug.audio.master-down");
        /// <summary>Audio: master volume up (and unmute).</summary>
        public static readonly ActionRef AudioMasterUp = Id("debug.audio.master-up");
        /// <summary>Audio: mute / unmute.</summary>
        public static readonly ActionRef AudioMute = Id("debug.audio.mute");
        /// <summary>Audio: play the test tone.</summary>
        public static readonly ActionRef AudioTest = Id("debug.audio.test");
        /// <summary>Audio: open / close the settings panel.</summary>
        public static readonly ActionRef AudioPanel = Id("debug.audio.panel");
        /// <summary>Audio panel (while open): the next row.</summary>
        public static readonly ActionRef AudioRowNext = Id("debug.audio.panel.row-next");
        /// <summary>Audio panel (while open): the previous row.</summary>
        public static readonly ActionRef AudioRowPrevious = Id("debug.audio.panel.row-previous");
        /// <summary>Audio panel (while open): the row's level up.</summary>
        public static readonly ActionRef AudioLevelUp = Id("debug.audio.panel.level-up");
        /// <summary>Audio panel (while open): the row's level down.</summary>
        public static readonly ActionRef AudioLevelDown = Id("debug.audio.panel.level-down");

        /// <summary>Declares the debug rows into <paramref name="keymap"/> (idempotent; today's keys).</summary>
        public static void Declare(Keymap keymap)
        {
            new RowDeclarer(keymap, InputScope.Global, Qualifier)
                .Press(EditorKey.F3, PerformanceOverlay, "Performance overlay")
                .Press(EditorKey.F6, AudioMasterDown, "Audio: master volume down")
                .Press(EditorKey.F7, AudioMasterUp, "Audio: master volume up")
                .Press(EditorKey.F8, AudioMute, "Audio: mute")
                .Press(EditorKey.F9, AudioTest, "Audio: test tone")
                .Press(EditorKey.F10, AudioPanel, "Audio: settings panel")
                .Press(EditorKey.Down, AudioRowNext, "Audio panel (open): next row")
                .Press(EditorKey.Up, AudioRowPrevious, "Audio panel (open): previous row")
                .Press(EditorKey.Right, AudioLevelUp, "Audio panel (open): level up")
                .Press(EditorKey.Left, AudioLevelDown, "Audio panel (open): level down");
        }

        /// <summary>A reader of the debug rows over <paramref name="input"/> (declares them first).</summary>
        public static KeymapReader Reader(Keymap keymap, IChordInput? input)
        {
            Declare(keymap);
            return new KeymapReader(keymap, InputScope.Global, Qualifier) { Input = input };
        }
    }
}
