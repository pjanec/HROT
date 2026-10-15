using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace Hrot.UI.Common.Audio
{
    /// <summary>
    /// ⭐ CE-1120 (docs/designs/audio/DESIGN_FPS_Audio.md AU-1, AU-6) — THE seam to a sound library: clips by a key, one-shot and looping
    /// voices with a gain (0…1), a pan (−1 left … +1 right) and a pitch (1 = as recorded). Everything that decides WHAT plays and HOW LOUD
    /// lives above it (pure, railed through <see cref="NullAudioOutput"/>); swapping raylib for OpenAL (A4) replaces one class.
    /// </summary>
    public interface IAudioOutput : IDisposable
    {
        /// <summary>Opens the device; false ⇒ no sound on this machine (the layer goes <c>Silent</c>, FPS unchanged).</summary>
        bool Open();
        bool IsOpen { get; }

        /// <summary>A clip from mono 16-bit samples.</summary>
        void Define(long clip, short[] samples, int sampleRate);

        /// <summary>A clip from a file (OGG / WAV); false when it cannot be read.</summary>
        bool DefineFile(long clip, string path);

        bool Has(long clip);

        /// <summary>Plays <paramref name="clip"/> once; the voice id, or −1.</summary>
        int Play(long clip, float gain, float pan, float pitch);

        /// <summary>Plays <paramref name="clip"/> round and round; the voice id, or −1.</summary>
        int Loop(long clip, float gain, float pan, float pitch);

        void Set(int voice, float gain, float pan, float pitch);
        bool IsPlaying(int voice);
        void Stop(int voice);
        void StopAll();

        /// <summary>Once a frame: feeds the loops, frees the finished one-shots.</summary>
        void Tick();

        /// <summary>The operator's master volume, 0…1.</summary>
        float MasterVolume { get; set; }
    }

    /// <summary>
    /// ⭐ CE-1120 (AU-6) — no device: nothing is heard, everything is RECORDED — the rails read <see cref="Played"/> and
    /// <see cref="Looping"/>. Also what a headless host or a machine with no sound card gets when asked (<see cref="Opens"/> = false).
    /// </summary>
    public sealed class NullAudioOutput : IAudioOutput
    {
        public sealed record Voice(int Id, long Clip, bool Loop)
        {
            public float Gain { get; set; }
            public float Pan { get; set; }
            public float Pitch { get; set; }
            public bool Playing { get; set; } = true;
        }

        private readonly Dictionary<long, int> _clips = new();
        private readonly Dictionary<int, Voice> _voices = new();
        private int _next = 1;

        /// <summary>What <see cref="Open"/> answers (a rail sets false to stand for a machine with no sound card).</summary>
        public bool Opens { get; init; } = true;

        /// <summary>Every one-shot played, in order (rails).</summary>
        public List<Voice> Played { get; } = new();

        public IEnumerable<Voice> Looping { get { foreach (var v in _voices.Values) if (v.Loop && v.Playing) yield return v; } }

        /// <summary>How long a one-shot "plays" (Tick calls) before it is finished — rails make a voice end.</summary>
        public int OneShotTicks { get; set; } = 30;

        private readonly Dictionary<int, int> _ticks = new();

        public bool IsOpen { get; private set; }
        public float MasterVolume { get; set; } = 1f;
        public bool Open() => IsOpen = Opens;
        public void Define(long clip, short[] samples, int sampleRate) => _clips[clip] = samples.Length;
        public bool DefineFile(long clip, string path) { _clips[clip] = 0; return true; }
        public bool Has(long clip) => _clips.ContainsKey(clip);
        public int SampleCount(long clip) => _clips.TryGetValue(clip, out var n) ? n : 0;

        public int Play(long clip, float gain, float pan, float pitch)
        {
            if (!IsOpen || !_clips.ContainsKey(clip)) return -1;
            var v = new Voice(_next++, clip, false) { Gain = gain, Pan = pan, Pitch = pitch };
            _voices[v.Id] = v;
            _ticks[v.Id] = 0;
            Played.Add(v);
            return v.Id;
        }

        public int Loop(long clip, float gain, float pan, float pitch)
        {
            if (!IsOpen || !_clips.ContainsKey(clip)) return -1;
            var v = new Voice(_next++, clip, true) { Gain = gain, Pan = pan, Pitch = pitch };
            _voices[v.Id] = v;
            return v.Id;
        }

        public void Set(int voice, float gain, float pan, float pitch)
        {
            if (_voices.TryGetValue(voice, out var v)) { v.Gain = gain; v.Pan = pan; v.Pitch = pitch; }
        }

        public bool IsPlaying(int voice) => _voices.TryGetValue(voice, out var v) && v.Playing;
        public void Stop(int voice) { if (_voices.TryGetValue(voice, out var v)) v.Playing = false; }
        public void StopAll() { foreach (var v in _voices.Values) v.Playing = false; }

        public void Tick()
        {
            foreach (var v in _voices.Values)
                if (!v.Loop && v.Playing && ++_ticks[v.Id] >= OneShotTicks) v.Playing = false;
        }

        public void Dispose() => IsOpen = false;
    }

    /// <summary>
    /// ⭐ CE-1120 (AU-1) — raylib's audio (raudio = miniaudio, inside the raylib native library on Windows and Linux): a one-shot is an
    /// ALIAS of the clip's sound (each its own gain / pan / pitch, freed when it ends); a loop is a MUSIC stream read from the clip's WAV
    /// bytes, kept in native memory for as long as the clip lives (the stream reads it while playing). ⚠ raylib's pan is 0…1 with 0.5 the
    /// centre; which end is LEFT is set by <see cref="PanOneIsLeft"/> — verified by ear (A0, the test key); <c>HROT_AUDIO_PAN_FLIP=1</c>
    /// flips it without a build.
    /// </summary>
    public sealed unsafe class RaylibAudioOutput : IAudioOutput
    {
        /// <summary>raudio 5.x mixes the LEFT channel at the buffer's pan (raudio.c, MixAudioFrames) ⇒ pan 1 is full left.</summary>
        public static readonly bool PanOneIsLeft = Environment.GetEnvironmentVariable("HROT_AUDIO_PAN_FLIP") != "1";

        private readonly Dictionary<long, Sound> _sounds = new();
        private readonly Dictionary<long, (IntPtr Data, int Size)> _wav = new();
        private readonly Dictionary<int, Sound> _shots = new();
        private readonly Dictionary<int, Music> _loops = new();
        private readonly List<int> _done = new();
        private int _next = 1;
        private float _master = 1f;

        public bool IsOpen { get; private set; }

        public float MasterVolume
        {
            get => _master;
            set { _master = Math.Clamp(value, 0f, 1f); if (IsOpen) Raylib.SetMasterVolume(_master); }
        }

        public bool Open()
        {
            if (IsOpen) return true;
            try
            {
                Raylib.InitAudioDevice();
                IsOpen = Raylib.IsAudioDeviceReady();
                if (IsOpen) Raylib.SetMasterVolume(_master);
            }
            catch (Exception) { IsOpen = false; }   // a missing native backend must never stop the host (AU-6)
            return IsOpen;
        }

        public void Define(long clip, short[] samples, int sampleRate)
        {
            if (!IsOpen || samples.Length == 0) return;
            fixed (short* p = samples)
            {
                var wave = new Wave { SampleCount = (uint)samples.Length, SampleRate = (uint)sampleRate, SampleSize = 16, Channels = 1, Data = p };
                Replace(clip, Raylib.LoadSoundFromWave(wave));   // the sound copies the samples
            }
            KeepWav(clip, SoundSynthesizer.Wav(samples, sampleRate));
        }

        public bool DefineFile(long clip, string path)
        {
            if (!IsOpen || !File.Exists(path)) return false;
            var sound = Raylib.LoadSound(path);
            if (sound.FrameCount == 0) return false;
            Replace(clip, sound);
            KeepWav(clip, File.ReadAllBytes(path), Path.GetExtension(path));
            return true;
        }

        private readonly Dictionary<long, string> _ext = new();

        private void Replace(long clip, Sound sound)
        {
            if (_sounds.TryGetValue(clip, out var old)) Raylib.UnloadSound(old);
            _sounds[clip] = sound;
        }

        private void KeepWav(long clip, byte[] bytes, string ext = ".wav")
        {
            if (_wav.TryGetValue(clip, out var old)) Marshal.FreeHGlobal(old.Data);
            var data = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, data, bytes.Length);
            _wav[clip] = (data, bytes.Length);
            _ext[clip] = ext.ToLowerInvariant();
        }

        public bool Has(long clip) => _sounds.ContainsKey(clip);

        private float Pan(float pan)
        {
            float p = 0.5f + 0.5f * Math.Clamp(pan, -1f, 1f);   // 0 left … 1 right
            return PanOneIsLeft ? 1f - p : p;
        }

        public int Play(long clip, float gain, float pan, float pitch)
        {
            if (!IsOpen || !_sounds.TryGetValue(clip, out var sound)) return -1;
            var alias = Raylib.LoadSoundAlias(sound);
            Raylib.SetSoundVolume(alias, Math.Clamp(gain, 0f, 1f));
            Raylib.SetSoundPan(alias, Pan(pan));
            Raylib.SetSoundPitch(alias, Math.Clamp(pitch, 0.25f, 4f));
            Raylib.PlaySound(alias);
            int id = _next++;
            _shots[id] = alias;
            return id;
        }

        public int Loop(long clip, float gain, float pan, float pitch)
        {
            if (!IsOpen || !_wav.TryGetValue(clip, out var wav)) return -1;
            var ext = System.Text.Encoding.ASCII.GetBytes((_ext.TryGetValue(clip, out var e) ? e : ".wav") + "\0");
            Music music;
            fixed (byte* x = ext) music = Raylib.LoadMusicStreamFromMemory((sbyte*)x, (byte*)wav.Data, wav.Size);
            if (music.FrameCount == 0) return -1;
            music.Looping = true;
            Raylib.SetMusicVolume(music, Math.Clamp(gain, 0f, 1f));
            Raylib.SetMusicPan(music, Pan(pan));
            Raylib.SetMusicPitch(music, Math.Clamp(pitch, 0.25f, 4f));
            Raylib.PlayMusicStream(music);
            int id = _next++;
            _loops[id] = music;
            return id;
        }

        public void Set(int voice, float gain, float pan, float pitch)
        {
            if (_loops.TryGetValue(voice, out var m))
            {
                Raylib.SetMusicVolume(m, Math.Clamp(gain, 0f, 1f));
                Raylib.SetMusicPan(m, Pan(pan));
                Raylib.SetMusicPitch(m, Math.Clamp(pitch, 0.25f, 4f));
            }
            else if (_shots.TryGetValue(voice, out var s))
            {
                Raylib.SetSoundVolume(s, Math.Clamp(gain, 0f, 1f));
                Raylib.SetSoundPan(s, Pan(pan));
            }
        }

        public bool IsPlaying(int voice)
            => _loops.ContainsKey(voice) || (_shots.TryGetValue(voice, out var s) && Raylib.IsSoundPlaying(s));

        public void Stop(int voice)
        {
            if (_loops.Remove(voice, out var m)) { Raylib.StopMusicStream(m); Raylib.UnloadMusicStream(m); }
            else if (_shots.Remove(voice, out var s)) { Raylib.StopSound(s); Raylib.UnloadSoundAlias(s); }
        }

        public void StopAll()
        {
            foreach (var id in new List<int>(_loops.Keys)) Stop(id);
            foreach (var id in new List<int>(_shots.Keys)) Stop(id);
        }

        public void Tick()
        {
            if (!IsOpen) return;
            foreach (var m in _loops.Values) Raylib.UpdateMusicStream(m);
            _done.Clear();
            foreach (var (id, s) in _shots) if (!Raylib.IsSoundPlaying(s)) _done.Add(id);
            foreach (var id in _done) { Raylib.UnloadSoundAlias(_shots[id]); _shots.Remove(id); }
        }

        public void Dispose()
        {
            if (!IsOpen) return;
            StopAll();
            foreach (var s in _sounds.Values) Raylib.UnloadSound(s);
            _sounds.Clear();
            foreach (var (data, _) in _wav.Values) Marshal.FreeHGlobal(data);
            _wav.Clear();
            Raylib.CloseAudioDevice();
            IsOpen = false;
        }
    }
}
