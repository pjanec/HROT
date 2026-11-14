using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.UI.Common.Audio
{
    /// <summary>
    /// ⭐ CE-1120 (docs/designs/audio/DESIGN_FPS_Audio.md AU-7, §1 sequence) — what plays: one-shots wait out their speed-of-sound DELAY,
    /// then take a voice; loops are held by a KEY while their source asks for them each frame and stop the frame it does not. At most
    /// <see cref="MaxVoices"/> voices: a new sound takes the place of the weakest voice (lowest priority, then quietest) only if it is
    /// stronger, else it is dropped. Pure above <see cref="IAudioOutput"/>.
    /// </summary>
    public sealed class SoundMixer
    {
        public const int MaxVoices = 32;

        /// <summary>The most loops at once (engines, beds), inside <see cref="MaxVoices"/>.</summary>
        public const int MaxLoops = 12;

        private readonly record struct Pending(long Clip, Vector3 At, bool Positional, float Gain, float Pitch, byte Priority, double PlayAt);

        private sealed class Voice
        {
            public int Id;
            public long Clip;
            public byte Priority;
            public float Gain;
            public bool Loop;
            public long Key;
            public bool Touched;
        }

        private readonly List<Pending> _pending = new();
        private readonly List<Voice> _voices = new();
        private readonly Dictionary<long, Voice> _loops = new();

        /// <summary>Per category volume (AU-9), multiplied into every gain; the category's index is <c>SoundCategory</c>.</summary>
        public float[] CategoryVolume { get; } = NewVolumes();   // ⭐ CE-2372 — one per category, Voices included

        private static float[] NewVolumes() { var a = new float[Enum.GetValues<SoundCategory>().Length]; Array.Fill(a, 1f); return a; }

        /// <summary>⭐ <c>CE-1305</c> TE-11 — the playback rate the scene is heard at (sim seconds per film second; 1 = real time, 0 = paused): the director's
        /// (<see cref="AudioDirector.Rate"/>), set by the layer each frame.</summary>
        public double Rate { get; set; } = 1d;

        /// <summary>⭐ TE-11 — the gain factor of a sound of <paramref name="category"/> at <see cref="Rate"/> (<see cref="SoundAtRate.Gain"/>, the one rule).</summary>
        public float RateGain(SoundCategory category) => SoundAtRate.Gain(category, Rate);

        /// <summary>Counters for the rails: started, dropped (budget / range), waiting for their delay.</summary>
        public int Started { get; private set; }
        public int Dropped { get; private set; }
        public int Waiting => _pending.Count;
        public int Voices => _voices.Count;

        /// <summary>A one-shot: <paramref name="gain"/> already distance-scaled; it plays after <paramref name="delay"/> seconds, panned
        /// from where the ear is THEN (a positional sound) or centred.</summary>
        public void Request(long clip, Vector3 at, bool positional, float gain, float pitch, byte priority, double now, double delay)
        {
            if (clip == 0 || gain <= 1e-4f) { Dropped++; return; }
            _pending.Add(new Pending(clip, at, positional, gain, pitch, priority, now + Math.Max(0.0, delay)));
        }

        /// <summary>A loop held by <paramref name="key"/> this frame (an engine by its entity, a bed by its kind).</summary>
        public void Hold(IAudioOutput output, long key, long clip, float gain, float pan, float pitch, byte priority)
        {
            if (_loops.TryGetValue(key, out var v) && v.Clip == clip && output.IsPlaying(v.Id))
            {
                v.Gain = gain;
                v.Touched = true;
                output.Set(v.Id, gain, pan, pitch);
                return;
            }
            if (v != null) StopVoice(output, v);
            if (gain <= 1e-4f) return;
            int loops = 0;
            foreach (var x in _voices) if (x.Loop) loops++;
            if ((loops >= MaxLoops || _voices.Count >= MaxVoices) && !MakeRoom(output, priority, gain, loopsOnly: loops >= MaxLoops)) { Dropped++; return; }
            int id = output.Loop(clip, gain, pan, pitch);
            if (id < 0) return;
            var nv = new Voice { Id = id, Clip = clip, Priority = priority, Gain = gain, Loop = true, Key = key, Touched = true };
            _voices.Add(nv);
            _loops[key] = nv;
        }

        /// <summary>Once a frame, after the sources asked: due one-shots start, loops nobody held stop, finished voices are freed.</summary>
        public void Update(IAudioOutput output, double now, in Ear ear)
        {
            _voices.RemoveAll(v => !v.Loop && !output.IsPlaying(v.Id));
            foreach (var v in new List<Voice>(_loops.Values))
                if (!v.Touched || !output.IsPlaying(v.Id)) StopVoice(output, v);
                else v.Touched = false;

            _pending.Sort((a, b) => b.Priority != a.Priority ? b.Priority.CompareTo(a.Priority) : b.Gain.CompareTo(a.Gain));
            for (int i = 0; i < _pending.Count; i++)
            {
                var p = _pending[i];
                if (p.PlayAt > now) continue;
                _pending.RemoveAt(i--);
                if (_voices.Count >= MaxVoices && !MakeRoom(output, p.Priority, p.Gain, loopsOnly: false)) { Dropped++; continue; }
                float pan = p.Positional ? Spatializer.Pan(ear, p.At) : 0f;
                int id = output.Play(p.Clip, Math.Clamp(p.Gain, 0f, 1f), pan, p.Pitch);
                if (id < 0) continue;
                _voices.Add(new Voice { Id = id, Clip = p.Clip, Priority = p.Priority, Gain = p.Gain });
                Started++;
            }
            output.Tick();
        }

        /// <summary>Everything stops (the unit is lost).</summary>
        public void Clear(IAudioOutput output)
        {
            foreach (var v in _voices) output.Stop(v.Id);
            _voices.Clear();
            _loops.Clear();
            _pending.Clear();
        }

        private bool MakeRoom(IAudioOutput output, byte priority, float gain, bool loopsOnly)
        {
            Voice? weakest = null;
            foreach (var v in _voices)
            {
                if (loopsOnly && !v.Loop) continue;
                if (weakest == null || v.Priority < weakest.Priority || (v.Priority == weakest.Priority && v.Gain < weakest.Gain)) weakest = v;
            }
            if (weakest == null || weakest.Priority > priority || (weakest.Priority == priority && weakest.Gain >= gain)) return false;
            StopVoice(output, weakest);
            return true;
        }

        private void StopVoice(IAudioOutput output, Voice v)
        {
            output.Stop(v.Id);
            _voices.Remove(v);
            if (v.Loop && _loops.TryGetValue(v.Key, out var held) && held == v) _loops.Remove(v.Key);
        }
    }
}
