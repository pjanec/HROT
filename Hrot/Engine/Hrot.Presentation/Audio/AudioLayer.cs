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
    /// <see cref="Map3D.PossessionStation"/> holds a unit (⭐ CE-1137 — or, when the operator turned on Settings ▸ Sound in the 3-D view,
    /// in the 3-D view from the camera): the first time it does, the device opens and every sound type is defined (a file
    /// when its <c>SoundDto</c> names one, else the generated placeholder); the ear is the FPS camera; losing the unit stops every voice.
    /// Attached on every map host by <c>MapInteractionPack.AttachMapLayers</c> beside the station. Keys while in FPS: F6 / F7 volume
    /// down / up, F8 mute, F9 a left-then-right test click (the A0 check of which side is which), F10 the sound panel (↑ ↓ pick master or
    /// a category, ← → change it). The volumes are the operator's (<see cref="AudioSettings"/>, AU-9), loaded on the first possess and
    /// saved on every change.
    /// </summary>
    public sealed class AudioLayer : IMapLayer, IDisposable
    {
        /// <summary>Where sound files named by a <c>SoundDto</c> are looked up (beside the executable).</summary>
        public const string AssetFolder = "assets/audio";

        private readonly Func<EntityRepository?> _world;
        private readonly Func<ITkbDatabase?> _tkb;
        private readonly Func<Entity> _held;
        private readonly Func<Ear?> _ear;
        private readonly Func<bool> _cameraSound;   // ⭐ CE-1137 — no unit held, yet the operator wants the scene heard from the camera
        private readonly Func<IAudioOutput> _makeOutput;
        private readonly Func<KeyboardKey, bool> _keyPressed;
        private IAudioOutput? _output;
        private EntityRepository? _listening;
        private double _now;
        private readonly Func<string?> _settingsPath;
        private AudioSettings? _settings;
        private int _panelRow;
        private double _shownUntil;
        private int _testStep;
        private double _testAt;

        /// <param name="held">The unit this station holds (<c>PossessionStation.Unit</c>), Null when none.</param>
        /// <param name="ear">Where the FPS camera is and looks; null when there is no 3-D camera.</param>
        /// <param name="output">Makes the output on the first possess; default: raylib's when a window runs and
        /// <c>HROT_AUDIO</c> is not 0, else <see cref="NullAudioOutput"/> with no device.</param>
        /// <param name="keyPressed">The keyboard; default raylib's while a window runs.</param>
        /// <param name="settingsPath">Where the operator's settings file is; null (the default) keeps them for the process only.</param>
        public AudioLayer(Func<EntityRepository?> world, Func<ITkbDatabase?> tkb, Func<Entity> held, Func<Ear?> ear,
                          Func<IAudioOutput>? output = null, Func<KeyboardKey, bool>? keyPressed = null, Func<string?>? settingsPath = null,
                          Func<bool>? cameraSound = null)
        {
            _cameraSound = cameraSound ?? (() => false);
            _settingsPath = settingsPath ?? (() => null);
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
                   () => view.Is3D ? new Ear(view.Camera3D.Eye, view.Camera3D.Forward) : Ear2D(view),
                   settingsPath: AudioSettings.DefaultPath,
                   // ⭐ CE-1137 — Settings ▸ Sound; ⭐ CE-4038 B11 (R-4059) — in the 2-D view too, and a film take's switch over it
                   cameraSound: () => AudioDirector.CameraSound);

        /// <summary>
        /// ⭐ CE-4038 B11 (docs/DESIGN_Film_Director.md §4c, D-14; R-4059) — the 2-D map's ear: at the view's centre, as high as half the visible
        /// width, so a wide view hears only the loud things (the same spatializer, no new rule). The map is north-up (CE-1040): the ear looks
        /// north, so east is to its right.
        /// </summary>
        public static Ear Ear2D(MapViewSwitch view)
        {
            var pose = view.CurrentPose;   // the overhead pose that shows what the 2-D view shows
            float aspect = Raylib.IsWindowReady() && Raylib.GetScreenHeight() > 0 ? Raylib.GetScreenWidth() / (float)Raylib.GetScreenHeight() : 16f / 9f;
            float halfWidth = pose.Distance * MathF.Tan(view.Camera3D.FovyDegrees * MathF.PI / 360f) * aspect;
            return new Ear(pose.LookAt + new Vector3(0f, 0f, halfWidth), Vector3.UnitY);
        }

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

        /// <summary>The operator's settings (AU-9) — loaded on the first possess.</summary>
        public AudioSettings Settings => _settings ??= LoadSettings();

        /// <summary>The operator's master volume, 0…1, and mute.</summary>
        public float Volume => Settings.Master;
        public bool Muted => Settings.Muted;

        /// <summary>The sound panel (F10) is open; its selected row (0 master, then each <c>SoundCategory</c>).</summary>
        public bool PanelOpen { get; private set; }
        public int PanelRow => _panelRow;

        public void Update(float dt)
        {
            _now += Math.Max(0f, dt);
            var w = _world();
            var unit = _held();
            // ⭐ CE-1137 — with no unit held, the camera may still listen (Settings ▸ Sound in the 3-D view): no "own" sounds then
            bool camera = (unit.IsNull || w == null || !w.IsAlive(unit)) && _cameraSound();
            if (camera) unit = Entity.Null;
            if (w == null || (!camera && (unit.IsNull || !w.IsAlive(unit))))
            {
                if (State != AudioState.Idle) Deactivate();
                return;
            }
            if (State == AudioState.Idle) Activate(w);
            Record(w);
            if (State != AudioState.Active || _output == null || _ear() is not { } ear) return;
            Keys();
            ApplySettings();   // ⭐ CE-4038 B11 — POST /audio changes the shared settings; they reach the output on the next frame
            // ⭐ CE-1305 TE-11 (§11v A2) — the director's playback rate rules what is heard; ⛔ not while a film is captured: then every sound is logged at its
            //   natural level and the offline track applies the same rule per pass (a captured frame takes ~0.8 s of wall time, so live hearing means nothing)
            Mixer.Rate = AudioDirector.Recorder != null ? 1d : AudioDirector.Rate ?? 1d;
            Sources.Collect(w, unit, ear, _now, dt, _output);
            Test(ear);
            Mixer.Update(_output, _now, ear);
        }

        private void Activate(EntityRepository w)
        {
            _output ??= _makeOutput();
            if (!_output.IsOpen && !_output.Open()) { State = AudioState.Silent; return; }
            DefineClips();
            ApplySettings();
            SoundEvents.Listen(w);
            _listening = w;
            State = AudioState.Active;
        }

        /// <summary>
        /// ⭐ CE-4038 B11 (§4c) — while the director captures a film, every sound goes through a <see cref="RecordingAudioOutput"/> into its
        /// <see cref="SoundTrack"/>; with no device (a cloud VM) the scene is still heard by the track, through an output that plays nothing.
        /// </summary>
        private void Record(EntityRepository w)
        {
            if (AudioDirector.Recorder is { } track)
            {
                track.CategoryOf ??= clip => SoundTkbCatalog.SoundOf(_tkb(), clip)?.Category;   // ⭐ CE-1305 TE-11 — the track's rule needs each cue's kind
                if (_output is RecordingAudioOutput r && ReferenceEquals(r.Track, track)) return;
                if (State == AudioState.Idle) return;
                if (_output != null) Mixer.Clear(_output);
                var inner = _output is RecordingAudioOutput old ? old.Inner : _output;
                if (inner == null || !inner.IsOpen) { inner = new NullAudioOutput(); inner.Open(); }
                _output = new RecordingAudioOutput(inner, track);
                DefineClips();
                ApplySettings();
                if (State == AudioState.Silent) { SoundEvents.Listen(w); _listening = w; State = AudioState.Active; }
            }
            else if (_output is RecordingAudioOutput rec)
            {
                Mixer.Clear(_output);
                _output = rec.Inner;
                if (!_output.IsOpen || _output is NullAudioOutput) { Deactivate(); State = AudioState.Silent; }
            }
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
                if (_output.Has(type) && (_output is not RecordingAudioOutput r || r.Track.HasClip(type))) continue;
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
            // ⭐ CE-2372 (AQ105 F) — the voice lines: a recording named in the line's TKB sound, else its words shouted in the speaker's voice
            foreach (var (type, line) in VoiceTkbCatalog.All)
            {
                if (_output.Has(type) && (_output is not RecordingAudioOutput r || r.Track.HasClip(type))) continue;
                var s = VoiceTkbCatalog.SoundOf(tkb, type);
                if (s == null) continue;
                bool loaded = false;
                foreach (var file in s.Files)
                {
                    var path = System.IO.Path.Combine(AppContext.BaseDirectory, AssetFolder, file);
                    if (_output.DefineFile(type, path)) { loaded = true; break; }
                }
                var voice = VoiceTkbCatalog.Voices[line.Voice];
                if (!loaded) _output.Define(type, SoundSynthesizer.Voice(line.Text, voice.PitchHz, voice.Formant), SoundSynthesizer.SampleRate);
            }
        }

        private void Keys()
        {
            if (_output == null) return;
            var s = Settings;
            bool changed = false;
            if (_keyPressed(KeyboardKey.F6)) { s.Master = Step(s.Master, -1); changed = true; }
            if (_keyPressed(KeyboardKey.F7)) { s.Master = Step(s.Master, +1); s.Muted = false; changed = true; }
            if (_keyPressed(KeyboardKey.F8)) { s.Muted = !s.Muted; changed = true; }
            if (_keyPressed(KeyboardKey.F9)) { _testStep = 1; _testAt = _now; }
            if (_keyPressed(KeyboardKey.F10)) { PanelOpen = !PanelOpen; _shownUntil = _now + 1.5; }
            if (PanelOpen)
            {
                int rows = 1 + AudioSettings.CategoryCount;
                if (_keyPressed(KeyboardKey.Down)) _panelRow = (_panelRow + 1) % rows;
                if (_keyPressed(KeyboardKey.Up)) _panelRow = (_panelRow + rows - 1) % rows;
                int dir = _keyPressed(KeyboardKey.Right) ? 1 : _keyPressed(KeyboardKey.Left) ? -1 : 0;
                if (dir != 0)
                {
                    if (_panelRow == 0) { s.Master = Step(s.Master, dir); if (dir > 0) s.Muted = false; }
                    else s.Categories[_panelRow - 1] = Step(s.Categories[_panelRow - 1], dir);
                    changed = true;
                }
            }
            if (!changed) return;
            ApplySettings();
            s.Save(_settingsPath());
            _shownUntil = _now + 1.5;
        }

        private static float Step(float v, int dir) => MathF.Round(Math.Clamp(v + 0.1f * dir, 0f, 1f) * 10f) / 10f;

        private AudioSettings LoadSettings()
        {
            string? path = null;
            try { path = _settingsPath(); } catch (Exception) { }
            return AudioPreferences.For(path);   // ⭐ CE-1137 — one object per file, shared with the Settings menu
        }

        /// <summary>The settings into the output (master, mute) and the mixer (per category).</summary>
        private void ApplySettings()
        {
            var s = Settings;
            if (_output != null) _output.MasterVolume = s.Muted ? 0f : s.Master;
            for (int i = 0; i < Mixer.CategoryVolume.Length && i < s.Categories.Length; i++) Mixer.CategoryVolume[i] = s.Categories[i];
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

        /// <summary>The volume shown for a moment after a key; the sound panel while it is open.</summary>
        public void DrawOverlay(RenderContext ctx)
        {
            if (State == AudioState.Idle) return;
            if (PanelOpen && State == AudioState.Active) { DrawPanel(); return; }
            if (_now > _shownUntil) return;
            var s = Settings;
            string text = State == AudioState.Silent ? "Sound: no device" : s.Muted ? "Sound: muted" : $"Sound: {(int)MathF.Round(s.Master * 100f)} %";
            Raylib.DrawText(text, 20, 20, 20, Color.RayWhite);
        }

        /// <summary>The panel's rows: master, then each category — "Name  80 %".</summary>
        public System.Collections.Generic.IReadOnlyList<string> PanelRows()
        {
            var s = Settings;
            var rows = new System.Collections.Generic.List<string> { s.Muted ? "Master  muted" : $"Master  {(int)MathF.Round(s.Master * 100f)} %" };
            var names = Enum.GetNames<SoundCategory>();
            for (int i = 0; i < names.Length && i < s.Categories.Length; i++) rows.Add($"{names[i]}  {(int)MathF.Round(s.Categories[i] * 100f)} %");
            return rows;
        }

        private void DrawPanel()
        {
            var rows = PanelRows();
            const int x = 20, y = 20, w = 260, line = 24;
            Raylib.DrawRectangle(x - 8, y - 8, w, line * (rows.Count + 1) + 12, new Color(0, 0, 0, 170));
            Raylib.DrawText("Sound (F10)  arrows change", x, y, 18, Color.LightGray);
            for (int i = 0; i < rows.Count; i++)
                Raylib.DrawText((i == _panelRow ? "> " : "  ") + rows[i], x, y + line * (i + 1), 20, i == _panelRow ? Color.Yellow : Color.RayWhite);
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
