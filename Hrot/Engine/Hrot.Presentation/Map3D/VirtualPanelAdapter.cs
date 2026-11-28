using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Input;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Vis2D.Abstractions;
using NodeEditor.Core.Action;

namespace Hrot.UI.Common.Map3D
{
    /// <summary>
    /// ⭐⭐⭐ Simulated devices SD-S5v (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13e D-122 – D-131) — THE STATION'S VIRTUAL PANEL: the generic adapter
    /// (an <see cref="IDevicePanelIo"/>) that owns the mouse and the keys while a view is bound to a device, and writes the device ONLY through its own encoder —
    /// exactly as a physical replica's adapter would (D-130). It never knows a device type: it reads the bound view's kept HUD run (the chips' PanelButtons) and
    /// the type's descriptor (hotkeys, wheel roles).
    /// <list type="bullet">
    /// <item>⭐ <b>POINT</b> (the default, D-123): the cursor is FREE; the PanelButton under it is the HOVER (station-local — the station frames it); LMB (the Fire
    /// press row) CLICKS it (<see cref="DevicePanelClick"/>) — LMB on no button does nothing.</item>
    /// <item>⭐ <b>LOOK</b> while RMB is held (the Zoom hold row, the free camera's convention): the cursor is hidden and captured, the drag turns the look;
    /// released ⇒ POINT, the cursor back where it was. ⭐ The arrows turn the look in fine steps (D-124). A source that owns the look (a tracker, D-126) ⇒ neither.</item>
    /// <item>⭐ <b>Hotkeys</b> (D-124): the type's element keys, as rows of the type's own keymap place (<see cref="EmbodiedKeys.DeclareDeviceType"/>), innermost in
    /// the scope chain. <b>The wheel</b>: a notch presses the element whose <c>Wheel</c> role it is. <b>The pad</b> (S2a): A … RB channels 0–5, the sticks axes 0–3.</item>
    /// <item>⭐ D-141: beside an attached primary (a replica) it still clicks and presses — but a LATCHING chip is refused (D-135) and the look and axes are the
    /// primary's.</item>
    /// </list>
    /// ⭐ Pause (D-129): all of it works paused — what a press does then is the device's rule. 0 B per steady frame (a type's rows and actions built once).
    /// </summary>
    public sealed class VirtualPanelAdapter : IDevicePanelIo
    {
        private readonly Keymap _keymap;
        private readonly KeymapReader _keys, _deviceKeys;
        private readonly Dictionary<int, TypeKeys> _types = new();
        private TypeKeys? _type;
        private Vector2 _savedCursor;
        private bool _wasBound;

        private sealed class TypeKeys
        {
            public KeymapReader Reader = null!;
            public ActionRef[] Actions = Array.Empty<ActionRef>();
            public DeviceElementKind[] Kinds = Array.Empty<DeviceElementKind>();
            public int[] Indexes = Array.Empty<int>();
            public int WheelUp = -1, WheelDown = -1;
        }

        /// <param name="keymap">the station's keymap (the type's hotkey rows are declared into it).</param>
        /// <param name="keys">the Embodied hat's reader (LMB = Fire, RMB = Zoom, the mouse look, the wheel).</param>
        /// <param name="deviceKeys">the device place's reader (the pad, the arrows); the bound type's place becomes its inner place.</param>
        public VirtualPanelAdapter(Keymap keymap, KeymapReader keys, KeymapReader deviceKeys, int maxDevices = 8)
        {
            _keymap = keymap ?? throw new ArgumentNullException(nameof(keymap));
            _keys = keys ?? throw new ArgumentNullException(nameof(keys));
            _deviceKeys = deviceKeys ?? throw new ArgumentNullException(nameof(deviceKeys));
            Encoder = new DeviceInputEncoder(DeviceInputSource.Virtual, maxDevices);
        }

        public DeviceInputSource Source => DeviceInputSource.Virtual;
        public DeviceInputEncoder Encoder { get; }

        /// <summary>LOOK: RMB held — the cursor captured, the drag turns the look.</summary>
        public bool Looking { get; private set; }

        /// <summary>The hovered PanelButton (a copy) while one is under the free cursor, else null.</summary>
        public DebugPrimitive? Hovered { get; private set; }

        /// <summary>The hovered chip would be refused (a latching control another source owns, D-135).</summary>
        public bool HoverRefused { get; private set; }

        /// <summary>Clicks that hit a button / presses by hotkey / by the wheel (rails).</summary>
        public long Clicks { get; private set; }
        /// <inheritdoc cref="Clicks"/>
        public long HotkeyPresses { get; private set; }
        /// <inheritdoc cref="Clicks"/>
        public long WheelPresses { get; private set; }

        /// <summary>
        /// One input frame for the view's bound device <paramref name="bound"/> (null: no view shows a device — the adapter lets go). Stages the frame's presses into
        /// <see cref="Encoder"/> (the station flushes it with the look and <c>Viewed</c>) and returns the LOOK delta in mouse pixels the station turns its look by
        /// (zero in POINT). <paramref name="view"/> is the bound view's kept frame (its HUD run), <paramref name="viewSize"/> the view's size in pixels.
        /// </summary>
        public Vector2 Frame(EntityRepository w, IInputProvider? input, Entity bound, DevicePanelIoBinding binding, ReadOnlySpan<DebugPrimitive> view, Vector2 viewSize)
        {
            Hovered = null;
            HoverRefused = false;
            if (bound.IsNull)
            {
                if (Looking || _wasBound) { Looking = false; input?.SetCursorLocked(true); }   // the plain eye: the first-person lock again
                _wasBound = false;
                _deviceKeys.Inner = null;
                return Vector2.Zero;
            }
            _wasBound = true;
            var type = DeviceTypes.Of(w, bound);
            _type = TypeKeysOf(w, bound, type);
            _deviceKeys.Inner = _type?.Reader;
            bool lookOwned = binding.LookOwnedElsewhere(bound), primary = binding.VirtualIsPrimary(bound), latchLocked = binding.LatchingOwnedElsewhere(bound);

            // LOOK while RMB is held (⛔ not when a tracker owns the look), else POINT with the cursor free
            var look = Vector2.Zero;
            if (_keys.Held(EmbodiedKeys.Zoom) && !lookOwned)
            {
                if (!Looking && input != null) { _savedCursor = input.MousePosition; Looking = true; }
                look = new Vector2(_keys.Axis(EmbodiedKeys.LookX), _keys.Axis(EmbodiedKeys.LookY));
            }
            else if (Looking)
            {
                Looking = false;
                input?.SetCursorLocked(false);
                input?.SetMousePosition(_savedCursor);   // back where it was
            }
            input?.SetCursorLocked(Looking);
            if (!lookOwned) look += new Vector2(_deviceKeys.Axis(EmbodiedKeys.DeviceLookX), _deviceKeys.Axis(EmbodiedKeys.DeviceLookY));

            // POINT: the hover and the click
            if (!Looking && input != null)
            {
                var canvas = PanelButtons.ToCanvas(input.MousePosition, viewSize);
                int hover = PanelButtons.Hit(view, canvas);
                if (hover >= 0)
                {
                    Hovered = view[hover];
                    HoverRefused = latchLocked && (DeviceElementKind)view[hover].PanelKind == DeviceElementKind.Switch;
                }
                if (_keys.Pressed(EmbodiedKeys.Fire) && DevicePanelClick.Click(w, Encoder, bound, type, view, canvas, latchLocked, out bool refused) >= 0 && !refused) Clicks++;
            }

            // the type's hotkeys and its wheel roles — the SAME press a click makes (D-125)
            if (_type is { } tk)
            {
                for (int i = 0; i < tk.Actions.Length; i++)
                {
                    if (!tk.Reader.Pressed(tk.Actions[i])) continue;
                    if (tk.Kinds[i] == DeviceElementKind.Switch && latchLocked) continue;   // D-135 — the replica's switch
                    DevicePanelClick.Press(w, Encoder, bound, type, tk.Kinds[i], tk.Indexes[i]);
                    HotkeyPresses++;
                }
                float wheel = _keys.Axis(EmbodiedKeys.CameraDistance);
                int byWheel = wheel > 0f ? tk.WheelUp : wheel < 0f ? tk.WheelDown : -1;
                if (byWheel >= 0) { Encoder.Press(bound, byWheel); WheelPresses++; }
            }

            // ⭐ SD-S2a (§3.13c D-103) — the pad, through the device place's rows: A B X Y LB RB = buttons 0–5; the sticks = axes 0–3 (the primary's only)
            for (int b = 0; b < EmbodiedKeys.PadDeviceChannels; b++) if (_deviceKeys.Pressed(EmbodiedKeys.DeviceChannel(b))) Encoder.Press(bound, b);
            if (primary)
                for (int a = 0; a < DeviceAxes.Count; a++) Encoder.SetAxis(bound, a, _deviceKeys.Axis(EmbodiedKeys.DeviceAxis(a)));
            return look;
        }

        /// <summary>The bound type's hotkey place, actions and wheel roles — declared and built on the FIRST sight of the type (a user action), then reused.</summary>
        private TypeKeys? TypeKeysOf(EntityRepository w, Entity device, DeviceDefDto? type)
        {
            if (type is null || !w.HasComponent<DeviceInfo>(device)) return null;
            int typeId = (int)w.GetComponentRO<DeviceInfo>(device).DeviceType;
            if (_types.TryGetValue(typeId, out var known)) return known;
            string name = DeviceTypes.TemplateOf(w, device)?.Name is { Length: > 0 } n ? n : "device " + typeId;
            string hat = EmbodiedKeys.DeclareDeviceType(_keymap, typeId, name, type);
            var actions = new List<ActionRef>();
            var kinds = new List<DeviceElementKind>();
            var indexes = new List<int>();
            var tk = new TypeKeys { Reader = new KeymapReader(_keymap, InputScope.Hat, hat) { Input = _deviceKeys.Input } };
            Span<int> counts = stackalloc int[3];
            foreach (var e in type.Elements)
            {
                int index = counts[(int)e.Kind]++;
                if (e.Kind == DeviceElementKind.Button && e.Wheel == DeviceWheelRole.Up) tk.WheelUp = index;
                if (e.Kind == DeviceElementKind.Button && e.Wheel == DeviceWheelRole.Down) tk.WheelDown = index;
                if (e.Kind == DeviceElementKind.Axis || string.IsNullOrEmpty(e.Hotkey)) continue;
                actions.Add(EmbodiedKeys.DeviceElement(typeId, e.Kind, index));
                kinds.Add(e.Kind);
                indexes.Add(index);
            }
            tk.Actions = actions.ToArray();
            tk.Kinds = kinds.ToArray();
            tk.Indexes = indexes.ToArray();
            _types[typeId] = tk;
            return tk;
        }

        /// <summary>Points every reader of the type places at <paramref name="input"/> (the station's Bind, once per provider).</summary>
        public void Bind(IChordInput? input)
        {
            foreach (var t in _types.Values) t.Reader.Input = input;
        }
    }
}
