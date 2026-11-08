using System;
using Fdp.Toolkit.Input;
using Hrot.Common.Editing;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>
    /// ⭐⭐ B-10 K7 (📄 docs/UX/DESIGN_Input_And_Commands.md §6b, R-4163) — the ids of what a map MOUSE GESTURE runs. They are the keymap's string action ids
    /// (<see cref="ActionRef.Command(string)"/>, the id space K5 folds into the catalogue). The map scope's verbs are resolved on the BACKEND by the
    /// systems that own the effect — <see cref="Hrot.ScenarioEditor.Systems.SelectionInteractionSystem"/> for the selection, the terminal for the menu it opens —
    /// and the RTS scope's by <see cref="RtsCommandGizmo"/>.
    /// </summary>
    public static class MapGestureIds
    {
        // ── the map scope ──────────────────────────────────────────────────────────────────────────────────────────────────────────
        /// <summary>Left click on an entity (selected or not): it becomes the selection alone.</summary>
        public const string Select = "map.select";
        /// <summary>Left click on empty ground: the selection is cleared.</summary>
        public const string Clear = "map.clear";
        /// <summary>Left press-and-drag from empty ground: the rubber band.</summary>
        public const string Band = "map.band";
        /// <summary>⭐ B-17 P5 — Shift + left click on an entity: it joins the selection.</summary>
        public const string SelectAdd = "map.select.add";
        /// <summary>⭐ B-17 P5 — Ctrl + left click on an entity: it joins the selection, or leaves it when it is already in.</summary>
        public const string SelectToggle = "map.select.toggle";
        /// <summary>⭐ B-17 P5 — Alt + left click: the next of the units overlapped under the cursor becomes the selection (each Alt-click the next).</summary>
        public const string SelectCycle = "map.select.cycle";
        /// <summary>⭐ B-17 P5 — Shift + press-and-drag from empty ground: the band ADDS its units to the selection.</summary>
        public const string BandAdd = "map.band.add";
        /// <summary>Right click on an entity of the selection: its menu, for the whole selection — the selection is unchanged (Action Catalogue §8 A4).</summary>
        public const string MenuSelection = "map.menu.selection";
        /// <summary>Right click on an entity outside the selection: it becomes the selection alone, and its menu opens.</summary>
        public const string MenuEntity = "map.menu.entity";
        /// <summary>Right click on empty ground: the EMPTY-SPACE menu only — the selection is left alone (R-4163).</summary>
        public const string MenuEmpty = "map.menu.empty";

        // ── the RTS scope (the Command hat) ────────────────────────────────────────────────────────────────────────────────────────
        /// <summary>RTS left click: the unit under the cursor becomes the selection; empty ground clears it.</summary>
        public const string RtsSelect = "rts.select";
        /// <summary>RTS Shift + left click: the unit joins the selection.</summary>
        public const string RtsSelectAdd = "rts.select.add";
        /// <summary>RTS right click on ground (or on a unit that is not hostile): every selected unit moves there.</summary>
        public const string RtsMove = "rts.move";
        /// <summary>RTS right click on a hostile: every selected unit attacks it.</summary>
        public const string RtsAttack = "rts.attack";
        /// <summary>RTS Ctrl + right click on any unit: it is attacked whatever its side (CE-1132).</summary>
        public const string RtsAttackForce = "rts.attack.force";

        /// <summary>Whether <paramref name="entry"/> runs the action <paramref name="id"/> (an ordinal compare — no allocation).</summary>
        public static bool Is(in GestureEntry entry, string id) => entry.Action.IsCommand && string.Equals(entry.Action.CommandId, id, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐⭐ B-10 K7 — the map's gesture bindings, declared ONCE in the keymap: the MAP scope (selection and menu) and the RTS scope (the <see cref="Hat.Command"/> hat's —
    /// it declares only what it CHANGES; everything else falls through to the map scope by the existing first-match-wins rule).
    /// </summary>
    public static class MapGestureBindings
    {
        private const GestureTarget EntityOrHostile = GestureTarget.Entity | GestureTarget.Hostile;

        /// <summary>The scope qualifier of the RTS scope: the Command hat's name.</summary>
        public static readonly string RtsHat = HatArbiter.WordOf(Hat.Command);

        private static void Label(Keymap keymap, string id, string label) => keymap.SetLabel(ActionRef.Command(id), label);

        private static GestureEntry Map(GestureButton b, GestureKind k, GestureTarget t, string id, GestureModifiers m = GestureModifiers.None)
            => new(new MapGesture(b, k, m, t), InputScope.Map, null, ActionRef.Command(id));

        private static GestureEntry Rts(GestureButton b, GestureTarget t, string id, GestureModifiers m = GestureModifiers.None)
            => new(new MapGesture(b, GestureKind.Click, m, t), InputScope.Hat, RtsHat, ActionRef.Command(id));

        /// <summary>Declares the defaults into <paramref name="keymap"/> (idempotent): the gestures, their labels, and the unit menu's letters.</summary>
        public static void Declare(Keymap keymap)
        {
            DeclareGestures(keymap);
            Hrot.Common.Actions.MapMenuActions.DeclareKeys(keymap);   // ⭐ K6 — the map's menu letters are rows too (declared on the actions, K2)
        }

        private static void DeclareGestures(Keymap keymap)
        {
            Label(keymap, MapGestureIds.Select, "Select it alone"); Label(keymap, MapGestureIds.Clear, "Clear the selection"); Label(keymap, MapGestureIds.Band, "Box select");
            Label(keymap, MapGestureIds.SelectAdd, "Add to the selection"); Label(keymap, MapGestureIds.SelectToggle, "Toggle in the selection");
            Label(keymap, MapGestureIds.SelectCycle, "Select the next overlapped unit"); Label(keymap, MapGestureIds.BandAdd, "Box select, adding");
            Label(keymap, MapGestureIds.MenuSelection, "Menu for the selection"); Label(keymap, MapGestureIds.MenuEntity, "Select it and open its menu"); Label(keymap, MapGestureIds.MenuEmpty, "Menu for empty space");
            Label(keymap, MapGestureIds.RtsSelect, "RTS: select"); Label(keymap, MapGestureIds.RtsSelectAdd, "RTS: add to the selection"); Label(keymap, MapGestureIds.RtsMove, "RTS: move here");
            Label(keymap, MapGestureIds.RtsAttack, "RTS: attack"); Label(keymap, MapGestureIds.RtsAttackForce, "RTS: attack whatever is there");
            // ── the map scope: the selection rules (Action Catalogue §8) and "right click = the menu" ──────────────────────────────
            keymap.DeclareGesture(Map(GestureButton.Left, GestureKind.Click, GestureTarget.AnyEntity, MapGestureIds.Select));
            keymap.DeclareGesture(Map(GestureButton.Left, GestureKind.Click, GestureTarget.Ground, MapGestureIds.Clear));
            keymap.DeclareGesture(Map(GestureButton.Left, GestureKind.Drag, GestureTarget.Ground, MapGestureIds.Band));
            // ⭐ B-17 P5 (DESIGN_Map_Interaction §2.5 "multi-select") — the modifier rows; the most specific row wins (Keymap.TryFindGesture), so a plain click keeps its row above
            keymap.DeclareGesture(Map(GestureButton.Left, GestureKind.Click, GestureTarget.AnyEntity, MapGestureIds.SelectAdd, GestureModifiers.Shift));
            keymap.DeclareGesture(Map(GestureButton.Left, GestureKind.Click, GestureTarget.AnyEntity, MapGestureIds.SelectToggle, GestureModifiers.Ctrl));
            keymap.DeclareGesture(Map(GestureButton.Left, GestureKind.Click, GestureTarget.AnyEntity, MapGestureIds.SelectCycle, GestureModifiers.Alt));
            keymap.DeclareGesture(Map(GestureButton.Left, GestureKind.Drag, GestureTarget.Ground, MapGestureIds.BandAdd, GestureModifiers.Shift));
            keymap.DeclareGesture(Map(GestureButton.Right, GestureKind.Click, GestureTarget.Selected, MapGestureIds.MenuSelection));
            keymap.DeclareGesture(Map(GestureButton.Right, GestureKind.Click, EntityOrHostile, MapGestureIds.MenuEntity));
            keymap.DeclareGesture(Map(GestureButton.Right, GestureKind.Click, GestureTarget.Ground, MapGestureIds.MenuEmpty));

            // ── the RTS scope: the click means ORDER, not menu (R-4163) ────────────────────────────────────────────────────────────
            keymap.DeclareGesture(Rts(GestureButton.Left, GestureTarget.Any, MapGestureIds.RtsSelect));
            keymap.DeclareGesture(Rts(GestureButton.Left, GestureTarget.Any, MapGestureIds.RtsSelectAdd, GestureModifiers.Shift));
            keymap.DeclareGesture(Rts(GestureButton.Right, GestureTarget.Ground | GestureTarget.Entity | GestureTarget.Selected, MapGestureIds.RtsMove));
            keymap.DeclareGesture(Rts(GestureButton.Right, GestureTarget.Hostile, MapGestureIds.RtsAttack));
            keymap.DeclareGesture(Rts(GestureButton.Right, GestureTarget.AnyEntity, MapGestureIds.RtsAttackForce, GestureModifiers.Ctrl));
        }
    }

    /// <summary>
    /// ⭐⭐ B-10 K7 — resolves ONE map gesture through the scope chain: the active hat's scope IN FRONT of the map's (the same chain the keys use, Input §2.2).
    /// 🔒 R-4163: the RTS scope changes the meaning of a click by sitting in front of the map scope — the map's own rows are never copied, an unbound gesture falls through.
    /// ⭐ Hot path: index loops over the keymap, no allocation.
    /// </summary>
    public sealed class MapGestureRouter
    {
        private static MapGestureRouter? _shared;

        /// <summary>The process's router over the process's map keymap — what every map host resolves through (one declaration, one resolver).</summary>
        public static MapGestureRouter Shared
        {
            get
            {
                if (_shared != null) return _shared;
                var keymap = Keymap.Process;   // ⭐ B-10 K6 — the ONE keymap: the ImGui KeyRouter and the shortcuts window read it too
                MapGestureBindings.Declare(keymap);
                return _shared = new MapGestureRouter(keymap);
            }
        }

        /// <param name="keymap">The keymap that holds the map's gesture rows.</param>
        public MapGestureRouter(Keymap keymap) => Keymap = keymap ?? throw new ArgumentNullException(nameof(keymap));

        /// <summary>The keymap resolved against.</summary>
        public Keymap Keymap { get; }

        /// <summary>The first row for <paramref name="gesture"/>: the hat's scope, then the map's.</summary>
        public bool Resolve(Hat hat, in MapGesture gesture, out GestureEntry entry)
        {
            if (hat == Hat.Command && Keymap.TryFindGesture(InputScope.Hat, MapGestureBindings.RtsHat, in gesture, out entry)) return true;
            return Keymap.TryFindGesture(InputScope.Map, null, in gesture, out entry);
        }

        /// <summary>The map scope's row only (no hat in front) — what the selection system and the terminal's menu resolve.</summary>
        public bool ResolveMap(in MapGesture gesture, out GestureEntry entry) => Keymap.TryFindGesture(InputScope.Map, null, in gesture, out entry);
    }
}
