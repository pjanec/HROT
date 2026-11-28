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
        /// <summary>⭐ CE-4670 (§7d D-6) — a crewman's head out of his hatch / back in; the vehicle's light discipline (Normal → Convoy → Blackout → Off).</summary>
        public static readonly ActionRef HeadOut = Id("fps.vehicle.head-out");
        /// <inheritdoc cref="HeadOut"/>
        public static readonly ActionRef Lights = Id("fps.vehicle.lights");
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

        // ⭐ S1d-3 (devices §3.6 D-38, Q-5's lean) — the view's source and the bound device's own buttons (⛔ CE-1219's B hold: dropped, D-75)
        /// <summary>The next view source: the plain eye → each device he operates → the eye.</summary>
        public static readonly ActionRef ViewNext = Id("fps.view.next");
        /// <summary>The previous view source.</summary>
        public static readonly ActionRef ViewPrevious = Id("fps.view.previous");
        /// <summary>While a view shows a device: designate for his men at the crosshair (CE-1219's LMB designate, rehomed — LMB is the device's).</summary>
        public static readonly ActionRef Designate = Id("fps.designate");
        /// <summary>While a view shows a device: its button <c>n</c> — the device's channel n: 0 = LMB (the <see cref="Fire"/> press, read as the device's
        /// while a view shows one — one chord, one row), 1 = RMB; the wheel is 2 up / 3 down.</summary>
        public static ActionRef DeviceButton(int index) => index == 0 ? Fire : DeviceButton2;
        private static readonly ActionRef DeviceButton2 = Id("fps.device.button.2");

        // ⭐⭐ SD-S2a (devices §3.13c D-102 / D-103) — the DEVICE place of the Embodied hat (D-38's device scope): rows read only while a view shows a device,
        //   and while one does they SHADOW the body's rows on the same pad input (KeymapReader.Inner) — the stick flies the device, it does not also walk him.
        /// <summary>The device place's qualifier — rows of the focused view's device (<c>Embodied device</c>).</summary>
        public static readonly string DeviceHat = Hat + " device";
        /// <summary>The focused view's device's channel <c>n</c> (0–5 from the pad: A B X Y LB RB; the TKB names what each channel does).</summary>
        public static ActionRef DeviceChannel(int index) => DeviceChannelIds[index];
        private static readonly ActionRef[] DeviceChannelIds =
        {
            Id("fps.device.channel.0"), Id("fps.device.channel.1"), Id("fps.device.channel.2"),
            Id("fps.device.channel.3"), Id("fps.device.channel.4"), Id("fps.device.channel.5"),
        };
        /// <summary>The pad channels <see cref="DeviceChannel"/> covers.</summary>
        public const int PadDeviceChannels = 6;
        /// <summary>The focused view's device's axis <c>n</c> (<c>DeviceControl.Axes</c>): 0 / 1 the left stick X / up, 2 / 3 the right stick X / up.</summary>
        public static ActionRef DeviceAxis(int index) => DeviceAxisIds[index];
        private static readonly ActionRef[] DeviceAxisIds = { Id("fps.device.axis.0"), Id("fps.device.axis.1"), Id("fps.device.axis.2"), Id("fps.device.axis.3") };

        /// <summary>⭐ SD-S2a (D-103) — a full right-stick deflection turns the look as far as this many pixels of mouse movement per frame.</summary>
        public const float PadLookPixelsPerFrame = 12f;

        // ── Hold ─────────────────────────────────────────────────────────────────────────────────
        /// <summary>Run (the gait).</summary>
        public static readonly ActionRef Sprint = Id("fps.sprint");
        /// <summary>Crouch while held; letting go stands him back up.</summary>
        public static readonly ActionRef CrouchHeld = Id("fps.stance.crouch.held");
        /// <summary>The command wheel while held.</summary>
        public static readonly ActionRef CommandWheel = Id("fps.wheel");
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
            Press(EditorKey.H, HeadOut, "Head out of the hatch / back in");   // ⭐ CE-4670
            Press(EditorKey.U, Lights, "Vehicle lights: normal / convoy / blackout / off");   // ⭐ CE-4670
            Press(EditorKey.C, Crouch, "Crouch (toggle)");
            Press(EditorKey.Z, Prone, "Prone (toggle)");
            Press(EditorKey.N, MessageLog, "Message log");
            Press(EditorKey.O, Shoulder, "Over-the-shoulder view");
            Press(EditorKey.I, Select, "Select the unit under the crosshair");
            Press(EditorKey.MouseLeft, Fire, "Fire: lock the shot (through a device: its button 1)");
            Press(EditorKey.X, CancelShot, "Drop the locked shot");
            Press(EditorKey.K, KillCam, "Kill cam");
            Press(EditorKey.F, Use, "Use");
            for (int i = 0; i < 4; i++) Press(EditorKey.D1 + i, Weapon(i), "Weapon " + (i + 1));
            Press(EditorKey.B, ViewNext, "View: next source (eye, each device)");                       // ⭐ S1d-3
            Press(EditorKey.B, ViewPrevious, "View: previous source", KeyModifiers.Shift);
            Press(EditorKey.MouseMiddle, Designate, "Designate for his men (through a device)");
            Press(EditorKey.MouseRight, DeviceButton(1), "Device button 2 (through a device)");

            Hold(EditorKey.Unknown, Sprint, "Run", KeyModifiers.Shift);
            Hold(EditorKey.Unknown, CrouchHeld, "Crouch", KeyModifiers.Ctrl);
            Hold(EditorKey.T, CommandWheel, "Command wheel");
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

            // ⭐⭐ SD-S2a (devices §3.13c D-102 / D-103) — the GAMEPAD, as rows like every key (rebindable, listed in the Keyboard shortcuts window). Declared
            //   AFTER the keys so a HUD hint (ChordOf: the first row) still names the key. Sticks Y are −1 up, so forward / up take a scale of −1.
            Axis(EditorKey.PadLeftY, MoveForward, "Move forward / back", -1f);
            Axis(EditorKey.PadLeftX, MoveSide, "Move left / right", -1f);
            Axis(EditorKey.PadRightX, LookX, "Look left / right", PadLookPixelsPerFrame);
            Axis(EditorKey.PadRightY, LookY, "Look up / down", PadLookPixelsPerFrame);
            Axis(EditorKey.PadA, Climb, "Climb / descend", +1f);   // A: jump / climb (a flyer's up)
            Axis(EditorKey.PadB, Climb, "Climb / descend", -1f);
            Hold(EditorKey.PadTriggerL, Zoom, "Zoom");             // LT: aim
            Hold(EditorKey.PadTriggerR, Trigger, "Fire");          // RT: the trigger …
            Press(EditorKey.PadTriggerR, Fire, "Fire: lock the shot (through a device: its button 1)");   // … and its press (through a device: channel 0, as LMB)
            Press(EditorKey.PadA, Use, "Use");                     // A: use (tap) / the verb list (held)
            Hold(EditorKey.PadA, UseHeld, "Use (verb list)");
            Hold(EditorKey.PadLeftStick, Sprint, "Run");
            Press(EditorKey.PadRightStick, Crouch, "Crouch (toggle)");
            Press(EditorKey.PadY, NextRound, "Next round");
            Press(EditorKey.PadBack, ViewNext, "View: next source (eye, each device)");
            Press(EditorKey.PadStart, Map, "2-D map");
            Press(EditorKey.PadDpadRight, TeamNext, "Next man of the squad");
            Press(EditorKey.PadDpadLeft, TeamPrevious, "Previous man of the squad");
            Press(EditorKey.PadDpadUp, SeatNext, "Next seat");

            // the device place: the focused view's device (D-103) — the sticks are its axes, the face buttons and bumpers its channels 0–5
            var device = new RowDeclarer(keymap, InputScope.Hat, DeviceHat);
            device.Axis(EditorKey.PadLeftX, DeviceAxis(0), "Device axis 1 (left stick X)", 1f);
            device.Axis(EditorKey.PadLeftY, DeviceAxis(1), "Device axis 2 (left stick up)", -1f);
            device.Axis(EditorKey.PadRightX, DeviceAxis(2), "Device axis 3 (right stick X)", 1f);
            device.Axis(EditorKey.PadRightY, DeviceAxis(3), "Device axis 4 (right stick up)", -1f);
            EditorKey[] buttons = { EditorKey.PadA, EditorKey.PadB, EditorKey.PadX, EditorKey.PadY, EditorKey.PadLB, EditorKey.PadRB };
            for (int i = 0; i < buttons.Length; i++) device.Press(buttons[i], DeviceChannel(i), "Device button " + (i + 1));
        }
    }
}
