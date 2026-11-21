using System;
using System.Collections.Generic;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.UI.Common.Audio
{
    /// <summary>
    /// ⭐ CE-1120 (docs/designs/audio/DESIGN_FPS_Audio.md AU-8, §5) — the PLACEHOLDER sounds, generated in code: noise bursts and envelopes
    /// for shots and blasts, hums and pulses for engines and rotors, filtered noise for wind and rumble. Deterministic (a fixed seed per
    /// sound), mono 16-bit at <see cref="SampleRate"/>, peaks kept below full scale; a LOOP's end is cross-faded into its start so it
    /// repeats without a click. No licence, no file — a CC0 recording named in the sound's <c>SoundDto.Files</c> replaces one.
    /// </summary>
    public static class SoundSynthesizer
    {
        public const int SampleRate = 22050;

        /// <summary>The highest sample, as a fraction of full scale.</summary>
        public const float Peak = 0.85f;

        /// <summary>How long a loop's end is cross-faded into its start (s).</summary>
        public const float LoopFade = 0.25f;

        /// <summary>The samples of <paramref name="synth"/>; empty for <see cref="SoundSynth.None"/>.</summary>
        public static short[] Make(SoundSynth synth)
        {
            var r = new Rng(0x9E3779B9u ^ (uint)synth * 2654435761u);
            return synth switch
            {
                SoundSynth.ShotSmall => Finish(Shot(ref r, 0.55f, crack: 0.004f, bodyLp: 0.25f, body: 0.035f, thumpHz: 140f, thump: 0.05f, tail: 0.18f)),
                SoundSynth.ShotMedium => Finish(Shot(ref r, 0.9f, crack: 0.006f, bodyLp: 0.15f, body: 0.07f, thumpHz: 90f, thump: 0.09f, tail: 0.3f)),
                SoundSynth.ShotLarge => Finish(Shot(ref r, 1.8f, crack: 0.008f, bodyLp: 0.08f, body: 0.15f, thumpHz: 55f, thump: 0.25f, tail: 0.6f)),
                SoundSynth.ShotFarSmall => Finish(FarShot(ref r, 1.0f, lp: 0.05f, decay: 0.25f, thumpHz: 90f)),
                SoundSynth.ShotFarMedium => Finish(FarShot(ref r, 1.4f, lp: 0.04f, decay: 0.4f, thumpHz: 65f)),
                SoundSynth.ShotFarLarge => Finish(FarShot(ref r, 2.4f, lp: 0.03f, decay: 0.8f, thumpHz: 45f)),
                SoundSynth.Burst => Finish(Burst(ref r)),
                SoundSynth.ExplosionSmall => Finish(Explosion(ref r, 1.5f, 0.08f, 0.4f, 55f, 0.3f, debris: 6)),
                SoundSynth.ExplosionMedium => Finish(Explosion(ref r, 2.5f, 0.12f, 0.8f, 45f, 0.5f, debris: 12)),
                SoundSynth.ExplosionLarge => Finish(Explosion(ref r, 4.0f, 0.18f, 1.4f, 35f, 0.9f, debris: 20)),
                SoundSynth.ExplosionHuge => Finish(Explosion(ref r, 6.0f, 0.24f, 2.2f, 28f, 1.2f, debris: 30)),   // ⭐ CE-1231 — longer, lower, more debris
                SoundSynth.BulletPass => Finish(BulletPass(ref r)),
                SoundSynth.ImpactSoft => Finish(ImpactSoft(ref r)),
                SoundSynth.ImpactHard => Finish(Ricochet(ref r)),
                SoundSynth.Crash => Finish(Explosion(ref r, 1.8f, 0.04f, 0.3f, 48f, 0.35f, debris: 28)),   // ⭐ CE-4600 (B-3b) — a dull blow (no blast's crack), then rubble falling
                SoundSynth.Glass => Finish(Glass(ref r)),   // ⭐ CE-5034 — a crack, then tinkling
                SoundSynth.FlameRoar => Loop(FlameRoar(ref r, 1f + LoopFade)),   // ⭐ CE-4635 — a held roar
                SoundSynth.DragonWhoosh => Finish(DragonWhoosh(ref r)),                 // ⭐ CE-4634 — one down-stroke
                SoundSynth.DragonRoar => Finish(BeastRoar(ref r, 2.2f, 70f, 98f, 58f)),   // ⭐ CE-4634 — a growl that swells and drops
                SoundSynth.DragonDeathRoar => Finish(BeastRoar(ref r, 3.6f, 105f, 80f, 32f)),   // ⭐ CE-4634 — long, falling away
                SoundSynth.Footsteps => Loop(Footsteps(ref r, 1f + LoopFade)),
                SoundSynth.WheeledEngine => Loop(Engine(ref r, 2f + LoopFade, 60f, clankHz: 0f)),
                SoundSynth.TrackedEngine => Loop(Engine(ref r, 2f + LoopFade, 45f, clankHz: 9f)),
                SoundSynth.Rotor => Loop(Rotor(ref r, 2f + LoopFade)),
                SoundSynth.Jet => Loop(Jet(ref r, 2f + LoopFade)),
                SoundSynth.Reload => Finish(Reload(ref r)),
                SoundSynth.EmptyClick => Finish(Click(ref r, 0.08f, 0f, 2400f)),
                SoundSynth.Breathing => Loop(Breathing(ref r, 3f + LoopFade)),
                SoundSynth.Door => Finish(Door(ref r)),
                SoundSynth.Wind => Loop(Wind(ref r, 8f + LoopFade)),
                SoundSynth.Birds => Loop(Birds(ref r, 8f + LoopFade)),
                SoundSynth.Insects => Loop(Insects(4f + LoopFade)),
                SoundSynth.Town => Loop(Town(ref r, 8f + LoopFade)),
                SoundSynth.BattleRumble => Loop(Rumble(ref r, 8f + LoopFade)),
                _ => Array.Empty<short>(),
            };
        }

        // ── ⭐ CE-4623 (animals AU-8) — the ANIMALS' placeholders: formant-shaped voices (a moo, a baa, a neigh, a bark, a meow, an oink, a bleat), chirps, a cluck, hooves, wings ─────────────────

        /// <summary>
        /// ⭐ CE-4623 — the placeholder clip of an animal sound: <paramref name="species"/> 0…11 (the TKB order 700…711, <c>AnimalSoundCatalog</c>) and <paramref name="line"/> 0…7 (call · alarm · special · hooves walk /
        /// trot / gallop · wings · flutter). A call is a harmonic voice through two formants; a bird's a few chirps; hooves, wings and flutter loops / bursts of filtered noise. A recording named in the TKB sound replaces it.
        /// </summary>
        public static short[] Animal(int species, int line)
        {
            var r = new Rng(0xA17A15u ^ (uint)(species * 8 + line + 1) * 2654435761u);
            return line switch
            {
                3 or 4 or 5 => Loop(Hooves(ref r, species, line, 2f + LoopFade)),
                6 => Loop(WingBeats(ref r, 2f + LoopFade, 7f)),
                7 => Finish(WingBeats(ref r, 0.5f, 16f)),
                _ => Finish(AnimalVoice(ref r, species, line)),
            };
        }

        /// <summary>One voiced pulse: a harmonic series on a pitch contour (a → mid → b), shaped by two formants, a vibrato, a rasp of noise, an envelope.</summary>
        private static void Pulse(ref Rng r, float[] b, int at, float seconds, float a, float mid, float c, float f1, float f2, float vibHz, float vib, float rasp, float gain)
        {
            int n = (int)(seconds * SampleRate);
            double phase = 0;
            for (int i = 0; i < n && at + i < b.Length; i++)
            {
                float t = i / (float)SampleRate, u = i / (float)n;
                float f0 = (u < 0.5f ? a + (mid - a) * (u * 2f) : mid + (c - mid) * ((u - 0.5f) * 2f)) * (1f + vib * MathF.Sin(2f * MathF.PI * vibHz * t));
                phase += f0 / SampleRate;
                float sum = 0f;
                for (int h = 1; h <= 26; h++)
                {
                    float fh = h * f0;
                    if (fh > 5000f) break;
                    float g = MathF.Exp(-(fh - f1) * (fh - f1) / (2f * 140f * 140f)) + 0.7f * MathF.Exp(-(fh - f2) * (fh - f2) / (2f * 320f * 320f)) + 0.04f;
                    sum += g / MathF.Pow(h, 0.8f) * MathF.Sin(2f * MathF.PI * h * (float)(phase % 1.0));
                }
                float env = MathF.Min(1f, t / 0.025f) * (1f - MathF.Pow(u, 3f));
                b[at + i] += gain * env * (sum + rasp * r.Next() * 1.5f);
            }
        }

        /// <summary>A bird's chirp: a clean tone gliding a → b, a short soft envelope.</summary>
        private static void Chirp(float[] b, int at, float seconds, float a, float c, float gain)
        {
            int n = (int)(seconds * SampleRate);
            double phase = 0;
            for (int i = 0; i < n && at + i < b.Length; i++)
            {
                float u = i / (float)n;
                phase += (a + (c - a) * u) / SampleRate;
                b[at + i] += gain * MathF.Sin(2f * MathF.PI * (float)(phase % 1.0)) * MathF.Sin(MathF.PI * u);
            }
        }

        private static float[] AnimalVoice(ref Rng r, int species, int line)
        {
            bool alarm = line != 0;
            var b = Buf(2.2f);
            switch (species)
            {
                case 0:   // cattle: a low moo — an alarm is a higher, rougher bellow
                    Pulse(ref r, b, 0, alarm ? 1.1f : 1.5f, alarm ? 150f : 110f, alarm ? 190f : 125f, alarm ? 120f : 85f, 420f, 850f, 5f, 0.03f, alarm ? 0.25f : 0.05f, 1f);
                    break;
                case 1:   // sheep: a trembling baa
                    Pulse(ref r, b, 0, 0.9f, 330f, 345f, 300f, 800f, 1500f, 22f, 0.07f, 0.08f, 1f);
                    if (alarm) Pulse(ref r, b, (int)(1.0f * SampleRate), 0.8f, 360f, 380f, 320f, 850f, 1600f, 24f, 0.07f, 0.1f, 1f);
                    break;
                case 2 or 3 or 4:   // a horse: a neigh — up, then falling away (a whinny when frightened)
                    Pulse(ref r, b, 0, alarm ? 1.4f : 1.1f, alarm ? 560f : 450f, alarm ? 1000f : 800f, alarm ? 380f : 330f, 900f, 2200f, 14f, 0.05f, 0.1f, 1f);
                    break;
                case 5:   // a dog: barks — one at rest, a few frightened or special
                    for (int k = 0; k < (line == 0 ? 1 : line == 1 ? 2 : 4); k++)
                        Pulse(ref r, b, (int)(k * 0.38f * SampleRate), 0.17f, 300f, 270f, 190f, 700f, 1500f, 0f, 0f, 0.3f, 1f);
                    break;
                case 6:   // a cat: a meow, rising then falling
                    Pulse(ref r, b, 0, alarm ? 1.1f : 0.9f, alarm ? 520f : 450f, alarm ? 800f : 700f, 400f, 900f, 2400f, 6f, 0.02f, alarm ? 0.3f : 0.06f, 1f);
                    break;
                case 7 or 8:   // a flock / a bird: chirps — more of them, quicker, when it is not a song
                    for (int k = 0; k < (alarm ? 9 : 4); k++)
                        Chirp(b, (int)(k * (alarm ? 0.09f : 0.22f) * SampleRate), alarm ? 0.07f : 0.12f, 2800f + 400f * (k % 3), 3800f + 300f * (k % 2), 0.9f);
                    break;
                case 9:   // a hen: a cluck — a cackle when frightened, a crow as the special
                    if (line == 2) { Pulse(ref r, b, 0, 1.2f, 420f, 900f, 520f, 700f, 1800f, 7f, 0.03f, 0.12f, 1f); break; }
                    for (int k = 0; k < (alarm ? 8 : 3); k++)
                        Pulse(ref r, b, (int)(k * (alarm ? 0.11f : 0.2f) * SampleRate), 0.09f, alarm ? 700f : 550f, alarm ? 650f : 500f, alarm ? 500f : 400f, 900f, 1800f, 0f, 0f, 0.1f, 1f);
                    break;
                case 10:   // a pig: grunts and an oink — a squeal when frightened
                    if (alarm) Pulse(ref r, b, 0, 0.9f, 800f, 1500f, 900f, 1400f, 2600f, 9f, 0.03f, 0.35f, 1f);
                    else for (int k = 0; k < 2; k++) Pulse(ref r, b, (int)(k * 0.3f * SampleRate), 0.28f, 170f, 150f, 110f, 500f, 1100f, 0f, 0f, 0.3f, 1f);
                    break;
                default:   // a goat: a bleat, harsher and lower than a sheep's
                    Pulse(ref r, b, 0, alarm ? 1.0f : 0.8f, alarm ? 300f : 250f, alarm ? 330f : 270f, alarm ? 250f : 200f, 700f, 1400f, 28f, 0.08f, 0.15f, 1f);
                    break;
            }
            return b;
        }

        /// <summary>Hooves: soft thuds at a gait's beat — heavier and lower for a big animal, pads for a small one; a loop.</summary>
        private static float[] Hooves(ref Rng r, int species, int line, float seconds)
        {
            var b = Buf(seconds);
            float beatsPerSecond = line == 3 ? 3.2f : line == 4 ? 2.6f : 3.6f;
            bool big = species is 0 or 2 or 3 or 4;
            float hz = big ? (species == 0 ? 70f : 90f) : species is 1 or 11 ? 140f : 200f;
            float gain = big ? 1f : species is 1 or 11 or 10 ? 0.6f : 0.35f;
            int beats = (int)(beatsPerSecond * (seconds - LoopFade));
            for (int k = 0; k < beats; k++)
            {
                int at = (int)((k / beatsPerSecond + (line == 5 && k % 2 == 1 ? 0.04f : 0f)) * SampleRate);
                float lp = 0f;
                for (int i = 0; i < (int)(0.11f * SampleRate) && at + i < b.Length; i++)
                {
                    float t = T(i);
                    lp += (r.Next() - lp) * 0.2f;
                    b[at + i] += gain * Env(t, 0.03f) * (Sin(hz * (1f - 0.25f * t / 0.11f), t) * 0.9f + lp * 0.6f);
                }
            }
            return b;
        }

        /// <summary>Wings: filtered noise beating at <paramref name="beatHz"/> — a flock's take-off, a bird flapping, a hen's flutter.</summary>
        private static float[] WingBeats(ref Rng r, float seconds, float beatHz)
        {
            var b = Buf(seconds);
            float lp = 0f, hp = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = T(i);
                float n = r.Next();
                lp += (n - lp) * 0.35f; hp += (lp - hp) * 0.03f;
                float am = MathF.Pow(0.5f + 0.5f * MathF.Sin(2f * MathF.PI * beatHz * t), 2f);
                b[i] = (lp - hp) * am * (seconds < 1f ? Env(t, 0.3f) : 1f);
            }
            return b;
        }

        /// <summary>⭐ CE-4634 — one heavy down-stroke of a 26 m wing: broadband noise low-passed hard (a body of air, not a hiss), swelling and dying over ~0.7 s, with a low thump of the
        /// stroke's end.</summary>
        private static float[] DragonWhoosh(ref Rng r)
        {
            var b = Buf(0.75f);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = T(i), u = t / 0.75f;
                lp += (r.Next() - lp) * 0.08f; lp2 += (lp - lp2) * 0.12f;
                float swell = MathF.Pow(MathF.Sin(MathF.PI * MathF.Min(1f, u * 1.15f)), 2f);
                b[i] = 5f * lp2 * swell + 0.35f * Sin(38f, t) * MathF.Exp(-MathF.Abs(u - 0.6f) * 9f);
            }
            return b;
        }

        /// <summary>⭐ CE-4634 — a beast's roar: a low voiced growl (<see cref="Pulse"/> on a pitch contour a → mid → c, dark formants, a heavy rasp) over a rumble of brown noise; the
        /// dragon's roar swells and drops, its death roar starts high and falls away.</summary>
        private static float[] BeastRoar(ref Rng r, float seconds, float a, float mid, float c)
        {
            var b = Buf(seconds);
            Pulse(ref r, b, 0, seconds, a, mid, c, 320f, 820f, 9f, 0.06f, 0.9f, 0.7f);
            Pulse(ref r, b, (int)(0.03f * SampleRate), seconds - 0.05f, a * 0.5f, mid * 0.5f, c * 0.5f, 250f, 600f, 6f, 0.05f, 0.5f, 0.5f);   // the octave below: a big chest
            float brown = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float u = i / (float)b.Length;
                brown = 0.996f * brown + 0.05f * r.Next();
                b[i] += 1.6f * brown * MathF.Sin(MathF.PI * u);
            }
            return b;
        }

        /// <summary>A small deterministic generator (xorshift32).</summary>
        private struct Rng
        {
            private uint _x;
            public Rng(uint seed) { _x = seed == 0 ? 1u : seed; }
            public float Next() { _x ^= _x << 13; _x ^= _x >> 17; _x ^= _x << 5; return (_x & 0xFFFFFF) / (float)0x800000 - 1f; }
            public float Unit() => (Next() + 1f) * 0.5f;
        }

        private static float[] Buf(float seconds) => new float[Math.Max(1, (int)(seconds * SampleRate))];
        private static float T(int i) => i / (float)SampleRate;
        private static float Env(float t, float decay) => MathF.Exp(-t / MathF.Max(decay, 1e-4f));
        private static float Sin(float hz, float t) => MathF.Sin(2f * MathF.PI * hz * t);

        // ── one-shots ────────────────────────────────────────────────────────────────────────────────────────────────────────────

        private static float[] Shot(ref Rng r, float seconds, float crack, float bodyLp, float body, float thumpHz, float thump, float tail)
        {
            var b = Buf(seconds);
            float lp1 = 0f, lp2 = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = T(i), n = r.Next();
                lp1 += bodyLp * (n - lp1);
                lp2 += 0.05f * (n - lp2);
                b[i] = n * Env(t, crack) + 1.6f * lp1 * Env(t, body) + 0.7f * Sin(thumpHz, t) * Env(t, thump) + 1.2f * lp2 * Env(t, tail) * 0.4f;
            }
            return b;
        }

        private static float[] FarShot(ref Rng r, float seconds, float lp, float decay, float thumpHz)
        {
            var b = Buf(seconds);
            float l = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = T(i);
                l += lp * (r.Next() - l);
                float attack = MathF.Min(1f, t / 0.01f);
                b[i] = attack * (3f * l * Env(t, decay) + 0.5f * Sin(thumpHz, t) * Env(t, decay * 0.6f));
            }
            AddEcho(b, 0.35f, 0.3f);
            return b;
        }

        private static float[] Burst(ref Rng r)
        {
            var b = Buf(1.1f);
            var one = Shot(ref r, 0.25f, 0.004f, 0.25f, 0.03f, 140f, 0.04f, 0.1f);
            for (int k = 0; k < 10; k++) Mix(b, one, (int)(k * 0.1f * SampleRate), 0.85f + 0.15f * r.Unit());
            return b;
        }

        private static float[] Explosion(ref Rng r, float seconds, float lpBody, float body, float rumbleHz, float rumble, int debris)
        {
            var b = Buf(seconds);
            float l1 = 0f, l2 = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = T(i), n = r.Next();
                l1 += lpBody * (n - l1);
                l2 += 0.02f * (n - l2);
                float attack = MathF.Min(1f, t / 0.002f);
                b[i] = attack * (0.8f * n * Env(t, 0.01f) + 2.5f * l1 * Env(t, body * 0.25f) + 5f * l2 * Env(t, body)
                       + 0.8f * Sin(rumbleHz, t) * Env(t, rumble));
            }
            for (int k = 0; k < debris; k++)   // falling debris: sparse small clicks in the tail
            {
                int at = (int)((0.2f + r.Unit() * (seconds - 0.4f)) * SampleRate);
                var c = Click(ref r, 0.02f, 0f, 900f + 1500f * r.Unit());
                Mix(b, c, at, 0.15f * (1f - at / (float)b.Length));
            }
            return b;
        }

        private static float[] BulletPass(ref Rng r)
        {
            var b = Buf(0.3f);
            float prev = 0f, bp1 = 0f, bp2 = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = T(i), n = r.Next();
                float crack = (n - prev) * Env(t, 0.003f);   // high-passed: the supersonic crack
                prev = n;
                float f = 3000f - 2100f * MathF.Min(1f, t / 0.25f);   // the whiz, falling in pitch
                float w = 2f * MathF.Sin(MathF.PI * f / SampleRate), q = 0.08f;
                bp1 += w * bp2; bp2 += w * (n - bp1 - q * bp2);   // a state-variable band-pass (bp2)
                float hump = MathF.Sin(MathF.PI * MathF.Min(1f, t / 0.28f));
                b[i] = 1.2f * crack + 0.6f * bp2 * hump;
            }
            return b;
        }

        private static float[] ImpactSoft(ref Rng r)
        {
            var b = Buf(0.18f);
            float l = 0f;
            for (int i = 0; i < b.Length; i++) { float t = T(i); l += 0.15f * (r.Next() - l); b[i] = 2f * l * Env(t, 0.03f) + 0.6f * Sin(200f, t) * Env(t, 0.04f); }
            return b;
        }

        private static float[] Ricochet(ref Rng r)
        {
            var b = Buf(0.55f);
            float phase = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = T(i);
                float f = 2600f - 900f * MathF.Min(1f, t / 0.5f);
                phase += 2f * MathF.PI * f / SampleRate;
                b[i] = 0.7f * MathF.Sin(phase) * Env(t, 0.15f) + r.Next() * Env(t, 0.004f);
            }
            return b;
        }

        /// <summary>⭐ CE-5034 — a pane shattering: a bright crack of high noise, then shards tinkling down (short high pings at random, thinning out).</summary>
        private static float[] Glass(ref Rng r)
        {
            var b = Buf(1.2f);
            float prev = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = T(i), n = r.Next();
                b[i] = 0.9f * (n - prev) * Env(t, 0.03f);   // differenced noise: the high crack
                prev = n;
            }
            for (int k = 0; k < 40; k++)
            {
                float at = 0.02f + 0.9f * MathF.Pow(0.5f * (r.Next() + 1f), 1.6f);
                float hz = 3000f + 3500f * 0.5f * (r.Next() + 1f);
                float amp = 0.35f * (1f - at);
                int i0 = (int)(at * SampleRate);
                for (int i = i0; i < b.Length; i++) { float t = T(i) - at; if (t > 0.08f) break; b[i] += amp * Sin(hz, t) * Env(t, 0.012f); }
            }
            return b;
        }

        private static float[] Click(ref Rng r, float seconds, float at, float hz)
        {
            var b = Buf(seconds);
            for (int i = 0; i < b.Length; i++) { float t = T(i) - at; if (t < 0f) continue; b[i] = (0.6f * Sin(hz, t) + 0.5f * r.Next()) * Env(t, 0.006f); }
            return b;
        }

        private static float[] Reload(ref Rng r)
        {
            var b = Buf(0.65f);
            Mix(b, Click(ref r, 0.06f, 0f, 2000f), 0, 1f);
            float l = 0f;
            int s0 = (int)(0.22f * SampleRate), s1 = (int)(0.3f * SampleRate);
            for (int i = s0; i < s1 && i < b.Length; i++) { l += 0.3f * (r.Next() - l); b[i] += 1.2f * l; }   // the slide
            Mix(b, Click(ref r, 0.08f, 0f, 1400f), (int)(0.45f * SampleRate), 1.2f);
            return b;
        }

        private static float[] Door(ref Rng r)
        {
            var b = Buf(0.9f);
            float phase = 0f;
            for (int i = 0; i < (int)(0.7f * SampleRate); i++)
            {
                float t = T(i);
                float f = 300f - 120f * t / 0.7f + 15f * r.Next();
                phase += 2f * MathF.PI * f / SampleRate;
                b[i] = 0.35f * MathF.Sin(phase) * (0.5f + 0.5f * MathF.Sin(2f * MathF.PI * 23f * t)) * MathF.Sin(MathF.PI * t / 0.7f);
            }
            Mix(b, ImpactSoft(ref r), (int)(0.7f * SampleRate), 1f);
            return b;
        }

        // ── loops (built a fade longer than their period; Loop() folds the extra into the start) ─────────────────────────────────

        private static float[] Footsteps(ref Rng r, float seconds)
        {
            var b = Buf(seconds);
            foreach (float at in new[] { 0.02f, 0.52f, 1.02f })
            {
                float l = 0f;
                int s = (int)(at * SampleRate);
                for (int i = 0; i < (int)(0.12f * SampleRate) && s + i < b.Length; i++)
                {
                    float t = T(i);
                    l += 0.2f * (r.Next() - l);
                    b[s + i] += (2f * l + 0.3f * r.Next()) * Env(t, 0.035f);
                }
            }
            return b;
        }

        private static float[] Engine(ref Rng r, float seconds, float hz, float clankHz)
        {
            var b = Buf(seconds);
            float l = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = T(i);
                float saw = 0f;   // a band-limited saw (eight harmonics): no reset jump, so neither a click nor a seam at the loop point
                for (int k = 1; k <= 8; k++) saw += Sin(k * hz, t) / k;
                saw *= 2f / MathF.PI;
                l += 0.05f * (r.Next() - l);
                float am = 0.8f + 0.2f * Sin(7f, t);
                b[i] = am * (0.5f * saw + 0.3f * Sin(2f * hz, t) + 0.15f * Sin(3f * hz, t) + 1.5f * l);
                if (clankHz > 0f)
                {
                    float ph = (clankHz * t) % 1f;   // the tracks: a metallic tick per link
                    b[i] += 0.35f * Sin(1800f, ph / clankHz) * Env(ph / clankHz, 0.008f);
                }
            }
            return b;
        }

        private static float[] Rotor(ref Rng r, float seconds)
        {
            var b = Buf(seconds);
            const float blades = 16f;   // a four-blade rotor at ~4 rev/s: the blade slap
            float l = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = T(i), n = r.Next();
                l += 0.12f * (n - l);
                float ph = (blades * t) % 1f;
                b[i] = 2.2f * l * Env(ph / blades, 0.012f) + 0.6f * l + 0.05f * Sin(1500f, t);
            }
            return b;
        }

        private static float[] Jet(ref Rng r, float seconds)
        {
            var b = Buf(seconds);
            float l1 = 0f, l2 = 0f;
            for (int i = 0; i < b.Length; i++) { float t = T(i), n = r.Next(); l1 += 0.06f * (n - l1); l2 += 0.35f * (n - l2); b[i] = 2f * l1 + 0.25f * l2 + 0.08f * Sin(2200f, t); }
            return b;
        }

        private static float[] Breathing(ref Rng r, float seconds)
        {
            var b = Buf(seconds);
            float l = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = T(i), ph = t % 1.5f;   // in 0.6 s, out 0.8 s, a pause
                l += 0.18f * (r.Next() - l);
                float env = ph < 0.6f ? MathF.Sin(MathF.PI * ph / 0.6f) * 0.6f : ph < 1.4f ? MathF.Sin(MathF.PI * (ph - 0.6f) / 0.8f) : 0f;
                b[i] = 1.5f * l * env;
            }
            return b;
        }

        private static float[] Wind(ref Rng r, float seconds)
        {
            var b = Buf(seconds);
            float brown = 0f, l = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = T(i);
                brown = 0.995f * brown + 0.05f * r.Next();
                l += 0.02f * (brown - l);
                float gust = 0.6f + 0.4f * Sin(0.25f, t) * Sin(0.11f, t + 1.3f);
                b[i] = gust * (2f * l + 0.3f * brown);
            }
            return b;
        }

        private static float[] Birds(ref Rng r, float seconds)
        {
            var b = Buf(seconds);
            for (int k = 0; k < 7; k++)
            {
                int at = (int)(r.Unit() * (seconds - 1f) * SampleRate);
                int chirps = 3 + (int)(r.Unit() * 4f);
                float f0 = 2800f + 1600f * r.Unit();
                for (int c = 0; c < chirps; c++)
                {
                    int s = at + (int)(c * 0.07f * SampleRate);
                    float phase = 0f;
                    for (int i = 0; i < (int)(0.04f * SampleRate) && s + i < b.Length; i++)
                    {
                        float t = T(i);
                        float f = f0 + 1500f * t / 0.04f;
                        phase += 2f * MathF.PI * f / SampleRate;
                        b[s + i] += 0.5f * MathF.Sin(phase) * MathF.Sin(MathF.PI * t / 0.04f);
                    }
                }
            }
            return b;
        }

        private static float[] Insects(float seconds)
        {
            var b = Buf(seconds);
            for (int i = 0; i < b.Length; i++) { float t = T(i); b[i] = 0.3f * Sin(4500f, t) * MathF.Max(0f, Sin(30f, t)); }
            return b;
        }

        private static float[] Town(ref Rng r, float seconds)
        {
            var b = Buf(seconds);
            float brown = 0f;
            for (int i = 0; i < b.Length; i++) { float t = T(i); brown = 0.997f * brown + 0.04f * r.Next(); b[i] = 1.5f * brown + 0.05f * Sin(100f, t); }
            return b;
        }

        /// <summary>⭐ CE-4635 — a flamethrower's roar: a low rumble (brown noise through a slow low-pass, wobbling), a hiss (high-passed noise, swelling and ebbing) and a few crackles
        /// (short random pops). A loop: <see cref="Loop"/> cross-fades its tail into the start.</summary>
        private static float[] FlameRoar(ref Rng r, float seconds)
        {
            var b = Buf(seconds);
            float brown = 0f, low = 0f, hp = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = T(i), n = r.Next();
                brown = 0.995f * brown + 0.05f * n;
                low += 0.04f * (brown - low);
                hp = n - (hp + 0.35f * (n - hp));   // a crude high-pass: what the low-pass leaves of the noise
                float swell = 0.75f + 0.25f * Sin(5f, t) * Sin(2.3f, t + 0.1f);
                b[i] = (4.5f * low + 0.35f * hp * swell + 0.5f * Sin(48f, t) * (0.6f + 0.4f * Sin(7f, t))) * swell;
            }
            for (int k = 0; k < 14; k++)   // crackle: a short pop of noise at a random moment
            {
                int at = (int)(r.Unit() * (b.Length - 400)), len = 60 + (int)(r.Unit() * 240);
                float amp = 0.25f + 0.5f * r.Unit();
                for (int i = 0; i < len; i++) b[at + i] += amp * r.Next() * Env(T(i), 0.004f);
            }
            return b;
        }

        private static float[] Rumble(ref Rng r, float seconds)
        {
            var b = Buf(seconds);
            float brown = 0f, l = 0f;
            for (int i = 0; i < b.Length; i++) { brown = 0.998f * brown + 0.04f * r.Next(); l += 0.01f * (brown - l); b[i] = 3f * l; }
            for (int k = 0; k < 6; k++)   // distant guns and bursts, felt more than heard
            {
                int at = (int)(r.Unit() * (seconds - 1.5f) * SampleRate);
                Mix(b, FarShot(ref r, 1.2f, 0.02f, 0.5f, 40f), at, 0.5f + 0.5f * r.Unit());
            }
            return b;
        }

        // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

        private static void Mix(float[] into, float[] what, int at, float gain)
        {
            for (int i = 0; i < what.Length && at + i < into.Length; i++) if (at + i >= 0) into[at + i] += gain * what[i];
        }

        private static void AddEcho(float[] b, float delaySeconds, float gain)
        {
            int d = (int)(delaySeconds * SampleRate);
            for (int i = b.Length - 1; i >= d; i--) b[i] += gain * b[i - d];
        }

        // ── voices ───────────────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ CE-2372 (AQ105 F; DESIGN_FPS_Audio §9) — a SHOUTED PHRASE as a placeholder: the words' rhythm (a syllable per vowel group, a
        /// burst per stop, a hiss per fricative, a pause per word and more at punctuation), voiced by a glottal pulse train at
        /// <paramref name="pitchHz"/> raised for a shout and falling at the end, through three formant resonators per vowel scaled by
        /// <paramref name="formant"/> (a voice per side). Not speech — a shout with the line's shape, so the operator tells "Moving!" from
        /// "Man down!" by its length and stress until a recording replaces it. Deterministic (seeded by the words and the voice).
        /// </summary>
        public static short[] Voice(string text, float pitchHz, float formant)
        {
            uint seed = 2166136261u;
            foreach (char c in text) seed = (seed ^ c) * 16777619u;
            var r = new Rng(seed ^ (uint)(pitchHz * 100f));
            var units = VoiceUnits(text);
            float total = 0.05f;
            foreach (var u in units) total += u.Seconds;
            var b = Buf(total + 0.25f);
            float phase = 0f, n1 = 0f;
            var res = new Resonator[3];
            int at = (int)(0.03f * SampleRate), vowelsDone = 0, vowels = 0;
            foreach (var u in units) if (u.Kind == 'V') vowels++;
            foreach (var u in units)
            {
                int len = (int)(u.Seconds * SampleRate);
                if (u.Kind == 'V' || u.Kind == 'N')
                {
                    var (f1, f2, f3) = u.Kind == 'N' ? (300f, 1200f, 2500f) : u.Formants;
                    res[0].Tune(f1 * formant * 1.1f, 90f); res[1].Tune(f2 * formant, 120f); res[2].Tune(f3 * formant, 170f);
                    float rise = vowels <= 1 ? 1f : 1f - 0.18f * vowelsDone / (vowels - 1);   // the phrase falls
                    for (int i = 0; i < len && at + i < b.Length; i++)
                    {
                        float k = i / (float)Math.Max(1, len);
                        float last = u.Kind == 'V' && vowelsDone == vowels - 1 ? 1f - 0.25f * k : 1f;   // the last vowel drops
                        float f0 = pitchHz * 1.55f * rise * last * (1f + 0.02f * Sin(5.5f, T(at + i)));
                        phase += f0 / SampleRate;
                        if (phase >= 1f) phase -= 1f;
                        float pulse = (1f - 2f * phase) + 0.15f * r.Next();   // a sawtooth glottis, a little breath
                        float env = MathF.Min(1f, k * 12f) * MathF.Min(1f, (1f - k) * 8f) * (u.Kind == 'N' ? 0.35f : u.Stress);
                        b[at + i] += env * (res[0].Step(pulse) + 0.6f * res[1].Step(pulse) + 0.3f * res[2].Step(pulse));
                    }
                    if (u.Kind == 'V') vowelsDone++;
                }
                else if (u.Kind == 'F' || u.Kind == 'P')
                {
                    float hp = u.Kind == 'F' ? 0.85f : 0.6f;
                    for (int i = 0; i < len && at + i < b.Length; i++)
                    {
                        float k = i / (float)Math.Max(1, len);
                        float w = r.Next();
                        n1 = n1 + hp * (w - n1);
                        float env = u.Kind == 'P' ? Env(T(i), 0.012f) : MathF.Sin(MathF.PI * k);
                        b[at + i] += env * (w - n1) * (u.Kind == 'P' ? 0.9f : 0.35f);
                    }
                }
                at += len;   // 'S' — silence
            }
            AddEcho(b, 0.06f, 0.18f);   // a street's slap-back
            return Finish(b);
        }

        private readonly record struct VoiceUnit(char Kind, float Seconds, (float, float, float) Formants = default, float Stress = 1f);

        /// <summary>The words as sound units: V a vowel, N a voiced consonant, F a fricative, P a stop's burst, S silence.</summary>
        private static List<VoiceUnit> VoiceUnits(string text)
        {
            var u = new List<VoiceUnit>();
            string s = text.ToLowerInvariant();
            bool firstVowelOfWord = true;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y')
                {
                    int j = i;
                    while (j + 1 < s.Length && s[j + 1] is 'a' or 'e' or 'i' or 'o' or 'u') j++;
                    if (c == 'e' && j == i && (i + 1 >= s.Length || !char.IsLetter(s[i + 1])) && !firstVowelOfWord) continue;   // a silent final e
                    u.Add(new VoiceUnit('V', firstVowelOfWord ? 0.17f : 0.12f, VowelFormants(c), firstVowelOfWord ? 1f : 0.75f));
                    firstVowelOfWord = false;
                    i = j;
                }
                else if (c is 's' or 'f' or 'h' or 'c' or 'x' or 'z' or 'j') u.Add(new VoiceUnit('F', c == 'h' ? 0.04f : 0.07f));
                else if (c is 'p' or 't' or 'k' or 'b' or 'd' or 'g' or 'q') { u.Add(new VoiceUnit('S', 0.025f)); u.Add(new VoiceUnit('P', 0.03f)); }
                else if (char.IsLetter(c)) u.Add(new VoiceUnit('N', 0.05f));
                else if (c == ' ') { u.Add(new VoiceUnit('S', 0.04f)); firstVowelOfWord = true; }
                else if (c is '!' or '.' or ',' or '?') { u.Add(new VoiceUnit('S', 0.16f)); firstVowelOfWord = true; }
            }
            return u;
        }

        private static (float, float, float) VowelFormants(char v) => v switch
        {
            'a' => (730f, 1090f, 2440f),
            'e' => (530f, 1840f, 2480f),
            'i' or 'y' => (390f, 1990f, 2550f),
            'o' => (570f, 840f, 2410f),
            _ => (440f, 1020f, 2240f),
        };

        /// <summary>A two-pole resonator (one formant).</summary>
        private struct Resonator
        {
            private float _a1, _a2, _g, _y1, _y2;
            public void Tune(float hz, float bandwidth)
            {
                float rr = MathF.Exp(-MathF.PI * bandwidth / SampleRate);
                _a1 = 2f * rr * MathF.Cos(2f * MathF.PI * hz / SampleRate);
                _a2 = -rr * rr;
                _g = 1f - rr;
            }
            public float Step(float x)
            {
                float y = _g * x + _a1 * _y1 + _a2 * _y2;
                _y2 = _y1; _y1 = y;
                return y;
            }
        }

        /// <summary>A one-shot: a short fade at the end, normalised.</summary>
        private static short[] Finish(float[] b)
        {
            int fade = Math.Min(b.Length, SampleRate / 50);
            for (int i = 0; i < fade; i++) b[b.Length - 1 - i] *= i / (float)fade;
            return Quantise(b);
        }

        /// <summary>A loop: the last <see cref="LoopFade"/> seconds cross-faded into the start, then cut to its period.</summary>
        private static short[] Loop(float[] b)
        {
            int w = (int)(LoopFade * SampleRate), n = b.Length - w;
            var o = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (i < w) { float a = i / (float)w; o[i] = b[i] * a + b[n + i] * (1f - a); }
                else o[i] = b[i];
            }
            return Quantise(o);
        }

        private static short[] Quantise(float[] b)
        {
            float max = 1e-6f;
            foreach (var v in b) max = MathF.Max(max, MathF.Abs(v));
            float k = Peak / max;
            var s = new short[b.Length];
            for (int i = 0; i < b.Length; i++) s[i] = (short)Math.Clamp((int)MathF.Round(b[i] * k * short.MaxValue), short.MinValue, short.MaxValue);
            return s;
        }

        /// <summary>The samples as a WAV file (RIFF, PCM16 mono) — what a loop stream reads.</summary>
        public static byte[] Wav(short[] samples, int sampleRate = SampleRate)
        {
            int data = samples.Length * 2;
            var w = new byte[44 + data];
            void S(int at, string s) { for (int i = 0; i < 4; i++) w[at + i] = (byte)s[i]; }
            void I(int at, int v) => BitConverter.TryWriteBytes(new Span<byte>(w, at, 4), v);
            void H(int at, short v) => BitConverter.TryWriteBytes(new Span<byte>(w, at, 2), v);
            S(0, "RIFF"); I(4, 36 + data); S(8, "WAVE"); S(12, "fmt "); I(16, 16); H(20, 1); H(22, 1);
            I(24, sampleRate); I(28, sampleRate * 2); H(32, 2); H(34, 16); S(36, "data"); I(40, data);
            Buffer.BlockCopy(samples, 0, w, 44, data);
            return w;
        }
    }
}
