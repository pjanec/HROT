using Fdp.Toolkit.Input;
using NodeEditor.Core.Action;
using NodeEditor.Primitives;

namespace Hrot.UI.Common.Map3D
{
    /// <summary>
    /// ⭐⭐ B-10 K4 (📄 docs/UX/DESIGN_Input_And_Commands.md §6g) — the FPS station's input as ROWS of the keymap, in the Embodied hat's scope
    /// (<see cref="InputScope.Hat"/>, qualifier <see cref="Hat"/>). ⛔ SUPERSEDES the <c>KeyboardKey</c> constants read straight off the input provider (<c>MapKey</c>,
    /// <c>TeamSwitchKey</c> …): the key of every action is DATA here, and <see cref="FpsCameraDriver"/> / <see cref="FallScreen"/> read it through a <see cref="KeymapReader"/>.
    /// <para>Press rows are one-shot actions; Hold rows are state while held (sprint, the trigger, the zoom button, lean, the wheel, binoculars, the scoreboard, the use key);
    /// Axis rows are values (move, strafe, climb, the mouse look, the wheel's camera distance). The defaults are exactly today's keys.</para>
    /// </summary>
    public static class EmbodiedKeys
    {
        /// <summary>The hat's name — the rows' scope qualifier (<c>Embodied</c>).</summary>
        public static readonly string Hat = Hrot.ScenarioEditor.Map.HatArbiter.WordOf(Hrot.Common.Editing.Hat.Embodied);

        private static ActionRef Id(string id) => ActionRef.Command(id);

        // ── Press ────────────────────────────────────────────────────────────────────────────────
        /// <summary>Get out of the vehicle, else let go of the unit (also the fall screen's end).</summary>
        public static readonly ActionRef Release = Id("fps.release");
        /// <summary>The 2-D map centred on him, and back.</summary>
        public static readonly ActionRef Map = Id("fps.map");
        /// <summary>The next man of his squad (also the fall screen's "a squad mate").</summary>
        public static readonly ActionRef TeamNext = Id("fps.team.next");
        /// <summary>The previous man of his squad.</summary>
        public static readonly ActionRef TeamPrevious = Id("fps.team.previous");
        /// <summary>The next seat of the vehicle.</summary>
        public static readonly ActionRef SeatNext = Id("fps.seat.next");
        /// <summary>Lock the view to the hull (a vehicle, a flyer) / free a person's look from his body.</summary>
        public static readonly ActionRef LookLock = Id("fps.look.lock");
        /// <summary>The next round of the selected weapon.</summary>
        public static readonly ActionRef NextRound = Id("fps.round.next");
        /// <summary>Switch the weapon between SIGHT and LOCK.</summary>
        public static readonly ActionRef FireMode = Id("fps.fire.mode");
        /// <summary>Toggle crouched.</summary>
        public static readonly ActionRef Crouch = Id("fps.stance.crouch");
        /// <summary>Toggle prone.</summary>
        public static readonly ActionRef Prone = Id("fps.stance.prone");
        /// <summary>Open / close his message log.</summary>
        public static readonly ActionRef MessageLog = Id("fps.messagelog");
        /// <summary>Toggle the over-the-shoulder view.</summary>
        public static readonly ActionRef Shoulder = Id("fps.shoulder");
        /// <summary>Select the unit under the crosshair.</summary>
        public static readonly ActionRef Select = Id("fps.select");
        /// <summary>The use key: a press fixes the target; a tap sends the primary verb, held (<see cref="UseHeld"/>) it opens the list.</summary>
        public static readonly ActionRef Use = Id("fps.use");
        /// <summary>Fire: a press locks one shot on what the crosshair rests on (the trigger itself is <see cref="Trigger"/>).</summary>
        public static readonly ActionRef Fire = Id("fps.fire");
        /// <summary>Drop the locked shot.</summary>
        public static readonly ActionRef CancelShot = Id("fps.fire.cancel");
        /// <summary>The kill cam, on the fall screen.</summary>
        public static readonly ActionRef KillCam = Id("fps.killcam");
        /// <summary>Pick weapon <c>n</c> (1–4) — and a verb of the open use list.</summary>
        public static ActionRef Weapon(int index) => WeaponIds[index];
        private static readonly ActionRef[] WeaponIds = { Id("fps.weapon.1"), Id("fps.weapon.2"), Id("fps.weapon.3"), Id("fps.weapon.4") };

        // ── Hold ─────────────────────────────────────────────────────────────────────────────────
        /// <summary>Run (the gait).</summary>
        public static readonly ActionRef Sprint = Id("fps.sprint");
        /// <summary>Crouch while held; letting go stands him back up.</summary>
        public static readonly ActionRef CrouchHeld = Id("fps.stance.crouch.held");
        /// <summary>The command wheel while held.</summary>
        public static readonly ActionRef CommandWheel = Id("fps.wheel");
        /// <summary>The binoculars while held.</summary>
        public static readonly ActionRef Binoculars = Id("fps.binoculars");
        /// <summary>Lean left while held.</summary>
        public static readonly ActionRef LeanLeft = Id("fps.lean.left");
        /// <summary>Lean right while held.</summary>
        public static readonly ActionRef LeanRight = Id("fps.lean.right");
        /// <summary>The scoreboard while held.</summary>
        public static readonly ActionRef Scoreboard = Id("fps.scoreboard");
        /// <summary>The zoom (sights / scope) while held.</summary>
        public static readonly ActionRef Zoom = Id("fps.zoom");
        /// <summary>The trigger while held.</summary>
        public static readonly ActionRef Trigger = Id("fps.trigger");
        /// <summary>The use key's HOLD: held it opens the verb list, letting go sends (the press is <see cref="Use"/>).</summary>
        public static readonly ActionRef UseHeld = Id("fps.use.held");

        // ── Axis ─────────────────────────────────────────────────────────────────────────────────
        /// <summary>Forward (+) / back (−).</summary>
        public static readonly ActionRef MoveForward = Id("fps.move.forward");
        /// <summary>Left (+) / right (−): strafe, turn or steer by what he is.</summary>
        public static readonly ActionRef MoveSide = Id("fps.move.side");
        /// <summary>Up (+) / down (−): a flyer's climb.</summary>
        public static readonly ActionRef Climb = Id("fps.climb");
        /// <summary>The mouse look along X.</summary>
        public static readonly ActionRef LookX = Id("fps.look.x");
        /// <summary>The mouse look along Y.</summary>
        public static readonly ActionRef LookY = Id("fps.look.y");
        /// <summary>The wheel: the camera's distance (and the use list's verb).</summary>
        public static readonly ActionRef CameraDistance = Id("fps.camera.distance");

        /// <summary>Declares the Embodied hat's rows into <paramref name="keymap"/> (idempotent) — today's keys, with their labels for the Keyboard shortcuts window.</summary>
        public static void Declare(Keymap keymap)
        {
            var rows = new RowDeclarer(keymap, InputScope.Hat, Hat);
            void Press(EditorKey key, ActionRef a, string label, KeyModifiers m = KeyModifiers.None) => rows.Press(key, a, label, m);
            void Hold(EditorKey key, ActionRef a, string label, KeyModifiers m = KeyModifiers.None) => rows.Hold(key, a, label, m);
            void Axis(EditorKey key, ActionRef a, string label, float scale) => rows.Axis(key, a, label, scale);

            Press(EditorKey.Escape, Release, "Get out / let go of the unit");
            Press(EditorKey.M, Map, "2-D map");
            Press(EditorKey.J, TeamNext, "Next man of the squad");
            Press(EditorKey.J, TeamPrevious, "Previous man of the squad", KeyModifiers.Shift);
            Press(EditorKey.G, SeatNext, "Next seat");
            Press(EditorKey.V, LookLock, "Lock the view to the hull / free the look");
            Press(EditorKey.R, NextRound, "Next round");
            Press(EditorKey.L, FireMode, "Sight / lock fire mode");
            Press(EditorKey.C, Crouch, "Crouch (toggle)");
            Press(EditorKey.Z, Prone, "Prone (toggle)");
            Press(EditorKey.N, MessageLog, "Message log");
            Press(EditorKey.O, Shoulder, "Over-the-shoulder view");
            Press(EditorKey.I, Select, "Select the unit under the crosshair");
            Press(EditorKey.MouseLeft, Fire, "Fire: lock the shot");
            Press(EditorKey.X, CancelShot, "Drop the locked shot");
            Press(EditorKey.K, KillCam, "Kill cam");
            Press(EditorKey.F, Use, "Use");
            for (int i = 0; i < 4; i++) Press(EditorKey.D1 + i, Weapon(i), "Weapon " + (i + 1));

            Hold(EditorKey.Unknown, Sprint, "Run", KeyModifiers.Shift);
            Hold(EditorKey.Unknown, CrouchHeld, "Crouch", KeyModifiers.Ctrl);
            Hold(EditorKey.T, CommandWheel, "Command wheel");
            Hold(EditorKey.B, Binoculars, "Binoculars");
            Hold(EditorKey.Q, LeanLeft, "Lean left");
            Hold(EditorKey.E, LeanRight, "Lean right");
            Hold(EditorKey.Tab, Scoreboard, "Scoreboard");
            Hold(EditorKey.MouseRight, Zoom, "Zoom");
            Hold(EditorKey.MouseLeft, Trigger, "Fire");
            Hold(EditorKey.F, UseHeld, "Use (verb list)");

            Axis(EditorKey.W, MoveForward, "Move forward / back", +1f);
            Axis(EditorKey.S, MoveForward, "Move forward / back", -1f);
            Axis(EditorKey.A, MoveSide, "Move left / right", +1f);
            Axis(EditorKey.D, MoveSide, "Move left / right", -1f);
            Axis(EditorKey.Q, Climb, "Climb / descend", +1f);
            Axis(EditorKey.Space, Climb, "Climb / descend", +1f);
            Axis(EditorKey.Z, Climb, "Climb / descend", -1f);
            Axis(EditorKey.C, Climb, "Climb / descend", -1f);
            Axis(EditorKey.MouseDeltaX, LookX, "Look left / right (mouse)", 1f);
            Axis(EditorKey.MouseDeltaY, LookY, "Look up / down (mouse)", 1f);
            Axis(EditorKey.MouseWheel, CameraDistance, "Camera distance (wheel)", 1f);
        }
    }
}
