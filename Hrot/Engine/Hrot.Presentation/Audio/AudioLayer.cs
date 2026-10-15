using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Hrot.Core.Tkb;
using Raylib_cs;

namespace Hrot.UI.Common.Audio
{
    /// <summary>The audio layer's state (docs/designs/audio/DESIGN_FPS_Audio.md §1 state diagram).</summary>
    public enum AudioState : byte
    {
        /// <summary>No unit held: nothing runs, the device is never opened.</summary>
        Idle = 0,
        /// <summary>A unit is held and the device plays.</summary>
        Active = 1,
        /// <summary>A unit is held but this machine has no sound device (headless, CI, no card): FPS runs unchanged (AU-6).</summary>
        Silent = 2,
    }

    /// <summary>
    /// ⭐ CE-1120 (docs/designs/audio/DESIGN_FPS_Audio.md AU-3, §1) — the sound of the scene, ONLY while this map host's
    /// <see cref="Map3D.PossessionStation"/> holds a unit: the first time it does, the device opens and every sound type is defined (a file
    /// when its <c>SoundDto</c> names one, else the generated placeholder); the ear is the FPS camera; losing the unit stops every voice.
    /// Attached on every map host by <c>MapInteractionPack.AttachMapLayers</c> beside the station. Keys while in FPS: F6 / F7 volume
    /// down / up, F8 mute, F9 a left-then-right test click (the A0 check of which side is which).
    /// </summary>
    public sealed class AudioLayer : IMapLayer, IDisposable
    {
        /// <summary>Where sound files named by a <c>SoundDto</c> are looked up (beside the executable).</summary>
        public const string AssetFolder = "assets/audio";

        private readonly Func<EntityRepository?> _world;
        private readonly Func<ITkbDatabase?> _tkb;
        private readonly Func<Entity> _held;
        private readonly Func<Ear?> _ear;
        private readonly Func<IAudioOutput> _makeOutput;
        private readonly Func<KeyboardKey, bool> _keyPressed;
        private IAudioOutput? _output;
        private EntityRepository? _listening;
        private double _now;
        private float _volume = 0.8f;
        private bool _muted;
        private double _shownUntil;
        private int _testStep;
        private double _testAt;

        /// <param name="held">The unit this station holds (<c>PossessionStation.Unit</c>), Null when none.</param>
        /// <param name="ear">Where the FPS camera is and looks; null when there is no 3-D camera.</param>
        /// <param name="output">Makes the output on the first possess; default: raylib's when a window runs and
        /// <c>HROT_AUDIO</c> is not 0, else <see cref="NullAudioOutput"/> with no device.</param>
        /// <param name="keyPressed">The keyboard; default raylib's while a window runs.</param>
        public AudioLayer(Func<EntityRepository?> world, Func<ITkbDatabase?> tkb, Func<Entity> held, Func<Ear?> ear,
                          Func<IAudioOutput>? output = null, Func<KeyboardKey, bool>? keyPressed = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _tkb = tkb ?? throw new ArgumentNullException(nameof(tkb));
            _held = held ?? throw new ArgumentNullException(nameof(held));
            _ear = ear ?? throw new ArgumentNullException(nameof(ear));
            _makeOutput = output ?? DefaultOutput;
            _keyPressed = keyPressed ?? (k => Raylib.IsWindowReady() && Raylib.IsKeyPressed(k));
            Mixer = new SoundMixer();
            Sources = new SoundSources(tkb, Mixer);
        }

        /// <summary>⭐ CE-1120 — for the station: the layer reads the station's unit and its 3-D camera.</summary>
        public static AudioLayer For(Map3D.PossessionStation station, MapViewSwitch view, Func<EntityRepository?> world, Func<ITkbDatabase?> tkb)
            => new(world, tkb, () => station.InFps ? station.Unit : Entity.Null,
                   () => view.Is3D ? new Ear(view.Camera3D.Eye, view.Camera3D.Forward) : null);

        private static IAudioOutput DefaultOutput()
            => Environment.GetEnvironmentVariable("HROT_AUDIO") != "0" && Raylib.IsWindowReady()
                ? new RaylibAudioOutput()
                : new NullAudioOutput { Opens = false };

        public string Name => "Sound";
        public int LayerBitIndex => -1;

        public AudioState State { get; private set; }
        public SoundMixer Mixer { get; }
        public SoundSources Sources { get; }

        /// <summary>The output once made (rails read the null output's record).</summary>
        public IAudioOutput? Output => _output;

        /// <summary>The operator's volume (AU-9), 0…1, and mute — kept for the process, not the scenario.</summary>
        public float Volume => _volume;
        public bool Muted => _muted;

        public void Update(float dt)
        {
            _now += Math.Max(0f, dt);
            var w = _world();
            var unit = _held();
            if (w == null || unit.IsNull || !w.IsAlive(unit))
            {
                if (State != AudioState.Idle) Deactivate();
                return;
            }
            if (State == AudioState.Idle) Activate(w);
            if (State != AudioState.Active || _output == null || _ear() is not { } ear) return;
            Keys();
            Sources.Collect(w, unit, ear, _now, dt, _output);
            Test(ear);
            Mixer.Update(_output, _now, ear);
        }

        private void Activate(EntityRepository w)
        {
            _output ??= _makeOutput();
            if (!_output.IsOpen && !_output.Open()) { State = AudioState.Silent; return; }
            DefineClips();
            _output.MasterVolume = _muted ? 0f : _volume;
            SoundEvents.Listen(w);
            _listening = w;
            State = AudioState.Active;
        }

        private void Deactivate()
        {
            if (_output != null) Mixer.Clear(_output);
            if (_listening != null) SoundEvents.Release(_listening);
            _listening = null;
            Sources.Reset();
            State = AudioState.Idle;
        }

        private void DefineClips()
        {
            if (_output == null) return;
            var tkb = _tkb();
            foreach (var type in SoundTkbCatalog.BuiltIn.Keys)
            {
                if (_output.Has(type)) continue;
                var s = SoundTkbCatalog.SoundOf(tkb, type);
                if (s == null) continue;
                bool loaded = false;
                foreach (var file in s.Files)
                {
                    var path = System.IO.Path.Combine(AppContext.BaseDirectory, AssetFolder, file);
                    if (_output.DefineFile(type, path)) { loaded = true; break; }
                }
                if (!loaded && s.Synth != SoundSynth.None)
                    _output.Define(type, SoundSynthesizer.Make(s.Synth), SoundSynthesizer.SampleRate);
            }
        }

        private void Keys()
        {
            if (_output == null) return;
            bool changed = false;
            if (_keyPressed(KeyboardKey.F6)) { _volume = MathF.Max(0f, _volume - 0.1f); changed = true; }
            if (_keyPressed(KeyboardKey.F7)) { _volume = MathF.Min(1f, _volume + 0.1f); _muted = false; changed = true; }
            if (_keyPressed(KeyboardKey.F8)) { _muted = !_muted; changed = true; }
            if (_keyPressed(KeyboardKey.F9)) { _testStep = 1; _testAt = _now; }
            if (!changed) return;
            _output.MasterVolume = _muted ? 0f : _volume;
            _shownUntil = _now + 1.5;
        }

        /// <summary>A0 — a click 90° to the LEFT of the ear, then one to the RIGHT half a second later.</summary>
        private void Test(in Ear ear)
        {
            if (_testStep == 0) return;
            var fwd = new Vector3(ear.Forward.X, ear.Forward.Y, 0f);
            fwd = fwd.LengthSquared() > 1e-6f ? Vector3.Normalize(fwd) : Vector3.UnitY;
            var right = new Vector3(fwd.Y, -fwd.X, 0f);
            if (_testStep == 1)
            {
                Mixer.Request(SoundTkbCatalog.EmptyClick, ear.Position - right * 5f, true, 1f, 1f, SoundTkbCatalog.PriorityOwn, _now, 0.0);
                _testStep = 2;
            }
            else if (_now - _testAt >= 0.5)
            {
                Mixer.Request(SoundTkbCatalog.EmptyClick, ear.Position + right * 5f, true, 1f, 1f, SoundTkbCatalog.PriorityOwn, _now, 0.0);
                _testStep = 0;
            }
        }

        public void Draw(RenderContext ctx) { }

        /// <summary>The volume shown for a moment after a key.</summary>
        public void DrawOverlay(RenderContext ctx)
        {
            if (State == AudioState.Idle || _now > _shownUntil) return;
            string text = State == AudioState.Silent ? "Sound: no device" : _muted ? "Sound: muted" : $"Sound: {(int)MathF.Round(_volume * 100f)} %";
            Raylib.DrawText(text, 20, 20, 20, Color.RayWhite);
        }

        public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
        public Entity? PickEntity(Vector2 worldPos) => null;

        public void Dispose()
        {
            Deactivate();
            _output?.Dispose();
            _output = null;
        }
    }
}
