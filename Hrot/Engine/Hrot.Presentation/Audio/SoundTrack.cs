using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.UI.Common.Audio
{
    /// <summary>
    /// ⭐ <c>CE-4038</c> B11 (docs/DESIGN_Film_Director.md §4c, R-4059) — the film director's hold on the sound: a take's <c>sound</c> switch
    /// (<see cref="TakeSound"/>: true = heard from the camera whatever the menu says, false = silent, null = the user's own switch), and the
    /// <see cref="Recorder"/> that logs every sound the mixer starts while a film is captured. ⭐ FPS always sounds (the switch is the
    /// CAMERA's). Process-wide, as <see cref="AudioPreferences"/> is: one window, one director.
    /// </summary>
    public static class AudioDirector
    {
        public static bool? TakeSound { get; set; }

        /// <summary>The film's cue log while capturing; null otherwise.</summary>
        public static SoundTrack? Recorder { get; set; }

        /// <summary>⭐ <c>CE-1305</c> TE-11 — the director's playback rate now (sim seconds per film second; 0 paused); null = no director plays (heard at 1×).
        /// The runner sets it each playing frame (<see cref="Map3D.Director.IDirectorSound.SetRate"/>); the layer hands it to the mixer.</summary>
        public static double? Rate { get; set; }

        /// <summary>Whether the camera listens: the take's switch, else the user's.</summary>
        public static bool CameraSound => TakeSound ?? AudioPreferences.CameraSound;
    }

    /// <summary>
    /// ⭐ <c>CE-4038</c> B11 (§4c) — the film's sound track: a captured frame takes ~0.8 s, so the device's output cannot be recorded. ⭐ Instead
    /// every sound the mixer starts is LOGGED (its clip, gain, pan and pitch, at the sim time it started), the clips' samples are kept, and
    /// after the pass the WAV is rendered OFFLINE onto the film's own timeline (each frame's sim time — slow motion and the gaps between shots
    /// included), for ffmpeg to mux. One mixer, one spatializer, live and on film.
    /// </summary>
    public sealed class SoundTrack
    {
        public const int SampleRate = 48000;

        public sealed record Cue(double SimTime, long Clip, float Gain, float Pan, float Pitch, bool Loop, int Voice, SoundCategory? Category = null)
        {
            public double? StoppedAt { get; set; }
        }

        private readonly Func<double> _simTime;
        private readonly Dictionary<long, (short[] Samples, int Rate)> _clips = new();
        private readonly List<Cue> _cues = new();
        private readonly Dictionary<int, Cue> _loops = new();

        /// <param name="simTime">The film's sim time now (the director offsets each reel item onto one timeline).</param>
        public SoundTrack(Func<double> simTime) => _simTime = simTime ?? throw new ArgumentNullException(nameof(simTime));

        public IReadOnlyList<Cue> Cues => _cues;

        /// <summary>⭐ <c>CE-1305</c> TE-11 — a clip's category (the layer sets it from the TKB): the offline track applies <see cref="SoundAtRate"/> per pass, so it
        /// must know an explosion from a shot. Null, or a null answer: unknown (not an explosion).</summary>
        public Func<long, SoundCategory?>? CategoryOf { get; set; }
        public bool HasClip(long clip) => _clips.ContainsKey(clip);

        public void Keep(long clip, short[] samples, int rate) { if (samples.Length > 0) _clips[clip] = (samples, rate); }

        public void Started(long clip, float gain, float pan, float pitch, bool loop, int voice)
        {
            var cue = new Cue(_simTime(), clip, gain, pan, pitch, loop, voice, CategoryOf?.Invoke(clip));
            _cues.Add(cue);
            if (loop && voice >= 0) _loops[voice] = cue;
        }

        public void Stopped(int voice)
        {
            if (_loops.Remove(voice, out var cue)) cue.StoppedAt = _simTime();
        }

        /// <summary>
        /// The WAV of the film: <paramref name="frameSimTimes"/>[i] is the sim time film frame i shows (NaN: a card). A sound is placed at the
        /// film time its sim time falls at; one in a gap the film skips is left out. False when there is nothing to hear.
        /// ⭐ <c>CE-1305</c> TE-11 (§11v A1) — at ANY rate a sound keeps its natural pitch and length (never resampled by the rate); the film's rate at each frame
        /// (<see cref="RateAt"/>) rules it through <see cref="SoundAtRate"/>: a one-shot by the rate where it starts, a loop frame by frame (silent while held,
        /// on a card, or past 2×).
        /// </summary>
        public bool RenderWav(string path, IReadOnlyList<double> frameSimTimes, int fps)
        {
            int frames = frameSimTimes.Count;
            if (frames == 0 || _cues.Count == 0 || fps <= 0) return false;
            int length = (int)((long)frames * SampleRate / fps) + SampleRate / fps;
            var left = new float[length];
            var right = new float[length];
            var gain = new float[frames];
            int placed = 0;
            foreach (var cue in _cues)
            {
                if (!_clips.TryGetValue(cue.Clip, out var clip)) continue;
                // ⭐ CE-1369 — a cue is logged ONCE (when the mixer starts it) but a source time can be filmed more than once
                //   (an instant replay is a segment that goes back): it sounds in EVERY pass that shows its sim time.
                double[]? stops = cue.Loop && cue.StoppedAt is { } s ? FilmTimes(frameSimTimes, s, fps).ToArray() : null;
                for (int i = 0; i < frames; i++) gain[i] = SoundAtRate.Gain(cue.Category, RateAt(frameSimTimes, i, fps));
                foreach (double start in FilmTimes(frameSimTimes, cue.SimTime, fps))
                {
                    int startFrame = Math.Clamp((int)(start * fps + 1e-9), 0, frames - 1);
                    if (!cue.Loop && gain[startFrame] <= 0f) continue;   // ⭐ TE-11 — a one-shot started past 2× (not a blast) is not heard
                    double? end = null;
                    if (cue.Loop)
                    {
                        // a loop ends at the first stop shown AFTER its start in the same pass, else with the film
                        end = (double)frames / fps;
                        if (stops != null) foreach (double e in stops) if (e > start) { end = e; break; }
                    }
                    Mix(left, right, clip.Samples, clip.Rate, cue, start, end, gain, fps, cue.Loop ? -1f : gain[startFrame]);
                    placed++;
                }
            }
            if (placed == 0) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            WriteWav(path, left, right);
            return true;
        }

        /// <summary>⭐ <c>CE-1305</c> TE-11 — the film's rate at frame <paramref name="i"/>: sim seconds per film second to the next frame (the last frame: the one
        /// before it); 0 on a card (NaN) and on a held frame.</summary>
        internal static double RateAt(IReadOnlyList<double> frameSimTimes, int i, int fps)
        {
            int n = frameSimTimes.Count;
            if (n < 2 || fps <= 0) return 1d;
            if (i >= n - 1) i = n - 2;
            double a = frameSimTimes[i], b = frameSimTimes[i + 1];
            return double.IsNaN(a) || double.IsNaN(b) || b < a ? 0d : (b - a) * fps;
        }

        /// <summary>The FIRST film time (s) of a sim time (see <see cref="FilmTimes"/>); null in a gap or on a card.</summary>
        internal static double? FilmTime(IReadOnlyList<double> frameSimTimes, double simTime, int fps)
        {
            foreach (double t in FilmTimes(frameSimTimes, simTime, fps)) return t;
            return null;
        }

        /// <summary>
        /// ⭐ CE-1369 — EVERY film time (s) of a sim time, in film order: one per pass of the film that shows it (an instant replay films
        /// the same sim time twice). Between two filmed frames the time is interpolated; in a gap the film skips, or on a card, none.
        /// </summary>
        internal static IEnumerable<double> FilmTimes(IReadOnlyList<double> frameSimTimes, double simTime, int fps)
        {
            for (int i = 0; i + 1 < frameSimTimes.Count; i++)
            {
                double a = frameSimTimes[i], b = frameSimTimes[i + 1];
                if (double.IsNaN(a) || double.IsNaN(b) || b < a) continue;
                if (simTime >= a && simTime < b)
                {
                    if (b - a > 0.25) { if (simTime - a < 1d / fps) yield return (double)i / fps; }   // a gap the film skips
                    else yield return (i + (b > a ? (simTime - a) / (b - a) : 0d)) / fps;
                }
            }
        }

        /// <param name="frameGain">⭐ TE-11 — a loop's factor per film frame (<see cref="SoundAtRate"/>); <paramref name="once"/> ≥ 0: a one-shot's factor, fixed where it starts.</param>
        private static void Mix(float[] left, float[] right, short[] samples, int rate, Cue cue, double start, double? end, float[] frameGain, int fps, float once)
        {
            // pan −1 … 1 (left … right), as the mixer hands it to the device; equal-power
            float p = 0.5f + 0.5f * Math.Clamp(cue.Pan, -1f, 1f);
            float gl = cue.Gain * MathF.Cos(p * MathF.PI / 2f), gr = cue.Gain * MathF.Sin(p * MathF.PI / 2f);
            double step = rate * Math.Clamp(cue.Pitch, 0.25f, 4f) / SampleRate;   // ⛔ never × the film's rate (TE-11: natural pitch)
            int at = (int)(start * SampleRate);
            int stop = end is { } e ? Math.Min(left.Length, (int)(e * SampleRate)) : left.Length;
            double src = 0d;
            for (int i = at; i < stop && i >= 0; i++)
            {
                int k = (int)src;
                if (k >= samples.Length)
                {
                    if (!cue.Loop) break;
                    src -= samples.Length;
                    k = (int)src;
                }
                float f = once >= 0f ? once : frameGain[Math.Min(frameGain.Length - 1, (int)((long)i * fps / SampleRate))];
                float v = samples[k] / 32768f * f;
                left[i] += v * gl;
                right[i] += v * gr;
                src += step;
            }
        }

        private static void WriteWav(string path, float[] left, float[] right)
        {
            float peak = 1f;
            for (int i = 0; i < left.Length; i++) peak = MathF.Max(peak, MathF.Max(MathF.Abs(left[i]), MathF.Abs(right[i])));
            using var w = new BinaryWriter(File.Create(path));
            int bytes = left.Length * 4;
            w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(SampleRate); w.Write(SampleRate * 4); w.Write((short)4); w.Write((short)16);
            w.Write("data"u8); w.Write(bytes);
            for (int i = 0; i < left.Length; i++)
            {
                w.Write((short)Math.Clamp(left[i] / peak * 32767f, -32768f, 32767f));
                w.Write((short)Math.Clamp(right[i] / peak * 32767f, -32768f, 32767f));
            }
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4038</c> B11 — an output that plays through <paramref name="inner"/> (or nothing, when the machine has no device — a cloud VM)
    /// and tells the <see cref="SoundTrack"/> every clip it is given and every sound it starts.
    /// </summary>
    public sealed class RecordingAudioOutput : IAudioOutput
    {
        private readonly IAudioOutput _inner;
        private readonly SoundTrack _track;

        public RecordingAudioOutput(IAudioOutput inner, SoundTrack track)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _track = track ?? throw new ArgumentNullException(nameof(track));
        }

        public IAudioOutput Inner => _inner;
        public SoundTrack Track => _track;

        public bool Open() => _inner.Open();
        public bool IsOpen => _inner.IsOpen;
        public float MasterVolume { get => _inner.MasterVolume; set => _inner.MasterVolume = value; }

        public void Define(long clip, short[] samples, int sampleRate)
        {
            _track.Keep(clip, samples, sampleRate);
            _inner.Define(clip, samples, sampleRate);
        }

        public bool DefineFile(long clip, string path)
        {
            if (WavReader.TryRead(path, out var samples, out var rate)) _track.Keep(clip, samples, rate);
            return _inner.DefineFile(clip, path);
        }

        public bool Has(long clip) => _inner.Has(clip);

        public int Play(long clip, float gain, float pan, float pitch)
        {
            int v = _inner.Play(clip, gain, pan, pitch);
            _track.Started(clip, gain, pan, pitch, loop: false, v);
            return v;
        }

        public int Loop(long clip, float gain, float pan, float pitch)
        {
            int v = _inner.Loop(clip, gain, pan, pitch);
            _track.Started(clip, gain, pan, pitch, loop: true, v);
            return v;
        }

        public void Set(int voice, float gain, float pan, float pitch) => _inner.Set(voice, gain, pan, pitch);
        public bool IsPlaying(int voice) => _inner.IsPlaying(voice);

        public void Stop(int voice)
        {
            _track.Stopped(voice);
            _inner.Stop(voice);
        }

        public void StopAll() => _inner.StopAll();
        public void Tick() => _inner.Tick();
        public void Dispose() => _inner.Dispose();
    }

    /// <summary>Mono 16-bit samples of a PCM WAV file (the first channel); OGG is not decoded here (the sound track then leaves the clip out).</summary>
    internal static class WavReader
    {
        public static bool TryRead(string path, out short[] samples, out int rate)
        {
            samples = Array.Empty<short>();
            rate = 0;
            try
            {
                if (!File.Exists(path) || !path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return false;
                using var r = new BinaryReader(File.OpenRead(path));
                if (new string(r.ReadChars(4)) != "RIFF") return false;
                r.ReadInt32();
                if (new string(r.ReadChars(4)) != "WAVE") return false;
                short channels = 1, bits = 16;
                while (r.BaseStream.Position + 8 <= r.BaseStream.Length)
                {
                    var id = new string(r.ReadChars(4));
                    int size = r.ReadInt32();
                    if (id == "fmt ")
                    {
                        short format = r.ReadInt16(); channels = r.ReadInt16(); rate = r.ReadInt32();
                        r.ReadInt32(); r.ReadInt16(); bits = r.ReadInt16();
                        if (format != 1 || bits != 16) return false;
                        r.BaseStream.Seek(size - 16, SeekOrigin.Current);
                    }
                    else if (id == "data")
                    {
                        int n = size / 2 / Math.Max((short)1, channels);
                        samples = new short[n];
                        for (int i = 0; i < n; i++)
                        {
                            samples[i] = r.ReadInt16();
                            for (int c = 1; c < channels; c++) r.ReadInt16();
                        }
                        return rate > 0;
                    }
                    else r.BaseStream.Seek(size + (size & 1), SeekOrigin.Current);
                }
            }
            catch (Exception ex) when (ex is IOException or EndOfStreamException or UnauthorizedAccessException) { }
            return false;
        }
    }
}
